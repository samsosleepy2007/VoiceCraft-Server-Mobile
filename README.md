# VoiceCraft Server Mobile

Android/mobile host for **VoiceCraft v1.7.1** with an Endstone + Render control-plane bridge for hosted Minecraft Bedrock servers.

Current development bundle:

- Android app: **`1.7.1-android-phase2-ui4.4`** (version code `10`)
- Endstone plugin: **`0.2.6`**
- Render Relay: **`0.2.1`**
- Render Relay protocol: **`1`**
- VoiceCraft upstream: **v1.7.1**, pinned to commit `85aaccccbb58adb23e8c87144e8b1c24bf4b2011`

> VoiceCraft v1.7.1 wire/audio protocol remains unchanged. Render carries control/state/binding traffic only; voice audio still uses VoiceCraft/LiteNetLib UDP directly to Android.

## Architecture

```text
Minecraft Bedrock @ MCSV
        │
        ▼
Endstone VoiceCraft 0.2.6
        │ outbound WSS
        ▼
Primary Render Relay ──────┐
        │                  │ automatic failover
        │                  ▼
        │            Backup Relay(s)
        │ outbound WSS     │
        └──────────────────┘
                │
                ▼
VoiceCraft Server Mobile UI4.4
        │
        ├─ VoiceCraft v1.7.1 runtime
        ├─ LiteNetLib UDP voice server :9050
        ├─ McHttp TCP compatibility :9050
        ├─ Minecraft player/entity bridge
        ├─ Bind / Rebind / real Unbind
        └─ relay/binding dashboard
```

## UI4.4 / Endstone 0.2.6 highlights

### Multi-relay automatic failover

- Primary Relay is required.
- Backup Relays are optional and may be added in any quantity.
- Android and Endstone use the same ordered relay list.
- The active relay is retried up to **5 times** with the normal 5-second retry cadence.
- After 5 failures, selection advances to the next Backup Relay.
- After the final backup, selection wraps back to Primary.
- Relay health requires both the WebSocket connection and the opposite peer to be visible.
- VoiceCraft UDP runtime is not restarted during failover.
- Existing VoiceCraft clients, entities and completed bindings remain in Android memory while the WSS control plane reconnects.

Recommended deployment for quota/failure isolation:

```text
Render Account A
└─ Primary Relay

Render Account B
└─ Backup Relay #1

Render Account C (optional)
└─ Backup Relay #2
```

All relay deployments use the same:

```text
server_id
BRIDGE_SECRET
protocol = 1
```

See [`MULTI_RELAY_FAILOVER.md`](MULTI_RELAY_FAILOVER.md) for the full behavior and test checklist.

### `/vc` control menu

Endstone exposes one public player command:

```text
/vc
```

The menu changes with the player's binding state:

```text
Not bound   → Bind Microphone
Pending     → Cancel Pending Bind
Bound       → Disconnect / Unbind Microphone
```

Status and operator tracked-player views remain available from the same menu.

### Real VoiceCraft Unbind

`Disconnect / Unbind Microphone` is no longer only a local-state reset.

Flow:

```text
Player confirms Disconnect
        │
        ▼
Endstone sends protocol-1 unbind
(requestId + current entityId)
        │
        ▼
Render Relay forwards only
(no cache / no replay)
        │
        ▼
Android validates current binding
        │
        ▼
VoiceCraft server disconnects NetPeer
        │
        ▼
Android sends unbind_result
        │
        ▼
Endstone clears binding state
and suppresses Auto Rebind
```

Safety behavior:

- stale `entityId` requests are rejected by Android;
- destructive `unbind` messages are never cached/replayed by the Render Relay;
- manual unbind suppresses the normal unexpected-disconnect Auto Rebind event;
- a 20-second confirmation timeout prevents `/vc` from remaining stuck in `Disconnecting`;
- timed-out request tombstones allow a late success to reconcile safely;
- a late result for an old entity cannot clear a newer binding.

Unexpected VoiceCraft client disconnects still use the normal **5-second Auto Rebind** flow.

### In-game Relay alerts

Endstone posts relay state changes to players using Bedrock `§` formatting, without emoji.

Examples of state classes:

```text
Red     relay/voice control plane temporarily unavailable
Red     Primary exhausted and no Backup Relay is configured
Yellow  moving to Backup #N / wrapping to Primary
Green   Backup #N paired successfully / Primary recovered
```

Alerts are session-based, so the 5-second retry loop does not send the same outage warning every attempt.

## Required setup

### 1. Deploy the Primary Render Relay

Create a Render Web Service from this repository:

```text
Root Directory: VoiceCraft.Bridge.Relay
Runtime: Node
Build Command: npm install --omit=dev
Start Command: npm start
Health Check Path: /health
```

Environment variable:

```text
BRIDGE_SECRET=<strong-random-secret-at-least-16-characters>
```

A normal service URL such as:

```text
https://voicecraft-main.onrender.com
```

is converted by Android to:

```text
wss://voicecraft-main.onrender.com/bridge
```

### 2. Deploy optional Backup Relays

Deploy the same `VoiceCraft.Bridge.Relay` source on each backup service and use the **same `BRIDGE_SECRET`**.

Example:

```text
Primary   https://voicecraft-main.onrender.com
Backup #1 https://voicecraft-backup1.onrender.com
Backup #2 https://voicecraft-backup2.onrender.com
```

### 3. Configure Android

In the Android app configure:

```text
Primary Render Service URL
Server ID      (default: mcsv-main)
Bridge Secret  (same value as every Render BRIDGE_SECRET)
```

Use **Manage Backup Relays** to add optional backup URLs.

Primary-only configuration is valid. The Android VoiceCraft server will not start until the required Primary bridge settings pass validation.

### 4. Install Endstone 0.2.6

Use Endstone `0.11.x` on the Minecraft Bedrock host and install:

```text
endstone_voicecraft-0.2.6-py3-none-any.whl
```

Remove older `endstone_voicecraft-*.whl` files before installing the new wheel.

Generate the Endstone config from Android after Primary and backups are configured.

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
url = "wss://voicecraft-main.onrender.com/bridge"
backup_urls = [
  "wss://voicecraft-backup1.onrender.com/bridge"
]
server_id = "mcsv-main"
secret = "THE_SAME_SECRET_USED_BY_ANDROID_AND_EVERY_RELAY"
reconnect_seconds = 5
max_attempts = 5
peer_timeout_seconds = 30
```

### 5. Start VoiceCraft Server Mobile

Tap **Start Server** in Android.

The current Android service starts the real VoiceCraft v1.7.1 runtime and keeps relay reconnect/failover isolated inside the bridge controller.

Default voice/McHttp port:

```text
9050
```

### 6. Connect a VoiceCraft client

For LAN testing:

```text
<ANDROID_LAN_IP>:9050
```

Set positioning mode to **Server**.

When the client receives its one-use Binding Key, join Minecraft and use the automatic Bind form or run:

```text
/vc
```

## Relay protocol additions

Protocol number remains **1**. UI4.4 adds compatible control message types rather than a new protocol version.

Endstone → Android:

```text
player_state
player_leave
bind
unbind
heartbeat
sync_begin / sync_end
```

Android → Endstone:

```text
bind_result
unbind_result
request_snapshot
server_status
voice_client_disconnected
```

The relay never transports VoiceCraft audio.

## Security notes

- Use `wss://` for deployed Render connections.
- Do not hardcode the shared Bridge Secret in a public repository.
- Use the same Bridge Secret on Primary and backups for seamless failover.
- If any relay is compromised, rotate the secret consistently on every relay, Android and Endstone.
- Binding Keys and the Bridge Secret are intentionally hidden from normal runtime logs.

## CI / verification

The repository validates:

- pinned VoiceCraft upstream commit;
- Android UI4.4 version/source guards;
- full Android ARM64 `dotnet publish`;
- Endstone 0.11.10 API compatibility;
- `/vc` metadata ownership and one-command registration;
- ordered/circular multi-relay behavior and Primary-only behavior;
- relay alert anti-spam transitions;
- real protocol-1 `bind`, `unbind`, `unbind_result`, snapshot, disconnect and reconnect forwarding;
- bad-secret rejection and Server-ID room isolation;
- destructive unbind forwarding with **no replay after Android reconnect**;
- wheel metadata/entrypoint and installed-wheel import;
- exclusion of `__pycache__` / `.pyc` from the wheel.

## Roadmap

Current Phase 2 makes Render resilient as the control plane. Voice audio is still direct UDP to Android.

The next major phase is a public UDP relay/tunnel for remote users and CGNAT environments:

```text
VoiceCraft clients --UDP--> Public UDP relay/VPS
                              │
                              │ authenticated tunnel
                              ▼
                       Android Tunnel Agent
                              │
                       127.0.0.1:9050
                              ▼
                    VoiceCraft v1.7.1 runtime
```

Render WSS remains the control plane; voice should not be tunneled through Render WebSockets.
