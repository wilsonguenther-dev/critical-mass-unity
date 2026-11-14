#!/usr/bin/env python3
"""online-rooms-2: compare two per-tick fingerprint logs ("tick hash" lines) from two online peers.
Exit 0 when every tick both logged agrees and they share at least --min ticks; 1 on the first difference (named); 2 on too
little overlap. Usage: compare-hashes.py a.txt b.txt [--min 100]"""
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
    a, b = load(args[0]), load(args[1])
    common = sorted(set(a) & set(b))
    for t in common:
        if a[t] != b[t]:
            print(f"DESYNC first differing fingerprint at tick {t}: {a[t]} vs {b[t]} ({len(common)} common ticks)")
            return 1
    if len(common) < need:
        print(f"TOO LITTLE OVERLAP: {len(common)} common fingerprints, need {need} (a has {len(a)}, b has {len(b)})")
        return 2
    print(f"MATCH {len(common)} fingerprints agree, ticks {common[0]}..{common[-1]}")
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
