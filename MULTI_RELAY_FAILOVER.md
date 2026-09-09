# Multi-Relay Auto Failover

Target bundle:

- Android: `1.7.1-android-phase2-ui4.3` (version code `9`)
- Endstone: `0.2.5`
- Render Relay protocol: `1`
- VoiceCraft upstream: `v1.7.1` unchanged

## Goal

The Render relay is only the Minecraft state/binding/control plane. If the active Render service becomes unavailable (including a Free-plan quota suspension), Android and Endstone can automatically move to another relay without restarting the VoiceCraft UDP server.

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

All relay deployments must use the same:

```text
Server ID
Bridge Secret / BRIDGE_SECRET
Protocol 1
```

The intended deployment is to place Primary and backups on independent Render accounts so one account's Free quota does not disable every relay at once.

## Android setup

The normal Bridge page still owns the Primary Render Service URL, Server ID and Bridge Secret.

Use **+ Manage Backup Relays** to open the optional Backup Relay Manager.

For each backup:

1. Tap **+ Add Backup Relay**.
2. Paste the backup Render Service URL, for example `https://voicecraft-backup.onrender.com`.
3. Add more rows if needed.
4. Remove any relay with **Remove**.
5. Tap **Save Backups**.

Zero backups is valid. The server can still start with Primary only.

Android stores backup URLs separately from Primary and converts each accepted service URL to the required `/bridge` WebSocket endpoint.

## Endstone config

Use **Copy Plugin Config** in the Android app after configuring Primary and backups.

Example:

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

Invalid optional backup entries are ignored by Endstone. A valid Primary remains sufficient to enable the bridge.

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

With Primary only, exhausting five attempts does not stop the server. It starts another Primary retry cycle.

## Peer health

A successful WebSocket handshake alone is not considered fully healthy.

Android must see the Endstone peer and Endstone must see the Android peer. If the opposite peer is not visible within the configured peer timeout, the connection is treated as unhealthy and participates in the same retry/failover sequence.

This protects against a split state such as:

```text
Android  -> Backup #1
Endstone -> Primary
```

## What survives a failover

The VoiceCraft runtime is started independently from the relay reconnect loop. Relay failover does not recreate it.

Expected behavior during `Primary -> Backup`:

```text
VoiceCraft UDP server       stays running
VoiceCraft voice clients    stay connected
VoiceCraft entities         remain in memory
Completed bindings          remain in memory
WebSocket control plane     reconnects to next relay
Minecraft state updates     pause briefly until relay/peer recovery
```

After Android and Endstone meet on the active relay, the normal snapshot/state synchronization restores current Minecraft positions and bridge state.

## Recommended Render deployment

For quota redundancy, deploy each relay under an independent Render account while using the same relay source and the same `BRIDGE_SECRET`.

Example:

```text
Render Account A
  Primary: https://voicecraft-main.onrender.com

Render Account B
  Backup #1: https://voicecraft-backup1.onrender.com

Render Account C
  Backup #2: https://voicecraft-backup2.onrender.com
```

Each service uses the existing relay deployment settings:

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
2. Confirm both report Primary and normal Minecraft state/binding works.
3. Bind a VoiceCraft client with `/vc`.
4. Stop or suspend Primary.
5. Confirm logs count Primary failures up to `5/5`.
6. Confirm both sides move to `Backup #1`.
7. Confirm the VoiceCraft client did not reconnect and the UDP runtime did not restart.
8. Confirm Minecraft state resumes after peer/snapshot recovery.
9. Stop Backup #1 and verify the same ordered behavior toward the next backup or Primary wrap-around.
10. Repeat with no backup configured and confirm Primary-only retry remains functional.

## Protocol compatibility

This feature changes relay selection only.

It does **not** change:

- VoiceCraft v1.7.1 network/audio packets
- LiteNetLib UDP delivery behavior
- Render Relay protocol version 1
- `/vc` binding UI semantics
- automatic join-time Bind form
- automatic 5-second Rebind form after a bound VoiceCraft client disconnects
