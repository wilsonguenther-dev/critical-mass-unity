# hud-art-1 round 2 sheet: each class and the Kaiju beside the character the game shows today.
# usage: python3 sheet_match.py OUT.jpg
import os, sys
from PIL import Image, ImageDraw, ImageFont
here = os.path.dirname(os.path.abspath(__file__)); U = os.path.expanduser("~/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/")
BM = os.path.normpath(os.path.join(here, "../..")); NEW = os.path.normpath(os.path.join(BM, "../Icons"))
OLD = os.path.expanduser("~/critical-mass-unity/Assets/_Game/Resources/UI/Icons")
FONT = os.path.expanduser("~/cm-wt-hud-art-1/Assets/_Game/Art/Fonts/NunitoSans-Bold.ttf")
rows = [  # class, in-game source, crop box, note
    ("warrior", "characters-hy-v1/posed-3d-warrior.jpg", (0, 30, 260, 400), "new model"),
    ("farmer", "characters-hy-v1/posed-3d-farmer.jpg", (8, 30, 215, 420), "new model"),
    ("miner", "miner-v3/views/front.jpg", None, "previous model"),
    ("builder", "builder-v1/sbs-front.jpg", (1100, 0, 1400, 441), "unchanged"),
    ("healer", "healer-v2/sbs-front.jpg", (490, 0, 977, 610), "unchanged"),
    ("demolitions", "demolitions-v1/turnaround.jpg", (0, 20, 350, 480), "unchanged"),
    ("kaiju", "kaiju-v4/sbs-11-three-quarter.jpg", (505, 0, 1020, 662), "previous model"),
]
H = 200; cols = [("IN GAME TODAY", 230), ("ICON BEFORE", 150), ("ICON AFTER", 150), ("CUT-IN AFTER", 230)]; LW = 150
W = LW + sum(w for _, w in cols) + 10 * len(cols); sheet = Image.new("RGB", (W, 44 + len(rows) * (H + 10)), (24, 35, 59))
d = ImageDraw.Draw(sheet)
try: f = ImageFont.truetype(FONT, 18); fs = ImageFont.truetype(FONT, 14)
except Exception: f = fs = ImageFont.load_default()
x = LW
for name, w in cols: d.text((x + 6, 12), name, fill=(247, 240, 229), font=fs); x += w + 10
def onbg(im, box_w, box_h, bg=(64, 76, 104)):
    im = im.convert("RGBA"); im.thumbnail((box_w, box_h), Image.LANCZOS)
    c = Image.new("RGBA", (box_w, box_h), bg + (255,)); c.alpha_composite(im, ((box_w - im.width) // 2, (box_h - im.height) // 2)); return c.convert("RGB")
def icon(folder, c):
    n = "kaiju-adult-neutral" if c == "kaiju" else f"portrait-{c}-azure"
    p = os.path.join(folder, n + "@2x.png")
    return Image.open(p) if os.path.exists(p) else None
for i, (c, src, box, note) in enumerate(rows):
    y = 44 + i * (H + 10)
    d.text((10, y + 70), c.upper(), fill=(247, 240, 229), font=f); d.text((10, y + 96), note, fill=(22, 199, 217), font=fs)
    g = Image.open(os.path.join(U, src)).convert("RGB"); g = g.crop(box) if box else g
    x = LW; sheet.paste(onbg(g, 230, H, (200, 200, 200)), (x, y)); x += 240
    for folder in (OLD, NEW):
        im = icon(folder, c)
        if im is None: d.text((x + 30, y + 90), "none", fill=(255, 90, 95), font=f)
        else: sheet.paste(onbg(im, 150, H), (x, y))
        x += 160
    cut = os.path.join(BM, "bm-kaiju-neutral.png" if c == "kaiju" else f"bm-portrait-{c}-azure.png")
    sheet.paste(onbg(Image.open(cut), 230, H), (x, y))
sheet.save(sys.argv[1], quality=82, optimize=True); print(sys.argv[1], sheet.size, os.path.getsize(sys.argv[1]))
