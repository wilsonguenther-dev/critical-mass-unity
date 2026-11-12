#!/usr/bin/env python3
"""Critical Mass HUD icon set and class portraits (bible p15), drawn by script as vector art.

    python3 build_icons.py                 # SVGs into ./svg, PNGs @1x/@2x/@4x into Assets/_Game/Resources/UI/Icons
    python3 build_icons.py --sheet OUT.jpg # also a review sheet: every icon at HUD size and at 4x

@1x is the size the HUD shows the icon at on a 1920x1080 screen (Art.px); the HUD picks the tier by canvas scale.
Style (bible p15, art direction 6.5): chunky shapes, one thick Void Navy #18233B silhouette line at constant screen
px, thinner inner lines, two or three flat tone steps with the light from the upper left and a highlight shape, big
expressive eyes on faces, team colour on the large top surfaces (helmets, hats, crests), neutrals stay neutral.
Needs shapely and rsvg-convert.
"""
import hashlib
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cmvec import *  # noqa: E402,F401,F403

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
SVG_DIR = os.path.join(HERE, 'svg')
OUT = os.path.join(GAME, 'Resources', 'UI', 'Icons')
META_TEMPLATE = os.path.join(GAME, 'Resources', 'UI', 'Logo', 'critical-mass-logo@1x.png.meta')
TEAMS = ('azure', 'crimson')
CLASSES = ('warrior', 'farmer', 'miner', 'demolitions', 'builder')
C = 64.0


def T_scale(k, dx=0.0, dy=0.0):
    return lambda g: mv(sc(g, k, k, origin=(C, C)), dx, dy)


ID = lambda g: g


# ---------------------------------------------------------------- shapes

def shield_shape(cx, top, w, h):
    hw = w / 2
    topline = [(cx - hw, top + h * 0.08), (cx - hw * 0.5, top), (cx, top + h * 0.06), (cx + hw * 0.5, top), (cx + hw, top + h * 0.08)]
    right = cub((cx + hw, top + h * 0.08), (cx + hw, top + h * 0.55), (cx + hw * 0.45, top + h * 0.82), (cx, top + h))[1:]
    left = [(2 * cx - x, y) for x, y in reversed(right[:-1])]
    return P(topline + right + left)


def heart_shape(cx, cy, s):
    g = unary_union([circ(cx - s * 0.24, cy - s * 0.12, s * 0.28), circ(cx + s * 0.24, cy - s * 0.12, s * 0.28),
                     P([(cx - s * 0.5, cy - s * 0.04), (cx + s * 0.5, cy - s * 0.04), (cx, cy + s * 0.46)])])
    return g.buffer(s * 0.03).buffer(-s * 0.03)


def leaf_shape(cx, cy, length, width, ang):
    r = (length ** 2 / 4 + width ** 2 / 4) / width
    d = r - width / 2
    g = circ(0, -d, r).intersection(circ(0, d, r))
    g = affinity.rotate(affinity.rotate(g, 90, origin=(0, 0)), ang, origin=(0, 0))
    return mv(g, cx, cy)


def star(cx, cy, r0, r1, n, rot0=-90):
    pts = []
    for i in range(n * 2):
        r = r0 if i % 2 == 0 else r1
        a = math.radians(rot0 + 180 * i / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return P(pts)


def crescent_swoosh(cx=64, cy=64, r=54, off=12, ang=45):
    a = math.radians(ang)
    return circ(cx, cy, r).difference(circ(cx + off * math.cos(a), cy + off * math.sin(a), r - off * 0.5))


def trident(T=ID):
    shaft = rrect(59, 46, 10, 52, 4)
    tip = P([(64, 22), (75, 44), (64, 40), (53, 44)])
    prongs = stroke_geom([(42, 36), (42, 52), (50, 61), (64, 63), (78, 61), (86, 52), (86, 36)], 9)
    t1 = P([(36, 40), (42, 22), (48, 40)])
    t2 = P([(80, 40), (86, 22), (92, 40)])
    butt = P([(56, 96), (72, 96), (64, 108)])
    return T(unary_union([shaft, tip, prongs, t1, t2, butt]))


def horns_flame(T=ID):
    hl = taper(cub((54, 70), (32, 66), (26, 44), (36, 24)), 16, 3)
    hr = mirror_x(hl, 64)
    flame = unary_union([circ(64, 70, 13), P([(51, 67), (60, 46), (64, 28), (70, 46), (77, 67)])])
    shaft = rrect(59, 80, 10, 18, 4)
    butt = P([(56, 96), (72, 96), (64, 108)])
    return T(unary_union([hl, hr, flame, shaft, butt]))


def emblem(team, T=ID):
    return trident(T) if team == 'azure' else horns_flame(T)


# ---------------------------------------------------------------- tools (drawn in the full 128 frame, T maps them)

def swoosh(a, team, T=ID, ang=45):
    tm = TEAM[team]
    g = T(crescent_swoosh(64, 64, 54, 14, ang))
    a.toon(g, (tm['base'], tm['shade'], tm['light']), k=0.06, hl=0.05)


def sword(a, team, T=ID):
    o = (C, C)
    R = lambda g: T(rot(g, -45, o))
    a.toon(R(circ(14, 64, 8)), BRASS, k=0.18)
    a.toon(R(rrect(16, 57, 24, 14, 4)), WOOD, k=0.2)
    blade = P([(42, 55), (100, 55), (118, 64), (100, 73), (42, 73)])
    a.toon(R(blade), METAL, k=0.1, hl=0.05)
    a.fill(R(P([(46, 64), (100, 64), (112, 64), (100, 69), (46, 69)])), METAL[1], sil=False)
    a.line(R(blade))
    a.toon(R(rrect(34, 44, 11, 40, 5)), BRASS, k=0.2)
    a.fill(R(circ(39.5, 64, 4.5)), TEAM[team]['base'], sil=False)


def war_shield(a, team, T=ID):
    tm = TEAM[team]
    for y, x0 in ((42, 8), (64, 4), (86, 10)):
        a.toon(T(rrect(x0, y - 4, 30, 8, 4)), (tm['light'], tm['base'], WHITE), k=0.0, hl=0)
    sh = T(shield_shape(72, 18, 76, 96))
    a.toon(sh, (tm['base'], tm['shade'], tm['light']), k=0.12)
    inner = sh.buffer(-8 * _k(T))
    a.toon(sh.difference(inner), METAL, k=0.0, hl=0, line=False, sil=False)
    a.fill(inner.intersection(mv(inner, -6 * _k(T), -6 * _k(T))).difference(mv(inner, 6 * _k(T), 6 * _k(T))), tm['light'], sil=False)
    a.line(inner)
    a.line(sh)
    a.toon(T(circ(72, 58, 13)), METAL, k=0.18)
    a.glint(*T(Point(66, 52)).coords[0], 3.5 * _k(T), 2.2 * _k(T))
    a.glint(*T(Point(56, 34)).coords[0], 7 * _k(T), 3.5 * _k(T))


def _k(T):
    b = T(box(0, 0, 10, 10)).bounds
    return (b[2] - b[0]) / 10


def staff(a, team, T=ID):
    a.toon(T(stroke_geom([(30, 110), (86, 32)], 11)), WOOD, k=0.15)
    a.toon(T(taper(cub((84, 34), (90, 16), (110, 18), (104, 38)), 11, 6)), WOOD, k=0.15)
    for cx, cy, ang in ((98, 54, 35), (70, 24, -50)):
        lf = T(leaf_shape(cx, cy, 34, 16, ang))
        a.toon(lf, LEAF, k=0.14)
        x0, y0 = cx - 13 * math.cos(math.radians(ang)), cy - 13 * math.sin(math.radians(ang))
        x1, y1 = cx + 13 * math.cos(math.radians(ang)), cy + 13 * math.sin(math.radians(ang))
        a.fill(T(stroke_geom([(x0, y0), (x1, y1)], 2.6)), LEAF[1], sil=False)


def roots(a, team, T=ID):
    tm = TEAM[team]
    a.toon(T(ell(64, 106, 50, 13)), (tm['base'], tm['shade'], tm['light']), k=0.0, hl=0)
    a.fill(T(ell(64, 104, 34, 7)), tm['light'], sil=False)
    for pts, w0 in (((48, 106), (30, 80), (60, 66), (40, 36)), 15), (((64, 108), (72, 76), (54, 56), (70, 22)), 17), \
                   (((80, 106), (100, 84), (74, 66), (94, 42)), 15):
        g = T(taper(cub(*pts, n=14), w0, 4))
        a.toon(g, WOOD, k=0.12)
        a.toon(T(leaf_shape(pts[3][0] + 6, pts[3][1] - 2, 20, 10, -30)), LEAF, k=0.14)


def pickaxe(a, team, T=ID):
    o = (C, C)
    R = lambda g: T(rot(g, 38, o))
    a.toon(R(stroke_geom([(64, 34), (64, 118)], 12)), WOOD, k=0.15)
    a.fill(R(rrect(57, 46, 14, 9, 2)), TEAM[team]['base'], sil=False)
    a.line(R(rrect(57, 46, 14, 9, 2)))
    upper = cub((14, 50), (40, 14), (88, 14), (114, 50))
    lower = cub((106, 46), (82, 30), (46, 30), (22, 46))
    head = P(upper + lower)
    a.toon(R(head), METAL, k=0.12)
    a.toon(R(rrect(54, 18, 20, 22, 5)), STEEL_DARK, k=0.15)


def wall_break(a, team, T=ID):
    tm = TEAM[team]
    wall = T(rrect(14, 52, 100, 60, 7))
    a.toon(wall, STONE, k=0.1)
    for y in (72, 92):
        a.fill(T(stroke_geom([(18, y), (110, y)], 3.2)), NAVY, sil=False)
    for x, y0 in ((40, 52), (88, 52), (28, 72), (64, 72), (100, 72), (46, 92), (82, 92)):
        a.fill(T(stroke_geom([(x, y0 + 2), (x, y0 + 18)], 3.2)), NAVY, sil=False)
    crack = T(P([(60, 52), (70, 52), (62, 70), (74, 84), (60, 112), (64, 86), (52, 72)]))
    a.fill(crack, NAVY, sil=False)
    a.toon(T(star(64, 40, 34, 15, 8, rot0=-90)), (tm['base'], tm['shade'], tm['light']), k=0.08)
    a.toon(T(star(64, 40, 16, 7, 8, rot0=-68)), CREAMT, k=0, hl=0)
    for cx, cy, r, an in ((104, 30, 9, 20), (22, 30, 8, 70), (112, 52, 6, 40)):
        pts = [(cx + r * math.cos(math.radians(an + d)), cy + r * math.sin(math.radians(an + d))) for d in (0, 95, 190, 270)]
        a.toon(T(P(pts)), STONE, k=0.2)


def bomb(a, team, T=ID):
    tm = TEAM[team]
    for y, x0, L in ((54, 6, 22), (74, 2, 18)):
        a.toon(T(rrect(x0, y - 3.5, L, 7, 3.5)), (tm['light'], tm['base'], WHITE), k=0, hl=0)
    body = T(circ(60, 74, 38))
    a.toon(body, SLATE, k=0.14, hl=0.08)
    band = body.intersection(T(box(0, 66, 128, 80)))
    a.fill(band, tm['base'], sil=False)
    a.fill(band.intersection(T(box(0, 66, 128, 70))), tm['light'], sil=False)
    a.line(band)
    a.line(body)
    a.glint(*T(Point(44, 56)).coords[0], 9 * _k(T), 5 * _k(T))
    cap = T(rot(rrect(68, 30, 22, 16, 4), 40, (79, 38)))
    a.toon(cap, BRASS, k=0.2)
    a.toon(T(taper(cub((84, 32), (92, 20), (102, 26), (104, 16)), 6, 5)), ('#D8B47A', '#9C7A45', '#F5DDAE'), k=0.0, hl=0)
    a.toon(T(star(106, 15, 13, 5, 6, rot0=-80)), ('#FFE08A', BRASS[0], WHITE), k=0.0, hl=0)
    a.fill(T(circ(106, 15, 4)), WHITE, sil=False)


def mine(a, team, T=ID):
    tm = TEAM[team]
    a.toon(T(ell(64, 100, 52, 14)), (STONE[1], NAVY, STONE[0]), k=0, hl=0)
    dome = T(unary_union([ell(64, 84, 42, 26).intersection(box(0, 0, 128, 84)), rrect(20, 80, 88, 16, 7)]))
    a.toon(dome, STEEL_DARK, k=0.12, hl=0.08)
    a.fill(T(rrect(20, 86, 88, 4, 2)), STEEL_DARK[1], sil=False)
    for x in (40, 88):
        a.toon(T(rrect(x - 4, 54, 8, 14, 3)), METAL, k=0.2)
    a.toon(T(circ(64, 56, 11)), (tm['base'], tm['shade'], tm['light']), k=0.1)
    a.glint(*T(Point(61, 52)).coords[0], 3.5 * _k(T), 2.4 * _k(T))
    for ang in (-150, -90, -30):
        r0, r1 = 20, 32
        p0 = (64 + r0 * math.cos(math.radians(ang)), 56 + r0 * math.sin(math.radians(ang)))
        p1 = (64 + r1 * math.cos(math.radians(ang)), 56 + r1 * math.sin(math.radians(ang)))
        a.toon(T(stroke_geom([p0, p1], 6)), (tm['light'], tm['base'], WHITE), k=0, hl=0)


def hammer(a, team, T=ID):
    o = (C, C)
    R = lambda g: T(rot(g, 32, o))
    a.toon(R(stroke_geom([(64, 50), (64, 118)], 12)), WOOD, k=0.15)
    a.fill(R(rrect(57, 96, 14, 18, 4)), WOOD[1], sil=False)
    head = rrect(26, 20, 76, 34, 8)
    a.toon(R(head), METAL, k=0.12)
    a.toon(R(rrect(20, 24, 12, 26, 4)), METAL, k=0.2)
    band = rrect(56, 18, 16, 38, 3)
    a.toon(R(band), (TEAM[team]['base'], TEAM[team]['shade'], TEAM[team]['light']), k=0.15, hl=0)
    a.glint(*R(Point(42, 28)).coords[0], 8 * _k(T), 2.6 * _k(T), rot_deg=32)


def build(a, team, T=ID):
    tm = TEAM[team]
    for x, y, h in ((24, 44, 66), (57, 38, 72), (90, 44, 66)):
        a.toon(T(rrect(x, y, 14, h, 4)), WOOD, k=0.18)
    for y, ang in ((54, -7), (80, 6)):
        pl = T(rot(rrect(12, y, 104, 17, 4), ang, (64, y + 8)))
        a.toon(pl, WOOD, k=0.14)
        a.fill(T(rot(stroke_geom([(18, y + 9), (110, y + 9)], 1.6), ang, (64, y + 8))), WOOD[1], sil=False)
        for x in (31, 64, 97):
            a.ink(T(circ(x, y + 8.5 + (x - 64) * math.tan(math.radians(ang)), 2.4)))
    a.toon(T(circ(98, 30, 18)), (tm['base'], tm['shade'], tm['light']), k=0.12)
    plus = T(unary_union([rrect(95, 20, 6, 20, 2), rrect(88, 27, 20, 6, 2)]))
    a.toon(plus, CREAMT, k=0, hl=0)


TOOLS = {'warrior': (sword, war_shield), 'farmer': (staff, roots), 'miner': (pickaxe, wall_break),
         'demolitions': (bomb, mine), 'builder': (hammer, build)}


# ---------------------------------------------------------------- icon builders

def icon_crest(team):
    a = Art(f'crest-{team}', 70)
    tm = TEAM[team]
    sh = shield_shape(64, 10, 98, 112)
    a.toon(sh, (tm['base'], tm['shade'], tm['light']), k=0.1, hl=0.05)
    inner = sh.buffer(-12)
    a.toon(inner, (tm['deep'], NAVY, tm['shade']), k=0.08, hl=0.06)
    em = emblem(team, T_scale(0.78, 0, 2))
    a.toon(em, CREAMT, k=0.1, hl=0.05)
    a.glint(36, 24, 9, 4, -20)
    return a


def icon_class(cls, team):
    a = Art(f'class-{cls}-{team}', 40)
    tm = TEAM[team]
    a.toon(circ(64, 64, 56), (tm['base'], tm['shade'], tm['light']), k=0.08, hl=0.05)
    a.toon(circ(64, 64, 48), (tm['tint'], mix(tm['tint'], tm['base'], 0.45), WHITE), k=0.1, hl=0)
    TOOLS[cls][0](a, team, T_scale(0.9))
    return a


def icon_ability(cls, kind, team):
    a = Art(f'{"attack" if kind == 0 else "ability"}-{cls}-{team}', 54)
    if kind == 0 and cls in ('warrior', 'farmer', 'miner', 'builder'):
        swoosh(a, team, T_scale(1.0), ang=40)
    TOOLS[cls][kind](a, team, ID)
    return a


def icon_biomass():
    a = Art('carry-biomass', 54)
    orb = circ(62, 70, 44)
    a.toon(orb, BIOMASS, k=0.16, hl=0.06)
    inner = orb.buffer(-a.IN * 2)
    lines = []
    for row in range(-3, 4):
        for col in range(-3, 4):
            cx, cy = 62 + col * 26 + (13 if row % 2 else 0), 70 + row * 22
            hexp = [(cx + 14 * math.cos(math.radians(60 * k + 30)), cy + 13 * math.sin(math.radians(60 * k + 30))) for k in range(7)]
            lines.append(stroke_geom(hexp, 3.0))
    a.fill(unary_union(lines).intersection(inner), BIOMASS[1], sil=False)
    a.fill(ell(44, 50, 15, 9, -35).intersection(inner), '#F4FFC8', sil=False)
    a.fill(circ(34, 70, 4).intersection(inner), '#F4FFC8', sil=False)
    a.line(orb)
    a.toon(taper(cub((66, 28), (68, 22), (70, 18), (74, 12)), 6, 4), WOOD, k=0, hl=0)
    lf = leaf_shape(88, 18, 30, 14, -20)
    a.toon(lf, LEAF, k=0.14)
    return a


def icon_corruption():
    a = Art('carry-corruption', 54)
    for i in range(7):
        ang = -90 + i * 360 / 7 + 10
        p = lambda r, d=0: (62 + r * math.cos(math.radians(ang + d)), 70 + r * math.sin(math.radians(ang + d)))
        a.toon(P([p(34, -14), p(56), p(34, 14)]), (CORRUPT[1], NAVY, CORRUPT[0]), k=0, hl=0)
    orb = circ(62, 70, 40)
    a.toon(orb, CORRUPT, k=0.16, hl=0.06)
    inner = orb.buffer(-a.IN * 2)
    sp = [(62 + (4 + t * 2.2) * math.cos(t * 0.62), 72 + (4 + t * 2.2) * math.sin(t * 0.62)) for t in range(0, 14)]
    a.fill(taper(sp, 4, 9).intersection(inner), MAGENTA[0], sil=False)
    a.fill(ell(46, 52, 12, 7, -35).intersection(inner), MAGENTA[2], sil=False)
    a.line(orb)
    return a


def icon_carry_empty():
    a = Art('carry-empty', 54)
    a.fill(circ(64, 66, 40), '#2E304A')
    for i in range(12):
        a0 = math.radians(i * 30 + 4)
        a1 = math.radians(i * 30 + 22)
        pts = [(64 + 34 * math.cos(a0 + (a1 - a0) * t / 6), 66 + 34 * math.sin(a0 + (a1 - a0) * t / 6)) for t in range(7)]
        a.fill(stroke_geom(pts, 5), CREAM, sil=False)
    a.line(circ(64, 66, 40))
    return a


def crown_geom():
    body = P([(22, 82), (16, 40), (42, 62), (64, 30), (86, 62), (112, 40), (106, 82)])
    band = rrect(20, 78, 88, 24, 6)
    return body, band


def icon_crown(name='crown', px=34):
    a = Art(name, px)
    body, band = crown_geom()
    if px < 30:
        body, band = sc(body, 0.84, 0.84, origin=(64, 64)), sc(band, 0.84, 0.84, origin=(64, 64))
    a.toon(body, CREAMT, k=0.12, hl=0.06)
    for x, y in ((16, 38), (64, 27), (112, 38)):
        if px < 30:
            x, y = 64 + (x - 64) * 0.84, 64 + (y - 64) * 0.84
        a.toon(circ(x, y, 8 if px >= 30 else 9), BRASS, k=0.2)
    a.toon(band, CREAMT, k=0.16, hl=0.0)
    for x in ((42, 64, 86) if px >= 30 else ()):
        a.toon(P([(x, 82), (x + 7, 90), (x, 98), (x - 7, 90)]), BRASS, k=0.2, hl=0)
    return a


def egg_shape(cx, cy, rx, ry):
    pts = []
    for i in range(48):
        t = 2 * math.pi * i / 48
        k = 1.0 - 0.2 * max(0.0, -math.sin(t))
        pts.append((cx + rx * k * math.cos(t), cy + ry * math.sin(t) * (1.0 if math.sin(t) > 0 else 1.08)))
    return P(pts)


def icon_egg(name='kaiju-egg', px=72):
    a = Art(name, px)
    g = egg_shape(64, 70, 44, 50)
    left = g.intersection(box(0, 0, 64, 128))
    a.fill(left, SEAM_TEAL)
    a.fill(g.difference(box(0, 0, 64, 128)), SEAM_RED)
    seam = a.IN * 1.6 if px >= 40 else a.IN * 1.2
    inner = g.buffer(-seam * 0.4)
    cells = []
    for row in range(-4, 5):
        for col in range(-3, 4):
            cx, cy = 64 + col * 24 + (12 if row % 2 else 0), 70 + row * 19
            h = (row * 73856093 ^ col * 19349663) & 0xffff
            hexp = [(cx + 13 * math.cos(math.radians(60 * k + 30)) + ((h >> (k % 8)) % 5 - 2),
                     cy + 12 * math.sin(math.radians(60 * k + 30)) + ((h >> (k % 6 + 3)) % 5 - 2)) for k in range(6)]
            c = P(hexp).buffer(-seam * 0.5).intersection(inner)
            if c.area > 20:
                cells.append((c, (row + col) % 3 == 0))
    for c, dark in cells:
        tones = (EGG[1], NAVY, EGG[0]) if dark else EGG
        a.toon(c, tones, k=0.18 if px >= 40 else 0, hl=0.1 if px >= 40 else 0, line=False, sil=False)
    a.fill(stroke_geom([(64, 22), (60, 40), (68, 52), (61, 66)], seam * 1.4).intersection(g), '#C9FFF1', sil=False)
    a.glint(44, 38, 8, 4, -50, color='#8790A8')
    a.line(g, w=a.IN * 2)
    return a


def kaiju_tones(state):
    if state == 'neutral':
        return KAIJU_N, '#F9A858'
    if state == 'berserk':
        return CORRUPT, '#FF4FA8'
    tm = TEAM[state]
    return (tm['base'], tm['shade'], tm['light']), CREAM


def icon_hatchling():
    a = Art('kaiju-hatchling', 72)
    tones, _ = kaiju_tones('neutral')
    head = unary_union([circ(64, 66, 40), ell(64, 88, 36, 24)])
    a.toon(head, tones, k=0.12, hl=0.06)
    a.fill(ell(64, 96, 24, 13).intersection(head.buffer(-a.IN)), BONE[0], sil=False)
    for x in (44, 84):
        eye(a, x, 70, 12, 14, look=(0.2 if x < 64 else -0.1, -0.2), iris='#F9A858')
    for x in (34, 94):
        a.fill(ell(x, 86, 6, 3.5), BLUSH, sil=False)
    for x in (58, 70):
        a.ink(ell(x, 92, 2.4, 2))
    a.ink(stroke_geom(cub((54, 100), (60, 106), (68, 106), (74, 100), 8), 3.4))
    a.toon(P([(60, 103), (64, 110), (68, 103)]), BONE, k=0, hl=0)
    shell = P([(22, 52), (26, 30), (44, 16), (64, 12), (84, 16), (102, 30), (106, 52), (96, 44), (88, 54), (78, 42),
               (68, 54), (58, 42), (48, 54), (38, 42), (30, 52)])
    a.toon(shell, BONE, k=0.12, hl=0.06)
    a.ink(stroke_geom([(76, 22), (72, 30), (78, 34)], 2.4))
    return a


def icon_adult(state):
    a = Art(f'kaiju-adult-{state}', 72)
    tones, eyec = kaiju_tones(state)
    for x0, s in ((30, 1), (98, -1)):
        horn = P(cub((x0, 50), (x0 - 14 * s, 36), (x0 - 18 * s, 22), (x0 - 10 * s, 6)) + [(x0 + 12 * s, 40)])
        a.toon(horn, BONE, k=0.18)
    a.toon(P([(52, 26), (64, 6), (76, 26)]), (tones[1], NAVY, tones[0]), k=0, hl=0)
    head = unary_union([ell(64, 54, 42, 34), rrect(30, 54, 68, 52, 20)])
    a.toon(head, tones, k=0.12, hl=0.06)
    a.fill(rrect(42, 80, 44, 24, 10).intersection(head.buffer(-a.IN)), BONE[1] if state == 'berserk' else BONE[0], sil=False)
    mouth = P([(40, 86), (88, 86), (82, 108), (46, 108)]).buffer(3)
    a.fill(mouth, '#5A1E2E')
    a.fill(ell(64, 106, 16, 5).intersection(mouth), '#E06A7A', sil=False)
    for x in (46, 56, 72, 82):
        a.fill(P([(x - 4, 84), (x + 4, 84), (x, 94)]), WHITE, sil=False)
    for x in (52, 76):
        a.fill(P([(x - 4, 110), (x + 4, 110), (x, 100)]), WHITE, sil=False)
    a.line(mouth)
    for x, s in ((46, 1), (82, -1)):
        e = ell(x, 58, 11, 8)
        a.fill(e, eyec)
        a.ink(ell(x + 2 * s, 59, 3.4, 6).intersection(e))
        a.fill(circ(x - 3, 55, 2.2), WHITE, sil=False)
        lid = P([(x - 16, 40), (x + 16, 40), (x + 16 * s, 50 + 6), (x - 16 * s, 50 - 2)])
        a.fill(lid.intersection(e.buffer(a.IN)), tones[0], sil=False)
        a.line(e)
        a.ink(taper([(x - 15 * s, 46), (x, 50), (x + 13 * s, 56)], 7, 4))
    for x in (56, 72):
        a.ink(ell(x, 74, 3, 2.2))
    if state == 'berserk':
        for pts in (((38, 34), (44, 40), (40, 46)), ((88, 30), (84, 38), (90, 42))):
            a.fill(stroke_geom(pts, 3), MAGENTA[2], sil=False)
    a.glint(46, 30, 9, 4, -25)
    return a


def icon_heart(kind, name=None, px=32):
    a = Art(name or f'heart-{kind}', px)
    if kind == 'empty':
        tones = ('#2E304A', '#22243A', '#454866')
    else:
        tm = TEAM[kind]
        tones = (tm['base'], tm['shade'], tm['light'])
    g = heart_shape(64, 66, 108)
    a.toon(g, tones, k=0.12, hl=0.06)
    if kind != 'empty':
        a.glint(40, 44, 10, 6, -40)
    return a


def icon_banner(team):
    a = Art(f'map-banner-{team}', 28)
    tm = TEAM[team]
    a.toon(rrect(18, 6, 10, 116, 4), WOOD, k=0.2, hl=0)
    a.toon(circ(23, 8, 8), BRASS, k=0.2, hl=0)
    flag = P([(28, 14), (112, 14), (112, 104), (70, 86), (28, 104)])
    a.toon(flag, (tm['base'], tm['shade'], tm['light']), k=0.1, hl=0.04)
    a.toon(emblem(team, lambda g: mv(sc(g, 0.74, 0.66, origin=(64, 64)), 6, -12)), CREAMT, k=0, hl=0, line=False, sil=False)
    return a


def icon_you():
    a = Art('map-you', 18)
    a.toon(circ(64, 64, 52), BRASS, k=0.1, hl=0)
    a.toon(P([(64, 22), (98, 96), (64, 78), (30, 96)]), CREAMT, k=0, hl=0)
    return a


# ---------------------------------------------------------------- portraits

SKIN = {'warrior': ('#C98A5E', '#9A6340', '#E8B48A'), 'farmer': ('#F1C29B', '#C9946C', '#FFE0C4'),
        'miner': ('#8E5B3A', '#6A3F26', '#B98361'), 'demolitions': ('#E7B48A', '#BC8660', '#FAD3B0'),
        'builder': ('#B47A50', '#875536', '#D9A57C')}


def portrait(cls, team):
    a = Art(f'portrait-{cls}-{team}', 106)
    tm = TEAM[team]
    tones = (tm['base'], tm['shade'], tm['light'])
    skin = SKIN[cls]
    tile = rrect(4, 4, 120, 120, 16)
    a.clip = tile
    a.under = []
    a.zoom = (1.2, 64, 62)   # head-dominant bust like the p15 card portrait; the tile stays put (drawn unzoomed below)
    a.under.append(f'<path d="{path_d(tile)}" fill="{tm["tint"]}"/>')
    a.under.append(f'<path d="{path_d(tile.intersection(P([(4, 92), (92, 4), (124, 4), (124, 20), (20, 124), (4, 124)])))}" fill="{mix(tm["tint"], WHITE, 0.5)}"/>')
    a.fill(tile, tm['tint'], sil=False, opacity=0)

    cx, cy = 64, 72
    # shoulders and neck
    a.toon(rrect(16, 106, 96, 44, 22), tones, k=0.1, hl=0.05)
    a.toon(rrect(54, 96, 20, 16, 6), skin, k=0.25, hl=0)
    if cls == 'warrior':
        for x in (26, 102):
            a.toon(ell(x, 112, 18, 13), METAL, k=0.15)
    elif cls == 'farmer':
        for x in (40, 88):
            a.toon(rrect(x - 6, 104, 12, 30, 4), STRAW, k=0.2, hl=0)
    elif cls == 'builder':
        a.toon(rot(rrect(20, 112, 100, 10, 4), -18, (64, 116)), WOOD, k=0.2, hl=0)
    # head
    head = ell(cx, cy, 34, 36)
    for x in (cx - 34, cx + 34):
        a.toon(circ(x, cy + 4, 8), skin, k=0.2, hl=0)
    a.toon(head, skin, k=0.12, hl=0.05)
    a.fill(ell(cx - 22, cy + 18, 6, 3.5), BLUSH, sil=False, opacity=0.8)
    a.fill(ell(cx + 22, cy + 18, 6, 3.5), BLUSH, sil=False, opacity=0.8)
    a.fill(ell(cx + 1, cy + 13, 4.5, 3.4), skin[1], sil=False)
    ey = cy + 2
    if cls == 'warrior':
        eye(a, cx - 14, ey, 10, 12, look=(0.3, 0.1), lid=0.34, lid_side=-1, skin=skin)
        eye(a, cx + 14, ey, 10, 12, look=(0.3, 0.1), lid=0.34, lid_side=1, skin=skin)
        brow(a, cx - 26, ey - 18, cx - 6, ey - 11, 6)
        brow(a, cx + 26, ey - 18, cx + 6, ey - 11, 6)
        mask = ell(cx, cy + 26, 30, 16).intersection(head.buffer(1)).union(rrect(cx - 30, cy + 14, 60, 8, 3).intersection(head.buffer(1)))
        a.toon(mask, (tm['deep'], NAVY, tm['shade']), k=0.1, hl=0)
        helm = ell(cx, cy - 6, 40, 36).intersection(box(0, 0, 128, cy - 12)).union(rrect(cx - 40, cy - 22, 80, 12, 5))
        a.toon(helm, tones, k=0.12, hl=0.06)
        a.toon(rrect(cx - 40, cy - 20, 80, 9, 4), METAL, k=0.2, hl=0)
        a.toon(rrect(cx - 6, cy - 46, 12, 26, 5), METAL, k=0.2, hl=0.06)
        for x in (cx - 38, cx + 38):
            a.toon(rrect(x - 6, cy - 16, 12, 30, 5), tones, k=0.15, hl=0)
        a.glint(cx - 20, cy - 32, 8, 3.5, -30)
    elif cls == 'farmer':
        eye(a, cx - 14, ey, 10, 12, look=(-0.1, -0.1))
        eye(a, cx + 14, ey, 10, 12, look=(-0.1, -0.1))
        brow(a, cx - 24, ey - 17, cx - 6, ey - 18, 5, bend=4)
        brow(a, cx + 6, ey - 22, cx + 24, ey - 20, 5, bend=5)
        m = P(cub((cx - 14, cy + 20), (cx - 8, cy + 34), (cx + 10, cy + 34), (cx + 16, cy + 19)) + [(cx, cy + 22)])
        a.fill(m, '#7A2A36')
        a.fill(ell(cx + 2, cy + 30, 7, 3.5).intersection(m), '#E8707E', sil=False)
        a.fill(rrect(cx - 10, cy + 19, 22, 4, 1.5).intersection(m), WHITE, sil=False)
        a.line(m)
        for dx, dy in ((-24, 12), (-19, 15), (-27, 16), (20, 12), (25, 15), (17, 16)):
            a.fill(circ(cx + dx, cy + dy, 1.4), skin[1], sil=False)
        crown = ell(cx, cy - 34, 26, 20).intersection(box(0, 0, 128, cy - 28))
        brim = ell(cx, cy - 26, 58, 13)
        a.toon(brim, STRAW, k=0.1, hl=0.05)
        a.toon(crown.union(rrect(cx - 26, cy - 40, 52, 12, 4)), STRAW, k=0.14, hl=0.06)
        a.toon(rrect(cx - 26, cy - 38, 52, 9, 3), tones, k=0.15, hl=0)
        a.toon(leaf_shape(cx + 26, cy - 50, 22, 11, -60), LEAF, k=0.14)
        for x in range(-44, 50, 11):
            a.fill(stroke_geom([(cx + x, cy - 30), (cx + x + 4, cy - 22)], 1.6).intersection(brim.buffer(-a.IN)), STRAW[1], sil=False)
    elif cls == 'miner':
        eye(a, cx - 14, ey + 1, 10, 11, look=(0.2, 0.1), lid=0.25, lid_side=0, skin=skin)
        eye(a, cx + 14, ey + 1, 10, 11, look=(0.2, 0.1), lid=0.45, lid_side=0, skin=skin)
        brow(a, cx - 26, ey - 16, cx - 6, ey - 13, 6.5)
        brow(a, cx + 6, ey - 12, cx + 26, ey - 17, 6.5, bend=-2)
        stache = unary_union([ell(cx - 9, cy + 20, 12, 6, 15), ell(cx + 9, cy + 20, 12, 6, -15)])
        a.toon(stache, ('#4A2E1E', '#2E1B12', '#6E4A33'), k=0.12, hl=0)
        m = P([(cx + 2, cy + 27), (cx + 18, cy + 23), (cx + 14, cy + 30), (cx + 4, cy + 31)])
        a.fill(m, WHITE)
        a.line(m)
        a.fill(ell(cx - 22, cy + 6, 6, 4, 20), skin[1], sil=False, opacity=0.9)
        hat = ell(cx, cy - 8, 38, 34).intersection(box(0, 0, 128, cy - 14))
        a.toon(hat, tones, k=0.12, hl=0.06)
        a.toon(ell(cx, cy - 14, 46, 8), tones, k=0.2, hl=0)
        a.toon(rrect(cx - 4, cy - 42, 8, 28, 3), (tm['light'], tm['base'], WHITE), k=0, hl=0)
        a.toon(circ(cx, cy - 26, 10), BRASS, k=0.2)
        a.toon(circ(cx, cy - 26, 6), ('#FFF2B8', '#F0C860', WHITE), k=0, hl=0)
        a.glint(cx - 20, cy - 32, 7, 3, -30)
    elif cls == 'demolitions':
        for x in (cx - 15, cx + 15):
            a.toon(circ(x, ey, 14), BRASS, k=0.16)
        a.toon(rrect(cx - 4, ey - 3, 8, 6, 2), BRASS, k=0, hl=0)
        for x, lk in ((cx - 15, 0.1), (cx + 15, 0.25)):
            lens = circ(x, ey, 10)
            a.fill(lens, mix(tm['tint'], WHITE, 0.4))
            a.ink(ell(x + lk * 8, ey + 1, 5.5, 6.5).intersection(lens))
            a.fill(circ(x + lk * 8 - 2.5, ey - 2.5, 2.2), WHITE, sil=False)
            a.fill(lens.intersection(P([(x - 12, ey + 4), (x + 4, ey - 12), (x + 8, ey - 8), (x - 8, ey + 8)])), WHITE, sil=False, opacity=0.6)
            a.line(lens)
        brow(a, cx - 28, ey - 22, cx - 8, ey - 19, 5.5, bend=5)
        brow(a, cx + 8, ey - 19, cx + 28, ey - 24, 5.5, bend=5)
        m = P(cub((cx - 18, cy + 18), (cx - 12, cy + 36), (cx + 12, cy + 36), (cx + 18, cy + 17)))
        a.fill(m, '#7A2A36')
        a.fill(rrect(cx - 16, cy + 17, 32, 6, 2).intersection(m), WHITE, sil=False)
        a.fill(ell(cx, cy + 31, 8, 3.5).intersection(m), '#E8707E', sil=False)
        a.line(m)
        a.fill(ell(cx + 22, cy + 10, 5, 3), '#5D5560', sil=False, opacity=0.7)
        for pts in (((cx - 30, cy - 30), (cx - 40, cy - 46), (cx - 22, cy - 38)), ((cx + 6, cy - 34), (cx + 4, cy - 54), (cx + 16, cy - 38)),
                    ((cx + 24, cy - 32), (cx + 38, cy - 46), (cx + 32, cy - 26))):
            a.toon(P(list(pts)), ('#3A2E2A', '#241C1A', '#5C4A44'), k=0, hl=0)
        band = ell(cx, cy - 6, 37, 33).intersection(box(0, cy - 34, 128, cy - 18))
        a.toon(band, tones, k=0.14, hl=0.06)
        for d in ((cx + 32, cy - 26, 30), (cx + 34, cy - 18, 60)):
            a.toon(rot(rrect(d[0], d[1], 22, 9, 4), d[2], (d[0], d[1] + 4)), tones, k=0.15, hl=0)
        for x in (cx - 16, cx + 4):
            a.fill(circ(x, cy - 26, 2.2), CREAM, sil=False)
    else:  # builder
        eye(a, cx - 14, ey + 1, 10, 12, look=(0.25, 0.05), lid=0.32, lid_side=0, skin=skin)
        eye(a, cx + 14, ey + 1, 10, 12, look=(0.25, 0.05), lid=0.32, lid_side=0, skin=skin)
        brow(a, cx - 24, ey - 15, cx - 6, ey - 16, 6, bend=3)
        brow(a, cx + 6, ey - 17, cx + 24, ey - 15, 6, bend=3)
        m = P(cub((cx - 12, cy + 20), (cx - 4, cy + 30), (cx + 10, cy + 30), (cx + 16, cy + 17)) + [(cx + 2, cy + 23)])
        a.fill(m, '#7A2A36')
        a.fill(rrect(cx - 10, cy + 19, 24, 4, 1.5).intersection(m), WHITE, sil=False)
        a.line(m)
        a.toon(rot(rrect(cx + 26, cy - 14, 34, 7, 3), -60, (cx + 32, cy - 6)), STRAW, k=0.1, hl=0)
        cap = ell(cx, cy - 8, 37, 33).intersection(box(0, 0, 128, cy - 14))
        a.toon(cap, tones, k=0.12, hl=0.06)
        a.toon(P(cub((cx - 40, cy - 16), (cx - 52, cy - 14), (cx - 56, cy - 8), (cx - 44, cy - 6)) + [(cx - 10, cy - 12), (cx - 10, cy - 18)]),
               tones, k=0.2, hl=0)
        a.toon(rrect(cx - 37, cy - 18, 74, 6, 3), (tm['deep'], NAVY, tm['shade']), k=0, hl=0)
        a.toon(circ(cx + 4, cy - 30, 8), CREAMT, k=0.2, hl=0)
        a.toon(unary_union([rrect(cx + 2, cy - 36, 4, 12, 1), rrect(cx - 2, cy - 32, 12, 4, 1)]), BRASS, k=0, hl=0)
        a.glint(cx - 18, cy - 30, 7, 3, -30)
    return a


# ---------------------------------------------------------------- catalogue and output

def catalogue():
    out = []
    for t in TEAMS:
        out.append(icon_crest(t))
    for cls in CLASSES:
        for t in TEAMS:
            out.append(icon_class(cls, t))
            out.append(icon_ability(cls, 0, t))
            out.append(icon_ability(cls, 1, t))
            out.append(portrait(cls, t))
    out += [icon_biomass(), icon_corruption(), icon_carry_empty(), icon_crown(), icon_crown('map-crown', 18),
            icon_egg(), icon_egg('map-egg', 24), icon_hatchling()]
    out += [icon_adult(s) for s in ('neutral', 'azure', 'crimson', 'berserk')]
    out += [icon_heart(k) for k in ('azure', 'crimson', 'empty')]
    out += [icon_banner(t) for t in TEAMS]
    out.append(icon_you())
    return out


def write_meta(png):
    meta = png + '.meta'
    guid = hashlib.md5(('cm-ui-icons/' + os.path.basename(png)).encode()).hexdigest()
    with open(META_TEMPLATE) as f:
        lines = f.read().splitlines()
    out = []
    for ln in lines:
        if ln.startswith('guid:'):
            ln = 'guid: ' + guid
        elif ln.strip().startswith('textureCompression:'):
            ln = ln.split(':')[0] + ': 0'
        out.append(ln)
    with open(meta, 'w') as f:
        f.write('\n'.join(out) + '\n')


def main(argv):
    os.makedirs(SVG_DIR, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    arts = catalogue()
    manifest = {}
    for a in arts:
        sp = os.path.join(SVG_DIR, a.name + '.svg')
        with open(sp, 'w') as f:
            f.write(a.svg())
        for k in (1, 2, 4):
            png = os.path.join(OUT, f'{a.name}@{k}x.png')
            raster(sp, png, a.px * k)
            write_meta(png)
        manifest[a.name] = a.px
    with open(os.path.join(SVG_DIR, 'manifest.json'), 'w') as f:
        json.dump(manifest, f, indent=1, sort_keys=True)
    print(f'{len(arts)} icons -> {OUT}')
    if '--sheet' in argv:
        sheet(arts, argv[argv.index('--sheet') + 1])


def sheet(arts, path):
    from PIL import Image, ImageDraw, ImageFont
    W = 1400
    pad = 10
    font = ImageFont.truetype(os.path.join(GAME, 'Resources', 'UI', 'Fonts', 'NunitoSans-ExtraBold.ttf'), 12)
    tiles = []
    for a in arts:
        im1 = Image.open(os.path.join(OUT, f'{a.name}@1x.png')).convert('RGBA')
        im4 = Image.open(os.path.join(OUT, f'{a.name}@4x.png')).convert('RGBA')
        if im4.width > 216:
            im4 = im4.resize((216, 216), Image.LANCZOS)
        tw = max(im1.width + im4.width + 3 * pad, 150)
        th = max(im4.height, im1.height) + 2 * pad + 16
        t = Image.new('RGBA', (tw, th), (27, 49, 77, 255))
        t.alpha_composite(im1, (pad, pad + (im4.height - im1.height) // 2))
        t.alpha_composite(im4, (im1.width + 2 * pad, pad))
        ImageDraw.Draw(t).text((pad, th - 16), f'{a.name} {a.px}px', fill=(247, 240, 229), font=font)
        tiles.append(t)
    x = y = pad
    rowh = 0
    placed = []
    for t in tiles:
        if x + t.width > W - pad:
            x, y = pad, y + rowh + pad
            rowh = 0
        placed.append((t, x, y))
        x += t.width + pad
        rowh = max(rowh, t.height)
    H = y + rowh + pad
    s = Image.new('RGB', (W, H), (24, 35, 59))
    for t, x, y in placed:
        s.paste(t, (x, y), t)
    s.save(path, quality=86)
    print('sheet', path, s.size)


if __name__ == '__main__':
    main(sys.argv[1:])
