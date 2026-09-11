import postgres from 'postgres'

const sql = postgres(Deno.env.get('SUPABASE_DB_URL')!, { prepare: false, max: 1 })
const enc = new TextEncoder()
const dec = new TextDecoder()

const cors = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Headers': 'content-type, authorization',
  'Access-Control-Allow-Methods': 'GET,POST,OPTIONS',
  'Cache-Control': 'no-store',
  'X-Content-Type-Options': 'nosniff',
}

class HttpError extends Error {
  status: number
  code: string
  constructor(status: number, message: string, code: string) {
    super(message)
    this.status = status
    this.code = code
  }
}

function json(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { ...cors, 'content-type': 'application/json; charset=utf-8' },
  })
}

function b64url(bytes: Uint8Array) {
  let s = ''
  for (const b of bytes) s += String.fromCharCode(b)
  return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function unb64url(value: string) {
  const padded = value.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - value.length % 4) % 4)
  const s = atob(padded)
  return Uint8Array.from(s, c => c.charCodeAt(0))
}

function randomId(bytes = 16) {
  const out = new Uint8Array(bytes)
  crypto.getRandomValues(out)
  return b64url(out)
}

function safeEqual(a: Uint8Array, b: Uint8Array) {
  if (a.length !== b.length) return false
  let diff = 0
  for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i]
  return diff === 0
}

async function guestSigningKey() {
  const rows = await sql`
    select decrypted_secret
    from vault.decrypted_secrets
    where name = 'voicecraft_guest_signing_key_v1'
    limit 1`
  const secret = String(rows[0]?.decrypted_secret ?? '')
  if (!secret) throw new Error('Guest signing key is unavailable')
  const raw = Uint8Array.from(atob(secret), c => c.charCodeAt(0))
  if (raw.length < 32) throw new Error('Guest signing key is invalid')
  return crypto.subtle.importKey(
    'raw',
    raw,
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign', 'verify'])
}

async function signToken(payload: Record<string, unknown>) {
  const header = b64url(enc.encode(JSON.stringify({
    alg: 'HS256',
    typ: 'JWT',
    kid: 'voicecraft-guest-v1',
  })))
  const body = b64url(enc.encode(JSON.stringify(payload)))
  const input = `${header}.${body}`
  const key = await guestSigningKey()
  const sig = new Uint8Array(
    await crypto.subtle.sign('HMAC', key, enc.encode(input)))
  return `${input}.${b64url(sig)}`
}

async function verifyToken(token: string) {
  const parts = token.split('.')
  if (parts.length !== 3)
    throw new HttpError(401, 'Invalid guest token.', 'GUEST_TOKEN_INVALID')

  const [header, body, signature] = parts
  let payload: any
  try {
    payload = JSON.parse(dec.decode(unb64url(body)))
  } catch {
    throw new HttpError(401, 'Invalid guest token.', 'GUEST_TOKEN_INVALID')
  }

  const key = await guestSigningKey()
  const expected = new Uint8Array(
    await crypto.subtle.sign('HMAC', key, enc.encode(`${header}.${body}`)))

  let actual: Uint8Array
  try {
    actual = unb64url(signature)
  } catch {
    throw new HttpError(401, 'Invalid guest token.', 'GUEST_TOKEN_INVALID')
  }

  if (!safeEqual(expected, actual))
    throw new HttpError(401, 'Invalid guest token.', 'GUEST_TOKEN_INVALID')

  const now = Math.floor(Date.now() / 1000)
  if (payload?.iss !== 'voicecraft'
      || payload?.kind !== 'guest'
      || payload?.tier !== 'GUEST'
      || payload?.account !== false)
    throw new HttpError(401, 'Invalid guest token.', 'GUEST_TOKEN_INVALID')

  if (!Number.isFinite(payload?.exp) || payload.exp <= now)
    throw new HttpError(401, 'Guest token expired.', 'GUEST_TOKEN_EXPIRED')

  return payload
}

function normalizeGuestId(value: unknown) {
  const id = String(value ?? '').trim().toUpperCase()
  if (!/^GUEST-[A-Z0-9-]{8,58}$/.test(id) || id.length > 64)
    throw new HttpError(400, 'Invalid local Guest ID.', 'INVALID_GUEST_ID')
  return id
}

async function parseBody(req: Request) {
  try { return await req.json() as Record<string, any> }
  catch { return {} }
}

Deno.serve(async (req: Request) => {
  try {
    if (req.method === 'OPTIONS')
      return new Response(null, { status: 204, headers: cors })

    const url = new URL(req.url)
    const path = url.pathname.replace(/^\/voicecraft-guest/, '') || '/'

    if (req.method === 'GET' && (path === '/' || path === '/health')) {
      return json({
        ok: true,
        service: 'VoiceCraft Guest Session',
        version: '1.0.0',
        persistence: 'local-only',
        databaseAccount: false,
      })
    }

    if (req.method === 'POST' && path === '/v1/session') {
      const body = await parseBody(req)
      const guestId = normalizeGuestId(body.guestId)
      const now = Math.floor(Date.now() / 1000)
      const ttl = 24 * 60 * 60

      const payload = {
        iss: 'voicecraft',
        sub: `guest:${guestId}`,
        kind: 'guest',
        tier: 'GUEST',
        role: 'GUEST',
        account: false,
        guestId,
        installationId: String(body.installationId ?? '').slice(0, 128) || undefined,
        appVersion: String(body.appVersion ?? '').slice(0, 64) || undefined,
        iat: now,
        exp: now + ttl,
        jti: randomId(16),
      }

      const token = await signToken(payload)
      return json({
        token,
        expiresAt: new Date((now + ttl) * 1000).toISOString(),
        profile: {
          guestId,
          tier: 'GUEST',
          account: false,
          persistence: 'LOCAL_ONLY',
        },
        entitlements: {
          cloudSync: false,
          accountRecovery: false,
          admin: false,
        },
      }, 201)
    }

    if (req.method === 'POST' && path === '/v1/verify') {
      const header = req.headers.get('authorization') ?? ''
      const token = header.startsWith('Bearer ')
        ? header.slice(7).trim()
        : ''
      if (!token)
        throw new HttpError(401, 'Guest token required.', 'GUEST_TOKEN_REQUIRED')

      const payload = await verifyToken(token)
      return json({
        valid: true,
        profile: {
          guestId: payload.guestId,
          tier: 'GUEST',
          account: false,
        },
        expiresAt: new Date(payload.exp * 1000).toISOString(),
      })
    }

    throw new HttpError(404, 'Not found.', 'NOT_FOUND')
  } catch (error) {
    const e = error as any
    const status = e instanceof HttpError ? e.status : 500
    if (status >= 500) console.error(error)
    return json({
      error: status >= 500 ? 'Internal server error.' : e.message,
      code: e.code ?? 'INTERNAL_ERROR',
    }, status)
  }
})
