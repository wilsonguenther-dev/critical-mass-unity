#!/usr/bin/env python3
"""Player-seat telemetry summary (px lane T, 2026-11-22). Python 3 standard library only.

Reads player-seat JSONL logs (tools/px-telemetry/run.sh headless runs, or the Unity logger's
persistentDataPath/PlayerSeatLogs/*.jsonl) and prints plain-text tables per class.

  summarize.py PATH [PATH ...]            files or folders (folders: every *.jsonl inside, recursively)
  summarize.py PATH --by-opponent         one block per class x opponent (duel logs)
  summarize.py --compare A B              diff two runs (before / after a tuning change); A and B are files or folders
  summarize.py PATH --dead 20             list up to N dead presses per class (default 12)

Schema and meaning of every field: docs/testing/PLAYER-SEAT-TELEMETRY.md.
"""
import json
import os
import re
import statistics
import sys
from collections import Counter, defaultdict

BUTTONS = ["attack", "ability", "dig_place", "interact", "dodge", "cancel", "jump"]
SUPPORTED_SCHEMA = 1


def files_of(paths):
    out = []
    for p in paths:
        if os.path.isdir(p):
            for root, _, names in os.walk(p):
                out += [os.path.join(root, n) for n in sorted(names) if n.endswith(".jsonl")]
        elif os.path.isfile(p):
            out.append(p)
        else:
            sys.exit("summarize: no such file or folder: " + p)
    return sorted(out)


def runs_of(path):
    """Yield (header, records, summary) for every run (header ... summary) in one file."""
    head, recs = None, []
    with open(path, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except ValueError:
                print("summarize: %s:%d: not JSON, skipped" % (path, n), file=sys.stderr)
                continue
            k = r.get("kind")
            if k == "header":
                if head is not None:
                    yield head, recs, None
                head, recs = r, []
                if r.get("schema", 0) > SUPPORTED_SCHEMA:
                    print("summarize: %s: schema %s is newer than this script (%d); numbers may be wrong"
                          % (path, r.get("schema"), SUPPORTED_SCHEMA), file=sys.stderr)
            elif k == "summary":
                yield head or {}, recs, r
                head, recs = None, []
            else:
                recs.append(r)
    if head is not None:
        yield head, recs, None


def opponent(head):
    m = re.search(r"duel vs (\w+)", head.get("mode", ""))
    return m.group(1) if m else "-"


class Stats:
    def __init__(self):
        self.runs = 0
        self.seconds = 0.0
        self.presses = Counter()
        self.res = defaultdict(Counter)          # button -> resolution -> n
        self.refused = defaultdict(Counter)      # button -> reason_name -> n
        self.swings = 0
        self.outcomes = Counter()
        self.miss = Counter()
        self.miss_by = defaultdict(list)         # reason -> magnitudes (u or degrees)
        self.blocked = Counter()
        self.crit = Counter()
        self.damage = 0.0
        self.kills = 0
        self.ttk = []
        self.wins = self.losses = 0
        self.taken = 0
        self.taken_dmg = 0.0
        self.taken_side = Counter()
        self.cue_presses = 0
        self.cue_known = False
        self.cue_delays = []
        self.dead = []                           # (file, tick, button, resolved, why)
        self.silent = Counter()                  # button -> presses the sim answered with nothing / no reason
        self.move_ticks = 0
        self.stuck_ticks = 0
        self.reversals = 0
        self.surface = defaultdict(lambda: [0, 0, 0.0, 0])   # surface -> [ticks, stuck ticks, speed share sum, samples]
        self.world = Counter()

    def add(self, path, head, recs, summ):
        hz = head.get("sim_hz", 30) or 30
        self.runs += 1
        t0 = head.get("tick", 0)
        t1 = summ.get("tick", t0) if summ else max([r.get("tick", t0) for r in recs] + [t0])
        self.seconds += max(0, t1 - t0) / hz
        first_kill = None
        for r in recs:
            k = r.get("kind")
            if k in ("press", "repeat"):
                b = r.get("button", "?")
                res = r.get("resolved", "nothing")
                if k == "press":
                    self.presses[b] += 1
                    self.res[b][res] += 1
                    if res == "refused":
                        self.refused[b][r.get("reason_name", "unknown")] += 1
                    why = None
                    if res == "nothing":
                        why = "the sim raised nothing"
                    elif res == "refused" and (r.get("reason_name") in (None, "unknown") or str(r.get("reason_name", "")).startswith("code_")):
                        why = "refused without a known reason"
                    if why:
                        self.silent[b] += 1
                    cues = r.get("cues") or []
                    if r.get("cue_source") == "ledger":
                        self.cue_known = True
                        if cues:
                            self.cue_presses += 1
                            self.cue_delays += [c["delay_ms"] for c in cues if c.get("delay_ms") is not None]
                        elif res != "dead":
                            why = (why + "; " if why else "") + "no feedback cue"
                    if why and res != "dead":
                        tgt = r.get("target") or {}
                        self.dead.append((os.path.basename(path), r.get("tick"), b, res, why,
                                          "target %s at %.2f u (reach %.2f)" % (tgt.get("class"), tgt.get("dist", 0), tgt.get("reach", 0)) if tgt else "no enemy"))
                s = r.get("swing")
                if s:
                    self.swings += 1
                    o = s.get("outcome", "pending")
                    self.outcomes[o] += 1
                    if o == "miss":
                        mr = s.get("miss_reason", "other")
                        self.miss[mr] += 1
                        if mr in ("out_of_range", "range_and_arc") and s.get("out_of_range_by") is not None:
                            self.miss_by["out_of_range"].append(s["out_of_range_by"])
                        if mr in ("outside_arc", "range_and_arc") and s.get("outside_arc_by_deg") is not None:
                            self.miss_by["outside_arc"].append(s["outside_arc_by_deg"])
                    if o == "blocked":
                        self.blocked[s.get("blocked_name", "?")] += 1
                    if s.get("critical", "none") != "none":
                        self.crit[s["critical"]] += 1
                    self.damage += s.get("damage", 0) or 0
                    if s.get("kills", 0):
                        self.kills += s["kills"]
                        if first_kill is None:
                            first_kill = (s.get("contact_tick", r["tick"]) - t0) / hz
                else:
                    self.damage += r.get("damage", 0) or 0
                    if r.get("kills", 0):
                        self.kills += r["kills"]
                        if first_kill is None:
                            first_kill = (r["tick"] - t0) / hz
                for key, v in (r.get("world") or {}).items():
                    if v:
                        self.world[key] += v
            elif k == "hit_taken":
                self.taken += 1
                self.taken_dmg += r.get("damage", 0) or 0
                self.taken_side[r.get("from_side", "unknown")] += 1
            elif k == "move":
                n = r.get("ticks", 0)
                self.move_ticks += n
                if r.get("stuck"):
                    self.stuck_ticks += n
                self.reversals += r.get("reversals", 0)
                sf = self.surface[r.get("surface", "?")]
                sf[0] += n
                sf[1] += n if r.get("stuck") else 0
                sf[2] += r.get("speed_share", 0) or 0
                sf[3] += 1
        if first_kill is not None:
            self.ttk.append(first_kill)
        if summ:
            if summ.get("kills", 0) > 0 and summ.get("alive"):
                self.wins += 1
            elif not summ.get("alive", True):
                self.losses += 1

    # derived numbers (used by the tables and --compare)
    def numbers(self):
        mins = self.seconds / 60 if self.seconds > 0 else float("nan")
        sw = self.swings or float("nan")
        tp = sum(self.presses.values())
        d = {
            "runs": self.runs, "minutes": round(self.seconds / 60, 2), "presses": tp,
            "land_rate": self.outcomes["hit"] / sw, "miss_rate": self.outcomes["miss"] / sw,
            "interrupted_rate": self.outcomes["interrupted"] / sw, "blocked_rate": self.outcomes["blocked"] / sw,
            "damage_per_min": self.damage / mins, "kills": self.kills, "wins": self.wins, "losses": self.losses,
            "median_ttk_s": statistics.median(self.ttk) if self.ttk else float("nan"),
            "hits_taken_per_min": self.taken / mins, "damage_taken_per_min": self.taken_dmg / mins,
            "silent_presses": sum(self.silent.values()),
            "stuck_share": self.stuck_ticks / self.move_ticks if self.move_ticks else float("nan"),
            "reversals_per_min": self.reversals / (self.move_ticks / 30 / 60) if self.move_ticks else float("nan"),
        }
        for mr in ("out_of_range", "outside_arc", "range_and_arc", "target_dodged", "no_target", "other"):
            d["miss_" + mr] = self.miss[mr] / sw
        for b in BUTTONS:
            n = self.presses[b]
            if n:
                d["refused_share_" + b] = self.res[b]["refused"] / n
                d["nothing_share_" + b] = self.res[b]["nothing"] / n
        if self.cue_known:
            d["cue_share"] = self.cue_presses / tp if tp else float("nan")
            d["median_cue_delay_ms"] = statistics.median(self.cue_delays) if self.cue_delays else float("nan")
        return d


def pct(x):
    return "  -  " if x != x else "%4.0f%%" % (100 * x)


def num(x, f="%.1f"):
    return "-" if x != x else f % x


def collect(paths, by_opponent):
    groups = defaultdict(Stats)
    for p in files_of(paths):
        for head, recs, summ in runs_of(p):
            key = head.get("class", "?") + ((" vs " + opponent(head)) if by_opponent else "")
            groups[key].add(p, head, recs, summ)
    return groups


def table(groups, dead_n):
    for key in sorted(groups):
        s = groups[key]
        d = s.numbers()
        print("=" * 100)
        print("%s   runs %d   %.1f min of play   presses %d" % (key, s.runs, s.seconds / 60, d["presses"]))
        print("-" * 100)
        print("%-10s %7s %9s %9s %9s %9s   %s" % ("button", "presses", "answered", "buffered", "refused", "nothing", "resolved to (top) / refused because"))
        for b in BUTTONS:
            n = s.presses[b]
            if not n:
                continue
            r = s.res[b]
            ans = n - r["refused"] - r["nothing"] - r["dead"]
            top = ", ".join("%s %d" % kv for kv in r.most_common(4))
            why = ", ".join("%s %d" % kv for kv in s.refused[b].most_common(4))
            print("%-10s %7d %9s %9s %9s %9s   %s%s" % (b, n, pct(ans / n), pct(r["buffered"] / n), pct(r["refused"] / n), pct(r["nothing"] / n), top, ("  | refused: " + why) if why else ""))
        print("-" * 100)
        print("swings %d   land %s   miss %s   blocked %s   interrupted %s   cancelled %s   (critical: %s)" % (
            s.swings, pct(d["land_rate"]), pct(d["miss_rate"]), pct(d["blocked_rate"]), pct(d["interrupted_rate"]),
            pct(s.outcomes["cancelled"] / s.swings if s.swings else float("nan")), ", ".join("%s %d" % kv for kv in s.crit.most_common()) or "none"))
        if s.miss:
            print("miss reasons, ranked (share of all swings):")
            for mr, n in s.miss.most_common():
                extra = ""
                if mr in ("out_of_range", "range_and_arc") and s.miss_by["out_of_range"]:
                    extra += "  out of range by median %.2f u" % statistics.median(s.miss_by["out_of_range"])
                if mr in ("outside_arc", "range_and_arc") and s.miss_by["outside_arc"]:
                    extra += "  outside the arc by median %.0f deg" % statistics.median(s.miss_by["outside_arc"])
                print("   %-15s %5d  %s%s" % (mr, n, pct(n / s.swings), extra))
        if s.blocked:
            print("blocked by: " + ", ".join("%s %d" % kv for kv in s.blocked.most_common()))
        print("damage/min %s   kills %d   wins %d  losses %d   median time to first kill %s s   hits taken/min %s (%s dmg/min; from %s)" % (
            num(d["damage_per_min"]), s.kills, s.wins, s.losses, num(d["median_ttk_s"]), num(d["hits_taken_per_min"]), num(d["damage_taken_per_min"]),
            ", ".join("%s %d" % kv for kv in s.taken_side.most_common()) or "-"))
        if s.world:
            print("world: " + ", ".join("%s %s" % (k, num(v, "%.0f")) for k, v in sorted(s.world.items())))
        if s.move_ticks:
            print("movement: %.1f s held, stuck %s of it, %s reversals/min" % (s.move_ticks / 30, pct(d["stuck_share"]), num(d["reversals_per_min"])))
            for sf, (t, st, ss, n) in sorted(s.surface.items(), key=lambda kv: -kv[1][0]):
                print("   %-11s %6.1f s  stuck %s  mean speed %s of walk" % (sf, t / 30, pct(st / t if t else float("nan")), pct(ss / n if n else float("nan"))))
        if s.cue_known:
            print("feedback: %s of presses raised at least one cue; median cue delay %s ms" % (pct(d["cue_share"]), num(d["median_cue_delay_ms"], "%.0f")))
        else:
            print("feedback: no ledger in these logs (headless): dead = silent in the sim only")
        print("dead presses (silent in the sim%s): %d" % (", or no cue" if s.cue_known else "", len(s.dead)))
        for row in s.dead[:dead_n]:
            print("   %s tick %s  %-9s -> %-8s  %s; %s" % row)
        if len(s.dead) > dead_n:
            print("   ... %d more" % (len(s.dead) - dead_n))


def compare(a, b, by_opponent):
    ga, gb = collect([a], by_opponent), collect([b], by_opponent)
    for key in sorted(set(ga) | set(gb)):
        da = ga[key].numbers() if key in ga else {}
        db = gb[key].numbers() if key in gb else {}
        print("=" * 78)
        print("%s   A = %s   B = %s" % (key, a, b))
        print("%-28s %12s %12s %12s" % ("number", "A", "B", "B - A"))
        for k in sorted(set(da) | set(db)):
            va, vb = da.get(k, float("nan")), db.get(k, float("nan"))
            dl = vb - va if va == va and vb == vb else float("nan")
            share = k.endswith("rate") or "share" in k or k.startswith("miss_")
            f = (lambda x: pct(x)) if share else (lambda x: num(x, "%.2f"))
            mark = "" if dl != dl or abs(dl) < 1e-9 else "  <"
            print("%-28s %12s %12s %12s%s" % (k, f(va), f(vb), (("%+.0f pt" % (100 * dl)) if share and dl == dl else num(dl, "%+.2f")), mark))


def main(argv):
    by_opp = "--by-opponent" in argv
    argv = [x for x in argv if x != "--by-opponent"]
    dead_n = 12
    if "--dead" in argv:
        i = argv.index("--dead")
        dead_n = int(argv[i + 1])
        del argv[i:i + 2]
    if argv and argv[0] == "--compare":
        if len(argv) != 3:
            sys.exit("usage: summarize.py --compare A B")
        compare(argv[1], argv[2], by_opp)
        return 0
    if not argv or argv[0] in ("-h", "--help"):
        print(__doc__)
        return 0 if argv else 64
    table(collect(argv, by_opp), dead_n)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
