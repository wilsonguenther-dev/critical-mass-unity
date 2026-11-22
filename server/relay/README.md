# Critical Mass room relay

A tiny relay for online rooms: a Cloudflare Worker plus one Durable Object per room (WebSocket hibernation API).
It forwards bytes between the players in one room and knows nothing about the game. The host's game is the
authority; the relay only stamps who sent each packet, enforces size and rate limits, and closes idle rooms.
No secrets live in this folder (no account id, no keys). It is safe in a public repo.

## Deploy (the owner does this once, on his own Cloudflare account, free Workers plan, no card)

```
cd server/relay && npx wrangler@4 login && npx wrangler@4 deploy
```

`wrangler login` opens a browser sign-in to the Cloudflare account that should own the relay. `deploy` prints the
address, for example `https://cm-rooms.<your-subdomain>.workers.dev`. Then give the game that address in one of
three ways (first match wins):

1. launch flag `-onlineRelay https://cm-rooms.<sub>.workers.dev`
2. environment variable `CM_RELAY_URL`
3. bake it in: set `OnlineConfig.DefaultRelayUrl` in `Assets/_Game/Runtime/Online/OnlineMatch.cs` (an address is
   not a secret; anyone who plays a build can see it).

For the GitHub cross-machine smoke test, also set the repository variable `CM_RELAY_URL` (Settings, Variables).

## Endpoints

| Method | Path | What |
|---|---|---|
| POST | `/rooms` | new room: `201 {"code":"ABC234","key":"..."}`; the key proves the caller is the host |
| GET | `/rooms/CODE/ws?v=1[&key=K]` | WebSocket into the room (host if the key matches, else a joiner) |
| GET | `/health` | `ok` |
| GET | `/usage` | b15: today's (UTC) count from this relay's rooms: `{"day","messages","connects","alarms","rooms","requests","free"}`. `requests` estimates what the free plan counts. |

Frames are binary: client to relay `[0][to][payload]` (to = peer number or 255 for everyone) or `[1]` (host only:
lock the room when the match starts). Relay to client: `[0][from][payload]`, `[1][you]` welcome, `[2][peer]`
joined, `[3][peer]` left, `[4][reason]` closing. Reasons are listed in `src/protocol.js` and turned into plain
sentences by the game (`RelayTransport.ReasonText`).

Limits (src/protocol.js): 32 players a room, 1300 bytes a frame, 90 frames a second per player (burst 180), a
room closes after 10 minutes idle, 2 minutes if the host never connects, and 4 hours at most.

## Free tier (checked 2026-11-14)

- Workers Free: 100,000 requests a day. https://developers.cloudflare.com/workers/platform/limits/
- Durable Objects on Free (SQLite-backed only, which this uses): 100,000 requests a day, 13,000 GB-s a day.
  Incoming WebSocket messages are billed 20 to 1 (20 messages = 1 request); outgoing messages are free.
  https://developers.cloudflare.com/durable-objects/platform/pricing/
- Per object soft limit 1,000 requests a second. https://developers.cloudflare.com/durable-objects/platform/limits/

On the free plan, going over a daily limit makes requests fail until the next day (UTC); it never bills.
Matches a day by room size, measured before and after b15's traffic cut: `docs/online/ZERO-COST-ONLINE.md`.

b17 (checked again 2026-11-21, same numbers): chat and lobby voice caps, per-IP and per-room limits, and a daily
budget guard (chat dropped at 60% of the free day, no new rooms at 90%). Numbers, what an attacker can still do, and
the deploy step: `docs/online/CHAT-AND-VOICE.md`. The `[[ratelimits]]` blocks in `wrangler.toml` may be deleted if a
Free-plan deploy refuses them; the code falls back to in-memory limits.

## Watching the allowance (b15)

- `python3 tools/online/relay-usage.py` asks Cloudflare for the account's Durable Object requests per day (the GraphQL
  Analytics API, with the login `wrangler login` saved on this Mac; nothing is printed or stored). Works today, no deploy.
- After the next deploy: `GET /usage` returns the relay's own count; the host's game reads it once per room and shows
  "Online today: about N of M matches left" (and warns before START when the day is nearly used).
- `wrangler.toml` turns Workers Logs on at a 1% sample (free plan: 200,000 log events a day, 3 days kept).

Deploying this version adds one Durable Object class (`Usage`, migration `v2`). A deploy restarts the room objects (as
any new Durable Object code does), so anyone in a room at that moment may be disconnected: deploy when nobody is
playing (check `relay-usage.py` for the last hour).

## Test locally (no account needed)

```
cd server/relay && npm test                                  # protocol unit tests (node)
npx --yes wrangler@4 dev --local --port 8787 --ip 127.0.0.1  # a local relay at http://127.0.0.1:8787
```
