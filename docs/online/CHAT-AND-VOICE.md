# Chat and voice (b17)

Player chat for the online demo: quick-chat callouts, optional typed chat, and optional lobby push-to-talk voice.
Everything rides the existing room connection through the free `cm-rooms` relay. None of it can change a match.

## What players get

| | Keyboard | Controller |
|---|---|---|
| Typed chat in the match (off by default) | ENTER to your team, SHIFT+ENTER to everyone; TAB switches while typing; ENTER sends; ESC cancels | none (typing on a pad is miserable) |
| Typed chat in the room (off by default) | T to your team, Y to everyone | none |
| Quick chat (on by default) | none | in the match, hold D-PAD RIGHT (a short tap still zooms the minimap); in the room, hold Y / TRIANGLE; steer with a stick and let go to send; B cancels |
| Scroll-back | PAGE UP / PAGE DOWN / END or the mouse wheel over the chat box, in the room and on the pause screen (the box moves into the pause screen's right margin and shows 18 lines) | |
| Settings | CHAT SETTINGS (room footer, or the button above the chat box on the pause screen): chat, typed chat, word filter, mute everyone, voice, and every player with MUTE and REPORT | same panel |
| Voice (off by default, room only) | hold V | hold LB |

The eight callouts: Group on the hill, Kaiju coming!, Need a Healer, Defend our base, Attack now, Fall back (team),
Nice one!, Good game (everyone). A team callout also drops a short marker on the map through the existing ping
markers (`Pings.Mirror`, view only): the hill, the kaiju (or its shrine before it hatches), your base, or the sender.

## Safety (Senior Panel 2026-11-21: players may be minors, there are no accounts)

- **Typed free text ships OFF.** Callouts and pings are on. Turning typed chat on shows one plain line: "Typed chat is
  on: players you do not know can write to you. Mute or report anyone from the chat panel." With typed chat off, this
  machine neither sends nor shows typed lines (it still shows callouts and the room's own notices).
- **Mute, mute everyone, report.** REPORT (or `/report NAME`) mutes the player at once and appends the evidence (time,
  name, the last 20 lines) to `chat-reports.txt` in the game's data folder on this computer. There is no server, so a
  report goes nowhere else.
- **Word filter** on by default, each player's own switch (it masks what this machine shows; the sender's words travel
  as typed so another player's filter choice is theirs).
- **No links, no markup.** Anything that looks like an address is replaced before sending (`ChatFilter.Transport`);
  labels have no link handler; every `<` and `>` a player types becomes a look-alike (U+2039/U+203A), so no Unity UI or
  TextMeshPro tag can form. Names are cleaned (`ChatFilter.CleanName`); the only markup is the game's own team colour.
- **Rate limits** on the sender (3 lines at once, then one per 1.5 s), on the host (the whole room: 24 at once, then 6
  a second), and on the relay (below). Max 120 characters.
- **Bots never chat**: only confirmed room members are routed. **Chat off** switch in the panel.
- **Flashing:** chat only fades in and out (no flashes), and the callout markers are the existing pings.

## Chat never touches the match

Chat is message type 31 (v1), voice 34. The lockstep layer hands every packet of type 20 and up to the room
(`LockstepSession.Other`), never to the inputs, the state hash, or the replay's input stream.
`ChatB17Tests.TheSameMatchWithAndWithoutChatHasTheSameFingerprintsAndLockstepTraffic` plays the same online match
twice, once silent and once with chat, callouts and voice every 100 ms, and requires the same fingerprints at every
check and byte-for-byte the same lockstep traffic (sim-tests run 44946905769):

| Profile | checks | lockstep messages | lockstep bytes | chat messages |
|---|---|---|---|---|
| Classic | 31 equal | 3504 / 3504 | 471504 / 471504 | 0 / 1608 |
| PhysicsOnly | 31 equal | 3504 / 3504 | 471504 / 471504 | 0 / 1608 |
| Full | 31 equal | 3504 / 3504 | 471504 / 471504 | 0 / 1608 |

The goldens are the base branch's pins: Classic 0x5C912AEED16BC302 / 0x2980068FA0457CC2, PhysicsOnly
0x0836DA5FD21C4854 / 0x780B92B03E6FFEAE, Full 0x48A817A4B2B3F068 / 0x1978E881F4005F28 (16v16 seed 1, and the
whole 5v5 match seed 42 in the slow suite).

**Old clients.** A game older than b17 has no case for 31 or 34 and drops them; this build reads the b11 type 30
line and ignores 32, 33 and 35 to 39 (tested). A later version of 31 may only append fields. The room's HELLO build
check means mixed builds never share a room anyway.

## Relay cost against the free plan

Free limits, fetched 2026-11-21: Durable Objects 100,000 requests a day (page dated 2026-11-13), incoming WebSocket
messages billed 20 to 1, outgoing free; Workers 100,000 requests a day (page dated 2026-11-19). Over a limit,
requests fail until 00:00 UTC; nothing is billed.

Measured in `ChatB17Tests.AFullRoomSpammingAtTheLimitStaysWithinTheStatedShareOfTheFreeRelay` (32 members, every
player trying a line every 50 ms for a minute):

| | before (quiet room) | after (everyone spamming at the limit) |
|---|---|---|
| messages per player-minute | 63.8 | 116.2 |
| bytes per player-minute | 1,382 | 2,456 |
| chat messages a minute (room) | 0 | 1,677 (rule: 1,717 worst case) |
| relay requests a minute (room) | | 83.9 |

The worst case rule: 31 joiners x (3 + 40) + host 24 + 6 x 60 = 1,717 messages a minute = 86 requests a minute.
That is 5.8% of what a 32-player match itself costs (28,800 messages a minute) and 0.84% of the free day for a
10-minute room spamming nonstop. Normal chat is a small fraction of that.

## Voice: the decision

Requirements: zero running cost, no paid service, no player accounts, Windows + macOS, must not touch the sim, and
(Senior Panel) no player's IP address exposed to other players.

1. **Peer-to-peer WebRTC (rejected).** `com.unity.webrtc` is marked deprecated on its own package page (last 3.0.0,
   2025-09). Published measurements put the share of sessions that need a TURN relay at roughly 10 to 22%; with free
   public STUN only, those players simply cannot connect. Cloudflare's TURN costs $0.05/GB outside its SFU, and its
   public STUN has no published terms. And peer-to-peer hands every player the others' IP addresses, which the panel
   ruled out.
2. **Voice frames over the existing relay, everyone, always (rejected).** 16 kHz IMA ADPCM at 120 ms frames is 970
   bytes a frame, 8.33 frames a second; each frame costs 2 relay messages (uplink plus the host's one broadcast). 32
   players talking: 32 x 8.33 x 2 = 533 messages a second = 1,600 requests a minute, more than the match itself;
   a 10-minute room would burn 16% of the free day.
3. **Lobby only, push-to-talk, team only, through the relay (built, OFF by default).** At most 2 talkers a team
   (the host's floor), so at most 4 in a room: 4 x 8.33 x 2 x 60 = 4,000 messages a minute = 200 requests a minute
   worst case, 50 requests per talker-minute; measured 3,502 messages a minute with six people holding the key for a
   whole minute (four got the floor). Voice is off in the match on every machine. The relay never reveals addresses (it
   stamps the sender and forwards), so the panel's IP concern does not arise. No third-party library: the codec is
   IMA ADPCM written in `Net/Voice.cs` (round trip SNR 34.8 dB on a speech-like test signal). Opus would be about 4x
   smaller but needs a native plugin per platform; at 50 requests per talker-minute it is not needed for the budget.

Turning voice on shows: "Voice goes to your team in this room only, through the game's relay: nobody sees your
address, and nothing is recorded." The microphone opens only while the key is held; one press talks at most 15 s.
Muted players (and mute everyone) are never played. The indicator under the chat box names who is talking. A mac
build gets `NSMicrophoneUsageDescription` (Editor `MicUsageDescription`), so macOS asks once; if access is refused or
there is no microphone, the line says so and text chat carries on.

**Privacy note.** The host forwards a voice frame as one broadcast, so enemy clients receive the frame and drop it
(they never play it). A modified client could listen to the other team in the lobby. Unicast to teammates would close
that at about 15x the cost; for a lobby-only demo feature we chose the broadcast.

## Relay hardening (in the PR, NOT deployed)

What it does, cheapest check first, before any storage or Durable Object work:

- Per IP: 3 room creations a minute, 60 joins a minute (a joiner waiting on its host asks every 1.5 s), 9,000
  messages a minute (checked every 100th message). Uses the Workers Rate Limiting binding where the account has it,
  with an in-memory fallback per isolate.
- Per room: a join window (40 joins a minute), a bucket per IP inside the room (120 a second, 240 burst) and one for
  the whole room (700 a second, 1,400 burst); over it the socket is closed.
- Chat frames: over 200 bytes dropped; per-sender caps (joiners 1 a second burst 4, host 8 a second burst 30; voice
  10 a second per talker, 40 a second room-wide); 30 strikes and the socket is closed.
- Daily budget guard: at 60% of the free day the relay drops chat and voice frames (match traffic continues); at 90%
  it refuses new rooms so the matches already running can finish.

**What still works for an attacker.** Anyone can still spend the day's allowance: many IPs (or one patient IP
staying under every limit, for example by opening rooms and sending at just under the caps) add up. The guard makes
chat go first and keeps running matches alive, and LAN / direct play never touches the relay, but the only real fixes
cost money (a paid plan) or need accounts or tokens (a signed join ticket). Per-location rate limits are approximate
by design.

## Deploy step for the coordinator

```
cd server/relay && npx wrangler@4 deploy
```

Deploy when nobody is playing (check `python3 tools/online/relay-usage.py` for the last hour): a deploy restarts the
room objects. The three `[[ratelimits]]` blocks in `wrangler.toml` need the Workers Rate Limiting binding; if the
Free plan refuses them, delete those blocks and deploy again (the in-memory fallback then does the per-IP limits).
The worker stays backward compatible: older games send nothing new, and type 31/34 frames are only relayed messages.

## What needs two real machines with microphones

- Voice end to end: a mac and a Windows PC in one room, both with voice on, each holding the talk key: is it heard,
  how is the delay (expect about 0.3 to 0.5 s), does the mac ask for the microphone once, does a refused mic fall
  back to text with the plain message.
- The quick-chat wheel and the D-pad tap/hold split on a real controller, and the callout markers in a live match.
- Typing in a match while moving the mouse and pressing game keys (that no key reaches the fighter or the HUD).
- Scroll-back and the settings panel over the pause screen at 1080p and 4K.

CI proves the wire, the filters, the limits, the costs and the hashes; online-smoke proves a Windows host and a macOS
joiner exchange a typed line and a callout through the relay (and drop/rejoin still passes).
