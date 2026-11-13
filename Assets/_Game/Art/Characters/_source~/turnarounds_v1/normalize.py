"""Put every view of a class at the front view's scale: rescale side, three-quarter and back so the figure height equals the
front's and the soles sit on the front's sole line, centred. Classes only (the Kaiju's height changes with the angle).
usage: python3 normalize.py turnaround/  -> rewrites the class view JPEGs in place and prints the before/after heights."""
import sys, os
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from measure import fig
A = sys.argv[1]
for k in ("farmer", "miner", "builder", "healer", "demolitions"):
    fp = f"{A}/{k}/{k}-front-2k.jpg"; f0 = fig(fp); hs = []
    for v in ("three-quarter", "side", "back"):
        p = f"{A}/{k}/{k}-{v}-2k.jpg"; f = fig(p); hs.append(f["h"]); im = Image.open(p).convert("RGB")
        s = f0["h"] / f["h"]
        if abs(s - 1) < 0.004: continue
        big = im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS)
        out = Image.new("RGB", im.size, tuple(f["bg"]))
        dx = round(1024 - f["cx"] * s); dy = round(f0["bottom"] - f["bottom"] * s)
        out.paste(big, (dx, dy)); out.save(p, quality=88)
    after = [fig(f"{A}/{k}/{k}-{v}-2k.jpg")["h"] for v in ("three-quarter", "side", "back")]
    print(k, "front", f0["h"], "others before", hs, "after", after)
