#!/usr/bin/env python3
"""Contact sheets + edge checks for the Big Moments PNGs.  python3 contact_sheet.py <out-folder>
Navy panel #18233B: files as the game draws them untinted (white). White panel: files multiplied by the Azure team
colour #16C7D9 (white art is invisible on white). Edge check: semi-transparent pixels must carry the art colour, so the
composite on a dark and a light backdrop never goes darker (on light) or lighter (on dark) than both art and backdrop."""
import glob, json, os, sys
import numpy as np
from PIL import Image, ImageDraw
HERE = os.path.dirname(os.path.abspath(__file__)); SRC = os.path.normpath(os.path.join(HERE, ".."))
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
files = sorted(glob.glob(os.path.join(SRC, "bm-*.png")))
NAVY, WHITE, AZURE = (0x18, 0x23, 0x3B), (255, 255, 255), (0x16, 0xC7, 0xD9)
checks = {}
for f in files:
    im = np.asarray(Image.open(f).convert("RGBA")).astype(np.float32) / 255
    rgb, a = im[..., :3], im[..., 3:]
    soft = (a[..., 0] > 0.02) & (a[..., 0] < 0.98)
    res = {"size": Image.open(f).size, "kb": round(os.path.getsize(f) / 1024, 1),
           "transparent_black_px": int(((a[..., 0] == 0) & (rgb.max(-1) < 0.2)).sum()),
           "soft_px": int(soft.sum()), "nonwhite_hue_px": int((np.ptp(rgb, -1) > 1 / 255).sum())}
    for name, bg in (("dark", 0.0), ("light", 1.0)):
        comp = rgb * a + bg * (1 - a)
        # the composite of a straight-alpha pixel must lie between the art value and the backdrop
        lo, hi = np.minimum(rgb, bg), np.maximum(rgb, bg)
        res[f"halo_{name}_px"] = int((((comp < lo - 1 / 255) | (comp > hi + 1 / 255)).any(-1) & soft).sum())
    checks[os.path.basename(f)] = res
json.dump(checks, open(os.path.join(out, "checks.json"), "w"), indent=1)
cell, pad, cols = 300, 18, 4
rows = (len(files) + cols - 1) // cols
for panel, bgc, tint in (("navy", NAVY, None), ("white", WHITE, AZURE)):
    sheet = Image.new("RGB", (cols * (cell + pad) + pad, rows * (cell + pad + 22) + pad), bgc)
    dr = ImageDraw.Draw(sheet)
    for i, f in enumerate(files):
        im = Image.open(f).convert("RGBA")
        im.thumbnail((cell, cell), Image.LANCZOS)
        if tint:
            px = np.asarray(im).astype(np.float32)
            px[..., :3] *= np.array(tint) / 255.0
            im = Image.fromarray(px.astype(np.uint8), "RGBA")
        x = pad + (i % cols) * (cell + pad); y = pad + (i // cols) * (cell + pad + 22)
        dr.rectangle([x - 1, y - 1, x + cell, y + cell], outline=(90, 100, 120) if panel == "navy" else (200, 200, 200))
        sheet.paste(im, (x + (cell - im.width) // 2, y + (cell - im.height) // 2), im)
        dr.text((x, y + cell + 5), os.path.basename(f)[:-4], fill=(230, 230, 230) if panel == "navy" else (40, 40, 40))
    sheet.save(os.path.join(out, f"contact-{panel}.png"), optimize=True)
    sheet.convert("RGB").save(os.path.join(out, f"contact-{panel}.jpg"), quality=80)
for k, v in checks.items():
    print(k, v["size"], v["kb"], "halo", v["halo_dark_px"], v["halo_light_px"], "blackT", v["transparent_black_px"], "hue", v["nonwhite_hue_px"])
