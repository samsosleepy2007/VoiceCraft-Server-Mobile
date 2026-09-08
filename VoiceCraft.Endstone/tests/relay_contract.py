from __future__ import annotations

import asyncio
import json
import os
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

import aiohttp

from endstone_voicecraft.bridge import EndstoneRelayClient


REPO_ROOT = Path(__file__).resolve().parents[2]
RELAY_DIR = REPO_ROOT / "VoiceCraft.Bridge.Relay"
PORT = int(os.environ.get("VOICECRAFT_CONTRACT_PORT", "18765"))
SECRET = "contract-test-secret-0123456789"
URL = f"ws://127.0.0.1:{PORT}/bridge"
SERVER_ID = "mcsv-main"


class TestLogger:
    def __init__(self) -> None:
        self.lines: list[tuple[str, str]] = []

    def info(self, message: str) -> None:
        self.lines.append(("info", str(message)))
        print(f"[plugin-info] {message}")

    def warning(self, message: str) -> None:
        self.lines.append(("warning", str(message)))
        print(f"[plugin-warning] {message}")


class RelayProcess:
    def __init__(self) -> None:
        self.process: subprocess.Popen[bytes] | None = None

    def start(self) -> None:
        env = os.environ.copy()
        env["PORT"] = str(PORT)
        env["BRIDGE_SECRET"] = SECRET
        self.process = subprocess.Popen(
            ["node", "index.js"],
            cwd=RELAY_DIR,
            env=env,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )

    def stop(self) -> None:
        process = self.process
        self.process = None
        if process is None or process.poll() is not None:
            return
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)


async def wait_until(predicate, timeout: float, label: str) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        await asyncio.sleep(0.05)
    raise AssertionError(f"Timed out waiting for {label}")


async def wait_health(timeout: float = 10.0) -> None:
    deadline = time.monotonic() + timeout
    async with aiohttp.ClientSession() as session:
        while time.monotonic() < deadline:
            try:
                async with session.get(f"http://127.0.0.1:{PORT}/health", timeout=1.0) as response:
                    if response.status == 200:
                        body = await response.json()
                        assert body.get("ok") is True
                        return
            except Exception:
                pass
            await asyncio.sleep(0.1)
    raise AssertionError("Relay did not become healthy")


async def connect_role(
    session: aiohttp.ClientSession,
    role: str,
    server_id: str = SERVER_ID,
    secret: str = SECRET,
) -> aiohttp.ClientWebSocketResponse:
    ws = await session.ws_connect(URL, heartbeat=10, max_msg_size=65536)
    await ws.send_json(
        {
            "type": "hello",
            "role": role,
            "serverId": server_id,
            "secret": secret,
            "protocol": 1,
            "test": True,
        }
    )
    msg = await ws.receive(timeout=3)
    assert msg.type == aiohttp.WSMsgType.TEXT, (msg.type, msg.data)
    payload = json.loads(msg.data)
    assert payload.get("type") == "hello_ok", payload
    assert payload.get("serverId") == server_id, payload
    return ws


async def recv_type(ws: aiohttp.ClientWebSocketResponse, wanted: str, timeout: float = 4.0) -> dict[str, Any]:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        remaining = max(0.05, deadline - time.monotonic())
        msg = await ws.receive(timeout=remaining)
        if msg.type == aiohttp.WSMsgType.TEXT:
            payload = json.loads(msg.data)
            if payload.get("type") == wanted:
                return payload
        elif msg.type in (aiohttp.WSMsgType.CLOSE, aiohttp.WSMsgType.CLOSED, aiohttp.WSMsgType.ERROR):
            raise AssertionError(f"WebSocket closed while waiting for {wanted}: type={msg.type} code={ws.close_code}")
    raise AssertionError(f"Timed out waiting for WebSocket message type={wanted}")


async def wait_incoming(client: EndstoneRelayClient, wanted: str, timeout: float = 4.0) -> dict[str, Any]:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        for payload in client.drain_incoming():
            if payload.get("type") == wanted:
                return payload
        await asyncio.sleep(0.05)
    raise AssertionError(f"Timed out waiting for Endstone incoming type={wanted}")


async def assert_no_message_type(ws: aiohttp.ClientWebSocketResponse, unwanted: str, timeout: float = 0.6) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            msg = await ws.receive(timeout=min(0.15, deadline - time.monotonic()))
        except asyncio.TimeoutError:
            continue
        if msg.type == aiohttp.WSMsgType.TEXT:
            payload = json.loads(msg.data)
            assert payload.get("type") != unwanted, payload
        elif msg.type in (aiohttp.WSMsgType.CLOSE, aiohttp.WSMsgType.CLOSED, aiohttp.WSMsgType.ERROR):
            break


async def main() -> None:
    relay = RelayProcess()
    logger = TestLogger()
    client = EndstoneRelayClient(logger, URL, SERVER_ID, SECRET, reconnect_seconds=1.0)

    try:
        relay.start()
        await wait_health()
        client.start()
        await wait_until(lambda: client.connected, 8.0, "Endstone relay connection")

        async with aiohttp.ClientSession() as session:
            android = await connect_role(session, "android")
            await recv_type(android, "peer_status")
            await recv_type(android, "sync_begin")
            await recv_type(android, "sync_end")
            await wait_until(lambda: client.android_connected, 5.0, "Android peer status on Endstone")

            state = {
                "type": "player_state",
                "name": "ContractPlayer",
                "xuid": "2535471173648691",
                "uuid": "943acbc3-2721-37f5-8dda-d61344e2dc3a",
                "dimension": "Overworld",
                "x": 12.25,
                "y": 72.0,
                "z": -8.5,
                "yaw": 174.8,
                "pitch": 18.6,
            }
            assert client.send(state)
            received_state = await recv_type(android, "player_state")
            for key, value in state.items():
                assert received_state.get(key) == value, (key, received_state)
            assert received_state.get("serverId") == SERVER_ID

            await android.send_json({"type": "request_snapshot", "serverId": SERVER_ID})
            request_snapshot = await wait_incoming(client, "request_snapshot")
            assert request_snapshot.get("serverId") == SERVER_ID

            bind = {
                "type": "bind",
                "requestId": "contract-bind-001",
                "bindingKey": "AbC12",
                **{key: value for key, value in state.items() if key != "type"},
            }
            assert client.send(bind)
            received_bind = await recv_type(android, "bind")
            assert received_bind.get("requestId") == bind["requestId"]
            assert received_bind.get("bindingKey") == bind["bindingKey"]
            assert received_bind.get("xuid") == state["xuid"]

            await android.send_json(
                {
                    "type": "bind_result",
                    "serverId": SERVER_ID,
                    "requestId": bind["requestId"],
                    "xuid": state["xuid"],
                    "success": True,
                    "reason": "",
                    "entityId": 7,
                }
            )
            bind_result = await wait_incoming(client, "bind_result")
            assert bind_result.get("success") is True
            assert bind_result.get("entityId") == 7

            await android.send_json(
                {
                    "type": "voice_client_disconnected",
                    "serverId": SERVER_ID,
                    "xuid": state["xuid"],
                    "uuid": state["uuid"],
                    "name": state["name"],
                    "entityId": 7,
                }
            )
            disconnected = await wait_incoming(client, "voice_client_disconnected")
            assert disconnected.get("xuid") == state["xuid"]
            assert disconnected.get("entityId") == 7

            other = await connect_role(session, "android", server_id="other-room")
            await recv_type(other, "peer_status")
            await recv_type(other, "sync_begin")
            await recv_type(other, "sync_end")
            state2 = dict(state)
            state2["x"] = 99.0
            assert client.send(state2)
            main_room_state = await recv_type(android, "player_state")
            assert main_room_state.get("x") == 99.0
            await assert_no_message_type(other, "player_state")
            await other.close()

            bad = await session.ws_connect(URL)
            await bad.send_json(
                {
                    "type": "hello",
                    "role": "android",
                    "serverId": "bad-secret-room",
                    "secret": "definitely-wrong-secret",
                    "protocol": 1,
                }
            )
            close_msg = await bad.receive(timeout=3)
            assert close_msg.type in (aiohttp.WSMsgType.CLOSE, aiohttp.WSMsgType.CLOSED), close_msg.type
            assert bad.close_code == 4403, bad.close_code
            await bad.close()

            await android.close()

        relay.stop()
        await wait_until(lambda: not client.connected, 10.0, "Endstone disconnect after relay stop")
        relay.start()
        await wait_health()
        await wait_until(lambda: client.connected, 12.0, "Endstone reconnect after relay restart")

        async with aiohttp.ClientSession() as session:
            android2 = await connect_role(session, "android")
            await recv_type(android2, "peer_status")
            await recv_type(android2, "sync_begin")
            await recv_type(android2, "sync_end")
            await wait_until(lambda: client.android_connected, 5.0, "Android peer after relay restart")
            assert client.send({"type": "heartbeat", "online": 1, "tracked": 1, "pendingBindings": 0})
            heartbeat = await recv_type(android2, "heartbeat")
            assert heartbeat.get("online") == 1
            await android2.close()

        assert any("BRIDGE connected" in line for _, line in logger.lines)
        assert any("BRIDGE reconnect" in line or "BRIDGE reconnected" in line for _, line in logger.lines)
        print("VoiceCraft Endstone 0.2.3 / Relay / Android protocol-1 contract OK")
    finally:
        client.stop()
        relay.stop()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except Exception as exc:
        print(f"CONTRACT TEST FAILED: {type(exc).__name__}: {exc}", file=sys.stderr)
        raise
