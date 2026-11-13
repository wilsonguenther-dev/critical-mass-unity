#!/usr/bin/env python3
"""Critical Mass logo (bible p04): primary lockup and compact badge, rebuilt as vector art (v2: carved stone letters).

The wordmark is the Bungee typeface (SIL OFL 1.1, Resources/UI/Fonts/Bungee-OFL.txt) converted to outline paths with
fontTools, each letter tilted and lifted for the bouncy baseline, then stacked as flat sticker layers: Void Navy outer
outline, Warm Cream inner rim, a darker bevel offset under the face, and the flat face (CRITICAL azure, MASS coral over
orange-red in two hard bands). Behind it: a dark faceted stone egg with teal (left) and red (right) seams, floating mossy
rocks, blue crystals left, a corruption plume right and a waterfall under the left rocks. No gradients, no reference
letterforms. Rebuild:

    python3 build_logo.py            # writes the SVGs here and the PNGs into Assets/_Game/Resources/UI/Logo/

Needs fontTools and rsvg-convert (Homebrew librsvg).
"""
import math
import os
import subprocess
import sys

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import stone  # noqa: E402  (v2 carved-stone letters and faceted egg, shapely)

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
FONT = os.path.join(GAME, 'Resources', 'UI', 'Fonts', 'Bungee-Regular.ttf')
OUT_PNG = os.path.join(GAME, 'Resources', 'UI', 'Logo')

# Bible p05 tokens (CMTokens.cs) plus logo-only shades derived from them.
NAVY = '#18233B'
CREAM = '#F7F0E5'
AZURE = '#16C7D9'
AZURE_FACE = '#3FD6EE'      # CRITICAL face, Azure lifted one band
AZURE_BEVEL = '#0E7F98'     # darker teal bevel
CORAL = '#FF7A57'           # MASS upper band
ORANGE_RED = '#F2502F'      # MASS lower band
MASS_BEVEL = '#A8321F'
STONE = '#6F7282'
STONE_LIGHT = '#9C9480'
STONE_DARK = '#4A4E5E'
EGG = '#3A4052'
EGG_DARK = '#2A2F3F'
EGG_SEAM_TEAL = '#4FDDC3'
EGG_SEAM_RED = '#FF5A5F'
MOSS = '#8DBF3A'
MOSS_LIGHT = '#B7F238'
CRYSTAL = '#16C7D9'
CRYSTAL_LIGHT = '#9BEFFA'
CORRUPTION = '#8E3FD1'
CORRUPTION_HOT = '#D63C8E'
WATER = '#6FC0EC'
FOAM = '#C4E4F4'

font = TTFont(FONT)
glyphs = font.getGlyphSet()
cmap = font.getBestCmap()
UPM = font['head'].unitsPerEm
CAP = font['OS/2'].sCapHeight or 700


def glyph_path(ch, x, y, size, tilt_deg):
    """SVG path of one letter: baseline-left at (x, y) in SVG units, cap height = size, tilted about its centre."""
    name = cmap[ord(ch)]
    g = glyphs[name]
    s = size / CAP
    adv = g.width * s
    cx, cy = x + adv / 2, y - size / 2
    a = math.radians(tilt_deg)
    ca, sa = math.cos(a), math.sin(a)
    # font units -> svg: (fx, fy) -> (x + fx*s, y - fy*s), then rotate about (cx, cy)
    pen = SVGPathPen(glyphs)
    # affine: X = ca*(x+fx*s-cx) - sa*(y-fy*s-cy) + cx ; Y = sa*(x+fx*s-cx) + ca*(y-fy*s-cy) + cy
    xx, xy = ca * s, sa * s          # dX/dfx, dX/dfy
    yx, yy = sa * s, -ca * s         # dY/dfx, dY/dfy
    dx = ca * (x - cx) - sa * (y - cy) + cx
    dy = sa * (x - cx) + ca * (y - cy) + cy
    tp = TransformPen(pen, (xx, yx, xy, yy, dx, dy))
    g.draw(tp)
    return pen.getCommands(), adv


def word(text, x0, base, size, tilts, lifts, tracking):
    paths, x = [], x0
    for i, ch in enumerate(text):
        p, adv = glyph_path(ch, x, base - lifts[i % len(lifts)], size, tilts[i % len(tilts)])
        paths.append(p)
        x += adv + tracking
    return ' '.join(paths), x - tracking - x0


def measure(text, size, tracking):
    s = size / CAP
    return sum(glyphs[cmap[ord(c)]].width * s for c in text) + tracking * (len(text) - 1)


def fit(text, width):
    """Cap height that makes text span width (tracking 2% of the size, as wordmark_layers uses)."""
    return width / (measure(text, 1.0, 0.0) + 0.02 * (len(text) - 1))


def poly(pts, fill, stroke=NAVY, sw=7, extra=''):
    d = 'M' + ' L'.join(f'{x:.1f},{y:.1f}' for x, y in pts) + ' Z'
    st = f' stroke="{stroke}" stroke-width="{sw}" stroke-linejoin="round"' if stroke else ''
    return f'<path d="{d}" fill="{fill}"{st} {extra}/>'


def rock(cx, cy, w, h, moss=True, seed=0):
    """Floating rock: light top face, stone body, dark underside wedge, moss cap; navy outline."""
    t = h * 0.32
    top = [(cx - w / 2, cy), (cx - w * 0.3, cy - t), (cx + w * 0.35, cy - t * 0.9), (cx + w / 2, cy - t * 0.1)]
    body = [(cx - w / 2, cy), (cx + w / 2, cy - t * 0.1), (cx + w * 0.42, cy + h * 0.45), (cx + w * 0.1, cy + h * 0.75),
            (cx - w * 0.2, cy + h * 0.6), (cx - w * 0.45, cy + h * 0.35)]
    out = [poly(body + [], STONE, sw=7), poly([(cx + w * 0.05, cy + h * 0.05), (cx + w * 0.42, cy + h * 0.45),
                                                 (cx + w * 0.1, cy + h * 0.75)], STONE_DARK, stroke=None),
           poly(top, STONE_LIGHT, sw=6)]
    if moss:
        m = [(cx - w * 0.48, cy + 2), (cx - w * 0.32, cy - t - 6), (cx - w * 0.05, cy - t - 12), (cx + w * 0.25, cy - t - 8),
             (cx + w * 0.46, cy - t * 0.2), (cx + w * 0.3, cy + 6), (cx + w * 0.12, cy - 2), (cx - w * 0.1, cy + 8),
             (cx - w * 0.3, cy + 2)]
        out.append(poly(m, MOSS, sw=6))
        out.append(poly([(cx - w * 0.25, cy - t - 2), (cx - w * 0.05, cy - t - 9), (cx + w * 0.15, cy - t - 5),
                         (cx - w * 0.05, cy - t + 2)], MOSS_LIGHT, stroke=None))
    return ''.join(out)


def chip(cx, cy, r, rot, fill=STONE):
    pts = [(cx + r * math.cos(math.radians(rot + a)), cy + r * math.sin(math.radians(rot + a))) for a in (0, 80, 170, 250)]
    return poly(pts, fill, sw=5)


def egg(cx, cy, rx, ry):
    """Faceted stone egg: an outline polygon, cell seams in teal (left half) and red (right half)."""
    n = 18
    outline = []
    for i in range(n):
        a = 2 * math.pi * i / n
        k = 1.0 - 0.18 * max(0.0, -math.sin(a))   # narrower top
        outline.append((cx + rx * k * math.cos(a), cy + ry * math.sin(a) * (1.0 if math.sin(a) > 0 else 1.08)))
    out = [poly(outline, EGG, sw=10)]
    # facet cells: a stretched hex grid clipped by the egg
    cells = []
    for row in range(-3, 4):
        for col in range(-3, 4):
            px = cx + col * rx * 0.52 + (rx * 0.26 if row % 2 else 0)
            py = cy + row * ry * 0.36
            if ((px - cx) / rx) ** 2 + ((py - cy) / ry) ** 2 >1.3:
                continue
            hexp = []
            for k in range(6):
                vx = px + rx * 0.30 * math.cos(math.radians(60 * k + 30))
                vy = py + ry * 0.24 * math.sin(math.radians(60 * k + 30))
                key = (round(vx / 7), round(vy / 7))
                h = (key[0] * 73856093 ^ key[1] * 19349663) & 0xffff
                hexp.append((vx + ((h & 255) / 255 - 0.5) * rx * 0.16, vy + ((h >> 8) / 255 - 0.5) * ry * 0.12))
            seam = EGG_SEAM_TEAL if px < cx else EGG_SEAM_RED
            cells.append(poly(hexp, EGG_DARK if (row + col) % 3 == 0 else EGG, stroke=seam, sw=5))
    out.append(f'<clipPath id="eggclip"><path d="M' + ' L'.join(f'{x:.1f},{y:.1f}' for x, y in outline) + ' Z"/></clipPath>')
    out.append('<g clip-path="url(#eggclip)">' + ''.join(cells) + '</g>')
    out.append(poly(outline, 'none', sw=10))
    return ''.join(out)


def crystals(x, y, s, flip=False):
    out = []
    for dx, h, w, lean in ((0, 0.9, 0.42, -6), (0.3, 0.62, 0.34, 12), (-0.28, 0.5, 0.3, -16)):
        bx = x + dx * s
        tip = (bx + math.sin(math.radians(lean)) * h * s, y - h * s)
        pts = [(bx - w * s / 2, y), tip, (bx + w * s / 2, y)]
        out.append(poly(pts, CRYSTAL, sw=6))
        out.append(poly([(bx - w * s * 0.1, y), tip, (bx + w * s * 0.35, y)], CRYSTAL_LIGHT, stroke=None))
    return ''.join(out)


def plume(x, y, s):
    """Corruption plume: three curling violet/magenta tongues rising from the right rocks."""
    out = []
    for i, (ox, h, c) in enumerate(((0, 1.0, CORRUPTION), (0.25, 0.75, CORRUPTION_HOT), (-0.2, 0.6, CORRUPTION_HOT))):
        bx = x + ox * s
        d = (f'M{bx - 0.12 * s:.1f},{y:.1f} C{bx - 0.3 * s:.1f},{y - 0.4 * h * s:.1f} {bx + 0.25 * s:.1f},{y - 0.6 * h * s:.1f} '
             f'{bx + 0.05 * s:.1f},{y - h * s:.1f} C{bx + 0.35 * s:.1f},{y - 0.7 * h * s:.1f} {bx + 0.15 * s:.1f},{y - 0.3 * h * s:.1f} '
             f'{bx + 0.14 * s:.1f},{y:.1f} Z')
        out.append(f'<path d="{d}" fill="{c}" stroke="{NAVY}" stroke-width="6" stroke-linejoin="round"/>')
    return ''.join(out)


def waterfall(x, y, w, h):
    return (poly([(x, y), (x + w, y), (x + w * 0.9, y + h), (x + w * 0.1, y + h)], WATER, sw=6)
            + poly([(x + w * 0.25, y + 4), (x + w * 0.4, y + 4), (x + w * 0.35, y + h - 4), (x + w * 0.22, y + h - 4)], FOAM, stroke=None)
            + poly([(x - 8, y + h - 6), (x + w + 8, y + h - 6), (x + w + 2, y + h + 12), (x - 2, y + h + 12)], FOAM, sw=5))


def wordmark_layers(top, bottom, cx, top_base, top_size, bot_base, bot_size):
    """The two-word wordmark as carved stone (v2): chiselled letter faces, a lighter top plane, a darker lower bevel,
    a deep extruded side under each letter, faceted planes, cracks and moss caps; Warm Cream inner rim and a thick Void
    Navy outer outline around the whole block (brief: double outline)."""
    trk_t, trk_b = top_size * 0.02, bot_size * 0.03
    wt = measure(top, top_size, trk_t)
    wb = measure(bottom, bot_size, trk_b)
    tilts_t = (-4, 3, -2, 4, -3, 2, -4, 3)
    lifts_t = (0, 8, 2, 10, 0, 7, 3, 9)
    tilts_b = (3, -3, 4, -2)
    lifts_b = (6, 0, 9, 2)
    lt = stone.word_geoms(glyphs, cmap, CAP, top, cx - wt / 2, top_base, top_size, tilts_t, lifts_t, trk_t, seed=11)
    lb = stone.word_geoms(glyphs, cmap, CAP, bottom, cx - wb / 2, bot_base, bot_size, tilts_b, lifts_b, trk_b, seed=29)
    parts = stone.carve(lt + lb, [top_size] * len(lt) + [bot_size] * len(lb),
                        [stone.CRITICAL] * len(lt) + [stone.MASS] * len(lb), seed=5)
    return parts, wt, wb


def primary():
    W, H = 1200, 940
    cx = W / 2
    top = fit('CRITICAL', W * 0.80)
    bot = min(fit('MASS', W * 0.62), top * 1.45)
    parts = [stone.egg(cx, 300, 185, 225)]
    parts += [rock(300, 300, 120, 150), rock(905, 285, 130, 160), rock(150, 560, 150, 150), rock(1050, 590, 150, 140)]
    parts += [chip(250, 140, 22, 10), chip(960, 110, 24, 40), chip(430, 70, 18, 70), chip(800, 60, 20, 5, STONE_LIGHT),
              chip(80, 430, 18, 30), chip(1130, 450, 20, 60), chip(200, 840, 22, 15), chip(1000, 850, 20, 50),
              # v2: denser debris field like p04
              chip(330, 120, 26, 25), chip(870, 150, 28, 65), chip(380, 210, 16, 50, STONE_LIGHT), chip(820, 230, 18, 10),
              chip(60, 300, 22, 80), chip(1150, 300, 24, 20), chip(260, 420, 14, 35, STONE_LIGHT), chip(950, 420, 16, 75),
              chip(120, 760, 20, 40), chip(1080, 740, 22, 5), chip(330, 900, 18, 60), chip(880, 905, 20, 30),
              chip(520, 40, 14, 15, STONE_LIGHT), chip(690, 30, 16, 55), chip(1170, 620, 16, 45), chip(30, 640, 18, 20)]
    parts.append(crystals(150, 520, 230))
    parts.append(plume(1040, 575, 300))
    parts.append(waterfall(100, 610, 80, 250))
    wm, wt, wb = wordmark_layers('CRITICAL', 'MASS', cx, 470 + top, top, 470 + top + 40 + bot, bot)
    # base rocks under the wordmark
    parts += [rock(cx - 170, 800, 170, 120), rock(cx + 10, 815, 210, 130), rock(cx + 200, 800, 160, 115)]
    parts.append(wm)
    return W, H, parts


def badge():
    W, H = 800, 1000
    cx = W / 2
    top = fit('CRITICAL', W * 0.86)
    bot = min(fit('MASS', W * 0.74), top * 1.6)
    parts = [stone.egg(cx, 290, 175, 210), rock(150, 230, 110, 160, moss=False), rock(650, 230, 110, 160, moss=False),
             chip(220, 70, 22, 20), chip(560, 60, 22, 70), chip(400, 30, 18, 40, STONE_LIGHT), chip(90, 520, 18, 10),
             chip(720, 540, 18, 50)]
    parts += [rock(cx, 840, 420, 170), rock(120, 640, 120, 110), rock(680, 640, 120, 110)]
    wm, _, _ = wordmark_layers('CRITICAL', 'MASS', cx, 450 + top, top, 450 + top + 36 + bot, bot)
    parts.append(wm)
    d = 60
    parts.append(poly([(cx, 790 - d), (cx + d, 790), (cx, 790 + d), (cx - d, 790)], CRYSTAL, sw=9))
    parts.append(poly([(cx, 790 - d * 0.6), (cx + d * 0.6, 790), (cx, 790)], CRYSTAL_LIGHT, stroke=None))
    return W, H, parts


def write(name, W, H, parts):
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">'
           f'<title>CRITICAL MASS</title>' + ''.join(parts) + '</svg>')
    p = os.path.join(HERE, name + '.svg')
    with open(p, 'w') as f:
        f.write(svg)
    return p


def main():
    os.makedirs(OUT_PNG, exist_ok=True)
    for name, fn, base_w in (('critical-mass-logo', primary, 600), ('critical-mass-badge', badge, 400)):
        W, H, parts = fn()
        svg = write(name, W, H, parts)
        for k in (1, 2, 4):
            out = os.path.join(OUT_PNG, f'{name}@{k}x.png')
            subprocess.run(['rsvg-convert', '-w', str(base_w * k), '-o', out, svg], check=True)
            print(out)


if __name__ == '__main__':
    sys.exit(main())
