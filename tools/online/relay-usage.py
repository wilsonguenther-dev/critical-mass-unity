#!/usr/bin/env python3
"""b15-zero-cost: how much of today's free relay allowance is used, from Cloudflare's own count (GraphQL Analytics API).

Reads the login that `npx wrangler@4 login` already saved on this computer (never printed, never written anywhere) and
asks Cloudflare for the Durable Object requests of the account, per day and per hour, then turns them into matches left.

    python3 tools/online/relay-usage.py            # today and the last 7 days
    python3 tools/online/relay-usage.py --json     # one JSON object (for a script)

No account, key or service beyond the one the relay already runs on. If the saved login has expired, run
`cd server/relay && npx wrangler@4 whoami` once (it refreshes the login), then run this again.

Numbers: Cloudflare's analytics count every incoming WebSocket message as one request; the free plan bills them 20 to 1
(https://developers.cloudflare.com/durable-objects/platform/pricing/, footnote 2: "The 20:1 ratio does not affect Durable
Object metrics and analytics, which reflect actual usage"). So "billed" below is an estimate: (requests - other) / 20 +
other, where "other" (connections, alarms, room creation) is estimated from the relay's own /usage count when given.
"""
import datetime
import json
import os
import re
import sys
import urllib.request

FREE = 100000
PER_MSG = 20
# Requests one 8-minute match costs after b15 (RelayBudget.RequestsPerMatch, ZeroCostOnlineTests prints the measured ones).
PER_MATCH = {2: 754, 4: 1489, 8: 2960, 32: 11847}


def wrangler_token():
    for p in [os.path.expanduser("~/Library/Preferences/.wrangler/config/default.toml"),
              os.path.expanduser("~/.config/.wrangler/config/default.toml"),
              os.path.expanduser("~/.wrangler/config/default.toml")]:
        if os.path.exists(p):
            m = re.search(r'^oauth_token\s*=\s*"([^"]+)"', open(p).read(), re.M)
            if m:
                return m.group(1)
    sys.exit("relay-usage: no saved Cloudflare login on this computer (run: cd server/relay && npx wrangler@4 login)")


def call(tok, url, body=None):
    req = urllib.request.Request(url, data=json.dumps(body).encode() if body else None,
                                 headers={"Authorization": "Bearer " + tok, "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        if e.code in (401, 403):
            sys.exit("relay-usage: the saved login has expired; run `cd server/relay && npx wrangler@4 whoami` once, then again")
        sys.exit("relay-usage: Cloudflare answered %d" % e.code)


def main():
    as_json = "--json" in sys.argv
    tok = wrangler_token()
    accts = call(tok, "https://api.cloudflare.com/client/v4/accounts").get("result") or []
    acct = None
    for a in accts:
        sub = call(tok, "https://api.cloudflare.com/client/v4/accounts/%s/workers/subdomain" % a["id"]).get("result") or {}
        if sub.get("subdomain") == "driviaclip" or len(accts) == 1:
            acct = a["id"]
            break
    if not acct:
        sys.exit("relay-usage: no account with the relay's workers.dev subdomain")
    since = (datetime.datetime.utcnow().date() - datetime.timedelta(days=7)).isoformat()
    q = """query($a:String!,$d:Date!){viewer{accounts(filter:{accountTag:$a}){
      days: durableObjectsInvocationsAdaptiveGroups(filter:{date_geq:$d},limit:100,orderBy:[date_ASC]){dimensions{date scriptName} sum{requests}}
    }}}"""
    r = call(tok, "https://api.cloudflare.com/client/v4/graphql", {"query": q, "variables": {"a": acct, "d": since}})
    if r.get("errors"):
        sys.exit("relay-usage: " + json.dumps(r["errors"])[:300])
    rows = r["data"]["viewer"]["accounts"][0]["days"]
    days = {}
    for x in rows:
        d = x["dimensions"]["date"]
        days.setdefault(d, {})[x["dimensions"]["scriptName"]] = x["sum"]["requests"]
    today = datetime.datetime.utcnow().date().isoformat()
    out = {"today_utc": today, "free_per_day": FREE, "days": {}}
    for d in sorted(days):
        raw = sum(days[d].values())
        billed = int(raw / PER_MSG) + 1   # nearly all relay requests are WebSocket messages
        out["days"][d] = {"raw_requests": raw, "billed_estimate": billed, "by_script": days[d],
                          "matches_left_8_players": max(0, (FREE - billed) // PER_MATCH[8]) if d == today else None}
    if as_json:
        print(json.dumps(out, indent=1))
        return
    print("Durable Object requests on the relay's Cloudflare account (UTC days; free plan: %d a day, resets 00:00 UTC)" % FREE)
    for d, v in out["days"].items():
        print("  %s  %7d counted by Cloudflare, about %6d billed (%.1f%% of the day)%s" % (
            d, v["raw_requests"], v["billed_estimate"], 100.0 * v["billed_estimate"] / FREE, "  <- today" if d == today else ""))
    t = out["days"].get(today)
    used = t["billed_estimate"] if t else 0
    print("Matches left today, about:", ", ".join("%d players: %d" % (h, max(0, (FREE - used) // c)) for h, c in sorted(PER_MATCH.items())))


if __name__ == "__main__":
    main()
