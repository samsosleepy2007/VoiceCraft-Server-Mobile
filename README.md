# VoiceCraft Server Mobile

Android/mobile host for **VoiceCraft v1.7.1** with an Endstone + Render control-plane bridge for hosted Minecraft Bedrock servers.

Current Android app version: **`1.7.1-android-phase2-ui4.2`** (version code `8`).  
Current Endstone plugin version: **`0.2.3`**, paired with Android UI4.2 and Render Relay protocol 1.

> The VoiceCraft v1.7.1 wire protocol is kept unchanged. `VoiceCraft.Upstream` is pinned to commit `85aaccccbb58adb23e8c87144e8b1c24bf4b2011`.

## What this project contains

```text
Minecraft Bedrock @ MCSV
        │
        ▼
Endstone VoiceCraft plugin
        │ outbound WSS
        ▼
Render Relay
        │ outbound WSS
        ▼
VoiceCraft Server Mobile (Android)
        │
        ├─ VoiceCraft UDP server
        ├─ McHttp TCP compatibility transport
        ├─ player/entity state bridge
        └─ binding support
```

The Render relay carries **Minecraft player state + binding control data**. Voice audio still uses VoiceCraft/LiteNetLib UDP directly to the Android server.

## Android app highlights

- Native .NET 10 Android ARM64 APK
- Foreground Service + partial wake lock
- VoiceCraft UDP server, default UDP `9050`
- Android raw `TcpListener` McHttp compatibility transport, default TCP `9050`
- Required Render Relay preflight before startup
- Render Service URL → automatic `wss://.../bridge` conversion
- Ready-to-paste Endstone `config.toml` generator
- Copy buttons for IP, port, IP:port, WSS, Bridge Secret, Server Key, plugin config and complete setup
- Thai UI by default with Thai / English switching
- Light and dark themes
- Thai-localized runtime logs
- Error diagnosis with likely cause and suggested fix
- Red validation highlights for missing/invalid required setup
- Setup popup that jumps directly to the field that must be fixed
- Detailed in-app setup guide and Render shortcut
- Soft modern UI with rounded cards, floating navigation and interaction animations
- UI4.1 native click-dispatch hotfix so animation-only touch handlers no longer swallow button actions
- UI4.2 live Minecraft player / binding dashboard with Request Snapshot
- Automatic 5-second rebind prompt when an already-bound VoiceCraft client disconnects

## UI4 design refresh

UI4 changes the Android interface toward a soft modern productivity-app style while keeping the blue/white VoiceCraft identity:

- softer blue / indigo surfaces and more whitespace
- larger rounded cards and floating bottom navigation
- center Start / Stop action in the bottom bar
- gradient server-status hero card
- animated page entrance and card motion
- press scale/fade feedback + haptic feedback on buttons
- animated focus feedback on inputs
- status pulse when server state changes
- improved bridge flow and automatic diagnostic-help cards
- old UI3 launcher kept compiled only as a disabled fallback/reference

### UI4.1 interaction hotfix

UI4.1 keeps the UI4 design unchanged and repairs the shared Android button interaction layer.

The root cause was the managed `.Touch` event used for press animation. In .NET for Android, listener callbacks that return `bool` expose `EventArgs.Handled`; the generated event starts handled unless explicitly changed. The UI4 animation handler subscribed to `Touch` but did not set `Handled = false`, so the press animation could run while the native `Click` / `PerformClick()` action never fired.

UI4.1 fixes that centrally in `AppButton.cs`:

- animation touch events explicitly pass through with `Handled = false`
- Android native `Button` click/accessibility behavior remains the source of truth
- every native click writes a `CLICK:` diagnostic entry
- exceptions thrown by a button action are caught at the shared button layer, written as `ACTION ERROR`, and surfaced to the user instead of looking like a dead button
- existing UI4 scale/fade animation and haptic feedback are preserved

## Required setup

### 1. Deploy the Render relay

Create a Render Web Service from this repository using:

```text
Root Directory: VoiceCraft.Bridge.Relay
Runtime: Node
Build Command: npm install --omit=dev
Start Command: npm start
Health Check Path: /health
```

Add the environment variable:

```text
BRIDGE_SECRET=<strong-random-secret>
```

After deployment, Render gives a normal HTTPS service URL such as:

```text
https://voicecraft-server-mobile.onrender.com
```

Paste that URL into the Android app. The app generates:

```text
wss://voicecraft-server-mobile.onrender.com/bridge
```

### 2. Configure the Android app

Open **Bridge** and provide:

```text
Render Service URL
Server ID      (default: mcsv-main)
Bridge Secret  (must match BRIDGE_SECRET on Render)
```

The app will not start VoiceCraft Server until these required values and the server port pass validation.

### 3. Install the Endstone plugin

Use Endstone `0.11.x` on the Minecraft Bedrock host and install the verified companion wheel from the UI4.1 GitHub Release:

```text
endstone_voicecraft-0.2.1-py3-none-any.whl
```

Plugin 0.2.1 keeps the existing Phase 2 protocol used by Android UI4.1, validates the Render bridge configuration before connecting, and adds reconnect/close diagnostics. CI verifies it against Endstone `0.11.10` and the repository's real Node relay.

Start the Minecraft server once, then use **Copy Plugin Config** in the Android app and paste the generated config into the plugin `config.toml`.

The generated bridge section looks like:

```toml
[bridge]
enabled = true
url = "wss://your-service.onrender.com/bridge"
server_id = "mcsv-main"
secret = "YOUR_SHARED_SECRET"
reconnect_seconds = 5
```

The following values must match:

```text
Render BRIDGE_SECRET = Android Bridge Secret = Endstone bridge.secret
Android Server ID    = Endstone bridge.server_id
Android WebSocket    = Endstone bridge.url = wss://<render-service>/bridge
```

### 4. Start VoiceCraft Server

Tap **Start Server** on Home or the center button in the bottom navigation.

Before anything launches, the app checks:

```text
Voice / McHttp Port
Render Service URL
Generated WebSocket URL
Server ID
Bridge Secret
```

If something is missing or invalid, startup is blocked and the app shows exactly what must be fixed and where.

### 5. Connect VoiceCraft Client

For current LAN testing, connect a compatible VoiceCraft `1.7.x` client to:

```text
<ANDROID_LAN_IP>:9050
```

Set positioning to **Server**.

After the VoiceCraft client receives a binding key, bind from Minecraft with:

```text
/vcbind ABC12
```

Replace `ABC12` with the actual key shown by the VoiceCraft client.

## Repository layout

```text
VoiceCraft.Upstream/             pinned VoiceCraft v1.7.1 source
VoiceCraft.Server.Android/       Android server host + UI
VoiceCraft.Endstone/             Endstone plugin
VoiceCraft.Bridge.Relay/         Render Node/WebSocket relay
tools/                           build-time upstream patches
.github/workflows/               Android / Endstone / relay CI
```

## Clone

```bash
git clone --recurse-submodules https://github.com/samsosleepy2007/VoiceCraft-Server-Mobile.git
cd VoiceCraft-Server-Mobile
```

If the repository was cloned without submodules:

```bash
git submodule update --init --recursive
```

## Build Android locally

Requirements:

- .NET 10 SDK
- .NET Android workload
- Android SDK / API 36
- Java 17

The CI build applies the Android runtime patches before publish:

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

The patch scripts modify only the checked-out submodule working tree. Reset it with:

```bash
git -C VoiceCraft.Upstream reset --hard
git -C VoiceCraft.Upstream clean -fd
```

## Build Endstone plugin

From `VoiceCraft.Endstone`:

```bash
python -m pip install build
python -m build --wheel
```

The wheel is produced under:

```text
VoiceCraft.Endstone/dist/
```

## Version history

### Endstone 0.2.1 — UI4.1 companion

- verified companion wheel for Android `1.7.1-android-phase2-ui4.1`
- keeps Render Relay protocol `1` and the existing Android Phase 2 message contract
- strict bridge config validation: `ws://`/`wss://`, hostname, exact `/bridge` path, Server ID up to 100 characters, Bridge Secret at least 16 characters, and no placeholder/query/fragment values
- advertises `pluginVersion = 0.2.1`
- improved reconnect-attempt, reconnect-success and WebSocket close diagnostics
- `/vcunbind` wording now explicitly means “cancel pending bind request”; it does not unbind an already-bound VoiceCraft entity
- real CI contract test covers authentication, peer status, player state, snapshot request, bind/bind-result, room isolation, bad-secret rejection and reconnect after relay restart
- wheel CI validates Endstone 0.11.10 annotations, pre-spawn filtering, metadata, entrypoint, bundled config and installed-wheel import

### Android UI4.1 — `1.7.1-android-phase2-ui4.1` / code 7

- fixed the UI4 bug where every button animated on touch but the actual action could be swallowed before native `Click`
- explicitly keeps animation-only `.Touch` events non-consuming with `Handled = false`
- preserves Android native `PerformClick()` behavior for click, sound and accessibility
- logs successful native click dispatch with `CLICK:` diagnostics
- catches unexpected button-action exceptions and reports `ACTION ERROR` instead of silently appearing unresponsive
- keeps the UI4 design, animations, haptics, validation and server logic unchanged

### Android UI4 — `1.7.1-android-phase2-ui4` / code 6

- soft productivity-style blue/white interface
- gradient hero card and floating bottom navigation
- center Start / Stop action
- animated page/card entrance
- button press scale/fade + haptic feedback
- input focus/status animations
- diagnostics help card improvements
- README and release history brought up to date

### Android UI3 — `1.7.1-android-phase2-ui3` / code 5

- Render Relay became mandatory before server startup
- startup stops before Foreground Service / UDP / TCP if config is incomplete
- second service-side preflight added for safety
- guided popup lists missing fields and jumps to the affected page/field
- red validation highlighting
- top language/theme/info controls rebuilt to fix touch interaction
- optional bridge switch removed because Render/Endstone is now part of the required architecture

### Android UI2 — `1.7.1-android-phase2-ui2` / code 4

- Thai became the default UI language
- Thai / English switching
- light / dark theme switching with persisted preference
- `By SamSoSleepy` branding
- button interaction feedback
- in-app setup guide
- Open Render shortcut
- Thai log localization
- common runtime/WebSocket/auth/network error diagnosis with cause + suggested fix

### Android UI1 — `1.7.1-android-phase2-modern-ui1` / code 3

- first blue/white card-based Android redesign
- Home / Bridge / Logs / Settings navigation
- Render URL → WebSocket `/bridge` generation
- Bridge Secret generator and show/hide controls
- quick-copy actions
- ready-to-paste Endstone config generator
- setup readiness/status overview

### Phase 2 control plane — Endstone `0.2.0`

- Endstone → Render outbound WSS bridge
- Render relay authentication, cache, health endpoint and WebSocket forwarding
- Android outbound WSS controller
- player state sync: identity, dimension, position and rotation
- `/vcbind` forwarding
- one-use 5-character binding keys
- pre-spawn invalid Y state filtering
- server-tick `RuntimeDispatcher` so WebSocket callbacks do not mutate the VoiceCraft world from network threads
- secrets and binding keys hidden from logs

### Endstone Phase 1 — `0.1.0` / `0.1.1`

- Endstone 0.11.x plugin package
- player join/quit diagnostics
- XUID / UUID / dimension / position / yaw / pitch tracking
- `/vcbind`, `/vcunbind`, `/vcstatus`, `/vcdump`
- scheduler tracking + heartbeat
- `0.1.1` fixed Endstone event-handler annotations for real Endstone 0.11 runtime validation

### Android Phase 1 / transport stabilization

- initial native Android ARM64 host
- VoiceCraft v1.7.1 headless runtime in Foreground Service
- partial wake lock and private app storage
- Spectre.Console headless compatibility fix
- Android diagnostics UI/logging
- Android `HttpListener` replaced with raw `TcpListener` HTTP/1.1 McHttp transport
- localhost raw TCP probe added
- cleartext McHttp enabled for Android LAN testing
- LAN TCP 9050 reachability verified from another device

Full release notes are also tracked in [`CHANGELOG.md`](CHANGELOG.md).

## Current limitation

The Phase 2 Render bridge solves the **Minecraft state/binding control plane** only.

It does **not** provide a public UDP path for VoiceCraft audio. Remote Internet players still need a future UDP relay/tunnel design that preserves separate LiteNetLib peer identities.

## License / upstream

VoiceCraft upstream is licensed under **GNU GPL v3**. The upstream license is included inside the pinned `VoiceCraft.Upstream` submodule. Changes in this repository are intended to remain GPL-compatible.

Upstream: https://github.com/AvionBlock/VoiceCraft
