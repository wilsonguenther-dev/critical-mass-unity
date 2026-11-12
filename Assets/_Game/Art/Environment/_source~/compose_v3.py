"""Kit v3 deliverable images (system python3 + Pillow; no Blender).

    python3 compose_v3.py <render_dir> <out_dir>

Gameplay renders come in at 3840x2160 (render_v3.py --scale 2): each becomes a 1920x1080 JPEG (the 1080p frame)
and, for the field shots, a centre crop at 2x (`-2x.jpg`). Side-by-sides put the bible crop LEFT and ours RIGHT,
1400 px wide at most. Every JPEG is kept under 180 KB. Bible crops are read from ~/critical-mass/loop/parity/ref.
"""
import io
import os
import sys

from PIL import Image, ImageDraw, ImageFont

REF = os.path.expanduser("~/critical-mass/loop/parity/ref")
src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)


def font(sz):
    for f in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        if os.path.exists(f):
            return ImageFont.truetype(f, sz)
    return ImageFont.load_default()


def save(im, path, cap=180_000):
    for q in (88, 82, 76, 70, 64, 58, 52, 46, 40):
        b = io.BytesIO()
        im.save(b, "JPEG", quality=q, optimize=True)
        if b.tell() < cap:
            break
    with open(path, "wb") as f:
        f.write(b.getvalue())
    print(os.path.basename(path), im.size, b.tell() // 1024, "KB q", q)


def load(name):
    return Image.open(os.path.join(src, name + ".png")).convert("RGB")


def bible(fname, box=None):
    im = Image.open(os.path.join(REF, fname)).convert("RGB")
    return im.crop(box) if box else im


def sbs(left, right, cap, name, W=1400):
    H = 560
    l2 = left.resize((max(1, int(left.width * H / left.height)), H), Image.LANCZOS)
    r2 = right.resize((max(1, int(right.width * H / right.height)), H), Image.LANCZOS)
    tot = l2.width + r2.width + 12
    if tot > W:
        s = (W - 14) / (tot - 12)
        l2 = l2.resize((int(l2.width * s), int(H * s)), Image.LANCZOS)
        r2 = r2.resize((int(r2.width * s), int(H * s)), Image.LANCZOS)
    hh = l2.height
    im = Image.new("RGB", (l2.width + r2.width + 12, hh + 34), (24, 35, 59))
    im.paste(l2, (0, 34))
    im.paste(r2, (l2.width + 12, 34))
    d = ImageDraw.Draw(im)
    d.text((8, 8), "BIBLE  |  " + cap + "  |  OURS (kit v3)", fill=(247, 240, 229), font=font(17))
    save(im, os.path.join(out, "sbs-%s.jpg" % name))


def strip(fnames, h=300):
    ims = [bible(f) for f in fnames]
    ims = [i.resize((int(i.width * h / i.height), h), Image.LANCZOS) for i in ims]
    im = Image.new("RGB", (sum(i.width for i in ims) + 8 * (len(ims) - 1), h), (255, 255, 255))
    x = 0
    for i in ims:
        im.paste(i, (x, 0))
        x += i.width + 8
    return im


R = {}
for n in ("key_map_hill_gameplay", "field_ledge_path_bridge", "rim_waterfall", "route_crystals", "lineup_1_edges_cliffs",
          "lineup_2_foliage_rocks_crystals", "lineup_3_water_bridge_ground_sky"):
    big = load(n)
    R[n] = big.resize((1920, 1080), Image.LANCZOS)
    save(R[n], os.path.join(out, n + ".jpg"))
    if not n.startswith("lineup"):
        w, h = big.size
        save(big.crop((w // 4, h // 4, w // 4 + 1920, h // 4 + 1080)), os.path.join(out, n + "-2x.jpg"))
ov = load("p12_overview_north_up")
save(ov, os.path.join(out, "p12_overview_north_up.jpg"))

sbs(bible("15-hud-full.png", (0, 40, 934, 420)), R["key_map_hill_gameplay"], "p15 gameplay mock vs the hill and its surroundings at the gameplay camera", "map-hill")
sbs(bible("12-map-a-skybreak.png"), ov, "p12 Skybreak Basin vs our map, orthographic north-up", "p12-overview")
sbs(bible("15-hud-full.png", (0, 40, 934, 420)), R["field_ledge_path_bridge"], "p15 vs field: ledge, path, bridge", "field")
sbs(bible("12-map-a-skybreak.png", (0, 330, 220, 520)), R["rim_waterfall"], "p12 rim waterfalls vs our rim + waterfall", "rim-waterfall")
sbs(bible("12-map-a-skybreak.png", (20, 120, 260, 300)), R["route_crystals"], "p12 route crystals vs ours", "route-crystals")
sbs(strip(["14-tiles.png", "14-bridge.png", "14-ramp.png"]), R["lineup_3_water_bridge_ground_sky"], "p14 tiles, bridge, ramp vs kit lineup 3", "kit-tiles-bridge")
sbs(strip(["14-wall-intact.png", "14-wall-cracked.png", "14-wall-destroyed.png"]), R["lineup_1_edges_cliffs"], "p14 wall states (not this kit) vs our cliff + lip lineup", "kit-cliffs")
sbs(bible("12-map-a-skybreak.png", (60, 400, 330, 600)), R["lineup_2_foliage_rocks_crystals"], "p12 trees, rocks, crystals vs kit lineup 2", "kit-foliage")
