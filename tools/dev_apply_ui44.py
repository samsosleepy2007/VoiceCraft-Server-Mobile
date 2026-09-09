from __future__ import annotations

from pathlib import Path


def write(path: str, content: str) -> None:
    Path(path).write_text(content, encoding="utf-8")


def replace_exact(path: str, old: str, new: str, expected: int = 1) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    found = text.count(old)
    if found != expected:
        raise SystemExit(f"{path}: expected {expected} occurrence(s), found {found}: {old[:120]!r}")
    p.write_text(text.replace(old, new), encoding="utf-8")


def replace_all(path: str, old: str, new: str, minimum: int = 1) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    found = text.count(old)
    if found < minimum:
        raise SystemExit(f"{path}: expected at least {minimum} occurrence(s), found {found}: {old!r}")
    p.write_text(text.replace(old, new), encoding="utf-8")


write(
    "VoiceCraft.Endstone/src/endstone_voicecraft/failover.py",
    '''from __future__ import annotations

import asyncio
import json
import queue
from typing import Any, Iterable

import aiohttp

from .bridge import EndstoneRelayClient


class MultiRelayEndstoneClient(EndstoneRelayClient):
    """Protocol-1 Endstone relay client with ordered multi-relay failover.

    The current relay is retried up to ``max_attempts`` times. After that the
    client advances to the next configured relay, wrapping back to Primary.
    A relay is considered healthy only after the Android peer is visible.

    Network-thread status transitions are copied into a thread-safe local
    queue. The Endstone plugin drains that queue on the game/server thread so
    this class never touches Player or Server objects from its WSS thread.
    """

    def __init__(
        self,
        logger: Any,
        urls: Iterable[str],
        server_id: str,
        secret: str,
        reconnect_seconds: float = 5.0,
        max_queue: int = 2048,
        plugin_version: str = "0.2.6",
        max_attempts: int = 5,
        peer_timeout_seconds: float = 30.0,
    ) -> None:
        cleaned: list[str] = []
        for raw in urls:
            value = str(raw or "").strip()
            if value and value not in cleaned:
                cleaned.append(value)
        if not cleaned:
            raise ValueError("at least one relay URL is required")

        super().__init__(
            logger,
            cleaned[0],
            server_id,
            secret,
            reconnect_seconds,
            max_queue,
        )
        self._urls = tuple(cleaned)
        self._plugin_version = str(plugin_version or "0.2.6")
        self._max_attempts = max(1, int(max_attempts))
        self._peer_timeout_seconds = max(5.0, float(peer_timeout_seconds))
        self._active_index = 0
        self._attempts_on_active = 0
        self._ever_connected = False
        self._status_events: queue.Queue[dict[str, Any]] = queue.Queue(maxsize=128)
        self._outage_active = False
        self._no_backup_announced = False
        self._paired_relay_index: int | None = None

    @property
    def relay_urls(self) -> tuple[str, ...]:
        return self._urls

    @property
    def active_relay_index(self) -> int:
        return self._active_index

    @property
    def active_relay_name(self) -> str:
        return "Primary" if self._active_index == 0 else f"Backup #{self._active_index}"

    @property
    def attempts_on_active(self) -> int:
        return self._attempts_on_active

    def drain_status_events(self, limit: int = 64) -> list[dict[str, Any]]:
        items: list[dict[str, Any]] = []
        for _ in range(max(1, int(limit))):
            try:
                items.append(self._status_events.get_nowait())
            except queue.Empty:
                break
        return items

    def _emit_status(self, kind: str, **values: Any) -> None:
        payload = {"type": str(kind), **values}
        try:
            self._status_events.put_nowait(payload)
        except queue.Full:
            try:
                self._status_events.get_nowait()
            except queue.Empty:
                pass
            try:
                self._status_events.put_nowait(payload)
            except queue.Full:
                pass

    def _begin_outage_once(self, relay_name: str) -> None:
        if self._outage_active:
            return
        self._outage_active = True
        self._no_backup_announced = False
        self._paired_relay_index = None
        self._emit_status(
            "relay_lost",
            relay_index=self._active_index,
            relay_name=relay_name,
            backup_count=max(0, len(self._urls) - 1),
        )

    def _mark_paired(self) -> None:
        if self._paired_relay_index == self._active_index and not self._outage_active:
            self._attempts_on_active = 0
            return
        recovered = self._outage_active
        self._paired_relay_index = self._active_index
        self._attempts_on_active = 0
        self._emit_status(
            "relay_paired",
            relay_index=self._active_index,
            relay_name=self.active_relay_name,
            recovered=recovered,
        )
        self._outage_active = False
        self._no_backup_announced = False

    def _advance_relay(self) -> None:
        if len(self._urls) <= 1:
            self._attempts_on_active = 0
            if not self._no_backup_announced:
                self._no_backup_announced = True
                self._emit_status(
                    "no_backup",
                    relay_index=0,
                    relay_name="Primary",
                )
            return

        previous_index = self._active_index
        previous_name = self.active_relay_name
        self._active_index = (self._active_index + 1) % len(self._urls)
        self._url = self._urls[self._active_index]
        self._attempts_on_active = 0
        self._paired_relay_index = None
        self._emit_status(
            "failover_started",
            from_index=previous_index,
            from_name=previous_name,
            to_index=self._active_index,
            to_name=self.active_relay_name,
        )
        self._logger.warning(
            f"BRIDGE FAILOVER {previous_name} -> {self.active_relay_name} "
            f"relay={self._safe_endpoint(self._url)}"
        )

    async def _run(self) -> None:
        timeout = aiohttp.ClientTimeout(total=None, sock_connect=15, sock_read=None)
        while not self._stop.is_set():
            self._url = self._urls[self._active_index]
            endpoint_name = self.active_relay_name
            try:
                if self._attempts_on_active:
                    self._logger.info(
                        f"BRIDGE {endpoint_name} attempt={self._attempts_on_active + 1}/{self._max_attempts} "
                        f"relay={self._safe_endpoint(self._url)} server_id={self._server_id}"
                    )

                async with aiohttp.ClientSession(timeout=timeout) as session:
                    async with session.ws_connect(
                        self._url,
                        heartbeat=20.0,
                        receive_timeout=60.0,
                        max_msg_size=65536,
                    ) as ws:
                        await ws.send_json(
                            {
                                "type": "hello",
                                "role": "endstone",
                                "serverId": self._server_id,
                                "secret": self._secret,
                                "protocol": 1,
                                "pluginVersion": self._plugin_version,
                            }
                        )

                        hello = await ws.receive(timeout=10.0)
                        if hello.type != aiohttp.WSMsgType.TEXT:
                            raise RuntimeError(
                                f"Relay did not return hello_ok (message_type={hello.type}, close_code={ws.close_code})"
                            )
                        hello_data = json.loads(hello.data)
                        if hello_data.get("type") != "hello_ok":
                            raise RuntimeError("Relay rejected hello")

                        was_reconnect = self._ever_connected
                        self._record_error("")
                        self._set_state(True, False)
                        self._ever_connected = True
                        self._logger.info(
                            f"BRIDGE {'reconnected' if was_reconnect else 'connected'} "
                            f"via {endpoint_name} relay={self._safe_endpoint(self._url)} server_id={self._server_id}"
                        )

                        sender = asyncio.create_task(self._sender_loop(ws))
                        receiver = asyncio.create_task(self._receiver_loop(ws))
                        peer_watch = asyncio.create_task(self._peer_watchdog(ws))
                        tasks = {sender, receiver, peer_watch}
                        try:
                            done, _ = await asyncio.wait(
                                tasks,
                                return_when=asyncio.FIRST_COMPLETED,
                            )
                            for task in done:
                                await task
                        finally:
                            for task in tasks:
                                task.cancel()
                            await asyncio.gather(*tasks, return_exceptions=True)

                        if not self._stop.is_set():
                            detail = f"close_code={ws.close_code}"
                            ws_error = ws.exception()
                            if ws_error is not None:
                                detail += f" error={type(ws_error).__name__}: {ws_error}"
                            raise RuntimeError(f"relay websocket closed ({detail})")
            except asyncio.CancelledError:
                raise
            except Exception as exc:
                self._begin_outage_once(endpoint_name)
                self._attempts_on_active += 1
                self._record_error(f"{type(exc).__name__}: {exc}")
                self._logger.warning(
                    f"BRIDGE {endpoint_name} failed {self._attempts_on_active}/{self._max_attempts}: "
                    f"{type(exc).__name__}: {exc}; retrying in {self._reconnect_seconds:.0f}s"
                )
                if self._attempts_on_active >= self._max_attempts:
                    self._advance_relay()
            finally:
                self._set_state(False, False)

            await self._sleep_with_stop(self._reconnect_seconds)

    async def _receiver_loop(self, ws: aiohttp.ClientWebSocketResponse) -> None:
        async for msg in ws:
            if self._stop.is_set():
                return
            if msg.type == aiohttp.WSMsgType.TEXT:
                self._handle_incoming_text(msg.data)
                if self.android_connected:
                    self._mark_paired()
            elif msg.type in (
                aiohttp.WSMsgType.CLOSE,
                aiohttp.WSMsgType.CLOSED,
                aiohttp.WSMsgType.ERROR,
            ):
                return

    async def _peer_watchdog(self, ws: aiohttp.ClientWebSocketResponse) -> None:
        missing_since: float | None = None
        loop = asyncio.get_running_loop()
        while not self._stop.is_set() and not ws.closed:
            await asyncio.sleep(2.0)
            if self.android_connected:
                missing_since = None
                self._mark_paired()
                continue
            now = loop.time()
            if missing_since is None:
                missing_since = now
            elif now - missing_since >= self._peer_timeout_seconds:
                raise RuntimeError(
                    f"Android peer not visible on {self.active_relay_name} for {self._peer_timeout_seconds:.0f}s"
                )
''',
)

write(
    "VoiceCraft.Endstone/src/endstone_voicecraft/auto_rebind.py",
    '''from __future__ import annotations

import uuid
from typing import Any

from endstone import Player

from .auto_bind import VoiceCraftEndstone as VoiceCraftEndstone022


class VoiceCraftEndstone(VoiceCraftEndstone022):
    """Endstone 0.2.6: automatic rebind plus safe manual disconnect."""

    version = "0.2.6"
    _RECONNECT_FORM_DELAY_TICKS = 100  # 5 seconds at 20 TPS

    def __init__(self) -> None:
        super().__init__()
        self._rebind_waiting: set[str] = set()
        self._bound_entity_by_player: dict[str, int] = {}
        self._pending_unbind_requests: dict[str, str] = {}
        self._pending_unbind_entities: dict[str, int] = {}
        self._manual_unbound_entities: set[int] = set()

    def on_disable(self) -> None:
        self._rebind_waiting.clear()
        self._bound_entity_by_player.clear()
        self._pending_unbind_requests.clear()
        self._pending_unbind_entities.clear()
        self._manual_unbound_entities.clear()
        super().on_disable()

    def handle_player_quit(self, player: Player) -> None:
        player_key = self._player_key(player)
        self._rebind_waiting.discard(player_key)
        self._bound_entity_by_player.pop(player_key, None)
        self._pending_unbind_requests.pop(player_key, None)
        self._pending_unbind_entities.pop(player_key, None)
        super().handle_player_quit(player)

    def _handle_bind_result(self, message: dict[str, Any]) -> None:
        player_key = str(message.get("xuid", "") or message.get("uuid", ""))
        success = bool(message.get("success", False))
        super()._handle_bind_result(message)

        if not success or not player_key:
            return

        self._rebind_waiting.discard(player_key)
        self._pending_unbind_requests.pop(player_key, None)
        self._pending_unbind_entities.pop(player_key, None)
        try:
            entity_id = int(message.get("entityId"))
        except (TypeError, ValueError):
            entity_id = None
        if entity_id is not None:
            self._bound_entity_by_player[player_key] = entity_id

    def _request_manual_unbind(self, player: Player) -> bool:
        player_key = self._player_key(player)
        if player_key not in self._bound_players:
            player.send_message("VoiceCraft is not currently bound.")
            return False
        if player_key in self._pending_unbind_requests:
            player.send_message("VoiceCraft disconnect is already in progress.")
            return False

        entity_id = self._bound_entity_by_player.get(player_key)
        if entity_id is None:
            player.send_error_message("VoiceCraft bound entity is unavailable. Open /vc Status and try again.")
            return False
        if self._bridge is None or not self._bridge.connected or not self._bridge.android_connected:
            player.send_error_message(
                "VoiceCraft mobile server is not reachable right now. Disconnect was not queued; try again after the bridge reconnects."
            )
            return False

        request_id = uuid.uuid4().hex
        queued = self._bridge_send(
            {
                "type": "unbind",
                "requestId": request_id,
                "name": str(player.name),
                "xuid": str(player.xuid or ""),
                "uuid": str(player.unique_id),
                "entityId": entity_id,
            }
        )
        if not queued:
            player.send_error_message("VoiceCraft bridge queue is unavailable; disconnect was not sent.")
            return False

        self._pending_unbind_requests[player_key] = request_id
        self._pending_unbind_entities[player_key] = entity_id
        self._rebind_waiting.discard(player_key)
        player.send_message("VoiceCraft: disconnect request sent to the mobile server.")
        self.logger.info(
            f"UNBIND requested player={player.name} xuid={player.xuid} entity={entity_id} request={request_id[:8]}"
        )
        return True

    def _drain_bridge_messages(self) -> None:
        if self._bridge is None:
            return
        for message in self._bridge.drain_incoming():
            kind = str(message.get("type", ""))
            if kind == "request_snapshot":
                self._send_full_snapshot()
            elif kind == "bind_result":
                self._handle_bind_result(message)
            elif kind == "unbind_result":
                self._handle_unbind_result(message)
            elif kind == "voice_client_disconnected":
                self._handle_voice_client_disconnected(message)

    def _handle_unbind_result(self, message: dict[str, Any]) -> None:
        player_key = str(message.get("xuid", "") or message.get("uuid", ""))
        request_id = str(message.get("requestId", ""))
        if not player_key or self._pending_unbind_requests.get(player_key) != request_id:
            self.logger.info(
                f"UNBIND stale result ignored player_key={player_key[:12] or '?'} request={request_id[:8] or '?'}"
            )
            return

        entity_id = self._pending_unbind_entities.pop(player_key, None)
        self._pending_unbind_requests.pop(player_key, None)
        success = bool(message.get("success", False))
        reason = str(message.get("reason", ""))[:160]
        player = self._find_online_player(player_key)

        if not success:
            if player is not None:
                player.send_error_message(f"VoiceCraft disconnect failed: {reason or 'mobile server rejected the request'}")
            self.logger.warning(
                f"UNBIND failed player_key={player_key[:12]} entity={entity_id} request={request_id[:8]} reason={reason or '-'}"
            )
            return

        if entity_id is not None:
            self._manual_unbound_entities.add(entity_id)
            if len(self._manual_unbound_entities) > 256:
                self._manual_unbound_entities.pop()
        self._bound_entity_by_player.pop(player_key, None)
        self._bound_players.discard(player_key)
        self._pending_bind_keys.pop(player_key, None)
        self._pending_bind_requests.pop(player_key, None)
        self._rebind_waiting.discard(player_key)
        self._auto_bind_scheduled.discard(player_key)
        # Manual unbind means stay unbound until the player intentionally opens
        # /vc and binds again. This suppresses the automatic join/rebind form.
        self._auto_bind_shown.add(player_key)

        if player is not None:
            player.send_message("VoiceCraft: disconnected successfully. Use /vc when you want to bind again.")
        self.logger.info(
            f"UNBIND success player_key={player_key[:12]} entity={entity_id} request={request_id[:8]}; auto-rebind suppressed"
        )

    def _handle_voice_client_disconnected(self, message: dict[str, Any]) -> None:
        player_key = str(message.get("xuid", "") or message.get("uuid", ""))
        if not player_key or player_key in self._rebind_waiting:
            return

        try:
            disconnected_entity = int(message.get("entityId"))
        except (TypeError, ValueError):
            disconnected_entity = None

        pending_manual_entity = self._pending_unbind_entities.get(player_key)
        if disconnected_entity is not None and pending_manual_entity == disconnected_entity:
            self.logger.info(
                f"VOICE DISCONNECT manual-unbind race suppressed player_key={player_key[:12]} entity={disconnected_entity}"
            )
            return
        if disconnected_entity is not None and disconnected_entity in self._manual_unbound_entities:
            self._manual_unbound_entities.discard(disconnected_entity)
            self.logger.info(
                f"VOICE DISCONNECT post-unbind stale event suppressed player_key={player_key[:12]} entity={disconnected_entity}"
            )
            return

        expected_entity = self._bound_entity_by_player.get(player_key)
        if (
            expected_entity is not None
            and disconnected_entity is not None
            and disconnected_entity != expected_entity
        ):
            self.logger.info(
                f"VOICE DISCONNECT stale event ignored player_key={player_key[:12]} "
                f"entity={disconnected_entity} current_entity={expected_entity}"
            )
            return

        player = self._find_online_player(player_key)
        if player is None:
            return

        self._bound_entity_by_player.pop(player_key, None)
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
''',
)

write(
    "VoiceCraft.Endstone/src/endstone_voicecraft/menu.py",
    '''from __future__ import annotations

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
''',
)

replace_all("VoiceCraft.Endstone/pyproject.toml", 'version = "0.2.5"', 'version = "0.2.6"')
replace_all("VoiceCraft.Endstone/README.md", "0.2.5", "0.2.6")
replace_all("VoiceCraft.Endstone/README.md", "UI4.3", "UI4.4")

replace_exact(
    "VoiceCraft.Bridge.Relay/index.js",
    '''    case "bind": {
      const requestId = String(message.requestId || "");
      if (!requestId) return;
      room.pendingBinds = room.pendingBinds.filter((x) => x.requestId !== requestId);
      room.pendingBinds.push(message);
      if (room.pendingBinds.length > MAX_PENDING_BINDS) {
        room.pendingBinds.splice(0, room.pendingBinds.length - MAX_PENDING_BINDS);
      }
      sendJson(room.android, message);
      return;
    }
''',
    '''    case "bind": {
      const requestId = String(message.requestId || "");
      if (!requestId) return;
      room.pendingBinds = room.pendingBinds.filter((x) => x.requestId !== requestId);
      room.pendingBinds.push(message);
      if (room.pendingBinds.length > MAX_PENDING_BINDS) {
        room.pendingBinds.splice(0, room.pendingBinds.length - MAX_PENDING_BINDS);
      }
      sendJson(room.android, message);
      return;
    }
    case "unbind": {
      const requestId = String(message.requestId || "");
      const entityId = Number(message.entityId);
      if (!requestId || !Number.isInteger(entityId)) return;
      // Destructive control messages are intentionally never cached/replayed.
      // A stale entityId must never be able to disconnect a later binding.
      sendJson(room.android, message);
      return;
    }
''',
)
replace_exact(
    "VoiceCraft.Bridge.Relay/index.js",
    '''    case "request_snapshot":
    case "server_status":
    case "entity_key":
    case "voice_client_disconnected":
      sendJson(room.endstone, message);
''',
    '''    case "request_snapshot":
    case "server_status":
    case "entity_key":
    case "voice_client_disconnected":
    case "unbind_result":
      sendJson(room.endstone, message);
''',
)
replace_all("VoiceCraft.Bridge.Relay/index.js", 'relayVersion: "0.2.0"', 'relayVersion: "0.2.1"')
replace_all("VoiceCraft.Bridge.Relay/index.js", 'VoiceCraft Endstone Relay v0.2.0', 'VoiceCraft Endstone Relay v0.2.1')
replace_all("VoiceCraft.Bridge.Relay/package.json", '"version": "0.2.0"', '"version": "0.2.1"')

android = "VoiceCraft.Server.Android/EndstoneBridgeController.cs"
replace_exact(
    android,
    '    private readonly Dictionary<int, string> _playerByEntity = new();\n',
    '    private readonly Dictionary<int, string> _playerByEntity = new();\n    private readonly Dictionary<int, BridgeUnbindRequest> _manualUnbindByEntity = new();\n',
)
replace_exact(
    android,
    '''        if (disconnectedPlayer is not null)
        {
            QueueOutgoing(new
            {
                type = "voice_client_disconnected",
                xuid = disconnectedPlayer.Xuid,
                uuid = disconnectedPlayer.Uuid,
                name = disconnectedPlayer.Name,
                entityId = entity.Id
            });
            AndroidRuntimeLog.Append(
                "BRIDGE",
                $"Voice client disconnected while Minecraft player={disconnectedPlayer.Name} remains online; rebind requested entity={entity.Id}");
        }
        else
''',
    '''        if (_manualUnbindByEntity.Remove(entity.Id, out var manualRequest))
        {
            SendUnbindResult(manualRequest, true, string.Empty, entity.Id);
            AndroidRuntimeLog.Append(
                "BRIDGE",
                $"UNBIND completed player={manualRequest.Name} entity={entity.Id} request={ShortId(manualRequest.RequestId)}; auto-rebind event suppressed");
        }
        else if (disconnectedPlayer is not null)
        {
            QueueOutgoing(new
            {
                type = "voice_client_disconnected",
                xuid = disconnectedPlayer.Xuid,
                uuid = disconnectedPlayer.Uuid,
                name = disconnectedPlayer.Name,
                entityId = entity.Id
            });
            AndroidRuntimeLog.Append(
                "BRIDGE",
                $"Voice client disconnected while Minecraft player={disconnectedPlayer.Name} remains online; rebind requested entity={entity.Id}");
        }
        else
''',
)
replace_exact(
    android,
    '''    private void HandlePlayerLeave(string playerKey, string name)
''',
    '''    private void HandleManualUnbind(BridgeUnbindRequest request)
    {
        if (!_boundByPlayer.TryGetValue(request.PlayerKey, out var currentEntityId))
        {
            SendUnbindResult(request, false, "player is not currently bound");
            return;
        }
        if (currentEntityId != request.EntityId)
        {
            SendUnbindResult(request, false, $"stale entity id (current={currentEntityId})", currentEntityId);
            AndroidRuntimeLog.Append(
                "BRIDGE",
                $"UNBIND stale request rejected player={request.Name} requested_entity={request.EntityId} current_entity={currentEntityId} request={ShortId(request.RequestId)}");
            return;
        }
        if (_world?.GetEntity(currentEntityId) is not VoiceCraftNetworkEntity entity || entity.Destroyed)
        {
            SendUnbindResult(request, false, "voice client entity no longer exists", currentEntityId);
            return;
        }
        var server = entity.NetPeer.Server;
        if (server is null)
        {
            SendUnbindResult(request, false, "voice client server is unavailable", currentEntityId);
            return;
        }

        _manualUnbindByEntity[currentEntityId] = request;
        AndroidRuntimeLog.Append(
            "BRIDGE",
            $"UNBIND accepted player={request.Name} entity={currentEntityId} request={ShortId(request.RequestId)}; disconnecting VoiceCraft peer");
        try
        {
            server.Disconnect(entity.NetPeer, "VoiceCraft.DisconnectReason.Kicked");
        }
        catch (Exception ex)
        {
            _manualUnbindByEntity.Remove(currentEntityId);
            SendUnbindResult(request, false, $"{ex.GetType().Name}: {ex.Message}", currentEntityId);
        }
    }

    private void SendUnbindResult(BridgeUnbindRequest request, bool success, string reason, int? entityId = null)
    {
        QueueOutgoing(new
        {
            type = "unbind_result",
            requestId = request.RequestId,
            xuid = request.Xuid,
            uuid = request.Uuid,
            name = request.Name,
            success,
            reason,
            entityId
        });
    }

    private void HandlePlayerLeave(string playerKey, string name)
''',
)
replace_exact(
    android,
    '''            case "bind":
            {
                if (!BridgeBindRequest.TryParse(root, out var request))
                    return;
                VoiceCraft.Server.RuntimeDispatcher.Post(() => HandleBind(request));
                break;
            }
''',
    '''            case "bind":
            {
                if (!BridgeBindRequest.TryParse(root, out var request))
                    return;
                VoiceCraft.Server.RuntimeDispatcher.Post(() => HandleBind(request));
                break;
            }
            case "unbind":
            {
                if (!BridgeUnbindRequest.TryParse(root, out var request))
                    return;
                VoiceCraft.Server.RuntimeDispatcher.Post(() => HandleManualUnbind(request));
                break;
            }
''',
)
replace_exact(
    android,
    '''    private sealed record BridgeBindRequest(string RequestId, string BindingKey, BridgePlayerState State)
''',
    '''    private sealed record BridgeUnbindRequest(
        string RequestId,
        string Name,
        string Xuid,
        string Uuid,
        int EntityId)
    {
        public string PlayerKey => !string.IsNullOrWhiteSpace(Xuid) ? Xuid : Uuid;

        public static bool TryParse(JsonElement root, out BridgeUnbindRequest request)
        {
            request = null!;
            var requestId = GetString(root, "requestId");
            var name = GetString(root, "name");
            var xuid = GetString(root, "xuid");
            var uuid = GetString(root, "uuid");
            if (string.IsNullOrWhiteSpace(requestId) ||
                (string.IsNullOrWhiteSpace(xuid) && string.IsNullOrWhiteSpace(uuid)) ||
                !root.TryGetProperty("entityId", out var entityElement) ||
                entityElement.ValueKind != JsonValueKind.Number ||
                !entityElement.TryGetInt32(out var entityId))
                return false;
            request = new BridgeUnbindRequest(requestId, name, xuid, uuid, entityId);
            return true;
        }
    }

    private sealed record BridgeBindRequest(string RequestId, string BindingKey, BridgePlayerState State)
''',
)
replace_all(android, "Phase 2 UI4.3", "Phase 2 UI4.4")
replace_all(android, "1.7.1-android-phase2-ui4.3", "1.7.1-android-phase2-ui4.4")
replace_all(android, 'bridgeVersion = "0.2.5"', 'bridgeVersion = "0.2.6"')
replace_all(android, 'reason = "android-ui4.3"', 'reason = "android-ui4.4"', minimum=0) if False else None
# The older snapshot reason may still say ui4.2; update it as metadata only.
replace_all(android, 'reason = "android-ui4.2"', 'reason = "android-ui4.4"')

replace_all("VoiceCraft.Server.Android/ModernMainActivity.cs", "0.2.5", "0.2.6")
replace_all("VoiceCraft.Server.Android/ModernMainActivity.cs", "UI4.3", "UI4.4")
replace_exact(
    "VoiceCraft.Server.Android/VoiceCraft.Server.Android.csproj",
    '<!-- UI4.3: optional multi-relay auto failover; 5 attempts per relay; Endstone companion 0.2.5 -->',
    '<!-- UI4.4: multi-relay failover, real /vc unbind and in-game alerts; Endstone companion 0.2.6 -->',
)
replace_exact("VoiceCraft.Server.Android/VoiceCraft.Server.Android.csproj", '<ApplicationVersion>9</ApplicationVersion>', '<ApplicationVersion>10</ApplicationVersion>')
replace_exact(
    "VoiceCraft.Server.Android/VoiceCraft.Server.Android.csproj",
    '<ApplicationDisplayVersion>1.7.1-android-phase2-ui4.3</ApplicationDisplayVersion>',
    '<ApplicationDisplayVersion>1.7.1-android-phase2-ui4.4</ApplicationDisplayVersion>',
)

contract = "VoiceCraft.Endstone/tests/relay_contract.py"
replace_exact(
    contract,
    '''            disconnected = await wait_incoming(client, "voice_client_disconnected")
            assert disconnected.get("xuid") == state["xuid"]
            assert disconnected.get("entityId") == 7

''',
    '''            disconnected = await wait_incoming(client, "voice_client_disconnected")
            assert disconnected.get("xuid") == state["xuid"]
            assert disconnected.get("entityId") == 7

            unbind = {
                "type": "unbind",
                "requestId": "contract-unbind-001",
                "name": state["name"],
                "xuid": state["xuid"],
                "uuid": state["uuid"],
                "entityId": 7,
            }
            assert client.send(unbind)
            received_unbind = await recv_type(android, "unbind")
            assert received_unbind.get("requestId") == unbind["requestId"]
            assert received_unbind.get("entityId") == 7

            await android.send_json(
                {
                    "type": "unbind_result",
                    "serverId": SERVER_ID,
                    "requestId": unbind["requestId"],
                    "name": state["name"],
                    "xuid": state["xuid"],
                    "uuid": state["uuid"],
                    "success": True,
                    "reason": "",
                    "entityId": 7,
                }
            )
            unbind_result = await wait_incoming(client, "unbind_result")
            assert unbind_result.get("requestId") == unbind["requestId"]
            assert unbind_result.get("success") is True
            assert unbind_result.get("entityId") == 7

''',
)
replace_all(contract, "VoiceCraft Endstone 0.2.3 / Relay / Android protocol-1 contract OK", "VoiceCraft Endstone 0.2.6 / Relay 0.2.1 / Android protocol-1 contract OK")

print("UI4.4 / Endstone 0.2.6 patch applied")
