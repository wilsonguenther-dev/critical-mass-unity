#!/usr/bin/env python3
"""online-rooms-2: compare two per-tick fingerprint logs ("tick hash" lines) from two online peers.
Exit 0 when every tick both logged agrees and they share at least --min ticks; 1 on the first difference (named); 2 on too
little overlap. Usage: compare-hashes.py a.txt b.txt [--min 100] [--from TICK]
b15-rejoin: --from TICK counts (and prints) only the common ticks at or after TICK toward --min, so a rejoined peer's log
can be required to agree well past the rejoin; every common tick is still compared."""
import sys

def load(p):
    d = {}
    with open(p) as f:
        for line in f:
            parts = line.split()
            if len(parts) == 2:
                d[int(parts[0])] = parts[1]
    return d

def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    need = 100
    if "--min" in argv:
        need = int(argv[argv.index("--min") + 1])
        args = [a for a in args if a != str(need)]
    start = None
    if "--from" in argv:
        start = int(argv[argv.index("--from") + 1])
        args = [x for x in args if x != str(start)]
    a, b = load(args[0]), load(args[1])
    common = sorted(set(a) & set(b))
    for t in common:
        if a[t] != b[t]:
            print(f"DESYNC first differing fingerprint at tick {t}: {a[t]} vs {b[t]} ({len(common)} common ticks)")
            return 1
    late = [t for t in common if start is None or t >= start]
    if len(late) < need:
        print(f"TOO LITTLE OVERLAP: {len(late)} common fingerprints{'' if start is None else f' from tick {start}'}, need {need} (a has {len(a)}, b has {len(b)})")
        return 2
    if start is None:
        print(f"MATCH {len(common)} fingerprints agree, ticks {common[0]}..{common[-1]}")
    else:
        print(f"MATCH {len(common)} fingerprints agree, ticks {common[0]}..{common[-1]}; {len(late)} of them from tick {start}, ticks {late[0]}..{late[-1]}")
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
