#!/usr/bin/env python3
"""b16 duel-order lever search (tools/balance, run by the ci/balance/b16-* workflow; python 3, no packages).

  b16-arms.py grid  OUT.tsv                 every combination of the levers below, one arm a line (name TAB switches)
  b16-arms.py pick  SWEEP_DIR OUT.tsv [K]   reads every duel-sweep CSV in SWEEP_DIR, keeps the arms whose duel order holds,
                                            ranks them by lever cost (fewest and smallest changes first) and writes the K
                                            cheapest lever sets at both Warrior speed arms, plus the Warrior trade arms for
                                            the cheapest one, for the 72-seed runs
  b16-arms.py table REPORT_DIR...           one Markdown table of the target rows of each 72-seed report (balance-report.json)
"""
import csv, glob, itertools, json, os, sys

CF = "Depth.CombatFeel."
# (key, [values]; the first value is the shipped game, aa2ecfef)
LEVERS = [
    ("sunder", ["true", "false"], CF + "SunderBreaksArmour={}"),
    ("pierce", ["true", "false"], CF + "MinerPierceWarrior={}"),
    ("mwind", ["0.35", "0.40"], CF + "Classes[Miner].AttackWindupSeconds={}"),
    ("wdmg", ["21", "24", "27"], CF + "Classes[Warrior].AttackDamage={}"),
    ("whp", ["115", "130", "145"], CF + "Classes[Warrior].MaxHp={}"),
    ("refund", ["0", "0.6"], CF + "StaggerCancelRefund={}"),
    ("hstag", ["on", "off"], None),
    ("mdmg", ["17", "20"], CF + "Classes[Miner].AttackDamage={}"),
    ("ddmg", ["12", "15"], CF + "Classes[Demolitions].AttackDamage={}"),
    ("wspd", ["3.8", "3.61", "3.5"], CF + "Classes[Warrior].MoveSpeed={}"),
]
SHIPPED = {k: v[0] for k, v, _ in LEVERS}
SHIPPED["mwind"] = "0.20"


def switches(arm):
    out = []
    for k, vals, fmt in LEVERS:
        v = arm[k]
        if k == "hstag":
            if v == "off":
                out.append(CF + "StrikeStaggers=true,true,true,true,true,false")
            continue
        if v == SHIPPED[k]:
            continue
        out.append(fmt.format(v))
    return ";".join(out)


def name(arm):
    return "_".join(k + "-" + arm[k] for k, _, _ in LEVERS)


def grid(out):
    keys = [k for k, _, _ in LEVERS]
    with open(out, "w") as f:
        for combo in itertools.product(*[v for _, v, _ in LEVERS]):
            arm = dict(zip(keys, combo))
            f.write(name(arm) + "\t" + switches(arm) + "\n")


def parse(nm):
    return dict(p.split("-", 1) for p in nm.split("_"))


def cost(a):
    c = 0.0
    c += a["sunder"] == "false"
    c += a["pierce"] == "false"
    c += 0.2 if a["mwind"] == "0.40" else 0
    c += (int(a["wdmg"]) - 21) / 3.0
    c += (int(a["whp"]) - 115) / 15.0
    c += 1.5 if a["refund"] != "0" else 0
    c += a["hstag"] == "off"
    c += a["mdmg"] != "17"
    c += a["ddmg"] != "12"
    return c


def pick(sweep_dir, out, k):
    rows = []
    for p in glob.glob(os.path.join(sweep_dir, "**", "*.csv"), recursive=True):
        with open(p) as f:
            rows += list(csv.DictReader(f))
    by = {r["arm"]: r for r in rows}
    print("[pick] %d arms read" % len(by))
    ok = [r for r in rows if r["violations"] == "0"]
    print("[pick] %d arms hold the duel order" % len(ok))
    # a lever set = the arm without its Warrior speed; it must hold at both reduced speeds
    sets = {}
    for r in ok:
        a = parse(r["arm"])
        if a["wspd"] == "3.8":
            continue
        key = tuple((kk, a[kk]) for kk, _, _ in LEVERS if kk != "wspd")
        sets.setdefault(key, set()).add(a["wspd"])
    both = [s for s, sp in sets.items() if {"3.61", "3.5"} <= sp]
    print("[pick] %d lever sets hold at both Warrior speed arms (5%% and 8%% slower)" % len(both))
    pool = both if both else list(sets.keys())
    if not pool:
        # nothing holds: take the arms with the fewest violations so the 72-seed runs still say where it stands
        best = sorted(rows, key=lambda r: (int(r["violations"]), cost(parse(r["arm"]))))[:k]
        pool = [tuple((kk, parse(r["arm"])[kk]) for kk, _, _ in LEVERS if kk != "wspd") for r in best]
        print("[pick] none holds; the %d arms with the fewest violations instead" % len(pool))
    pool.sort(key=lambda s: cost(dict(s)))
    seen, top = set(), []
    for s in pool:
        if s in seen:
            continue
        seen.add(s)
        top.append(dict(s))
        if len(top) >= k:
            break
    with open(out, "w") as f:
        f.write("base\t\n")
        for i, a in enumerate(top):
            for spd in ("3.61", "3.5"):
                arm = dict(a, wspd=spd)
                f.write("c%d-%s\t%s\n" % (i, spd, switches(arm)))
            print("[pick] c%d cost %.2f: %s" % (i, cost(a), switches(dict(a, wspd="3.8")) or "(shipped)"))
        if top:
            a = dict(top[0], wspd="3.61")
            sw = switches(a)
            f.write("c0-3.61-outnumbered\t%s;%sOutnumberedBreaksArmour=true\n" % (sw, CF))
            f.write("c0-3.61-whiff\t%s;%sWhiffExtraSeconds=0.25,0,0,0,0,0\n" % (sw, CF))
            f.write("c0-3.61-both\t%s;%sOutnumberedBreaksArmour=true;%sWhiffExtraSeconds=0.25,0,0,0,0,0\n" % (sw, CF, CF))
    return 0


ROWS = ["duel.order", "duel.unbeaten", "ttk.median", "blowout", "leader.half.win", "lead.changes", "dmg.top10",
        "dmgshare.Warrior", "time.deadwalkback.all", "stuck", "escape.all", "escape.dodge.all", "tick.mean.us", "tick.p99.us"]


def table(dirs):
    hdr = "| arm | " + " | ".join(ROWS) + " |"
    print(hdr)
    print("|" + "---|" * (len(ROWS) + 1))
    for d in dirs:
        p = os.path.join(d, "balance-report.json")
        if not os.path.exists(p):
            continue
        j = json.load(open(p))
        rows = {r["key"]: r for r in j.get("rows", j.get("Rows", []))}
        cells = []
        for k in ROWS:
            r = rows.get(k)
            cells.append("" if r is None else ("%.3g" % r.get("value", r.get("Value", float("nan")))))
        print("| " + os.path.basename(d.rstrip("/")) + " | " + " | ".join(cells) + " |")


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "grid":
        grid(sys.argv[2])
    elif cmd == "pick":
        sys.exit(pick(sys.argv[2], sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 4))
    elif cmd == "table":
        table(sys.argv[2:])
