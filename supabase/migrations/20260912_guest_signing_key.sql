do $$
begin
  if not exists (
    select 1
    from vault.decrypted_secrets
    where name = 'voicecraft_guest_signing_key_v1'
  ) then
    perform vault.create_secret(
      encode(gen_random_bytes(48), 'base64'),
      'voicecraft_guest_signing_key_v1',
      'HMAC-SHA256 signing key for stateless VoiceCraft Guest tokens'
    );
  end if;
end $$;
