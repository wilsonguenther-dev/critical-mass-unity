#!/usr/bin/env python3
"""intro-film-2 edge gate: no layer may show a straight cut edge.

  python3 tools/film/check_edges.py [LAYER_DIR] [--min 80] [--json OUT.json]

For every RGBA layer (PNG) it finds pixel-exact straight alpha boundaries: runs longer than --min px (default 80)
where a hard opaque/clear step lies on one pixel row or column (what a rectangular crop or a layer bound
leaves; painted contours wobble and never do this), and runs of opaque pixels along the image's own border (content
cut by the layer bounds). Exit 1 on any hit.
"""
import glob, json, os, sys
import numpy as np
import cv2


def longest_true_run(b):
    """b: 2-D bool; longest run of True along axis 1 for each row -> (max_len, row, start)."""
    best = (0, -1, -1)
    for i, row in enumerate(b):
        if not row.any():
            continue
        d = np.diff(np.concatenate([[0], row.astype(np.int8), [0]]))
        st, en = np.nonzero(d == 1)[0], np.nonzero(d == -1)[0]
        k = int(np.argmax(en - st))
        if en[k] - st[k] > best[0]:
            best = (int(en[k] - st[k]), i, int(st[k]))
    return best


def check(path, mn):
    im = cv2.imread(path, cv2.IMREAD_UNCHANGED)
    if im is None or im.ndim < 3 or im.shape[2] < 4:
        return None
    al = im[..., 3]
    a = al > 127
    hi, lo = al > 220, al < 35
    hits = []
    # a cut is a HARD step on one pixel row/column (opaque next to clear, no anti-aliased ramp); keyed painted
    # contours always carry a soft ramp, so they never match
    eh = (hi[:-1] & lo[1:]) | (lo[:-1] & hi[1:])
    ev = ((hi[:, :-1] & lo[:, 1:]) | (lo[:, :-1] & hi[:, 1:])).T
    for name, b in (('horizontal', eh), ('vertical', ev)):
        n, i, s = longest_true_run(b)
        if n > mn:
            hits.append(dict(kind=name + ' straight cut', length=n, at=i, start=s))
    for name, line in (('top', a[0]), ('bottom', a[-1]), ('left', a[:, 0]), ('right', a[:, -1])):
        n, _, s = longest_true_run(line[None, :])
        if n > mn:
            hits.append(dict(kind='opaque along layer ' + name + ' bound', length=n, start=s))
    return hits


def main():
    a = sys.argv[1:]
    d = a[0] if a and not a[0].startswith('--') else os.path.join(os.path.dirname(__file__), '..', '..',
                                                                     'Assets/_Game/Art/Story/_film2~')
    mn = int(a[a.index('--min') + 1]) if '--min' in a else 80
    rep, bad = {}, 0
    for p in sorted(glob.glob(os.path.join(d, '*.png'))):
        h = check(p, mn)
        if h is None:
            continue
        rep[os.path.basename(p)] = h
        bad += bool(h)
        print(('FAIL ' if h else 'ok   ') + os.path.basename(p) + ('  ' + json.dumps(h) if h else ''))
    print(f'{len(rep)} layers, {bad} with straight edges')
    if '--json' in a:
        json.dump(rep, open(a[a.index('--json') + 1], 'w'), indent=1)
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
