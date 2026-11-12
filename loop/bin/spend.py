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
import re
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


# ───────────────────────────── per-batch cost report (U-040) ─────────────────────────────
SEAT_RES = [('builder', re.compile(r'You are BUILDER for item (\S+) \("[^"]*"\) in batch (\d+)')),
            ('integrator', re.compile(r'You are the INTEGRATOR for batch (\d+)')),
            ('fixer', re.compile(r'You are the FIX SEAT for coupled batch (\d+)')),
            ('reviewer', re.compile(r'You are the REVIEWER for batch (\d+)'))]
BASELINE_BATCH_1 = {'run_id': 'wf_403d1691-a59', 'agents': 7, 'subagent_tokens': 808397, 'tool_uses': 162,
                    'wall_minutes': 17.6, 'landed_items': 5, 'tokens_per_landed_item': 161679,
                    'source': 'the workflow runtime report for batch 1 (PR 1, 579a53f), 2026-11-12'}


def seat_of(transcript):
    """(kind, batch) of one agent transcript, read from its first user message (the seat prompt)."""
    with open(transcript, errors='replace') as fh:
        for line in fh:
            try:
                o = json.loads(line)
            except ValueError:
                continue
            if o.get('type') != 'user':
                continue
            c = (o.get('message') or {}).get('content')
            text = c if isinstance(c, str) else ' '.join(x.get('text', '') for x in c or [] if isinstance(x, dict))
            for kind, rx in SEAT_RES:
                m = rx.search(text)
                if m:
                    return kind, int(m.groups()[-1])
            return 'other', None
    return 'other', None


def batch_report(run_id, batch, tag='', projects=PROJECTS, write=True):
    """Tokens per seat kind and rounds for one batch of a run, so the cost of the next batch can be extrapolated.
    Writes loop/logs/batch-report-<run>-<batch>.json and .md and one live-log sentence."""
    kinds = {}
    for f in find_transcripts(run_id, projects):
        kind, b = seat_of(f)
        if b != batch:
            continue
        t = transcript_tokens([f])
        k = kinds.setdefault(kind, {'seats': 0, 'counted': 0, 'output': 0, 'cache_read': 0})
        k['seats'] += 1
        k['counted'] += t['counted']
        k['output'] += t['output']
        k['cache_read'] += t['cache_read']
    ints = [r for r in (json.loads(l) for l in (C.LOGS / 'integrations.jsonl').read_text().splitlines() if l.strip())
            if r.get('batch') == batch and (not tag or r.get('tag') == tag)] if (C.LOGS / 'integrations.jsonl').exists() else []
    total = sum(k['counted'] for k in kinds.values())
    rep = {'ts': C.utc(), 'run_id': run_id, 'batch': batch, 'tag': tag, 'by_seat_kind': kinds,
           'counted_tokens': total, 'integrator_runs': len(ints), 'compile_rounds': sum(r.get('rounds', 0) for r in ints),
           'fix_rounds': kinds.get('fixer', {}).get('seats', 0), 'results': [r.get('result') for r in ints],
           'per_builder_seat': (kinds['builder']['counted'] // kinds['builder']['seats']) if kinds.get('builder') else None,
           'baseline': BASELINE_BATCH_1}
    if write:
        base = C.LOGS / f'batch-report-{run_id}-{batch}'
        C.write_json(base.with_suffix('.json'), rep)
        lines = [f'# Batch {batch} cost report ({run_id})', '', f'Written {rep["ts"]} (date -u). Counted = input + output + '
                 'cache creation (U-014 measure; cache reads listed apart).', '', '| Seat kind | Seats | Counted tokens | Output | Cache read |',
                 '| --- | --- | --- | --- | --- |']
        lines += [f'| {k} | {v["seats"]} | {v["counted"]} | {v["output"]} | {v["cache_read"]} |' for k, v in sorted(kinds.items())]
        lines += ['', f'Integrator runs {rep["integrator_runs"]}, compile rounds {rep["compile_rounds"]}, fix rounds '
                  f'{rep["fix_rounds"]}; results {rep["results"]}.', '',
                  f'Baseline (batch 1, measured): {BASELINE_BATCH_1["agents"]} agents, {BASELINE_BATCH_1["subagent_tokens"]} '
                  f'subagent tokens, {BASELINE_BATCH_1["tool_uses"]} tool uses, {BASELINE_BATCH_1["wall_minutes"]} min, about '
                  f'{BASELINE_BATCH_1["tokens_per_landed_item"]} tokens per landed item.', '']
        base.with_suffix('.md').write_text('\n'.join(lines))
        C.livelog(f'Batch {batch} cost: {total} counted tokens over {sum(v["seats"] for v in kinds.values())} seats '
                  f'({", ".join(f"{k} {v['seats']}" for k, v in sorted(kinds.items()))}); {rep["compile_rounds"]} compile '
                  f'round(s), {rep["fix_rounds"]} fix round(s).')
    return rep


def missing_batch_reports(run_id, projects=PROJECTS):
    """Shutdown: write the report of every batch that ran an integrator in this run and has none yet."""
    seen = set()
    if (C.LOGS / 'integrations.jsonl').exists():
        for l in (C.LOGS / 'integrations.jsonl').read_text().splitlines():
            try:
                r = json.loads(l)
            except ValueError:
                continue
            seen.add(int(r['batch']))
    out = []
    for b in sorted(seen):
        if not (C.LOGS / f'batch-report-{run_id}-{b}.json').exists():
            batch_report(run_id, b, projects=projects)
            out.append(b)
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('action', choices=['check', 'watch', 'batch-report'])
    ap.add_argument('--batch', type=int)
    ap.add_argument('--tag', default='')
    ap.add_argument('--run-id')
    ap.add_argument('--cap', type=int)
    ap.add_argument('--projects-dir', default=str(PROJECTS))
    ap.add_argument('--interval', type=int, default=60)
    ap.add_argument('--dry-run', action='store_true')
    a = ap.parse_args(argv)
    run = C.read_json(C.STATE / 'run.json', {}) or {}
    cap = a.cap or run.get('cap')
    if a.action == 'batch-report':
        rid = a.run_id or run.get('run_id')
        if not rid or a.batch is None:
            print(json.dumps({'error': 'batch-report needs --batch and a run id'}))
            return 2
        rep = batch_report(rid, a.batch, a.tag, a.projects_dir, not a.dry_run)
        print(json.dumps({k: rep[k] for k in ('batch', 'counted_tokens', 'compile_rounds', 'fix_rounds')}))
        return 0
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
