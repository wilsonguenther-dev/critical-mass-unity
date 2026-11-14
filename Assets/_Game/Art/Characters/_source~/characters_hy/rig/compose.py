"""Contact sheets from the L2 renders (PIL). python3 compose.py <render_dir> <metrics_dir> <out_dir>"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

REN, MET, OUT = sys.argv[1:4]
os.makedirs(OUT, exist_ok=True)
try:
    FONT = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 13)
    FONTB = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 16)
except Exception:
    FONT = FONTB = ImageFont.load_default()
BG = (206, 206, 206)


def save(im, path, maxw=1400, kb=400):
    if im.width > maxw:
        im = im.resize((maxw, int(im.height * maxw / im.width)), Image.LANCZOS)
    q = 88
    while True:
        im.convert("RGB").save(path, "JPEG", quality=q, optimize=True)
        if os.path.getsize(path) <= kb * 1024 or q <= 45:
            break
        q -= 7


def open_fit(p, w, h):
    if not os.path.exists(p):
        im = Image.new("RGB", (w, h), (120, 60, 60))
        ImageDraw.Draw(im).text((5, 5), "missing", fill="white", font=FONT)
        return im
    im = Image.open(p).convert("RGB")
    im.thumbnail((w, h), Image.LANCZOS)
    c = Image.new("RGB", (w, h), (84, 84, 84))
    c.paste(im, ((w - im.width) // 2, (h - im.height) // 2))
    return c


def worst_sheet(cls, met, tagm="Hero"):
    clips = list(met[f"{tagm}_after"].keys())
    tw, th = 120, 140
    cols = 11
    rows = (len(clips) + cols - 1) // cols
    W = cols * (tw + 6) + 10
    Hh = 40 + rows * (2 * th + 46)
    im = Image.new("RGB", (W, Hh), BG)
    dr = ImageDraw.Draw(im)
    dr.text((8, 8), f"{cls}: worst frame of every clip, BEFORE (top, shipped weights) and AFTER (bottom, new rig); Hero mesh, close 3/4. "
            "Numbers: max edge stretch / inverted faces", fill=(20, 30, 50), font=FONTB)
    for i, c in enumerate(clips):
        x = 8 + (i % cols) * (tw + 6)
        y = 40 + (i // cols) * (2 * th + 46)
        b, a = met[f"{tagm}_before"][c], met[f"{tagm}_after"][c]
        im.paste(open_fit(f"{REN}/{cls}/worst_before_{c}.png", tw, th), (x, y + 14))
        im.paste(open_fit(f"{REN}/{cls}/worst_after_{c}.png", tw, th), (x, y + 14 + th + 16))
        dr.text((x, y), c[:19], fill=(0, 0, 0), font=FONT)
        dr.text((x, y + 14 + th), f"{b['max']:.2f} / {b['inv']}", fill=(150, 20, 20), font=FONT)
        dr.text((x, y + 30 + 2 * th), f"{a['max']:.2f} / {a['inv']}", fill=(10, 100, 30), font=FONT)
    save(im, f"{OUT}/worst-{cls.lower()}.jpg")


def strip_sheet(cls, strips):
    tw, th = 150, 180
    gw = 150
    W = 6 * (tw + 4) + 140
    rows = []
    for clip, frames in strips:
        for which in ("before", "after"):
            rows.append((clip, which, "strip", tw, th))
            rows.append((clip, which, "stripgame", gw, gw))
    Hh = 40 + sum(r[4] + 8 for r in rows)
    im = Image.new("RGB", (W, Hh), BG)
    dr = ImageDraw.Draw(im)
    dr.text((8, 8), f"{cls}: 6-frame strips of the two hardest clips, before and after (close 3/4 Hero; gameplay camera 55 deg LOD0, 2x)",
            fill=(20, 30, 50), font=FONTB)
    y = 40
    for clip, which, kind, w, h in rows:
        dr.text((8, y + h // 2 - 16), f"{clip[:17]}\n{which.upper()}\n{'close' if kind == 'strip' else 'game cam'}",
                fill=(150, 20, 20) if which == "before" else (10, 100, 30), font=FONT)
        for k in range(6):
            im.paste(open_fit(f"{REN}/{cls}/{kind}_{which}_{clip}_{k}.png", w, h), (136 + k * (tw + 4), y))
        y += h + 8
    save(im, f"{OUT}/strips-{cls.lower()}.jpg")


def lineup(classes, rep):
    tiles = []
    for cls in classes:
        for which in ("fallback", "before", "after"):
            p = f"{REN}/{cls}/lineup_{which}.png"
            if os.path.exists(p):
                tiles.append((cls, which, p))
    tw = 240
    W = len(tiles) * (tw + 6) + 10
    im = Image.new("RGB", (W, tw + 70), BG)
    dr = ImageDraw.Draw(im)
    dr.text((8, 6), "Gameplay camera (55 deg pitch, 35 deg FOV, 23.4 u), idle frame 0, true 1080p pixels: shipped/fallback vs new",
            fill=(20, 30, 50), font=FONTB)
    for i, (cls, which, p) in enumerate(tiles):
        x = 8 + i * (tw + 6)
        t = Image.open(p).convert("RGB")
        im.paste(t, (x, 30))
        px = rep.get(cls, {}).get("game_px", {}).get(which)
        dr.text((x, 34 + tw), f"{cls} {which}  {px} px", fill=(0, 0, 0), font=FONT)
    save(im, f"{OUT}/lineup-rig.jpg")


rep = json.load(open(f"{REN}/l2_report.json"))
plan = json.load(open(f"{MET}/plan.json"))
for cls, cfg in plan["chars"].items():
    if cls not in rep or "error" in rep[cls]:
        continue
    met = json.load(open(f"{MET}/metrics_{cls}.json"))
    worst_sheet(cls, met)
    strip_sheet(cls, cfg["renders"]["strips"])
lineup([c for c in plan["chars"] if c != "Kaiju"], rep)
print("COMPOSED", sorted(os.listdir(OUT)))
