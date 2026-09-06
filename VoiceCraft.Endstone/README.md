# VoiceCraft.Endstone

Endstone bridge for the VoiceCraft Server Mobile project.

Target: Endstone API 0.11.x / MCSV Endstone 0.11.10.

## Phase 2

Phase 2 keeps Minecraft player state server-side on MCSV and sends it through an outbound WebSocket relay to VoiceCraft Server Mobile. Neither MCSV nor Android needs to connect to the Android LAN address.

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
VoiceCraft Server Mobile
        |
 VoiceCraft UDP
        v
 VoiceCraft clients
```

The relay carries only the Minecraft state/binding control plane. Voice audio remains on the normal VoiceCraft UDP transport.

## Features

- Track Bedrock players directly from Endstone: name, XUID, UUID, dimension, X/Y/Z, yaw and pitch.
- Filter pre-spawn/transitional positions such as the observed BDS `Y=32768` sentinel before they can reach VoiceCraft.
- Send changed player state at the configured scheduler interval (default 2 ticks / about 10 Hz at 20 TPS).
- Forward `/vcbind <key>` securely to VoiceCraft Server Mobile through the relay.
- Never intentionally print a binding key or bridge secret to the Endstone console.
- Reconnect automatically and send a full player snapshot when Android reconnects.
- `/vcstatus`, `/vcunbind`, and operator-only `/vcdump` diagnostics remain available.

## Install on MCSV

1. Run the Bedrock server through Endstone 0.11.x.
2. Remove an older `endstone_voicecraft-*.whl` from `plugins/`.
3. Upload `endstone_voicecraft-0.2.0-py3-none-any.whl` into `plugins/`.
4. Start once so Endstone creates the plugin data/config folder.
5. Configure the `[bridge]` section shown below.
6. Restart the server.

Endstone itself already depends on `aiohttp`, which the Phase 2 outbound WebSocket client uses.

## Bridge configuration

Deploy `VoiceCraft.Bridge.Relay` on Render and configure one secret in Render:

```text
BRIDGE_SECRET=<long-random-secret>
```

Then configure the same relay URL, server ID, and secret in this plugin's `config.toml`:

```toml
[bridge]
enabled = true
url = "wss://YOUR-RELAY.onrender.com/bridge"
server_id = "mcsv-main"
secret = "THE_SAME_BRIDGE_SECRET"
reconnect_seconds = 5
```

The Android app must use the exact same `url`, `server_id`, and `secret`.

Expected Endstone logs after both sides are online:

```text
BRIDGE connected relay=wss://.../bridge server_id=mcsv-main
BRIDGE STATUS relay=connected android=connected
BRIDGE snapshot queued players=1
```

## Binding flow

When a VoiceCraft client connects to the Android server in **Server positioning mode**, Android assigns that voice entity a stock-style temporary 5-character binding key and puts it in the VoiceCraft client description.

The Minecraft player runs:

```text
/vcbind ABC12
```

Endstone sends the bind request to Android through Render. Android resolves the temporary key against the connected VoiceCraft entity, attaches the Minecraft player's state, and returns a bind result. The binding key is one-use and is not logged.

## Commands

- `/vcbind <key>` sends the one-use VoiceCraft binding key to the mobile server bridge.
- `/vcunbind` clears a pending bind request on the Endstone side.
- `/vcstatus` shows tracker and relay/Android peer state.
- `/vcdump` prints current valid player state snapshots; operator permission only.

## Tracking configuration

```toml
[tracking]
interval_ticks = 2
position_epsilon = 0.05
rotation_epsilon = 1.0
log_position_changes = false
heartbeat_seconds = 30
```

The plugin ignores non-finite coordinates, extreme world coordinates, and Y outside `-4096..4096`, specifically preventing the pre-spawn `Y=32768` value observed on MCSV from entering VoiceCraft proximity calculations.

## Current network boundary

Phase 2 solves the **Minecraft state + binding path** across the Internet. It does not expose Android's VoiceCraft UDP voice port publicly. For the first end-to-end test, the VoiceCraft client can be on the same LAN as the Android server while the Minecraft server remains remote on MCSV. A public UDP relay/endpoint is a separate follow-up phase.
