#!/usr/bin/env python3
"""b12-w: the cross-play verdict for two online peers' logs (a Windows player and a Mac player, or console peers).

Reads, from each side's log (Unity Player.log or the console peer's output):
  [CM.Online] build=.. config=.. protocol=.. platform=.. runtime=.. netbuild=.. phase=join|start   (b12-w builds on)
  ONLINE SETUP ... config 0x<own> world0 0x.. host config 0x<host's> host world0 0x..             (every build)
  ONLINE SUMMARY ... / ONLINE EXIT n / ONLINE ERROR ..
and, optionally, each side's stamp line from player-stamp.py (--stamp-a / --stamp-b FILE). Prints one line per side and
one verdict line per check, then exits 1 if any check fails:
  - both sides reached match start (an ONLINE SETUP line) and exited 0;
  - both sides built the world from the same ConfigHash (own == other's own == host's), and the same tick-0 world;
  - both sides carry the same build (netbuild from the [CM.Online] line, else the stamp's netbuild) and protocol.
The per-tick fingerprints are compared separately by compare-hashes.py.
Usage: crossplay-check.py A.log B.log [--stamp-a FILE] [--stamp-b FILE] [--names host,join]
"""
import re
import sys

VERSION = re.compile(r"\[CM\.Online\] (build=\S+ config=\S+ protocol=\S+ platform=\S+(?: \S+=\S+)*)")
SETUP = re.compile(r"ONLINE SETUP .*?config 0x([0-9A-F]{16}) world0 0x([0-9A-F]{16}) host config 0x([0-9A-F]{16}) host world0 0x([0-9A-F]{16})")
EXIT = re.compile(r"ONLINE EXIT (-?\d+)")
ERROR = re.compile(r"ONLINE (?:ERROR|SETUP MISMATCH|DESYNC)[^\r\n]*")
SUMMARY = re.compile(r"ONLINE SUMMARY[^\r\n]*")


def fields(line):
    return dict(kv.split("=", 1) for kv in line.split() if "=" in kv)


def side(log_path, stamp_path):
    with open(log_path, "r", encoding="utf-8", errors="replace") as f:
        text = f.read()
    s = {"log": log_path}
    versions = [fields(m.group(1)) for m in VERSION.finditer(text)]
    s["version_lines"] = versions
    m = SETUP.search(text)
    if m:
        s["config"], s["world0"], s["host_config"], s["host_world0"] = m.groups()
    e = EXIT.findall(text)
    s["exit"] = int(e[-1]) if e else None
    s["errors"] = ERROR.findall(text)
    sm = SUMMARY.findall(text)
    s["summary"] = sm[-1] if sm else None
    if stamp_path:
        try:
            with open(stamp_path) as f:
                s["stamp"] = fields(f.read().strip())
        except OSError:
            s["stamp"] = {}
    else:
        s["stamp"] = {}
    v = versions[-1] if versions else {}
    s["build"] = v.get("build") or s["stamp"].get("version") or "unknown"
    s["netbuild"] = v.get("netbuild") or s["stamp"].get("netbuild") or "unknown"
    s["protocol"] = v.get("protocol", "unknown")
    s["platform"] = v.get("platform", "unknown")
    s["runtime"] = v.get("runtime", "unknown")
    s["version_config"] = v.get("config")
    return s


def main(argv):
    pos = [a for a in argv if not a.startswith("--")]
    opt = {}
    for k in ("--stamp-a", "--stamp-b", "--names"):
        if k in argv:
            opt[k] = argv[argv.index(k) + 1]
            pos.remove(opt[k])
    if len(pos) != 2:
        print(__doc__)
        return 64
    names = opt.get("--names", "a,b").split(",")
    a, b = side(pos[0], opt.get("--stamp-a")), side(pos[1], opt.get("--stamp-b"))
    fails = []
    for n, s in zip(names, (a, b)):
        print(f"SIDE {n}: build={s['build']} netbuild={s['netbuild']} config={s.get('config', 'none')} "
              f"host_config={s.get('host_config', 'none')} world0={s.get('world0', 'none')} protocol={s['protocol']} "
              f"platform={s['platform']} runtime={s['runtime']} exit={s['exit']} version_lines={len(s['version_lines'])}")
        if s["summary"]:
            print(f"SIDE {n}: {s['summary']}")
        for e in s["errors"]:
            print(f"SIDE {n}: {e}")
        if "config" not in s:
            fails.append(f"{n} never reached match start (no ONLINE SETUP line)")
        if s["exit"] != 0:
            fails.append(f"{n} exit {s['exit']}")
        if s["version_config"] and s.get("config") and s["version_config"] != s["config"]:
            fails.append(f"{n} [CM.Online] config {s['version_config']} differs from its ONLINE SETUP config {s['config']}")

    def check(label, x, y):
        ok = x == y and x not in (None, "unknown")
        print(f"CHECK {label}: {names[0]}={x} {names[1]}={y} -> {'SAME' if ok else ('UNKNOWN' if x in (None, 'unknown') or y in (None, 'unknown') else 'DIFFER')}")
        return ok

    if not check("config", a.get("config"), b.get("config")):
        fails.append("ConfigHash differs or is missing")
    if a.get("config") and b.get("host_config") and a.get("host_config") != b.get("host_config"):
        fails.append("the two sides saw different host configs")
    if not check("world0", a.get("world0"), b.get("world0")):
        fails.append("tick-0 world differs or is missing")
    if not check("netbuild", a["netbuild"], b["netbuild"]):
        fails.append("build differs or is unknown")
    if a["protocol"] != "unknown" or b["protocol"] != "unknown":
        if not check("protocol", a["protocol"], b["protocol"]):
            fails.append("protocol differs")
    check("version", a["build"], b["build"])   # printed; netbuild (the commit) is what the room compares
    if fails:
        print("CROSSPLAY CHECK FAILED: " + "; ".join(fails))
        return 1
    print("CROSSPLAY CHECK OK: same config, same tick-0 world, same build, both exited 0")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
