from __future__ import annotations

from endstone import Player

from .menu import VoiceCraftEndstone as VoiceCraftEndstone026


MIC_OFF = "voicecraft:mic_off"
MIC_ON = "voicecraft:mic_on"
LEGACY_HOLD = "voicecraft:mic_hold"
LEGACY_TOGGLE = "voicecraft:mic_toggle"
MIC_IDS = {MIC_OFF, MIC_ON, LEGACY_HOLD, LEGACY_TOGGLE}


def _type_id(stack: object | None) -> str:
    if stack is None:
        return ""
    value = getattr(stack, "type", None)
    if isinstance(value, str):
        return value
    return str(getattr(value, "id", ""))


def _is_mic(type_id: str) -> bool:
    return type_id in MIC_IDS


class VoiceCraftEndstone(VoiceCraftEndstone026):
    """Endstone 0.2.7: integrated VoiceCraft Item Mic state bridge."""

    prefix = "VoiceCraftEndstone"
    version = "0.2.7"
    api_version = "0.11"
    description = "VoiceCraft Endstone player-state, binding, failover and Item Mic bridge"
    authors = ["SamSoSleepy"]

    def __init__(self) -> None:
        super().__init__()
        self._mic_state: dict[str, bool] = {}
        self._mic_source: dict[str, str] = {}

    def on_enable(self) -> None:
        super().on_enable()
        self.server.scheduler.run_task(self, self._item_mic_tick, delay=0, period=1)
        self.logger.info(
            "VoiceCraft Item Mic integrated: voicecraft:mic_on / voicecraft:mic_off; "
            "offhand always forces ON"
        )

    def on_disable(self) -> None:
        self._mic_state.clear()
        self._mic_source.clear()
        super().on_disable()

    def handle_player_quit(self, player: Player) -> None:
        player_key = self._player_key(player)
        self._mic_state.pop(player_key, None)
        self._mic_source.pop(player_key, None)
        super().handle_player_quit(player)

    def _state_message(self, state):
        message = super()._state_message(state)
        mic_on = self._mic_state.get(state.xuid or state.uuid)
        if mic_on is not None:
            message["micOn"] = mic_on
        return message

    def _send_full_snapshot(self) -> None:
        # The normal full snapshot now carries micOn because _state_message is
        # overridden above. This keeps reconnect/failover state synchronized
        # without introducing a new bridge protocol message type.
        super()._send_full_snapshot()

    def _item_mic_tick(self) -> None:
        online: set[str] = set()
        for player in self.server.online_players:
            player_key = self._player_key(player)
            online.add(player_key)
            try:
                mic_on, source = self._read_mic_state(player)
                previous = self._mic_state.get(player_key)
                source_changed = self._mic_source.get(player_key) != source
                if previous is None or previous != mic_on or source_changed:
                    self._mic_state[player_key] = mic_on
                    self._mic_source[player_key] = source
                    self._emit_mic_state(player, player_key, mic_on, source, previous)
            except Exception as exc:
                self.logger.warning(
                    f"ITEM MIC tick failed player={getattr(player, 'name', '?')}: "
                    f"{type(exc).__name__}: {exc}"
                )

        for stale_key in set(self._mic_state).difference(online):
            self._mic_state.pop(stale_key, None)
            self._mic_source.pop(stale_key, None)

    def _read_mic_state(self, player: Player) -> tuple[bool, str]:
        inventory = player.inventory
        off_id = _type_id(inventory.item_in_off_hand)
        # The BP intentionally treats any VoiceCraft Mic in the offhand as an
        # always-on override while preserving the underlying Toggle latch.
        if _is_mic(off_id):
            return True, "offhand"

        main_id = _type_id(inventory.item_in_main_hand)
        found_on = main_id == MIC_ON
        found_off = main_id == MIC_OFF

        for stack in inventory.contents:
            type_id = _type_id(stack)
            if type_id == MIC_ON:
                found_on = True
            elif type_id == MIC_OFF:
                found_off = True

        if found_on:
            return True, "item"
        if found_off:
            return False, "item"

        # If the addon/items are not present, preserve normal VoiceCraft
        # behaviour instead of leaving a player server-muted indefinitely.
        return True, "no-item"

    def _emit_mic_state(
        self,
        player: Player,
        player_key: str,
        mic_on: bool,
        source: str,
        previous: bool | None,
    ) -> None:
        state = self._states.get(player_key)
        queued = False
        if state is not None:
            queued = self._bridge_send(self._state_message(state))

        self.logger.info(
            f"ITEM MIC player={player.name} mic={'ON' if mic_on else 'OFF'} "
            f"source={source} queued={queued}"
        )

        # Avoid chat spam on the initial no-item compatibility state. When the
        # actual Mic item becomes active or changes state, show the player the
        # same concise status feedback as the standalone Item Mic plugin.
        if source == "no-item" and previous is None:
            return
        try:
            player.send_message(
                f"§{'a' if mic_on else 'c'}[VoiceCraft] MIC {'ON' if mic_on else 'OFF'}§r"
            )
        except Exception:
            pass
