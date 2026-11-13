#!/usr/bin/env python3
"""DG-56: a screenshot 'draws' when its mean brightness is above 0.08 and more than 1% of its pixels differ from the most
common colour (a black or single-colour window fails). Usage: check_screenshot.py PNG [PNG...]; exit 0 when all pass."""
import sys


def verdict(pixels):
    """pixels: list of (r, g, b) 0..255. Returns (ok, mean_brightness, off_fraction)."""
    if not pixels:
        return False, 0.0, 0.0
    n = len(pixels)
    mean = sum((0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0 for r, g, b in pixels) / n
    counts = {}
    for p in pixels:
        counts[p] = counts.get(p, 0) + 1
    off = 1.0 - max(counts.values()) / n
    return mean > 0.08 and off > 0.01, mean, off


def main(paths):
    from PIL import Image
    ok = True
    for p in paths:
        try:
            im = Image.open(p).convert('RGB')
        except OSError as e:
            print(f'{p}: unreadable ({e})')
            ok = False
            continue
        im.thumbnail((320, 320))
        good, mean, off = verdict(list(im.getdata()))
        print(f'{p}: mean={mean:.3f} off_mode={off:.3f} {"ok" if good else "FAIL"}')
        ok &= good
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]) if len(sys.argv) > 1 else 64)
