# VoiceCraft Server Mobile

Android/mobile host for **VoiceCraft v1.7.1** with an Endstone + Render control-plane bridge for hosted Minecraft Bedrock servers.

## Current release

- Android app: **`1.7.1-android-phase2-ui4.5-account-v2-guest`** (version code `11`)
- Endstone plugin: **`0.2.6`**
- Render Relay: **`0.2.1`**
- Render Relay protocol: **`1`**
- VoiceCraft upstream: **v1.7.1**, pinned to commit `85aaccccbb58adb23e8c87144e8b1c24bf4b2011`

Download the newest production bundle from the repository **Releases** page.

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
VoiceCraft Server Mobile UI4.5
        │
        ├─ Account V2 / local Free Account gate
        ├─ VoiceCraft v1.7.1 runtime
        ├─ LiteNetLib UDP voice server :9050
        ├─ McHttp TCP compatibility :9050
        ├─ Minecraft player/entity bridge
        ├─ Bind / Rebind / real Unbind
        └─ relay/binding dashboard
```

## UI4.5 / Account V2 highlights

### Account V2 + Free Account

The Android app now starts at the VoiceCraft account gate.

Two user modes are supported:

- **VoiceCraft Account** — sign in with an existing registered VoiceCraft account.
- **Free Account** — creates a random `GUEST-...` identity stored only on that Android installation.

Free Account behavior:

- profile/session data is encrypted with AES-GCM;
- encryption keys are held in Android Keystore;
- data is stored under Android `NoBackupFilesDir`;
- Android cloud backup/device-transfer restore is disabled for the app;
- force-close, normal restart and APK update keep the same Guest ID;
- **Clear App Data** or uninstall permanently removes the Guest ID;
- reinstalling or creating a Free Account after data removal produces a new Guest ID;
- Guest identities are not inserted into the main VoiceCraft account tables.

The mobile app intentionally exposes only normal account login. Admin OTP/MFA controls and admin-role indicators are not shown in the Android UI.

### Cleaner mobile UI

UI4.5 reorganizes the app for smaller screens and tablets:

- the main header is simplified;
- Language, Theme, Information and Account controls are moved into **Settings**;
- crowded three-button rows are replaced by clearer full-width actions where appropriate;
- Home is focused on server status, address, relay state and player/bind state;
- Relay setup is grouped into clear Render, Server/Secret and Plugin Config sections;
- Account Center now follows the same light/dark visual language as the rest of the app;
- Thai and English remain available.

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

### Real VoiceCraft Unbind

`Disconnect / Unbind Microphone` disconnects the actual VoiceCraft client instead of only clearing local plugin state.

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

```text
Red     relay/control plane temporarily unavailable
Red     Primary exhausted and no Backup Relay is configured
Yellow  moving to Backup #N / wrapping to Primary
Green   Backup #N paired successfully / Primary recovered
```

Alerts are session-based so the reconnect loop does not flood Minecraft chat.

## Required setup

### 1. Install the Android APK

Install the signed ARM64 APK from the latest GitHub Release.

On first launch:

1. sign in with an existing VoiceCraft account, or
2. choose **Create Free Account** for a device-local Guest identity.

### 2. Deploy the Primary Render Relay

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

### 3. Deploy optional Backup Relays

Deploy the same `VoiceCraft.Bridge.Relay` source on each backup and use the **same `BRIDGE_SECRET`**.

```text
Primary   https://voicecraft-main.onrender.com
Backup #1 https://voicecraft-backup1.onrender.com
Backup #2 https://voicecraft-backup2.onrender.com
```

### 4. Configure Android Relay settings

Open **Relay** and configure:

```text
Primary Render Service URL
Server ID      (default: mcsv-main)
Bridge Secret  (same value as every Render BRIDGE_SECRET)
```

Use **Manage Backup Relays** for optional backups. Primary-only configuration is valid.

### 5. Install Endstone 0.2.6

Use Endstone `0.11.x` on the Minecraft Bedrock host and install:

```text
endstone_voicecraft-0.2.6-py3-none-any.whl
```

Remove older `endstone_voicecraft-*.whl` files before installing the new wheel.

Generate the Endstone config from Android after Relay settings are configured.

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

### 6. Start VoiceCraft Server Mobile

Use the center **Start** button in the Android bottom navigation.

Before startup, Android validates the Relay URL, generated WebSocket URL, Server ID, Bridge Secret and Voice/McHttp port.

Default voice/McHttp port:

```text
9050
```

### 7. Connect a VoiceCraft client

For LAN testing:

```text
<ANDROID_LAN_IP>:9050
```

Set positioning mode to **Server**.

After the client receives its one-use Binding Key, join Minecraft and run:

```text
/vc
```

## Relay protocol additions

Protocol number remains **1**.

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

The Render Relay never transports VoiceCraft audio.

## Security notes

- Use `wss://` for deployed Render connections.
- Never commit the shared Bridge Secret to a public repository.
- Use the same Bridge Secret on Primary and backups for seamless failover.
- If a Relay is compromised, rotate the secret consistently on every Relay, Android and Endstone.
- Binding Keys and Bridge Secrets are hidden from normal runtime logs.
- Free Account files use encrypted local storage and are excluded from Android backup/restore.
- Guest signing uses the separate `voicecraft-guest` backend and does not create normal account records.

## CI / verification

The repository validates:

- pinned VoiceCraft v1.7.1 upstream commit;
- Android UI4.5 / Account V2 source guards;
- Guest local-storage hardening (`NoBackupFilesDir`, Android Keystore, AES-GCM);
- exactly one Android launcher (`AccountGateActivity`);
- no admin OTP/admin-role UI in the mobile login flow;
- full Android ARM64 `dotnet build` and `dotnet publish`;
- Endstone 0.11.x compatibility and wheel packaging;
- ordered/circular multi-relay behavior and Primary-only behavior;
- Relay alert anti-spam transitions;
- protocol-1 `bind`, `unbind`, `unbind_result`, snapshot and reconnect forwarding;
- destructive unbind forwarding with **no replay after Android reconnect**.

## Release assets

The production release workflow publishes:

```text
VoiceCraft-Server-Mobile-UI4.5-AccountV2-arm64-Signed.apk
endstone_voicecraft-0.2.6-py3-none-any.whl
SHA256SUMS.txt
```

## Current limitation / roadmap

Render WSS is the control plane only. Voice audio is still direct UDP to Android.

The next major networking phase is a public UDP relay/tunnel for remote users and CGNAT environments:

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

Render WSS should remain the control plane; VoiceCraft audio should not be tunneled through Render WebSockets.
