import http from "node:http";
import crypto from "node:crypto";
import { WebSocketServer, WebSocket } from "ws";

const PORT = Number.parseInt(process.env.PORT || "10000", 10);
const BRIDGE_SECRET = process.env.BRIDGE_SECRET || "";
const MAX_MESSAGE_BYTES = Number.parseInt(process.env.MAX_MESSAGE_BYTES || "65536", 10);
const MAX_PENDING_BINDS = 64;

if (!BRIDGE_SECRET || BRIDGE_SECRET.length < 16) {
  console.error("BRIDGE_SECRET must be configured and at least 16 characters long.");
  process.exit(1);
}

const rooms = new Map();

function getRoom(serverId) {
  let room = rooms.get(serverId);
  if (!room) {
    room = {
      endstone: null,
      android: null,
      latestStates: new Map(),
      pendingBinds: [],
      createdAt: Date.now(),
    };
    rooms.set(serverId, room);
  }
  return room;
}

function safeEqual(a, b) {
  const aa = Buffer.from(String(a || ""));
  const bb = Buffer.from(String(b || ""));
  if (aa.length !== bb.length) return false;
  return crypto.timingSafeEqual(aa, bb);
}

function sendJson(ws, value) {
  if (!ws || ws.readyState !== WebSocket.OPEN) return false;
  ws.send(JSON.stringify(value));
  return true;
}

function notifyPeerStatus(serverId, room) {
  sendJson(room.endstone, {
    type: "peer_status",
    serverId,
    androidConnected: room.android?.readyState === WebSocket.OPEN,
  });
  sendJson(room.android, {
    type: "peer_status",
    serverId,
    endstoneConnected: room.endstone?.readyState === WebSocket.OPEN,
  });
}

function replayToAndroid(serverId, room) {
  const ws = room.android;
  if (!ws || ws.readyState !== WebSocket.OPEN) return;

  sendJson(ws, { type: "sync_begin", serverId, source: "relay-cache" });
  for (const state of room.latestStates.values()) {
    sendJson(ws, state);
  }
  for (const bind of room.pendingBinds) {
    sendJson(ws, bind);
  }
  sendJson(ws, { type: "sync_end", serverId, source: "relay-cache" });
}

function forwardEndstone(serverId, room, message) {
  switch (message.type) {
    case "player_state": {
      const playerId = String(message.xuid || message.uuid || "");
      if (!playerId) return;
      room.latestStates.set(playerId, message);
      sendJson(room.android, message);
      return;
    }
    case "player_leave": {
      const playerId = String(message.xuid || message.uuid || "");
      if (playerId) room.latestStates.delete(playerId);
      sendJson(room.android, message);
      return;
    }
    case "bind": {
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
    case "sync_begin":
    case "sync_end":
    case "heartbeat":
      sendJson(room.android, message);
      return;
    default:
      return;
  }
}

function forwardAndroid(serverId, room, message) {
  switch (message.type) {
    case "bind_result": {
      const requestId = String(message.requestId || "");
      if (requestId) {
        room.pendingBinds = room.pendingBinds.filter((x) => x.requestId !== requestId);
      }
      sendJson(room.endstone, message);
      return;
    }
    case "request_snapshot":
    case "server_status":
    case "entity_key":
      sendJson(room.endstone, message);
      return;
    default:
      return;
  }
}

const server = http.createServer((req, res) => {
  if (req.url === "/health") {
    const summary = [...rooms.entries()].map(([serverId, room]) => ({
      serverId,
      endstone: room.endstone?.readyState === WebSocket.OPEN,
      android: room.android?.readyState === WebSocket.OPEN,
      cachedPlayers: room.latestStates.size,
      pendingBinds: room.pendingBinds.length,
    }));
    res.writeHead(200, { "content-type": "application/json; charset=utf-8" });
    res.end(JSON.stringify({ ok: true, rooms: summary }));
    return;
  }

  res.writeHead(200, { "content-type": "text/plain; charset=utf-8" });
  res.end("VoiceCraft Endstone Relay v0.2.0\n");
});

const wss = new WebSocketServer({ noServer: true, maxPayload: MAX_MESSAGE_BYTES });

server.on("upgrade", (req, socket, head) => {
  const url = new URL(req.url || "/", `http://${req.headers.host || "localhost"}`);
  if (url.pathname !== "/bridge") {
    socket.write("HTTP/1.1 404 Not Found\r\nConnection: close\r\n\r\n");
    socket.destroy();
    return;
  }

  wss.handleUpgrade(req, socket, head, (ws) => wss.emit("connection", ws, req));
});

wss.on("connection", (ws) => {
  ws.isAlive = true;
  ws.bridgeRole = null;
  ws.serverId = null;

  ws.on("pong", () => {
    ws.isAlive = true;
  });

  const authTimer = setTimeout(() => {
    if (!ws.bridgeRole) ws.close(4401, "hello required");
  }, 8000);

  ws.on("message", (data, isBinary) => {
    if (isBinary || data.length > MAX_MESSAGE_BYTES) {
      ws.close(4400, "invalid message");
      return;
    }

    let message;
    try {
      message = JSON.parse(data.toString("utf8"));
    } catch {
      ws.close(4400, "invalid json");
      return;
    }

    if (!ws.bridgeRole) {
      if (message?.type !== "hello") {
        ws.close(4401, "hello required");
        return;
      }

      const role = String(message.role || "");
      const serverId = String(message.serverId || "").trim();
      const secret = String(message.secret || "");
      if (!serverId || serverId.length > 100 || !["endstone", "android"].includes(role) || !safeEqual(secret, BRIDGE_SECRET)) {
        ws.close(4403, "authentication failed");
        return;
      }

      clearTimeout(authTimer);
      ws.bridgeRole = role;
      ws.serverId = serverId;
      const room = getRoom(serverId);

      const previous = room[role];
      if (previous && previous !== ws && previous.readyState === WebSocket.OPEN) {
        previous.close(4409, "replaced by newer connection");
      }
      room[role] = ws;

      console.log(`[bridge] ${role} connected server=${serverId}`);
      sendJson(ws, { type: "hello_ok", role, serverId, relayVersion: "0.2.0" });
      notifyPeerStatus(serverId, room);

      if (role === "android") {
        replayToAndroid(serverId, room);
        sendJson(room.endstone, { type: "request_snapshot", serverId, reason: "android-connected" });
      }
      return;
    }

    if (String(message.serverId || ws.serverId) !== ws.serverId) return;
    const room = getRoom(ws.serverId);
    if (ws.bridgeRole === "endstone") {
      forwardEndstone(ws.serverId, room, message);
    } else {
      forwardAndroid(ws.serverId, room, message);
    }
  });

  ws.on("close", () => {
    clearTimeout(authTimer);
    if (!ws.bridgeRole || !ws.serverId) return;
    const room = rooms.get(ws.serverId);
    if (!room) return;
    if (room[ws.bridgeRole] === ws) room[ws.bridgeRole] = null;
    console.log(`[bridge] ${ws.bridgeRole} disconnected server=${ws.serverId}`);
    notifyPeerStatus(ws.serverId, room);
  });

  ws.on("error", (err) => {
    console.warn(`[bridge] websocket error: ${err.message}`);
  });
});

const heartbeat = setInterval(() => {
  for (const ws of wss.clients) {
    if (ws.isAlive === false) {
      ws.terminate();
      continue;
    }
    ws.isAlive = false;
    ws.ping();
  }
}, 25000);

wss.on("close", () => clearInterval(heartbeat));

server.listen(PORT, "0.0.0.0", () => {
  console.log(`VoiceCraft Endstone Relay listening on :${PORT}`);
});
