#!/usr/bin/env python3
"""Regenerate tools/remote/air-sim-weights.tsv (the fixture weights air-sim-job.sh splits by) from NUnit 3 result files.
   python3 tools/remote/air-sim-weights.py [--scale X] RESULT.xml... > tools/remote/air-sim-weights.tsv
A class seen in several files keeps its largest time. --scale converts another machine's seconds to the Air's
(GitHub ubuntu-latest to the Air: about 0.79, measured 2026-11-21 on the fast suite)."""
import sys, xml.etree.ElementTree as ET
args = sys.argv[1:]; scale = 1.0
if args[:1] == ['--scale']:
    scale = float(args[1]); args = args[2:]
w = {}
for f in args:
    per = {}
    for ts in ET.parse(f).getroot().iter('test-suite'):
        if ts.get('type') in ('TestFixture', 'ParameterizedFixture') and ts.get('classname'):
            per[ts.get('classname')] = per.get(ts.get('classname'), 0.0) + float(ts.get('duration') or 0) * scale
    for k, v in per.items():
        w[k] = max(w.get(k, 0.0), v)
print('# seconds on the Air per fixture class, for the --jobs split (tools/remote/air-sim-weights.py). Missing class = 60 s.')
for k, v in sorted(w.items(), key=lambda x: -x[1]):
    print(f'{v:.0f}\t{k}')
