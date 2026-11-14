# hud-art-1: icons-sheet.jpg (before = the shipped icons, after = the new set), on the HUD's panel colour, true pixel sizes.
import sys, os
from PIL import Image, ImageDraw, ImageFont
OLD = os.path.expanduser("~/critical-mass-unity/Assets/_Game/Resources/UI/Icons") if len(sys.argv) < 3 else sys.argv[2]
NEW = sys.argv[1]
OUT = os.path.expanduser("~/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/hud-art-1/icons-sheet.jpg")
C = ["warrior", "farmer", "miner", "demolitions", "builder", "healer"]
W = 1600; bg = (30, 38, 58); sheet = Image.new("RGB", (W, 1000), bg); d = ImageDraw.Draw(sheet)
try: font = ImageFont.truetype("/Users/wilsonguenther/cm-wt-hud-art-1/Assets/_Game/Resources/UI/Fonts/NunitoSans-ExtraBold.ttf", 18)
except Exception: font = ImageFont.load_default()
y = 12
def label(t):
    global y; d.text((16, y), t, fill=(247, 240, 229), font=font); y += 28
def row(names, folder, tier, gap=10, x0=16):
    global y; x = x0; h = 0
    for n in names:
        p = os.path.join(folder, f"{n}@{tier}x.png")
        if os.path.exists(p):
            im = Image.open(p).convert("RGBA"); sheet.paste(im, (x, y), im); h = max(h, im.height); x += im.width + gap
        else:
            x += 40
    y += h + 14
por = [f"portrait-{c}-{t}" for c in C for t in ("azure", "crimson")]
label("Class portraits @1x (HUD card, kill feed, death recap). BEFORE (Healer had none):"); row(por, OLD, 1)
label("AFTER, cut from the new cut-in characters:"); row(por, NEW, 1)
aa = [f"{k}-{c}-{t}" for c in C for t in ("azure", "crimson") for k in ("attack", "ability")]
label("Attack + ability buttons @1x. BEFORE:"); row(aa, OLD, 1, gap=6)
label("AFTER:"); row(aa, NEW, 1, gap=6)
kj = ["kaiju-egg", "kaiju-egg-cracking", "kaiju-hatchling", "kaiju-adult-neutral", "kaiju-adult-azure", "kaiju-adult-crimson", "kaiju-adult-berserk"]
label("Egg and Kaiju state badges @1x (rage panel). BEFORE / AFTER:"); yy = y; row(kj, OLD, 1); y = yy; row(kj, NEW, 1, x0=760)
label("Resources + power-up items @2x (coin, wood, water, food, haste, might, ward; BEFORE flat vector shapes) and @2x close-ups:")
row(["res-coin", "res-wood", "res-water", "res-food", "item-haste", "item-might", "item-ward", "portrait-warrior-azure", "portrait-healer-crimson", "ability-farmer-azure", "attack-miner-crimson",
     "kaiju-adult-berserk"], NEW, 2, gap=14)
sheet = sheet.crop((0, 0, W, y + 4)); sheet.save(OUT, quality=80, optimize=True); print(OUT, sheet.size, os.path.getsize(OUT))
