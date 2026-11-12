"""Bible-crop-beside-render sheets and one contact sheet (system Python 3 with Pillow; not Blender).

    python3 "Assets/_Game/Art/Kaiju/_source~/compose_kaiju.py" <render_dir> <ref_dir> <out_dir>

ref_dir is the bible crop folder (~/critical-mass/loop/parity/ref). For each render whose name is a crop name, the
render is scaled so its subject is as tall as the crop's subject and placed at the same spot on a bible-cream canvas.
"""
import glob
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kaiju_palette as PAL  # noqa: E402

REN, REF, OUT = sys.argv[1:4]
os.makedirs(OUT, exist_ok=True)
BG = tuple(int(PAL.BACKGROUND[i:i + 2], 16) for i in (1, 3, 5))
SCALE = 2


def subject_box(im):
    px = im.convert("RGB").load()
    w, h = im.size
    xs, ys = [], []
    for y in range(0, h):
        for x in range(0, w):
            r, g, b = px[x, y]
            if min(r, g, b) < 215:
                xs.append(x); ys.append(y)
    return (min(xs), min(ys), max(xs) + 1, max(ys) + 1) if xs else (0, 0, w, h)


def on_cream(rgba, size=None):
    bg = Image.new("RGB", rgba.size, BG)
    bg.paste(rgba, (0, 0), rgba)
    return bg


renders = sorted(glob.glob(os.path.join(REN, "*.png")))
pairs = []
for path in renders:
    name = os.path.splitext(os.path.basename(path))[0]
    refp = os.path.join(REF, name + ".png")
    if not os.path.exists(refp):
        continue
    ref = Image.open(refp).convert("RGB")
    ref = ref.resize((ref.width * SCALE, ref.height * SCALE), Image.LANCZOS)
    x0, y0, x1, y1 = subject_box(ref)
    ren = Image.open(path).convert("RGBA")
    bb = ren.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    ren = ren.crop(bb)
    k = (y1 - y0) / ren.height
    if ren.width * k > ref.width:
        k = ref.width / ren.width
    ren = ren.resize((max(1, int(ren.width * k)), max(1, int(ren.height * k))), Image.LANCZOS)
    canvas = Image.new("RGB", ref.size, BG)
    cx = (x0 + x1) // 2 - ren.width // 2
    cx = max(0, min(ref.width - ren.width, cx))
    canvas.paste(ren, (cx, y1 - ren.height), ren)
    pad = 36
    sheet = Image.new("RGB", (ref.width * 2 + 30, ref.height + pad), (255, 255, 255))
    sheet.paste(ref, (0, pad))
    sheet.paste(canvas, (ref.width + 30, pad))
    d = ImageDraw.Draw(sheet)
    d.text((6, 8), "bible " + name, fill=(20, 20, 20))
    d.text((ref.width + 36, 8), "kaiju-v1 render (Blender preview of CM/Toon)", fill=(20, 20, 20))
    sheet.save(os.path.join(OUT, "sbs-" + name + ".png"))
    pairs.append(sheet)

# contact sheet: every render on cream, labelled, 6 per row
thumbs = []
T = 300
for path in renders:
    name = os.path.splitext(os.path.basename(path))[0]
    ren = Image.open(path).convert("RGBA")
    bb = ren.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    ren = ren.crop(bb)
    k = (T - 30) / max(ren.width, ren.height)
    ren = ren.resize((max(1, int(ren.width * k)), max(1, int(ren.height * k))), Image.LANCZOS)
    t = Image.new("RGB", (T, T + 18), BG)
    t.paste(ren, ((T - ren.width) // 2, T - ren.height - 6), ren)
    ImageDraw.Draw(t).text((6, T + 3), name, fill=(20, 20, 20))
    thumbs.append(t)
cols = 6
rows = (len(thumbs) + cols - 1) // cols
cs = Image.new("RGB", (cols * T, rows * (T + 18)), (255, 255, 255))
for i, t in enumerate(thumbs):
    cs.paste(t, ((i % cols) * T, (i // cols) * (T + 18)))
cs.save(os.path.join(OUT, "contact-sheet.png"))
print("pairs", len(pairs), "renders", len(thumbs))
