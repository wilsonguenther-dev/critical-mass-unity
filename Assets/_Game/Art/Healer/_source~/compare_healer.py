"""Review images and measurements for the Healer (system python3 + numpy + scipy + Pillow, not Blender).

  python3 compare_healer.py <render_dir> <out_dir> [bible_ref_dir]

Writes into out_dir: turnaround.jpg, face-closeup.jpg, props.jpg (via sheet.py), gameplay-<team>.jpg,
gameplay-2x.jpg, sbs-lineup.jpg (bible Warrior, Farmer, Miner crops LEFT, ours RIGHT, same figure height),
sbs-<view>.jpg (bible Farmer view LEFT as the style anchor, ours RIGHT), silhouette-64.jpg and checks.json.

Silhouette overlap: every figure is reduced to a 64 px tall black mask (bible: colour distance from the paper,
largest part kept, holes filled; ours: render alpha). Masks are aligned on their bounding-box centre, then the
horizontal shift (+-8 px) and mirror that give the HIGHEST IoU are kept, so the number is the worst case for us.
The bible's own classes against each other are the yardstick.
"""
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as nd

HERE = os.path.dirname(os.path.abspath(__file__))
R, OUT = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
REF = sys.argv[3] if len(sys.argv) > 3 else os.path.expanduser("~/critical-mass/loop/parity/ref")
UP = os.path.expanduser("~/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port")
PAPER = (247, 242, 233)
os.makedirs(OUT, exist_ok=True)
subprocess.run([sys.executable, os.path.join(HERE, "sheet.py"), R, OUT, "all"], check=True)


def font(sz):
    for f in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/Library/Fonts/Arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, sz)
    return ImageFont.load_default()


def save_jpg(im, path, cap=180_000, maxw=1400):
    if im.width > maxw:
        im = im.resize((maxw, int(im.height * maxw / im.width)), Image.LANCZOS)
    for q in (88, 80, 72, 64, 56, 48, 40, 32):
        im.save(path, quality=q, optimize=True)
        if os.path.getsize(path) <= cap:
            break
    return os.path.getsize(path)


def on_paper(im):
    im = im.convert("RGBA")
    p = Image.new("RGBA", im.size, PAPER + (255,))
    p.alpha_composite(im)
    return p.convert("RGB")


def paper_mask(rgb, title_rows=0):
    a = np.asarray(rgb.convert("RGB")).astype(int)
    border = np.concatenate([a[0], a[-1], a[:, 0], a[:, -1]])
    bg = np.median(border, axis=0)
    m = np.sqrt(((a - bg) ** 2).sum(-1)) > 40
    # drop low-chroma light pixels in the bottom fifth (painted ground shadow)
    ch = a.max(-1) - a.min(-1)
    rows = np.where(m.any(1))[0]
    if len(rows):
        y0 = rows[0] + int(0.82 * (rows[-1] - rows[0]))
        sh = (ch < 22) & (a.mean(-1) > 140)
        sh[:y0] = False
        m &= ~sh
    if title_rows:
        m[:title_rows] = False
    return m


def keep_main(m, keep=0.25):
    lab, n = nd.label(nd.binary_dilation(m, iterations=2))
    if n == 0:
        return m
    sizes = nd.sum(m, lab, range(1, n + 1))
    big = [i + 1 for i, s in enumerate(sizes) if s >= keep * sizes.max()]
    out = np.isin(lab, big) & m
    return nd.binary_fill_holes(out)


def leftmost_figure(m):
    lab, n = nd.label(nd.binary_dilation(m, iterations=6))
    sizes = nd.sum(m, lab, range(1, n + 1))
    cands = [i + 1 for i, s in enumerate(sizes) if s >= 0.2 * sizes.max()]
    xs = {i: np.where((lab == i).any(0))[0][0] for i in cands}
    i = min(cands, key=lambda k: xs[k])
    return nd.binary_fill_holes((lab == i) & m)


def crop(m):
    ys, xs = np.where(m)
    return m[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def to_h(m, H=64):
    c = crop(m)
    w = max(1, int(round(c.shape[1] * H / c.shape[0])))
    return np.asarray(Image.fromarray((c * 255).astype(np.uint8)).resize((w, H), Image.BILINEAR)) > 127


def iou_best(a, b, shift=8):
    W = max(a.shape[1], b.shape[1]) + 2 * shift + 4
    best = 0.0
    for bb in (b, b[:, ::-1]):
        A = np.zeros((a.shape[0], W), bool)
        xa = (W - a.shape[1]) // 2
        A[:, xa:xa + a.shape[1]] = a
        for dx in range(-shift, shift + 1):
            Bm = np.zeros_like(A)
            xb = (W - bb.shape[1]) // 2 + dx
            Bm[:, xb:xb + bb.shape[1]] = bb
            u = (A | Bm).sum()
            best = max(best, (A & Bm).sum() / u if u else 0)
    return round(float(best), 3)


def alpha_mask(path):
    return np.asarray(Image.open(path).convert("RGBA"))[..., 3] > 127


checks = {}
masks = {}
for code, name in (("06", "Warrior"), ("07", "Farmer"), ("08", "Miner")):
    masks[name] = keep_main(paper_mask(Image.open(os.path.join(REF, f"{code}-front.png"))))
for name, folder in (("Demolitions v1", "demolitions-v1"), ("Builder v1", "builder-v1")):
    p = os.path.join(UP, folder, "turnaround.jpg")
    if os.path.exists(p):
        try:
            masks[name] = leftmost_figure(paper_mask(Image.open(p), title_rows=48))
        except Exception as e:  # indicative only
            checks.setdefault("notes", []).append(f"{name}: mask failed ({e})")
masks["Healer"] = alpha_mask(os.path.join(R, "ortho_front_azure.png"))
m64 = {k: to_h(v) for k, v in masks.items()}
names = list(m64)
table = {}
for i, a in enumerate(names):
    for b in names[i + 1:]:
        table[f"{a} vs {b}"] = iou_best(m64[a], m64[b])
checks["silhouette_iou_64px_front"] = table
bible_pairs = [table[k] for k in ("Warrior vs Farmer", "Warrior vs Miner", "Farmer vs Miner")]
ours = {k: v for k, v in table.items() if "Healer" in k}
checks["silhouette_summary"] = {
    "bible_classes_against_each_other_max": max(bible_pairs), "bible_classes_mean": round(sum(bible_pairs) / 3, 3),
    "healer_vs_bible_max": max(v for k, v in ours.items() if any(n in k for n in ("Warrior", "Farmer", "Miner"))),
    "healer_vs_all_max": max(ours.values()),
    "gate": "Healer overlap with every class <= the bible classes' own highest mutual overlap",
}
checks["silhouette_summary"]["pass"] = checks["silhouette_summary"]["healer_vs_all_max"] <= max(bible_pairs)

# 64 px silhouette sheet: every class front plus our 8 gameplay facings
tiles = [(k, m64[k]) for k in names]
for f in ("S", "SW", "W", "NW", "N", "NE", "E", "SE"):
    p = os.path.join(R, f"facing_{f}.png")
    if os.path.exists(p):
        tiles.append((f"Healer {f}", to_h(alpha_mask(p))))
S = 3
Wt = sum(t.shape[1] * S + 24 for _, t in tiles) + 24
sheet = Image.new("RGB", (Wt, 64 * S + 70), PAPER)
d = ImageDraw.Draw(sheet)
x = 24
for lb, t in tiles:
    im = Image.fromarray(np.where(t, 24, 247).astype(np.uint8)).resize((t.shape[1] * S, 64 * S), Image.NEAREST)
    sheet.paste(im.convert("RGB"), (x, 12))
    d.text((x, 64 * S + 22), lb, fill=(50, 50, 60), font=font(15))
    x += t.shape[1] * S + 24
checks["silhouette_sheet_bytes"] = save_jpg(sheet, os.path.join(OUT, "silhouette-64.jpg"))


# line-up: bible fronts LEFT, ours RIGHT, scaled to the same figure height
def fig(path, mask):
    im = on_paper(Image.open(path)) if path.endswith(".png") and "ortho" in path else Image.open(path).convert("RGB")
    ys, xs = np.where(mask)
    pad = 12
    box = (max(0, xs.min() - pad), max(0, ys.min() - pad), min(im.width, xs.max() + pad), min(im.height, ys.max() + pad))
    return im.crop(box), ys.max() - ys.min()


H = 520
row = []
for code, name in (("06", "Warrior (bible p06)"), ("07", "Farmer (bible p07)"), ("08", "Miner (bible p08)")):
    nm = name.split()[0]
    row.append((fig(os.path.join(REF, f"{code}-front.png"), masks[nm]), name))
row.append((fig(os.path.join(R, "ortho_front_azure.png"), masks["Healer"]), "Healer v1 (ours)"))
ims = []
for (im, fh), lb in row:
    k = H / fh
    ims.append((im.resize((int(im.width * k), int(im.height * k)), Image.LANCZOS), lb))
Wl = sum(i.width for i, _ in ims) + 30 * (len(ims) + 1)
Hl = max(i.height for i, _ in ims) + 60
lu = Image.new("RGB", (Wl, Hl), PAPER)
d = ImageDraw.Draw(lu)
x = 30
for im, lb in ims:
    lu.paste(im, (x, Hl - 50 - im.height))
    d.text((x, Hl - 40), lb, fill=(50, 50, 60), font=font(20))
    x += im.width + 30
checks["sbs_lineup_bytes"] = save_jpg(lu, os.path.join(OUT, "sbs-lineup.jpg"))

# per-view side-by-sides: bible Farmer (the other woman, style anchor) LEFT, ours RIGHT
for view in ("front", "three-quarter", "profile", "back"):
    L = Image.open(os.path.join(REF, f"07-{view}.png")).convert("RGB")
    Rr = on_paper(Image.open(os.path.join(R, f"ortho_{view}_azure.png")))
    Hs = 560
    L = L.resize((int(L.width * Hs / L.height), Hs), Image.LANCZOS)
    Rr = Rr.resize((int(Rr.width * Hs / Rr.height), Hs), Image.LANCZOS)
    s = Image.new("RGB", (L.width + Rr.width + 60, Hs + 50), PAPER)
    s.paste(L, (20, 40))
    s.paste(Rr, (L.width + 40, 40))
    d = ImageDraw.Draw(s)
    d.text((20, 10), f"bible 07-{view}.png (Farmer, style anchor)", fill=(50, 50, 60), font=font(18))
    d.text((L.width + 40, 10), f"ours: Healer v1 {view}", fill=(50, 50, 60), font=font(18))
    checks[f"sbs_{view}_bytes"] = save_jpg(s, os.path.join(OUT, f"sbs-{view}.jpg"))

# gameplay
for team in ("azure", "crimson"):
    p = os.path.join(R, f"gameplay_{team}.png")
    if os.path.exists(p):
        checks[f"gameplay_{team}_bytes"] = save_jpg(Image.open(p).convert("RGB"), os.path.join(OUT, f"gameplay-{team}.jpg"))
pa, pc = (os.path.join(R, f"gameplay2x_{t}.png") for t in ("azure", "crimson"))
if os.path.exists(pa):
    A, C = Image.open(pa).convert("RGB"), Image.open(pc).convert("RGB")
    s = Image.new("RGB", (A.width + C.width + 30, max(A.height, C.height) + 40), (128, 128, 128))
    s.paste(A, (10, 30))
    s.paste(C, (A.width + 20, 30))
    ImageDraw.Draw(s).text((10, 6), "gameplay camera, true 2x: Azure | Crimson", fill=(255, 255, 255), font=font(18))
    checks["gameplay_2x_bytes"] = save_jpg(s, os.path.join(OUT, "gameplay-2x.jpg"))

# outline presence on the front sheet: share of the silhouette rim drawn in Void Navy
a = np.asarray(Image.open(os.path.join(R, "ortho_front_azure.png")).convert("RGBA")).astype(int)
m = a[..., 3] > 127
rim = m & ~nd.binary_erosion(m, iterations=2)
navy = np.array([0x18, 0x23, 0x3B])
dark = np.sqrt(((a[..., :3] - navy) ** 2).sum(-1)) < 60
checks["outline_rim_navy_share_front"] = round(float((dark & rim).sum() / max(rim.sum(), 1)), 3)

# team share at the gameplay camera (2x crop, Azure): pixels near the team azure hues
g = np.asarray(Image.open(pa).convert("RGB")).astype(int) if os.path.exists(pa) else None
if g is not None:
    fg = np.sqrt(((g - 128) ** 2).sum(-1)) > 12
    team = np.zeros(fg.shape, bool)
    for hx in ("#3E78C6", "#2E9BE6", "#8BE6FF"):
        c = np.array([int(hx[i:i + 2], 16) for i in (1, 3, 5)])
        team |= np.sqrt(((g - c) ** 2).sum(-1)) < 55
    checks["team_pixels_share_gameplay_2x"] = round(float((team & fg).sum() / max(fg.sum(), 1)), 3)

with open(os.path.join(OUT, "checks.json"), "w") as f:
    json.dump(checks, f, indent=1)
print(json.dumps(checks, indent=1))
