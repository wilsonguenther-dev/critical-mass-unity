"""Prepare a bible crop for the hosted image-to-3D page: tight crop, clean alpha mask, square pad, gentle upscale.
  python -I prep_input.py <guide.npz> <out.png> [--size 1024] [--deline]
--deline: soften the drawn ink outline (dark thin strokes inside the mask are replaced by the median of nearby
          non-ink colours) so the generator does not bake black paint into the texture.
Writes only pixels: no file names or text go into the image.
"""
import sys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage as ndi

src, out = sys.argv[1], sys.argv[2]
size = int(sys.argv[sys.argv.index("--size") + 1]) if "--size" in sys.argv else 1024
d = np.load(src)
m = d["mask"].astype(bool)
rgb = d["rgb"].astype(np.float32)
if rgb.max() <= 1.0:
    rgb = rgb * 255.0
m = ndi.binary_fill_holes(m)
lab, n = ndi.label(m)
if n > 1:  # keep the character only (drops fragments of neighbouring figures on the sheet)
    sizes = ndi.sum(m, lab, range(1, n + 1))
    m = lab == (1 + int(np.argmax(sizes)))
if "--deline" in sys.argv:
    lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
    ink = (lum < 70) & m
    # thin strokes only: ink that disappears under a 2-px opening is a line, big dark areas (hair, navy) stay
    thick = ndi.binary_opening(ink, iterations=2)
    line = ink & ~thick
    line = ndi.binary_dilation(line, iterations=1) & m
    keep = m & ~line
    idx = ndi.distance_transform_edt(~keep, return_distances=False, return_indices=True)
    rgb = rgb[idx[0], idx[1]]
    print("deline: replaced", int(line.sum()), "px of", int(m.sum()))
ys, xs = np.nonzero(m)
y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
crop = rgb[y0:y1, x0:x1]
a = m[y0:y1, x0:x1].astype(np.uint8) * 255
im = Image.fromarray(np.dstack([crop.clip(0, 255).astype(np.uint8), a]), "RGBA")
h, w = a.shape
side = int(max(h, w) * 1.12)
canvas = Image.new("RGBA", (side, side), (255, 255, 255, 0))
canvas.paste(im, ((side - w) // 2, (side - h) // 2))
canvas = canvas.resize((size, size), Image.LANCZOS)
al = canvas.getchannel("A").filter(ImageFilter.GaussianBlur(0.8))
canvas.putalpha(al)
canvas.save(out)
print("crop", (x0, y0, x1, y1), "->", canvas.size)
