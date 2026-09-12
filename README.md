# VoiceCraft Server Mobile

VoiceCraft Server Mobile connects a **Minecraft Bedrock server** to a **VoiceCraft server running on Android** by using an **Endstone plugin** and a **Render WebSocket relay**.

The important idea is that Minecraft data and voice audio use two different paths:

- **Minecraft state/control data** goes through Endstone → Render Relay → Android.
- **Voice audio** goes directly between VoiceCraft clients and the Android VoiceCraft server over the normal VoiceCraft UDP transport.

Render is therefore a **control/state relay**, not an audio relay.

## Architecture

```text
Minecraft Bedrock Server
        │
        │ player position / rotation / dimension
        │ bind / unbind / player state
        ▼
Endstone VoiceCraft Plugin
        │
        │ secure WebSocket (WSS)
        ▼
Render Relay
        │
        │ secure WebSocket (WSS)
        ▼
VoiceCraft Server Mobile
Android VoiceCraft Server
        ▲
        │
        │ VoiceCraft / LiteNetLib UDP audio
        │
VoiceCraft Clients
```

## Component Responsibilities

| Component | Runs on | Main responsibility | Communicates with |
|---|---|---|---|
| Minecraft Bedrock Server | Minecraft server | Hosts the game world and players | Endstone |
| Endstone VoiceCraft Plugin | Minecraft server | Reads player position, rotation, dimension and binding actions | Render Relay |
| Render Relay | Render | Forwards Minecraft state and control messages between Endstone and Android | Endstone + Android |
| VoiceCraft Server Mobile | Android | Runs the VoiceCraft server and maps Minecraft player state to VoiceCraft entities | Render Relay + VoiceCraft Clients |
| VoiceCraft Client | Player device | Captures microphone audio and receives nearby-player audio | Android VoiceCraft Server |

## How the Connection Works

| Step | What happens |
|---|---|
| 1 | The Endstone plugin connects to the configured Render Relay using WSS. |
| 2 | The Android server connects to the same relay using the same `serverId` and bridge secret. |
| 3 | Render places both connections in the same logical server room and forwards bridge messages between them. |
| 4 | Endstone sends Minecraft player state such as position, rotation, dimension and player identity. |
| 5 | Android receives that state and updates the matching VoiceCraft network entity. |
| 6 | VoiceCraft uses the updated entity positions to determine proximity and spatial voice behavior. |
| 7 | Voice audio itself travels directly between VoiceCraft clients and the Android VoiceCraft server over UDP. |

## Player Binding

A Minecraft player and a VoiceCraft client must be associated with each other before server-side positioning can work correctly.

```text
VoiceCraft Client connects
        │
        ▼
Android creates / detects a VoiceCraft entity
        │
        ▼
A temporary binding key is generated
        │
        ▼
Player enters the key through /vc in Minecraft
        │
        ▼
Endstone sends the bind request through Render
        │
        ▼
Android links the Minecraft player to the VoiceCraft entity
```

After binding, Endstone continuously supplies the Minecraft position for that player. Android applies that position to the linked VoiceCraft entity, so VoiceCraft can calculate who should hear whom.

## Data Flow

| Data | Source | Path | Destination |
|---|---|---|---|
| Player position | Minecraft / Endstone | Endstone → WSS → Render → WSS → Android | VoiceCraft entity |
| Rotation | Minecraft / Endstone | Endstone → Render → Android | VoiceCraft entity |
| Dimension | Minecraft / Endstone | Endstone → Render → Android | VoiceCraft world mapping |
| Bind request | Minecraft `/vc` | Endstone → Render → Android | VoiceCraft entity binding |
| Unbind request | Minecraft `/vc` | Endstone → Render → Android | VoiceCraft entity binding |
| Relay status | Android / Endstone | Through Render | Opposite bridge peer |
| Voice audio | VoiceCraft Client | UDP directly to Android | VoiceCraft Server |
| Routed voice audio | Android | UDP directly to clients | Nearby VoiceCraft Clients |

## Why Render Is Used

Minecraft Bedrock and the Android VoiceCraft server may be running on completely different networks. The Render Relay gives both sides a public WebSocket endpoint they can connect to without requiring the Minecraft server to connect directly to the Android device.

```text
Endstone ───────┐
                ├── Render Relay ── shared server room
Android ────────┘
```

The relay identifies the connection by `serverId` and authenticates the bridge using the configured secret. Messages belonging to one VoiceCraft server are kept separate from other server rooms.

## Relay Failover

The Android bridge can use a primary relay and optional backup relays.

```text
Primary Relay
     │
     ├── available → keep using Primary
     │
     └── unavailable
             │
             ▼
        Backup Relay #1
             │
             └── if unavailable → next backup
```

If the active relay becomes unavailable, the bridge retries and can move to a configured backup relay. Once both Endstone and Android are connected to the same working relay again, player-state synchronization continues.

The failover system only affects **control/state traffic**. VoiceCraft audio still uses the direct UDP connection to Android.

## Voice Path vs Control Path

| Path | Protocol | Purpose |
|---|---|---|
| Endstone ↔ Render ↔ Android | WSS | Player state, binding, unbinding, snapshots and bridge status |
| VoiceCraft Client ↔ Android | VoiceCraft / LiteNetLib UDP | Real-time voice audio |

This separation is important: **Render does not process or forward microphone audio**. It only keeps the Minecraft world state synchronized with the Android VoiceCraft server.

## In Short

```text
Minecraft knows where the player is.
Endstone reads that information.
Render transports that information.
Android applies it to VoiceCraft.
VoiceCraft uses it for proximity voice.
Audio goes directly to Android over UDP.
```

---

This project is an unofficial modified distribution based on **VoiceCraft by AvionBlock** and is not an official AvionBlock release. See [NOTICE.md](NOTICE.md) and [LICENSE.md](LICENSE.md) for attribution and license information.
