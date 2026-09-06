# VoiceCraft Server Mobile

Android/mobile server host for **VoiceCraft v1.7.1**.

This project keeps the VoiceCraft 1.7.1 wire protocol unchanged. The original VoiceCraft source is pinned as the `VoiceCraft.Upstream` git submodule at tag `v1.7.1` / commit `85aaccccbb58adb23e8c87144e8b1c24bf4b2011`, while this repository contains the Android host and a small build-time patch that makes the desktop server runtime reusable from an Android Foreground Service.

## Phase 1 goals

- Android ARM64 APK
- Original LiteNetLib VoiceCraft UDP server, default UDP `9050`
- McHttp transport on the same numeric TCP port, default TCP `9050`
- Android Foreground Service + partial wake lock
- App-private writable server config/log storage
- Start / Stop / connection-info UI
- Stock VoiceCraft 1.7.x client/addon protocol; no packet or Opus re-encoding changes
- LAN-first deployment

## Clone

```bash
git clone --recurse-submodules https://github.com/samsosleepy2007/VoiceCraft-Server-Mobile.git
cd VoiceCraft-Server-Mobile
```

If the repository was cloned without submodules:

```bash
git submodule update --init --recursive
```

## Build locally

Requirements: .NET 10 SDK, .NET Android workload, Android SDK/API 36.

```bash
python tools/apply_phase1.py VoiceCraft.Upstream
dotnet workload restore VoiceCraft.Server.Android/VoiceCraft.Server.Android.csproj
dotnet publish VoiceCraft.Server.Android/VoiceCraft.Server.Android.csproj -c Release -r android-arm64
```

APK output:

```text
VoiceCraft.Server.Android/bin/Release/net10.0-android/android-arm64/publish/*.apk
```

The patch intentionally modifies only the checked-out submodule working tree. Reset it any time with:

```bash
git -C VoiceCraft.Upstream reset --hard
git -C VoiceCraft.Upstream clean -fd
```

## Android test order

1. Install the ARM64 APK.
2. Set Android battery usage for VoiceCraft Server to **Unrestricted**.
3. Start the server on port `9050`.
4. Connect a normal VoiceCraft client from another device to `<PHONE_LAN_IP>:9050`.
5. Connect the Bedrock addon to `http://<PHONE_LAN_IP>:9050` with the server key shown by the app.
6. Bind two players and verify position/proximity audio routing.
7. Turn the phone screen off for 10+ minutes and verify the server remains reachable.

## Phase 1 limitation

Phase 1 is LAN-first. CGNAT/public-internet reachability is not solved here; a relay/tunnel mode belongs in a later phase.

## License / upstream

VoiceCraft upstream is licensed under GNU GPL v3. The upstream license is included inside the pinned `VoiceCraft.Upstream` submodule. Changes in this repository are intended to remain GPL-compatible.

Upstream: https://github.com/AvionBlock/VoiceCraft
