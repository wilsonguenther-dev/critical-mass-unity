// npm test (node --test test/protocol.test.js)   (no Cloudflare account needed: covers the pure parts of the relay)
import test from "node:test";
import assert from "node:assert/strict";
import { Bucket, normaliseCode, randomCode, CODE_ALPHABET, CODE_LENGTH, chatKind, CHAT_RATE_JOINER, CHAT_BURST_JOINER, VOICE_RATE, VOICE_BURST, WindowLimiter, budgetLevel, ROOM_RATE, IP_RATE_PER_ROOM, MSGS_PER_MIN_PER_IP } from "../src/protocol.js";

test("room codes use only the unambiguous alphabet", () => {
  for (let i = 0; i < 2000; i++) {
    const c = randomCode();
    assert.equal(c.length, CODE_LENGTH);
    for (const ch of c) assert.ok(CODE_ALPHABET.includes(ch), c);
  }
});

test("typed codes are cleaned up, junk is refused", () => {
  assert.equal(normaliseCode(" k7q2pf "), "K7Q2PF");
  assert.equal(normaliseCode("K7Q-2PF"), "K7Q2PF");
  assert.equal(normaliseCode("K7Q2P"), null);
  assert.equal(normaliseCode("K7Q2P0"), null); // 0 is not in the alphabet
  assert.equal(normaliseCode("../../x"), null);
  assert.equal(normaliseCode(null), null);
});

test("the token bucket allows the game's rate and stops a flood", () => {
  const b = new Bucket(90, 180);
  let t = 1000, ok = 0;
  for (let i = 0; i < 35 * 60; i++) { t += 1000 / 35; if (b.take(t)) ok++; }
  assert.equal(ok, 35 * 60, "35 messages a second for a minute always pass");
  let flood = 0;
  for (let i = 0; i < 1000; i++) if (b.take(t)) flood++;
  assert.ok(flood <= 181, `a burst of 1000 at once is cut to the burst size (passed ${flood})`);
});

test("b17-chat: only chat-block payloads fall under the chat caps", () => {
  assert.equal(chatKind(new Uint8Array([0, 255, 31, 1])), "chat");
  assert.equal(chatKind(new Uint8Array([0, 0, 30])), "chat");
  assert.equal(chatKind(new Uint8Array([0, 0, 39])), "chat");
  assert.equal(chatKind(new Uint8Array([0, 255, 34, 1])), "voice");
  assert.equal(chatKind(new Uint8Array([0, 255, 1, 0])), null, "lockstep inputs are never capped as chat");
  assert.equal(chatKind(new Uint8Array([0, 255, 21])), null, "room messages neither");
  assert.equal(chatKind(new Uint8Array([0, 255, 40])), null);
  assert.equal(chatKind(new Uint8Array([1])), null);
  assert.equal(chatKind(new Uint8Array([0, 255])), null);
});

test("b17-chat: the relay's chat cap passes the game's own rate and cuts a flood", () => {
  // The game: a burst of 3, then one line per 1.5 s. Ten minutes of that always passes the joiner cap.
  const b = new Bucket(CHAT_RATE_JOINER, CHAT_BURST_JOINER);
  let t = 1000, ok = 0, sent = 0;
  for (let i = 0; i < 3; i++) { sent++; if (b.take(t)) ok++; }
  for (let i = 0; i < 400; i++) { t += 1500; sent++; if (b.take(t)) ok++; }
  assert.equal(ok, sent);
  let flood = 0;
  for (let i = 0; i < 100; i++) if (b.take(t)) flood++;
  assert.ok(flood <= CHAT_BURST_JOINER + 1, `a flood is cut to the burst (passed ${flood})`);
  // Voice: 120 ms frames (8.3 a second) for a minute pass.
  const v = new Bucket(VOICE_RATE, VOICE_BURST);
  let vok = 0;
  for (let i = 0; i < 500; i++) { t += 120; if (v.take(t)) vok++; }
  assert.equal(vok, 500);
});

test("b17-harden: limits fit real play and the budget guard drops chat before matches", () => {
  // A real 32-human match sends about 485 messages a second; a joiner about 15; eight players behind one router 120.
  assert.ok(ROOM_RATE >= 485 * 1.3, "a full match fits the room ceiling");
  assert.ok(IP_RATE_PER_ROOM >= 8 * 15, "eight players on one home address fit");
  assert.ok(MSGS_PER_MIN_PER_IP / 60 >= IP_RATE_PER_ROOM, "the cross-room per-IP limit is not tighter than one room");
  const w = new WindowLimiter(3, 60000);
  assert.ok(w.take("a", 0) && w.take("a", 1) && w.take("a", 2));
  assert.equal(w.take("a", 3), false, "a fourth room in a minute from one address is refused");
  assert.ok(w.take("b", 3), "another address is not");
  assert.ok(w.take("a", 60001), "the next minute opens again");
  assert.deepEqual([budgetLevel(10000).chat, budgetLevel(10000).newRooms], [true, true]);
  assert.deepEqual([budgetLevel(65000).chat, budgetLevel(65000).newRooms], [false, true], "60%: chat goes first");
  assert.deepEqual([budgetLevel(95000).chat, budgetLevel(95000).newRooms], [false, false], "90%: no new rooms");
});

test("b17-harden: room codes never fall back to Math.random", () => {
  let calls = 0;
  const rand = (b) => { calls++; for (let i = 0; i < b.length; i++) b[i] = calls === 1 ? 250 : 3; return b; };
  const c = randomCode(rand);
  assert.equal(c.length, CODE_LENGTH);
  assert.ok(calls >= 2, "an all-rejected draw draws again");
});
