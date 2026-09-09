from __future__ import annotations

import asyncio
import json
from typing import Any, Iterable

import aiohttp

from .bridge import EndstoneRelayClient


class MultiRelayEndstoneClient(EndstoneRelayClient):
    """Protocol-1 Endstone relay client with ordered multi-relay failover.

    The current relay is retried up to ``max_attempts`` times. After that the
    client advances to the next configured relay, wrapping back to Primary.
    A relay is considered healthy only after the Android peer is visible.
    """

    def __init__(
        self,
        logger: Any,
        urls: Iterable[str],
        server_id: str,
        secret: str,
        reconnect_seconds: float = 5.0,
        max_queue: int = 2048,
        plugin_version: str = "0.2.5",
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
        self._plugin_version = str(plugin_version or "0.2.5")
        self._max_attempts = max(1, int(max_attempts))
        self._peer_timeout_seconds = max(5.0, float(peer_timeout_seconds))
        self._active_index = 0
        self._attempts_on_active = 0
        self._ever_connected = False

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

    def _advance_relay(self) -> None:
        if len(self._urls) <= 1:
            self._attempts_on_active = 0
            return
        previous = self.active_relay_name
        self._active_index = (self._active_index + 1) % len(self._urls)
        self._url = self._urls[self._active_index]
        self._attempts_on_active = 0
        self._logger.warning(
            f"BRIDGE FAILOVER {previous} -> {self.active_relay_name} "
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
                        peer_watch = asyncio.create_task(self._peer_watchdog(ws))
                        try:
                            async for msg in ws:
                                if self._stop.is_set():
                                    break
                                if msg.type == aiohttp.WSMsgType.TEXT:
                                    self._handle_incoming_text(msg.data)
                                    if self.android_connected:
                                        self._attempts_on_active = 0
                                elif msg.type in (
                                    aiohttp.WSMsgType.CLOSE,
                                    aiohttp.WSMsgType.CLOSED,
                                    aiohttp.WSMsgType.ERROR,
                                ):
                                    break
                                if peer_watch.done():
                                    await peer_watch
                        finally:
                            sender.cancel()
                            peer_watch.cancel()
                            await asyncio.gather(sender, peer_watch, return_exceptions=True)

                        if not self._stop.is_set():
                            detail = f"close_code={ws.close_code}"
                            ws_error = ws.exception()
                            if ws_error is not None:
                                detail += f" error={type(ws_error).__name__}: {ws_error}"
                            raise RuntimeError(f"relay websocket closed ({detail})")
            except asyncio.CancelledError:
                raise
            except Exception as exc:
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

    async def _peer_watchdog(self, ws: aiohttp.ClientWebSocketResponse) -> None:
        missing_since: float | None = None
        loop = asyncio.get_running_loop()
        while not self._stop.is_set() and not ws.closed:
            await asyncio.sleep(2.0)
            if self.android_connected:
                missing_since = None
                self._attempts_on_active = 0
                continue
            now = loop.time()
            if missing_since is None:
                missing_since = now
            elif now - missing_since >= self._peer_timeout_seconds:
                raise RuntimeError(
                    f"Android peer not visible on {self.active_relay_name} for {self._peer_timeout_seconds:.0f}s"
                )
