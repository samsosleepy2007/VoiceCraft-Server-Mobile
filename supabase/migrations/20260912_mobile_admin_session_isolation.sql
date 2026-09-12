create table if not exists voicecraft.mobile_sessions (
  id uuid primary key default gen_random_uuid(),
  account_id uuid not null references voicecraft.accounts(id) on delete cascade,
  device_id uuid null references voicecraft.devices(id) on delete set null,
  token_hash text not null unique,
  token_version integer not null,
  created_at timestamptz not null default now(),
  last_seen_at timestamptz not null default now(),
  expires_at timestamptz not null,
  revoked_at timestamptz null,
  ip inet null,
  user_agent text null
);

create index if not exists mobile_sessions_account_id_idx
  on voicecraft.mobile_sessions(account_id);

alter table voicecraft.mobile_sessions enable row level security;
revoke all on voicecraft.mobile_sessions from anon, authenticated;
