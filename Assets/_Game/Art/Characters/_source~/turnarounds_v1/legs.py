"""Skeleton-parity numbers on front views: sole line, crotch height (first figure pixel above the soles in the centre column,
i.e. top of the gap between the legs) and the leg length in px. usage: python3 legs.py front.jpg ..."""
import sys, os
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from measure import fig
for p in sys.argv[1:]:
    f = fig(p); a = np.asarray(Image.open(p).convert("RGB")).astype(int)
    m = np.abs(a - np.array(f["bg"])).sum(2) > 40
    col = m[:, f["cx"] - 6:f["cx"] + 7].any(1); y = f["bottom"] - 5
    while y > f["top"] and not col[y]: y -= 1
    print("%-28s sole %4d  crotch %4d  leg %4d px  figure %4d px  leg/figure %.2f" % (os.path.basename(p), f["bottom"], y, f["bottom"] - y, f["h"], (f["bottom"] - y) / f["h"]))
