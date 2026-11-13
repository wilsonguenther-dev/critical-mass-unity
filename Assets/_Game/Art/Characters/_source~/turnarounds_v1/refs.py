"""Build the reference images for turnarounds-v1 from the bible PDF.  usage: python3 refs.py <bible.pdf> <scratch>/refs
Writes the full artist pages as JPEG (as the Warrior v8 run did) and the per-view crops upscaled 3x."""
import sys, os, fitz
from PIL import Image
pdf, out = sys.argv[1], sys.argv[2]; os.makedirs(out, exist_ok=True)
d = fitz.open(pdf)
pages = {}
for pn in (6, 7, 8, 10, 11):
    pix = fitz.Pixmap(d, d[pn - 1].get_images(full=True)[0][0])
    if pix.n > 3: pix = fitz.Pixmap(fitz.csRGB, pix)
    im = Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
    pages[pn] = im; im.save(f"{out}/page{pn}.jpg", quality=88)
# crop boxes on the 1536x1024 artist images (x0, y0, x1, y1)
CROPS = {
    "farmer_front": (7, (0, 0, 262, 530)), "farmer_tq": (7, (330, 0, 660, 530)),
    "farmer_side": (7, (700, 0, 1005, 530)), "farmer_back": (7, (1005, 0, 1345, 530)),
    "farmer_staff": (7, (950, 540, 1245, 975)),
    "miner_front": (8, (10, 0, 392, 445)), "miner_tq": (8, (392, 0, 790, 445)),
    "miner_side": (8, (780, 0, 1080, 445)), "miner_back": (8, (1095, 0, 1350, 445)),
    "miner_pick": (8, (0, 760, 280, 1005)),
    "hatchling": (10, (160, 280, 445, 500)),
    "kaiju_front": (11, (10, 10, 378, 470)), "kaiju_tq": (11, (378, 10, 762, 470)),
    "kaiju_side": (11, (762, 10, 1140, 470)), "kaiju_back": (11, (1132, 10, 1532, 470)),
    "warrior_front": (6, (10, 60, 460, 545)),
}
for k, (pn, box) in CROPS.items():
    c = pages[pn].crop(box); c = c.resize((c.width * 3, c.height * 3), Image.LANCZOS)
    c.save(f"{out}/crop_{k}.jpg", quality=90)
print("refs", len(CROPS))
