# VoiceCraft.Endstone

Phase 1 Endstone plugin for the VoiceCraft Server Mobile project.

Target: Endstone API 0.11.x / MCSV Endstone 0.11.10.

## Phase 1 goals

- Track online Bedrock players from Endstone directly.
- Capture XUID, UUID, dimension, position, yaw and pitch.
- Log join/quit and dimension changes without spamming every movement tick.
- Provide `/vcbind`, `/vcunbind`, `/vcstatus`, and operator-only `/vcdump` diagnostics.
- Keep binding keys in memory only. Phase 1 does not yet forward a binding key to VoiceCraft Server Mobile.
- Prepare a stable state model for Phase 2 network bridge work.

## Install on MCSV

1. Reinstall the Bedrock server using Endstone 0.11.x and restore your world/backups.
2. Download the built `endstone_voicecraft-0.1.0-py3-none-any.whl`.
3. Upload the wheel into the Endstone `plugins/` directory.
4. Restart the server.
5. Look for `VoiceCraft Endstone Phase 1 enabled` in console.

## Commands

- `/vcbind <key>` captures a VoiceCraft binding key in memory for Phase 2. The key itself is never printed to console.
- `/vcunbind` clears the pending in-memory key.
- `/vcstatus` shows tracker status and the current player's state.
- `/vcdump` prints current player state snapshots. Operator permission only.

## Configuration

The plugin creates `plugins/VoiceCraftEndstone/config.toml` (actual data folder name is controlled by Endstone) on first start.

Defaults:

- tracker interval: 2 ticks (about 10 Hz on a healthy 20 TPS server)
- position epsilon: 0.05 block
- rotation epsilon: 1 degree
- movement logging: disabled
- heartbeat summary: every 30 seconds

Phase 2 will add the outbound bridge from Endstone/MCSV to VoiceCraft Server Mobile while preserving the upstream VoiceCraft voice protocol.
