import postgres from 'postgres'

const sql = postgres(Deno.env.get('SUPABASE_DB_URL')!, { prepare: false, max: 1 })

const corsHeaders = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Headers': 'authorization, content-type',
  'Access-Control-Allow-Methods': 'GET,POST,OPTIONS',
  'Cache-Control': 'no-store',
  'X-Content-Type-Options': 'nosniff',
}

class HttpError extends Error {
  status: number
  code: string
  constructor(status: number, message: string, code = 'REQUEST_FAILED') {
    super(message)
    this.status = status
    this.code = code
  }
}

const enc = new TextEncoder()

function json(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { ...corsHeaders, 'content-type': 'application/json; charset=utf-8' },
  })
}

function randomBytes(length: number) {
  const out = new Uint8Array(length)
  crypto.getRandomValues(out)
  return out
}

function b64(bytes: Uint8Array) {
  let s = ''
  for (const b of bytes) s += String.fromCharCode(b)
  return btoa(s)
}

function b64url(bytes: Uint8Array) {
  return b64(bytes).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function normalizeLogin(value: unknown) {
  const login = String(value ?? '').trim()
  if (!/^[A-Za-z0-9._-]{3,64}$/.test(login)) {
    throw new HttpError(400, 'Invalid account name.', 'INVALID_LOGIN_NAME')
  }
  return login
}

function bearer(req: Request) {
  const header = req.headers.get('authorization') ?? ''
  return header.startsWith('Bearer ') ? header.slice(7).trim() : ''
}

function clientIp(req: Request) {
  return req.headers.get('x-forwarded-for')?.split(',')[0]?.trim()
    || req.headers.get('cf-connecting-ip')
    || null
}

function parseBody(req: Request) {
  return req.json().catch(() => ({})) as Promise<Record<string, any>>
}

async function sha256Hex(value: string) {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', enc.encode(value)))
  return [...digest].map(b => b.toString(16).padStart(2, '0')).join('')
}

async function verifyPassword(password: string, stored: string) {
  const rows = await sql`select extensions.crypt(${password}, ${stored}) = ${stored} as ok`
  return Boolean(rows[0]?.ok)
}

async function audit(actor: string | null, action: string, metadata: any, ip: string | null) {
  try {
    await sql`
      insert into voicecraft.audit_logs(actor_account_id,target_account_id,action,metadata,ip)
      values(${actor},${actor},${action},${sql.json(metadata ?? {})},${ip})
    `
  } catch (e) {
    console.error('audit failed', e)
  }
}

function backupRelaysAllowed(auth: any) {
  return Boolean(auth?.is_admin) || Number(auth?.role_rank ?? 0) >= 10
}

async function userSessionFor(req: Request) {
  const token = bearer(req)
  if (!token) throw new HttpError(401, 'Authentication required.', 'AUTH_REQUIRED')

  const tokenHash = await sha256Hex(token)

  const userRows = await sql`
    select s.id session_id,s.account_id,a.login_name,s.device_id,s.expires_at,a.token_version,
           r.rank role_rank,r.is_admin,'user'::text as mobile_session_source
    from voicecraft.sessions s
    join voicecraft.accounts a on a.id=s.account_id
    join voicecraft.roles r on r.code=a.role_code
    left join voicecraft.devices d on d.id=s.device_id
    where s.token_hash=${tokenHash}
      and s.revoked_at is null
      and s.expires_at>now()
      and s.token_version=a.token_version
      and s.kind='user'
      and r.is_admin=false
      and a.status='active'
      and (a.expires_at is null or a.expires_at>now())
      and (d.id is null or d.revoked_at is null)
    limit 1
  `

  let auth = userRows[0]
  if (auth) {
    await sql`update voicecraft.sessions set last_seen_at=now() where id=${auth.session_id}`
    return { ...auth, tokenHash }
  }

  const mobileRows = await sql`
    select ms.id session_id,ms.account_id,a.login_name,ms.device_id,ms.expires_at,a.token_version,
           r.rank role_rank,r.is_admin,'isolated'::text as mobile_session_source
    from voicecraft.mobile_sessions ms
    join voicecraft.accounts a on a.id=ms.account_id
    join voicecraft.roles r on r.code=a.role_code
    left join voicecraft.devices d on d.id=ms.device_id
    where ms.token_hash=${tokenHash}
      and ms.revoked_at is null
      and ms.expires_at>now()
      and ms.token_version=a.token_version
      and r.is_admin=true
      and a.status='active'
      and (a.expires_at is null or a.expires_at>now())
      and (d.id is null or d.revoked_at is null)
    limit 1
  `

  auth = mobileRows[0]
  if (!auth) throw new HttpError(401, 'Session is invalid or expired.', 'SESSION_INVALID')

  await sql`update voicecraft.mobile_sessions set last_seen_at=now() where id=${auth.session_id}`
  return { ...auth, tokenHash }
}

async function route(req: Request) {
  if (req.method === 'OPTIONS') return new Response(null, { status: 204, headers: corsHeaders })

  const url = new URL(req.url)
  const path = url.pathname.replace(/^\/voicecraft-mobile/, '') || '/'

  if (req.method === 'GET' && (path === '/' || path === '/health')) {
    return json({
      ok: true,
      service: 'VoiceCraft Mobile Account API',
      version: '1.1.0',
      auth: 'mobile-only',
    })
  }

  if (req.method === 'POST' && path === '/v1/login') {
    const body = await parseBody(req)
    const loginName = normalizeLogin(body.loginName)
    const password = String(body.password ?? '')

    const rows = await sql`
      select a.id,a.login_name,a.status,a.expires_at,a.token_version,
             c.password_hash,c.failed_attempts,c.locked_until,
             r.rank role_rank,r.is_admin
      from voicecraft.accounts a
      join voicecraft.account_credentials c on c.account_id=a.id
      join voicecraft.roles r on r.code=a.role_code
      where lower(a.login_name)=lower(${loginName})
      limit 1
    `

    const account = rows[0]
    if (!account) throw new HttpError(401, 'Invalid account or password.', 'INVALID_CREDENTIALS')
    if (account.status !== 'active') throw new HttpError(403, 'This account is not active.', 'ACCOUNT_DISABLED')
    if (account.expires_at && new Date(account.expires_at) <= new Date()) {
      throw new HttpError(403, 'This account has expired.', 'ACCOUNT_EXPIRED')
    }
    if (account.locked_until && new Date(account.locked_until) > new Date()) {
      throw new HttpError(423, 'This account is temporarily locked.', 'ACCOUNT_LOCKED')
    }

    if (!(await verifyPassword(password, account.password_hash))) {
      await sql`
        update voicecraft.account_credentials
        set failed_attempts=failed_attempts+1,
            locked_until=case
              when failed_attempts+1>=5 then now()+interval '15 minutes'
              else locked_until
            end
        where account_id=${account.id}
      `
      throw new HttpError(401, 'Invalid account or password.', 'INVALID_CREDENTIALS')
    }

    await sql`
      update voicecraft.account_credentials
      set failed_attempts=0,locked_until=null
      where account_id=${account.id}
    `

    let deviceId: string | null = null
    const installationId = String(body.installationId ?? '').trim()
    if (installationId) {
      const d = await sql`
        insert into voicecraft.devices(
          account_id,installation_id,device_name,device_model,
          platform,app_version,last_seen_at,revoked_at
        )
        values(
          ${account.id},${installationId},${String(body.deviceName ?? '') || null},
          ${String(body.deviceModel ?? '') || null},${String(body.platform ?? 'android')},
          ${String(body.appVersion ?? '') || null},now(),null
        )
        on conflict(account_id,installation_id)
        do update set
          device_name=excluded.device_name,
          device_model=excluded.device_model,
          platform=excluded.platform,
          app_version=excluded.app_version,
          last_seen_at=now(),
          revoked_at=null
        returning id
      `
      deviceId = d[0]?.id ?? null
    }

    const token = b64url(randomBytes(32))
    const tokenHash = await sha256Hex(token)
    const ttlMs = account.is_admin
      ? 8 * 60 * 60 * 1000
      : 30 * 24 * 60 * 60 * 1000
    const expiresAt = new Date(Date.now() + ttlMs).toISOString()

    if (account.is_admin) {
      await sql`
        insert into voicecraft.mobile_sessions(
          account_id,device_id,token_hash,token_version,expires_at,ip,user_agent
        )
        values(
          ${account.id},${deviceId},${tokenHash},${account.token_version},
          ${expiresAt},${clientIp(req)},${req.headers.get('user-agent')}
        )
      `
    } else {
      await sql`
        insert into voicecraft.sessions(
          account_id,device_id,kind,token_hash,token_version,expires_at,ip,user_agent
        )
        values(
          ${account.id},${deviceId},'user',${tokenHash},${account.token_version},
          ${expiresAt},${clientIp(req)},${req.headers.get('user-agent')}
        )
      `
    }

    await audit(account.id, 'mobile_login_success', {
      deviceId,
      isolatedSession: Boolean(account.is_admin),
    }, clientIp(req))

    return json({
      token,
      expiresAt,
      account: {
        id: account.id,
        loginName: account.login_name,
      },
      capabilities: {
        backupRelays: backupRelaysAllowed(account),
      },
    })
  }

  if (req.method === 'GET' && path === '/v1/entitlements') {
    const auth = await userSessionFor(req)
    return json({
      capabilities: {
        backupRelays: backupRelaysAllowed(auth),
      },
    })
  }

  if (req.method === 'POST' && path === '/v1/logout') {
    const auth = await userSessionFor(req)
    if (auth.mobile_session_source === 'isolated') {
      await sql`
        update voicecraft.mobile_sessions
        set revoked_at=coalesce(revoked_at,now())
        where id=${auth.session_id}
      `
    } else {
      await sql`
        update voicecraft.sessions
        set revoked_at=coalesce(revoked_at,now())
        where id=${auth.session_id}
      `
    }
    await audit(auth.account_id, 'mobile_logout', {}, clientIp(req))
    return json({ success: true })
  }

  throw new HttpError(404, 'Not found.', 'NOT_FOUND')
}

Deno.serve(async req => {
  try {
    return await route(req)
  } catch (error) {
    const e = error as any
    const status = e instanceof HttpError ? e.status : 500
    if (status >= 500) console.error(error)
    return json({
      error: status >= 500 ? 'Internal server error.' : e.message,
      code: e.code ?? (status >= 500 ? 'INTERNAL_ERROR' : 'REQUEST_FAILED'),
    }, status)
  }
})
