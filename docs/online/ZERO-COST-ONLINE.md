# Online play on zero spend (b15-zero-cost)

Owner ruling: "We're not paying for the relay. We've got to engineer that really, really good."
This page says what online play costs the free relay, what b15 changed, the three ways to host friends, the free tier's
hard walls, and what would one day force paying, with facts. Numbers marked "measured" come from the sim gate
(`Sim.Tests/Net/ZeroCostOnlineTests.cs`, which prints every one) or from Cloudflare's own count; "estimate" means
arithmetic from the measured rates; "unverified" means nobody could check it without breaking something.

## 1. The relay and its free limits

The relay is one Cloudflare Worker plus one Durable Object per room (`server/relay`), on the owner's free Workers plan
(account subdomain `driviaclip`, script `cm-rooms`). Players' games talk to it over WebSockets (`Net/Relay/RelayTransport.cs`).

| Limit (Workers Free) | Value | Source |
|---|---|---|
| Durable Object requests | 100,000 a day, reset 00:00 UTC | [DO pricing](https://developers.cloudflare.com/durable-objects/platform/pricing/) |
| How WebSocket messages count | incoming: 20 messages = 1 request; outgoing: free; protocol pings: free; opening a socket: 1 request | same page, footnote 2 |
| Durable Object time awake | 13,000 GB-s a day (a room object is 128 MB and stays awake through a match) | same page |
| Storage | 5 GB total; 100,000 rows written and 5 million read a day | same page |
| Past a limit | "further operations of that type will fail with an error"; it never bills | same page |
| Worker requests | 100,000 a day (only room creation and joining touch the Worker); past it, error 1027 | [Workers limits](https://developers.cloudflare.com/workers/platform/limits/) |
| One room object | soft limit 1,000 requests a second; 32 MiB per received message | [DO limits](https://developers.cloudflare.com/durable-objects/platform/limits/) |
| Workers Logs | free: 200,000 log events a day, kept 3 days | [Workers Logs](https://developers.cloudflare.com/workers/observability/logs/workers-logs/) |

So the cost of a room is the number of messages the games SEND to the relay, divided by 20, plus one request per
connection, per room made, per housekeeping alarm (one a minute; whether alarms count on Free is not stated, so they are
counted) and per usage report. Bytes cost nothing. Messages the relay sends out cost nothing.

**Unverified:** whether an open WebSocket in the middle of a match is cut when the day's limit is hit, or only new
requests fail. Testing it means using up the real day's allowance, which would stop online play for everyone until
00:00 UTC, so it was not tested. The game is built for the worse case (section 5).

**What Cloudflare's own count says (measured, 2026-11-14 to 11-18, `tools/online/relay-usage.py`):** the busiest day
(11-15) counted 128,281 Durable Object requests. Cloudflare's analytics count every incoming message as one ("The 20:1
ratio does not affect Durable Object metrics and analytics"), so that day billed about 6,400, 6% of the allowance. Quiet
days: 11,000 to 15,000 counted, about 600 to 760 billed (under 1%). Nothing has come near the limit yet.

## 2. What a match sent before b15, and why

Measured by the sim gate (sim-tests run 44245987359): a real room through the game's own online code on a simulated
relay, every message counted by kind (`Net/TrafficMeter.cs`). "Requests per match" = an 8-minute match with a 2-minute
lobby, all players on the relay; "matches a day" = 100,000 divided by that, if nothing else used the relay that day.

| Humans | Relay receives (room) | Messages per player-minute | Bytes per player-minute | Requests per match | Matches a day | Joiners' input delay |
|---|---|---|---|---|---|---|
| 2 | 60.0 a second -> 30.1 | 1,799 -> 902 | 119,930 -> 59,340 | 1,472 -> 754 | 67 -> 132 | 3 -> 4 ticks |
| 4 | 120.0 -> 60.1 | 1,800 -> 902 | 123,552 -> 64,299 | 2,926 -> 1,489 | 34 -> 67 | 3 -> 4 |
| 8 | 240.0 -> 120.2 | 1,800 -> 902 | 125,363 -> 66,779 | 5,834 -> 2,960 | 17 -> 33 | 3 -> 4 |
| 32 | 532.8 -> 483.5 | 999 -> 907 | 92,940 -> 68,889 | 13,030 -> 11,847 | 7 -> 8 | 4 -> 5 |
| 16 (estimate) | | | | | 8 -> 16 | |

Where the messages went before b15: nearly all of them were joiners' inputs, one packet per tick (30 a second) each,
plus the host's bundles, one per tick. 30 ticks a second times every player is the whole bill; everything else (lobby,
chat, rejoin) was under 3%. A 32-human room was already cheaper per player because its joiners sent on a 60 ms gate.
Every arm: every fingerprint MATCHED, no late inputs, no extra waiting.

## 3. What b15 changed (the simulation is untouched)

1. **Joiners pack two ticks of input per packet.** A packet goes when its last tick is odd (ticks 0-1, 2-3, ...), so every
   joiner's packets cover the same ticks, and the host broadcasts its bundles in the same pairs (when a pair's last tick
   is sequenced, or after one tick of waiting at most). On a simulated clock the pairs lined up by themselves; on the real
   relay (online-smoke 44246410965) the host still sent one bundle a tick, so the pairing is explicit since online-smoke
   44247175418, which measured the host's messages halved (1,756 to 909 a minute) with every fingerprint matching. An input waits at
   most one tick (33 ms) longer than before to leave the joiner's computer, and the measured input delay grew by exactly
   one tick. The host reports each joiner's margin from when its newest pair ARRIVED, so the lead policy is not fooled by
   bundles that now go out right after a pair completes. `LockstepOptions.ClientTicksPerPacket`. Three ticks a packet was
   tried for big rooms: 31% fewer messages at 32 humans than two a packet, but felt delay 4 -> 6 ticks, over the one-tick
   rule, so it is off; big rooms gain only 9% (they already sent every two ticks). The sim gate keeps proving it
   (`ABigRoomSendsTwoTicksAPacketBecauseThreeCostsMoreThanOneTickOfDelay`).
2. **Each state fingerprint travels once** on WebSocket and TCP links (they never lose a packet), instead of the newest two
   in every packet. Saves 24 bytes a packet; the receiving side now files a fingerprint in a free slot, so a check still
   waiting is never pushed out. Datagram links keep repeating them.
3. **After the match has ended**, a keep-alive once a second instead of four times.
4. **Local network and direct play cost the relay nothing** (section 4). In a room with both, the host's broadcast goes
   to the relay only while a relay player is in it.

Not changed, on purpose:

- **Delta / run-length encoding of live inputs.** The relay bills messages, not bytes, so smaller packets save nothing
  on the bill. Bytes per player-minute are measured and guarded anyway (section 6). The rejoin log keeps its codec (38%).
- **Sending less while a player stands still or is dead.** In lockstep, the host must hear every tick's input from every
  joiner before anyone may step it. Sending nothing would make the host wait for a late input; promising the same input
  for several future ticks would delay a player's next move by those ticks. Both break the one-tick latency rule.
- **The lobby** (ping and roster once a second, a pong per joiner): measured at about 2% of a match's messages.
- **Chat** (at most one line per 1.5 s per player): a rounding error.
- **Star through the host vs every-to-every.** Already the cheapest shape under this bill: each joiner sends one message
  per packet to the host, and the host one broadcast for everyone (the relay fans it out for free). Every-to-every would
  cost the same from joiners plus nothing saved on the host.

**Rejoin with pairs (found and fixed):** a player who rejoined finished the replay at the newest tick the host had
SEQUENCED, the host's input delay ahead of the host's own stepping, and then played at that edge, so every bundle not
sent the instant it was sequenced (with pairs: every second one) made them wait: 11.7 s and 1.7 s in two online-smoke
runs. Now the rejoiner holds its clock at going live until the host's stepping reaches it (2 or 3 ticks), which is where
a joiner who never left sits (`LockstepOptions.RejoinWaitsForTheHost`). Sim gate, same room with and without the hold:
2 humans waited 1,490 ms vs 0; 8 and 32 humans 0 vs 0; live 25 / 325 / 1,200 ms after the accept
(`ARejoinedPlayerIsLiveSoonAndDoesNotWaitOnThePairedBundles_*`).

## 4. How to host friends, three ways

**A. Room code (anyone, anywhere).** CREATE ROOM, send the 6-character code. This uses the relay's daily allowance.

**B. Same network (free).** Everyone on the same Wi-Fi or wired network. The host chooses CREATE ROOM as usual. The host's
game also listens on TCP port 47800 and announces the room on the network once a second (UDP port 47801). A friend on the
same network who types the code joins the host's computer directly, without the relay, and the join page lists rooms it
hears ("On your network: WILSON's room, code K7Q 2PF"). They can also type the host's address, shown on the room screen
("On the same network they can also type 192.168.1.5"). If the network blocks this (guest Wi-Fi and some school or
office networks keep devices apart), the code still works through the relay, automatically.

**C. By address over the internet (free, needs the host's router).** The friend types the host's public address, for
example `203.0.113.7` (or `203.0.113.7:47800`). For that to work, the host's router must send TCP port 47800 to the
host's computer ("port forwarding", in the router's settings page; search your router's model and "port forwarding").
The host's public address is what any "what is my IP" page shows. If the host's internet provider shares one public
address between many homes (common on mobile and some fibre plans), port forwarding cannot work; use A or B.

The first time a game hosts or joins this way, Windows (and macOS if its firewall is on) asks whether the game may accept
incoming connections. Allow it on private networks. Saying no only turns off B and C; A still works.

In every case the room, the match and the checks are the same: one lockstep protocol, the same version and rules checks,
the same chat, the same drop-and-rejoin. A room can mix all three: one friend by code, two on the couch.

**When the room service cannot make a room** (offline, the day's allowance used, or the service is down), CREATE ROOM
opens a room anyway, for friends on the network or who type the address ("Online rooms are not available right now.
Friends on your network choose JOIN ROOM and type your address.").

**Not built:** NAT hole-punching (friends in two homes with no port forwarding). It needs a STUN server; Cloudflare lists
`stun.cloudflare.com:3478` but its page states no terms of use or free allowance, and Google's public STUN servers have
no published terms either, so by the owner's rule (free public infrastructure only with cited terms) it was not built.
UPnP (the game asking the router to forward the port itself) needs no service and could be added later.

## 5. What happens at the limit

- **The host sees the allowance.** After the relay is redeployed with this branch, the relay counts what its rooms cost
  today (`GET /usage`) and the host's room screen says "Online today: about 30 of 33 matches of 8 players left."
  When less than one match is left, the first press of START warns ("Online rooms are nearly full for today: about 1 match
  left. Friends on your network can still join for free. Press START again to play anyway.").
- **Room creation fails at the limit:** the game opens a network-and-address room instead (section 4).
- **The relay fails during a match** (the limit, or anything else): proven in the sim gate
  (`WhenTheRelayDiesMidMatchDirectPlayersPlayOnAndRelayPlayersBecomeBots`): the host's match goes on; players who came by
  the network or by address keep playing and stay in step; players who came by code become bots at the same tick on
  every remaining machine; on their own screens they see the connection lost. Whether Cloudflare cuts an open socket at the
  limit at all is unverified (section 1).
- **The owner can watch it** any time: `python3 tools/online/relay-usage.py` (today and the last 7 days, matches left).

## 6. The cost guard

`ZeroCostOnlineTests.CostPerPlayerMinuteStaysAtTheBaseline_{2,4,8,32}Humans` run a real room through the game's own
online path on a simulated relay, before and after b15, and fail if, after b15:
messages per player-minute rise more than 10% over the pinned baseline, bytes per player-minute rise more than 10% over
theirs, the cut falls under 40% (8% for 32 humans), the in-game estimate is off by more than 15%, the joiners' felt
input delay grows by more than one tick, or any fingerprint differs. It runs in the sim gate on every batch.

## 7. If paying is ever needed

Facts, from the [Durable Objects pricing page](https://developers.cloudflare.com/durable-objects/platform/pricing/)
and the [Workers pricing page](https://developers.cloudflare.com/workers/platform/pricing/) (checked 2026-11-17):

- **The first wall is requests**: 100,000 a day free (above). The second is **awake time**: 13,000 GB-s a day free. A
  room object is billed at 128 MB for as long as it is awake, and a room in play is always awake, so one 10-minute room
  is about 77 GB-s: about 169 rooms a day of ANY size. For 2-player rooms the two walls are close (132 vs 169).
- **Workers Paid is $5 a month** (minimum) and includes 1 million Durable Object requests and 400,000 GB-s a month, then
  $0.15 per million requests and $12.50 per million GB-s. Incoming WebSocket messages still count 20 to 1.
- So $5 a month covers about 338 eight-player matches a month (requests are the limit there; awake time would allow
  about 5,200), and each match after that costs about $0.0014 (about $0.00044 of requests plus $0.00096 of awake time).
  A 32-player match costs about $0.0028 once over the allowance.
- Paying changes nothing in the game or the relay code: the same Worker, the same rooms. It is a switch on the
  Cloudflare account (Workers & Pages, Plans). Past a free limit nothing is ever billed; it only fails.
- When to consider it: when `tools/online/relay-usage.py` shows days above about 70% of the allowance, or players report
  "Online rooms are full for today". Until then, the free tier plus same-network and address play cost nothing.
