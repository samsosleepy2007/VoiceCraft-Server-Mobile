from __future__ import annotations

import asyncio
import json
import queue
import threading
import time
from typing import Any

import aiohttp


class EndstoneRelayClient:
    """Background WSS client used by the Endstone plugin.

    The networking thread never touches Endstone Player/Server objects. The
    plugin serializes plain dictionaries on the main server thread and the
    bridge transports those dictionaries to the relay.
    """

    def __init__(
        self,
        logger: Any,
        url: str,
        server_id: str,
        secret: str,
        reconnect_seconds: float = 5.0,
        max_queue: int = 2048,
    ) -> None:
        self._logger = logger
        self._url = url
        self._server_id = server_id
        self._secret = secret
        self._reconnect_seconds = max(1.0, float(reconnect_seconds))
        self._outgoing: queue.Queue[dict[str, Any]] = queue.Queue(maxsize=max_queue)
        self._incoming: queue.Queue[dict[str, Any]] = queue.Queue(maxsize=max_queue)
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None
        self._state_lock = threading.Lock()
        self._connected = False
        self._android_connected = False
        self._last_error = ""

    @property
    def enabled(self) -> bool:
        return bool(self._url and self._server_id and self._secret)

    @property
    def connected(self) -> bool:
        with self._state_lock:
            return self._connected

    @property
    def android_connected(self) -> bool:
        with self._state_lock:
            return self._android_connected

    @property
    def last_error(self) -> str:
        with self._state_lock:
            return self._last_error

    def start(self) -> None:
        if not self.enabled or self._thread is not None:
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._thread_main, name="VoiceCraft-Endstone-Bridge", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        thread = self._thread
        if thread is not None and thread.is_alive():
            thread.join(timeout=4.0)
        self._thread = None
        self._set_state(False, False)

    def send(self, message: dict[str, Any]) -> bool:
        if not self.enabled:
            return False
        payload = dict(message)
        payload.setdefault("serverId", self._server_id)
        payload.setdefault("ts", int(time.time() * 1000))
        try:
            self._outgoing.put_nowait(payload)
            return True
        except queue.Full:
            # Drop an older position update first; never print payload because it
            # may contain a temporary VoiceCraft binding key.
            self._drop_one_outgoing()
            try:
                self._outgoing.put_nowait(payload)
                return True
            except queue.Full:
                return False

    def drain_incoming(self, limit: int = 128) -> list[dict[str, Any]]:
        items: list[dict[str, Any]] = []
        for _ in range(max(1, limit)):
            try:
                items.append(self._incoming.get_nowait())
            except queue.Empty:
                break
        return items

    def _drop_one_outgoing(self) -> None:
        try:
            self._outgoing.get_nowait()
        except queue.Empty:
            return

    def _thread_main(self) -> None:
        try:
            asyncio.run(self._run())
        except Exception as exc:  # defensive: keep exceptions out of Endstone main thread
            self._record_error(f"{type(exc).__name__}: {exc}")
            self._logger.warning(f"Bridge thread stopped: {type(exc).__name__}: {exc}")

    async def _run(self) -> None:
        timeout = aiohttp.ClientTimeout(total=None, sock_connect=15, sock_read=None)
        while not self._stop.is_set():
            try:
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
                                "pluginVersion": "0.2.0",
                            }
                        )

                        hello = await ws.receive(timeout=10.0)
                        if hello.type != aiohttp.WSMsgType.TEXT:
                            raise RuntimeError("Relay did not return hello_ok")
                        hello_data = json.loads(hello.data)
                        if hello_data.get("type") != "hello_ok":
                            raise RuntimeError("Relay rejected hello")

                        self._record_error("")
                        self._set_state(True, False)
                        self._logger.info(
                            f"BRIDGE connected relay={self._safe_endpoint(self._url)} server_id={self._server_id}"
                        )

                        sender = asyncio.create_task(self._sender_loop(ws))
                        try:
                            async for msg in ws:
                                if self._stop.is_set():
                                    break
                                if msg.type == aiohttp.WSMsgType.TEXT:
                                    self._handle_incoming_text(msg.data)
                                elif msg.type in (aiohttp.WSMsgType.CLOSE, aiohttp.WSMsgType.CLOSED, aiohttp.WSMsgType.ERROR):
                                    break
                        finally:
                            sender.cancel()
                            await asyncio.gather(sender, return_exceptions=True)
            except asyncio.CancelledError:
                raise
            except Exception as exc:
                self._record_error(f"{type(exc).__name__}: {exc}")
                self._logger.warning(
                    f"BRIDGE disconnected: {type(exc).__name__}: {exc}; retrying in {self._reconnect_seconds:.0f}s"
                )
            finally:
                self._set_state(False, False)

            await self._sleep_with_stop(self._reconnect_seconds)

    async def _sender_loop(self, ws: aiohttp.ClientWebSocketResponse) -> None:
        while not self._stop.is_set() and not ws.closed:
            sent = False
            for _ in range(64):
                try:
                    payload = self._outgoing.get_nowait()
                except queue.Empty:
                    break
                await ws.send_str(json.dumps(payload, separators=(",", ":"), ensure_ascii=False))
                sent = True
            if not sent:
                await asyncio.sleep(0.025)

    def _handle_incoming_text(self, text: str) -> None:
        try:
            data = json.loads(text)
        except Exception:
            return
        if not isinstance(data, dict):
            return

        kind = data.get("type")
        if kind == "peer_status":
            with self._state_lock:
                self._android_connected = bool(data.get("androidConnected"))
        elif kind == "hello_ok":
            return

        try:
            self._incoming.put_nowait(data)
        except queue.Full:
            try:
                self._incoming.get_nowait()
            except queue.Empty:
                pass
            try:
                self._incoming.put_nowait(data)
            except queue.Full:
                pass

    async def _sleep_with_stop(self, seconds: float) -> None:
        end = time.monotonic() + seconds
        while not self._stop.is_set() and time.monotonic() < end:
            await asyncio.sleep(0.2)

    def _set_state(self, connected: bool, android_connected: bool) -> None:
        with self._state_lock:
            self._connected = connected
            self._android_connected = android_connected

    def _record_error(self, error: str) -> None:
        with self._state_lock:
            self._last_error = error

    @staticmethod
    def _safe_endpoint(url: str) -> str:
        # Never include query parameters because they may accidentally contain credentials.
        return url.split("?", 1)[0]
