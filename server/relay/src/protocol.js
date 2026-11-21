// Wire format between the game and the relay. Mirrored by Assets/_Game/Runtime/Online/RelayFrames.cs; change both together.
// Every WebSocket message is binary. The relay never looks inside game payloads: it only routes them.
//
// game -> relay
//   [0x00][to u8][payload...]   send payload to peer `to` (255 = every other peer in the room)
//   [0x01]                      host only: lock the room (the match started; nobody new may join)
// relay -> game
//   [0x00][from u8][payload...] a payload from peer `from`
//   [0x01][you u8][count u8][peer u8 x count]   welcome: your peer number (0 = host) and who is already in
//   [0x02][peer u8]             a peer joined
//   [0x03][peer u8]             a peer left (closed, timed out or was removed)
//   [0x04][reason u8]           the room is closing for you; the socket closes right after
export const OP_DATA = 0x00;
export const OP_LOCK = 0x01;
export const OP_WELCOME = 0x01;
export const OP_JOINED = 0x02;
export const OP_LEFT = 0x03;
export const OP_CLOSING = 0x04;

export const BROADCAST = 255;
export const HOST = 0;

// Closing reasons (the game turns each into one plain sentence).
export const REASON = {
  NOT_FOUND: 1,     // no such room code
  FULL: 2,          // 32 people already
  STARTED: 3,       // the match already started (no late joining yet)
  HOST_LEFT: 4,     // the host closed the room
  TOO_FAST: 5,      // the client flooded the relay
  TOO_BIG: 6,       // a message over MAX_FRAME
  IDLE: 7,          // nothing happened for IDLE_MS
  BAD_VERSION: 8,   // the game is a different network version
  BAD_REQUEST: 9,   // malformed message
  EXPIRED: 10,      // the room outlived MAX_ROOM_MS
  HOST_NOT_READY: 11, // the room exists (POST /rooms) but the host's game has not connected yet: ask again shortly
};

export const PROTOCOL_VERSION = 1;
// b15-zero-cost: the free plan's daily Durable Object requests and the 20:1 rate for incoming WebSocket messages
// (https://developers.cloudflare.com/durable-objects/platform/pricing/, checked 2026-11-17).
export const FREE_REQUESTS_PER_DAY = 100000;
export const MESSAGES_PER_REQUEST = 20;
export const MAX_PEERS = 32;
export const MAX_FRAME = 1300;          // bytes; the game never sends more than 1200 of payload
export const RATE_PER_SEC = 90;         // sustained messages per second per socket (the game sends 30 to 35)
export const RATE_BURST = 180;
export const IDLE_MS = 10 * 60 * 1000;  // a room with no traffic for 10 minutes closes
export const HOST_CONNECT_MS = 2 * 60 * 1000;   // the host must connect within 2 minutes of creating the room
export const MAX_ROOM_MS = 4 * 60 * 60 * 1000;  // a room lives at most 4 hours

// b17-chat: the relay peeks at ONE byte, the game's message type (payload byte 0), only to cap chat and voice; it still
// never parses game payloads. Types 30 to 39 are chat (34 = voice). Stock games never reach these rates (their own
// limits are lower, Assets/_Game/Net/Chat.cs and Voice.cs); a modified game that floods chat has the excess dropped
// and is closed (TOO_FAST) after CHAT_STRIKES drops. Messages that are dropped are still billed (they arrived), which
// is why the game limits itself first. Backward compatible: no frame changes, old games are unaffected.
export const CHAT_FIRST = 30, CHAT_LAST = 39, VOICE_TYPE = 34;
export const CHAT_RATE_JOINER = 1, CHAT_BURST_JOINER = 4;     // a line a second (the game: one per 1.5 s, burst 3)
export const CHAT_RATE_HOST = 8, CHAT_BURST_HOST = 30;        // the host routes for everyone (the game: 6 a second, burst 24)
export const VOICE_RATE = 10, VOICE_BURST = 20;               // per talker (the game: 8.3 frames a second)
export const VOICE_ROOM_RATE = 40, VOICE_ROOM_BURST = 60;     // the whole room (the host's floor: 4 talkers, 2 a team)
export const CHAT_STRIKES = 30;
export const CHAT_MAX_BYTES = 200;    // a chat line frame is at most ~150 bytes; voice frames up to MAX_FRAME

// b17-harden (Senior Panel C-M1, anyone can burn the free day): per-IP and per-room limits, cheapest check first.
// Per IP, in the Worker before any Durable Object is touched (Workers Rate Limiting binding, approximate and per
// Cloudflare location; when the binding is missing, a best-effort in-memory limiter per isolate is used instead):
export const CREATE_PER_MIN_PER_IP = 3;   // POST /rooms
export const JOIN_PER_MIN_PER_IP = 60;    // WebSocket upgrades (a joiner waiting on its host asks every 1.5 s)
export const MSGS_PER_MIN_PER_IP = 9000;  // 150 a second across every room (8 players behind one home router fit)
export const MSG_CHECK_EVERY = 100;       // a socket asks the per-IP limiter once per this many messages
// Per room, in the room object (exact):
export const IP_RATE_PER_ROOM = 120, IP_BURST_PER_ROOM = 240;   // all sockets of one IP in one room
export const ROOM_RATE = 700, ROOM_BURST = 1400;                // a real 32-human match sends about 485 a second
export const JOINS_PER_MIN_PER_ROOM = 40;
// The day's budget (the Usage object's estimate of billed requests, learned by every room once a minute):
export const BUDGET_CHAT_OFF = 0.6;       // from 60% of the free day, chat and voice frames are dropped (matches go on)
export const BUDGET_NO_NEW_ROOMS = 0.9;   // from 90%, no new rooms (the game falls back to its network/address room)

/** b17-chat: which cap a data frame [0x00][to][type...] falls under: "chat", "voice" or null (everything else). */
export function chatKind(data) {
  if (!data || data.length < 3 || data[0] !== OP_DATA) return null;
  const t = data[2];
  if (t === VOICE_TYPE) return "voice";
  return t >= CHAT_FIRST && t <= CHAT_LAST ? "chat" : null;
}

// Room codes: 6 characters from an alphabet without look-alikes (no 0/O, 1/I/L). 31^6 = about 887 million codes.
export const CODE_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
export const CODE_LENGTH = 6;

export function randomCode(rand = crypto.getRandomValues.bind(crypto)) {
  const bytes = new Uint8Array(CODE_LENGTH * 2);
  rand(bytes);
  let s = "";
  for (let i = 0; s.length < CODE_LENGTH && i < bytes.length; i++) {
    const b = bytes[i];
    if (b >= 248) continue; // 248 = 31 * 8: reject to keep the draw uniform
    s += CODE_ALPHABET[b % 31];
  }
  // b17-harden (panel C-m1): no Math.random fallback; draw again until the code is full (uniform by rejection).
  return s.length === CODE_LENGTH ? s : s + randomCode(rand).slice(0, CODE_LENGTH - s.length);
}

export function normaliseCode(raw) {
  if (typeof raw !== "string") return null;
  const c = raw.trim().toUpperCase().replace(/[\s-]/g, "");
  if (c.length !== CODE_LENGTH) return null;
  for (const ch of c) if (!CODE_ALPHABET.includes(ch)) return null;
  return c;
}

export function randomKey() {
  const b = new Uint8Array(16);
  crypto.getRandomValues(b);
  return [...b].map((x) => x.toString(16).padStart(2, "0")).join("");
}

// Token bucket, one per socket.
export class Bucket {
  constructor(rate = RATE_PER_SEC, burst = RATE_BURST) {
    this.rate = rate;
    this.burst = burst;
    this.tokens = burst;
    this.at = 0;
  }
  take(now) {
    if (this.at === 0) this.at = now;
    this.tokens = Math.min(this.burst, this.tokens + ((now - this.at) / 1000) * this.rate);
    this.at = now;
    if (this.tokens < 1) return false;
    this.tokens -= 1;
    return true;
  }
}

export function frame(op, ...bytes) {
  return new Uint8Array([op, ...bytes]);
}

/** b17-harden: a best-effort fixed-window limiter (per isolate) for when the rate-limit binding is not configured. */
export class WindowLimiter {
  constructor(limit, windowMs = 60000, max = 5000) { this.limit = limit; this.windowMs = windowMs; this.max = max; this.m = new Map(); }
  take(key, now, delta = 1) {
    let e = this.m.get(key);
    if (!e || now - e.at >= this.windowMs) {
      if (this.m.size >= this.max) this.m.clear();
      e = { at: now, n: 0 };
      this.m.set(key, e);
    }
    if (e.n + delta > this.limit) return false;
    e.n += delta;
    return true;
  }
}

/** b17-harden: what the day's budget allows, from the used share of the free requests. */
export function budgetLevel(requests, free = FREE_REQUESTS_PER_DAY) {
  const used = free > 0 ? requests / free : 1;
  return { chat: used < BUDGET_CHAT_OFF, newRooms: used < BUDGET_NO_NEW_ROOMS, used };
}
