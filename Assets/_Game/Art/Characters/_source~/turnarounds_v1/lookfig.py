"""Look sheet of figure crops, all at one scale (feet aligned). usage: python3 lookfig.py out.jpg img[=label] ..."""
import sys, os
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from measure import fig
out = sys.argv[1]; items = [x.split("=") for x in sys.argv[2:]]; tiles = []
for it in items:
    f = fig(it[0]); im = Image.open(it[0]).convert("RGB")
    t = im.crop((max(0, f["cx"] - f["w"] // 2 - 8), 0, min(im.width, f["cx"] + f["w"] // 2 + 8), im.height)); tiles.append((t, it[-1]))
R = int(os.environ.get("ROWS", "2")); per = -(-len(tiles) // R)
rows = [tiles[i:i + per] for i in range(0, len(tiles), per)]
sc = min(960 / max(sum(t.width for t, _ in r) for r in rows), 1100 / (2048 * len(rows)))
rh = round(2048 * sc); sh = Image.new("RGB", (960, rh * len(rows)), "white")
for ri, r in enumerate(rows):
    x = 0
    for t, lb in r:
        t = t.resize((round(t.width * sc), round(t.height * sc)), Image.LANCZOS); sh.paste(t, (x, ri * rh))
        ImageDraw.Draw(sh).text((x + 3, ri * rh + 3), lb[:30], fill=(200, 0, 0)); x += t.width
q = 75
while True:
    sh.save(out, quality=q)
    if os.path.getsize(out) < 150_000 or q < 25: break
    q -= 8
print(out, sh.size, os.path.getsize(out))
