from __future__ import annotations

import hashlib
import time
from typing import Any

from endstone import Player
from endstone.command import Command, CommandSender
from endstone.plugin import Plugin

from .listener import VoiceCraftListener
from .model import PlayerState


class VoiceCraftEndstone(Plugin):
    prefix = "VoiceCraftEndstone"
    version = "0.1.0"
    api_version = "0.11"
    description = "VoiceCraft Endstone player-state bridge diagnostics"
    authors = ["SamSoSleepy"]

    commands = {
        "vcbind": {
            "description": "Capture a VoiceCraft binding key for the current player",
            "usages": ["/vcbind <key: str>"],
            "permissions": ["voicecraft.command.bind"],
        },
        "vcunbind": {
            "description": "Clear the pending VoiceCraft binding key",
            "usages": ["/vcunbind"],
            "permissions": ["voicecraft.command.bind"],
        },
        "vcstatus": {
            "description": "Show VoiceCraft Endstone tracker status",
            "usages": ["/vcstatus"],
            "permissions": ["voicecraft.command.status"],
        },
        "vcdump": {
            "description": "Dump tracked VoiceCraft player states",
            "usages": ["/vcdump"],
            "permissions": ["voicecraft.command.dump"],
        },
    }

    permissions = {
        "voicecraft.command.bind": {
            "description": "Allow a player to capture/clear their VoiceCraft binding key.",
            "default": True,
        },
        "voicecraft.command.status": {
            "description": "Allow viewing VoiceCraft tracker status.",
            "default": True,
        },
        "voicecraft.command.dump": {
            "description": "Allow dumping all VoiceCraft player states.",
            "default": "op",
        },
    }

    def __init__(self) -> None:
        super().__init__()
        self._states: dict[str, PlayerState] = {}
        self._pending_bind_keys: dict[str, str] = {}
        self._last_movement_log: dict[str, float] = {}
        self._interval_ticks = 2
        self._position_epsilon = 0.05
        self._rotation_epsilon = 1.0
        self._log_position_changes = False
        self._heartbeat_ticks = 600
        self._heartbeat_accumulator = 0

    def on_enable(self) -> None:
        self.save_default_config()
        self._load_settings()
        self.register_events(VoiceCraftListener(self))
        self.server.scheduler.run_task(self, self._tracking_tick, delay=0, period=self._interval_ticks)

        self.logger.info(
            "VoiceCraft Endstone Phase 1 enabled: "
            f"interval={self._interval_ticks} ticks, Endstone API={self.api_version}, network_bridge=disabled"
        )
        self.logger.info("Commands ready: /vcbind /vcunbind /vcstatus /vcdump")

        for player in self.server.online_players:
            self.handle_player_join(player)

    def on_disable(self) -> None:
        try:
            self.server.scheduler.cancel_tasks(self)
        except Exception as exc:
            self.logger.warning(f"Could not cancel scheduler tasks cleanly: {type(exc).__name__}: {exc}")
        self._states.clear()
        self._pending_bind_keys.clear()
        self._last_movement_log.clear()
        self.logger.info("VoiceCraft Endstone Phase 1 disabled")

    def on_command(self, sender: CommandSender, command: Command, args: list[str]) -> bool:
        if command.name == "vcbind":
            return self._command_bind(sender, args)
        if command.name == "vcunbind":
            return self._command_unbind(sender)
        if command.name == "vcstatus":
            return self._command_status(sender)
        if command.name == "vcdump":
            return self._command_dump(sender)
        return False

    def _load_settings(self) -> None:
        tracking = self.config.get("tracking", {})
        binding = self.config.get("binding", {})

        self._interval_ticks = self._bounded_int(tracking.get("interval_ticks", 2), 1, 20, 2)
        self._position_epsilon = self._bounded_float(tracking.get("position_epsilon", 0.05), 0.001, 10.0, 0.05)
        self._rotation_epsilon = self._bounded_float(tracking.get("rotation_epsilon", 1.0), 0.01, 180.0, 1.0)
        self._log_position_changes = bool(tracking.get("log_position_changes", False))
        heartbeat_seconds = self._bounded_int(tracking.get("heartbeat_seconds", 30), 1, 3600, 30)
        self._heartbeat_ticks = heartbeat_seconds * 20

        self._min_key_length = self._bounded_int(binding.get("min_key_length", 4), 1, 1024, 4)
        self._max_key_length = self._bounded_int(binding.get("max_key_length", 128), self._min_key_length, 4096, 128)

    @staticmethod
    def _bounded_int(value: Any, minimum: int, maximum: int, fallback: int) -> int:
        try:
            value = int(value)
        except (TypeError, ValueError):
            return fallback
        return max(minimum, min(maximum, value))

    @staticmethod
    def _bounded_float(value: Any, minimum: float, maximum: float, fallback: float) -> float:
        try:
            value = float(value)
        except (TypeError, ValueError):
            return fallback
        return max(minimum, min(maximum, value))

    @staticmethod
    def _player_key(player: Player) -> str:
        xuid = str(player.xuid or "")
        return xuid if xuid else str(player.unique_id)

    @staticmethod
    def _snapshot(player: Player) -> PlayerState:
        location = player.location
        return PlayerState(
            name=str(player.name),
            xuid=str(player.xuid or ""),
            uuid=str(player.unique_id),
            dimension=str(player.dimension.name),
            x=float(location.x),
            y=float(location.y),
            z=float(location.z),
            yaw=float(location.yaw),
            pitch=float(location.pitch),
        )

    @staticmethod
    def _fingerprint(secret: str) -> str:
        return hashlib.sha256(secret.encode("utf-8")).hexdigest()[:10]

    def handle_player_join(self, player: Player) -> None:
        try:
            state = self._snapshot(player)
            key = self._player_key(player)
            self._states[key] = state
            self.logger.info(f"PLAYER JOIN {state.compact()}")
        except Exception as exc:
            self.logger.error(f"PLAYER JOIN snapshot failed for {player.name}: {type(exc).__name__}: {exc}")

    def handle_player_quit(self, player: Player) -> None:
        key = self._player_key(player)
        state = self._states.pop(key, None)
        self._pending_bind_keys.pop(key, None)
        self._last_movement_log.pop(key, None)
        if state is not None:
            self.logger.info(f"PLAYER QUIT name={state.name} xuid={state.xuid} uuid={state.uuid}")
        else:
            self.logger.info(f"PLAYER QUIT name={player.name} xuid={player.xuid}")

    def _tracking_tick(self) -> None:
        current_keys: set[str] = set()

        for player in self.server.online_players:
            try:
                key = self._player_key(player)
                current_keys.add(key)
                current = self._snapshot(player)
                previous = self._states.get(key)

                if previous is None:
                    self._states[key] = current
                    self.logger.info(f"TRACK DISCOVER {current.compact()}")
                    continue

                dimension_changed = current.dimension != previous.dimension
                position_changed = current.position_changed(previous, self._position_epsilon)
                rotation_changed = current.rotation_changed(previous, self._rotation_epsilon)

                if dimension_changed:
                    self.logger.info(
                        f"DIMENSION {current.name} xuid={current.xuid} {previous.dimension} -> {current.dimension} "
                        f"pos=({current.x:.2f},{current.y:.2f},{current.z:.2f})"
                    )

                if self._log_position_changes and (position_changed or rotation_changed):
                    now = time.monotonic()
                    if now - self._last_movement_log.get(key, 0.0) >= 1.0:
                        self._last_movement_log[key] = now
                        self.logger.info(f"MOVE {current.compact()}")

                if dimension_changed or position_changed or rotation_changed or current.name != previous.name:
                    self._states[key] = current
            except Exception as exc:
                self.logger.warning(f"TRACK ERROR player={getattr(player, 'name', '?')}: {type(exc).__name__}: {exc}")

        for stale_key in set(self._states).difference(current_keys):
            stale = self._states.pop(stale_key)
            self._pending_bind_keys.pop(stale_key, None)
            self._last_movement_log.pop(stale_key, None)
            self.logger.info(f"TRACK REMOVE name={stale.name} xuid={stale.xuid}")

        self._heartbeat_accumulator += self._interval_ticks
        if self._heartbeat_accumulator >= self._heartbeat_ticks:
            self._heartbeat_accumulator = 0
            pending = sum(1 for key in current_keys if key in self._pending_bind_keys)
            self.logger.info(
                f"HEARTBEAT online={len(current_keys)} tracked={len(self._states)} pending_bindings={pending} bridge=phase1-disabled"
            )

    def _command_bind(self, sender: CommandSender, args: list[str]) -> bool:
        if not isinstance(sender, Player):
            sender.send_error_message("/vcbind must be run by a player.")
            return False
        if len(args) != 1:
            sender.send_error_message("Usage: /vcbind <key>")
            return False

        key_value = args[0].strip()
        if not (self._min_key_length <= len(key_value) <= self._max_key_length):
            sender.send_error_message(
                f"Binding key length must be {self._min_key_length}-{self._max_key_length} characters."
            )
            return False

        player_key = self._player_key(sender)
        self._pending_bind_keys[player_key] = key_value
        fingerprint = self._fingerprint(key_value)
        self.logger.info(
            f"BIND CAPTURED player={sender.name} xuid={sender.xuid} key_fingerprint={fingerprint} bridge=phase1-disabled"
        )
        sender.send_message("VoiceCraft binding key captured safely in memory.")
        sender.send_message("Phase 1 tracker is working; network binding will be enabled in Phase 2.")
        return True

    def _command_unbind(self, sender: CommandSender) -> bool:
        if not isinstance(sender, Player):
            sender.send_error_message("/vcunbind must be run by a player.")
            return False
        removed = self._pending_bind_keys.pop(self._player_key(sender), None)
        if removed is None:
            sender.send_message("No pending VoiceCraft binding key was stored.")
        else:
            self.logger.info(f"BIND CLEARED player={sender.name} xuid={sender.xuid}")
            sender.send_message("Pending VoiceCraft binding key cleared.")
        return True

    def _command_status(self, sender: CommandSender) -> bool:
        online = len(self.server.online_players)
        sender.send_message(
            f"VoiceCraft Endstone v{self.version}: Phase 1 tracker active, bridge disabled, "
            f"online={online}, tracked={len(self._states)}, interval={self._interval_ticks} ticks"
        )
        if isinstance(sender, Player):
            key = self._player_key(sender)
            state = self._states.get(key)
            if state is None:
                try:
                    state = self._snapshot(sender)
                except Exception:
                    state = None
            if state is not None:
                sender.send_message(
                    f"You: dim={state.dimension} pos=({state.x:.2f}, {state.y:.2f}, {state.z:.2f}) "
                    f"yaw={state.yaw:.1f} pitch={state.pitch:.1f}"
                )
            sender.send_message(
                "Pending binding: " + ("yes" if key in self._pending_bind_keys else "no")
            )
        return True

    def _command_dump(self, sender: CommandSender) -> bool:
        if not self._states:
            sender.send_message("VoiceCraft tracker has no player states.")
            return True
        sender.send_message(f"VoiceCraft tracked states ({len(self._states)}):")
        for state in sorted(self._states.values(), key=lambda item: item.name.lower()):
            sender.send_message(state.compact())
        return True
