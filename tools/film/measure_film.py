#!/usr/bin/env python3
"""intro-film-2: picture measurements on a rendered frame folder (grain off) or a video file.

  python3 tools/film/measure_film.py FRAMES_DIR_OR_VIDEO [--json OUT.json]

- holds: runs of consecutive frames whose picture area changes by less than 0.25/255 mean absolute difference
  (a frozen image); reports every run over 12 frames;
- cuts: mean luma (picture area) on the last frame before and first frame after each grid cut, and the step;
- bars: rows of exact black at top and bottom of each frame (must be the fixed 2.0:1 bars on every frame).
"""
import glob, json, os, subprocess, sys
import numpy as np
import cv2

FPS = 30
CUTS = [3.75, 9.75, 19.50, 23.25, 28.50, 32.25, 40.50]
HOLD_THR = 0.25
HOLD_MAX = 12


def frames(src):
    if os.path.isdir(src):
        for p in sorted(glob.glob(os.path.join(src, '*.jpg')) + glob.glob(os.path.join(src, '*.png'))):
            yield cv2.cvtColor(cv2.imread(p), cv2.COLOR_BGR2GRAY)
    else:
        w, h = 512, 288
        pr = subprocess.Popen(['ffmpeg', '-v', 'error', '-i', src, '-vf', f'scale={w}:{h}', '-f', 'rawvideo', '-pix_fmt',
                               'gray', '-'], stdout=subprocess.PIPE)
        while True:
            b = pr.stdout.read(w * h)
            if len(b) < w * h:
                break
            yield np.frombuffer(b, np.uint8).reshape(h, w)


def main(src):
    diffs, lum, bars = [], [], []
    prev = None
    for g in frames(src):
        H = g.shape[0]
        bar = int(round(H / 18))                 # 80/1440 of the height
        pic = g[bar + 1:H - bar - 1].astype(np.float32)
        lum.append(float(pic.mean()))
        top = int(np.argmax(g.max(1) > 8)) if g.max() > 8 else H
        bars.append(top)
        if prev is not None:
            diffs.append(float(np.abs(pic - prev).mean()))
        prev = pic
    runs, cur = [], 0
    for i, d in enumerate(diffs):
        if d < HOLD_THR:
            cur += 1
        else:
            if cur + 1 > HOLD_MAX:
                runs.append(dict(start_s=round((i - cur) / FPS, 2), frames=cur + 1))
            cur = 0
    if cur + 1 > HOLD_MAX:
        runs.append(dict(start_s=round((len(diffs) - cur) / FPS, 2), frames=cur + 1))
    cuts = []
    for c in CUTS:
        f = int(round(c * FPS))
        if f < len(lum):
            cuts.append(dict(t=c, before=round(lum[f - 1], 1), after=round(lum[f], 1), step=round(lum[f] - lum[f - 1], 1)))
    lb = sorted(set(bars))
    rep = dict(frames=len(lum), holds_over_12=runs, longest_still_run=max([r['frames'] for r in runs], default=0),
               min_frame_diff=round(min(diffs), 3), cuts=cuts, first_lit_row_values=lb[:6])
    return rep


if __name__ == '__main__':
    a = sys.argv[1:]
    r = main(a[0])
    s = json.dumps(r, indent=1)
    print(s)
    if '--json' in a:
        open(a[a.index('--json') + 1], 'w').write(s)
