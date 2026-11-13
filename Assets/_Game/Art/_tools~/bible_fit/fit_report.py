"""Compare a render of OUR model against a bible guide (system python3: numpy, Pillow, scipy, OpenCV; no Blender).

  python3 fit_report.py <guide.json> <render.png> <out_prefix> [--mask render.mask.png] [--name warrior-front]

The render: ORTHOGRAPHIC, the camera from the guide's camera_suggested (or the class's tuned framing for that view),
whole character in frame, transparent background (or pass the white-on-black ID mask with --mask). Framing and
resolution are free: both silhouettes are scaled to the same character height, feet on the same line, and ours is
slid sideways to the best overlap.

Writes <out_prefix>.jpg (at most 960 px wide: bible | ours | signed difference with both width profiles | region
bars | the ranked to-do list) and <out_prefix>.json (all numbers, the to-do list first).
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bible_common as bc  # noqa: E402

PAST = (255, 140, 26)      # ours extends past the bible (bloat)
SHORT = (47, 123, 255)     # the bible has mass ours lacks
BOTH = (214, 214, 214)


def font(sz=11):
    try:
        return ImageFont.load_default(size=sz)
    except TypeError:
        return ImageFont.load_default()


def load_guide(path):
    with open(path) as fh:
        g = json.load(fh)
    z = np.load(os.path.splitext(path)[0] + ".npz")
    return g, z["mask"].astype(bool), z["labels"].astype(np.int32), z["rgb"]


def scale_to(rgb, m, H_target):
    """Crop ours to its silhouette, scale so the character is H_target px tall (area filter for colour, majority
    for the mask)."""
    ys, xs = np.where(m)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    s = H_target / float(y1 - y0)
    W, H = max(1, int(round((x1 - x0) * s))), int(round(H_target))
    c = Image.fromarray(rgb[y0:y1, x0:x1]).resize((W, H), Image.BOX)
    mm = Image.fromarray((m[y0:y1, x0:x1] * 255).astype(np.uint8)).resize((W, H), Image.BOX)
    return np.asarray(c), np.asarray(mm) > 127, s


def place(mb, m_o, rgb_o, fb, shift):
    """Put our scaled crop on a canvas the size of the bible crop + margins, feet on the bible's feet row."""
    pad = int(0.6 * fb.H)
    Hc, Wc = mb.shape[0] + 2 * pad, mb.shape[1] + 2 * pad
    canvas_m = np.zeros((Hc, Wc), bool)
    canvas_rgb = np.full((Hc, Wc, 3), 255, np.uint8)
    h, w = m_o.shape
    oy = fb.bot + 1 + pad - h
    ox = shift + pad
    ya, yb, xa, xb = max(0, oy), min(Hc, oy + h), max(0, ox), min(Wc, ox + w)
    if yb > ya and xb > xa:
        canvas_m[ya:yb, xa:xb] = m_o[ya - oy:yb - oy, xa - ox:xb - ox]
        canvas_rgb[ya:yb, xa:xb] = rgb_o[ya - oy:yb - oy, xa - ox:xb - ox]
    bm = np.zeros((Hc, Wc), bool)
    bm[pad:pad + mb.shape[0], pad:pad + mb.shape[1]] = mb
    return bm, canvas_m, canvas_rgb, pad


def iou(a, b):
    u = (a | b).sum()
    return float((a & b).sum()) / u if u else 0.0


def best_shift(mb, fb, m_o):
    """Slide ours sideways (whole pixels, within 25 % of height of the head-anchor alignment) to the best IoU."""
    fo = bc.Frame(np.pad(m_o, 0))
    base = int(round(fb.ax - fo.ax))
    best = (-1, base)
    for d in range(-int(0.25 * fb.H), int(0.25 * fb.H) + 1):
        bm, om, _, _ = place(mb, m_o, np.zeros(m_o.shape + (3,), np.uint8), fb, base + d)
        v = iou(bm, om)
        if v > best[0]:
            best = (v, base + d)
    return best[1], best[0], best[1] - base


def runs_where(mask_bool):
    out, start = [], None
    for i, v in enumerate(list(mask_bool) + [False]):
        if v and start is None:
            start = i
        elif not v and start is not None:
            out.append((start, i - 1))
            start = None
    return out


def band_cover(L, rid, fr):
    """Share of all colour pixels that are region rid, per 10 % height band (sums to the region's share)."""
    ys, _ = np.where(L == rid)
    tot = max(1, int((L >= 0).sum() + (L == -3).sum()))
    yn = fr.yn(ys)
    h, _ = np.histogram(np.clip(yn, 0, 0.9999), bins=10, range=(0, 1))
    return h / float(tot)


def pct(v):
    return f"{v * 100:.0f}%"


def compare(guide, mb, Lb, rgb_b, rgb_o_full, m_o_full):
    fb = bc.Frame(mb, guide["frame"]["anchor_x_px"])
    rgb_o, m_o, s = scale_to(rgb_o_full, m_o_full, fb.H)
    shift, best_iou, slide = best_shift(mb, fb, m_o)
    bm, om, orgb, pad = place(mb, m_o, rgb_o, fb, shift)
    # frames on the shared canvas; ours uses the bible anchor (after the slide) so x is comparable
    fbc = bc.Frame(bm, fb.ax + pad)
    foc = bc.Frame(om, fb.ax + pad)
    pb = guide["width_profile"]
    po = bc.width_profile(om, foc)
    ys = np.array(pb["y"])
    Lb_c = np.full(bm.shape, -1, np.int32)
    Lb_c[pad:pad + Lb.shape[0], pad:pad + Lb.shape[1]] = Lb
    # our regions: nearest bible palette colour
    ol_o = bc.outline_mask(orgb, om)
    regs_b = [r for r in guide["regions"] if r.get("lab")]
    Lo = bc.assign_to_palette(orgb, om, ol_o, [r["lab"] for r in regs_b])
    Lo = bc.absorb_specks(Lo, int((Lo >= 0).sum()))
    ro = bc.region_stats(orgb, Lo, foc, n_regions=len(regs_b), names=False)
    cls_o = (Lo >= 0).sum() + (Lo == -3).sum()
    for r in ro:                       # share of all our colour pixels (unmatched ones count in the total)
        r["share"] = round(float((Lo == r["id"]).sum()) / max(1, cls_o), 4)
    unmatched = float((Lo == -3).sum()) / max(1, cls_o)
    # the bible measured the same way (nearest palette colour), so a perfect match reads exactly zero
    brgb_c = np.full(bm.shape + (3,), 255, np.uint8)
    brgb_c[pad:pad + rgb_b.shape[0], pad:pad + rgb_b.shape[1]] = rgb_b
    Lb_a = Lb_c                       # the guide's region map (k-means, specks absorbed) is the bible truth
    rb_a = bc.region_stats(brgb_c, Lb_a, fbc, n_regions=len(regs_b), names=False)
    cls_b = (Lb_a >= 0).sum()
    for r, src in zip(rb_a, regs_b):
        r["share"] = round(float((Lb_a == r["id"]).sum()) / max(1, cls_b), 4)
        r["name"], r["hex"] = src.get("name"), src["hex"]
        if r.get("span_y") is None:
            r["span_y"], r["centroid"] = src["span_y"], src["centroid"]
    regs_cmp = rb_a
    skin = next((r["id"] for r in regs_b if r.get("name", "").startswith("skin")), None)
    lm_b = guide["landmarks"]
    lm_o = bc.landmarks(om, foc, po, Lo, ro, orgb, skin_id=skin, view=guide.get("view"))

    items = []
    total_b = float(np.nansum(bc.arr(pb, "fill"))) * 0.01
    # 1) outer width (span) bands
    Lb_, Rb_ = bc.arr(pb, "left"), bc.arr(pb, "right")
    Lo_, Ro_ = bc.arr(po, "left"), bc.arr(po, "right")
    span_b, span_o = Rb_ - Lb_, Ro_ - Lo_
    rel = (span_o - span_b) / np.maximum(span_b, 0.05)
    for sign in (1, -1):
        for a, b in runs_where(np.nan_to_num(rel * sign) > 0.08):
            if b - a < 2:
                continue
            r = np.nanmean(rel[a:b + 1])
            area = np.nansum(np.abs(span_o - span_b)[a:b + 1]) * 0.01
            zone = bc.band_name(ys[a], ys[b], lm_b)
            items.append({"kind": "width", "score": area / total_b, "y0": ys[a], "y1": ys[b],
                          "text": f"{zone}: silhouette {pct(abs(r))} too {'wide' if r > 0 else 'narrow'} between "
                                  f"{pct(ys[a])} and {pct(ys[b])} height (bible {np.nanmean(span_b[a:b + 1]):.2f}, "
                                  f"ours {np.nanmean(span_o[a:b + 1]):.2f} character heights)"})
    # 2) one-sided edges (a prop or limb sticking out, or missing, on one side)
    for side, d in (("screen-left", Lb_ - Lo_), ("screen-right", Ro_ - Rb_)):
        for sign in (1, -1):
            for a, b in runs_where(np.nan_to_num(d * sign) > 0.03):
                if b - a < 2:
                    continue
                mv = np.nanmean(d[a:b + 1])
                area = np.nansum(np.abs(d[a:b + 1])) * 0.01
                zone = bc.band_name(ys[a], ys[b], lm_b)
                items.append({"kind": "edge", "score": area / total_b, "y0": ys[a], "y1": ys[b],
                              "text": f"{zone}: the {side} edge {'sticks out' if mv > 0 else 'falls short'} by "
                                      f"{pct(abs(mv))} of height between {pct(ys[a])} and {pct(ys[b])} height"})
    # 3) filled mass bands (gaps between limbs, thickness)
    fb_, fo_ = bc.arr(pb, "fill"), bc.arr(po, "fill")
    relf = (fo_ - fb_) / np.maximum(fb_, 0.05)
    for sign in (1, -1):
        for a, b in runs_where(np.nan_to_num(relf * sign) > 0.12):
            if b - a < 2:
                continue
            r = np.nanmean(relf[a:b + 1])
            area = np.nansum(np.abs(fo_ - fb_)[a:b + 1]) * 0.01
            zone = bc.band_name(ys[a], ys[b], lm_b)
            items.append({"kind": "mass", "score": area / total_b, "y0": ys[a], "y1": ys[b],
                          "text": f"{zone}: {pct(abs(r))} {'too much' if r > 0 else 'too little'} filled mass between "
                                  f"{pct(ys[a])} and {pct(ys[b])} height"})
    # 4) colour regions
    region_rows = []
    for rb, r_o in zip(regs_cmp, ro):
        row = {"id": rb["id"], "name": rb.get("name"), "hex": rb["hex"], "share_bible": rb["share"],
               "share_ours": r_o["share"], "span_bible": rb["span_y"], "span_ours": r_o.get("span_y"),
               "centroid_bible": rb["centroid"], "centroid_ours": r_o.get("centroid")}
        region_rows.append(row)
        if rb["share"] < 0.015 and r_o["share"] < 0.015:
            continue
        ds = r_o["share"] - rb["share"]
        # where the colour sits: its share of the whole character in each 10 % height band, bible vs ours
        hb_ = band_cover(Lb_a, rb["id"], fbc)
        ho_ = band_cover(Lo, rb["id"], foc)
        row["bands_bible"], row["bands_ours"] = [round(float(v), 4) for v in hb_], [round(float(v), 4) for v in ho_]
        tv = 0.5 * float(np.abs(ho_ - hb_).sum())
        if tv < 0.02:
            continue                     # inside the measurement noise (self-test floor about 1.5 points)
        txt = f"{rb.get('name')} ({rb['hex']}) covers {pct(rb['share'])} in the bible and {pct(r_o['share'])} in ours"
        dif = ho_ - hb_
        k1, k2 = int(np.argmin(dif)), int(np.argmax(dif))
        if dif[k1] < -0.008:
            txt += (f"; missing most at {k1 * 10}-{k1 * 10 + 10}% height (bible {hb_[k1] * 100:.1f}% of the "
                    f"character there, ours {ho_[k1] * 100:.1f}%)")
        if dif[k2] > 0.008:
            txt += (f"; extra at {k2 * 10}-{k2 * 10 + 10}% height (ours {ho_[k2] * 100:.1f}%, bible "
                    f"{hb_[k2] * 100:.1f}%)")
        sp_err = 0.0
        src = next(r for r in regs_b if r["id"] == rb["id"])
        wgt = 1.0
        if src["lab"][0] < 25:          # very dark tones: painted shading and ink blur into them, half weight
            wgt = 0.5
            txt += " (dark tone: shading-sensitive, lower reliability)"
        items.append({"kind": "colour", "score": wgt * tv, "region": rb["id"],
                      "text": txt})
    # 5) landmarks
    for k, nice in (("chin", "chin (head-body split)"), ("eye_line", "eye line"), ("shoulder", "shoulder line"),
                    ("leg_split", "leg split (hem/crotch)"), ("waist", "waist")):
        if k in lm_b and k in lm_o and lm_b[k].get("confidence") != "low":
            d = lm_o[k]["y"] - lm_b[k]["y"]
            if abs(d) >= 0.02:
                items.append({"kind": "landmark", "score": abs(d) * 0.6, "text":
                              f"{nice} at {pct(lm_o[k]['y'])} height in ours vs {pct(lm_b[k]['y'])} in the bible "
                              f"({'too high' if d > 0 else 'too low'} by {pct(abs(d))} of height)"})
    if "head_width" in lm_b and "head_width" in lm_o:
        wb, wo = lm_b["head_width"]["w"], lm_o["head_width"]["w"]
        if abs(wo - wb) / wb > 0.06:
            items.append({"kind": "landmark", "score": abs(wo - wb) * 0.4, "text":
                          f"head {pct(abs(wo - wb) / wb)} too {'wide' if wo > wb else 'narrow'} "
                          f"({wo:.2f} vs {wb:.2f} of height)"})
    # rank, drop overlapping duplicates of the same band and direction
    items.sort(key=lambda it: -it["score"])
    kept = []
    for it in items:
        dup = False
        if "y0" in it:
            for k in kept:
                if "y0" in k and k["kind"] in ("width", "mass", "edge") and it["kind"] in ("width", "mass", "edge"):
                    ov = min(k["y1"], it["y1"]) - max(k["y0"], it["y0"])
                    if ov > 0.5 * (it["y1"] - it["y0"]) and (("too wide" in k["text"] or "too much" in k["text"]
                                                              or "sticks out" in k["text"]) ==
                                                             ("too wide" in it["text"] or "too much" in it["text"]
                                                              or "sticks out" in it["text"])):
                        dup = True
                        break
        if not dup:
            kept.append(it)
    kept = [it for it in kept if it["score"] >= 0.004]      # below this is measurement noise
    for it in kept:
        it["score"] = round(float(it["score"]), 4)
        for kk in ("y0", "y1"):
            if kk in it:
                it[kk] = round(float(it[kk]), 2)
    diff = {"past_share": round(float((om & ~bm).sum()) / max(1, bm.sum()), 4),
            "short_share": round(float((bm & ~om).sum()) / max(1, bm.sum()), 4)}
    res = {"todo": kept[:8], "iou": round(best_iou, 4), "slide_from_head_anchor_h": round(slide / fb.H, 4),
           "scale_ours_to_bible": round(s, 4), "difference": diff, "unmatched_colour_share": round(unmatched, 4),
           "landmarks_bible": lm_b, "landmarks_ours": lm_o, "regions": region_rows, "all_items": kept,
           "width_profile_ours": po}
    return res, dict(bm=bm, om=om, orgb=orgb, pad=pad, fbc=fbc, Lo=Lo, regs_b=regs_b, pb=pb, po=po)


def draw(guide, rgb_b, res, d, path, title):
    bm, om, orgb, pad, fbc = d["bm"], d["om"], d["orgb"], d["pad"], d["fbc"]
    ys, xs = np.where(bm | om)
    y0, y1 = max(0, ys.min() - 4), min(bm.shape[0], ys.max() + 5)
    x0, x1 = max(0, xs.min() - 4), min(bm.shape[1], xs.max() + 5)
    brgb = np.full(bm.shape + (3,), 255, np.uint8)
    brgb[pad:pad + rgb_b.shape[0], pad:pad + rgb_b.shape[1]] = rgb_b
    heat = np.full(bm.shape + (3,), 255, np.uint8)
    heat[bm & om] = BOTH
    heat[om & ~bm] = PAST
    heat[bm & ~om] = SHORT
    PH = 300
    s = PH / (y1 - y0)
    PW = int((x1 - x0) * s)
    ims = [Image.fromarray(a[y0:y1, x0:x1]).resize((PW, PH), Image.LANCZOS if k < 2 else Image.NEAREST)
           for k, a in enumerate((brgb, orgb, heat))]
    dh = ImageDraw.Draw(ims[2])
    for prof, colr in ((d["pb"], (24, 35, 59)), (d["po"], (200, 70, 0))):
        for side in ("left", "right"):
            pts = [((fbc.ax + x * fbc.H - x0) * s, (fbc.bot + 1 - y * fbc.H - y0) * s)
                   for y, x in zip(prof["y"], prof[side]) if x is not None]
            if len(pts) > 1:
                dh.line(pts, fill=colr, width=1)
    f, fs = font(11), font(10)
    for it in res["todo"]:
        if "y0" in it:
            ya = (fbc.bot + 1 - it["y1"] * fbc.H - y0) * s
            yb = (fbc.bot + 1 - it["y0"] * fbc.H - y0) * s
            dh.rectangle([0, ya, 3, yb], fill=(200, 30, 60))
    W = 960
    colw = (W - 8) // 3
    sc = min(1.0, colw / PW)
    ims = [im.resize((int(PW * sc), int(PH * sc))) for im in ims]
    PH2 = ims[0].height
    regs = [r for r in res["regions"] if r["share_bible"] >= 0.01 or r["share_ours"] >= 0.01]
    bar_h = 12 * len(regs) + 18
    todo_h = 14 * len(res["todo"]) + 22
    canvas = Image.new("RGB", (W, 18 + PH2 + 6 + bar_h + todo_h), "white")
    dc = ImageDraw.Draw(canvas)
    dc.text((4, 3), f"{title}   IoU {res['iou']:.3f}   ours past the bible {pct(res['difference']['past_share'])} "
                    f"(orange), short {pct(res['difference']['short_share'])} (blue)   lines: bible navy, ours orange",
            fill=(0, 0, 0), font=f)
    for i, im in enumerate(ims):
        canvas.paste(im, (i * (colw + 4), 18))
        dc.text((i * (colw + 4) + 2, 18 + PH2 - 12), ("bible", "ours (scaled, aligned)", "difference")[i],
                fill=(90, 90, 90), font=fs)
    y = 18 + PH2 + 6
    dc.text((4, y), "colour share  (bible bar on top, ours below; span = height range)", fill=(0, 0, 0), font=f)
    y += 15
    for r in regs:
        dc.rectangle([4, y + 1, 14, y + 10], fill=tuple(int(v) for v in bc.rgb_of(r["hex"])))
        dc.text((18, y), f"{r['name'][:14]:14s}", fill=(0, 0, 0), font=fs)
        bx = 120
        dc.rectangle([bx, y + 1, bx + int(r["share_bible"] * 900), y + 5], fill=(24, 35, 59))
        dc.rectangle([bx, y + 6, bx + int(r["share_ours"] * 900), y + 10], fill=(230, 120, 30))
        sb, so = r["span_bible"], r["span_ours"]
        sx = 470
        dc.text((sx - 92, y), f"{pct(r['share_bible'])} / {pct(r['share_ours'])}", fill=(0, 0, 0), font=fs)
        dc.line([(sx, y + 3), (sx + 1, y + 3)], fill=(0, 0, 0))
        dc.rectangle([sx + int(sb[0] * 480), y + 1, sx + int(sb[1] * 480), y + 5], fill=(24, 35, 59))
        if so:
            dc.rectangle([sx + int(so[0] * 480), y + 6, sx + int(so[1] * 480), y + 10], fill=(230, 120, 30))
        y += 12
    y += 4
    dc.text((4, y), "TO DO (largest first)", fill=(200, 30, 60), font=f)
    y += 15
    for i, it in enumerate(res["todo"]):
        dc.text((4, y), f"{i + 1}. {it['text']}"[:170], fill=(0, 0, 0), font=fs)
        y += 14
    canvas.save(path, quality=72)


def main():
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(2)
    gpath, rpath, out = sys.argv[1], sys.argv[2], sys.argv[3]
    mpath = sys.argv[sys.argv.index("--mask") + 1] if "--mask" in sys.argv else None
    name = sys.argv[sys.argv.index("--name") + 1] if "--name" in sys.argv else os.path.basename(out)
    guide, mb, Lb, rgb_b = load_guide(gpath)
    rgb_o, a_o = bc.load_rgba(rpath)
    mrgb = np.asarray(Image.open(mpath).convert("RGB")) if mpath else None
    m_o = bc.render_mask(rgb_o, a_o, mrgb)
    if a_o is not None:                 # composite transparent renders on white (paper) for colour sampling
        al = (a_o.astype(np.float32) / 255.0)[..., None]
        rgb_o = (rgb_o * al + 255 * (1 - al)).astype(np.uint8)
    res, d = compare(guide, mb, Lb, rgb_b, rgb_o, m_o)
    res = {"name": name, "guide": os.path.abspath(gpath), "render": os.path.abspath(rpath), **res}
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out + ".json", "w") as fh:
        json.dump(res, fh, indent=1, default=float)
    draw(guide, rgb_b, res, d, out + ".jpg", name)
    print(f"{name}: IoU {res['iou']:.3f}  past {pct(res['difference']['past_share'])} "
          f"short {pct(res['difference']['short_share'])}")
    for i, it in enumerate(res["todo"]):
        print(f"  {i + 1}. [{it['kind']} {it['score']:.3f}] {it['text']}")


if __name__ == "__main__":
    main()
