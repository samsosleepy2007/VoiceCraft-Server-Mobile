# Changelog

All notable changes to VoiceCraft Server Mobile are recorded here.

The project keeps VoiceCraft upstream pinned to **v1.7.1** while evolving the Android host, Endstone integration, Render relay and user interface around it.

## 2026-09-07 — Android UI4

Version: `1.7.1-android-phase2-ui4`  
Android version code: `6`

### Added

- New soft modern Android launcher inspired by productivity/mobile dashboard design patterns.
- Blue / indigo / white visual system with dark-mode equivalents.
- Gradient server-status hero card.
- Floating rounded bottom navigation.
- Center Start / Stop action in the bottom bar.
- Page fade/slide entrance animation.
- Staggered content entrance animation.
- Button scale/fade interaction feedback.
- Haptic feedback on button actions.
- Input focus animation.
- Server-state pulse animation.
- Cleaner bridge connection-flow cards.
- Automatic diagnostics-help card in Logs.

### Changed

- New `ModernMainActivity` is the launcher.
- UI3 `MainActivity` remains compiled as a disabled fallback/reference.
- Foreground notification now opens the new modern launcher.
- README rewritten to document the actual Phase 2 architecture and previous versions.

### Preserved

- Required Render Relay validation before server startup.
- Thai default language + English switching.
- Light / dark themes.
- Thai-localized logs.
- Render URL → `wss://.../bridge` generation.
- ready-to-paste Endstone config generation.
- error cause/fix diagnostics.
- VoiceCraft v1.7.1 protocol compatibility.

---

## 2026-09-07 — Android UI3

Version: `1.7.1-android-phase2-ui3`  
Android version code: `5`

### Added / Changed

- Render Relay setup became mandatory before VoiceCraft Server startup.
- Startup validation checks port, Render URL, generated WSS URL, Server ID and Bridge Secret.
- Incomplete setup blocks startup before Foreground Service / UDP / TCP are launched.
- Added a second service-side preflight as a safety layer.
- Validation popup lists every missing setting.
- Popup explains where each setting is located and how to fix it.
- “Go to Bridge / Settings” shortcut focuses the affected field.
- Missing/invalid required fields receive red highlights.
- Removed the optional bridge switch because Endstone + Render is now required by this architecture.
- Rebuilt top-bar language/theme/info controls so their touch targets do not overlap.
- Updated interaction handling to normal Android click events with press feedback.

---

## 2026-09-07 — Android UI2

Version: `1.7.1-android-phase2-ui2`  
Android version code: `4`

### Added

- Thai as the default app language.
- Thai / English UI switching.
- Light / dark theme switching.
- Persisted UI language and theme preferences.
- `By SamSoSleepy` branding.
- Button visual feedback.
- Red required-field validation.
- Detailed in-app setup guide.
- Open Render shortcut.
- Thai runtime-log localization.
- Error diagnosis with likely cause and suggested fix.

### Diagnostics covered

- wrong WebSocket `/bridge` path / HTTP 404
- Relay authentication mismatch
- Bridge Secret / Server ID mismatch
- port already in use
- connection refused
- DNS lookup failure
- timeout
- invalid bridge configuration
- unsupported Android runtime API

---

## 2026-09-06 — Android UI1

Version: `1.7.1-android-phase2-modern-ui1`  
Android version code: `3`

### Added

- First modern blue/white Android redesign.
- Home / Bridge / Logs / Settings pages.
- Render Service URL field.
- Automatic `http(s)` → `ws(s)` conversion with required `/bridge` path.
- Copy actions for:
  - LAN IP
  - port
  - IP:port
  - WebSocket URL
  - Bridge Secret
  - VoiceCraft Server Key
  - Endstone plugin config
  - complete bridge setup
- Cryptographically random Bridge Secret generator.
- Secret show/hide controls.
- Ready-to-paste Endstone `config.toml` generator.
- Setup readiness display.

---

## 2026-09-06 — Phase 2 control plane

Endstone plugin version: `0.2.0`

### Endstone

- Outbound authenticated WSS bridge to Render.
- Player create/update/state synchronization.
- XUID, UUID, name, dimension, position, yaw and pitch synchronization.
- `/vcbind` forwarding through the bridge.
- pre-spawn invalid position filtering (`Y=32768`).
- state snapshots and reconnect synchronization.

### Render relay

- Node.js WebSocket relay.
- `BRIDGE_SECRET` authentication.
- room separation by `serverId`.
- Android / Endstone peer-state reporting.
- cached player state replay.
- `/health` endpoint.
- WebSocket forwarding smoke-test CI.

### Android

- Outbound WSS bridge controller.
- VoiceCraft entity creation/update from Endstone player state.
- one-use five-character binding keys.
- binding key displayed through VoiceCraft client description.
- `RuntimeDispatcher` queues network callbacks to the VoiceCraft server tick.
- secrets and binding keys intentionally hidden from runtime logs.

### Important limitation

- Phase 2 solves player-state and binding transport only.
- Voice audio still requires direct LiteNetLib UDP reachability to Android.

---

## 2026-09-06 — Endstone Phase 1

Versions: `0.1.0`, `0.1.1`

### 0.1.0

- Endstone 0.11.x wheel package.
- player join / quit diagnostics.
- scheduler-based player tracking.
- XUID / UUID / dimension / position / rotation capture.
- `/vcbind` and `/vcunbind` command scaffolding.
- `/vcstatus` and operator-only `/vcdump`.
- configurable tracking interval, epsilon and heartbeat.
- binding keys kept in memory and not logged directly.

### 0.1.1

- Fixed Endstone 0.11 runtime event-handler validation by ensuring event annotations are real classes instead of deferred string annotations.
- Real MCSV runtime test confirmed plugin loading, player tracking and commands.

---

## 2026-09-06 — Android Phase 1 and transport stabilization

### Initial Android host

- Native .NET 10 Android ARM64 application.
- VoiceCraft v1.7.1 headless runtime.
- Foreground Service.
- partial wake lock.
- app-private writable storage.
- Start / Stop lifecycle.
- generated/persisted server key.
- UDP voice listener.
- McHttp compatibility transport.

### Headless compatibility fixes

- Patched VoiceCraft server project to build as a reusable library for Android.
- Removed Spectre.Console terminal assumptions in headless Android mode.
- Fixed Android-specific exception / clipboard compile issues.

### Android diagnostics

- Added Runtime / McHttp diagnostic log view.
- Added copy/clear log actions.
- Added local McHttp health/self-probe logging.

### McHttp Android transport

- Found that `HttpListener.Start()` could report success on Android while no usable socket was reachable.
- Replaced Android McHttp with raw `TcpListener` HTTP/1.1 transport.
- Preserved POST-only McHttp semantics, `/connect`, bearer sessions, Z85 body encoding and existing McApi framing.
- Added raw `TcpClient` self-probe.
- Enabled cleartext traffic for LAN McHttp testing.
- Verified LAN TCP reachability to Android from another device.

---

## Upstream

VoiceCraft upstream remains pinned to:

```text
AvionBlock/VoiceCraft
v1.7.1
85aaccccbb58adb23e8c87144e8b1c24bf4b2011
```

Upstream is GPLv3; this derivative project is intended to remain GPL-compatible.
