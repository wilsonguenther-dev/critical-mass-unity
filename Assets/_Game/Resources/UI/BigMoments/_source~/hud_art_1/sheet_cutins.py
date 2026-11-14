# hud-art-1: cutins-before-after.jpg. BEFORE: the shipped in-game capture of a big moment (integration batch 5, 3024x1964)
# and the painted 16:9 Kaiju card the berserk cut-in pasted as a rectangle; AFTER: this branch's in-game captures (3024x1964)
# plus the 16 cut-outs on a checkerboard (true alpha).
# usage: python3 sheet_cutins.py CAPTURE_DIR
import sys, os, glob
from PIL import Image, ImageDraw, ImageFont
cap = sys.argv[1]
U = os.path.expanduser("~/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port")
OUT = U + "/hud-art-1/cutins-before-after.jpg"
B = os.path.expanduser("~/cm-wt-hud-art-1/Assets/_Game/Resources/UI/BigMoments")
font = ImageFont.truetype(os.path.expanduser("~/cm-wt-hud-art-1/Assets/_Game/Resources/UI/Fonts/NunitoSans-ExtraBold.ttf"), 20)
W = 1600; tw = 520; th = int(tw * 1964 / 3024)
def shot(p):
    im = Image.open(p).convert("RGB"); return im.resize((tw, int(tw * im.height / im.width)))
before = [shot(U + "/integration-batch-5/captures/16-big-moment@3024x1964.jpg")]
k = Image.open(os.path.expanduser("~/cm-wt-hud-art-1/Assets/_Game/Resources/Story/cutin_kaiju.jpg")).convert("RGB")
# how the old berserk card sat in the 660 px slot: a square crop of the painting (MomentArt.PaintedCrop), hard edges
w = k.height; x = int(max(0, min(k.width - w, 0.62 * k.width - w / 2))); sq = k.crop((x, 0, x + w, k.height)).resize((th, th))
before.append(sq)
after_names = ["20-berserk", "21-hatch-azure", "22-sides-crimson", "23-warrior-azure", "26-demolitions-crimson", "28-healer-crimson"]
after = [shot(os.path.join(cap, n + "@3024x1964.png")) for n in after_names if os.path.exists(os.path.join(cap, n + "@3024x1964.png"))]
rows_after = (len(after) + 2) // 3
H = 40 + th + 40 + rows_after * (th + 30) + 40 + 200 + 20
sheet = Image.new("RGB", (W, H), (22, 28, 44)); d = ImageDraw.Draw(sheet)
d.text((16, 8), "BEFORE: the shipped cut-in (left) and the painted Kaiju card that played as a hard-edged rectangle (right)", fill=(247, 240, 229), font=font)
sheet.paste(before[0], (16, 40)); sheet.paste(before[1], (16 + tw + 16, 40))
y = 40 + th + 12
d.text((16, y), "AFTER (in game, 3024x1964, mid-hold): berserk, hatch (Azure), picks a side (Crimson), class cards", fill=(247, 240, 229), font=font); y += 30
for i, im in enumerate(after):
    sheet.paste(im, (16 + (i % 3) * (tw + 16), y + (i // 3) * (th + 30)))
y += rows_after * (th + 30) + 4
d.text((16, y), "The 16 cut-outs (true alpha, shown over a checkerboard): 6 classes x 2 teams, Kaiju neutral / Azure / Crimson / berserk", fill=(247, 240, 229), font=font); y += 30
names = [f"bm-portrait-{c}-{t}" for c in ["warrior", "farmer", "miner", "demolitions", "builder", "healer"] for t in ["azure", "crimson"]] + [f"bm-kaiju-{s}" for s in ["neutral", "azure", "crimson", "berserk"]]
cell = 96
for i, n in enumerate(names):
    bg = Image.new("RGBA", (cell, cell), (70, 80, 100, 255)); dd = ImageDraw.Draw(bg)
    for yy in range(0, cell, 12):
        for xx in range(0, cell, 12):
            if (xx // 12 + yy // 12) % 2: dd.rectangle((xx, yy, xx + 11, yy + 11), fill=(110, 120, 140, 255))
    im = Image.open(os.path.join(B, n + ".png")).convert("RGBA").resize((cell, cell), Image.LANCZOS)
    bg.alpha_composite(im); sheet.paste(bg.convert("RGB"), (16 + i * (cell + 2), y))
sheet = sheet.crop((0, 0, W, y + cell + 12)); sheet.save(OUT, quality=78, optimize=True)
if os.path.getsize(OUT) > 450_000: sheet.save(OUT, quality=64, optimize=True)
print(OUT, sheet.size, os.path.getsize(OUT))
