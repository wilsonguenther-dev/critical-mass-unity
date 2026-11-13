"""One downscaled look sheet (<=960 px wide, <150 KB) of several images with labels. usage: python3 look.py out.jpg H img[=label] ..."""
import sys
from PIL import Image, ImageDraw
out, H = sys.argv[1], int(sys.argv[2]); items = [x.split("=") for x in sys.argv[3:]]
ims = []
for it in items:
    im = Image.open(it[0]).convert("RGB"); im = im.resize((max(1, round(im.width * H / im.height)), H), Image.LANCZOS)
    ImageDraw.Draw(im).text((4, 4), it[1] if len(it) > 1 else it[0].split("/")[-1][:24], fill=(200, 0, 0)); ims.append(im)
rows, row, w = [], [], 0
for im in ims:
    if row and w + im.width > 960: rows.append(row); row, w = [], 0
    row.append(im); w += im.width
rows.append(row)
sh = Image.new("RGB", (960, H * len(rows)), "white")
for r, rw in enumerate(rows):
    x = 0
    for im in rw: sh.paste(im, (x, r * H)); x += im.width
q = 70
while True:
    sh.save(out, quality=q)
    import os
    if os.path.getsize(out) < 150_000 or q < 25: break
    q -= 8
print(out, sh.size, os.path.getsize(out))
