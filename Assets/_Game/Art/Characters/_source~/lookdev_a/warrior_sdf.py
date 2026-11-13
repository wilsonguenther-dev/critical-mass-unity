"""The Warrior (bible p06) as signed-distance parts. Rest pose of the KayKit rig: Z up, faces -Y, left = +X, metres.
Dimensions and placements come from build_warrior.py v7 (fit-tool tuned); the FORMS are new: clean volumes with
stated bevel radii, smooth unions where cloth or hair should flow, hard unions where the bible draws a line.

models() -> {"body": (lo, hi, parts), "shield": ..., "cleaver": ...}; parts = [(group, Shape), ...]
The shield and the cleaver are modelled in their canonical frames (see build_warrior.py) and placed by the rig step.
"""
import math
import random

import numpy as np

from sdfkit import M, Shape, chipped, cone, disc, ell, frame, lock, loop_tube, plane, prism, rbox, torus, union

GROUPS = ["skin", "hair", "teal", "navy", "cream", "trouser", "leather", "leather_dark", "boot", "brass", "stone",
          "stone_dark", "wrap", "wood", "iron", "glow", "stone_blade", "wood_seam"]
GID = {n: i for i, n in enumerate(GROUPS)}
H = 0.0065

HEAD_PIV, HEAD_K, TOPKNOT_LIFT = np.array((0.0, -0.02, 1.27)), 0.90, 0.028
HEAD_UP = 0.045          # bible_fit: eye line 5 % of height too low, so the head sits higher and the topknot is shorter
HEAD_C, HEAD_R = (0.0, -0.01, 1.49), (0.250, 0.232, 0.234)


def hs(p, lift=0.0):
    p = HEAD_PIV + (np.array(p, float) - HEAD_PIV) * HEAD_K
    p[2] += lift + HEAD_UP
    return tuple(p)


def stone_block(size, rng, chips=3, r=0.03, depth=0.035):
    half = tuple(s / 2 for s in size)
    return chipped(rbox(half, min(r, min(half) * 0.6)), half, rng, chips=chips, depth=depth)


def body():
    rng = random.Random(606)
    P = []

    def add(g, s):
        P.append((g, s))

    # ---------------------------------------------------------------- head: one smooth volume, full cheeks, small chin
    c = hs(HEAD_C)
    head = ell(c, (0.224, 0.207, 0.212)).su(ell((c[0], c[1] - 0.014, c[2] - 0.086), (0.186, 0.172, 0.132)), 0.07)
    head = head.su(ell((0, c[1] - 0.200, 1.385 + HEAD_UP + 0.02), (0.020, 0.020, 0.016)), 0.035)            # a hint of a nose
    for s in (1, -1):
        e = hs((0.247 * s, 0.035, 1.405))
        head = head.su(ell(e, (0.040, 0.050, 0.060), (0, 0, 12 * s)), 0.012)
    add("skin", head)
    add("skin", cone((0, 0, 1.13), (0, 0, 1.35), 0.10, 0.095, cap=False))

    # ---------------------------------------------------------------- hair: chunky pointed locks, smooth-unioned
    FP = (0.95, 1.10, 0.86, 0.50, 0.24)
    F_ = (0, -1, 0.35)
    K = HEAD_K
    locks = [
        ((0.06, -0.12, 1.775), (-0.06, -0.37, 1.720), (-0.17, -0.29, 1.545), 0.26, 0.10, F_),
        ((0.08, -0.10, 1.770), (0.17, -0.35, 1.700), (0.22, -0.27, 1.555), 0.22, 0.095, F_),
        ((-0.12, -0.06, 1.765), (-0.25, -0.24, 1.690), (-0.31, -0.17, 1.550), 0.20, 0.095, F_),
        ((0.17, -0.05, 1.755), (0.28, -0.21, 1.680), (0.32, -0.14, 1.555), 0.18, 0.09, F_),
        ((0.20, -0.06, 1.69), (0.31, -0.12, 1.62), (0.35, -0.05, 1.50), 0.16, 0.10, (1, 0, 0.2)),
        ((-0.20, -0.06, 1.69), (-0.31, -0.12, 1.62), (-0.35, -0.05, 1.50), 0.16, 0.10, (-1, 0, 0.2)),
        ((0.21, 0.12, 1.70), (0.32, 0.22, 1.63), (0.33, 0.25, 1.47), 0.20, 0.13, (1, 0.6, 0)),
        ((-0.21, 0.12, 1.70), (-0.32, 0.22, 1.63), (-0.33, 0.25, 1.47), 0.20, 0.13, (-1, 0.6, 0)),
        ((0.08, 0.22, 1.62), (0.13, 0.31, 1.51), (0.14, 0.30, 1.38), 0.20, 0.12, (0.3, 1, 0)),
        ((-0.08, 0.22, 1.62), (-0.13, 0.31, 1.51), (-0.14, 0.30, 1.38), 0.20, 0.12, (-0.3, 1, 0)),
        ((0.09, 0.17, 1.76), (0.15, 0.46, 1.76), (0.17, 0.57, 1.64), 0.23, 0.16, (0.2, 1, 0.3)),
        ((-0.09, 0.17, 1.76), (-0.15, 0.46, 1.76), (-0.17, 0.57, 1.64), 0.23, 0.16, (-0.2, 1, 0.3)),
        ((0.0, 0.07, 1.80), (0.13, -0.06, 1.81), (0.23, -0.12, 1.73), 0.20, 0.08, (0, 0, 1)),
        ((0.0, 0.07, 1.80), (-0.13, -0.06, 1.81), (-0.23, -0.12, 1.73), 0.20, 0.08, (0, 0, 1)),
        ((0.0, 0.07, 1.80), (0.0, -0.06, 1.81), (0.02, -0.17, 1.76), 0.20, 0.08, (0, 0, 1)),
    ]
    hair = [ell(hs((0, 0.055, 1.645)), (0.262 * K, 0.240 * K, 0.165 * K), (-14, 0, 0)),      # cap tilted back: the fringe locks make the hairline
            ell(hs((0, 0.12, 1.49)), (0.255 * K, 0.245 * K, 0.230 * K))]
    for p0, p1, p2, w, t, up in locks:
        hair.append(lock(hs(p0), hs(p1), hs(p2), w * K, t * K, up=up, prof=FP, tip=0.05))
    add("hair", union(hair, 0.006))
    L = TOPKNOT_LIFT
    LP = (0.55, 0.98, 1.10, 0.88, 0.56, 0.26)
    b = (0.0, 0.10, 1.815)
    tuft = [((-0.14, 0.08, 1.900), (-0.33, 0.10, 1.905), 0.19, 0.10, (0, -1, 0)),
            ((-0.09, 0.07, 1.945), (-0.22, 0.07, 1.985), 0.20, 0.10, (0, -1, 0)),
            ((0.01, 0.07, 1.960), (0.00, 0.06, 1.992), 0.20, 0.10, (0, -1, 0)),
            ((0.10, 0.08, 1.948), (0.24, 0.08, 1.982), 0.20, 0.10, (0, -1, 0)),
            ((0.16, 0.10, 1.895), (0.36, 0.13, 1.890), 0.18, 0.10, (0, -1, 0.2)),
            ((0.01, 0.34, 1.920), (0.03, 0.58, 1.830), 0.23, 0.12, (0, 0, 1))]
    top = [ell(hs((0, 0.10, 1.765), L), (0.09 * K, 0.09 * K, 0.06 * K))]
    for p1, p2, w, t, up in tuft:
        top.append(lock(hs(b, L), hs(p1, L), hs(p2, L), w * K, t * K, up=up, prof=LP, tip=0.05))
    add("hair", union(top, 0.005))
    add("brass", cone(hs((0, 0.10, 1.750), L), hs((0, 0.10, 1.825), L), 0.088, 0.082, rnd=0.014))

    # ---------------------------------------------------------------- torso
    hips = ell((0, 0.0, 0.66), (0.31, 0.22, 0.16))
    for s in (1, -1):                                # baggy trousers: a full puff gathered under the knee
        cx = 0.1915 * s
        puff = ell((cx * 1.08, 0.0, 0.455), (0.170, 0.185, 0.205)).su(ell((cx * 1.02, 0.005, 0.30), (0.122, 0.130, 0.07)), 0.05)
        hips = hips.su(puff, 0.028)
    add("trouser", hips)
    add("navy", ell((0, 0.03, 0.99), (0.40, 0.235, 0.31)))
    add("cream", ell((0, -0.145, 0.955), (0.150, 0.105, 0.205)))
    belt = cone((0, 0, 0.752), (0, 0, 0.850), 0.332, 0.342, rnd=0.016).scaled(1, 0.76, 1)
    add("leather_dark", belt)
    add("brass", disc(0.13, 0.065).at(frame((0, -0.248, 0.80), (0, -1, 0), (0, 0, 1))))
    add("brass", disc(0.075, 0.04).at(frame((-0.27, -0.17, 0.79), (-0.62, -0.78, 0), (0, 0, 1))))
    add("leather", rbox((0.07, 0.055, 0.075), 0.022).at(M((0.25, -0.185, 0.73), (0, 0, -25))))
    add("brass", rbox((0.074, 0.058, 0.026), 0.012).at(M((0.25, -0.19, 0.795), (0, 0, -25))))
    hem = [(-0.12, 0.0), (0.12, 0.0), (0.125, -0.31), (0.06, -0.37), (0.0, -0.32), (-0.06, -0.37), (-0.125, -0.31)]
    for y, ry in ((-0.252, 0), (0.247, 180)):
        add("teal", prism(hem, 0.020, rnd=0.010).at(M((0, y, 0.82), (90, 0, ry)) @ M(rot=(-8, 0, 0))))
    # scarf: one fat roll with folds, a second tighter roll, a V drape on the chest, a knot and one tail, a back fall
    sc = loop_tube(0.215, 0.185, lambda a: 1.212 - 0.050 * np.maximum(0.0, -np.sin(a)),
                   lambda a: 0.098 * (1.0 + 0.14 * np.sin(5 * a + 0.7)))
    sc = sc.su(loop_tube(0.185, 0.165, lambda a: 1.275 - 0.045 * np.maximum(0.0, -np.sin(a)),
                         lambda a: 0.074 * (1.0 + 0.16 * np.sin(4 * a + 2.1)), y0=0.01), 0.005)
    sc = sc.su(lock((0.01, -0.18, 1.17), (0.03, -0.250, 1.11), (0.0, -0.236, 1.035), 0.32, 0.07, up=(0, -1, 0),
                    prof=(1.0, 1.06, 0.92, 0.62), tip=0.2), 0.006)
    sc = sc.su(ell((-0.10, -0.228, 1.17), (0.085, 0.065, 0.075)), 0.006)
    sc = sc.su(lock((-0.10, -0.225, 1.15), (-0.16, -0.262, 1.06), (-0.19, -0.245, 0.96), 0.15, 0.055, up=(0, -1, 0),
                    prof=(1.0, 1.08, 0.95, 0.6), tip=0.25), 0.015)
    sc = sc.su(lock((0.0, 0.215, 1.20), (0.01, 0.280, 1.08), (0.0, 0.258, 0.93), 0.30, 0.055, up=(0, 1, 0),
                    prof=(1.0, 0.95, 0.78, 0.45), tip=0.2), 0.03)
    add("teal", sc)

    # ---------------------------------------------------------------- arms (T pose along X)
    for s in (1, -1):
        arm = ell((0.25 * s, 0, 1.11), (0.15, 0.15, 0.15))
        arm = arm.su(cone((0.22 * s, 0, 1.107), (0.47 * s, 0.01, 1.107), 0.13, 0.115, cap=False), 0.03)
        add("navy", arm)
        add("leather", cone((0.455 * s, 0.012, 1.107), (0.735 * s, 0, 1.107), 0.122, 0.142, rnd=0.02))
        add("leather_dark", torus(0.132, 0.016).at(M((0.54 * s, 0.01, 1.107), (0, 90, 0))))
        add("brass", torus(0.140, 0.022).at(M((0.70 * s, 0, 1.107), (0, 90, 0))))
        add("leather_dark", cone((0.725 * s, 0, 1.105), (0.785 * s, 0, 1.105), 0.142, 0.136, rnd=0.015))
        add("wrap", rbox((0.118, 0.118, 0.128), 0.07).at(M((0.85 * s, -0.005, 1.09))))        # fingerless dark glove
        for kz in (-0.072, -0.024, 0.024, 0.072):
            add("skin", ell((0.962 * s, -0.035, 1.09 + kz), (0.042, 0.054, 0.031)))
        add("skin", ell((0.82 * s, -0.125, 1.115), (0.05, 0.048, 0.066)))
        add("brass", disc(0.062, 0.04).at(M((0.46 * s, 0.012, 1.205))))
        # stone guard plates on the outside of the forearm and the back of the hand (bible: grey blocks, brass rivets)
        add("stone", stone_block((0.19, 0.17, 0.07), rng, chips=2, r=0.022, depth=0.02).at(M((0.605 * s, 0.0, 1.245))))
        add("brass", ell((0.605 * s, 0.0, 1.283), (0.026, 0.026, 0.016)))
        add("stone", stone_block((0.15, 0.16, 0.06), rng, chips=2, r=0.02, depth=0.018).at(M((0.86 * s, 0.0, 1.225), (0, 6 * s, 0))))

    # ---------------------------------------------------------------- pauldrons: layered hewn slabs + brass plate
    for s in (1, -1):
        top = M((0.30 * s, 0.0, 1.28), (0, -24 * s, 0))
        add("stone", stone_block((0.35, 0.39, 0.20), rng, chips=3, r=0.024, depth=0.045).at(top))
        mid = M((0.445 * s, 0.01, 1.12), (0, -60 * s, 0))
        add("stone", stone_block((0.27, 0.35, 0.16), rng, chips=3, r=0.022, depth=0.04).at(mid))
        add("brass", disc(0.115, 0.065).at(frame((0.34 * s, -0.215, 1.13), (0.42 * s, -0.90, 0.10), (0, 0, 1))))
        for rx, ry in ((0.12, -0.12), (0.13, 0.11)):
            add("brass", ell((0, 0, 0), (0.030, 0.030, 0.020)).at(top @ M((rx * s, ry, 0.098))))

    # ---------------------------------------------------------------- legs
    for s in (1, -1):
        cx = 0.1915 * s
        add("wrap", cone((cx, 0.012, 0.285), (cx, 0.02, 0.15), 0.113, 0.106, rnd=0.02))
        knee = ell((0, 0, 0), (0.136, 0.080, 0.128)).at(M((cx, -0.122, 0.34), (-12, 0, 6 * s)))
        add("stone_dark", knee)
        add("brass", ell((0, 0, 0), (0.036, 0.022, 0.036)).at(M((cx, -0.186, 0.347), (-12, 0, 0))))
        boot = cone((cx, 0.02, 0.05), (cx, 0.02, 0.180), 0.140, 0.134, rnd=0.02)
        boot = boot.su(rbox((0.190, 0.250, 0.084), 0.07).at(M((cx * 1.03, -0.10, 0.090))), 0.02)
        add("boot", boot)
        add("leather_dark", torus(0.136, 0.030).at(M((cx, 0.02, 0.176))))
        add("leather_dark", torus(0.146, 0.018, 1.6).at(M((cx, 0.02, 0.112))))
        add("brass", rbox((0.024, 0.016, 0.026), 0.008).at(M((cx + 0.150 * s, 0.0, 0.112))))
        add("stone_dark", ell((cx * 1.03, -0.272, 0.082), (0.196, 0.112, 0.074)).su(rbox((0.150, 0.06, 0.05), 0.03).at(M((cx, -0.24, 0.07))), 0.03))
        add("wrap", rbox((0.186, 0.262, 0.022), 0.012).at(M((cx * 1.03, -0.105, 0.021))))
    return (-1.03, -0.47, -0.012), (1.03, 0.63, 2.075), P


SHIELD_RIM_N = 9


def shield():
    """Canonical: face +Z, up +Y, outer radius 0.63."""
    rng = random.Random(77)
    P = []
    P.append(("wood_seam", cone((0, 0, -0.055), (0, 0, -0.02), 0.50, 0.50, rnd=0.01)))

    def wood_f(Pt):
        r2 = (Pt[:, 0] ** 2 + Pt[:, 1] ** 2)
        topz = 0.036 + 0.032 * np.maximum(0.0, 1.0 - r2 / 0.25)
        dr = np.sqrt(r2) - 0.492
        dz = np.maximum(Pt[:, 2] - topz, -0.03 - Pt[:, 2])
        d = np.maximum(dr, dz)
        # plank seams: crisp slots 0.02 wide and 0.02 deep at x = +-0.1, +-0.3
        gx = np.abs((Pt[:, 0] % 0.2) - 0.1)
        slot = np.maximum(gx - 0.010, topz - 0.020 - Pt[:, 2])
        return np.maximum(d, -slot)
    P.append(("wood", Shape(wood_f)))
    P.append(("wrap", torus(0.535, 0.070, 0.8).at(M((0, 0, -0.012)))))
    for k in range(SHIELD_RIM_N):
        a = 2 * math.pi * (k + 0.5) / SHIELD_RIM_N
        r = 0.535
        sx = 2 * math.pi * r / SHIELD_RIM_N * rng.uniform(0.80, 0.86)
        sz = rng.uniform(0.19, 0.22)
        m = M((r * math.cos(a), r * math.sin(a), 0.0), (rng.uniform(-6, 6), 0, math.degrees(a) + 90))
        P.append(("stone", stone_block((sx, 0.205, sz), rng, chips=3, r=0.024, depth=0.045).at(m)))
    P.append(("iron", cone((0, 0, 0.03), (0, 0, 0.085), 0.265, 0.250, rnd=0.018)))
    P.append(("glow", torus(0.180, 0.034, 0.7).at(M((0, 0, 0.086)))))
    P.append(("iron", ell((0, 0, 0.085), (0.125, 0.125, 0.075))))
    P.append(("glow", ell((0, 0, 0.158), (0.042, 0.042, 0.022))))
    for y in (-0.12, 0.12):
        P.append(("leather_dark", rbox((0.17, 0.03, 0.015), 0.008).at(M((0, y, -0.062)))))
    return (-0.68, -0.68, -0.14), (0.68, 0.68, 0.22), P


def cleaver():
    """Canonical: grip at the origin, pommel +Y, blade toward -Y, cutting edge +X, faces +-Z."""
    rng = random.Random(9)
    P = []
    BL, BW = 0.80, 1.0
    blade = [(-0.07, -0.09), (0.12, -0.10), (0.21, -0.16), (0.22, -0.80), (0.16, -0.88), (0.07, -0.86), (0.04, -0.90),
             (-0.06, -0.88), (-0.10, -0.80), (-0.10, -0.70), (-0.05, -0.68), (-0.05, -0.60), (-0.10, -0.58),
             (-0.10, -0.44), (-0.05, -0.42), (-0.05, -0.34), (-0.10, -0.32), (-0.10, -0.18)]
    blade = [(x * BW, y * BL) for x, y in blade]
    b = prism(blade, 0.068, rnd=0.016)
    for n, off in (((-0.6, -0.3, 0.74), 0.118), ((-0.5, 0.4, -0.76), 0.118)):
        b = b.isect(plane(n, off), 0.003)
    P.append(("stone_blade", b))
    edge = [(0.11, -0.13), (0.30, -0.17), (0.33, -0.30), (0.30, -0.38), (0.34, -0.50), (0.31, -0.60), (0.34, -0.74),
            (0.30, -0.84), (0.20, -0.95), (0.12, -0.90), (0.13, -0.76), (0.10, -0.60), (0.12, -0.40), (0.10, -0.20)]
    edge = [(x * BW, y * BL) for x, y in edge]
    # the glowing edge: a clean crystal slab, a little thinner than the stone, with a bevelled lip
    P.append(("glow", prism(edge, 0.078, rnd=0.022)))
    P.append(("leather", cone((0, 0.13, 0), (0, -0.10, 0), 0.040, 0.040, rnd=0.01)))
    for y in (0.08, 0.02, -0.04):
        P.append(("leather_dark", torus(0.042, 0.013).at(M((0, y, 0), (90, 0, 0)))))
    P.append(("brass", ell((0, 0.158, 0), (0.056, 0.056, 0.056))))
    P.append(("brass", rbox((0.095, 0.028, 0.058), 0.016).at(M((0.02, -0.095, 0)))))
    return (-0.17, -0.82, -0.13), (0.40, 0.24, 0.13), P


def models():
    return {"body": body(), "shield": shield(), "cleaver": cleaver()}
