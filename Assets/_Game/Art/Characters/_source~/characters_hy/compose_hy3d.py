"""compare-3d.jpg and posed-3d.jpg from build_hy3d.py renders.  compose_hy3d.py <renders> <bible_ref_dir> <out_dir>"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

REN, REF, OUT = (os.path.abspath(os.path.expanduser(a)) for a in sys.argv[1:4])
CUR = os.path.abspath(os.path.expanduser(sys.argv[4])) if len(sys.argv) > 4 else REN   # renders of the in-game FBX (HY_SRC_FBX run)
F = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 18)
FB = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 21)
BG, INK = (205, 205, 205), (24, 35, 59)


def ld(name):
    return Image.open(os.path.join(CUR if name.endswith("_v7") else REN, name + ".png")).convert("RGB")


def trim(im, pad=12):
    """Crop the uniform background of a close render."""
    bg = im.getpixel((2, 2))
    diff = Image.new("RGB", im.size, bg)
    from PIL import ImageChops
    box = ImageChops.difference(im, diff).convert("L").point(lambda v: 255 if v > 12 else 0).getbbox()
    if not box:
        return im
    return im.crop((max(0, box[0] - pad), max(0, box[1] - pad), min(im.width, box[2] + pad), min(im.height, box[3] + pad)))


def row(tiles, labels, H, title):
    ims = []
    for t in tiles:
        if t.height != H:
            t = t.resize((max(1, round(t.width * H / t.height)), H), Image.NEAREST if t.height < H / 1.4 else Image.LANCZOS)
        ims.append(t)
    W = sum(i.width for i in ims) + 12 * (len(ims) + 1)
    out = Image.new("RGB", (W, H + 70), BG)
    d = ImageDraw.Draw(out)
    d.text((12, 8), title, fill=INK, font=FB)
    x = 12
    for im, lb in zip(ims, labels):
        out.paste(im, (x, 36))
        d.text((x, H + 42), lb, fill=INK, font=F)
        x += im.width + 12
    return out


def stack(rows, path, maxw=1400, kb=390):
    W = max(r.width for r in rows)
    out = Image.new("RGB", (W, sum(r.height for r in rows)), BG)
    y = 0
    for r in rows:
        out.paste(r, (0, y))
        y += r.height
    if out.width > maxw:
        out = out.resize((maxw, round(out.height * maxw / out.width)), Image.LANCZOS)
    q = 85
    while True:
        out.save(path, quality=q, optimize=True)
        if os.path.getsize(path) < kb * 1024 or q <= 40:
            break
        q -= 7
    print(path, out.size, os.path.getsize(path) // 1024, "KB q", q)


bible_tq = Image.open(os.path.join(REF, "06-three-quarter.png")).convert("RGB")
r1 = row([bible_tq, trim(ld("tq_v7")), trim(ld("tq_hero_vcol")), trim(ld("tq_hero_tex"))],
         ["BIBLE p06 three-quarter", "CURRENT in game (Warrior.fbx)", "NEW Hero, vertex colour (game)", "NEW Hero, generated texture"],
         420, "Warrior: bible / current / new (Tencent HY 3D Global), close three-quarter, Idle pose")
r2 = row([ld("game_v7"), ld("game_lod0_vcol"), ld("game_hero_vcol"), ld("game_hero_tex")],
         ["CURRENT Warrior.fbx", "NEW match mesh (<= 4.5k tris)", "NEW Hero", "NEW Hero, texture"],
         330, "Gameplay camera (55 deg pitch, 35 deg FOV, 23.4 u), true 1080p pixels shown 1.5x")
stack([r1, r2], os.path.join(OUT, "compare-3d.jpg"))

p1 = row([trim(ld(f"pose_{n}_hero")) for n in ("idle", "run", "attack")] + [trim(ld("rest_hero"))],
         ["Idle", "Running_A (30%)", "1H_Melee_Attack_Chop (45%)", "rest (KayKit T-pose)"], 380, "NEW Warrior Hero mesh on the 41-bone KayKit rig")
p2 = row([trim(ld(f"pose_{n}_lod0")) for n in ("idle", "run", "attack")],
         ["Idle", "Running_A", "1H_Melee_Attack_Chop"], 300, "NEW match-size mesh (LOD0), same clips")
stack([p1, p2], os.path.join(OUT, "posed-3d.jpg"))
