"""Final sheets for turnarounds-v1. usage: python3 sheets.py <accepted dir: turnaround/> <obsidian out dir>
turnaround-<Name>.jpg (front, three-quarter, side, back at one scale, <=1400 px, <400 KB), props.jpg, lineup.jpg
(the class fronts at one pixel scale, feet aligned; the Kaiju scaled to the game config: hatchling 2.235 u, adult 5.0 u, a class 2.0 u)."""
import sys, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from measure import fig
A, O = sys.argv[1], sys.argv[2]
F = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 20); NAVY = (24, 35, 59); BG = (205, 205, 205)
NAMES = {"farmer": "Farmer", "miner": "Miner", "builder": "Builder", "healer": "Healer", "demolitions": "Demolitions",
         "kaiju_hatchling": "Kaiju-hatchling", "kaiju_adult": "Kaiju-adult"}
SRC = {"farmer": "bible p7", "miner": "bible p8", "builder": "DESIGNED, not from the bible", "healer": "DESIGNED, not from the bible",
       "demolitions": "DESIGNED, not from the bible", "kaiju_hatchling": "bible p10 (neutral)", "kaiju_adult": "bible p11, neutral colouring"}
def save(im, out, cap):
    if im.width > 1400: im = im.resize((1400, round(im.height * 1400 / im.width)), Image.LANCZOS)
    q = 86
    while True:
        im.save(out, quality=q, optimize=True)
        if os.path.getsize(out) < cap or q < 40: break
        q -= 6
    print(out.split("/")[-1], im.size, os.path.getsize(out))
def crop(p, top, bot):
    im = Image.open(p).convert("RGB"); f = fig(p); m = int(f["w"] * 0.04)
    return im.crop((max(0, f["cx"] - f["w"] // 2 - m), top, min(im.width, f["cx"] + f["w"] // 2 + m), bot))
for k, nm in NAMES.items():
    vs = [f"{A}/{k}/{k}-{v}-2k.jpg" for v in ("front", "three-quarter", "side", "back")]
    if not all(os.path.exists(v) for v in vs): print("skip", k); continue
    fs = [fig(v) for v in vs]; top = max(0, min(f["top"] for f in fs) - 20); bot = min(2048, max(f["bottom"] for f in fs) + 20)
    tiles = [crop(v, top, bot) for v in vs]; Hh = 720
    tiles = [t.resize((round(t.width * Hh / t.height), Hh), Image.LANCZOS) for t in tiles]
    W = sum(t.width for t in tiles) + 12 * 5; sh = Image.new("RGB", (W, Hh + 76), BG); d = ImageDraw.Draw(sh); x = 12
    for t, lb in zip(tiles, ("front", "three-quarter", "side", "back")):
        sh.paste(t, (x, 40)); d.text((x, Hh + 46), lb, fill=NAVY, font=F); x += t.width + 12
    d.text((12, 10), f"{nm}: neutral-pose turnaround, turnarounds-v1 ({SRC[k]}). 2D modelling input, not a 3D model.", fill=NAVY, font=F)
    save(sh, f"{O}/turnaround-{nm}.jpg", 400_000)
# props
props = sorted(p for k in NAMES for p in [os.path.join(A, k, x) for x in (os.listdir(f"{A}/{k}") if os.path.isdir(f"{A}/{k}") else [])] if "-prop-" in p)
if props:
    tiles = []
    for p in props:
        f = fig(p); im = Image.open(p).convert("RGB"); m = 30
        t = im.crop((max(0, f["cx"] - f["w"] // 2 - m), max(0, f["top"] - m), min(2048, f["cx"] + f["w"] // 2 + m), min(2048, f["bottom"] + m)))
        tiles.append((t.resize((round(t.width * 560 / t.height), 560), Image.LANCZOS), os.path.basename(p).split("-prop-")[0] + ": " + os.path.basename(p).split("-prop-")[1].replace("-2k.jpg", "")))
    W = sum(t.width for t, _ in tiles) + 12 * (len(tiles) + 1); sh = Image.new("RGB", (W, 640), BG); d = ImageDraw.Draw(sh); x = 12
    for t, lb in tiles: sh.paste(t, (x, 40)); d.text((x, 610), lb, fill=NAVY, font=F); x += t.width + 12
    d.text((12, 10), "turnarounds-v1: hand-held props, separate clean images (each at its own scale)", fill=NAVY, font=F)
    save(sh, f"{O}/props.jpg", 400_000)
# lineup
items = []
for k in ["warrior"] + list(NAMES):
    p = f"{A}/{k}/{k}-front-2k.jpg" if k != "warrior" else f"{A}/../../warrior_v8/turnaround/warrior-v8-front-2k.jpg"
    if os.path.exists(p): items.append((k, p))
cls = [fig(p)["h"] for k, p in items if not k.startswith("kaiju")]; href = float(np.median(cls))
S = 0.5; tiles = []
for k, p in items:
    f = fig(p); im = Image.open(p).convert("RGB")
    t = im.crop((max(0, f["cx"] - f["w"] // 2 - 10), f["top"], min(2048, f["cx"] + f["w"] // 2 + 10), f["bottom"] + 1))
    sc = S if not k.startswith("kaiju") else S * href * ({"kaiju_hatchling": 2.235, "kaiju_adult": 5.0}[k] / 2.0) / f["h"]
    tiles.append((k, t.resize((round(t.width * sc), round(t.height * sc)), Image.LANCZOS), f["h"]))
Hmax = max(t.height for _, t, _ in tiles); W = sum(t.width for _, t, _ in tiles) + 16 * (len(tiles) + 1)
sh = Image.new("RGB", (W, Hmax + 90), BG); d = ImageDraw.Draw(sh); x = 16; base = Hmax + 50
for k, t, h in tiles:
    sh.paste(t, (x, base - t.height)); d.text((x, base + 8), (NAMES.get(k, "Warrior v8")) + f" ({h}px)", fill=NAVY, font=F); x += t.width + 16
d.line([(0, base), (W, base)], fill=NAVY, width=1)
d.text((16, 10), "turnarounds-v1 lineup: class fronts at one pixel scale, feet on one line; Kaiju scaled to the game config (class 2.0 u, hatchling 2.235 u, adult 5.0 u)", fill=NAVY, font=F)
save(sh, f"{O}/lineup.jpg", 400_000)
