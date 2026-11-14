// Critical Mass room relay. A Worker routes HTTP; one Durable Object per room code forwards binary messages between the
// people in that room. The relay holds no game logic and never parses game payloads: deterministic lockstep means every
// game checks every other game (state fingerprints), so the relay only needs to route, limit and clean up.
//
//   POST /rooms                       -> 201 {"code":"K7Q2PF","key":"<host key>"}   create a room
//   GET  /rooms/<CODE>/ws?v=1&key=..  -> WebSocket                                  host joins (with its key)
//   GET  /rooms/<CODE>/ws?v=1         -> WebSocket                                  anyone else joins
//   GET  /health                      -> 200 ok
// Errors after the WebSocket opened arrive as a CLOSING frame with a reason code (see protocol.js), so the game can show
// one clear sentence instead of a bare socket error.
import { DurableObject } from "cloudflare:workers";
import {
  OP_DATA, OP_LOCK, OP_WELCOME, OP_JOINED, OP_LEFT, OP_CLOSING, BROADCAST, HOST, REASON, PROTOCOL_VERSION,
  MAX_PEERS, MAX_FRAME, IDLE_MS, HOST_CONNECT_MS, MAX_ROOM_MS, Bucket, frame, normaliseCode, randomCode, randomKey,
} from "./protocol.js";

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean);
    if (parts.length === 1 && parts[0] === "health") return new Response("ok");
    if (parts.length === 1 && parts[0] === "rooms" && request.method === "POST") {
      for (let attempt = 0; attempt < 5; attempt++) {
        const code = randomCode();
        const key = randomKey();
        const stub = env.ROOMS.get(env.ROOMS.idFromName(code));
        const ok = await stub.create(key);
        if (ok) return Response.json({ code, key }, { status: 201, headers: { "cache-control": "no-store" } });
      }
      return new Response("busy", { status: 503 });
    }
    if (parts.length === 3 && parts[0] === "rooms" && parts[2] === "ws") {
      if (request.headers.get("Upgrade") !== "websocket") return new Response("expected a WebSocket", { status: 426 });
      const code = normaliseCode(parts[1]);
      if (!code) return closingSocket(REASON.NOT_FOUND);
      const stub = env.ROOMS.get(env.ROOMS.idFromName(code));
      return stub.fetch(request);
    }
    return new Response("not found", { status: 404 });
  },
};

// A socket that only says why it cannot join, then closes (used before a room is even looked up).
function closingSocket(reason) {
  const pair = new WebSocketPair();
  pair[1].accept();
  pair[1].send(frame(OP_CLOSING, reason));
  pair[1].close(1000, "closing");
  return new Response(null, { status: 101, webSocket: pair[0] });
}

export class Room extends DurableObject {
  constructor(ctx, env) {
    super(ctx, env);
    this.buckets = new Map(); // peer -> Bucket (memory only; a hibernation wake starts a fresh bucket)
  }

  // ---- RPC from the Worker ------------------------------------------------------------------------------------------

  async create(key) {
    const meta = await this.ctx.storage.get("meta");
    if (meta && Date.now() - meta.created < MAX_ROOM_MS) return false; // code in use: the Worker draws another
    await this.ctx.storage.deleteAll();
    await this.ctx.storage.put("meta", { key, created: Date.now(), locked: false, nextPeer: 1, lastActive: Date.now(), hostSeen: false });
    await this.ctx.storage.setAlarm(Date.now() + HOST_CONNECT_MS);
    return true;
  }

  // ---- WebSocket join -----------------------------------------------------------------------------------------------

  async fetch(request) {
    const url = new URL(request.url);
    const pair = new WebSocketPair();
    const client = pair[0], server = pair[1];
    const meta = await this.ctx.storage.get("meta");
    const reject = (reason) => {
      server.accept();
      server.send(frame(OP_CLOSING, reason));
      server.close(1000, "closing");
      return new Response(null, { status: 101, webSocket: client });
    };
    if (Number(url.searchParams.get("v")) !== PROTOCOL_VERSION) return reject(REASON.BAD_VERSION);
    if (!meta || Date.now() - meta.created > MAX_ROOM_MS) return reject(REASON.NOT_FOUND);
    const sockets = this.ctx.getWebSockets();
    const isHost = url.searchParams.get("key") === meta.key && !sockets.some((s) => this.peerOf(s) === HOST);
    if (!isHost && !meta.hostSeen) return reject(REASON.NOT_FOUND);
    if (!isHost && meta.locked) return reject(REASON.STARTED);
    if (sockets.length >= MAX_PEERS) return reject(REASON.FULL);
    let peer = HOST;
    if (!isHost) {
      peer = meta.nextPeer;
      if (peer >= BROADCAST) return reject(REASON.FULL);
      meta.nextPeer = peer + 1;
    } else meta.hostSeen = true;
    meta.lastActive = Date.now();
    await this.ctx.storage.put("meta", meta);
    // Hibernation API: the object can sleep between messages; the peer number rides on the socket.
    this.ctx.acceptWebSocket(server);
    server.serializeAttachment({ peer });
    const others = sockets.map((s) => this.peerOf(s)).filter((p) => p !== undefined);
    server.send(frame(OP_WELCOME, peer, others.length, ...others));
    for (const s of sockets) this.trySend(s, frame(OP_JOINED, peer));
    await this.ctx.storage.setAlarm(Date.now() + 60 * 1000);
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws, message) {
    const from = this.peerOf(ws);
    if (from === undefined) return;
    if (typeof message === "string") return this.kick(ws, REASON.BAD_REQUEST);
    const data = new Uint8Array(message);
    if (data.length > MAX_FRAME) return this.kick(ws, REASON.TOO_BIG);
    let bucket = this.buckets.get(from);
    if (!bucket) this.buckets.set(from, (bucket = new Bucket()));
    if (!bucket.take(Date.now())) return this.kick(ws, REASON.TOO_FAST);
    this.touched = Date.now();
    if (data.length >= 2 && data[0] === OP_DATA) {
      const to = data[1];
      const out = new Uint8Array(data.length);
      out.set(data);
      out[1] = from; // the relay stamps the sender; nobody can speak for someone else
      for (const s of this.ctx.getWebSockets()) {
        const p = this.peerOf(s);
        if (p === undefined || p === from) continue;
        if (to === BROADCAST || to === p) this.trySend(s, out);
      }
      return;
    }
    if (data.length === 1 && data[0] === OP_LOCK && from === HOST) {
      const meta = await this.ctx.storage.get("meta");
      if (meta) { meta.locked = true; await this.ctx.storage.put("meta", meta); }
      return;
    }
    return this.kick(ws, REASON.BAD_REQUEST);
  }

  async webSocketClose(ws) { await this.left(ws); }
  async webSocketError(ws) { await this.left(ws); }

  async alarm() {
    const meta = await this.ctx.storage.get("meta");
    const sockets = this.ctx.getWebSockets();
    if (!meta) return;
    const now = Date.now();
    const lastActive = Math.max(meta.lastActive || 0, this.touched || 0);
    const expired = now - meta.created > MAX_ROOM_MS;
    const idle = now - lastActive > IDLE_MS;
    const hostNeverCame = !meta.hostSeen && now - meta.created > HOST_CONNECT_MS;
    if (expired || idle || hostNeverCame || (meta.hostSeen && sockets.length === 0)) {
      for (const s of sockets) this.kick(s, expired ? REASON.EXPIRED : REASON.IDLE);
      await this.ctx.storage.deleteAll();
      return;
    }
    if (this.touched) { meta.lastActive = this.touched; await this.ctx.storage.put("meta", meta); }
    await this.ctx.storage.setAlarm(now + 60 * 1000);
  }

  // ---- helpers ------------------------------------------------------------------------------------------------------

  peerOf(ws) {
    const a = ws.deserializeAttachment();
    return a && typeof a.peer === "number" ? a.peer : undefined;
  }

  trySend(ws, bytes) {
    try { ws.send(bytes); } catch { /* the socket is closing; webSocketClose cleans up */ }
  }

  kick(ws, reason) {
    this.trySend(ws, frame(OP_CLOSING, reason));
    try { ws.close(1000, "closing"); } catch { /* already closed */ }
  }

  async left(ws) {
    const peer = this.peerOf(ws);
    if (peer === undefined) return;
    ws.serializeAttachment({});
    this.buckets.delete(peer);
    const rest = this.ctx.getWebSockets().filter((s) => s !== ws && this.peerOf(s) !== undefined);
    if (peer === HOST) {
      // The host is the lockstep sequencer: without it nobody can continue (host migration is a later step).
      for (const s of rest) this.kick(s, REASON.HOST_LEFT);
      await this.ctx.storage.deleteAll();
      return;
    }
    for (const s of rest) this.trySend(s, frame(OP_LEFT, peer));
  }
}
