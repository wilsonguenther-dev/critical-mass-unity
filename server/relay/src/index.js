// Critical Mass room relay. A Worker routes HTTP; one Durable Object per room code forwards binary messages between the
// people in that room. The relay holds no game logic and never parses game payloads: deterministic lockstep means every
// game checks every other game (state fingerprints), so the relay only needs to route, limit and clean up.
//
//   POST /rooms                       -> 201 {"code":"K7Q2PF","key":"<host key>"}   create a room
//   GET  /rooms/<CODE>/ws?v=1&key=..  -> WebSocket                                  host joins (with its key)
//   GET  /rooms/<CODE>/ws?v=1         -> WebSocket                                  anyone else joins
//   GET  /health                      -> 200 ok
//   GET  /usage                       -> 200 {"day":..,"messages":..,"requests":..,"free":100000,...}  b15-zero-cost: this
//                                        relay's own count for today (UTC), so a host's game can say how many matches are left
// Errors after the WebSocket opened arrive as a CLOSING frame with a reason code (see protocol.js), so the game can show
// one clear sentence instead of a bare socket error.
import { DurableObject } from "cloudflare:workers";
import {
  OP_DATA, OP_LOCK, OP_WELCOME, OP_JOINED, OP_LEFT, OP_CLOSING, BROADCAST, HOST, REASON, PROTOCOL_VERSION,
  MAX_PEERS, MAX_FRAME, IDLE_MS, HOST_CONNECT_MS, MAX_ROOM_MS, Bucket, frame, normaliseCode, randomCode, randomKey,
  FREE_REQUESTS_PER_DAY, MESSAGES_PER_REQUEST,
  chatKind, CHAT_RATE_JOINER, CHAT_BURST_JOINER, CHAT_RATE_HOST, CHAT_BURST_HOST, VOICE_RATE, VOICE_BURST,
  VOICE_ROOM_RATE, VOICE_ROOM_BURST, CHAT_STRIKES, CHAT_MAX_BYTES,
  CREATE_PER_MIN_PER_IP, JOIN_PER_MIN_PER_IP, MSGS_PER_MIN_PER_IP, MSG_CHECK_EVERY, IP_RATE_PER_ROOM, IP_BURST_PER_ROOM,
  ROOM_RATE, ROOM_BURST, JOINS_PER_MIN_PER_ROOM, WindowLimiter, budgetLevel,
} from "./protocol.js";

// b17-harden: fallback limiters when the [[ratelimits]] bindings are absent (best effort: one isolate's view only).
const fallbackCreate = new WindowLimiter(CREATE_PER_MIN_PER_IP);
const fallbackJoin = new WindowLimiter(JOIN_PER_MIN_PER_IP);
const fallbackMsgs = new WindowLimiter(MSGS_PER_MIN_PER_IP);

// b17-harden: true = allowed. The binding is approximate and per location; never throws (a limiter error allows).
async function allow(binding, fallback, key, delta = 1) {
  if (binding && typeof binding.limit === "function") {
    try { return (await binding.limit({ key, delta })).success; } catch { return true; }
  }
  return fallback.take(key, Date.now(), delta);
}

function clientIp(request) {
  return request.headers.get("CF-Connecting-IP") || "unknown";
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean);
    if (parts.length === 1 && parts[0] === "health") return new Response("ok");
    if (parts.length === 1 && parts[0] === "usage" && request.method === "GET") {
      // b15-zero-cost: one Worker request plus one Durable Object request per ask; the game asks once per room it hosts.
      const usage = await env.USAGE.get(env.USAGE.idFromName("usage")).today();
      return Response.json(usage, { headers: { "cache-control": "no-store", "access-control-allow-origin": "*" } });
    }
    if (parts.length === 1 && parts[0] === "rooms" && request.method === "POST") {
      // b17-harden: cheapest first: the per-IP limit (no Durable Object touched), then the day's budget (one request).
      if (!(await allow(env.CREATE_LIMIT, fallbackCreate, clientIp(request)))) return new Response("slow down", { status: 429, headers: { "retry-after": "60" } });
      const usage = await env.USAGE.get(env.USAGE.idFromName("usage")).today();
      if (!budgetLevel(usage.requests).newRooms) return new Response("the free relay is nearly used up for today", { status: 503, headers: { "retry-after": "3600" } });
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
      // b17-harden: a bad version or a join flood is refused here, before the room object (a billed request) is touched.
      if (Number(url.searchParams.get("v")) !== PROTOCOL_VERSION) return closingSocket(REASON.BAD_VERSION);
      if (!(await allow(env.JOIN_LIMIT, fallbackJoin, clientIp(request)))) return closingSocket(REASON.TOO_FAST);
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
    // b17-chat: chat and voice caps (memory only, like the buckets): peer -> {chat, voice, strikes}; one room voice bucket.
    this.chatCaps = new Map();
    this.roomVoice = new Bucket(VOICE_ROOM_RATE, VOICE_ROOM_BURST);
    // b17-harden: per-IP and whole-room buckets, join window, the day's budget as last reported (chat on until told).
    this.ipBuckets = new Map();
    this.roomBucket = new Bucket(ROOM_RATE, ROOM_BURST);
    this.joinWindow = new WindowLimiter(JOINS_PER_MIN_PER_ROOM, 60000, 1);
    this.chatAllowed = true;
    this.msgCount = new Map(); // peer -> messages (memory only)
    // b15-zero-cost: what this room cost since its last report (memory only: a hibernation wake starts at 0, so the count
    // can miss a few messages of a quiet room; a room in play never sleeps). Reported to the Usage object every alarm.
    this.msgs = 0;
    this.connects = 0;
    this.creates = 0;
  }

  // b15-zero-cost: adds this room's cost since the last report to today's total (one Durable Object request).
  async report(alarms) {
    const delta = { msgs: this.msgs, connects: this.connects, alarms, rooms: this.creates };
    this.msgs = 0; this.connects = 0; this.creates = 0;
    if (!delta.msgs && !delta.connects && !delta.alarms && !delta.rooms) return;
    try {
      // b17-harden: the same call answers with today's total, so every busy room learns the budget once a minute for free.
      const total = await this.env.USAGE.get(this.env.USAGE.idFromName("usage")).add(delta);
      if (typeof total === "number") this.chatAllowed = budgetLevel(total).chat;
    } catch { /* counting must never break a room */ }
  }

  // ---- RPC from the Worker ------------------------------------------------------------------------------------------

  async create(key) {
    const meta = await this.ctx.storage.get("meta");
    if (meta && Date.now() - meta.created < MAX_ROOM_MS) return false; // code in use: the Worker draws another
    await this.ctx.storage.deleteAll();
    await this.ctx.storage.put("meta", { key, created: Date.now(), locked: false, nextPeer: 1, lastActive: Date.now(), hostSeen: false });
    await this.ctx.storage.setAlarm(Date.now() + HOST_CONNECT_MS);
    this.creates += 1;
    return true;
  }

  // ---- WebSocket join -----------------------------------------------------------------------------------------------

  async fetch(request) {
    const url = new URL(request.url);
    const pair = new WebSocketPair();
    const client = pair[0], server = pair[1];
    const reject = (reason) => {
      server.accept();
      server.send(frame(OP_CLOSING, reason));
      server.close(1000, "closing");
      return new Response(null, { status: 101, webSocket: client });
    };
    if (Number(url.searchParams.get("v")) !== PROTOCOL_VERSION) return reject(REASON.BAD_VERSION);
    // b17-harden: a room takes at most JOINS_PER_MIN_PER_ROOM sockets a minute (checked before storage is read).
    if (!this.joinWindow.take("room", Date.now())) return reject(REASON.TOO_FAST);
    const meta = await this.ctx.storage.get("meta");
    if (!meta || Date.now() - meta.created > MAX_ROOM_MS) return reject(REASON.NOT_FOUND);
    const sockets = this.ctx.getWebSockets();
    const isHost = url.searchParams.get("key") === meta.key && !sockets.some((s) => this.peerOf(s) === HOST);
    // The room exists from the POST that made it; a joiner who is quicker than the host's game hears "not ready yet"
    // (the game asks again) instead of "no such room".
    if (!isHost && !meta.hostSeen) return reject(REASON.HOST_NOT_READY);
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
    this.connects += 1;
    // Hibernation API: the object can sleep between messages; the peer number rides on the socket.
    this.ctx.acceptWebSocket(server);
    // The address stays in this socket's attachment while it is open (rate limits only); it is never stored or logged.
    server.serializeAttachment({ peer, ip: request.headers.get("CF-Connecting-IP") || "unknown" });
    const others = sockets.map((s) => this.peerOf(s)).filter((p) => p !== undefined);
    server.send(frame(OP_WELCOME, peer, others.length, ...others));
    for (const s of sockets) this.trySend(s, frame(OP_JOINED, peer));
    await this.ctx.storage.setAlarm(Date.now() + 60 * 1000);
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws, message) {
    this.msgs += 1; // b15-zero-cost: every incoming message is billed (20 to 1), whatever happens to it next
    const from = this.peerOf(ws);
    if (from === undefined) return;
    if (typeof message === "string") return this.kick(ws, REASON.BAD_REQUEST);
    const data = new Uint8Array(message);
    if (data.length > MAX_FRAME) return this.kick(ws, REASON.TOO_BIG);
    const now = Date.now();
    let bucket = this.buckets.get(from);
    if (!bucket) this.buckets.set(from, (bucket = new Bucket()));
    if (!bucket.take(now)) return this.kick(ws, REASON.TOO_FAST);
    // b17-harden: one IP's sockets share a bucket in this room; the whole room has a ceiling; and every MSG_CHECK_EVERY
    // messages a socket asks the per-IP limiter (all rooms, approximate) so one address cannot flood room after room.
    const att = ws.deserializeAttachment() || {};
    const ip = att.ip || "unknown";
    let ipb = this.ipBuckets.get(ip);
    if (!ipb) this.ipBuckets.set(ip, (ipb = new Bucket(IP_RATE_PER_ROOM, IP_BURST_PER_ROOM)));
    if (!ipb.take(now) || !this.roomBucket.take(now)) return this.kick(ws, REASON.TOO_FAST);
    const n = (this.msgCount.get(from) || 0) + 1;
    this.msgCount.set(from, n);
    if (n % MSG_CHECK_EVERY === 0 && !(await allow(this.env.MSG_LIMIT, fallbackMsgs, ip, MSG_CHECK_EVERY))) return this.kick(ws, REASON.TOO_FAST);
    this.touched = now;
    if (data.length >= 2 && data[0] === OP_DATA) {
      const kind = chatKind(data);
      // b17-chat + b17-harden: chat and voice have their own caps and size limits, and are the first thing dropped when
      // the day's budget runs low (the match's own messages keep flowing).
      if (kind && (!this.chatAllowed || (kind === "chat" && data.length > CHAT_MAX_BYTES) || !this.allowChat(ws, from, kind))) return;
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
      await this.report(1);
      return;
    }
    await this.report(1);
    if (this.touched) { meta.lastActive = this.touched; await this.ctx.storage.put("meta", meta); }
    await this.ctx.storage.setAlarm(now + 60 * 1000);
  }

  // ---- helpers ------------------------------------------------------------------------------------------------------

  // b17-chat: false = drop this chat or voice frame (over its cap); a sender with CHAT_STRIKES drops is closed.
  allowChat(ws, from, kind) {
    let c = this.chatCaps.get(from);
    if (!c) {
      const host = from === HOST;
      c = {
        chat: new Bucket(host ? CHAT_RATE_HOST : CHAT_RATE_JOINER, host ? CHAT_BURST_HOST : CHAT_BURST_JOINER),
        voice: new Bucket(VOICE_RATE, VOICE_BURST),
        strikes: 0,
      };
      this.chatCaps.set(from, c);
    }
    const now = Date.now();
    const ok = kind === "voice" ? c.voice.take(now) && this.roomVoice.take(now) : c.chat.take(now);
    if (ok) return true;
    c.strikes += 1;
    if (c.strikes >= CHAT_STRIKES) this.kick(ws, REASON.TOO_FAST);
    return false;
  }

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
    this.chatCaps.delete(peer);
    this.msgCount.delete(peer);
    const rest = this.ctx.getWebSockets().filter((s) => s !== ws && this.peerOf(s) !== undefined);
    if (peer === HOST) {
      // The host is the lockstep sequencer: without it nobody can continue (host migration is a later step).
      for (const s of rest) this.kick(s, REASON.HOST_LEFT);
      await this.ctx.storage.deleteAll();
      await this.report(0);
      return;
    }
    for (const s of rest) this.trySend(s, frame(OP_LEFT, peer));
  }
}

// b15-zero-cost: one object for the whole relay that keeps today's cost (UTC day, as Cloudflare's free limits reset at
// 00:00 UTC). Rooms add to it once a minute (their alarm) and when they close. "requests" is the estimate of what the
// free plan counts: incoming WebSocket messages / 20, plus one per WebSocket opened, per room made, per alarm and per
// report (https://developers.cloudflare.com/durable-objects/platform/pricing/). Other Workers on the same account are
// not in it (the owner's tools/online/relay-usage.py asks Cloudflare for the account's whole count).
export class Usage extends DurableObject {
  async add(d) {
    const day = new Date().toISOString().slice(0, 10);
    const cur = (await this.ctx.storage.get(day)) || { msgs: 0, connects: 0, alarms: 0, rooms: 0, reports: 0 };
    cur.msgs += d.msgs || 0;
    cur.connects += d.connects || 0;
    cur.alarms += d.alarms || 0;
    cur.rooms += d.rooms || 0;
    cur.reports += 1;
    await this.ctx.storage.put(day, cur);
    const total = Math.ceil(cur.msgs / MESSAGES_PER_REQUEST) + cur.connects + cur.alarms + 2 * cur.rooms + cur.reports;
    if (cur.reports === 1) {
      // A new day: forget days older than a week (a handful of rows).
      const old = new Date(Date.now() - 8 * 86400000).toISOString().slice(0, 10);
      const keys = [...(await this.ctx.storage.list()).keys()].filter((k) => k < old);
      if (keys.length) await this.ctx.storage.delete(keys);
    }
    return total; // b17-harden: rooms learn the day's budget from this answer
  }

  async today() {
    const day = new Date().toISOString().slice(0, 10);
    const c = (await this.ctx.storage.get(day)) || { msgs: 0, connects: 0, alarms: 0, rooms: 0, reports: 0 };
    const requests = Math.ceil(c.msgs / MESSAGES_PER_REQUEST) + c.connects + c.alarms + 2 * c.rooms + c.reports;
    return { day, messages: c.msgs, connects: c.connects, alarms: c.alarms, rooms: c.rooms, requests, free: FREE_REQUESTS_PER_DAY,
      note: "this relay only, rooms still open count at their next minute; resets 00:00 UTC" };
  }
}
