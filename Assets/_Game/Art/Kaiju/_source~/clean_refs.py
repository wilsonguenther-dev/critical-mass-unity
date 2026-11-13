"""Clean copies of the Kaiju bible crops for bible_fit/bible_guide.py (plain Python).

  python3 clean_refs.py <ref_dir> <bible_fit_tools_dir> <out_dir>

The p11 crops carry a sliver of the NEXT drawing (crystal tips of the view below) that does not touch the crop
border; bible_guide's keep_character keeps such interior pieces, so the guide took that sliver for the feet and every
height was off by about 18 %. This writes copies where everything except the largest connected piece (the creature,
with its ground shadow) is painted paper colour. The originals are never touched."""
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

REF, TOOLS, OUT = sys.argv[1:4]
sys.path.insert(0, TOOLS)
import bible_common as bc  # noqa: E402

os.makedirs(OUT, exist_ok=True)
for f in sorted(os.listdir(REF)):
    if not (f.startswith("10-") or f.startswith("11-")) or not f.endswith(".png"):
        continue
    rgb = np.asarray(Image.open(os.path.join(REF, f)).convert("RGB")).copy()
    paper = bc.estimate_paper(rgb)
    diff = np.abs(rgb.astype(np.float32) - paper).mean(axis=2)
    m = ndimage.binary_closing(diff > bc.THRESH, iterations=1)
    lab, n = ndimage.label(m)
    if n > 1:
        area = ndimage.sum(m, lab, range(1, n + 1))
        keep = ndimage.binary_dilation(lab == (1 + int(np.argmax(area))), iterations=3)
        rgb[~keep] = np.asarray(paper, dtype=np.uint8)
    Image.fromarray(rgb).save(os.path.join(OUT, f))
    print("CLEAN", f, "pieces", n)
