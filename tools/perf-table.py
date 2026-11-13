#!/usr/bin/env python3
"""perf-smooth-1: one table from hidden-smoke folders (tools/demo-smoke.sh --qualities High,Medium,Low).

  python3 tools/perf-table.py DIR [DIR ...]        prints a markdown table, one row per match (quality level)

Per match it reads perf.json / perf-mN.json (PerfOverlay + the FrameRecorder summary "frame_recorder") and the
frames CSV beside it: frames, % over 33 ms, p99, frames over 50 ms, worst match frame, GC collections and the
longest GC.Collect, allocation per frame, loading frames and the slowest loading frame, ticks per frame, and the
three subsystems slow frames spend the most extra time in.
"""
import csv, json, os, sys


def rows_for(d):
    out = []
    for k in range(1, 10):
        p = os.path.join(d, 'perf.json' if k == 1 else f'perf-m{k}.json')
        if not os.path.exists(p):
            break
        perf = json.load(open(p))
        fr = perf.get('frame_recorder') or {}
        csvp = os.path.join(d, 'frames.csv' if k == 1 else f'frames-m{k}.csv')
        frames, loading, gcmax, gcover10 = [], [], 0.0, 0
        if os.path.exists(csvp):
            with open(csvp) as f:
                lines = [l for l in f if not l.startswith('#')]
            for r in csv.DictReader(lines):
                ms = float(r['frame_ms'])
                (loading if r['loading'] == '1' else frames).append(ms)
                if r['loading'] != '1':
                    gcmax = max(gcmax, float(r['gc_ms'] or 0))   # in-match collections only
                    gcover10 += float(r['gc_ms'] or 0) > 10
        frames.sort()
        n = len(frames)
        p99 = frames[min(n - 1, int(0.99 * (n - 1) + 0.5))] if n else None
        over33 = sum(1 for x in frames if x >= 33.3)
        over50 = sum(1 for x in frames if x > 50)
        top = ', '.join(f"{a[0].split('.')[-1]} +{a[1] - a[2]:.1f}" for a in (fr.get('spike_excess_ms') or [])[:3])
        out.append({
            'run': os.path.basename(os.path.normpath(d)), 'match': k, 'quality': perf.get('quality'),
            'frames': n, 'pct33': 100.0 * over33 / n if n else None, 'over33': over33, 'p99': p99, 'over50': over50,
            'worst': frames[-1] if n else None, 'gc_n': fr.get('gc_collections'), 'gc_max': gcmax, 'gc_over10': gcover10, 'pacing': perf.get('pacing', 'rule'),
            'gc_marker_max': next((m['max'] for m in perf.get('markers', []) if m['name'] == 'GC.Collect'), None),
            'alloc': (fr.get('alloc_kb_per_frame') or {}).get('mean'), 'load_frames': len(loading),
            'load_max': max(loading) if loading else None, 'ticks': fr.get('ticks_per_frame'), 'top': top,
            'p50': frames[n // 2] if n else None,
        })
    return out


def f(v, fmt='{:.1f}'):
    return '-' if v is None else fmt.format(v)


def main(dirs):
    print('| run | match | quality | frames | p50 ms | p99 ms | >=33 ms | % >=33 | >50 ms | worst ms | pacing | GC n | in-match GC max ms | GC >10 ms | alloc KB/frame | loading frames | worst loading ms | slow frames spend extra in |')
    print('| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |')
    for d in dirs:
        for r in rows_for(d):
            print(f"| {r['run']} | {r['match']} | {r['quality']} | {r['frames']} | {f(r['p50'])} | {f(r['p99'])} | {r['over33']} | "
                  f"{f(r['pct33'], '{:.2f}')} | {r['over50']} | {f(r['worst'])} | {r['pacing']} | {r['gc_n']} | {f(r['gc_max'])} | {r['gc_over10']} | "
                  f"{f(r['alloc'], '{:.2f}')} | {r['load_frames']} | {f(r['load_max'])} | {r['top']} |")


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(64)
    main(sys.argv[1:])
