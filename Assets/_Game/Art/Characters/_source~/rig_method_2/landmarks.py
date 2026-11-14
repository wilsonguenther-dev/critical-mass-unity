# rig-method-2 (2026-11-14): find skeleton landmarks on a generated humanoid mesh by geometry alone (pure numpy,
# no Blender), so the same code runs inside Blender and on any machine.
# Input: V (N,3) metres, Z up, facing -Y, feet near z=0, roughly centred on x=0; F (M,3) triangles (welded or not).
# Output: dict of joint positions plus per-hand finger chains and a "notes" list of what was measured or assumed.
# Method: horizontal plane sections (loops) for legs, crotch, armpits and neck; sections across each arm for the
# wrist; the hand beyond the wrist split into connected pieces past a cut plane for separate fingers (5) or
# thumb + mitt (the mitt is then split into an index strip and a three-finger mitten).
import numpy as np


def weld(V, F, q=1e-5):
    key = np.round(V / q).astype(np.int64)
    u, idx, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    return V[idx], inv.ravel()[F], inv.ravel()


def plane_loops(V, F, p, n, vmask=None, min_segs=4):
    """Loops where the plane (point p, normal n) cuts the mesh. Returns list of dicts (points = segment midpoints)."""
    s = (V - p) @ n
    t = F if vmask is None else F[vmask[F].all(1)]
    st = s[t]
    neg = st < 0
    cross = neg.any(1) & ~neg.all(1)
    t = t[cross]; st = st[cross]
    if len(t) == 0:
        return []
    keys, segs, pts = {}, [], {}
    for tr, ss in zip(t, st):
        ek = []
        for a, b, sa, sb in ((tr[0], tr[1], ss[0], ss[1]), (tr[1], tr[2], ss[1], ss[2]), (tr[2], tr[0], ss[2], ss[0])):
            if (sa < 0) != (sb < 0):
                k = (a, b) if a < b else (b, a)
                if k not in keys:
                    keys[k] = len(keys)
                    w = sa / (sa - sb)
                    pts[keys[k]] = V[a] + (V[b] - V[a]) * w
                ek.append(keys[k])
        if len(ek) == 2:
            segs.append(ek)
    par = list(range(len(keys)))

    def f(x):
        while par[x] != x:
            par[x] = par[par[x]]; x = par[x]
        return x
    for a, b in segs:
        ra, rb = f(a), f(b)
        if ra != rb:
            par[ra] = rb
    groups = {}
    for a, b in segs:
        groups.setdefault(f(a), []).append((a, b))
    loops = []
    for g in groups.values():
        if len(g) < min_segs:
            continue
        P = np.array([pts[k] for k in {x for e in g for x in e}])
        per = sum(np.linalg.norm(pts[a] - pts[b]) for a, b in g)
        loops.append({'c': P.mean(0), 'min': P.min(0), 'max': P.max(0), 'per': per, 'n': len(g), 'P': P})
    return loops


def components(nv, F):
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    e = np.vstack([F[:, [0, 1]], F[:, [1, 2]]])
    g = coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(nv, nv))
    return connected_components(g, directed=False)[1]


def components_np(nv, F):
    """Connected components without scipy (Blender's Python has numpy only)."""
    lab = np.arange(nv)
    e = np.vstack([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]])
    while True:
        m = np.minimum(lab[e[:, 0]], lab[e[:, 1]])
        new = lab.copy()
        np.minimum.at(new, e[:, 0], m)
        np.minimum.at(new, e[:, 1], m)
        new = new[new]
        if np.array_equal(new, lab):
            return lab
        lab = new


def comps(nv, F):
    try:
        return components(nv, F)
    except ImportError:
        return components_np(nv, F)


def find(V0, F0):
    notes = []
    V, F, _ = weld(V0, F0)
    z0 = V[:, 2].min()
    H = V[:, 2].max() - z0
    Z = np.array([0, 0, 1.0])
    L = {}
    lv = lambda frac: plane_loops(V, F, np.array([0, 0, z0 + frac * H]), Z)

    # 1. crotch: lowest level (above 10% H) with one wide loop spanning x=0 (the pelvis), legs below it.
    crotch = None
    for fr in np.arange(0.10, 0.60, 0.005):
        if any(l['min'][0] < -0.06 * H and l['max'][0] > 0.06 * H and l['max'][0] - l['min'][0] > 0.22 * H
               for l in lv(fr)):
            crotch = fr; break
    if crotch is None:
        crotch = 0.30; notes.append('crotch not found, assumed 0.30 H')
    zc = z0 + crotch * H
    notes.append(f'crotch at {crotch:.3f} H')

    # 2. legs: centre of each side's points at 75% of crotch height (works when a scarf or coat joins the legs)
    for side, sg in (('l', 1), ('r', -1)):
        def side_centre(zz, w=0.012):
            m = (np.abs(V[:, 2] - zz) < w * H) & (V[:, 0] * sg > 0.03 * H) & (V[:, 0] * sg < 0.30 * H)
            return V[m].mean(0) if m.sum() > 8 else np.array([sg * 0.09 * H, 0, zz])
        th = side_centre(z0 + 0.75 * crotch * H)
        lx, ly = th[0], th[1]
        hipz = zc + 0.035 * H
        afr = min(max(0.30 * crotch, 0.05), 0.10)
        az = z0 + afr * H
        an = side_centre(az, 0.008)
        ax, ay = an[0], an[1]
        notes.append(f'ankle {side} at {afr:.3f} H (0.30 x crotch, clamped 0.05-0.10)')
        L['upperleg.' + side] = np.array([lx, ly, hipz])
        L['foot.' + side] = np.array([ax, ay, az])
        kz = (hipz + az) / 2
        kc = side_centre(kz)
        L['lowerleg.' + side] = np.array([(lx + ax) / 2, kc[1] - 0.01 * H, kz])
        foot = V[(V[:, 2] < z0 + 0.04 * H) & (np.sign(V[:, 0]) == sg) & (np.abs(V[:, 0] - ax) < 0.12 * H)]
        tipy = foot[:, 1].min() if len(foot) else ay - 0.1 * H
        L['toes.' + side] = np.array([ax, ay + 0.65 * (tipy - ay), z0 + 0.02 * H])
        L['_toetip.' + side] = np.array([ax, tipy, z0 + 0.02 * H])

    # 3. arms: start at the hand (most lateral vertex between 20% and 52% H) and follow the arm's section upward
    # until it merges with the torso (its inner edge jumps inward or its perimeter doubles): that level is the armpit.
    hands = {}
    for side, sg in (('l', 1), ('r', -1)):
        band = (V[:, 2] > z0 + 0.20 * H) & (V[:, 2] < z0 + 0.52 * H) & (np.sign(V[:, 0]) == sg)
        i = np.argmax(np.where(band, V[:, 0] * sg, -1e9))
        hc = V[i]
        prev = None; track = []
        zz = hc[2] + 0.05 * H   # start above the hand: spread fingers would read as a merge
        while zz < z0 + 0.80 * H:
            ls = [l for l in plane_loops(V, F, np.array([0, 0, zz]), Z) if l['c'][0] * sg > 0]
            if prev is None:
                ls = [l for l in ls if np.linalg.norm((l['c'] - hc)[:2]) < 0.14 * H]
            else:
                ls = [l for l in ls if np.linalg.norm((l['c'] - prev['c'])[:2]) < 0.06 * H]
            if not ls:
                break
            l = min(ls, key=lambda q: np.linalg.norm((q['c'] - (prev['c'] if prev else hc))[:2]))
            inner = l['min'][0] * sg if sg > 0 else -l['max'][0]
            if prev is not None:
                pin = prev['min'][0] * sg if sg > 0 else -prev['max'][0]
                if inner < pin - 0.04 * H or l['per'] > 2.2 * prev['per'] or inner < 0.02 * H:
                    break
            track.append((zz, l)); prev = l
            zz += 0.004 * H
        if len(track) < 5:
            notes.append(f'arm {side}: track failed'); continue
        fr_t = (track[-1][0] - z0) / H
        fr = min(max(fr_t, 0.45), 0.62)
        if fr != fr_t:
            notes.append(f'armpit {side}: tracked {fr_t:.3f} H, clamped to {fr:.3f} H (pads or crevices hide the armpit)')
        l = min(track, key=lambda t: abs(t[0] - (z0 + fr * H)))[1]
        inner = l['min'][0] if sg > 0 else l['max'][0]
        outer = l['max'][0] if sg > 0 else l['min'][0]
        sh = np.array([inner + 0.35 * (outer - inner), l['c'][1], z0 + fr * H + 0.045 * H])
        L['upperarm.' + side] = sh
        notes.append(f'armpit {side} at {fr:.3f} H (arm followed {len(track)} sections up from the hand)')
        ax_ = hc - sh; alen = np.linalg.norm(ax_); ax_ /= alen
        # wrist: thinnest arm section between 55% and 92% of shoulder->hand, cut perpendicular to the arm
        best = None
        near = np.linalg.norm(np.cross(V - sh, ax_), axis=1) < 0.12 * H
        for t in np.arange(0.55, 0.93, 0.01):
            p = sh + ax_ * alen * t
            ls = plane_loops(V, F, p, ax_, vmask=near & (np.linalg.norm(V - p, axis=1) < 0.10 * H))
            if not ls:
                continue
            per = min(ls, key=lambda q: np.linalg.norm(q['c'] - p))['per']
            if best is None or per < best[1]:
                best = (t, per, p)
        wt = best[0] if best else 0.78
        # the hand region: beyond the wrist, near the hand
        wr = sh + ax_ * alen * wt
        hv = ((V - wr) @ ax_ > 0) & (np.linalg.norm(V - hc, axis=1) < 0.16 * H) & near
        hpts = V[hv]
        hcen = hpts.mean(0)
        hdir = hcen - wr; hdir /= np.linalg.norm(hdir)
        tip = hpts[np.argmax((hpts - wr) @ hdir)]
        hl = (tip - wr) @ hdir
        L['wrist.' + side] = wr
        L['lowerarm.' + side] = sh + (wr - sh) * 0.5 + np.array([0, 0.012 * H, 0])
        L['hand.' + side] = wr + hdir * 0.08 * hl
        L['handslot.' + side] = wr + hdir * 0.45 * hl
        notes.append(f'wrist {side} at {wt:.2f} of shoulder-to-hand; hand length {hl:.3f} m')
        hands[side] = finger_chains(V, F, hv, wr, hdir, hl, sg, H, notes, side)
    # 4. torso and head
    hipy = np.mean([L[k][1] for k in ('upperleg.l', 'upperleg.r')])
    shz = np.mean([L[k][2] for k in ('upperarm.l', 'upperarm.r') if k in L]) if any(k in L for k in ('upperarm.l', 'upperarm.r')) else z0 + 0.55 * H
    L['hips'] = np.array([0, hipy, zc + 0.06 * H])
    L['spine'] = np.array([0, hipy, L['hips'][2] + 0.30 * (shz - L['hips'][2])])
    L['chest'] = np.array([0, hipy, L['hips'][2] + 0.62 * (shz - L['hips'][2])])
    # neck: thinnest x-width of the front-half points between the shoulders and shoulders + 12% H
    neck = None
    for fr in np.arange((shz - z0) / H - 0.02, (shz - z0) / H + 0.12, 0.004):
        m = (np.abs(V[:, 2] - (z0 + fr * H)) < 0.004 * H) & (V[:, 1] < hipy + 0.02 * H) & (np.abs(V[:, 0]) < 0.25 * H)
        if m.sum() < 8:
            continue
        w = np.ptp(V[m, 0])
        if neck is None or w < neck[1]:
            neck = (fr, w, V[m].mean(0))
    if neck is not None and neck[1] > 0.25 * H:
        notes.append(f'no clear neck (thinnest front width {neck[1]:.3f} m): head joint = shoulders + 3% H')
        neck = ((shz - z0) / H + 0.03, neck[1], np.array([0, hipy, 0]))
    nz = z0 + (neck[0] if neck else 0.62) * H
    L['head'] = np.array([0, neck[2][1] if neck else hipy, nz])
    L['_headtop'] = np.array([0, L['head'][1], z0 + H])
    notes.append(f'neck at {(nz - z0) / H:.3f} H (width {neck[1] if neck else -1:.3f} m)')
    feet = [L['_toetip.l'][1] - L['foot.l'][1], L['_toetip.r'][1] - L['foot.r'][1]]
    notes.append(f'facing: toe tips are {np.mean(feet):+.3f} m from the ankles along y (negative = faces -Y)')
    return {'joints': {k: v.tolist() for k, v in L.items()}, 'hands': hands, 'height': float(H), 'z0': float(z0),
            'crotch_frac': float(crotch), 'notes': notes}


def finger_chains(V, F, hv, wr, hdir, hl, sg, H, notes, side):
    """Separate pieces of the hand past a cut plane. Thumb = the first piece to split off; fingers = the most pieces."""
    idx = np.nonzero(hv)[0]
    res = []
    for cut in np.arange(0.25, 0.86, 0.05):
        keep = hv & ((V - wr) @ hdir > cut * hl)
        Fk = F[keep[F].all(1)]
        if len(Fk) == 0:
            continue
        lab = comps(len(V), Fk)
        used = np.unique(Fk)
        labs, cnt = np.unique(lab[used], return_counts=True)
        total = cnt.sum()
        big = [(l, c) for l, c in zip(labs, cnt) if c > 0.02 * total and c >= 12]
        res.append((cut, [(used[lab[used] == l], c) for l, c in big]))
    if not res:
        notes.append(f'hand {side}: no pieces'); return {'mode': 'none'}
    counts = [len(r[1]) for r in res]
    notes.append(f'hand {side}: pieces per cut ' + ' '.join(f'{c:.2f}:{n}' for (c, _), n in zip(res, counts)))
    best = max(range(len(res)), key=lambda i: (min(counts[i], 5), -i))
    cut, pieces = res[best]
    chains = []
    for vids, c in pieces:
        P = V[vids]
        d = (P - wr) @ hdir
        base = P[d < np.quantile(d, 0.15)].mean(0)
        tipp = P[np.argmax(np.linalg.norm(P - wr, axis=1))]
        chains.append({'base': base, 'tip': tipp, 'n': int(c), 'vids': vids})
    # thumb: the piece whose base is farthest from the hand axis (it leaves the palm sideways)
    def off_axis(ch):
        r = ch['base'] - wr
        return np.linalg.norm(r - (r @ hdir) * hdir)
    mode = 'five' if len(chains) >= 5 else ('partial' if len(chains) >= 3 else ('mitt' if len(chains) == 2 else 'fist'))
    out = {'mode': mode, 'cut': float(cut), 'pieces': len(chains)}
    if len(chains) == 0:
        chains = [{'base': wr + hdir * 0.4 * hl, 'tip': wr + hdir * hl, 'n': int(hv.sum()), 'vids': np.nonzero(hv)[0]}]
        notes.append(f'hand {side}: no pieces past the cuts; the whole hand is one block')
    if len(chains) == 1:
        # one piece: a fist or a mitt with the thumb fused; place a thumb on the front (-Y) side, mitten ahead
        ch = chains[0]
        out['thumb'] = [wr + hdir * 0.30 * hl + np.array([0, -0.03 * H, 0]), wr + hdir * 0.55 * hl + np.array([0, -0.04 * H, 0])]
        out['mitten'] = [ch['base'], ch['tip']]
        out['index'] = [ch['base'] + np.array([0, -0.012 * H, 0]), ch['tip'] + np.array([0, -0.012 * H, 0])]
        out['finger_vids'] = {'mitten': ch['vids']}
        notes.append(f'hand {side}: one piece (fist or mitt), thumb placed by rule')
    else:
        chains.sort(key=off_axis, reverse=True)
        thumb = chains[0]
        rest = chains[1:]
        out['thumb'] = [thumb['base'], thumb['tip']]
        if mode in ('five', 'partial'):
            # order the four fingers from the thumb side outwards
            tdir = thumb['base'] - wr; tdir -= (tdir @ hdir) * hdir; tdir /= np.linalg.norm(tdir) + 1e-9
            rest.sort(key=lambda ch: -(ch['base'] - wr) @ tdir)
            names = ['index', 'middle', 'ring', 'pinky'] if len(rest) >= 4 else (['index', 'middle', 'ring'] if len(rest) == 3 else ['index', 'mitten'])
            for nm, ch in zip(names, rest[:4]):
                out[nm] = [ch['base'], ch['tip']]
            out['finger_vids'] = {'thumb': thumb['vids'], **{nm: ch['vids'] for nm, ch in zip(names, rest[:4])}}
        else:
            m = max(rest, key=lambda ch: ch['n'])
            out['mitten'] = [m['base'], m['tip']]
            tdir = thumb['base'] - m['base']; tdir -= (tdir @ hdir) * hdir; tdir /= np.linalg.norm(tdir) + 1e-9
            out['index'] = [m['base'] + tdir * 0.012 * H, m['tip'] + tdir * 0.012 * H]
            out['finger_vids'] = {'thumb': thumb['vids'], 'mitten': m['vids']}
            out['index_side_dir'] = tdir
    return out
