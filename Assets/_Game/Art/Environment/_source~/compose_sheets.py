"""Put the sky behind the RGBA renders, build bible-vs-render side-by-sides and one contact sheet (system python3 +
Pillow; no Blender).  python3 compose_sheets.py <render_dir> <out_dir> [iteration_label]

Bible crops are read (never written) from ~/critical-mass/loop/parity/ref (the committed 150 dpi crops).
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

REF = os.path.expanduser("~/critical-mass/loop/parity/ref")
src, out = sys.argv[1], sys.argv[2]
label = sys.argv[3] if len(sys.argv) > 3 else ""
os.makedirs(out, exist_ok=True)

SKY_TOP, SKY_BOT = (155, 198, 217), (226, 238, 242)   # palette 'sky' -> lighter haze

PAIRS = [
    # render, bible crop, optional crop box on the bible image, caption
    ("ingame_azure_base", "15-hud-full.png", (0, 150, 470, 470), "In-game camera at the Azure base vs bible p15 (left half)"),
    ("ingame_hill", "15-hud-full.png", (300, 120, 760, 400), "In-game camera at the hill vs bible p15 (hill)"),
    ("ingame_shrine", "14-shrine-azure.png", None, "In-game camera at the egg shrine vs bible p14 shrine"),
    ("p15_wide_ingame_angle", "15-hud-full.png", None, "Wide, in-game angle vs bible p15 gameplay mock"),
    ("p12_overview_north_up", "12-map-a-skybreak.png", None, "Whole map, north up vs bible p12 Skybreak Basin"),
    ("p03_hero_hill_shrine", "03-moodboard.png", (222, 0, 955, 455), "Hero: hill and shrine vs bible p03 arena hero"),
    ("kit_lineup", "14-wall-intact.png", None, "Kit lineup (states) vs bible p14 props"),
]


def sky_behind(im):
    w, h = im.size
    bg = Image.new("RGB", (w, h))
    px = bg.load()
    for y in range(h):
        t = y / max(1, h - 1)
        c = tuple(int(SKY_TOP[i] + (SKY_BOT[i] - SKY_TOP[i]) * t) for i in range(3))
        for x in range(w):
            px[x, y] = c
    bg.paste(im, (0, 0), im)
    return bg


def font(sz):
    for f in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        if os.path.exists(f):
            return ImageFont.truetype(f, sz)
    return ImageFont.load_default()


def side_by_side(name, ref, box, cap):
    rp = os.path.join(src, name + ".png")
    if not os.path.exists(rp):
        return None
    ours = sky_behind(Image.open(rp).convert("RGBA"))
    ours.save(os.path.join(out, name + ".png"))
    b = Image.open(os.path.join(REF, ref)).convert("RGB")
    if box:
        b = b.crop(box)
    H = 520
    b = b.resize((int(b.width * H / b.height), H), Image.LANCZOS)
    o = ours.resize((int(ours.width * H / ours.height), H), Image.LANCZOS)
    sheet = Image.new("RGB", (b.width + o.width + 30, H + 50), (24, 35, 59))
    sheet.paste(b, (10, 40))
    sheet.paste(o, (b.width + 20, 40))
    d = ImageDraw.Draw(sheet)
    d.text((10, 10), f"BIBLE  |  OURS {label}:  {cap}", fill=(247, 240, 229), font=font(20))
    p = os.path.join(out, "sbs_" + name + ".png")
    sheet.save(p)
    return sheet


sheets = [s for s in (side_by_side(*p) for p in PAIRS) if s is not None]
W = max(s.width for s in sheets)
contact = Image.new("RGB", (W, sum(s.height for s in sheets) + 10 * len(sheets)), (24, 35, 59))
y = 0
for s in sheets:
    contact.paste(s, (0, y))
    y += s.height + 10
scale = min(1.0, 2400 / W)
contact = contact.resize((int(contact.width * scale), int(contact.height * scale)), Image.LANCZOS)
contact.save(os.path.join(out, "contact_sheet.png"))
print("SHEETS", len(sheets), os.path.join(out, "contact_sheet.png"))
