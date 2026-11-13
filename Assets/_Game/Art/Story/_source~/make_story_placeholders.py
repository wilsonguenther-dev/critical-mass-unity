#!/usr/bin/env python3
"""
DG-19 story intro: PLACEHOLDER parallax layers for the five story cards.

Draws flat shapes in the bible p05 palette (the same hexes as Sim/Config/Palette.cs and Runtime/Hud/Style/CMTokens.cs) so
the story intro runs end to end before the real stills exist. Every file this script writes carries "-placeholder" in its
name; the real stills (rendered later from the game's own models, brief: Obsidian unity-port/briefs/story-cards.md) are
dropped next to them WITHOUT the suffix, and StoryContent prefers the real file whenever one is present.

Output: Assets/_Game/Art/Story/Resources/StoryCards/card-<n>/{back,mid,front}-placeholder.png, 1920 x 1080, RGBA.
  back  = opaque sky or backdrop (moves least)
  mid   = transparent, the subject (moves a little)
  front = transparent, foreground framing (moves most)

Run from anywhere (plain Python 3 + Pillow, no game tools):
  python3 Assets/_Game/Art/Story/_source~/make_story_placeholders.py
Deterministic: the same script writes the same pixels.
"""
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFont

W, H = 1920, 1080
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "Resources", "StoryCards"))
FONT = os.path.normpath(os.path.join(HERE, "..", "..", "..", "Resources", "UI", "Fonts", "Bungee-Regular.ttf"))

# ---- bible p05 + art direction 2.4 (Palette.cs) ----
VOID_NAVY = "#18233B"
AZURE = "#16C7D9"
CRIMSON = "#FF5A5F"
CREAM = "#F7F0E5"
BIOMASS = "#B7F238"
CORRUPTION = "#8E3FD1"
STONE = "#6F7282"
BRASS = "#C79245"
SKY_TOP = "#6FCBEA"
SKY_HORIZON = "#CDEFF8"
EGG_SEAM = "#4FDDC3"
PANEL_NAVY = "#1B314D"
METER_ORANGE = "#F9A858"


def rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    a, b = rgb(a), rgb(b)
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def rgba(h, a=255):
    return rgb(h) + (a,)


def new_layer(opaque_hex=None, edge_hex=VOID_NAVY):
    """Opaque backdrop, or a transparent layer whose clear pixels carry the outline colour (no dark fringes when filtered)."""
    if opaque_hex is not None:
        return Image.new("RGBA", (W, H), rgba(opaque_hex))
    return Image.new("RGBA", (W, H), rgba(edge_hex, 0))


def vgrad(img, top_hex, bottom_hex, y0=0, y1=H):
    d = ImageDraw.Draw(img)
    for y in range(y0, y1):
        t = (y - y0) / max(1, (y1 - y0 - 1))
        d.line([(0, y), (W, y)], fill=mix(top_hex, bottom_hex, t) + (255,))


def outlined_polygon(d, pts, fill_hex, outline=VOID_NAVY, width=10, alpha=255):
    d.polygon(pts, fill=rgba(fill_hex, alpha))
    d.line(pts + [pts[0]], fill=rgba(outline), width=width, joint="curve")


def outlined_ellipse(d, box, fill_hex, outline=VOID_NAVY, width=10):
    d.ellipse(box, fill=rgba(fill_hex), outline=rgba(outline), width=width)


def island(cx, top_y, half_w, depth, seed):
    """A floating-island silhouette: a flat-ish top and a jagged underside, as a polygon."""
    r = random.Random(seed)
    pts = []
    steps = 9
    for i in range(steps + 1):
        x = cx - half_w + 2 * half_w * i / steps
        pts.append((x, top_y + r.uniform(-10, 10)))
    for i in range(steps, -1, -1):
        x = cx - half_w + 2 * half_w * i / steps
        k = 1 - abs((i / steps) * 2 - 1)
        pts.append((x, top_y + 30 + depth * (0.25 + 0.75 * k) + r.uniform(-25, 25)))
    return pts


def stamp(img, n, layer):
    """Clearly marks the file as placeholder art (bottom-left, Stone, small)."""
    d = ImageDraw.Draw(img)
    try:
        f = ImageFont.truetype(FONT, 26)
    except OSError:
        f = ImageFont.load_default()
    text = "PLACEHOLDER ART  CARD %d  %s" % (n, layer.upper())
    y = 120 if layer == "back" else (156 if layer == "mid" else 192)  # below the intro's top letterbox bar
    d.text((28, y), text, font=f, fill=rgba(CREAM if layer == "back" else STONE, 150))


def crack(d, x0, y0, x1, y1, seed, width):
    r = random.Random(seed)
    pts = [(x0, y0)]
    n = 9
    for i in range(1, n):
        t = i / n
        pts.append((x0 + (x1 - x0) * t + r.uniform(-60, 60), y0 + (y1 - y0) * t + r.uniform(-35, 35)))
    pts.append((x1, y1))
    d.line(pts, fill=rgba(CREAM), width=width + 16, joint="curve")
    d.line(pts, fill=rgba(SKY_HORIZON), width=width, joint="curve")
    return pts


def banner(d, pole_x, base_y, height, flag_hex, facing):
    d.rectangle([pole_x - 9, base_y - height, pole_x + 9, base_y], fill=rgba(VOID_NAVY))
    fx = pole_x + 12 * facing
    w = 230 * facing
    pts = [(fx, base_y - height + 10), (fx + w, base_y - height + 40), (fx + w * 0.82, base_y - height + 110),
           (fx + w, base_y - height + 180), (fx, base_y - height + 200)]
    outlined_polygon(d, pts, flag_hex, width=9)
    d.ellipse([pole_x - 20, base_y - height - 26, pole_x + 20, base_y - height + 14], fill=rgba(BRASS),
              outline=rgba(VOID_NAVY), width=6)


def egg(d, cx, cy, rx, ry, seam=True):
    outlined_ellipse(d, [cx - rx, cy - ry, cx + rx, cy + ry], CREAM, width=12)
    if seam:
        pts = [(cx - rx * 0.75, cy - ry * 0.1), (cx - rx * 0.4, cy - ry * 0.3), (cx - rx * 0.1, cy - ry * 0.05),
               (cx + rx * 0.2, cy - ry * 0.35), (cx + rx * 0.55, cy - ry * 0.1), (cx + rx * 0.8, cy - ry * 0.25)]
        d.line(pts, fill=rgba(EGG_SEAM), width=14, joint="curve")


def kaiju_silhouette(d, cx, base_y, s, fill_hex=VOID_NAVY, eye_hex=METER_ORANGE):
    body = [(cx - 260 * s, base_y), (cx - 230 * s, base_y - 260 * s), (cx - 150 * s, base_y - 420 * s),
            (cx - 60 * s, base_y - 520 * s), (cx + 60 * s, base_y - 540 * s), (cx + 150 * s, base_y - 470 * s),
            (cx + 210 * s, base_y - 330 * s), (cx + 270 * s, base_y - 120 * s), (cx + 300 * s, base_y)]
    outlined_polygon(d, body, fill_hex, outline=CREAM, width=8)
    for i in range(5):  # back spikes
        x = cx - 170 * s + i * 70 * s
        y = base_y - 430 * s - (40 * s if i in (1, 2) else 0) + i * 22 * s
        outlined_polygon(d, [(x - 34 * s, y + 30 * s), (x, y - 70 * s), (x + 34 * s, y + 30 * s)], fill_hex,
                         outline=CREAM, width=6)
    d.ellipse([cx + 40 * s, base_y - 470 * s, cx + 80 * s, base_y - 440 * s], fill=rgba(eye_hex))


def hill(d, cx, base_y, half_w, height, fill_hex="#5E8F4A"):
    pts = [(cx - half_w, base_y)]
    for i in range(1, 16):
        t = i / 16
        x = cx - half_w + 2 * half_w * t
        pts.append((x, base_y - height * math.sin(math.pi * t) ** 0.8))
    pts.append((cx + half_w, base_y))
    outlined_polygon(d, pts, fill_hex, width=10)
    return pts


def units(d, x0, x1, y, n, team_hex, seed):
    r = random.Random(seed)
    for i in range(n):
        x = x0 + (x1 - x0) * (i + 0.5) / n + r.uniform(-12, 12)
        yy = y + r.uniform(-8, 8)
        d.ellipse([x - 16, yy - 52, x + 16, yy - 20], fill=rgba(CREAM), outline=rgba(VOID_NAVY), width=5)
        d.rounded_rectangle([x - 20, yy - 24, x + 20, yy + 20], radius=10, fill=rgba(team_hex), outline=rgba(VOID_NAVY),
                            width=5)


# ---------------------------------------------------------------- cards

def card1():
    """The sky cracked. And the land rose to meet it."""
    back = new_layer(SKY_TOP)
    vgrad(back, "#3C5E8A", SKY_HORIZON)
    d = ImageDraw.Draw(back)
    crack(d, 380, 0, 1560, 420, 11, 18)
    crack(d, 1100, 160, 1700, 90, 12, 8)
    mid = new_layer()
    d = ImageDraw.Draw(mid)
    for cx, ty, hw, dep, seed in ((420, 560, 260, 200, 1), (1460, 500, 300, 240, 2), (960, 700, 420, 260, 3)):
        pts = island(cx, ty, hw, dep, seed)
        outlined_polygon(d, pts, STONE, width=10)
        d.rectangle([cx - hw + 20, ty - 8, cx + hw - 20, ty + 22], fill=rgba("#5E8F4A"))
    front = new_layer()
    d = ImageDraw.Draw(front)
    outlined_polygon(d, [(0, 1080), (0, 820), (220, 760), (430, 860), (520, 1080)], VOID_NAVY, outline=PANEL_NAVY)
    outlined_polygon(d, [(1920, 1080), (1920, 780), (1700, 720), (1500, 880), (1440, 1080)], VOID_NAVY,
                     outline=PANEL_NAVY)
    return back, mid, front


def card2():
    """Two banners. One hill. Neither has learned to share."""
    back = new_layer(SKY_TOP)
    vgrad(back, SKY_TOP, SKY_HORIZON)
    d = ImageDraw.Draw(back)
    for cx, cy, r in ((300, 220, 70), (1650, 160, 90), (1150, 300, 50)):
        d.ellipse([cx - r * 2, cy - r, cx + r * 2, cy + r], fill=rgba(CREAM, 200))
    mid = new_layer()
    d = ImageDraw.Draw(mid)
    hill(d, 960, 900, 700, 330)
    d.polygon([(960, 520), (930, 590), (990, 590)], fill=rgba(BRASS))  # the hill marker
    d.rectangle([954, 590, 966, 640], fill=rgba(VOID_NAVY))
    front = new_layer()
    d = ImageDraw.Draw(front)
    banner(d, 330, 1000, 520, AZURE, 1)
    banner(d, 1590, 1000, 520, CRIMSON, -1)
    d.rectangle([0, 990, W, H], fill=rgba(VOID_NAVY))
    return back, mid, front


def card3():
    """Far away, a Kaiju egg sleeps. It remembers every meal."""
    back = new_layer(VOID_NAVY)
    vgrad(back, VOID_NAVY, "#2E3F63")
    d = ImageDraw.Draw(back)
    r = random.Random(7)
    for _ in range(90):
        x, y, s = r.uniform(0, W), r.uniform(0, 600), r.uniform(2, 5)
        d.ellipse([x - s, y - s, x + s, y + s], fill=rgba(CREAM, int(r.uniform(90, 200))))
    mid = new_layer()
    d = ImageDraw.Draw(mid)
    # shrine: two stone pillars and a lintel, a raised plinth
    outlined_polygon(d, [(560, 900), (1360, 900), (1300, 820), (620, 820)], STONE)
    for x in (680, 1180):
        outlined_polygon(d, [(x, 820), (x + 60, 820), (x + 60, 360), (x, 360)], STONE)
    outlined_polygon(d, [(620, 360), (1300, 360), (1260, 290), (660, 290)], STONE)
    front = new_layer()
    d = ImageDraw.Draw(front)
    d.ellipse([760, 760, 1160, 840], fill=rgba(EGG_SEAM, 90))  # a faint glow under the egg
    egg(d, 960, 640, 150, 190)
    outlined_polygon(d, [(0, 1080), (0, 940), (300, 900), (520, 1080)], PANEL_NAVY, outline=VOID_NAVY)
    outlined_polygon(d, [(1920, 1080), (1920, 920), (1600, 900), (1420, 1080)], PANEL_NAVY, outline=VOID_NAVY)
    return back, mid, front


def card4():
    """Feed it, and it fights for you. Corrupt it, and it fights everyone."""
    back = new_layer(VOID_NAVY)
    d = ImageDraw.Draw(back)
    for x in range(W):
        t = x / (W - 1)
        c = mix("#3E6B2A", "#3A1F5C", min(1, max(0, (t - 0.35) / 0.3)))
        d.line([(x, 0), (x, H)], fill=c + (255,))
    mid = new_layer()
    d = ImageDraw.Draw(mid)
    kaiju_silhouette(d, 960, 960, 1.15)
    front = new_layer()
    d = ImageDraw.Draw(front)
    r = random.Random(4)
    for _ in range(14):  # biomass orbs, left
        x, y, s = r.uniform(80, 700), r.uniform(300, 980), r.uniform(26, 48)
        outlined_ellipse(d, [x - s, y - s, x + s, y + s], BIOMASS, width=7)
    for _ in range(12):  # corruption shards, right
        x, y, s = r.uniform(1240, 1860), r.uniform(300, 980), r.uniform(34, 60)
        outlined_polygon(d, [(x, y - s), (x + s * 0.45, y), (x, y + s), (x - s * 0.45, y)], CORRUPTION, width=7)
    return back, mid, front


def card5():
    """Hold the hill. Raise the Kaiju."""
    back = new_layer(SKY_TOP)
    vgrad(back, "#F3B27A", "#F7E3C9", 0, 620)
    vgrad(back, "#F7E3C9", SKY_HORIZON, 620, H)
    d = ImageDraw.Draw(back)
    d.ellipse([820, 380, 1100, 660], fill=rgba(CREAM))
    mid = new_layer()
    d = ImageDraw.Draw(mid)
    kaiju_silhouette(d, 1380, 860, 1.25, fill_hex=PANEL_NAVY)
    front = new_layer()
    d = ImageDraw.Draw(front)
    hill(d, 760, 1100, 820, 360)
    d.polygon([(760, 690), (728, 760), (792, 760)], fill=rgba(BRASS))
    d.rectangle([754, 760, 766, 800], fill=rgba(VOID_NAVY))
    units(d, 420, 720, 880, 5, AZURE, 21)
    units(d, 800, 1100, 880, 5, CRIMSON, 22)
    return back, mid, front


CARDS = [card1, card2, card3, card4, card5]
LAYERS = ("back", "mid", "front")


def main():
    for n, fn in enumerate(CARDS, start=1):
        imgs = fn()
        folder = os.path.join(OUT, "card-%d" % n)
        os.makedirs(folder, exist_ok=True)
        for layer, img in zip(LAYERS, imgs):
            stamp(img, n, layer)
            path = os.path.join(folder, "%s-placeholder.png" % layer)
            img.save(path, optimize=True)
            print(os.path.relpath(path))
    return 0


if __name__ == "__main__":
    sys.exit(main())
