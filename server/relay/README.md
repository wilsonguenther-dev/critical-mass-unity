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
See the audit for matches per day at 3, 8 and 32 humans.

## Test locally (no account needed)

```
cd server/relay && npm test                                  # protocol unit tests (node)
npx --yes wrangler@4 dev --local --port 8787 --ip 127.0.0.1  # a local relay at http://127.0.0.1:8787
```
