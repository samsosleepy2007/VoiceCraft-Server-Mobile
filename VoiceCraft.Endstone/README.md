# VoiceCraft.Endstone

Endstone companion plugin for **VoiceCraft Server Mobile**.

Target: **Endstone API 0.11.x / MCSV Endstone 0.11.10**.

Current companion bundle:

- Android: **VoiceCraft Server Mobile UI4.2** (`1.7.1-android-phase2-ui4.2`, version code `8`)
- Endstone plugin: **`0.2.4`**
- Render Relay protocol: **`1`**
- VoiceCraft upstream protocol: **v1.7.1 unchanged**

## Architecture

```text
Minecraft Bedrock @ MCSV
        |
     Endstone
        |
 outbound WSS
        v
 Render Relay
        ^
 outbound WSS
        |
VoiceCraft Server Mobile UI4.2
        |
 VoiceCraft UDP
        v
 VoiceCraft clients
```

The Render relay carries Minecraft player state, binding control messages, snapshots and disconnect/rebind events. Voice audio still uses the normal VoiceCraft/LiteNetLib UDP path directly to the Android server.

## Endstone 0.2.4

Version `0.2.4` fixes the command-registration regression present in 0.2.2/0.2.3 and replaces the old command set with a single in-game control menu.

Run:

```text
/vc
```

This opens an Endstone `ActionForm` with:

- **Bind Microphone** — opens the Binding Key form and submits through the existing secure bind path.
- **Cancel Pending Bind** — cancels a pending bind request only; it does not disconnect an already-bound VoiceCraft client.
- **Status** — shows bridge state, online/tracked counts, binding state, dimension, position, yaw and pitch.
- **Tracked Players (Admin)** — shows tracked player-state snapshots and requires operator permission.

The old public slash commands are no longer registered:

```text
/vcbind
/vcunbind
/vcstatus
/vcdump
```

Their underlying handlers are retained internally and are called by the `/vc` UI, so the existing binding and diagnostic logic is not duplicated.

### Why the commands disappeared in 0.2.2/0.2.3

Endstone 0.11 builds Python plugin metadata from the final exported class `__dict__`. The 0.2.2 and 0.2.3 entry points exported subclasses that inherited `commands` and `permissions` from parent classes. Python method inheritance still worked, so player tracking, Auto Bind and Auto Rebind continued to function, but Endstone did not see inherited command metadata while constructing `PluginDescription`.

0.2.4 declares `commands`, `permissions`, `api_version`, `prefix`, `description` and `authors` directly on the final exported class. CI now checks this exact loader-sensitive condition so the regression cannot silently return.

## Automatic Bind and Rebind

### Initial join

When an unbound player joins:

1. Endstone waits until the player's real spawn state is valid.
2. Transitional BDS positions such as `Y=32768` are ignored.
3. Endstone opens **VoiceCraft - Bind Microphone** automatically.
4. The player enters the one-use Binding Key shown by VoiceCraft Client.
5. The request is sent through Render to Android.
6. Android binds the Minecraft player state to the matching VoiceCraft entity and returns `bind_result`.
7. A wrong or expired key causes the Binding Key form to be offered again.

The Binding Key and Bridge Secret are never intentionally printed to the plugin logs.

### VoiceCraft client disconnect

Endstone 0.2.3+ receives `voice_client_disconnected` when Android detects that a previously-bound VoiceCraft entity disappeared while the Minecraft player is still online.

The plugin:

- warns the player immediately,
- waits `100 ticks / 5 seconds`,
- opens the Binding Key form again,
- suppresses duplicate rebind timers,
- ignores stale disconnect events for an older VoiceCraft entity,
- cancels unnecessary delayed UI if the player leaves or binds successfully first.

## Features

- Tracks Bedrock player name, XUID, UUID, dimension, X/Y/Z, yaw and pitch.
- Filters pre-spawn/transitional states before they reach VoiceCraft.
- Sends changed player state at the configured scheduler interval (default 2 ticks / about 10 Hz at 20 TPS).
- Automatic join-time Binding Key UI.
- Automatic 5-second Rebind UI after VoiceCraft client disconnect.
- `/vc` in-game control menu.
- Strict Render bridge configuration validation.
- Automatic Render relay reconnect.
- Snapshot synchronization when Android reconnects or requests a fresh snapshot.
- Secrets and Binding Keys hidden from normal logs.
- Render Relay protocol `1` preserved.

## Install on MCSV

1. Run the Bedrock server through Endstone `0.11.x` (CI tests against `0.11.10`).
2. Remove older `endstone_voicecraft-*.whl` files from `plugins/`.
3. Install:

```text
endstone_voicecraft-0.2.4-py3-none-any.whl
```

4. Start the server once so Endstone creates the plugin data/config folder.
5. In VoiceCraft Server Mobile UI4.2, open **Bridge** and configure Render URL, Server ID and Bridge Secret.
6. Use **Copy Plugin Config** in the Android app and paste the generated TOML into the Endstone plugin `config.toml`.
7. Restart the Bedrock/Endstone server.
8. Join Minecraft and verify that Auto Bind appears for an unbound player.
9. Run `/vc` and verify that the VoiceCraft menu opens.

Endstone provides the Python runtime used by the plugin. The wheel declares `aiohttp>=3.9` for its outbound WebSocket client.

## Required shared settings

These values must match across all three components:

```text
Render BRIDGE_SECRET = Android Bridge Secret = Endstone bridge.secret
Android Server ID    = Endstone bridge.server_id
Android WebSocket    = Endstone bridge.url = wss://<render-service>/bridge
```

Recommended configuration generated by the Android app:

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

## Expected logs

After Render, Android and Endstone are connected correctly:

```text
BRIDGE connected relay=wss://.../bridge server_id=mcsv-main
BRIDGE STATUS relay=connected android=connected
BRIDGE snapshot queued players=1
VoiceCraft UI ready: /vc
```

When an unbound player finishes spawning:

```text
BIND FORM shown player=<name> xuid=<xuid>
```

After a temporary Render restart/redeploy:

```text
BRIDGE disconnected: ...; retrying in 5s
BRIDGE reconnect attempt=1 relay=wss://.../bridge server_id=mcsv-main
BRIDGE reconnected relay=wss://.../bridge server_id=mcsv-main
```

## `/vc` permissions

```text
voicecraft.command.menu    default: true
voicecraft.command.bind    default: true
voicecraft.command.status  default: true
voicecraft.command.dump    default: op
```

The menu itself is available to normal players. Admin-only functionality still checks its dedicated permission before displaying tracked player data.

## Protocol contract verified by CI

The 0.2.4 CI validates:

- Endstone `0.11.10` event annotations,
- `ActionForm`, `ModalForm` and `TextInput` availability,
- final exported plugin metadata stored directly in `VoiceCraftEndstone.__dict__`,
- exactly one public command: `/vc`,
- Auto Bind and Auto Rebind inheritance,
- pre-spawn filtering,
- strict Render bridge validation,
- wheel metadata and entry point,
- installed-wheel import,
- real Endstone ↔ Render Relay protocol-1 behavior,
- Endstone authentication / `hello_ok`,
- Android mock authentication / `hello_ok`,
- `peer_status`,
- `player_state`,
- `request_snapshot`,
- `bind` and `bind_result`,
- `voice_client_disconnected`,
- Server-ID room isolation,
- bad-secret rejection,
- relay restart and Endstone reconnect.

## Current network boundary

Phase 2 solves the **Minecraft state + binding control plane** across the Internet. It does not expose Android's VoiceCraft UDP voice port publicly.

The next networking phase is a public UDP relay/tunnel that preserves separate LiteNetLib peer identities while allowing the Android server to remain behind mobile/CGNAT networks.
