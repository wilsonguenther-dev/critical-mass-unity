#!/usr/bin/env python3
"""Spend governor (decision 5, U-014). Counts the workflow's own reported total tokens, never output tokens alone.

Sources, in order:
  1. `totalTokens` in <session>/workflows/<runId>.json, the number the Workflow runtime reports (39.4 M on Monday
     for about 14 landed items). source = "workflow-totalTokens".
  2. While that file does not exist yet: a live estimate from the run's agent transcripts
     (<session>/subagents/workflows/<runId>/agent-*.jsonl): input + output + cache-creation tokens per assistant
     message, deduplicated by message id. On Monday's three runs this estimate read 1.198 to 1.218 times the reported
     total, but on the aborted 2026-11-12 run wf_a743c82d-56d it read 0.886 (282,970 against 319,516), so it can err
     LOW. The raw count is therefore divided by ESTIMATE_FLOOR_RATIO (0.85) before the thresholds (panel revision
     2026-11-12); `raw` keeps the undivided count. source = "transcript-estimate".
Cache READS are not counted by either source; that matches the U-014 measure (Monday's all-in 543 M includes them).

Thresholds: ratio >= 0.90 writes loop/state/STOP-DISPATCH (no new builders or batches); >= 1.00 writes
loop/state/HALT. Exit codes: 0 ok, 10 stop-dispatch, 11 halt, 12 no data for the run yet.

  spend.py check --run-id wf_... --cap N [--projects-dir ~/.claude/projects] [--dry-run]
  spend.py check                       (reads run_id and cap from loop/state/run.json)
  spend.py watch --interval 60 [--dry-run]   (the launcher starts this as the "spend" watcher)
"""
import argparse
import glob
import json
import os
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cmloop as C  # noqa: E402

PROJECTS = Path(os.environ.get('CM_LOOP_PROJECTS_DIR') or Path.home() / '.claude/projects')
STOP_AT, HALT_AT = 0.90, 1.00
ESTIMATE_FLOOR_RATIO = 0.85  # lowest estimate/reported ratio seen (0.886 on 2026-11-12), rounded down


def find_run_json(run_id, projects=PROJECTS):
    hits = glob.glob(str(Path(projects) / '*' / '*' / 'workflows' / f'{run_id}.json'))
    return Path(sorted(hits)[-1]) if hits else None


def find_transcripts(run_id, projects=PROJECTS):
    return sorted(glob.glob(str(Path(projects) / '*' / '*' / 'subagents' / 'workflows' / run_id / 'agent-*.jsonl')))


def transcript_tokens(files):
    tot = {'input': 0, 'output': 0, 'cache_creation': 0, 'cache_read': 0, 'messages': 0}
    for f in files:
        seen = set()
        with open(f, errors='replace') as fh:
            for line in fh:
                try:
                    o = json.loads(line)
                except ValueError:
                    continue
                if o.get('type') != 'assistant':
                    continue
                m = o.get('message') or {}
                u = m.get('usage')
                mid = m.get('id')
                if not u or mid in seen:
                    continue
                seen.add(mid)
                tot['messages'] += 1
                tot['input'] += int(u.get('input_tokens') or 0)
                tot['output'] += int(u.get('output_tokens') or 0)
                tot['cache_creation'] += int(u.get('cache_creation_input_tokens') or 0)
                tot['cache_read'] += int(u.get('cache_read_input_tokens') or 0)
    tot['counted'] = tot['input'] + tot['output'] + tot['cache_creation']
    return tot


def measure(run_id, projects=PROJECTS):
    rj = find_run_json(run_id, projects)
    if rj:
        d = C.read_json(rj, {}) or {}
        if isinstance(d.get('totalTokens'), (int, float)):
            return {'spent': int(d['totalTokens']), 'source': 'workflow-totalTokens', 'file': str(rj),
                    'status': d.get('status')}
    files = find_transcripts(run_id, projects)
    if files:
        t = transcript_tokens(files)
        return {'spent': int(-(-t['counted'] // ESTIMATE_FLOOR_RATIO)), 'raw': t['counted'],
                'source': 'transcript-estimate', 'agents': len(files), 'detail': t}
    return {'spent': None, 'source': 'none'}


def classify(spent, cap):
    if spent is None:
        return 'no-data', None
    ratio = spent / cap
    return ('halt' if ratio >= HALT_AT else 'stop-dispatch' if ratio >= STOP_AT else 'ok'), round(ratio, 4)


def check(run_id, cap, projects=PROJECTS, dry=False):
    if not cap or int(cap) <= 0:
        raise SystemExit('spend: a positive --cap is required (Wilson sets it; it is never raised without his word)')
    m = measure(run_id, projects)
    action, ratio = classify(m['spent'], int(cap))
    rec = {'ts': C.utc(), 'run_id': run_id, 'cap': int(cap), 'spent': m['spent'], 'ratio': ratio, 'action': action,
           'source': m['source']}
    if not dry:
        C.append_jsonl(C.LOGS / 'spend.jsonl', rec)
        C.write_json(C.STATE / 'spend.json', rec)
        if action in ('stop-dispatch', 'halt') and not (C.STATE / 'STOP-DISPATCH').exists():
            (C.STATE / 'STOP-DISPATCH').write_text(f'spend {m["spent"]} of cap {cap} ({ratio:.0%}) at {rec["ts"]}\n')
            C.livelog(f'Spend reached {ratio:.0%} of the cap; no new builders start.', dry)
        if action == 'halt' and not (C.STATE / 'HALT').exists():
            (C.STATE / 'HALT').write_text(f'spend cap reached: {m["spent"]} of {cap} at {rec["ts"]}\n')
            C.livelog('Spend reached the cap; the loop halts.', dry)
            try:
                C.notify_enqueue('halt', f'halt-spend-{run_id}', 'Critical Mass Unity loop stopped: it reached the '
                                 'spend cap you set. Nothing new starts until you raise it or close the run.')
                C.notify_flush(dry)
            except SystemExit as e:
                print(e, file=sys.stderr)
    elif action in ('stop-dispatch', 'halt'):
        print(f'[dry-run] would write {"HALT and " if action == "halt" else ""}STOP-DISPATCH', file=sys.stderr)
    return rec


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('action', choices=['check', 'watch'])
    ap.add_argument('--run-id')
    ap.add_argument('--cap', type=int)
    ap.add_argument('--projects-dir', default=str(PROJECTS))
    ap.add_argument('--interval', type=int, default=60)
    ap.add_argument('--dry-run', action='store_true')
    a = ap.parse_args(argv)
    run = C.read_json(C.STATE / 'run.json', {}) or {}
    cap = a.cap or run.get('cap')
    if a.action == 'check':
        rid = a.run_id or run.get('run_id')
        if not rid:
            print(json.dumps({'action': 'no-data', 'error': 'no run id (pass --run-id or record-launch first)'}))
            return 12
        rec = check(rid, cap, a.projects_dir, a.dry_run)
        print(json.dumps(rec))
        return {'ok': 0, 'stop-dispatch': 10, 'halt': 11}.get(rec['action'], 12)
    while True:  # watch mode is only used through watchers/watch.py spend, which owns the pidfile
        run = C.read_json(C.STATE / 'run.json', {}) or {}
        if run.get('run_id') and (a.cap or run.get('cap')):
            print(json.dumps(check(run['run_id'], a.cap or run.get('cap'), a.projects_dir, a.dry_run)), flush=True)
        time.sleep(a.interval)


if __name__ == '__main__':
    sys.exit(main())
