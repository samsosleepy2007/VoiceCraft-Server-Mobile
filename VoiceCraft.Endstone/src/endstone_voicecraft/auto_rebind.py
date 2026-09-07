from __future__ import annotations

from typing import Any

from endstone import Player

from .auto_bind import VoiceCraftEndstone as VoiceCraftEndstone022


class VoiceCraftEndstone(VoiceCraftEndstone022):
    """Endstone 0.2.3: automatic rebind UX after a VoiceCraft client disconnects."""

    version = "0.2.3"
    _RECONNECT_FORM_DELAY_TICKS = 100  # 5 seconds at 20 TPS

    def __init__(self) -> None:
        super().__init__()
        self._rebind_waiting: set[str] = set()

    def on_disable(self) -> None:
        self._rebind_waiting.clear()
        super().on_disable()

    def handle_player_quit(self, player: Player) -> None:
        self._rebind_waiting.discard(self._player_key(player))
        super().handle_player_quit(player)

    def _handle_bind_result(self, message: dict[str, Any]) -> None:
        player_key = str(message.get("xuid", "") or message.get("uuid", ""))
        success = bool(message.get("success", False))
        super()._handle_bind_result(message)
        if success and player_key:
            self._rebind_waiting.discard(player_key)

    def _drain_bridge_messages(self) -> None:
        if self._bridge is None:
            return
        for message in self._bridge.drain_incoming():
            kind = str(message.get("type", ""))
            if kind == "request_snapshot":
                self._send_full_snapshot()
            elif kind == "bind_result":
                self._handle_bind_result(message)
            elif kind == "voice_client_disconnected":
                self._handle_voice_client_disconnected(message)

    def _handle_voice_client_disconnected(self, message: dict[str, Any]) -> None:
        player_key = str(message.get("xuid", "") or message.get("uuid", ""))
        if not player_key or player_key in self._rebind_waiting:
            return

        player = self._find_online_player(player_key)
        if player is None:
            return

        self._bound_players.discard(player_key)
        self._pending_bind_keys.pop(player_key, None)
        self._pending_bind_requests.pop(player_key, None)
        self._auto_bind_shown.discard(player_key)
        self._rebind_waiting.add(player_key)

        player.send_error_message(
            "VoiceCraft หลุด! จะดำเนินการให้ใส่ Binding Key ใหม่อีกครั้งใน 5 วินาที..."
        )
        self.logger.info(
            f"VOICE DISCONNECT player={player.name} xuid={player.xuid}; rebind form scheduled in 5s"
        )

        def callback() -> None:
            self._rebind_waiting.discard(player_key)
            if player_key in self._bound_players or player_key in self._pending_bind_keys:
                return
            if self._find_online_player(player_key) is None:
                return
            self._auto_bind_shown.discard(player_key)
            self._show_auto_bind_form(player_key)

        try:
            self.server.scheduler.run_task(
                self,
                callback,
                delay=self._RECONNECT_FORM_DELAY_TICKS,
            )
        except Exception as exc:
            self._rebind_waiting.discard(player_key)
            self.logger.warning(
                f"REBIND FORM schedule failed player_key={player_key[:12]}: {type(exc).__name__}: {exc}"
            )
            self._schedule_auto_bind_form(player_key, self._RECONNECT_FORM_DELAY_TICKS)
