"""Compose renders into review sheets (system python3 + Pillow + numpy).  python3 sheet.py <render_dir> <out_dir> <what>
what: turnaround | face | props | gameplay | all"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

R, OUT = sys.argv[1], sys.argv[2]
WHAT = sys.argv[3] if len(sys.argv) > 3 else "all"
PAPER = (247, 242, 233)
os.makedirs(OUT, exist_ok=True)


def font(sz):
    for f in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/Library/Fonts/Arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, sz)
    return ImageFont.load_default()


def on_paper(path, bg=PAPER):
    im = Image.open(path).convert("RGBA")
    p = Image.new("RGBA", im.size, bg + (255,))
    p.alpha_composite(im)
    return p.convert("RGB")


def save_jpg(im, path, cap=180_000, maxw=1400):
    if im.width > maxw:
        im = im.resize((maxw, int(im.height * maxw / im.width)), Image.LANCZOS)
    for q in (88, 80, 72, 64, 56, 48, 40):
        im.save(path, quality=q, optimize=True)
        if os.path.getsize(path) <= cap:
            break
    print(os.path.basename(path), im.size, os.path.getsize(path))


def row(paths, labels, H=520, title=None):
    ims = [on_paper(p) for p in paths]
    ims = [i.resize((int(i.width * H / i.height), H), Image.LANCZOS) for i in ims]
    top = 40 if title else 0
    W = sum(i.width for i in ims) + 20 * (len(ims) + 1)
    s = Image.new("RGB", (W, H + 50 + top), PAPER)
    d = ImageDraw.Draw(s)
    if title:
        d.text((20, 8), title, fill=(40, 40, 50), font=font(24))
    x = 20
    for im, lb in zip(ims, labels):
        s.paste(im, (x, top + 10))
        d.text((x, top + H + 16), lb, fill=(60, 60, 70), font=font(20))
        x += im.width + 20
    return s


if WHAT in ("turnaround", "all"):
    views = ["front", "three-quarter", "profile", "back"]
    save_jpg(row([f"{R}/ortho_{v}_azure.png" for v in views], views, 560,
                 "Healer version 2: winged battle healer turnaround (proposed class, Azure, KayKit Idle frame 1)"), f"{OUT}/turnaround.jpg")
if WHAT in ("face", "all"):
    save_jpg(on_paper(f"{R}/face_closeup.png"), f"{OUT}/face-closeup.jpg", maxw=1000)
if WHAT in ("props", "all"):
    save_jpg(row([f"{R}/props_azure.png", f"{R}/props_crimson.png"], ["Azure", "Crimson"], 420,
                 "Heal: Bloom Gauntlet (left in each)   Cleanse: Lantern (right)"), f"{OUT}/props.jpg")
