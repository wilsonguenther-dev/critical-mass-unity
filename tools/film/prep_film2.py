#!/usr/bin/env python3
"""Intro film 2: turn the generated stills into compositing layers (Assets/_Game/Art/Story/_film2~/).

  python3 tools/film/prep_film2.py RAW_DIR [name ...]

Every layer in the film is new art generated for round 2 (prompts and QA in the Obsidian intro-film-2 notes). This
script only keys, cleans and recolours; it never paints:
- chroma key (flat magenta #FF00FF or green #00FF00 backgrounds) to straight-alpha RGBA, with spill removal, a 1 px
  edge choke and removal of stray specks;
- team recolour: the approved references are Azure (navy and teal); the Crimson copy of a character is the same pixels
  with only the team hues (170 to 250 degrees) remapped to crimson and maroon, so faces, gear and limbs stay identical;
- Kaiju states: the same painting with only the glowing seams and crystal spines (lime, 55 to 115 degrees) remapped to
  Azure, Crimson or berserk violet, plus a matching glow mask per state;
- debris: one sheet of rock fragments split into separate sprites by connected components.
"""
import os, sys, json
import numpy as np
import cv2

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(REPO, 'Assets/_Game/Art/Story/_film2~')


def load(p):
    im = cv2.imread(p, cv2.IMREAD_UNCHANGED)
    if im.shape[2] == 4:
        im = im[..., :3]
    return cv2.cvtColor(im, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0


def save(name, rgba):
    os.makedirs(OUT, exist_ok=True)
    p = os.path.join(OUT, name)
    a = np.clip(rgba, 0, 1)
    if a.shape[2] == 4:
        cv2.imwrite(p, cv2.cvtColor((a * 255 + 0.5).astype(np.uint8), cv2.COLOR_RGBA2BGRA), [cv2.IMWRITE_PNG_COMPRESSION, 6])
    else:
        cv2.imwrite(p, cv2.cvtColor((a * 255 + 0.5).astype(np.uint8), cv2.COLOR_RGB2BGR), [cv2.IMWRITE_JPEG_QUALITY, 94])
    print('wrote', name, a.shape[1], 'x', a.shape[0])


def key(rgb, kind='magenta', lo=0.12, hi=0.42, min_area=400):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    if kind == 'magenta':
        m = np.minimum(r, b) - g
    else:
        m = g - np.maximum(r, b)
    a = 1.0 - np.clip((m - lo) / (hi - lo), 0, 1)
    # choke 1 px and soften: removes the key-coloured fringe the generator leaves on edges
    a = cv2.erode(a, np.ones((3, 3), np.float32))
    a = cv2.GaussianBlur(a, (0, 0), 0.7)
    # drop specks (small islands of alpha)
    n, lab, st, _ = cv2.connectedComponentsWithStats((a > 0.5).astype(np.uint8), 8)
    keep = np.zeros(n, bool)
    keep[1:] = st[1:, cv2.CC_STAT_AREA] >= min_area
    solid = keep[lab]
    solid = cv2.dilate(solid.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool)
    a = a * solid
    # spill: pull the key hue out of edge pixels
    out = rgb.copy()
    if kind == 'magenta':
        sp = np.clip(np.minimum(r, b) - g, 0, None)
        out[..., 0] -= sp * 0.9
        out[..., 2] -= sp * 0.9
    else:
        sp = np.clip(g - np.maximum(r, b), 0, None)
        out[..., 1] -= sp * 0.9
    out = np.clip(out, 0, 1)
    return np.dstack([out, a])


def key_flat(rgb, lo=0.10, hi=0.22, min_area=400):
    """Key a flat background of any colour (estimated from the border pixels) by colour distance."""
    b = np.concatenate([rgb[:6].reshape(-1, 3), rgb[-6:].reshape(-1, 3), rgb[:, :6].reshape(-1, 3), rgb[:, -6:].reshape(-1, 3)])
    bg = np.median(b, 0)
    dist = np.sqrt(((rgb - bg) ** 2).sum(-1))
    a = np.clip((dist - lo) / (hi - lo), 0, 1)
    a = cv2.erode(a, np.ones((3, 3), np.float32))
    a = cv2.GaussianBlur(a, (0, 0), 0.7)
    n, lab, st, _ = cv2.connectedComponentsWithStats((a > 0.5).astype(np.uint8), 8)
    keep = np.zeros(n, bool)
    keep[1:] = st[1:, cv2.CC_STAT_AREA] >= min_area
    a = a * cv2.dilate(keep[lab].astype(np.uint8), np.ones((5, 5), np.uint8))
    return np.dstack([rgb, a])


def crop_alpha(rgba, pad=8):
    ys, xs = np.nonzero(rgba[..., 3] > 0.02)
    y0, y1 = max(0, ys.min() - pad), min(rgba.shape[0], ys.max() + pad + 1)
    x0, x1 = max(0, xs.min() - pad), min(rgba.shape[1], xs.max() + pad + 1)
    return rgba[y0:y1, x0:x1]


def hsv(rgb):
    return cv2.cvtColor(rgb.astype(np.float32), cv2.COLOR_RGB2HSV)     # H in degrees 0..360, S, V 0..1


def rgb_of(h):
    return cv2.cvtColor(h.astype(np.float32), cv2.COLOR_HSV2RGB)


def crimson(rgba):
    """Team recolour Azure -> Crimson: hues 170..250 (teal, blue, navy) -> 352 (crimson), saturation kept or lifted."""
    h = hsv(rgba[..., :3])
    H, S, V = h[..., 0], h[..., 1], h[..., 2]
    w = np.clip((H - 160) / 12.0, 0, 1) * np.clip((255 - H) / 12.0, 0, 1) * np.clip((S - 0.10) / 0.12, 0, 1)
    w = cv2.GaussianBlur(w, (0, 0), 0.8)
    H2 = np.where(w > 0, 354.0, H)
    S2 = np.clip(np.maximum(S * 1.15, 0.62), 0, 1)
    V2 = np.clip(V * 1.08 + 0.03, 0, 1)
    h2 = np.dstack([H2, S2, V2])
    out = rgba.copy()
    out[..., :3] = rgba[..., :3] * (1 - w[..., None]) + rgb_of(h2) * w[..., None]
    return out


KAIJU_HUES = dict(azure=(203.0, 1.0), crimson=(352.0, 1.35), violet=(278.0, 1.0))


def seam_mask(rgba):
    h = hsv(rgba[..., :3])
    H, S, V = h[..., 0], h[..., 1], h[..., 2]
    w = np.clip(1 - np.abs(H - 82) / 36.0, 0, 1) * np.clip((S - 0.5) / 0.2, 0, 1) * np.clip((V - 0.6) / 0.2, 0, 1)
    return cv2.GaussianBlur(w, (0, 0), 0.8) * rgba[..., 3]


def kaiju_state(rgba, state):
    w = seam_mask(rgba)
    if state == 'neutral':
        return rgba, w
    hue, sat = KAIJU_HUES[state]
    h = hsv(rgba[..., :3])
    h2 = np.dstack([np.full_like(h[..., 0], hue), np.clip(h[..., 1] * sat, 0, 1), h[..., 2]])
    out = rgba.copy()
    out[..., :3] = rgba[..., :3] * (1 - w[..., None]) + rgb_of(h2) * w[..., None]
    return out, w


def glow_from(rgba, w, col, blur=18):
    g = cv2.GaussianBlur(w, (0, 0), blur) * 1.6 + w * 0.6
    g = np.clip(g, 0, 1)
    return np.dstack([np.ones_like(g)[..., None] * np.array(col, np.float32), g])


def split_sheet(rgba, prefix, min_area=1500):
    n, lab, st, _ = cv2.connectedComponentsWithStats((rgba[..., 3] > 0.3).astype(np.uint8), 8)
    k = 0
    for i in range(1, n):
        if st[i, cv2.CC_STAT_AREA] < min_area:
            continue
        x, y, w, h = st[i, :4]
        m = (lab == i).astype(np.float32)
        m = cv2.dilate(m, np.ones((5, 5), np.uint8))
        piece = rgba.copy()
        piece[..., 3] *= m
        k += 1
        save(f'{prefix}_{k:02d}.png', crop_alpha(piece[max(0, y - 6):y + h + 6, max(0, x - 6):x + w + 6]))
    return k


# raw name -> (op, out name, extra)
JOBS = {
    'key-hero-arena': ('plate', 'hero.jpg'),
    'p-sky-dawn': ('plate', 'sky_dawn.jpg'),
    'p-sky-storm': ('plate', 'sky_storm.jpg'),
    'p-sky-night': ('plate', 'sky_night.jpg'),
    'p-sky-split': ('plate', 'sky_split.jpg'),
    'p-hill': ('plate', 'hill.jpg'),
    'p-choice': ('plate', 'choice.jpg'),
    'p-islands-far2': ('key', 'islands_far.png', 'magenta'),
    'p-arena-calm': ('key', 'arena_calm.png', 'magenta'),
    'p-rocks-rise': ('sheet', 'rock', 'magenta'),
    'p-debris': ('sheet', 'debris', 'magenta'),
    'p-shrine': ('key', 'shrine.png', 'magenta'),
    'p-shrine-fg': ('key', 'shrine_fg.png', 'magenta'),
    'p-meadow-peoples': ('key', 'meadow.png', 'magenta'),
    'banner-azure': ('key', 'banner_azure.png', 'magenta'),
    'banner-crimson2': ('key', 'banner_crimson.png', 'green'),
    'biomass': ('key', 'biomass.png', 'magenta'),
    'corruption2': ('keyflat', 'corruption.png'),
    'clouds-storm': ('key', 'clouds_storm.png', 'green'),
    'clouds-day2': ('key', 'clouds_day.png', 'magenta'),
    'egg': ('key', 'egg.png', 'magenta'),
    'hatchling-burst': ('key', 'hatchling.png', 'magenta'),
    'kaiju2-roar-a': ('kaiju', 'kaiju'),
    'kaiju2-roar-b': ('kaiju', 'kaiju_b'),
    'kaiju2-stand-c': ('kaiju', 'kaiju_hero'),
    'ch-warrior-charge': ('char', 'warrior_charge'),
    'ch-warrior-stand': ('char', 'warrior_stand'),
    'ch-farmer-stand': ('char', 'farmer_stand'),
    'ch-farmer-feed': ('char', 'farmer_feed'),
    'ch-miner-stand': ('char', 'miner_stand'),
    'ch-builder-stand': ('char', 'builder_stand'),
    'ch-healer-stand': ('char', 'healer_stand'),
    'ch-demolitions-stand': ('char', 'demolitions_stand'),
}


def run(raw_dir, only):
    for name, job in JOBS.items():
        if only and name not in only:
            continue
        p = os.path.join(raw_dir, name + '.png')
        if not os.path.exists(p):
            print('missing', name)
            continue
        rgb = load(p)
        op = job[0]
        if op == 'plate':
            save(job[1], rgb)
        elif op == 'key':
            save(job[1], crop_alpha(key(rgb, job[2])))
        elif op == 'keyflat':
            save(job[1], crop_alpha(key_flat(rgb)))
        elif op == 'sheet':
            split_sheet(key(rgb, job[2], min_area=200), job[1])
        elif op == 'char':
            a = crop_alpha(key(rgb, 'magenta'))
            save(job[1] + '.png', a)
            save(job[1] + '_cr.png', crimson(a))
        elif op == 'kaiju':
            a = crop_alpha(key(rgb, 'magenta'))
            cols = dict(neutral=(0.62, 1.0, 0.35), azure=(0.25, 0.66, 1.0), crimson=(1.0, 0.12, 0.2), violet=(0.72, 0.35, 1.0))
            for st in ('neutral', 'azure', 'crimson', 'violet'):
                im, w = kaiju_state(a, st)
                save(f'{job[1]}_{st}.png', im)
                save(f'{job[1]}_{st}_glow.png', glow_from(im, w, cols[st]))


if __name__ == '__main__':
    run(sys.argv[1], set(sys.argv[2:]))
