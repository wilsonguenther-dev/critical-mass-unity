"""Preview of a project_paint result (system python3: numpy, Pillow).

  python3 paint_preview.py <guide.json> <project-prefix> <out.jpg>

Left: the bible crop with every sampled vertex drawn as a dot in its guide colour (dot size = confidence).
Right: bloat (mesh outside the art, orange) and missing mass (art with no mesh, blue) over the agreed area (grey).
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw


def main():
    gpath, pref, out = sys.argv[1:4]
    gz = np.load(os.path.splitext(gpath)[0] + ".npz")
    pz = np.load(pref + ".npz")
    with open(pref + ".json") as fh:
        rep = json.load(fh)
    rgb = gz["rgb"]
    S = 2.0
    H, W = rgb.shape[:2]
    a = Image.fromarray((rgb * 0.45 + 140).astype(np.uint8)).resize((int(W * S), int(H * S)), Image.LANCZOS)
    d = ImageDraw.Draw(a)
    if "px" in pz:
        lin = pz["rgb_lin"]
        srgb = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(np.maximum(lin, 0), 1 / 2.4) - 0.055)
        for (x, y), c, cf in zip(pz["px"], srgb, pz["conf"]):
            r = 0.6 + 1.6 * cf
            d.ellipse([x * S - r, y * S - r, x * S + r, y * S + r], fill=tuple(int(v * 255) for v in c))
    A, B, pad = pz["bible"], pz["ours"], int(pz["pad"])
    heat = np.full(A.shape + (3,), 255, np.uint8)
    heat[A & B] = (214, 214, 214)
    heat[B & ~A] = (255, 140, 26)
    heat[A & ~B] = (47, 123, 255)
    ys, xs = np.where(A | B)
    heat = heat[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    b = Image.fromarray(heat)
    b = b.resize((int(b.width * a.height / b.height), a.height), Image.NEAREST)
    canvas = Image.new("RGB", (a.width + b.width + 6, a.height + 16), "white")
    canvas.paste(a, (0, 16))
    canvas.paste(b, (a.width + 6, 16))
    ImageDraw.Draw(canvas).text((3, 2), f"{rep['view']}: IoU {rep['iou']:.3f}  bloat {rep['bloat_share'] * 100:.1f}%  "
                                        f"missing {rep['missing_share'] * 100:.1f}%  (dots = per-vertex guide colour)",
                                fill=(0, 0, 0))
    if canvas.width > 960:
        canvas = canvas.resize((960, int(canvas.height * 960 / canvas.width)), Image.LANCZOS)
    canvas.save(out, quality=70)
    print(out, canvas.size)


if __name__ == "__main__":
    main()
