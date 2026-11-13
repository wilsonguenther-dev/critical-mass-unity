#!/usr/bin/env python3
"""Contact sheets for the intro film evidence: frames labelled with time, shot and mean picture luma.

  python3 tools/film/contact_sheet.py FRAMES_DIR OUT_PREFIX [--cols 5] [--per 20] [--width 1360]

Frames are files named t<seconds>_f<frame>.jpg. Every sheet is at most 1400 px wide and 400 KB (quality steps down).
"""
import glob, io, os, re, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(__file__))
import render_intro as R


def main():
    a = sys.argv[1:]
    src, prefix = a[0], a[1]
    cols = int(a[a.index('--cols') + 1]) if '--cols' in a else 5
    per = int(a[a.index('--per') + 1]) if '--per' in a else 20
    width = int(a[a.index('--width') + 1]) if '--width' in a else 1360
    files = sorted(glob.glob(os.path.join(src, 't*.jpg')))
    cw = width // cols
    font = ImageFont.truetype(R.FONT, 13)
    out = []
    for si in range(0, len(files), per):
        chunk = files[si:si + per]
        im0 = Image.open(chunk[0])
        ch = int(cw * im0.size[1] / im0.size[0])
        rows = (len(chunk) + cols - 1) // cols
        sheet = Image.new('RGB', (cw * cols, (ch + 18) * rows), (20, 20, 24))
        dr = ImageDraw.Draw(sheet)
        for k, f in enumerate(chunk):
            m = re.search(r't([\d.]+)_f(\d+)', os.path.basename(f))
            t = float(m.group(1))
            im = Image.open(f).convert('RGB')
            arr = np.asarray(im)
            H = arr.shape[0]
            bar = int(round(H * 80 / 1440))
            luma = float((arr[bar:H - bar] @ np.array([0.2126, 0.7152, 0.0722])).mean())
            th = im.resize((cw - 4, ch - 4), Image.LANCZOS)
            x, y = (k % cols) * cw, (k // cols) * (ch + 18)
            sheet.paste(th, (x + 2, y + 2))
            dr.text((x + 4, y + ch), f'{t:5.2f}s {R.shot_at(t)["id"]}  L{luma:.0f}', font=font, fill=(235, 225, 200))
        t0 = float(re.search(r't([\d.]+)', os.path.basename(chunk[0])).group(1))
        t1 = float(re.search(r't([\d.]+)', os.path.basename(chunk[-1])).group(1))
        path = f'{prefix}{si // per + 1:02d}_{t0:05.2f}-{t1:05.2f}s.jpg'
        q = 85
        while True:
            buf = io.BytesIO()
            sheet.save(buf, 'JPEG', quality=q)
            if buf.tell() <= 400_000 or q <= 40:
                break
            q -= 7
        open(path, 'wb').write(buf.getvalue())
        out.append(path)
    print('\n'.join(out))


if __name__ == '__main__':
    main()
