#!/usr/bin/env python3
"""HUD v2 icon review: bible crops beside our illustrated icons, measured colours, sharpness, icon sheet.

Usage: python3 tools/art/icon_review.py CAPTURE_DIR BIBLE_REF_DIR OUT_DIR
Writes sbs-icons-match.jpg, sbs-icons-player.jpg, sbs-icons-kaiju.jpg (bible left, ours right, 1400 px wide, under
180 KB), sheet-icons.jpg (every icon at HUD size and at 4x) and icons.json (per element: dominant colour of the bible
crop and of our icon, CIE dE2000 between them; crest edge transition at 720p / 1080p / 4K) into OUT_DIR.
Read-only on the project. Needs Pillow and numpy.
"""
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hud_review import de2000, sbs, save_jpeg  # noqa: E402

ICONS = os.path.join(ROOT, 'Assets/_Game/Resources/UI/Icons')
FONT = os.path.join(ROOT, 'Assets/_Game/Resources/UI/Fonts/NunitoSans-ExtraBold.ttf')
NAVY = (0x18, 0x23, 0x3B)
PANEL = (0x1B, 0x31, 0x4D)

# (label, bible file, crop box in that file, our icon)
GROUPS = {
    'match': [
        ('Azure crest', '15-hud-match.png', (12, 0, 52, 32), 'crest-azure'),
        ('crown medallion', '15-hud-match.png', (184, 20, 216, 50), 'crown'),
        ('Crimson crest', '15-hud-match.png', (350, 0, 390, 32), 'crest-crimson'),
        ('minimap banner', '15-hud-minimap.png', (10, 48, 44, 92), 'map-banner-azure'),
    ],
    'player': [
        ('portrait', '15-hud-player.png', (18, 0, 72, 46), 'portrait-warrior-azure'),
        ('heart', '15-hud-player.png', (100, 0, 122, 14), 'heart-azure'),
        ('hammer (attack)', '15-hud-player.png', (186, 0, 236, 44), 'attack-builder-azure'),
        ('wave (class ability)', '15-hud-player.png', (234, 0, 286, 44), 'ability-warrior-azure'),
        ('orb (carry biomass)', '15-hud-player.png', (284, 0, 334, 44), 'carry-biomass'),
    ],
    'kaiju': [
        ('egg', '10-egg.png', None, 'kaiju-egg'),
        ('hatchling', '10-hatchling.png', None, 'kaiju-hatchling'),
        ('azure adult', '10-azure-adult.png', None, 'kaiju-adult-azure'),
        ('crimson adult', '10-crimson-adult.png', None, 'kaiju-adult-crimson'),
        ('berserk adult', '10-berserk-adult.png', None, 'kaiju-adult-berserk'),
    ],
}


def dominant(im):
    """Median sRGB of the saturated, lit pixels (alpha > 0.5): the element's main colour."""
    a = np.asarray(im.convert('RGBA')).astype(float)
    rgb, al = a[..., :3] / 255.0, a[..., 3] / 255.0
    mx, mn = rgb.max(2), rgb.min(2)
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    m = (al > 0.5) & (s > 0.35) & (mx > 0.35)
    if m.sum() < 10:
        return None
    return tuple(int(round(v)) for v in np.median(rgb[m] * 255, axis=0))


def strip(items, ref_dir, side, tile=300):
    font = ImageFont.truetype(FONT, 22)
    out = Image.new('RGB', (tile * len(items), tile + 34), NAVY)
    d = ImageDraw.Draw(out)
    for i, (label, f, box, icon) in enumerate(items):
        if side == 'bible':
            im = Image.open(os.path.join(ref_dir, f)).convert('RGBA')
            if box:
                im = im.crop(box)
        else:
            im = Image.open(os.path.join(ICONS, icon + '@4x.png')).convert('RGBA')
        k = (tile - 24) / max(im.size)
        im = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.LANCZOS)
        bg = Image.new('RGBA', (tile, tile), PANEL + (255,))
        bg.alpha_composite(im, ((tile - im.width) // 2, (tile - im.height) // 2))
        out.paste(bg.convert('RGB'), (i * tile, 0))
        d.text((i * tile + 8, tile + 4), label, fill=(247, 240, 229), font=font)
    return out


def edge_transition(path, k, y, x0, x1):
    """Width in px (1080p units) of the crest's outer edge: scanning right from the world, the run of pixels between
    90% and 10% of the way from the world's luminance down to the navy outline's. Constant across 720p / 1080p / 4K for
    a texture drawn at or above its size; it grows with the scale for a stretched one."""
    im = Image.open(path).convert('L')
    row = [im.getpixel((int(x * k), int(y * k))) for x in np.arange(x0, x1, 1 / k)]
    world = float(np.median(row[:max(3, int(3 * k))]))
    navy = float(min(row))
    if world - navy < 30:
        return None
    i = next(j for j, v in enumerate(row) if v < navy + 0.1 * (world - navy))
    j = i
    while j > 0 and row[j - 1] < navy + 0.9 * (world - navy):
        j -= 1
    return round((i - j + 1) / k, 2)


def main():
    cap, ref, out = sys.argv[1:4]
    os.makedirs(out, exist_ok=True)
    m = {'elements': {}}
    for g, items in GROUPS.items():
        left, right = strip(items, ref, 'bible'), strip(items, ref, 'ours')
        m['sbs-icons-' + g] = sbs(left, right, os.path.join(out, f'sbs-icons-{g}.jpg'))
        for label, f, box, icon in items:
            b = Image.open(os.path.join(ref, f)).convert('RGBA')
            if box:
                b = b.crop(box)
            cb, co = dominant(b), dominant(Image.open(os.path.join(ICONS, icon + '@1x.png')))
            m['elements'][label] = {'icon': icon, 'bible': '#%02X%02X%02X' % cb if cb else None,
                                    'ours': '#%02X%02X%02X' % co if co else None,
                                    'dE2000': round(de2000(cb, co), 1) if cb and co else None}
    # the Azure crest's left edge, row through its middle (HudLayout: crest at x 590..660, y 0..70 at 1080p)
    m['crest_edge_transition_px_at_1080_units'] = {
        n: edge_transition(os.path.join(cap, n + '.png'), k, 35, 572, 604)
        for n, k in (('hud-720', 2 / 3), ('hud-1080', 1), ('hud-2160', 2)) if os.path.exists(os.path.join(cap, n + '.png'))}
    sheet = os.path.join(out, 'sheet-icons.jpg')
    subprocess.run([sys.executable, os.path.join(ROOT, 'Assets/_Game/Art/UI/Icons/_source~/build_icons.py'), '--sheet', sheet],
                   check=True, capture_output=True, env=dict(os.environ, PYTHONDONTWRITEBYTECODE='1'))
    im = Image.open(sheet)
    m['sheet-icons'] = save_jpeg(im.convert('RGB'), sheet, max_kb=900)
    with open(os.path.join(out, 'icons.json'), 'w') as f:
        json.dump(m, f, indent=1)
    print(json.dumps(m, indent=1))


if __name__ == '__main__':
    main()
