"""Deliverable images for look-development A (plain Python, Pillow). compose.py <renders> <work> <out> <bible_ref_dir> <fit_tool_dir>"""
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

R, WORK, OUT, REF, FIT = (os.path.abspath(os.path.expanduser(a)) for a in sys.argv[1:6])
os.makedirs(OUT, exist_ok=True)
CREAM = (253, 246, 236)
GRASS = (163, 193, 80)


def font(sz=15):
    try:
        return ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", sz)
    except Exception:
        return ImageFont.load_default()


def over(png, bg=CREAM, shadow=True):
    im = Image.open(png).convert("RGBA")
    a = np.asarray(im)[..., 3]
    ys, xs = np.where(a > 128)
    im = im.crop((max(0, xs.min() - 6), max(0, ys.min() - 6), min(im.width, xs.max() + 7), min(im.height, ys.max() + 7)))
    canvas = Image.new("RGBA", (im.width + 40, im.height + 46), bg + (255,))
    if shadow:
        sh = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(sh)
        w = im.width
        d.ellipse((20 + w * 0.12, im.height - 2, 20 + w * 0.88, im.height + 34), fill=(60, 50, 60, 90))
        canvas = Image.alpha_composite(canvas, sh.filter(ImageFilter.GaussianBlur(9)))
    canvas.alpha_composite(im, (20, 14))
    return canvas.convert("RGB")


def fit_h(im, h):
    return im.resize((max(1, round(im.width * h / im.height)), h), Image.LANCZOS)


def panel(im, label, h=572, w=None, pad=34):
    im = fit_h(im, h - pad - 8)
    w = w or im.width + 16
    if im.width > w - 8:
        im = im.resize((w - 8, round(im.height * (w - 8) / im.width)), Image.LANCZOS)
    p = Image.new("RGB", (w, h), CREAM)
    p.paste(im, ((w - im.width) // 2, pad + (h - pad - 8 - im.height)))
    ImageDraw.Draw(p).text((14, 9), label, fill=(40, 40, 50), font=font(15))
    return p


def row(panels, name, maxw=1400, kb=178):
    W = sum(p.width for p in panels)
    H = max(p.height for p in panels)
    out = Image.new("RGB", (W, H), CREAM)
    x = 0
    for p in panels:
        out.paste(p, (x, 0))
        x += p.width
    if out.width > maxw:
        out = out.resize((maxw, round(out.height * maxw / out.width)), Image.LANCZOS)
    q = 88
    path = os.path.join(OUT, name)
    while True:
        out.save(path, quality=q, optimize=True)
        if os.path.getsize(path) < kb * 1024 or q <= 40:
            break
        q -= 6
    print("WROTE", name, out.size, os.path.getsize(path) // 1024, "KB")


def bible(name, box=None):
    im = Image.open(os.path.join(REF, f"06-{name}.png")).convert("RGB")
    if box:
        w, h = im.size
        im = im.crop((int(w * box[0]), int(h * box[1]), int(w * box[2]), int(h * box[3])))
    return im


def rp(n):
    return os.path.join(R, n + ".png")


iou = {}
for v in ("front", "three-quarter", "profile", "back"):
    try:
        pre = os.path.join(WORK, f"fit-{v}")
        subprocess.run([sys.executable, os.path.join(FIT, "fit_report.py"), os.path.join(FIT, "guides", f"guide-06-{v}.json"),
                        rp(f"new_{v}"), pre, "--name", f"lookdevA-{v}"], check=True, capture_output=True)
        j = json.load(open(pre + ".json"))
        iou[v] = {"iou": j.get("iou"), "todo": j.get("todo", j.get("to_do", []))[:4]}
    except Exception as ex:
        iou[v] = {"error": str(ex)[:200]}
try:
    pre = os.path.join(WORK, "fit-v7-front")
    subprocess.run([sys.executable, os.path.join(FIT, "fit_report.py"), os.path.join(FIT, "guides", "guide-06-front.json"),
                    rp("v7_front"), pre, "--name", "v7-front"], check=True, capture_output=True)
    iou["v7_front"] = {"iou": json.load(open(pre + ".json")).get("iou")}
except Exception as ex:
    iou["v7_front"] = {"error": str(ex)[:200]}
json.dump(iou, open(os.path.join(OUT, "iou.json"), "w"), indent=1)
print("IOU", {k: v.get("iou", v.get("error")) for k, v in iou.items()})


def tag(v):
    x = iou.get(v, {}).get("iou")
    return f"  (silhouette IoU {x:.2f})" if isinstance(x, (int, float)) else ""


for v in ("front", "three-quarter", "profile", "back"):
    row([panel(bible(v), f"bible p06: 06-{v}.png", w=514 if v == "front" else None),
         panel(over(rp(f"new_{v}")), f"look-dev A: {v}{tag(v)}", w=514 if v == "front" else None)], f"sbs-{v}.jpg")
FACE = (0.28, 0.0, 0.78, 0.50)
row([panel(bible("front", FACE), "bible p06: face (from 06-front.png)", 600, 660),
     panel(over(rp("new_face"), shadow=False), "look-dev A: face, thin line", 600, 660)], "sbs-face.jpg")
row([panel(bible("front"), "bible p06", 520, 450), panel(over(rp("v7_front")), "version 7 (vertex colour, 3 bands)", 520, 450),
     panel(over(rp("new_front")), "look-dev A (sculpt + painted texture)", 520, 450)], "strip-bible-v7-new.jpg")
row([panel(bible("front", FACE), "bible p06", 430, 440), panel(over(rp("v7_face"), shadow=False), "version 7", 430, 440),
     panel(over(rp("new_face"), shadow=False), "look-dev A", 430, 440)], "strip-face-bible-v7-new.jpg")
row([panel(over(rp("new_front_line_none")), "(a) no outline: contact lines painted in the texture + rim light", 560, 466),
     panel(over(rp("new_front")), "(b) thin hull on the smooth mesh (about 1.25 px in game)", 560, 466),
     panel(over(rp("new_front_line_thick")), "(c) the old thick hull (2.5 px in game)", 560, 466)], "line-treatments.jpg")


def game(n):
    im = Image.open(rp(n)).convert("RGBA")
    bg = Image.new("RGBA", im.size, GRASS + (255,))
    a = np.asarray(im)[..., 3]
    ys, xs = np.where(a > 128)
    sh = Image.new("RGBA", im.size, (0, 0, 0, 0))
    cx, by = (xs.min() + xs.max()) / 2, ys.max()
    ImageDraw.Draw(sh).ellipse((cx - 26, by - 12, cx + 26, by + 6), fill=(24, 35, 59, 100))
    bg = Image.alpha_composite(bg, sh.filter(ImageFilter.GaussianBlur(2)))
    bg.alpha_composite(im)
    hgt = int(ys.max() - ys.min() + 1)
    c = bg.convert("RGB").crop((int(cx - 75), int(ys.min() - 22), int(cx + 75), int(ys.max() + 22)))
    return c, hgt


tiles = []
for n, lab in (("game_v7", "version 7 (11.9k tris)"), ("game_new_none", "A LOD0 4.3k, line (a) none"), ("game_new_thin", "A LOD0 4.3k, line (b) thin"),
               ("game_new_thick", "A LOD0 4.3k, line (c) thick"), ("game_hero_thin", "A Hero 22k, line (b)")):
    c, hgt = game(n)
    p = Image.new("RGB", (c.width * 2 + 20, c.height * 2 + 60 + c.height), GRASS)
    p.paste(c, (10 + c.width // 2, 34))                              # true size (1080p pixels)
    p.paste(c.resize((c.width * 2, c.height * 2), Image.NEAREST), (10, 44 + c.height))
    ImageDraw.Draw(p).text((8, 8), lab, fill=(20, 30, 20), font=font(14))
    tiles.append(p)
    iou.setdefault("game_px", {})[n] = hgt
row(tiles, "game-84px.jpg")
row([panel(bible("front"), "bible p06", 520, 450), panel(over(rp("new_front")), "Hero mesh (close cameras)", 520, 450),
     panel(over(rp("lod0_front")), "LOD0, 4,348 triangles (in-match mesh), same texture", 520, 450)], "hero-vs-lod0-front.jpg")
row([panel(bible("front", FACE), "bible p06", 430, 440), panel(over(rp("new_face"), shadow=False), "Hero", 430, 440),
     panel(over(rp("lod0_face"), shadow=False), "LOD0 (never seen this close in play)", 430, 440)], "hero-vs-lod0-face.jpg")
row([panel(over(rp("new_front_crimson")), "Crimson through the team mask texture (R cloth, G glow)", 520, 470),
     panel(over(rp("new_front")), "Azure (as painted)", 520, 470)], "team-swap.jpg")
if os.path.exists(rp("sculpt_rest")):
    row([panel(over(rp("sculpt_rest"), shadow=False), "the sculpt: distance-field forms, marching cubes, rest pose, clay", 560, 620)], "sculpt-clay.jpg")
row([panel(over(rp(n)), lab, 480) for n, lab in (("anim_walk", "Walking_A"), ("anim_chop", "1H_Melee_Attack_Chop"), ("anim_run", "Running_A"))], "anim-poses.jpg")
for nm in ("Albedo", "Mask"):
    im = Image.open(os.path.join(WORK, f"Warrior_LD_{nm}_1024.png"))
    if nm == "Mask":
        im = Image.merge("RGB", im.split()[:3])
    im = im.convert("RGB").resize((1024, 1024), Image.LANCZOS)
    p = Image.new("RGB", (1024, 1060), CREAM)
    p.paste(im, (0, 36))
    ImageDraw.Draw(p).text((10, 8), "albedo 1024 (sRGB, Azure as painted)" if nm == "Albedo" else
                           "mask 1024 (linear): R team cloth, G team glow, B emission, A baked occlusion (not shown)", fill=(40, 40, 50), font=font(15))
    row([p], f"texture-{nm.lower()}.jpg")
json.dump(iou, open(os.path.join(OUT, "iou.json"), "w"), indent=1)

# one 960 px check sheet for the builder's own eyes (front, face strip, gameplay crops)
parts = [Image.open(os.path.join(OUT, n)) for n in ("hero-vs-lod0-front.jpg", "strip-face-bible-v7-new.jpg", "game-84px.jpg")]
parts = [p.resize((960, round(p.height * 960 / p.width)), Image.LANCZOS) for p in parts]
sheet = Image.new("RGB", (960, sum(p.height for p in parts)), CREAM)
y = 0
for p in parts:
    sheet.paste(p, (0, y))
    y += p.height
q = 70
while True:
    sheet.save(os.path.join(WORK, "check_sheet.jpg"), quality=q)
    if os.path.getsize(os.path.join(WORK, "check_sheet.jpg")) < 148 * 1024 or q <= 30:
        break
    q -= 6
print("CHECK", sheet.size, q)
