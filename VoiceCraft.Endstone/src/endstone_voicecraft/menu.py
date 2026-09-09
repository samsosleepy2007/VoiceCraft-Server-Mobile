from __future__ import annotations

from endstone import Player
from endstone.command import Command, CommandSender
from endstone.form import ActionForm, ModalForm, TextInput

from .auto_rebind import VoiceCraftEndstone as VoiceCraftEndstone024


class VoiceCraftEndstone(VoiceCraftEndstone024):
    """Endstone 0.2.5: one /vc command with an in-game control menu."""

    # Keep all plugin metadata on the final exported class. Endstone 0.11's
    # Python loader builds PluginDescription from cls.__dict__, so inherited
    # class attributes such as commands/permissions are not sufficient.
    prefix = "VoiceCraftEndstone"
    version = "0.2.5"
    api_version = "0.11"
    description = "VoiceCraft Endstone player-state, binding and in-game UI bridge"
    authors = ["SamSoSleepy"]

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
            "description": "Allow binding or cancelling a pending VoiceCraft binding request.",
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
        self.logger.info("VoiceCraft UI + multi-relay failover ready: /vc")

    def on_command(self, sender: CommandSender, command: Command, args: list[str]) -> bool:
        if command.name != "vc":
            return False
        if not isinstance(sender, Player):
            sender.send_error_message("/vc must be run by a player because it opens an in-game form.")
            return False
        self._open_vc_menu(sender)
        return True

    def _open_vc_menu(self, player: Player) -> None:
        player_key = self._player_key(player)
        if player_key in self._bound_players:
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
        form.add_button("Bind Microphone", on_click=self._menu_bind)
        form.add_button("Cancel Pending Bind", on_click=self._menu_unbind)
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

    def _on_menu_bind_submit(self, player: Player, response: object) -> None:
        binding_key = self._extract_binding_key(response)
        if not binding_key:
            player.send_error_message("กรุณากรอก Binding Key ก่อนกด Bind")
            return
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

        if key in self._bound_players:
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
