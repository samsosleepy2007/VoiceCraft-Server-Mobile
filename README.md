# VoiceCraft Server Mobile

VoiceCraft Server Mobile connects a **Minecraft Bedrock server** to **Android**, where the VoiceCraft server is running, by using an **Endstone plugin** and a **Render WebSocket relay**.

To keep the architecture easy to understand, this README refers to the device running the VoiceCraft server simply as **Android**.

The system uses two separate network paths:

- **Minecraft state/control data**: Endstone → Render Relay → Android
- **Voice audio**: VoiceCraft Client ↔ Android directly over VoiceCraft / LiteNetLib UDP

Render is a **control/state relay only**. It does not carry microphone audio.

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
Android
(VoiceCraft server runs here)
        ▲
        │
        │ VoiceCraft / LiteNetLib UDP
        │
VoiceCraft Clients
```

## Component Responsibilities

| Component | Runs on | Main responsibility | Communicates with |
|---|---|---|---|
| Minecraft Bedrock Server | Minecraft server | Hosts the game world and players | Endstone |
| Endstone VoiceCraft Plugin | Minecraft server | Reads player position, rotation, dimension and bind/unbind actions | Render Relay |
| Render Relay | Render | Forwards Minecraft state and control messages between Endstone and Android | Endstone + Android |
| Android | Android device | Runs the VoiceCraft server and applies Minecraft player state to VoiceCraft entities | Render Relay + VoiceCraft Clients |
| VoiceCraft Client | Player device | Captures microphone audio and receives proximity voice | Android |

## How the Connection Works

| Step | What happens |
|---|---|
| 1 | Endstone connects to the configured Render Relay using WSS. |
| 2 | Android connects to the same relay using the same `serverId` and bridge secret. |
| 3 | Render places Endstone and Android in the same logical server room. |
| 4 | Endstone sends Minecraft player state such as position, rotation, dimension and player identity. |
| 5 | Render forwards that state to Android. |
| 6 | Android updates the matching VoiceCraft entity with the Minecraft position and world state. |
| 7 | VoiceCraft uses those entity positions to calculate proximity/spatial voice. |
| 8 | Voice audio travels directly between VoiceCraft clients and Android over UDP. |

## Player Binding

A Minecraft player and a VoiceCraft client must be linked before server-side positioning can work correctly.

```text
VoiceCraft Client connects
        │
        ▼
Android detects the VoiceCraft entity
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

After binding, Endstone keeps sending the player's Minecraft position. Android applies that position to the linked VoiceCraft entity, allowing VoiceCraft to determine who should hear whom.

## Data Flow

| Data | Source | Path | Destination |
|---|---|---|---|
| Player position | Minecraft / Endstone | Endstone → WSS → Render → WSS → Android | VoiceCraft entity |
| Rotation | Minecraft / Endstone | Endstone → Render → Android | VoiceCraft entity |
| Dimension | Minecraft / Endstone | Endstone → Render → Android | VoiceCraft world mapping |
| Bind request | Minecraft `/vc` | Endstone → Render → Android | VoiceCraft entity binding |
| Unbind request | Minecraft `/vc` | Endstone → Render → Android | VoiceCraft entity binding |
| Relay status | Android / Endstone | Through Render | Opposite bridge peer |
| Voice audio | VoiceCraft Client | UDP directly to Android | Android |
| Routed voice audio | Android | UDP directly to clients | Nearby VoiceCraft Clients |

## Why Render Is Used

The Minecraft server and Android may be on completely different networks. Both sides make an outbound WSS connection to Render, so the Minecraft server does not need to connect directly to Android for player-state and binding traffic.

```text
Endstone ───────┐
                ├── Render Relay ── shared server room
Android ────────┘
```

The relay separates connections by `serverId` and authenticates the bridge with the configured secret.

## VPN / Same-Network Requirement

Render solves the **control/state connection**, but it does not relay VoiceCraft audio. VoiceCraft clients still need direct UDP reachability to Android.

If the VoiceCraft clients and Android are not already on the same LAN, they should be connected through a VPN or mesh network that gives the devices mutually reachable private IP addresses.

Examples include:

- **Tailscale**
- **NordVPN**, when configured with a feature/setup that allows direct device-to-device private networking
- Another WireGuard/mesh VPN that places Android and the VoiceCraft clients on the same reachable private network

```text
VoiceCraft Client
      │
      │ VPN / virtual LAN
      │ direct VoiceCraft UDP
      ▼
Android
```

The important requirement is not the VPN brand itself. The VoiceCraft client must be able to reach Android's VPN/private IP and VoiceCraft UDP port directly.

A normal consumer VPN connection that only sends both devices through an Internet exit server is **not enough** unless it also provides device-to-device connectivity.

### Network Paths

| Connection | Needs Render? | Needs direct reachability? | Recommended network |
|---|---:|---:|---|
| Endstone ↔ Render | Yes | No direct Android connection required | Normal Internet |
| Render ↔ Android | Yes | No direct Minecraft connection required | Normal Internet |
| VoiceCraft Client ↔ Android | No | **Yes** | Same LAN or VPN/mesh network |

## Relay Failover

Android can use a primary relay and optional backup relays.

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

If the active relay becomes unavailable, Android retries and can move to a configured backup relay. Once Endstone and Android are connected to the same working relay again, player-state synchronization continues.

Relay failover only affects **control/state traffic**. Voice audio continues to use the direct UDP path between VoiceCraft clients and Android.

## Voice Path vs Control Path

| Path | Protocol | Purpose |
|---|---|---|
| Endstone ↔ Render ↔ Android | WSS | Player state, binding, unbinding, snapshots and bridge status |
| VoiceCraft Client ↔ Android | VoiceCraft / LiteNetLib UDP | Real-time voice audio |

This separation is important: **Render never processes or forwards microphone audio**.

## In Short

```text
Minecraft knows where the player is.
Endstone reads that information.
Render transports that information.
Android applies it to VoiceCraft.
VoiceCraft uses it for proximity voice.

Voice audio does NOT go through Render.
VoiceCraft clients connect directly to Android over UDP.
If they are on different networks, use a VPN/mesh network so they can reach Android directly.
```

---

This project is an unofficial modified distribution based on **VoiceCraft by AvionBlock** and is not an official AvionBlock release. See [NOTICE.md](NOTICE.md) and [LICENSE.md](LICENSE.md) for attribution and license information.
