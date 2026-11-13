"""Painted texture as a function of 3D position: paint(obj, P, gid) -> albedo (sRGB), team masks, emission.

Everything a texture painter would do by hand is derived from the distance fields:
  n        surface normal (gradient of the group field)
  hl       baked light: one fixed painted light (mostly from above, a little from the front and the character's right)
  ao       occlusion against ALL groups (soft, 4 radii)
  line     contact line: within ~6 mm of another material (thin dark linework only where forms overlap)
  edge     convex edge mask of the own group (crisp light edge highlights, wear)
  cav      concave crease of the own group (darkened folds, lock gaps, seams)
Each material then composes base / shade / lit colours from the bible palette with its own rules.
The same function feeds the previews (surface points of the lattice) and the atlas bake (texel centres).
"""
import os
import sys

import numpy as np
from scipy import ndimage as ndi

import warrior_sdf as W

F = np.float32
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from cm_art import face as face_mod  # noqa: E402  (the artist-approved face layout, v7)

G = W.GID


def hx(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], F)


def ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    t = np.asarray(t, F)
    if t.ndim == 1:
        t = t[:, None]
    return a * (1 - t) + b * t


_rng = np.random.default_rng(11)
_NV = ndi.gaussian_filter(_rng.random((48, 48, 48)).astype(F), 0.6, mode="wrap")
_NV = (_NV - _NV.min()) / (_NV.max() - _NV.min())


def noise(P, freq, off=0.0):
    q = (P * np.array(freq, F) + F(off)).T % 48
    return ndi.map_coordinates(_NV, q, order=1, mode="wrap").astype(F)


# material: base, shade, lit (bible palette + sampled lit/shade from the crops), style
MAT = {
    "skin": ("#F7B48A", "#DE8C68", "#FFD3B0"), "hair": ("#43302F", "#2B1D21", "#664847"),
    "teal": ("#1F8CAA", "#135A80", "#58C6DA"), "navy": ("#2A374D", "#1A2136", "#465A80"),
    "cream": ("#F2E5D5", "#CDB3A2", "#FFF8EC"), "trouser": ("#DCC4AD", "#A88B7E", "#F4E6D4"),
    "leather": ("#AD6743", "#733F2C", "#D39266"), "leather_dark": ("#633E2D", "#3E2520", "#8A5B40"),
    "boot": ("#8A5535", "#573224", "#B27A50"), "brass": ("#C79245", "#7C4F2B", "#F3D07E"),
    "stone": ("#C4AFA1", "#8C7A6F", "#EBDACA"), "stone_dark": ("#8C7A6F", "#584E52", "#B9A9A0"),
    "wrap": ("#3A3346", "#262231", "#57506A"), "wood": ("#B36C42", "#7A452C", "#D4955F"),
    "iron": ("#58697E", "#34405A", "#8EA2BE"), "glow": ("#4FE3F0", "#1FA9CF", "#E4FDFF"),
    "stone_blade": ("#6F7F98", "#4A5872", "#A9B9CF"), "wood_seam": ("#5A3526", "#3A2019", "#6A3E2A"),
}
INK = hx("#18233B")
LIGHTS = {"body": (-0.34, -0.30, 0.86), "shield": (-0.2, 0.84, 0.48), "cleaver": (-0.5, 0.6, 0.6)}
UPS = {"body": (0, 0, 1), "shield": (0, 1, 0), "cleaver": (-0.4, 0.8, 0.3)}
_face = {}


def face_rgba(x, z):
    """v7 face layout, repainted at 4x: (N,) x, z in final head space -> rgba."""
    if "img" not in _face:
        face_mod.SIZE = 1024
        _face["img"] = face_mod.paint(0.0, 1.46, 0.25, 0.25, head_scale=(0.90, 0.90, (0.0, -0.02, 1.27)))
    img = _face["img"]
    u = (x - 0.0) / 0.5 + 0.5
    v = (z - W.HEAD_UP - 0.018 - 1.46) / 0.5 + 0.5        # the head sits higher and the features sit a little higher in it
    co = np.stack([v * 1024 - 0.5, u * 1024 - 0.5])
    out = np.stack([ndi.map_coordinates(img[..., c], co, order=1, mode="constant") for c in range(4)], 1)
    return out.astype(F)


def terms(o, P, gid):
    n = o.group_sample(P, gid, lambda vol, p: o.grad(vol, p, o.h * 1.5))
    L = np.array(LIGHTS[o.name], F)
    L /= np.linalg.norm(L)
    hl = (n @ L) * 0.5 + 0.5
    ao = np.zeros(len(P), F)
    for r, w in ((0.012, 0.2), (0.03, 0.3), (0.06, 0.3), (0.11, 0.2)):
        ao += w * np.clip(o.s(o.far, P + n * r) / r, 0, 1)
    up = np.array(UPS[o.name], F)
    up /= np.linalg.norm(up)
    ao = 0.75 * ao + 0.25 * np.clip(o.s(o.far, P + (n * 0.5 + up * 0.7) * 0.09) / 0.07, 0, 1)
    # distance to the nearest OTHER group at this surface point
    d1, d2 = o.s(o.d1, P), o.s(o.d2, P)
    own = o.group_sample(P, gid, lambda vol, p: o.s(vol, p)[:, None])[:, 0]
    other = np.where(np.abs(own - d1) < o.h * 0.6, d2, d1)
    line = 1.0 - ss(0.0035, 0.0075, other)
    # curvature = divergence of the sculpt normal over 8 mm: about 40 on a 2.4 cm bevel, 80 on its corners,
    # 10 on the head, 0 on a flat face, negative in a crease. This is what draws the thin light edge lines.
    aux = np.where(np.abs(n[:, 2:3]) < 0.9, np.array([[0, 0, 1]], F), np.array([[1, 0, 0]], F))
    t1 = np.cross(n, aux)
    t1 /= np.maximum(np.linalg.norm(t1, axis=1, keepdims=True), 1e-9)
    t2 = np.cross(n, t1)
    gn = lambda Q: o.group_sample(Q, gid, lambda vol, q: o.grad(vol, q, o.h * 1.5))
    dd = 0.008
    div = np.zeros(len(P), F)
    for t in (t1, t2):
        div += ((gn(P + t * dd) - gn(P - t * dd)) * t).sum(1) / (2 * dd)
    edge = ss(20.0, 34.0, div)
    edge_b = ss(8.0, 26.0, div)
    cav = o.group_sample(P, gid, lambda vol, p: ((0.018 - o.s(vol, p + o.grad(vol, p) * 0.018)) / 0.018)[:, None])[:, 0]
    return dict(n=n, hl=hl, ao=np.clip(ao, 0, 1), line=line, edge=np.clip(edge, 0, 1), edge_b=np.clip(edge_b, 0, 1),
                cav=np.clip(cav, 0, 1), L=L, up=up)


def paint(o, P, gid):
    T = terms(o, P, gid)
    n, hl, ao, edge, cav = T["n"], T["hl"], T["ao"], T["edge"], T["cav"]
    N = len(P)
    alb = np.zeros((N, 3), F)
    cloth = np.zeros(N, F)
    glow = np.zeros(N, F)
    emis = np.zeros(N, F)
    upness = n @ T["up"]
    for name, (b, s, l) in MAT.items():
        m = gid == G[name]
        if not m.any():
            continue
        b, s, l = hx(b), hx(s), hx(l)
        p, h_, a_, e_, c_, nn, upn = P[m], hl[m], ao[m], edge[m], cav[m], n[m], upness[m]
        eb = T["edge_b"][m]
        hard = name in ("stone", "stone_dark", "stone_blade", "brass", "iron", "hair")
        w = 0.035 if hard else 0.16                       # crisp planes on hard materials, soft roll on cloth and skin
        t_sh = ss((0.58 if hard else 0.50) - w, (0.58 if hard else 0.50) + w, h_)
        t_li = ss(0.80 - w * 0.8, 0.80 + w * 0.8, h_)
        c = mix(mix(s, b, t_sh), l, t_li * (0.75 if hard else 0.45))
        c = c * (0.90 + 0.10 * h_[:, None])              # a slow painted gradient across each form
        occ = (1 - a_) ** 0.9
        c = mix(c, s * 0.72, np.clip(occ * 1.25, 0, 1) * 0.85)
        c = mix(c, s * 0.62, ss(0.25, 0.75, c_) * 0.75)   # creases of the own form
        hi_e = e_ * ss(0.40, 0.62, h_)    # crisp light edge, lit side only
        if name == "stone" or name == "stone_blade" or name == "stone_dark":
            bev = e_                                                   # the bevel itself (radius 2-3 cm)
            c = mix(c, np.minimum(l * 1.12, 1.0), bev * ss(0.42, 0.60, h_) * 0.85)     # crisp light bevel, lit side
            c = mix(c, s * 0.80, bev * ss(0.40, 0.22, h_) * 0.55)                      # dark bevel, shade side
            nick = ss(0.60, 0.63, noise(p, (13, 13, 13), 7.7)) * e_    # a few nicks ON the edges only
            c = mix(c, s * 0.85, nick * 0.7)
            crack = ss(0.012, 0.004, np.abs(noise(p, (6, 6, 6), 1.3) - 0.5)) * ss(0.62, 0.72, noise(p, (3, 3, 3), 9.0))
            c = mix(c, s * 0.72, crack * 0.75)
            if name == "stone":                           # small teal shards (team glow), a few per block
                # crisp crystal chips: an octahedron in some cells of a 13 cm lattice, cut by the stone surface
                cell = np.floor(p * 7.7)
                q = p * 7.7 - cell - 0.5
                hsh = np.abs(np.sin(cell @ np.array([12.9898, 78.233, 37.719], F)) * 43758.5453) % 1.0
                oct_ = np.abs(q[:, 0]) * 1.3 + np.abs(q[:, 1]) + np.abs(q[:, 2]) * 1.15
                shard = ss(0.46, 0.445, oct_) * (hsh < 0.40) * ss(0.35, 0.5, a_)
                c = mix(c, mix(hx("#2CB4D2"), hx("#93EEF6"), ss(0.36, 0.16, oct_)), shard)
                c = mix(c, hx("#176083"), shard * ss(0.41, 0.445, oct_) * 0.85)            # dark rim: the chip sits IN the stone
                glow[m] = shard
                emis[m] = shard * 0.45
        elif name in ("brass", "iron"):
            V = (T["L"] + T["up"] * 0.4)
            V /= np.linalg.norm(V)
            spec = np.clip(nn @ V, 0, 1) ** 14
            c = mix(c, l, ss(0.30, 0.55, spec) * 0.8)
            c = mix(c, np.array([1.0, 0.97, 0.84], F) if name == "brass" else hx("#D7E4F4"), ss(0.70, 0.78, spec) * 0.95)
            bounce = ss(0.35, 0.05, h_) * ss(0.0, -0.6, upn)       # warm bounce light on the underside
            c = mix(c, b * 1.05, bounce * 0.35)
            c = mix(c, np.minimum(l * 1.05, 1.0), e_ * ss(0.45, 0.62, h_) * 0.5)
        elif name == "hair":
            c = mix(c, l * 1.10, ss(0.80, 0.84, h_) * 0.55)                # one clean painted light shape per lock
            c = mix(c, s * 0.85, ss(0.15, 0.6, c_) * 0.5)
            c = mix(c, l, eb * ss(0.5, 0.7, h_) * 0.35)
        elif name in ("leather", "leather_dark", "boot", "wrap"):
            c = mix(c, l, e_ * ss(0.45, 0.62, h_) * 0.6)
        elif name == "wood":
            g1 = noise(p, (55, 3.0, 55), 1.0)
            grain = ss(0.035, 0.0, np.abs(g1 - 0.5)) * 0.5 + ss(0.02, 0.0, np.abs(noise(p, (90, 5.0, 90), 6.0) - 0.42)) * 0.3
            plank = np.floor((p[:, 0] + 0.1) / 0.2)
            c *= (1.0 + 0.07 * np.sin(plank * 2.4 + 1.0))[:, None]
            r = np.sqrt(p[:, 0] ** 2 + p[:, 1] ** 2)
            c = mix(c, l, ss(0.25, 0.9, p[:, 1] / 0.5 * 0.5 + 0.5) * 0.30)            # lighter toward the top of the face
            c = mix(c, s, grain)
            c = mix(c, s * 0.8, ss(0.40, 0.49, r) * 0.5)
            c = mix(c, l * 1.05, hi_e * 0.6)
            ang = np.arctan2(p[:, 1], p[:, 0])
            spl = ss(0.50, 0.56, noise(p, (9, 9, 9), 12.0) + 0.42 * ss(0.33, 0.47, r) * np.exp(-((ang + 0.95) / 0.42) ** 2)
                     + 0.36 * ss(0.36, 0.47, r) * np.exp(-((ang - 2.0) / 0.22) ** 2) - 0.12) * (p[:, 2] > 0)
            tc = mix(hx("#2A95AE"), hx("#62CBDC"), ss(0.5, 0.8, h_))
            c = mix(c, tc * (0.9 + 0.1 * g1[:, None]), spl)
            cloth[m] = spl
        elif name == "glow":
            if o.name == "cleaver":
                lip = ss(0.10, 0.31, p[:, 0])              # from the stone toward the cutting lip
                c = mix(hx("#1FA9CF"), hx("#5FE6F2"), ss(0.0, 0.55, lip))
                c = mix(c, hx("#E9FEFF"), ss(0.62, 0.95, lip))
                facet = ss(0.5, 0.56, noise(p, (11, 6, 3), 3.0))
                c = mix(c, hx("#A9F4FA"), facet * 0.45 * (1 - ss(0.6, 0.9, lip)))
                c = mix(c, hx("#F4FFFF"), hi_e * 0.8)
            else:
                c = mix(hx("#22A9D2"), hx("#58E2F0"), ss(0.30, 0.70, h_))
                c = mix(c, hx("#D6FBFF"), ss(0.84, 0.93, h_) * 0.8)
            glow[m] = 1.0
            emis[m] = 1.0
        elif name == "teal":
            c = mix(c, s * 0.80, ss(0.10, 0.55, c_) * 0.55)               # fold creases drawn dark and clean
            c = mix(c, l, hi_e * 0.45)
            if o.name == "body":                          # tabard: a pale band and hem trim that follow the zigzag
                ax = np.abs(p[:, 0])
                zh = 0.82 + np.interp(ax, [0, 0.06, 0.125], [-0.32, -0.37, -0.31]) * 0.99
                dz = p[:, 2] - zh
                tab = (p[:, 2] < 0.74) & (ax < 0.14)
                band = (ss(0.030, 0.036, dz) * ss(0.074, 0.068, dz)) * tab
                c = mix(c, hx("#9FE3EC") * (0.82 + 0.18 * h_[:, None]), band * 0.95)
                c = mix(c, s, ss(0.0, 0.25, (0.74 - p[:, 2]) / 0.3)[:, None] * 0.0 + (tab * ss(0.70, 0.76, p[:, 2]) * 0.5)[:, None])
            cloth[m] = 1.0
        elif name in ("trouser", "cream", "navy"):
            if name == "trouser":                         # gathered folds: vertical streaks that deepen toward the knee
                ang = np.arctan2(p[:, 1], np.abs(p[:, 0]) - 0.1915)
                fold = np.sin(ang * 7.0 + noise(p, (6, 6, 6), 2.0) * 6.0)
                dep = ss(0.50, 0.28, p[:, 2])
                c = mix(c, s, ss(0.55, 0.95, fold) * dep * 0.30)
                c = mix(c, l, ss(-0.55, -0.95, fold) * dep * 0.25)
            if name == "navy":
                c = mix(c, l, hi_e * 0.3 + ss(0.80, 0.92, h_) * 0.25)
        elif name == "skin":
            c = mix(c, hx("#F08F78"), ss(0.50, 0.36, h_) * ss(0.25, 0.45, h_) * 0.35)      # warm terminator
            if o.name == "body":
                hm = (p[:, 2] > 1.28) & (nn[:, 1] < -0.15) & (np.abs(p[:, 0]) < 0.235)
                if hm.any():
                    # the face stays in the light: lift baked shade on the front of the head (approved v7 idea)
                    front = ss(-0.15, -0.6, nn[:, 1]) * hm
                    c = mix(c, mix(b, l, 0.18) * (0.93 + 0.07 * a_[:, None]), front * 0.80)
                    fr = face_rgba(p[hm, 0], p[hm, 2])
                    k = fr[:, 3] * ss(-0.15, -0.35, nn[hm, 1])
                    c[hm] = c[hm] * (1 - k[:, None]) + fr[:, :3] * k[:, None]
        alb[m] = c
    # contact linework: thin, dark, only where two materials meet; never on the glow
    isglow = gid == G["glow"]
    ln = T["line"] * (1 - isglow)
    alb = mix(alb, mix(alb * 0.35, INK, 0.45), ln * 0.85)
    return dict(albedo=np.clip(alb, 0, 1), cloth=cloth, glow=glow, emis=emis, n=n, ao=ao, line=ln, hl=hl)


def preview_shade(o, P, gid, res, view_dir, light=None, ramp=True):
    """What the proposed game shader would show: albedo x soft ramp + cool shadow tint + rim + emission."""
    n = res["n"]
    v = -np.array(view_dir, F)
    if light is None:
        fwd = np.array(view_dir, F)
        up = np.array(UPS[o.name], F)
        up /= np.linalg.norm(up)
        right = np.cross(fwd, up)
        light = -right * 0.55 + up * 0.75 - fwd * 0.55
    light = np.array(light, F)
    light /= np.linalg.norm(light)
    hl = (n @ light) * 0.5 + 0.5
    t = ss(0.36, 0.60, hl)
    shade = mix(np.array([0.70, 0.72, 0.86], F)[None], np.ones((1, 3), F), t)
    col = res["albedo"] * (shade if ramp else 1.0)
    rim = np.clip(1 - n @ v, 0, 1) ** 3 * ss(0.45, 0.8, hl)
    col = col + rim[:, None] * 0.22 * np.array([1.0, 0.96, 0.88], F)
    e = res["emis"][:, None]
    col = col * (1 - 0.0 * e) + e * res["albedo"] * 0.35
    return np.clip(col, 0, 1)
