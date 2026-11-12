#!/usr/bin/env python3
"""HUD and logo review (hud-and-logo brief): side-by-sides, state sheet, token sheet and measured checks.

Usage: python3 tools/art/hud_review.py CAPTURE_DIR BIBLE_REF_DIR OUT_DIR
CAPTURE_DIR holds tools/unity-capture.sh output (hud-1080.png, hud-mask.png, state-*.png, title.png, ...).
Writes sbs-*.jpg (bible left, ours right, 1400 px wide, under 180 KB), states.jpg, title.jpg, tokens.png and
measurements.json into OUT_DIR. Needs Pillow. Read-only on the project.
"""
import json
import math
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
FONTS = os.path.join(ROOT, 'Assets/_Game/Resources/UI/Fonts')
LOGO = os.path.join(ROOT, 'Assets/_Game/Resources/UI/Logo')
TOKENS_CS = os.path.join(ROOT, 'Assets/_Game/Runtime/Hud/Style/CMTokens.cs')
PALETTE_CS = os.path.join(ROOT, 'Assets/_Game/Sim/Config/Palette.cs')
NAVY = (0x18, 0x23, 0x3B)
CREAM = (0xF7, 0xF0, 0xE5)

# HudLayout reference rects at 1920x1080 (top-left origin), mirrored from HudLayout.cs
CLUSTERS = {
    'match': (560, 0, 800, 128),
    'kaiju': (1556, 8, 340, 100),
    'player': (24, 924, 640, 132),
    'minimap': (1624, 834, 272, 222),
}


def save_jpeg(im, path, max_kb=180):
    q = 85
    while q > 20:
        im.save(path, 'JPEG', quality=q, optimize=True)
        if os.path.getsize(path) <= max_kb * 1024:
            return q
        q -= 5
    return q


def sbs(left, right, path, width=1400, label_l='BIBLE', label_r='OURS'):
    gap = 8
    h = 600
    lw = int(left.width * h / left.height)
    rw = int(right.width * h / right.height)
    k = (width - gap) / (lw + rw)
    h2 = max(1, int(h * k))
    l2 = left.convert('RGB').resize((max(1, int(lw * k)), h2), Image.LANCZOS)
    r2 = right.convert('RGB').resize((max(1, width - gap - l2.width), h2), Image.LANCZOS)
    out = Image.new('RGB', (width, h2 + 26), (40, 40, 40))
    out.paste(l2, (0, 26))
    out.paste(r2, (l2.width + gap, 26))
    d = ImageDraw.Draw(out)
    f = ImageFont.truetype(os.path.join(FONTS, 'NunitoSans-ExtraBold.ttf'), 16)
    d.text((6, 4), label_l, fill=(240, 240, 240), font=f)
    d.text((l2.width + gap + 6, 4), label_r, fill=(240, 240, 240), font=f)
    return save_jpeg(out, path)


def crop(im, rect, pad=0):
    x, y, w, h = rect
    return im.crop((max(0, x - pad), max(0, y - pad), min(im.width, x + w + pad), min(im.height, y + h + pad)))


def on_bg(img, bg, size, margin=0.06):
    canvas = Image.new('RGB', size, bg)
    w = int(size[0] * (1 - 2 * margin))
    h = int(img.height * w / img.width)
    if h > size[1] * (1 - 2 * margin):
        h = int(size[1] * (1 - 2 * margin))
        w = int(img.width * h / img.height)
    i2 = img.resize((w, h), Image.LANCZOS)
    canvas.paste(i2, ((size[0] - w) // 2, (size[1] - h) // 2), i2 if i2.mode == 'RGBA' else None)
    return canvas


# ---- colour science (CIEDE2000) ----
def srgb_to_lab(c):
    def lin(v):
        v /= 255.0
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = (lin(v) for v in c[:3])
    x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047
    y = (0.2126 * r + 0.7152 * g + 0.0722 * b)
    z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883
    f = lambda t: t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    return 116 * f(y) - 16, 500 * (f(x) - f(y)), 200 * (f(y) - f(z))


def de2000(c1, c2):
    L1, a1, b1 = srgb_to_lab(c1)
    L2, a2, b2 = srgb_to_lab(c2)
    C1, C2 = math.hypot(a1, b1), math.hypot(a2, b2)
    Cm = (C1 + C2) / 2
    G = 0.5 * (1 - math.sqrt(Cm ** 7 / (Cm ** 7 + 25 ** 7)))
    a1p, a2p = a1 * (1 + G), a2 * (1 + G)
    C1p, C2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1p = math.degrees(math.atan2(b1, a1p)) % 360
    h2p = math.degrees(math.atan2(b2, a2p)) % 360
    dLp, dCp = L2 - L1, C2p - C1p
    dh = h2p - h1p
    if C1p * C2p == 0:
        dh = 0
    elif dh > 180:
        dh -= 360
    elif dh < -180:
        dh += 360
    dHp = 2 * math.sqrt(C1p * C2p) * math.sin(math.radians(dh / 2))
    Lm, Cpm = (L1 + L2) / 2, (C1p + C2p) / 2
    if C1p * C2p == 0:
        hpm = h1p + h2p
    elif abs(h1p - h2p) <= 180:
        hpm = (h1p + h2p) / 2
    else:
        hpm = (h1p + h2p + 360) / 2 if h1p + h2p < 360 else (h1p + h2p - 360) / 2
    T = (1 - 0.17 * math.cos(math.radians(hpm - 30)) + 0.24 * math.cos(math.radians(2 * hpm))
         + 0.32 * math.cos(math.radians(3 * hpm + 6)) - 0.20 * math.cos(math.radians(4 * hpm - 63)))
    dtheta = 30 * math.exp(-(((hpm - 275) / 25) ** 2))
    Rc = 2 * math.sqrt(Cpm ** 7 / (Cpm ** 7 + 25 ** 7))
    Sl = 1 + 0.015 * (Lm - 50) ** 2 / math.sqrt(20 + (Lm - 50) ** 2)
    Sc, Sh = 1 + 0.045 * Cpm, 1 + 0.015 * Cpm * T
    Rt = -math.sin(math.radians(2 * dtheta)) * Rc
    return math.sqrt((dLp / Sl) ** 2 + (dCp / Sc) ** 2 + (dHp / Sh) ** 2 + Rt * (dCp / Sc) * (dHp / Sh))


def hexrgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def median_px(im, x, y, r=1):
    px = [im.getpixel((x + dx, y + dy)) for dx in range(-r, r + 1) for dy in range(-r, r + 1)]
    return tuple(sorted(p[i] for p in px)[len(px) // 2] for i in range(3))


def text_height(im, box, ref=CREAM, tol=70):
    """Vertical extent (px) of near-ref pixels inside box (digit height of cream text)."""
    x0, y0, x1, y1 = box
    rows = []
    for y in range(y0, y1):
        for x in range(x0, x1):
            p = im.getpixel((x, y))
            if sum(abs(p[i] - ref[i]) for i in range(3)) < tol:
                rows.append(y)
                break
    return (max(rows) - min(rows) + 1) if rows else 0


def run_profile(im, x, y0, y1):
    """Colour runs down a column: [(label, length)] with labels navy/cream/other."""
    runs = []
    for y in range(y0, y1):
        p = im.getpixel((x, y))
        lab = 'navy' if de2000(p, NAVY) < 8 else 'cream' if de2000(p, CREAM) < 10 else 'other'
        if runs and runs[-1][0] == lab:
            runs[-1][1] += 1
        else:
            runs.append([lab, 1])
    return runs


def token_sheet(path):
    """Our p05: swatches read from CMTokens.cs/Palette.cs, and the three shipped faces."""
    pal = dict(re.findall(r'public const string (\w+) = "(#[0-9A-Fa-f]{6})"', open(PALETTE_CS).read()))
    names = [('Void Navy', 'VoidNavy'), ('Azure Team', 'Azure'), ('Crimson Team', 'Crimson'), ('Warm Cream', 'Cream'),
             ('Biomass', 'Biomass'), ('Corruption', 'Corruption'), ('Stone', 'Stone'), ('Tool Brass', 'Brass')]
    W, H = 1440, 520
    im = Image.new('RGB', (W, H), CREAM)
    d = ImageDraw.Draw(im)
    ui = ImageFont.truetype(os.path.join(FONTS, 'NunitoSans-ExtraBold.ttf'), 18)
    mono = ImageFont.truetype(os.path.join(FONTS, 'JetBrainsMono-ExtraBold.ttf'), 18)
    for i, (label, key) in enumerate(names):
        x = 24 + i * 176
        c = hexrgb(pal[key])
        d.rounded_rectangle((x, 24, x + 150, 174), 14, fill=NAVY)
        d.rounded_rectangle((x + 3, 27, x + 147, 171), 12, fill=CREAM)
        d.rounded_rectangle((x + 5, 29, x + 145, 169), 10, fill=c)
        d.text((x, 184), label, fill=NAVY, font=ui)
        d.text((x, 208), pal[key].upper(), fill=NAVY, font=mono)
    big = [('Bungee-Regular.ttf', 'BUNGEE', 60), ('NunitoSans-ExtraBold.ttf', 'Nunito Sans', 54), ('JetBrainsMono-ExtraBold.ttf', '04:28  +12', 54)]
    for i, (f, s, sz) in enumerate(big):
        d.text((24 + i * 480, 280), s, fill=NAVY, font=ImageFont.truetype(os.path.join(FONTS, f), sz))
    notes = ['Display: logo, title, event cards (SIL OFL 1.1)', 'UI: labels, ability names (SIL OFL 1.1)', 'Numbers: clock, scores, counts (SIL OFL 1.1)']
    for i, n in enumerate(notes):
        d.text((24 + i * 480, 370), n, fill=NAVY, font=ui)
    im.save(path)
    return im


def main():
    cap, ref, out = sys.argv[1:4]
    os.makedirs(out, exist_ok=True)
    m = {}
    hud = Image.open(os.path.join(cap, 'hud-1080.png')).convert('RGB')
    R = lambda n: Image.open(os.path.join(ref, n))

    # side-by-sides
    m['sbs-hud'] = sbs(R('15-hud-full.png'), hud, os.path.join(out, 'sbs-hud.jpg'))
    m['sbs-hud-match'] = sbs(R('15-hud-match.png'), crop(hud, CLUSTERS['match'], 6), os.path.join(out, 'sbs-hud-match.jpg'))
    m['sbs-hud-player'] = sbs(R('15-hud-player.png'), crop(hud, (24, 896, 640, 160), 6), os.path.join(out, 'sbs-hud-player.jpg'))
    m['sbs-hud-minimap'] = sbs(R('15-hud-minimap.png'), crop(hud, CLUSTERS['minimap'], 6), os.path.join(out, 'sbs-hud-minimap.jpg'))
    m['sbs-hud-kaiju'] = sbs(R('15-hud-full.png').crop((734, 0, 934, 60)), crop(hud, CLUSTERS['kaiju'], 6), os.path.join(out, 'sbs-hud-kaiju.jpg'))
    logo = Image.open(os.path.join(LOGO, 'critical-mass-logo@2x.png')).convert('RGBA')
    badge = Image.open(os.path.join(LOGO, 'critical-mass-badge@2x.png')).convert('RGBA')
    m['sbs-logo'] = sbs(R('04-primary-logo.png'), on_bg(logo, NAVY, (1148, 898)), os.path.join(out, 'sbs-logo.jpg'))
    m['sbs-logo-badge'] = sbs(R('04-compact-badge.png'), on_bg(badge, NAVY, (804, 1014)), os.path.join(out, 'sbs-logo-badge.jpg'))
    # p05 composite: swatches row + type stack
    sw = ['05-void-navy.png', '05-azure-team.png', '05-crimson-team.png', '05-warm-cream.png', '05-biomass.png',
          '05-corruption.png', '05-stone.png', '05-tool-brass.png']
    bible05 = Image.new('RGB', (1440, 520), CREAM)
    for i, n in enumerate(sw):
        bible05.paste(R(n).convert('RGB').resize((150, 150)), (24 + i * 176, 24))
    ts = R('05-type-stack.png').convert('RGB')
    bible05.paste(ts.resize((1440, int(ts.height * 1440 / ts.width))), (0, 280))
    ours05 = token_sheet(os.path.join(out, 'tokens.png'))
    m['sbs-colour-type'] = sbs(bible05, ours05, os.path.join(out, 'sbs-colour-type.jpg'))

    # state sheet: hill states (match cluster), Kaiju states (kaiju cluster), death (full frame)
    tiles = []
    for n in ('state-hill-neutral', 'state-hill-azure', 'state-hill-contested'):
        tiles.append(crop(Image.open(os.path.join(cap, n + '.png')).convert('RGB'), CLUSTERS['match'], 4))
    for n in ('state-kaiju-neutral', 'state-kaiju-azure', 'state-kaiju-crimson', 'state-kaiju-berserk'):
        tiles.append(crop(Image.open(os.path.join(cap, n + '.png')).convert('RGB'), CLUSTERS['kaiju'], 4))
    death = Image.open(os.path.join(cap, 'state-death.png')).convert('RGB').resize((700, 394), Image.LANCZOS)
    sheet = Image.new('RGB', (1400, 3 * 140 + 4 * 112 + 394 + 40), (40, 40, 40))
    y = 0
    for t in tiles:
        t2 = t.resize((int(t.width * 0.875), int(t.height * 0.875))) if t.width > 700 else t
        sheet.paste(t2, (0, y))
        y += t2.height + 4
    sheet.paste(death, (700, 0))
    sheet = sheet.crop((0, 0, 1400, max(y, 394)))
    save_jpeg(sheet, os.path.join(out, 'states.jpg'))
    title = Image.open(os.path.join(cap, 'title.png')).convert('RGB').resize((1400, 788), Image.LANCZOS)
    save_jpeg(title, os.path.join(out, 'title.jpg'))
    for n in ('hud-720', 'hud-2160'):
        p = os.path.join(cap, n + '.png')
        if os.path.exists(p):
            im = Image.open(p).convert('RGB')
            m[n + '-size'] = im.size
            # same cluster at native pixels, for the sharpness check
            s = im.height / 1080
            x, yy, w, h = CLUSTERS['player']
            crop(im, (int(x * s), int((yy - 28) * s), int(w * s), int((h + 28) * s))).save(os.path.join(out, n + '-player.png'))

    # measured checks
    mask = Image.open(os.path.join(cap, 'hud-mask.png')).convert('RGB')
    px = mask.load()
    n_hud = sum(1 for yy in range(mask.height) for xx in range(mask.width)
                if abs(px[xx, yy][0] - 255) + px[xx, yy][1] + abs(px[xx, yy][2] - 255) > 60)
    m['hud_area_pct'] = round(100.0 * n_hud / (mask.width * mask.height), 2)
    m['clock_digit_px'] = text_height(hud, (930, 8, 1000, 64))
    m['score_digit_px_azure'] = text_height(hud, (730, 12, 858, 62))
    samples = {
        'Void Navy (clock pill)': ((960, 62), '#18233B'),
        'Warm Cream (crown)': ((960, 99), '#F7F0E5'),
        'Azure (shield)': ((619, 52), '#16C7D9'),
        'Crimson (shield)': ((1301, 52), '#FF5A5F'),
        'Tool Brass (ready rim)': ((420, 968), '#C79245'),
        'Meter orange (segment 1)': ((1657, 41), '#F9A858'),
    }
    m['swatches'] = {}
    for k, ((x, yy), h) in samples.items():
        p = median_px(hud, x, yy, 0 if 'rim' in k else 1)
        m['swatches'][k] = {'sample': '#%02X%02X%02X' % p, 'bible': h, 'dE2000': round(de2000(p, hexrgb(h)), 2)}
    # outline profile down through the Azure panel's top edge (x = 746): expect navy 3, cream 2, then fill
    m['outline_profile_azure_panel'] = run_profile(hud, 746, 0, 16)
    m['outline_profile_hp_plate'] = run_profile(hud, 300, 942, 960)
    with open(os.path.join(out, 'measurements.json'), 'w') as f:
        json.dump(m, f, indent=1)
    print(json.dumps(m, indent=1))


if __name__ == '__main__':
    main()
