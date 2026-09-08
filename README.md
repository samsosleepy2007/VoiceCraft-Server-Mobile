# VoiceCraft Server Mobile

Android/mobile host for **VoiceCraft v1.7.1** with an Endstone + Render control-plane bridge for hosted Minecraft Bedrock servers.

Current versions:

- Android app: **`1.7.1-android-phase2-ui4.2`** (version code `8`)
- Endstone plugin: **`0.2.4`**
- Render Relay protocol: **`1`**
- VoiceCraft upstream: **v1.7.1**, pinned to commit `85aaccccbb58adb23e8c87144e8b1c24bf4b2011`

> VoiceCraft v1.7.1 wire/audio protocol remains unchanged.

## Architecture

```text
Minecraft Bedrock @ MCSV
        │
        ▼
Endstone VoiceCraft 0.2.4
        │ outbound WSS
        ▼
Render Relay (protocol 1)
        │ outbound WSS
        ▼
VoiceCraft Server Mobile UI4.2
        │
        ├─ VoiceCraft v1.7.1 runtime
        ├─ LiteNetLib UDP voice server :9050
        ├─ McHttp TCP compatibility transport :9050
        ├─ player/entity state bridge
        └─ binding/rebinding support
```

Render carries the **control plane** only: Minecraft player state, binding, snapshots and disconnect/rebind events. Voice audio still uses VoiceCraft/LiteNetLib UDP directly to the Android server.

## Android UI4.2 highlights

- Native .NET 10 Android ARM64 APK.
- Foreground Service + partial wake lock.
- VoiceCraft UDP server, default UDP `9050`.
- Android raw `TcpListener` McHttp compatibility transport, default TCP `9050`.
- Required Render Relay preflight before startup.
- Render Service URL → automatic `wss://.../bridge` conversion.
- Ready-to-paste Endstone `config.toml` generator.
- Thai default UI with Thai/English switching.
- Light and dark themes.
- Runtime diagnostics and guided error help.
- Live Minecraft player/binding dashboard.
- `Request Snapshot` action.
- Detection of bound VoiceCraft client disconnects.
- Automatic 5-second Rebind prompt through Endstone.

## Endstone 0.2.4

Endstone 0.2.4 fixes the command-registration regression in 0.2.2/0.2.3 and consolidates all player-facing controls under one command:

```text
/vc
```

The `/vc` UI contains:

- **Bind Microphone** — opens a Binding Key input form.
- **Cancel Pending Bind** — cancels only a pending bind request.
- **Status** — shows bridge state, binding state, player position/dimension and tracker counts.
- **Tracked Players (Admin)** — operator-only tracked state view.

The old public commands are intentionally no longer registered:

```text
/vcbind
/vcunbind
/vcstatus
/vcdump
```

Their existing internal logic is reused by the UI, so Auto Bind, Auto Rebind and binding security behavior remain compatible with Android UI4.2 and Relay protocol 1.

## Required setup

### 1. Deploy the Render relay

Create a Render Web Service from this repository:

```text
Root Directory: VoiceCraft.Bridge.Relay
Runtime: Node
Build Command: npm install --omit=dev
Start Command: npm start
Health Check Path: /health
```

Add:

```text
BRIDGE_SECRET=<strong-random-secret>
```

A normal Render URL such as:

```text
https://voicecraft-server-mobile.onrender.com
```

is converted by Android to:

```text
wss://voicecraft-server-mobile.onrender.com/bridge
```

### 2. Configure Android

Open **Bridge** and provide:

```text
Render Service URL
Server ID      (default: mcsv-main)
Bridge Secret  (must match Render BRIDGE_SECRET)
```

The Android server will not start until the port and required bridge settings pass validation.

### 3. Install Endstone 0.2.4

Use Endstone `0.11.x` on the Minecraft Bedrock host and install:

```text
endstone_voicecraft-0.2.4-py3-none-any.whl
```

Remove older `endstone_voicecraft-*.whl` files before installing the new wheel.

Start the Minecraft server once, then use **Copy Plugin Config** in the Android app and paste the generated TOML into the Endstone plugin `config.toml`.

The required values must match:

```text
Render BRIDGE_SECRET = Android Bridge Secret = Endstone bridge.secret
Android Server ID    = Endstone bridge.server_id
Android WebSocket    = Endstone bridge.url = wss://<render-service>/bridge
```

### 4. Start VoiceCraft Server Mobile

Tap **Start Server** in the Android app.

Startup validates:

```text
Voice / McHttp Port
Render Service URL
Generated WebSocket URL
Server ID
Bridge Secret
```

### 5. Connect VoiceCraft Client

For LAN testing, connect a compatible VoiceCraft `1.7.x` client to:

```text
<ANDROID_LAN_IP>:9050
```

Set positioning to **Server**.

When the VoiceCraft client receives a one-use Binding Key, join Minecraft. Endstone automatically opens the Bind form for an unbound player. If needed, run:

```text
/vc
```

and choose **Bind Microphone**.

## Binding lifecycle

```text
VoiceCraft client connects
        │
        ▼
Android creates VoiceCraft entity
        │
        ▼
One-use Binding Key assigned
        │
        ▼
Minecraft player joins
        │
        ▼
Endstone waits for valid spawn state
        │
        ▼
Auto Bind form
        │
        ▼
Bind request → Render → Android
        │
        ▼
Minecraft player ↔ VoiceCraft entity
```

If the bound VoiceCraft client later disconnects while the Minecraft player remains online:

```text
Android detects entity destruction
        │
        ▼
voice_client_disconnected
        │
        ▼
Render Relay
        │
        ▼
Endstone warning
        │
        ▼
wait 5 seconds
        │
        ▼
Rebind form
```

Duplicate/stale disconnect events are suppressed.

## Repository layout

```text
VoiceCraft.Upstream/             pinned VoiceCraft v1.7.1 source
VoiceCraft.Server.Android/       Android server runtime + UI
VoiceCraft.Endstone/             Endstone companion plugin
VoiceCraft.Bridge.Relay/         Render Node/WebSocket relay
tools/                           build-time upstream patches
.github/workflows/               Android / Endstone / relay CI
```

## Build Android locally

Requirements:

- .NET 10 SDK
- .NET Android workload
- Android SDK / API 36
- Java 17

```bash
python tools/apply_phase1.py VoiceCraft.Upstream
python tools/apply_android_console_fix.py VoiceCraft.Upstream
python tools/apply_android_tcp_mchttp.py VoiceCraft.Upstream
python tools/apply_bridge_runtime.py VoiceCraft.Upstream

dotnet workload restore VoiceCraft.Server.Android/VoiceCraft.Server.Android.csproj
dotnet restore VoiceCraft.Server.Android/VoiceCraft.Server.Android.csproj
dotnet publish VoiceCraft.Server.Android/VoiceCraft.Server.Android.csproj -c Release -r android-arm64
```

APK output:

```text
VoiceCraft.Server.Android/bin/Release/net10.0-android/android-arm64/publish/*.apk
```

## Build Endstone locally

```bash
cd VoiceCraft.Endstone
python -m pip install build
python -m build --wheel
```

Output:

```text
VoiceCraft.Endstone/dist/endstone_voicecraft-0.2.4-py3-none-any.whl
```

## CI coverage

The Endstone workflow verifies against Endstone `0.11.10`:

- event-handler annotations,
- pre-spawn `Y=32768` filtering,
- `/vc` metadata declared directly on the exported class,
- exactly one public VoiceCraft command (`/vc`),
- `ActionForm`, `ModalForm` and `TextInput`,
- Auto Bind and Auto Rebind inheritance,
- strict bridge validation,
- real Endstone ↔ Node Relay protocol-1 contract,
- wheel metadata and entry point,
- installed-wheel import.

## Version milestones

- **Android UI4.2 + Endstone 0.2.3** — live player/binding dashboard and automatic rebind after VoiceCraft disconnect.
- **Endstone 0.2.4** — fixes inherited command metadata regression and introduces the single `/vc` control UI.
- **Endstone 0.2.2** — automatic join-time Binding Key form.
- **Endstone 0.2.1** — hardened relay validation and reconnect diagnostics.
- **Phase 2 / Endstone 0.2.0** — Endstone ↔ Render ↔ Android state/binding control plane.
- **Android Phase 1** — native ARM64 VoiceCraft v1.7.1 server runtime on Android.

Full history is tracked in [`CHANGELOG.md`](CHANGELOG.md).

## Current limitation / next networking phase

The current system solves the **Minecraft state + binding control plane** across the Internet.

It does **not** yet expose Android's VoiceCraft UDP voice port through CGNAT/mobile networks. Remote Internet players still need the planned public UDP relay/tunnel that preserves separate LiteNetLib peer identities.

## License / upstream

VoiceCraft upstream is licensed under **GNU GPL v3**. The pinned upstream license remains in `VoiceCraft.Upstream` and this derivative project is intended to remain GPL-compatible.

Upstream: https://github.com/AvionBlock/VoiceCraft
