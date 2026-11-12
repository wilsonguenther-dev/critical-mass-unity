"""Silhouette overlap gate: our orthographic renders against the clean bible masks (bible_masks.py).

  python3 iou_gate.py <render_dir> [out_json] [overlay.jpg]

IoU is measured inside the bible crop window after the best UNIFORM scale + translation of our mask (no stretching).
The window matters because several bible crops clip the figure at their edges: whatever of ours falls outside the crop
is not counted either way, because the bible art there is unknown; on sides where the bible figure does not touch
the crop edge the window is open, so overflow there counts against us. Scale is searched within +-22 % of the bbox-height
ratio, translation over every placement (normalised cross-correlation via cv2.matchTemplate).
Also reported: "iou_bbox_stretch", the v1 method (ours stretched to the bible bbox) on the clean masks, for comparison.
"""
import json
import os
import sys

import cv2
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
MASKS = os.path.join(HERE, "bible_masks")

FRAMINGS = {   # framing: (bible mask, our render)
    "front": ("08-front", "ortho_front_azure.png"),
    "three-quarter": ("08-three-quarter", "ortho_three-quarter_azure.png"),
    "profile": ("08-profile", "ortho_profile_azure.png"),
    "back": ("08-back", "ortho_back_azure.png"),
    "accent-crimson": ("08-accent-swap", "ortho_accent_crimson.png"),
    "front-pick": ("08-front-pick", "ortho_front-pick_azure.png"),
    "pose-barricade": ("08-pose-barricade", "ortho_pose-barricade_azure.png"),
    "barricade-deployed": ("08-barricade-deployed", "ortho_barricade-deployed_azure.png"),
    "barricade-damaged": ("08-barricade-damaged", "ortho_barricade-damaged_azure.png"),
}
TURNAROUND = ("front", "three-quarter", "profile", "back")


def bbox(m):
    ys, xs = np.nonzero(m)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def load_ours(path):
    a = np.asarray(Image.open(path).convert("RGBA"))[..., 3] > 127
    x0, y0, x1, y1 = bbox(a)
    return a[y0:y1, x0:x1]


def registered_iou(bm, om):
    """Best IoU over uniform scale and translation; returns (iou, scale, (dx, dy), scaled our mask placed in window)."""
    bm = bm.astype(np.float32)
    h, w = bm.shape
    bx0, by0, bx1, by1 = bbox(bm > 0)
    base = (by1 - by0) / om.shape[0]
    A = bm.sum()
    best = (0.0, None, None, None)
    for s in np.linspace(base * 0.78, base * 1.22, 45):
        oh, ow = max(2, round(om.shape[0] * s)), max(2, round(om.shape[1] * s))
        o = cv2.resize(om.astype(np.float32), (ow, oh), interpolation=cv2.INTER_AREA)
        o = (o > 0.5).astype(np.float32)
        P = max(oh, ow) + 2
        bp = cv2.copyMakeBorder(bm, P, P, P, P, cv2.BORDER_CONSTANT, value=0)
        # the window opens outward on every side where the bible figure is NOT clipped by the crop edge, so overflow
        # there is counted; only on clipped sides is our overflow ignored
        wp = np.zeros((h + 2 * P, w + 2 * P), np.float32)
        r0 = 0 if by0 > 1 else P
        r1 = h + 2 * P if by1 < h - 1 else P + h
        c0 = 0 if bx0 > 1 else P
        c1 = w + 2 * P if bx1 < w - 1 else P + w
        wp[r0:r1, c0:c1] = 1.0
        inter = cv2.matchTemplate(bp, o, cv2.TM_CCORR)
        inwin = cv2.matchTemplate(wp, o, cv2.TM_CCORR)
        iou = inter / np.maximum(1.0, A + inwin - inter)
        k = np.unravel_index(np.argmax(iou), iou.shape)
        if iou[k] > best[0]:
            y, x = k
            placed = np.zeros((h, w), bool)
            ys0, xs0 = y - P, x - P
            sy0, sx0 = max(0, -ys0), max(0, -xs0)
            dy0, dx0 = max(0, ys0), max(0, xs0)
            hh = min(oh - sy0, h - dy0)
            ww = min(ow - sx0, w - dx0)
            if hh > 0 and ww > 0:
                placed[dy0:dy0 + hh, dx0:dx0 + ww] = o[sy0:sy0 + hh, sx0:sx0 + ww] > 0
            best = (float(iou[k]), float(s), (int(xs0), int(ys0)), placed)
    return best


def stretch_iou(bm, om):
    x0, y0, x1, y1 = bbox(bm)
    b = bm[y0:y1, x0:x1]
    o = cv2.resize(om.astype(np.uint8) * 255, (x1 - x0, y1 - y0), interpolation=cv2.INTER_LINEAR) > 127
    return float((b & o).sum() / max(1, (b | o).sum()))


def run(render_dir, out_json=None, overlay=None, names=None):
    res, tiles = {}, []
    for name, (mk, rf) in FRAMINGS.items():
        if names and name not in names:
            continue
        p = os.path.join(render_dir, rf)
        if not os.path.exists(p):
            continue
        bm = np.asarray(Image.open(os.path.join(MASKS, mk + ".png"))) > 127
        om = load_ours(p)
        iou, s, off, placed = registered_iou(bm, om)
        bx0, by0, bx1, by1 = bbox(bm)
        res[name] = {"iou": round(iou, 3), "iou_bbox_stretch": round(stretch_iou(bm, om), 3),
                     "aspect_bible": round((bx1 - bx0) / (by1 - by0), 3), "aspect_ours": round(om.shape[1] / om.shape[0], 3),
                     # where ours spills over (+) or falls short (-) of the bible, in bible-crop px per side
                     "bible_only_px": int((bm & ~placed).sum()), "ours_only_px": int((placed & ~bm).sum())}
        if overlay:
            v = np.full(bm.shape + (3,), 255, np.uint8)
            v[bm & placed] = (90, 90, 90)
            v[bm & ~placed] = (40, 120, 255)     # bible only: blue (we are missing this)
            v[placed & ~bm] = (255, 120, 40)     # ours only: orange (we are too big here)
            im = Image.fromarray(v)
            im = im.resize((round(im.size[0] * 260 / im.size[1]), 260), Image.NEAREST)
            tiles.append(im)
    if out_json:
        with open(out_json, "w") as f:
            json.dump(res, f, indent=1)
    if overlay and tiles:
        W = sum(t.size[0] + 6 for t in tiles)
        rows = []
        row, w = [], 0
        for t in tiles:
            if w + t.size[0] > 1500 and row:
                rows.append(row)
                row, w = [], 0
            row.append(t)
            w += t.size[0] + 6
        rows.append(row)
        W = max(sum(t.size[0] + 6 for t in r) for r in rows)
        sheet = Image.new("RGB", (W, 266 * len(rows)), (200, 200, 200))
        for ri, r in enumerate(rows):
            x = 0
            for t in r:
                sheet.paste(t, (x, ri * 266))
                x += t.size[0] + 6
        sheet.thumbnail((960, 960))
        sheet.save(overlay, "JPEG", quality=62)
    return res


if __name__ == "__main__":
    r = run(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None, sys.argv[3] if len(sys.argv) > 3 else None)
    for k, v in r.items():
        print("IOU", k, v["iou"], "stretch", v["iou_bbox_stretch"], "aspect b/o", v["aspect_bible"], v["aspect_ours"],
              "miss", v["bible_only_px"], "over", v["ours_only_px"])
    t = [r[k]["iou"] for k in TURNAROUND if k in r]
    if t:
        print("TURNAROUND_MIN", min(t))
