"""Shared measuring code for bible_guide.py and fit_report.py (system python3: numpy, Pillow, scipy, OpenCV).

Normalised frame (used by every number in a guide or report)
  y = height above the feet as a fraction of the character's height: 0 = lowest silhouette pixel, 1 = top of the hair.
  x = horizontal offset from the ANCHOR in the same unit (character heights), screen right positive.
  ANCHOR = centroid x of the silhouette in the head band (y 0.75..0.95), the most stable part of a character between
  the drawing and our model (weapons and shields move, the head does not). fit_report then slides ours sideways to the
  best overlap, so the anchor only seeds the alignment.
"""
import numpy as np
from PIL import Image
from scipy import ndimage
import cv2

PAPER_NOMINAL = np.array((254, 247, 236), dtype=np.float32)   # bible paper (Farmer bible_masks.py)
THRESH = 22.0                                                  # mean |rgb - paper| above this = ink
HOLE_FRAC = 0.004             # enclosed holes smaller than this x height^2 are filled (cream highlights, small gaps)
L_DARK = 30.0                 # Lab L below this can be outline ink (Void Navy outline L ~ 13, navy cloth ~ 23)
WL = 0.5                      # weight of Lightness in colour distances: painted shading changes L more than hue
HEAD_BAND = (0.75, 0.95)

# Names for palette colours (sRGB hex). Bible swatches of the three classes plus generic fallbacks.
NAMED = {
    "team teal": "#2A95AE", "glow cyan": "#4FE3F0", "navy": "#2A374D", "cream": "#F2E5D5", "trouser cream": "#DCC6AE",
    "rust brown": "#AB6844", "dark leather": "#5E3B2B", "boot brown": "#7E4E33", "wood brown": "#9C6040",
    "brass": "#C79245", "warm stone": "#C9B0A0", "icy stone": "#9FB8CF", "dark stone": "#6E6A74", "skin": "#F7B48A",
    "skin shadow": "#D7865F", "hair brown": "#4F353E", "outline navy": "#18233B", "biomass green": "#B7F238",
    "leaf green": "#6E9A3A", "olive": "#7A7A3A", "straw yellow": "#E3C26A", "orange": "#E07A2E", "coral": "#E46753",
    "crimson": "#FF5A5F", "maroon": "#652D2F", "purple": "#8E3FD1", "grey": "#8A8A8A", "white": "#F8F8F8",
    "black": "#202020", "denim blue": "#4A6A9A",
}


def hex_of(rgb):
    r, g, b = [int(round(float(c))) for c in rgb[:3]]
    return "#%02X%02X%02X" % (r, g, b)


def rgb_of(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32)


def to_lab(rgb):
    """uint8/float RGB (..., 3) 0..255 -> Lab (L 0..100)."""
    a = np.asarray(rgb, dtype=np.float32).reshape(-1, 1, 3) / 255.0
    return cv2.cvtColor(a, cv2.COLOR_RGB2Lab).reshape(np.shape(rgb)[:-1] + (3,))


def feat(lab):
    f = np.array(lab, dtype=np.float32, copy=True)
    f[..., 0] *= WL
    return f


_NAMED_LAB = {k: to_lab(rgb_of(v)[None])[0] for k, v in NAMED.items()}


def name_colour(rgb):
    lab = to_lab(np.asarray(rgb, np.float32)[None])[0]
    best, bd = None, 1e9
    for k, v in _NAMED_LAB.items():
        d = float(np.linalg.norm(feat(lab) - feat(v)))
        if d < bd:
            best, bd = k, d
    return best, bd


def load_rgba(path):
    im = Image.open(path)
    if im.mode in ("RGBA", "LA") or (im.mode == "P" and "transparency" in im.info):
        a = np.asarray(im.convert("RGBA"))
        return a[..., :3].copy(), a[..., 3].copy()
    return np.asarray(im.convert("RGB")).copy(), None


def estimate_paper(rgb):
    b = np.concatenate([rgb[0], rgb[-1], rgb[:, 0], rgb[:, -1]]).astype(np.float32)
    d = np.abs(b - PAPER_NOMINAL).mean(axis=1)
    near = b[d < 30]
    return np.median(near, axis=0) if len(near) > 20 else PAPER_NOMINAL


def fill_small_holes(m):
    ys = np.where(m.any(axis=1))[0]
    if len(ys) == 0:
        return m
    h = ys[-1] - ys[0] + 1
    holes = ndimage.binary_fill_holes(m) & ~m
    hl, hn = ndimage.label(holes)
    if hn:
        ha = ndimage.sum(holes, hl, range(1, hn + 1))
        small = np.zeros(hn + 1, bool)
        small[1:] = ha < HOLE_FRAC * h * h
        m = m | small[hl]
    return m


def keep_character(m):
    """Largest connected piece plus big or interior pieces; pieces touching the crop border (neighbouring views) go."""
    lab, n = ndimage.label(m)
    if n == 0:
        return m
    area = ndimage.sum(m, lab, range(1, n + 1))
    big = area.max()
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    keep = np.zeros(n + 1, bool)
    for i in range(1, n + 1):
        a = area[i - 1]
        if a == big or (i not in border and a >= 0.004 * big):
            keep[i] = True
    return keep[lab]


def bible_mask(rgb, paper=None):
    """The Farmer bible_masks.py method (difference from the paper colour, keep the character, fill small holes);
    the paper colour is estimated from the crop border instead of fixed (the 06/08 crops are #FEF8EE, 07 #FEF7EC)."""
    paper = estimate_paper(rgb) if paper is None else paper
    diff = np.abs(rgb.astype(np.float32) - paper).mean(axis=2)
    m = ndimage.binary_closing(diff > THRESH, iterations=1)
    m = fill_small_holes(keep_character(m))
    return drop_ground_shadow(rgb, m)


def drop_ground_shadow(rgb, m):
    """The bible paints a pale, grey-beige contact shadow under the feet. Remove light, low-chroma pixels in the
    bottom 12 % of the silhouette, then keep the character again."""
    ys = np.where(m.any(axis=1))[0]
    if len(ys) == 0:
        return m
    top, bot = ys[0], ys[-1]
    lab = to_lab(rgb)
    chroma = np.hypot(lab[..., 1], lab[..., 2])
    rows = np.arange(m.shape[0])[:, None]
    low = rows > bot - 0.12 * (bot - top + 1)
    sh = m & low & (lab[..., 0] > 70) & (chroma < 20)
    if not sh.any():
        return m
    m2 = keep_character(m & ~sh)
    return ndimage.binary_opening(m2, iterations=1) | (m2 & ~low)


def render_mask(rgb, alpha=None, mask_rgb=None):
    """Our render: an ID mask (white = asset) if given, else alpha, else paper-difference."""
    if mask_rgb is not None:
        m = np.asarray(mask_rgb).astype(np.float32).mean(axis=2) > 127
    elif alpha is not None:
        m = alpha > 127
    else:
        return bible_mask(rgb)
    return fill_small_holes(keep_character(m))


def outline_mask(rgb, m):
    """Outline ink: dark pixels in thin strokes (removed by an opening sized to the stroke), plus a 1 px antialias
    rim. Large dark areas (hair, navy cloth) survive the opening and stay colour."""
    ys = np.where(m.any(axis=1))[0]
    h = ys[-1] - ys[0] + 1 if len(ys) else 100
    lab = to_lab(rgb)
    dark = (lab[..., 0] < L_DARK) & m
    k = max(3, int(round(0.012 * h)))
    se = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k))
    thick = cv2.morphologyEx(dark.astype(np.uint8), cv2.MORPH_OPEN, se).astype(bool)
    thin = dark & ~thick
    rim = m & ~ndimage.binary_erosion(m, iterations=1)
    out = ndimage.binary_dilation(thin, iterations=1) & m & ~thick
    return out | (rim & (lab[..., 0] < 55))


class Frame:
    """Normalised coordinates of one silhouette (see module doc)."""

    def __init__(self, m, anchor_x=None):
        ys, xs = np.where(m)
        self.top, self.bot = int(ys.min()), int(ys.max())
        self.left, self.right = int(xs.min()), int(xs.max())
        self.H = float(self.bot - self.top + 1)
        if anchor_x is None:
            y = self.yn(ys)
            sel = (y >= HEAD_BAND[0]) & (y <= HEAD_BAND[1])
            anchor_x = float(xs[sel].mean() + 0.5) if sel.any() else float(xs.mean() + 0.5)
        self.ax = float(anchor_x)

    def yn(self, r):
        return (self.bot + 1 - (np.asarray(r, np.float64) + 0.5)) / self.H

    def xn(self, c):
        return (np.asarray(c, np.float64) + 0.5 - self.ax) / self.H

    def row(self, y):
        return int(np.clip(round(self.bot + 1 - y * self.H - 0.5), self.top, self.bot))

    def col(self, x):
        return int(round(self.ax + x * self.H - 0.5))

    def to_dict(self):
        return {"top_px": self.top, "bottom_px": self.bot, "height_px": self.H, "anchor_x_px": round(self.ax, 2),
                "left_px": self.left, "right_px": self.right}


def runs_of(row):
    """[(start, end_inclusive)] of True runs in a 1D bool array."""
    d = np.diff(np.concatenate([[0], row.astype(np.int8), [0]]))
    s, e = np.where(d == 1)[0], np.where(d == -1)[0] - 1
    return list(zip(s.tolist(), e.tolist()))


def width_profile(m, fr, n=101):
    """At every 1 % of height: outer left/right extent, filled width, core run (the run under the anchor, or the
    nearest run) left/right, and the number of runs. All in character heights, NaN where the row is empty."""
    out = {k: [] for k in ("y", "left", "right", "fill", "core_left", "core_right", "runs")}
    half = max(0, int(round(fr.H * 0.005)))
    for i in range(n):
        y = i / (n - 1)
        r = fr.row(y)
        rows = m[max(fr.top, r - half):min(fr.bot, r + half) + 1]
        any_row = rows.any(axis=0)
        out["y"].append(round(y, 3))
        if not any_row.any():
            for k in ("left", "right", "fill", "core_left", "core_right"):
                out[k].append(None)
            out["runs"].append(0)
            continue
        xs = np.where(any_row)[0]
        out["left"].append(round(float(fr.xn(xs.min() - 0.5)), 4))
        out["right"].append(round(float(fr.xn(xs.max() + 0.5)), 4))
        out["fill"].append(round(float(rows.sum(axis=1).mean() / fr.H), 4))
        rr = runs_of(m[r])
        if rr:
            a = fr.ax - 0.5
            best = min(rr, key=lambda se: 0 if se[0] <= a <= se[1] else min(abs(se[0] - a), abs(se[1] - a)))
            out["core_left"].append(round(float(fr.xn(best[0] - 0.5)), 4))
            out["core_right"].append(round(float(fr.xn(best[1] + 0.5)), 4))
        else:
            out["core_left"].append(None)
            out["core_right"].append(None)
        out["runs"].append(len(rr))
    return out


def arr(prof, k):
    return np.array([np.nan if v is None else v for v in prof[k]], dtype=np.float64)


def smooth(v, w=3):
    v = np.asarray(v, np.float64)
    ok = ~np.isnan(v)
    if ok.sum() < 2:
        return v
    vi = np.interp(np.arange(len(v)), np.where(ok)[0], v[ok])
    k = np.ones(w) / w
    s = np.convolve(np.pad(vi, w // 2, mode="edge"), k, mode="valid")
    s[~ok] = np.nan
    return s


# ---------------------------------------------------------------- regions
def segment_regions(rgb, m, outline, k=12, merge_dist=9.0, speck_frac=0.0015, min_share=0.006, seed=0):
    """K-means in weighted Lab on the non-outline character pixels, merge near-identical clusters, absorb specks
    into their surroundings, fold tiny clusters into the nearest colour. Returns labels (-1 = background, -2 =
    outline) and a list of cluster centres (median RGB) indexed by label."""
    sel = m & ~outline
    lab = to_lab(rgb)
    f = feat(lab[sel]).astype(np.float32)
    k = int(min(k, max(2, len(f) // 50)))
    cv2.setRNGSeed(seed)
    crit = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 50, 0.2)
    _, lbl, cen = cv2.kmeans(f, k, None, crit, 4, cv2.KMEANS_PP_CENTERS)
    lbl = lbl.ravel()
    # merge clusters closer than merge_dist (share-weighted centres)
    changed = True
    while changed:
        changed = False
        ids = np.unique(lbl)
        cs = {i: f[lbl == i].mean(axis=0) for i in ids}
        best = None
        for ai, a in enumerate(ids):
            for b in ids[ai + 1:]:
                d = float(np.linalg.norm(cs[a] - cs[b]))
                if d < merge_dist and (best is None or d < best[0]):
                    best = (d, a, b)
        if best:
            lbl[lbl == best[2]] = best[1]
            changed = True
    L = np.full(m.shape, -1, np.int32)
    L[outline & m] = -2
    L[sel] = lbl
    area = sel.sum()
    L = absorb_specks(L, area, speck_frac)
    # fold clusters below min_share into the nearest colour
    while True:
        ids, cnt = np.unique(L[L >= 0], return_counts=True)
        small = [(c, i) for i, c in zip(ids, cnt) if c < min_share * area]
        if not small or len(ids) <= 2:
            break
        _, i = min(small)
        others = [j for j in ids if j != i]
        fi = feat(lab[L == i]).mean(axis=0)
        j = min(others, key=lambda o: np.linalg.norm(feat(lab[L == o]).mean(axis=0) - fi))
        L[L == i] = j
    # relabel 0..n-1 by share (largest first)
    ids, cnt = np.unique(L[L >= 0], return_counts=True)
    order = ids[np.argsort(-cnt)]
    out = np.full(m.shape, -1, np.int32)
    out[L == -2] = -2
    for n_, i in enumerate(order):
        out[L == i] = n_
    return out


def absorb_specks(L, area, speck_frac=0.0015):
    """Connected pieces of a label smaller than speck_frac x area take the commonest neighbouring label."""
    L = L.copy()
    for _ in range(2):
        for i in np.unique(L[L >= 0]):
            cl, cn = ndimage.label(L == i)
            if cn == 0:
                continue
            sizes = ndimage.sum(np.ones_like(cl), cl, range(1, cn + 1))
            for j in np.where(sizes < speck_frac * area)[0] + 1:
                piece = cl == j
                ring = ndimage.binary_dilation(piece, iterations=2) & ~piece
                nb = L[ring]
                nb = nb[(nb >= 0) & (nb != i)]
                if len(nb):
                    L[piece] = np.bincount(nb).argmax()
    return L


def region_stats(rgb, L, fr, n_regions=None, names=True):
    lab = to_lab(rgb)
    cls = L >= 0
    total = max(1, int(cls.sum()))
    regs = []
    ids = range(int(L.max()) + 1) if n_regions is None else range(n_regions)
    for i in ids:
        sel = L == i
        c = int(sel.sum())
        ys, xs = np.where(sel)
        r = {"id": int(i), "share": round(c / total, 4)}
        if c == 0:
            r.update({"bbox": None, "centroid": None, "span_y": None})
            regs.append(r)
            continue
        px = rgb[sel].astype(np.float32)
        Ls = lab[sel][:, 0]
        med = np.median(px, axis=0)
        lit = px[Ls >= np.percentile(Ls, 80)].mean(axis=0)
        shade = px[Ls <= np.percentile(Ls, 20)].mean(axis=0)
        yv, xv = fr.yn(ys), fr.xn(xs)
        r.update({
            "hex": hex_of(med), "lit_hex": hex_of(lit), "shade_hex": hex_of(shade),
            "lab": [round(float(v), 1) for v in np.median(lab[sel], axis=0)],
            "bbox": [round(float(xv.min()), 3), round(float(xv.max()), 3), round(float(yv.min()), 3),
                     round(float(yv.max()), 3)],
            "centroid": [round(float(xv.mean()), 3), round(float(yv.mean()), 3)],
            "span_y": [round(float(np.percentile(yv, 5)), 3), round(float(np.percentile(yv, 95)), 3)],
            "span_x": [round(float(np.percentile(xv, 5)), 3), round(float(np.percentile(xv, 95)), 3)],
        })
        if names:
            nm, d = name_colour(med)
            r["name"] = nm if d < 14 else (nm + "-ish")
            r["name_dist"] = round(d, 1)
        cl, cn = ndimage.label(sel)
        comps = []
        if cn:
            sizes = ndimage.sum(np.ones_like(cl), cl, range(1, cn + 1))
            for j in np.argsort(-sizes)[:3]:
                cy, cx = np.where(cl == j + 1)
                cyn, cxn = fr.yn(cy), fr.xn(cx)
                comps.append({"share": round(float(sizes[j]) / total, 4),
                              "bbox": [round(float(cxn.min()), 3), round(float(cxn.max()), 3),
                                       round(float(cyn.min()), 3), round(float(cyn.max()), 3)],
                              "centroid": [round(float(cxn.mean()), 3), round(float(cyn.mean()), 3)]})
        r["components"] = comps
        regs.append(r)
    return regs


def assign_to_palette(rgb, m, outline, centres_lab, reject=28.0):
    """Label our pixels with the nearest bible palette colour (weighted Lab). -3 = no palette colour within reject."""
    lab = feat(to_lab(rgb))
    C = feat(np.asarray(centres_lab, np.float32))
    sel = m & ~outline
    d = np.linalg.norm(lab[sel][:, None, :] - C[None], axis=2)
    lbl = d.argmin(axis=1)
    lbl[d.min(axis=1) > reject] = -3
    L = np.full(m.shape, -1, np.int32)
    L[outline & m] = -2
    L[sel] = lbl
    return L


# ---------------------------------------------------------------- landmarks
DEFAULT_ZONES = {"chin": 0.62, "shoulder": 0.56, "waist": 0.40, "leg_split": 0.22}


def landmarks(m, fr, prof, L, regions, rgb, skin_id=None, view=None):
    """Landmarks in normalised height; each {'y':..., 'method':..., 'confidence': high|medium|low}."""
    lm = {"top_of_head": {"y": 1.0, "method": "top silhouette pixel", "confidence": "high"},
          "feet": {"y": 0.0, "method": "lowest silhouette pixel", "confidence": "high"}}
    cl, cr = smooth(arr(prof, "core_left")), smooth(arr(prof, "core_right"))
    core = cr - cl
    ys = np.array(prof["y"])
    # face: the skin-coloured region's largest piece in the upper half
    if skin_id is None:
        cands = [r for r in regions if r.get("hex") and r["centroid"][1] > 0.5 and r.get("name", "").startswith("skin")]
        cands = cands or [r for r in regions if r.get("lab") and r["centroid"][1] > 0.5 and r["lab"][1] > 8
                          and r["lab"][2] > 14 and r["lab"][0] > 55]
        skin_id = max(cands, key=lambda r: r["share"])["id"] if cands else None
    chin = eye = None
    # head-body split from the outline alone: narrowest core run between 50 and 85 % height (the neck)
    band = (ys >= 0.45) & (ys <= 0.75)
    if band.sum() > 3 and not np.all(np.isnan(core[band])):
        i = int(np.nanargmin(np.where(band, core, np.nan)))
        lm["neck"] = {"y": round(float(ys[i]), 3), "w": round(float(core[i]), 3),
                      "method": "narrowest core run between 45 and 75 % height", "confidence": "low"}
    if view == "back":
        skin_id = -999                     # no face in a back view: chin comes from the neck below
    if skin_id is not None and skin_id >= 0:
        sk = L == skin_id
        lab_, n = ndimage.label(sk)
        if n:
            best, bs = None, 0
            for j in range(1, n + 1):
                yy, xx = np.where(lab_ == j)
                if len(yy) > bs and fr.yn(yy).mean() > 0.5:
                    best, bs = j, len(yy)
            if best is not None and bs > 0.002 * fr.H * fr.H:
                face = lab_ == best
                yy, xx = np.where(face)
                yv = fr.yn(yy)
                chin = float(np.percentile(yv, 1))
                lm["chin"] = {"y": round(chin, 3), "method": "bottom of the largest skin region (face)",
                              "confidence": "high"}
                # eye line: row with most dark pixels inside the filled face hull, upper 75 % of the face
                hull = ndimage.binary_fill_holes(face | (ndimage.binary_closing(face, iterations=3)))
                lab2 = to_lab(rgb)
                darkin = hull & (lab2[..., 0] < 45) & ~face
                rows = np.arange(yy.min(), yy.max() + 1)
                cnt = np.array([darkin[r].sum() for r in rows], np.float64)
                if cnt.max() > 0:
                    cnt = np.convolve(cnt, np.ones(3) / 3, mode="same")
                    face_top = yv.max()
                    ok = fr.yn(rows) > chin + 0.25 * (face_top - chin)
                    if ok.any():
                        r = rows[ok][np.argmax(cnt[ok])]
                        eye = float(fr.yn(r))
                        lm["eye_line"] = {"y": round(eye, 3), "method": "darkest row inside the face (eyes, brows)",
                                          "confidence": "medium"}
    if chin is None and "neck" in lm:
        chin = lm["neck"]["y"]
        lm["chin"] = {"y": chin, "method": "neck (no face visible)", "confidence": "low"}
    if chin is not None:
        top = 1.0
        lo_ = eye if eye is not None else chin + 0.25 * (top - chin)
        band = (ys >= lo_) & (ys <= lo_ + 0.25 * (top - chin))
        if band.any() and not np.all(np.isnan(core[band])):
            hw = float(np.nanmax(core[band]))
            lm["head_width"] = {"w": round(hw, 3), "method": "widest core run from the eye line (or 25 % up the "
                                "head) over the next quarter of the head; above the shoulder armour",
                                "confidence": "medium"}
        # shoulder: highest row below the chin where the core run reaches 85 % of its max in the 25 % below
        band = (ys >= chin - 0.25) & (ys < chin - 0.01)
        if band.any() and not np.all(np.isnan(core[band])):
            mx = np.nanmax(core[band])
            hit = ys[band][core[band] >= 0.85 * mx]
            if len(hit):
                lm["shoulder"] = {"y": round(float(hit.max()), 3), "w": round(float(mx), 3),
                                  "method": "first row below the chin reaching 85 % of the shoulder width",
                                  "confidence": "medium"}
    # leg split: from the feet up, the anchor column is background between the legs; the split is where it fills
    col = int(round(fr.ax - 0.5))
    split = None
    gap_seen = False
    for i, y in enumerate(ys):
        if y < 0.02 or y > 0.6:
            continue
        r = fr.row(y)
        inside = m[r, max(0, col - 1):col + 2].any()
        if not inside:
            gap_seen = True
        elif gap_seen:
            nxt = [m[fr.row(y2), max(0, col - 1):col + 2].any() for y2 in ys[i:i + 3]]
            if all(nxt):
                split = float(y)
                break
    if split is not None:
        lm["leg_split"] = {"y": round(split, 3), "method": "top of the gap between the legs under the head anchor",
                           "confidence": "medium"}
    sh = lm.get("shoulder", {}).get("y", DEFAULT_ZONES["shoulder"])
    lo = (split if split is not None else DEFAULT_ZONES["leg_split"]) + 0.04
    band = (ys >= lo) & (ys <= sh - 0.06)
    if band.sum() > 3 and not np.all(np.isnan(core[band])):
        i = int(np.nanargmin(np.where(band, core, np.nan)))
        lm["waist"] = {"y": round(float(ys[i]), 3), "w": round(float(core[i]), 3),
                       "method": "narrowest core run between the leg split and the shoulders", "confidence": "low"}
    return lm


def zone_name(y, lm):
    def g(k):
        return lm.get(k, {}).get("y", DEFAULT_ZONES.get(k))
    chin, sh, waist, split = g("chin"), g("shoulder"), g("waist"), g("leg_split")
    eye = lm.get("eye_line", {}).get("y")
    if y >= 0.85:
        return "hair mass"
    if eye is not None and y >= eye:
        return "upper head"
    if y >= chin:
        return "face/head"
    if y >= sh:
        return "shoulders"
    if y >= waist:
        return "chest/torso"
    if y >= split:
        return "hips/belt"
    if y >= split * 0.45:
        return "legs"
    return "feet/boots"


def band_name(y0, y1, lm):
    a, b = zone_name(y0, lm), zone_name(y1, lm)
    return a if a == b else f"{b} to {a}"
