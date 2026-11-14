"""Cut the 2K turnaround views to transparent 1024 px PNGs for HY 3D (background estimated from the border)."""
import sys, os
from PIL import Image, ImageFilter, ImageDraw
import numpy as np
from scipy import ndimage
T, OUT = sys.argv[1], sys.argv[2]
def cut(src, dst):
    im = Image.open(src).convert('RGB'); a = np.asarray(im).astype(int)
    border = np.concatenate([a[:8].reshape(-1,3), a[-8:].reshape(-1,3), a[:, :8].reshape(-1,3), a[:, -8:].reshape(-1,3)])
    bg = np.median(border, axis=0); d = np.abs(a - bg).max(axis=2)
    lab, n = ndimage.label(d <= 14)
    edge = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    fg = ~np.isin(lab, list(edge)); fg = ndimage.binary_opening(fg, iterations=1)
    lab2, n2 = ndimage.label(fg); sizes = ndimage.sum(fg, lab2, range(1, n2 + 1))
    fg = np.isin(lab2, [i + 1 for i, s in enumerate(sizes) if s > 0.002 * fg.size])
    alpha = Image.fromarray((fg * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8))
    out = im.copy(); out.putalpha(alpha); out = out.resize((1024, 1024), Image.LANCZOS); out.save(dst)
    ys, xs = np.where(fg); return bg, (xs.min(), ys.min(), xs.max(), ys.max())
sheet = Image.new('RGB', (960, 3 * 320), (128, 128, 128)); dr = ImageDraw.Draw(sheet)
for r, c in enumerate(['farmer', 'miner', 'kaiju_adult']):
    os.makedirs(f'{OUT}/{c}', exist_ok=True)
    files = {v: f'{T}/{c}/{c}-{v}-2k.jpg' for v in ['front', 'back', 'side', 'three-quarter']}
    pf = f'{T}/{c}/{c}-prop-' 
    for f in os.listdir(f'{T}/{c}'):
        if '-prop-' in f: files['prop-' + f.split('-prop-')[1].replace('-2k.jpg', '')] = f'{T}/{c}/{f}'
    for v, src in files.items():
        bg, bb = cut(src, f'{OUT}/{c}/{v}.png'); print(c, v, 'bg', bg, 'bbox', bb)
    for k, v in enumerate(['front', 'side', 'three-quarter']):
        im = Image.open(f'{OUT}/{c}/{v}.png'); bgim = Image.new('RGB', im.size, (128, 128, 128)); bgim.paste(im, mask=im.split()[3])
        sheet.paste(bgim.resize((320, 320)), (k * 320, r * 320))
sheet.save(f'{OUT}/facing.jpg', quality=60)
