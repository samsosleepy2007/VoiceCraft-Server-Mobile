from __future__ import annotations

import asyncio
import json
from typing import Any
from urllib.parse import urlparse

import aiohttp
from endstone import Player
from endstone.command import CommandSender

from .bridge import EndstoneRelayClient
from .plugin import VoiceCraftEndstone as VoiceCraftEndstoneBase


class CompatibleEndstoneRelayClient(EndstoneRelayClient):
    """Protocol-1 relay client used by Endstone 0.2.1.

    It keeps the 0.2.0 wire contract used by Android UI4.1 while improving
    reconnect diagnostics and advertising the matching plugin version.
    """

    def __init__(
        self,
        logger: Any,
        url: str,
        server_id: str,
        secret: str,
        reconnect_seconds: float = 5.0,
        max_queue: int = 2048,
        plugin_version: str = "0.2.1",
    ) -> None:
        super().__init__(logger, url, server_id, secret, reconnect_seconds, max_queue)
        self._plugin_version = str(plugin_version or "0.2.1")
        self._ever_connected = False
        self._reconnect_attempt = 0

    async def _run(self) -> None:
        timeout = aiohttp.ClientTimeout(total=None, sock_connect=15, sock_read=None)
        while not self._stop.is_set():
            try:
                if self._ever_connected or self._reconnect_attempt:
                    self._reconnect_attempt += 1
                    self._logger.info(
                        f"BRIDGE reconnect attempt={self._reconnect_attempt} "
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
                        self._reconnect_attempt = 0
                        self._logger.info(
                            f"BRIDGE {'reconnected' if was_reconnect else 'connected'} "
                            f"relay={self._safe_endpoint(self._url)} server_id={self._server_id}"
                        )

                        sender = asyncio.create_task(self._sender_loop(ws))
                        try:
                            async for msg in ws:
                                if self._stop.is_set():
                                    break
                                if msg.type == aiohttp.WSMsgType.TEXT:
                                    self._handle_incoming_text(msg.data)
                                elif msg.type in (
                                    aiohttp.WSMsgType.CLOSE,
                                    aiohttp.WSMsgType.CLOSED,
                                    aiohttp.WSMsgType.ERROR,
                                ):
                                    break
                        finally:
                            sender.cancel()
                            await asyncio.gather(sender, return_exceptions=True)

                        if not self._stop.is_set():
                            detail = f"close_code={ws.close_code}"
                            ws_error = ws.exception()
                            if ws_error is not None:
                                detail += f" error={type(ws_error).__name__}: {ws_error}"
                            raise RuntimeError(f"relay websocket closed ({detail})")
            except asyncio.CancelledError:
                raise
            except Exception as exc:
                if self._reconnect_attempt == 0:
                    self._reconnect_attempt = 1
                self._record_error(f"{type(exc).__name__}: {exc}")
                self._logger.warning(
                    f"BRIDGE disconnected: {type(exc).__name__}: {exc}; "
                    f"retrying in {self._reconnect_seconds:.0f}s"
                )
            finally:
                self._set_state(False, False)

            await self._sleep_with_stop(self._reconnect_seconds)


class VoiceCraftEndstone(VoiceCraftEndstoneBase):
    """Endstone 0.2.1 companion plugin for VoiceCraft Server Mobile UI4.1."""

    version = "0.2.1"

    # Copy the command metadata so changing the 0.2.1 help text cannot mutate
    # the base class dictionary retained for source compatibility.
    commands = {
        **VoiceCraftEndstoneBase.commands,
        "vcunbind": {
            **VoiceCraftEndstoneBase.commands["vcunbind"],
            "description": "Cancel a pending VoiceCraft bind request (does not unbind an already-bound client)",
        },
    }

    permissions = {
        **VoiceCraftEndstoneBase.permissions,
        "voicecraft.command.bind": {
            **VoiceCraftEndstoneBase.permissions["voicecraft.command.bind"],
            "description": "Allow a player to bind or cancel a pending VoiceCraft bind request.",
        },
    }

    @staticmethod
    def _validate_bridge_config(
        enabled: bool,
        url: str,
        server_id: str,
        secret: str,
    ) -> tuple[bool, str]:
        if not enabled:
            return False, "bridge is disabled"

        placeholders = ("YOUR-RELAY", "CHANGE_ME", "YOUR_SECRET")
        if any(marker in url or marker in secret for marker in placeholders):
            return False, "placeholder Render URL or Bridge Secret is still configured"

        try:
            parsed = urlparse(url)
        except Exception:
            return False, "relay URL could not be parsed"

        if parsed.scheme not in ("ws", "wss") or not parsed.netloc:
            return False, "relay URL must use ws:// or wss:// and include a hostname"
        if parsed.path != "/bridge":
            return False, "relay URL path must be exactly /bridge"
        if parsed.params or parsed.query or parsed.fragment:
            return False, "relay URL must not include parameters, query text, or a fragment"
        if not server_id:
            return False, "server_id is empty"
        if len(server_id) > 100:
            return False, "server_id is longer than the relay limit of 100 characters"
        if len(secret) < 16:
            return False, "Bridge Secret must be at least 16 characters"
        return True, ""

    def _load_settings(self) -> None:
        tracking = self.config.get("tracking", {})
        binding = self.config.get("binding", {})
        bridge = self.config.get("bridge", {})

        self._interval_ticks = self._bounded_int(tracking.get("interval_ticks", 2), 1, 20, 2)
        self._position_epsilon = self._bounded_float(
            tracking.get("position_epsilon", 0.05), 0.001, 10.0, 0.05
        )
        self._rotation_epsilon = self._bounded_float(
            tracking.get("rotation_epsilon", 1.0), 0.01, 180.0, 1.0
        )
        self._log_position_changes = bool(tracking.get("log_position_changes", False))
        heartbeat_seconds = self._bounded_int(
            tracking.get("heartbeat_seconds", 30), 1, 3600, 30
        )
        self._heartbeat_ticks = heartbeat_seconds * 20

        self._min_key_length = self._bounded_int(binding.get("min_key_length", 4), 1, 1024, 4)
        self._max_key_length = self._bounded_int(
            binding.get("max_key_length", 128), self._min_key_length, 4096, 128
        )

        enabled = bool(bridge.get("enabled", False))
        url = str(bridge.get("url", "")).strip()
        server_id = str(bridge.get("server_id", "mcsv-main")).strip()
        secret = str(bridge.get("secret", "")).strip()
        reconnect_seconds = self._bounded_float(
            bridge.get("reconnect_seconds", 5), 1.0, 60.0, 5.0
        )

        usable, validation_error = self._validate_bridge_config(
            enabled, url, server_id, secret
        )
        self._bridge_enabled = usable
        self._bridge_server_id = server_id or "mcsv-main"
        self._bridge = (
            CompatibleEndstoneRelayClient(
                self.logger,
                url,
                self._bridge_server_id,
                secret,
                reconnect_seconds,
                plugin_version=self.version,
            )
            if usable
            else None
        )

        if enabled and not usable:
            self.logger.warning(
                f"BRIDGE config invalid: {validation_error}; bridge remains disabled. "
                "Use Copy Plugin Config in VoiceCraft Server Mobile UI4.1."
            )

    def _command_unbind(self, sender: CommandSender) -> bool:
        if not isinstance(sender, Player):
            sender.send_error_message(
                "/vcunbind must be run by a player. This command only cancels a pending bind request."
            )
            return False

        player_key = self._player_key(sender)
        removed = self._pending_bind_keys.pop(player_key, None)
        self._pending_bind_requests.pop(player_key, None)
        if removed is None:
            sender.send_message(
                "No pending VoiceCraft binding request is waiting to be cancelled. "
                "An already-bound voice client is not changed by /vcunbind."
            )
        else:
            self.logger.info(f"BIND CLEARED player={sender.name} xuid={sender.xuid}")
            sender.send_message(
                "Pending VoiceCraft binding request cancelled. "
                "This does not unbind an already-bound voice client."
            )
        return True
