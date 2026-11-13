"""Prepare TRELLIS.2 inputs (1024x1024 RGB, figure cropped with an 8 % margin, centred on the grey background), as warrior_v8 did.
usage: python3 prep_trellis.py <scratch>/gen <out trellis_input dir> <character> <view>=<genname> ...
e.g. prep_trellis.py gen trellis_input farmer body-three-quarter=farmer_tq body-front=farmer_front staff=farmer_prop"""
import sys, os
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from measure import fig
gen, outd, ch = sys.argv[1:4]; os.makedirs(f"{outd}/{ch}", exist_ok=True)
for kv in sys.argv[4:]:
    k, g = kv.split("="); p = f"{gen}/{g}_1.png"; f = fig(p); im = Image.open(p).convert("RGB")
    a = np.asarray(im).astype(int); bg = tuple(f["bg"])
    m = np.abs(a - np.array(bg)).sum(2) > 40; ys, xs = np.where(m)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    side = int(max(x1 - x0, y1 - y0) * 1.16)
    sq = Image.new("RGB", (side, side), bg)
    sq.paste(im.crop((x0, y0, x1 + 1, y1 + 1)), ((side - (x1 - x0)) // 2, (side - (y1 - y0)) // 2))
    sq.resize((1024, 1024), Image.LANCZOS).save(f"{outd}/{ch}/{k}.png", optimize=True)
    print(ch, k, "from", g, "fig", f["w"], "x", f["h"])
