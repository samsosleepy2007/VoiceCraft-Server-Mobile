# Multi-Relay Auto Failover

Target bundle:

- Android: `1.7.1-android-phase2-ui4.4` (version code `10`)
- Endstone: `0.2.6`
- Render Relay: `0.2.1`
- Render Relay protocol: `1`
- VoiceCraft upstream: `v1.7.1` unchanged

## Goal

Render is only the Minecraft state/binding/control plane. If the active Render service becomes unavailable, Android and Endstone automatically move through the configured relay list without restarting the VoiceCraft UDP runtime.

Voice audio remains on the normal VoiceCraft/LiteNetLib UDP path.

## Relay model

One Primary relay is required. Backup relays are optional and may be added in any quantity.

```text
Primary (required)
  -> Backup #1 (optional)
  -> Backup #2 (optional)
  -> Backup #3 (optional)
  -> ...
  -> Primary
```

All relay deployments use the same:

```text
Server ID
Bridge Secret / BRIDGE_SECRET
Protocol 1
```

The intended quota-redundancy deployment is to place Primary and backups on independent Render accounts while keeping the shared secret and Server ID identical.

## Android setup

The normal Bridge page owns the Primary Render Service URL, Server ID and Bridge Secret.

Use **Manage Backup Relays** to configure optional backups.

For each backup:

1. Tap **Add Backup Relay**.
2. Paste the Render Service URL, for example `https://voicecraft-backup.onrender.com`.
3. Add more rows if needed.
4. Remove unwanted rows.
5. Save the Backup Relay list.

Zero backups is valid. The server can still start with Primary only.

## Endstone config

Use the Android-generated plugin config after Primary and backups are configured.

```toml
[tracking]
interval_ticks = 2
position_epsilon = 0.05
rotation_epsilon = 1.0
log_position_changes = false
heartbeat_seconds = 30

[binding]
min_key_length = 4
max_key_length = 128

[bridge]
enabled = true
url = "wss://voicecraft-primary.onrender.com/bridge"
backup_urls = [
  "wss://voicecraft-backup1.onrender.com/bridge",
  "wss://voicecraft-backup2.onrender.com/bridge"
]
server_id = "mcsv-main"
secret = "THE_SAME_SECRET_USED_BY_EVERY_RENDER_RELAY"
reconnect_seconds = 5
max_attempts = 5
peer_timeout_seconds = 30
```

Invalid optional backups are ignored. A valid Primary remains sufficient to enable the bridge.

## Failover behavior

For the currently selected relay:

```text
attempt 1
  wait 5s
attempt 2
  wait 5s
attempt 3
  wait 5s
attempt 4
  wait 5s
attempt 5
  -> advance to next relay
```

When the final backup exhausts its attempts, selection wraps back to Primary.

With Primary only, exhausting five attempts does not stop the VoiceCraft runtime. Another Primary retry cycle begins.

## Peer health / split-brain protection

A successful WebSocket handshake alone is not fully healthy.

Android must see the Endstone peer and Endstone must see the Android peer. If the opposite peer is not visible within the configured peer timeout, that relay participates in the same retry/failover sequence.

This protects against states such as:

```text
Android  -> Backup #1
Endstone -> Primary
```

## In-game outage alerts

Endstone 0.2.6 drains relay status transitions on the game thread and broadcasts Bedrock-formatted chat messages.

```text
Red     relay/voice control plane unavailable
Red     Primary exhausted and no Backup Relay exists
Yellow  switching to Backup #N or wrapping to Primary
Green   active relay paired successfully
```

The outage state is session-based. Reconnect attempts every 5 seconds do not repeat the initial outage warning every cycle.

## What survives a failover

The VoiceCraft runtime is started independently from the relay reconnect loop.

Expected behavior during `Primary -> Backup`:

```text
VoiceCraft UDP server       stays running
VoiceCraft voice clients    stay connected
VoiceCraft entities         remain in memory
Completed bindings          remain in memory
WebSocket control plane     reconnects to next relay
Minecraft state updates     pause until relay/peer recovery
```

After Android and Endstone meet on the active relay, snapshot/state synchronization restores current Minecraft positions and bridge state.

## Manual `/vc` Unbind and failover safety

Endstone 0.2.6 can disconnect a bound player's actual VoiceCraft client from `/vc`.

```text
/vc
  -> Disconnect / Unbind Microphone
  -> confirmation
  -> protocol-1 unbind(requestId, entityId)
  -> Android validates current entity
  -> VoiceCraft peer disconnected
  -> unbind_result
```

Important reliability rules:

- `unbind` is destructive and is **never cached/replayed by Render Relay**.
- Android rejects stale `entityId` requests.
- Manual unbind suppresses the normal unexpected-disconnect Auto Rebind path.
- Endstone waits up to 20 seconds for confirmation before releasing the `Disconnecting` UI state.
- A timed-out request is not assumed to have failed; a bounded tombstone allows a late result to reconcile local state.
- A late result belonging to an older entity can never clear a newer binding.

Unexpected VoiceCraft client disconnects still schedule the standard 5-second Auto Rebind form.

## Recommended Render deployment

```text
Render Account A
  Primary: https://voicecraft-main.onrender.com

Render Account B
  Backup #1: https://voicecraft-backup1.onrender.com

Render Account C
  Backup #2: https://voicecraft-backup2.onrender.com
```

Each service uses:

```text
Root Directory: VoiceCraft.Bridge.Relay
Runtime: Node
Build Command: npm install --omit=dev
Start Command: npm start
Health Check Path: /health
BRIDGE_SECRET: same shared secret on every relay
```

## Runtime test checklist

1. Start Android and Endstone with Primary + at least one Backup.
2. Confirm both pair on Primary and normal state/binding works.
3. Bind a VoiceCraft client from `/vc`.
4. Stop or suspend Primary.
5. Confirm one red outage message appears instead of one every retry.
6. Confirm logs count failures up to `5/5`.
7. Confirm a yellow message reports movement to Backup #1.
8. Confirm Android and Endstone both pair on Backup #1.
9. Confirm the green Backup #1 success message appears.
10. Confirm the VoiceCraft client did not reconnect and the UDP runtime did not restart.
11. Confirm Minecraft state resumes after peer/snapshot recovery.
12. Stop Backup #1 and verify ordered movement to the next backup or Primary wrap-around.
13. Repeat with Primary only and verify the explicit no-backup warning plus continued Primary retry cycles.
14. Bind a VoiceCraft client, use `/vc` → Disconnect, and verify the actual VoiceCraft client is disconnected.
15. Confirm manual disconnect does not open Auto Rebind.
16. Rebind intentionally from `/vc` and confirm a stale old unbind result cannot clear the new binding.

## CI contracts

Automated checks cover:

- Android UI4.4 version/source guards and ARM64 publish;
- Endstone 0.11.10 import/metadata compatibility;
- ordered/circular failover and Primary-only behavior;
- outage alert anti-spam transitions;
- real Endstone ↔ Relay protocol-1 `unbind` / `unbind_result` forwarding;
- relay authentication and Server-ID room isolation;
- relay restart/reconnect;
- destructive unbind forwarding with no replay after Android reconnect;
- wheel contents/version/entrypoint.

## Protocol compatibility

This feature does **not** change:

- VoiceCraft v1.7.1 network/audio packets;
- LiteNetLib UDP delivery behavior;
- Render Relay protocol version `1`;
- automatic join-time Bind flow;
- automatic 5-second Rebind after an unexpected VoiceCraft disconnect.
