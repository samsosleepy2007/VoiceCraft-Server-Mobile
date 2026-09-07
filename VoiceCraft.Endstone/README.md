# VoiceCraft.Endstone

Endstone bridge for the **VoiceCraft Server Mobile** project.

Target: **Endstone API 0.11.x / MCSV Endstone 0.11.10**.

Verified companion bundle: **VoiceCraft Server Mobile UI4.1** (`1.7.1-android-phase2-ui4.1`, Android version code `7`) + **Endstone plugin 0.2.1** + **Render Relay protocol 1**.

## Phase 2 architecture

```text
Minecraft Bedrock @ MCSV
        |
     Endstone
        |
 outbound WSS
        v
 Render relay
        ^
 outbound WSS
        |
VoiceCraft Server Mobile UI4.1
        |
 VoiceCraft UDP
        v
 VoiceCraft clients
```

The relay carries only Minecraft player-state and binding control data. Voice audio remains on the normal VoiceCraft/LiteNetLib UDP transport.

## 0.2.1 changes

- Keeps the existing Phase 2 protocol used by Android UI4.1; no Android protocol change is required.
- Validates the bridge configuration before opening WSS: `ws://`/`wss://`, a hostname, path exactly `/bridge`, Server ID 1-100 characters, and Bridge Secret at least 16 characters.
- Rejects placeholder values and relay URLs containing query/fragment text.
- Uses `pluginVersion = 0.2.1` during relay hello.
- Adds reconnect-attempt and reconnect-success logs.
- Adds WebSocket close-code/error details when the relay drops unexpectedly.
- Clarifies `/vcunbind`: it **only cancels a pending bind request**. It does not unbind a VoiceCraft entity that already completed binding.

## Features

- Tracks Bedrock players directly from Endstone: name, XUID, UUID, dimension, X/Y/Z, yaw and pitch.
- Filters pre-spawn/transitional states such as the observed BDS `Y=32768` sentinel before they can reach VoiceCraft.
- Sends changed player state at the configured scheduler interval (default 2 ticks / about 10 Hz at 20 TPS).
- Forwards `/vcbind <key>` securely to VoiceCraft Server Mobile through Render.
- Never intentionally prints a binding key or Bridge Secret to the Endstone console.
- Reconnects automatically after relay interruption.
- Sends a fresh snapshot when the Android peer reconnects.
- Provides `/vcstatus`, `/vcunbind`, and operator-only `/vcdump` diagnostics.

## Install on MCSV

1. Run the Bedrock server through Endstone `0.11.x` (tested in CI with `0.11.10`).
2. Remove older `endstone_voicecraft-*.whl` files from `plugins/`.
3. Upload `endstone_voicecraft-0.2.1-py3-none-any.whl` into `plugins/`.
4. Start the server once so Endstone creates the plugin data/config folder.
5. In VoiceCraft Server Mobile UI4.1, open **Bridge** and configure Render URL, Server ID and Bridge Secret.
6. Use **Copy Plugin Config** in the Android app and paste that complete TOML into the Endstone plugin `config.toml`.
7. Restart the Bedrock/Endstone server.

Endstone already provides the Python environment used by the plugin; the wheel declares `aiohttp>=3.9` for the outbound WebSocket client.

## Required shared settings

These values must match across all three components:

```text
Render BRIDGE_SECRET = Android Bridge Secret = Endstone bridge.secret
Android Server ID    = Endstone bridge.server_id
Android WebSocket    = Endstone bridge.url = wss://<render-service>/bridge
```

Recommended configuration from **Copy Plugin Config** in UI4.1:

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
url = "wss://YOUR-SERVICE.onrender.com/bridge"
server_id = "mcsv-main"
secret = "THE_SAME_BRIDGE_SECRET"
reconnect_seconds = 5
```

0.2.1 keeps the bridge disabled and prints the exact validation reason when the setup is invalid instead of repeatedly attempting a connection with a bad configuration.

## Expected connection logs

After Render, Android and Endstone are configured correctly:

```text
BRIDGE connected relay=wss://.../bridge server_id=mcsv-main
BRIDGE STATUS relay=connected android=connected
BRIDGE snapshot queued players=1
```

After a temporary Render restart/redeploy, 0.2.1 can show:

```text
BRIDGE disconnected: ...; retrying in 5s
BRIDGE reconnect attempt=1 relay=wss://.../bridge server_id=mcsv-main
BRIDGE reconnected relay=wss://.../bridge server_id=mcsv-main
```

## Binding flow

1. Connect a VoiceCraft 1.7.x client to the Android server using **Server positioning mode**.
2. Android creates a VoiceCraft network entity and assigns a one-use 5-character binding key to its description.
3. In Minecraft, run:

```text
/vcbind ABC12
```

4. Endstone sends `bind` through Render with the Minecraft player state.
5. Android resolves the key against the connected VoiceCraft entity, applies the Minecraft state and returns `bind_result`.
6. Endstone reports success or the rejection reason to the Minecraft player.

The binding key is intentionally not printed in plugin/Android runtime logs.

## Commands

- `/vcbind <key>` — send the one-use VoiceCraft binding key to Android through the relay.
- `/vcunbind` — **cancel a pending binding request only**; it does not disconnect an already-bound voice client.
- `/vcstatus` — show tracker state and relay/Android connectivity.
- `/vcdump` — print current valid player-state snapshots; operator permission only.

## Protocol contract verified by CI

The 0.2.1 CI test starts the repository's real Node relay and the real Endstone WSS client and verifies:

- Endstone authentication / `hello_ok`
- Android mock authentication / `hello_ok`
- `peer_status`
- `player_state` fields including dimension, yaw and pitch
- `request_snapshot`
- `bind` and `bind_result`
- Server-ID room isolation
- bad-secret rejection using relay close code `4403`
- relay shutdown followed by automatic Endstone reconnect
- heartbeat forwarding after reconnect

The wheel build additionally verifies Endstone 0.11.10 event annotations, the pre-spawn filter, wheel `METADATA`, Endstone entry point, bundled `config.toml`, and import after installation.

## Current network boundary

Phase 2 solves the **Minecraft state + binding path** across the Internet. It does not expose Android's VoiceCraft UDP voice port publicly. A public UDP endpoint/relay remains a separate networking phase.
