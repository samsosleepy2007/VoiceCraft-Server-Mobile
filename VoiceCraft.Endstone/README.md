# VoiceCraft.Endstone

Endstone companion plugin for **VoiceCraft Server Mobile**.

Target runtime:

- Endstone API: **0.11.x**
- Validated MCSV Endstone: **0.11.10**
- Android companion: **VoiceCraft Server Mobile UI4.4** (`1.7.1-android-phase2-ui4.4`, version code `10`)
- Endstone plugin: **`0.2.6`**
- Render Relay: **`0.2.1`**
- Render Relay protocol: **`1`**
- VoiceCraft upstream protocol: **v1.7.1 unchanged**

## Architecture

```text
Minecraft Bedrock @ MCSV
        │
        ▼
Endstone VoiceCraft 0.2.6
        │ outbound WSS
        ▼
Primary Render Relay
        │
        ├──── failover ────> Backup Relay(s)
        │
        ▼ outbound WSS
VoiceCraft Server Mobile UI4.4
        │
        ▼
VoiceCraft v1.7.1 UDP runtime
```

Render carries Minecraft state, binding/control messages and snapshots only. Voice audio remains direct VoiceCraft/LiteNetLib UDP to Android.

## `/vc` player controls

The only public VoiceCraft command is:

```text
/vc
```

The menu is context-aware:

```text
Not bound  → Bind Microphone
Pending    → Cancel Pending Bind
Bound      → Disconnect / Unbind Microphone
```

The menu also includes:

- **Status** — bridge state, binding state and local player state.
- **Tracked Players (Admin)** — operator-only tracker view.

The old public commands remain intentionally unregistered:

```text
/vcbind
/vcunbind
/vcstatus
/vcdump
```

Their internal logic is still reused by the UI where appropriate.

## Initial Bind

For an unbound player join:

1. Endstone waits for a valid real player state.
2. Transitional BDS states such as `Y=32768` are ignored.
3. The Bind form opens automatically.
4. The player enters the one-use Binding Key shown by the VoiceCraft client.
5. Endstone sends the bind through Render to Android.
6. Android associates the VoiceCraft entity with that Minecraft player.

## Unexpected VoiceCraft disconnect / Auto Rebind

If an already-bound VoiceCraft client disappears unexpectedly while the Minecraft player remains online:

1. Android emits `voice_client_disconnected`.
2. Endstone removes the local bound state.
3. A disconnect warning is shown.
4. After 5 seconds, Endstone opens the Bind form again.

Stale entity events are ignored so an old disconnect cannot reopen the form after a successful new bind.

## Real Manual Unbind

For a bound player, `/vc` shows **Disconnect / Unbind Microphone**.

After confirmation:

```text
Endstone
  │
  ├─ checks Relay + Android peer are reachable
  ├─ sends unbind(requestId, entityId)
  ▼
Render Relay 0.2.1
  │
  ├─ validates requestId/entityId shape
  ├─ forwards only
  └─ NEVER caches/replays destructive unbind
  ▼
Android UI4.4
  │
  ├─ validates player and current entityId
  ├─ rejects stale requests
  ├─ disconnects the actual VoiceCraft NetPeer
  └─ returns unbind_result
  ▼
Endstone
  ├─ clears bound state
  └─ suppresses Auto Rebind
```

### Timeout / late-result safety

Endstone waits 20 seconds for `unbind_result`.

If confirmation times out:

- the `/vc` UI is released from `Disconnecting` so the player is not stuck forever;
- the timeout is treated as **unknown**, not as a confirmed failure;
- Endstone retains a bounded tombstone for that request;
- a late success can still reconcile the old entity safely;
- a late result for an old entity cannot clear a newer binding;
- if the player retried the same entity, a late successful first request can finalize the desired disconnect and clear the redundant pending retry.

## Multi-relay failover

The bridge supports one Primary URL plus zero or more backups.

```text
Primary
  -> Backup #1
  -> Backup #2
  -> ...
  -> Primary
```

Default behavior:

```text
reconnect_seconds = 5
max_attempts = 5
peer_timeout_seconds = 30
```

A relay is healthy only when both the WebSocket is connected and the Android peer is visible.

## In-game Relay alerts

Relay status transitions are written into a thread-safe queue by the networking thread and drained from the normal Endstone server thread.

Player chat messages use Bedrock `§` formatting and no emoji:

```text
Red     relay/voice control plane temporarily unavailable
Red     no Backup Relay configured after Primary retry exhaustion
Yellow  switching between Primary / Backup #N
Green   relay paired and service recovered
```

One outage session emits one initial outage warning instead of repeating it every 5-second reconnect attempt.

## Configuration

Example `config.toml`:

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
url = "wss://voicecraft-primary.onrender.com/bridge"
backup_urls = [
  "wss://voicecraft-backup1.onrender.com/bridge",
  "wss://voicecraft-backup2.onrender.com/bridge"
]
server_id = "mcsv-main"
secret = "THE_SAME_SECRET_USED_BY_ANDROID_AND_EVERY_RELAY"
reconnect_seconds = 5
max_attempts = 5
peer_timeout_seconds = 30
```

Rules:

- Primary `url` must use `ws://` or `wss://` and path `/bridge`.
- `server_id` must match Android.
- `secret` must match Android and every Render relay.
- Optional invalid backup URLs are ignored rather than invalidating a valid Primary.

## Installation

Install the wheel into the Endstone plugin environment:

```text
endstone_voicecraft-0.2.6-py3-none-any.whl
```

Remove older `endstone_voicecraft-*.whl` files before installing the new version.

The package entry point remains:

```text
voicecraft = endstone_voicecraft:VoiceCraftEndstone
```

## CI coverage

CI validates:

- Endstone 0.11.10 event annotations and plugin loader behavior;
- final exported class metadata is declared directly in `__dict__`;
- `/vc` is the only public VoiceCraft command;
- Bind/Auto Bind/Auto Rebind inheritance;
- `0.2.6` version and installed-wheel import;
- multi-relay ordering and Primary-only behavior;
- relay alert anti-spam state transitions;
- real protocol-1 `bind`, `unbind`, `unbind_result`, snapshot and reconnect forwarding;
- bad-secret rejection and Server-ID room isolation;
- wheel entrypoint/metadata;
- no `__pycache__` or `.pyc` in the wheel.

## Compatibility

Endstone 0.2.6 does not modify the VoiceCraft v1.7.1 audio protocol. Relay protocol remains `1`, and voice audio is never transported over the Render WebSocket bridge.
