from __future__ import annotations

from typing import Any

from endstone import Player
from endstone.command import Command, CommandSender
from endstone.form import ActionForm, ModalForm, TextInput

from .auto_rebind import VoiceCraftEndstone as VoiceCraftEndstone025


class VoiceCraftEndstone(VoiceCraftEndstone025):
    """Endstone 0.2.6: /vc controls, real unbind and relay alerts."""

    prefix = "VoiceCraftEndstone"
    version = "0.2.6"
    api_version = "0.11"
    description = "VoiceCraft Endstone player-state, binding and in-game UI bridge"
    authors = ["SamSoSleepy"]

    _CHAT_PREFIX = "§6§l[VoiceCraft Server]§r "

    commands = {
        "vc": {
            "description": "Open the VoiceCraft control menu",
            "usages": ["/vc"],
            "permissions": ["voicecraft.command.menu"],
        },
    }

    permissions = {
        "voicecraft.command.menu": {
            "description": "Allow a player to open the VoiceCraft control menu.",
            "default": True,
        },
        "voicecraft.command.bind": {
            "description": "Allow binding, pending-bind cancellation, and manual VoiceCraft disconnect.",
            "default": True,
        },
        "voicecraft.command.status": {
            "description": "Allow viewing VoiceCraft bridge and player status.",
            "default": True,
        },
        "voicecraft.command.dump": {
            "description": "Allow viewing all tracked VoiceCraft player states.",
            "default": "op",
        },
    }

    def on_enable(self) -> None:
        super().on_enable()
        self.logger.info("VoiceCraft UI + real unbind + multi-relay alerts ready: /vc")

    def on_command(self, sender: CommandSender, command: Command, args: list[str]) -> bool:
        if command.name != "vc":
            return False
        if not isinstance(sender, Player):
            sender.send_error_message("/vc must be run by a player because it opens an in-game form.")
            return False
        self._open_vc_menu(sender)
        return True

    def _tracking_tick(self) -> None:
        super()._tracking_tick()
        bridge = self._bridge
        if bridge is None or not hasattr(bridge, "drain_status_events"):
            return
        for event in bridge.drain_status_events():
            self._handle_relay_status_event(event)

    def _broadcast_status(self, color: str, message: str) -> None:
        text = f"{self._CHAT_PREFIX}{color}{message}§r"
        for player in self.server.online_players:
            try:
                player.send_message(text)
            except Exception as exc:
                self.logger.warning(
                    f"RELAY ALERT send failed player={getattr(player, 'name', '?')}: {type(exc).__name__}: {exc}"
                )

    def _handle_relay_status_event(self, event: dict[str, Any]) -> None:
        kind = str(event.get("type", ""))
        if kind == "relay_lost":
            self._broadcast_status(
                "§c",
                "เซิฟเวอร์ไมค์ตอนนี้หยุดทำงานชั่วคราว กำลังพยายามเชื่อมต่อใหม่อีกครั้ง โปรดรอสักครู่...",
            )
            return

        if kind == "no_backup":
            self._broadcast_status(
                "§c",
                "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ไมค์หลักได้ และไม่พบเซิร์ฟเวอร์สำรอง แอดมินกรุณาตรวจสอบระบบโดยด่วน!",
            )
            return

        if kind == "failover_started":
            from_index = int(event.get("from_index", 0))
            to_index = int(event.get("to_index", 0))
            if to_index == 0:
                message = "เซิฟเวอร์สำรองทั้งหมดไม่สามารถเชื่อมต่อได้ กำลังกลับไปลองเซิฟเวอร์หลักอีกครั้ง โปรดรอสักครู่..."
            elif from_index == 0:
                message = f"ไม่สามารถเชื่อมต่อได้ กำลังย้ายไปเซิฟเวอร์สำรอง #{to_index} โปรดรอสักครู่..."
            else:
                message = (
                    f"เซิฟเวอร์สำรอง #{from_index} ไม่สามารถเชื่อมต่อได้ "
                    f"กำลังย้ายไปเซิฟเวอร์สำรอง #{to_index} โปรดรอสักครู่..."
                )
            self._broadcast_status("§e", message)
            return

        if kind == "relay_paired":
            relay_index = int(event.get("relay_index", 0))
            recovered = bool(event.get("recovered", False))
            if relay_index > 0:
                self._broadcast_status("§a", f"เชื่อมต่อเซิฟเวอร์สำรอง #{relay_index} สำเร็จ!")
            elif recovered:
                self._broadcast_status("§a", "เชื่อมต่อเซิฟเวอร์หลักสำเร็จ! ระบบกลับมาทำงานตามปกติแล้ว")

    def _open_vc_menu(self, player: Player) -> None:
        player_key = self._player_key(player)
        disconnecting = player_key in self._pending_unbind_requests
        if disconnecting:
            binding_state = "Disconnecting"
        elif player_key in self._bound_players:
            binding_state = "Bound"
        elif player_key in self._pending_bind_keys:
            binding_state = "Pending"
        else:
            binding_state = "Not bound"

        form = ActionForm(
            title="VoiceCraft",
            content=(
                f"VoiceCraft Endstone v{self.version}\n"
                f"Bridge: {self._bridge_status_text()}\n"
                f"Microphone: {binding_state}\n\n"
                "เลือกเมนูที่ต้องการ"
            ),
        )
        if disconnecting:
            pass
        elif player_key in self._bound_players:
            form.add_button("Disconnect / Unbind Microphone", on_click=self._menu_disconnect)
        elif player_key in self._pending_bind_keys:
            form.add_button("Cancel Pending Bind", on_click=self._menu_unbind)
        else:
            form.add_button("Bind Microphone", on_click=self._menu_bind)
        form.add_button("Status", on_click=self._menu_status)
        form.add_button("Tracked Players (Admin)", on_click=self._menu_dump)
        player.send_form(form)

    def _menu_bind(self, player: Player) -> None:
        if not player.has_permission("voicecraft.command.bind"):
            player.send_error_message("You do not have permission to bind VoiceCraft.")
            return

        player_key = self._player_key(player)
        if player_key in self._bound_players:
            player.send_message("VoiceCraft is already bound to your current voice client.")
            return
        if player_key in self._pending_bind_keys:
            player.send_message("A VoiceCraft binding request is already pending.")
            return
        if player_key in self._pending_unbind_requests:
            player.send_message("VoiceCraft disconnect is still in progress.")
            return

        form = ModalForm(
            title="VoiceCraft - Bind Microphone",
            controls=[
                TextInput(
                    label="กรอก Binding Key ที่แสดงใน VoiceCraft Client",
                    placeholder="Binding Key เช่น Ab3X9",
                )
            ],
            submit_button="Bind",
            on_submit=self._on_menu_bind_submit,
            on_close=self._on_menu_bind_close,
        )
        player.send_form(form)

    def _menu_disconnect(self, player: Player) -> None:
        if not player.has_permission("voicecraft.command.bind"):
            player.send_error_message("You do not have permission to disconnect VoiceCraft.")
            return
        player_key = self._player_key(player)
        if player_key not in self._bound_players:
            player.send_message("VoiceCraft is not currently bound.")
            return

        form = ActionForm(
            title="VoiceCraft - Disconnect",
            content=(
                "การดำเนินการนี้จะตัด VoiceCraft Client ของคุณออกจาก VoiceCraft Server จริง\n"
                "และจะไม่เปิด Auto Rebind หลังจากตัดสำเร็จ\n\n"
                "คุณสามารถใช้ /vc เพื่อ Bind ใหม่ภายหลังได้"
            ),
        )
        form.add_button("DISCONNECT", on_click=self._menu_disconnect_confirm)
        form.add_button("BACK", on_click=self._open_vc_menu)
        player.send_form(form)

    def _menu_disconnect_confirm(self, player: Player) -> None:
        self._request_manual_unbind(player)

    def _on_menu_bind_submit(self, player: Player, response: object) -> None:
        binding_key = self._extract_binding_key(response)
        if not binding_key:
            player.send_error_message("กรุณากรอก Binding Key ก่อนกด Bind")
            return
        self._auto_bind_shown.discard(self._player_key(player))
        self._command_bind(player, [binding_key])

    def _on_menu_bind_close(self, player: Player) -> None:
        player_key = self._player_key(player)
        if player_key in self._bound_players or player_key in self._pending_bind_keys:
            return
        player.send_message("ยังไม่ได้ Bind สามารถใช้ /vc เพื่อเปิดเมนู VoiceCraft ได้ทุกเมื่อ")

    def _on_auto_bind_close(self, player: Player) -> None:
        player_key = self._player_key(player)
        if player_key in self._bound_players or player_key in self._pending_bind_keys:
            return
        player.send_error_message(
            "คุณยังไม่ได้ Bind จึงไม่สามารถใช้ไมค์ได้ สามารถใช้ /vc เพื่อเปิดเมนู VoiceCraft และ Bind ภายหลังได้"
        )
        self.logger.info(f"BIND FORM closed unbound player={player.name} xuid={player.xuid}")

    def _menu_unbind(self, player: Player) -> None:
        if not player.has_permission("voicecraft.command.bind"):
            player.send_error_message("You do not have permission to change VoiceCraft binding state.")
            return
        self._command_unbind(player)

    def _menu_status(self, player: Player) -> None:
        if not player.has_permission("voicecraft.command.status"):
            player.send_error_message("You do not have permission to view VoiceCraft status.")
            return

        key = self._player_key(player)
        state = self._states.get(key)
        if state is None:
            try:
                candidate = self._snapshot(player)
                state = candidate if self._valid_state(candidate) else None
            except Exception:
                state = None

        if key in self._pending_unbind_requests:
            binding_state = "Disconnecting"
        elif key in self._bound_players:
            binding_state = "Bound"
        elif key in self._pending_bind_keys:
            binding_state = "Pending"
        else:
            binding_state = "Not bound"

        lines = [
            f"VoiceCraft Endstone v{self.version}",
            f"Bridge: {self._bridge_status_text()}",
            f"Online: {len(self.server.online_players)}",
            f"Tracked: {len(self._states)}",
            f"Binding: {binding_state}",
        ]
        if state is not None:
            lines.extend(
                [
                    f"Dimension: {state.dimension}",
                    f"Position: {state.x:.2f}, {state.y:.2f}, {state.z:.2f}",
                    f"Yaw/Pitch: {state.yaw:.1f} / {state.pitch:.1f}",
                ]
            )

        form = ActionForm(title="VoiceCraft Status", content="\n".join(lines))
        form.add_button("Back", on_click=self._open_vc_menu)
        player.send_form(form)

    def _menu_dump(self, player: Player) -> None:
        if not player.has_permission("voicecraft.command.dump"):
            player.send_error_message("Tracked Players is available to server operators only.")
            return

        if not self._states:
            content = "VoiceCraft tracker has no valid player states."
        else:
            states = sorted(self._states.values(), key=lambda item: item.name.lower())
            content = f"Tracked players: {len(states)}\n\n" + "\n".join(
                state.compact() for state in states
            )

        form = ActionForm(title="VoiceCraft Tracked Players", content=content)
        form.add_button("Back", on_click=self._open_vc_menu)
        player.send_form(form)
