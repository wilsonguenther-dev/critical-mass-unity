"""Turn bible crops into measurable GUIDES (system python3 + numpy, Pillow, scipy, OpenCV; no Blender).

  python3 bible_guide.py <ref_dir> <prefix> <out_dir> [--views front,three-quarter,profile,back] [--k 12]

  e.g. python3 bible_guide.py ~/critical-mass/loop/parity/ref 06 out/warrior

Per view writes into <out_dir>:
  guide-<prefix>-<view>.json   mask frame, WIDTH PROFILE (every 1 % of height), REGION MAP (palette colours with share,
                               bbox, centroid, height span, biggest pieces), LANDMARKS, suggested camera
  guide-<prefix>-<view>.npz    arrays for fit_report.py and project_paint.py: mask, labels (-1 bg, -2 outline), rgb
  guide-<prefix>-<view>.jpg    preview: art | region map (numbered) | silhouette with profile and landmarks
All coordinates: see bible_common (y = fraction of character height above the feet, x = from the head anchor).
"""
import hashlib
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bible_common as bc  # noqa: E402

VIEWS = ["front", "three-quarter", "profile", "back"]
# Suggested orthographic camera per view (azimuth 0 = camera in front of a character facing -Y, negative = camera
# toward the character's right so he faces screen right; elevation = camera above the horizon). These are the
# Warrior's tuned framings (render_warrior.py POSES); a class script may tune the azimuth to its drawing's turn.
CAMERAS = {"front": (0.0, 6.0), "three-quarter": (-35.0, 6.0), "profile": (-90.0, 4.0), "back": (180.0, 6.0)}
SCHEMA = "cm-bible-guide/1"


def font(sz=11):
    try:
        return ImageFont.load_default(size=sz)
    except TypeError:
        return ImageFont.load_default()


def region_preview(rgb, L, regions):
    out = np.full(rgb.shape, 255, np.uint8)
    for r in regions:
        if r.get("hex"):
            out[L == r["id"]] = bc.rgb_of(r["hex"]).astype(np.uint8)
    out[L == -2] = (24, 35, 59)
    return out


def make_preview(rgb, m, L, guide, path, H=320):
    fr = bc.Frame(m, guide["frame"]["anchor_x_px"])
    a = Image.fromarray(rgb)
    b = Image.fromarray(region_preview(rgb, L, guide["regions"]))
    c = Image.fromarray(np.where(m[..., None], 205, 255).astype(np.uint8).repeat(3, axis=2))
    s = H / rgb.shape[0]
    W = int(rgb.shape[1] * s)
    a, b, c = [im.resize((W, H), Image.NEAREST if im is not a else Image.LANCZOS) for im in (a, b, c)]
    db, dc = ImageDraw.Draw(b), ImageDraw.Draw(c)
    f = font(11)
    for r in guide["regions"]:
        for comp in r.get("components", [])[:1]:
            cx, cy = comp["centroid"]
            px, py = fr.col(cx) * s, (fr.bot + 1 - cy * fr.H) * s
            db.text((px - 4, py - 6), str(r["id"]), fill=(255, 255, 255), font=f, stroke_width=2,
                    stroke_fill=(0, 0, 0))
    prof = guide["width_profile"]
    for side, colr in (("left", (40, 90, 200)), ("right", (40, 90, 200)), ("core_left", (230, 120, 30)),
                       ("core_right", (230, 120, 30))):
        pts = [((fr.ax + x * fr.H) * s, (fr.bot + 1 - y * fr.H) * s) for y, x in zip(prof["y"], prof[side])
               if x is not None]
        if len(pts) > 1:
            dc.line(pts, fill=colr, width=1)
    for k, v in guide["landmarks"].items():
        if "y" in v and k not in ("feet", "top_of_head"):
            py = (fr.bot + 1 - v["y"] * fr.H) * s
            dc.line([(0, py), (W, py)], fill=(200, 30, 60), width=1)
            dc.text((2, py - 11), f"{k} {v['y']:.2f}", fill=(200, 30, 60), font=f)
    ax = fr.ax * s
    dc.line([(ax, 0), (ax, H)], fill=(120, 120, 120), width=1)
    canvas = Image.new("RGB", (min(960, 3 * W + 8), H + 16), "white")
    for i, im in enumerate((a, b, c)):
        canvas.paste(im, (i * (W + 4), 16))
    ImageDraw.Draw(canvas).text((4, 2), f"{guide['source']['crop']}  regions {len(guide['regions'])}  "
                                        f"H {fr.H:.0f}px  (art | regions | profile+landmarks)",
                                fill=(0, 0, 0), font=f)
    canvas.save(path, quality=70)


def build_guide(path, view, k=12):
    rgb, _ = bc.load_rgba(path)
    paper = bc.estimate_paper(rgb)
    m = bc.bible_mask(rgb, paper)
    fr = bc.Frame(m)
    ol = bc.outline_mask(rgb, m)
    L = bc.segment_regions(rgb, m, ol, k=k)
    regions = bc.region_stats(rgb, L, fr)
    prof = bc.width_profile(m, fr)
    lm = bc.landmarks(m, fr, prof, L, regions, rgb, view=view)
    az, el = CAMERAS.get(view, (0.0, 6.0))
    with open(path, "rb") as fh:
        sha = hashlib.sha1(fh.read()).hexdigest()[:12]
    guide = {
        "schema": SCHEMA,
        "source": {"crop": os.path.basename(path), "path": os.path.abspath(path), "sha1_12": sha,
                   "size_px": [int(rgb.shape[1]), int(rgb.shape[0])], "paper_rgb": [round(float(v), 1) for v in paper]},
        "view": view,
        "camera_suggested": {"projection": "ORTHO", "azimuth_deg": az, "elevation_deg": el,
                             "note": "fit_report normalises by height, so any framing works if the whole character is "
                                     "in frame, the projection is orthographic and the background is transparent "
                                     "(or an ID mask is given)"},
        "frame": fr.to_dict(),
        "coords": "y = fraction of character height above the feet; x = character heights from the head anchor "
                  "(centroid of the silhouette between 75 and 95 % height), screen right positive",
        "silhouette": {"area_h2": round(float(m.sum()) / fr.H ** 2, 4),
                       "width_h": round((fr.right - fr.left + 1) / fr.H, 4),
                       "aspect_w_over_h": round((fr.right - fr.left + 1) / fr.H, 4),
                       "outline_share": round(float((ol & m).sum()) / max(1, m.sum()), 4)},
        "width_profile": prof,
        "regions": regions,
        "landmarks": lm,
    }
    return guide, rgb, m, L, ol


def main():
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(2)
    ref, prefix, out = sys.argv[1], sys.argv[2], sys.argv[3]
    views = VIEWS
    if "--views" in sys.argv:
        views = sys.argv[sys.argv.index("--views") + 1].split(",")
    k = int(sys.argv[sys.argv.index("--k") + 1]) if "--k" in sys.argv else 12
    os.makedirs(out, exist_ok=True)
    for v in views:
        p = os.path.join(ref, f"{prefix}-{v}.png")
        if not os.path.exists(p):
            print("MISSING", p)
            continue
        guide, rgb, m, L, ol = build_guide(p, v, k)
        base = os.path.join(out, f"guide-{prefix}-{v}")
        with open(base + ".json", "w") as fh:
            json.dump(guide, fh, indent=1)
        np.savez_compressed(base + ".npz", mask=m.astype(np.uint8), labels=L.astype(np.int16), rgb=rgb,
                            outline=ol.astype(np.uint8))
        make_preview(rgb, m, L, guide, base + ".jpg")
        lm = {kk: vv.get("y", vv.get("w")) for kk, vv in guide["landmarks"].items()}
        regs = ", ".join(f"{r['id']}:{r.get('name')} {r['share'] * 100:.0f}%" for r in guide["regions"])
        print(f"{prefix}-{v}: H {guide['frame']['height_px']:.0f}px, {len(guide['regions'])} regions [{regs}]")
        print("   landmarks", lm)


if __name__ == "__main__":
    main()
