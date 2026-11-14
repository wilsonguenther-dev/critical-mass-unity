# hud-art-1 review strip: raw | cut-out over a game-like background | 200% edge crops. usage: review.py OUT.jpg name...
import sys
from PIL import Image
out, names = sys.argv[1], sys.argv[2:]
rows = []
for n in names:
    raw = Image.open(f"/tmp/hudart-gen/{n}.png").convert("RGB").resize((360, 360))
    cut = Image.open(f"/tmp/hudart-matte/{n}.png").convert("RGBA")
    bg = Image.new("RGBA", cut.size, (88, 140, 70, 255))
    for y in range(0, cut.size[1], 64):
        for x in range(0, cut.size[0], 64):
            if (x // 64 + y // 64) % 2: bg.paste((60, 98, 150, 255), (x, y, x + 64, y + 64))
    comp = Image.alpha_composite(bg, cut)
    small = comp.resize((360, 360)).convert("RGB")
    # 200% crops at the top of the silhouette and the right side
    a = cut.split()[3]; bb = a.getbbox()
    cx, cy = (bb[0] + bb[2]) // 2, bb[1] + 20
    c1 = comp.crop((cx - 90, cy - 40, cx + 90, cy + 140)).resize((360, 360), Image.NEAREST).convert("RGB")
    rx, ry = bb[2] - 60, (bb[1] + bb[3]) // 2
    c2 = comp.crop((rx - 90, ry - 90, rx + 90, ry + 90)).resize((360, 360), Image.NEAREST).convert("RGB")
    row = Image.new("RGB", (1440, 360)); [row.paste(im, (i * 360, 0)) for i, im in enumerate([raw, small, c1, c2])]
    rows.append(row)
sheet = Image.new("RGB", (1440, 360 * len(rows)))
for i, r in enumerate(rows): sheet.paste(r, (0, i * 360))
sheet.thumbnail((1400, 1400)); sheet.save(out, quality=62); print(out, sheet.size)
