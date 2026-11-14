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
export const MAX_PEERS = 32;
export const MAX_FRAME = 1300;          // bytes; the game never sends more than 1200 of payload
export const RATE_PER_SEC = 90;         // sustained messages per second per socket (the game sends 30 to 35)
export const RATE_BURST = 180;
export const IDLE_MS = 10 * 60 * 1000;  // a room with no traffic for 10 minutes closes
export const HOST_CONNECT_MS = 2 * 60 * 1000;   // the host must connect within 2 minutes of creating the room
export const MAX_ROOM_MS = 4 * 60 * 60 * 1000;  // a room lives at most 4 hours

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
  while (s.length < CODE_LENGTH) s += CODE_ALPHABET[(Math.random() * 31) | 0];
  return s;
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
