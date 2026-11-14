// npm test (node --test test/protocol.test.js)   (no Cloudflare account needed: covers the pure parts of the relay)
import test from "node:test";
import assert from "node:assert/strict";
import { Bucket, normaliseCode, randomCode, CODE_ALPHABET, CODE_LENGTH } from "../src/protocol.js";

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
