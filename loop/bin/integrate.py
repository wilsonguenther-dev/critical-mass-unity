#!/usr/bin/env python3
"""Integrate one batch (decision 3). The mechanical part of the integrator seat; the Opus integrator runs this.

  integrate.py --batch N --items ID1,ID2,... [--run-tag T] [--drop ID=reason ...]
               [--local] [--no-pr] [--items-dir DIR] [--gates compile,editmode,...] [--deadline-min 40]

Flow:
  1. Take the compile lock for the whole batch (owner integrator-batch-N, this pid, start time); the stall alarm reads it.
  2. Cut loop/int-<tag>-<N> from current origin/main in the integration worktree (~/critical-mass-unity-lanes/_int).
  3. Merge each item branch loop/<ID> in the given (stable) order. A merge conflict drops that item.
  4. Scope-check each item (git only). A scope violation drops that item.
  5. Run the gates in order, ONE compile per round. On a red gate, attribute it to an item from the compiler/test
     output (file paths in errors, failed test names vs tests_required, stack traces), DROP that item immediately
     (no fix attempts), and re-run the round with the rest. A red that names no item's file returns the whole batch
     uncounted and flags the architect. skipped gates (exit 78) are recorded as SKIPPED, never as passes.
  6. Green: push the integration branch, open ONE PR to main (the script refuses any other base), squash-merge it with
     a `loop-landed: <ID>` line per item, prove each id is landed on origin/main, queue one card, flush.
A dropped item's failure text goes to loop/state/failures/<ID>.md; it returns to the queue at most twice, then the
next red parks it for the architect (loop/state/parked/<ID>.json).
--local: use local branches, no fetch, no push, no PR (fixture dry runs). --no-pr: stop after the green round.
Prints one compact JSON summary (well under 40 KB); the full report goes to loop/logs/batch-<tag>-<N>.json.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cmloop as C  # noqa: E402

GATES_DIR = C.REPO / 'loop' / 'gates'
DEFAULT_GATES = ['compile', 'editmode', 'playmode-smoke', 'build-mac', 'parity', 'budgets']
PATH_RE = re.compile(r'((?:Assets|Packages)/[^\s:(]+?\.(?:cs|asmdef|shader|hlsl|uss|uxml|unity|prefab|asset))')


def run_gate(name, project, extra=(), env=None):
    p = subprocess.run([str(GATES_DIR / name), '--project', str(project), *extra], capture_output=True, text=True,
                       env=env)
    lines = [l for l in p.stdout.splitlines() if l.startswith('{')]
    rec = json.loads(lines[-1]) if lines else {'gate': name, 'status': 'fail', 'exit': p.returncode,
                                               'detail': (p.stderr or p.stdout).strip()[-300:] or 'no JSON line'}
    rec['exit'] = p.returncode  # the process's real code wins
    return rec


def item_files_map(ids, ref, base, cwd):
    return {i: set(C.changed_files(base, ref(i), cwd=cwd)) for i in ids}


def norm(path, project):
    s = str(path).replace(str(project).rstrip('/') + '/', '')
    m = PATH_RE.search(s)
    return m.group(1) if m else None


def attribute_paths(text_lines, files_by_item, project):
    """Map error lines to the item whose diff touched the file named in the line."""
    hits = {}
    for ln in text_lines:
        f = norm(ln, project)
        if not f:
            continue
        for iid, files in files_by_item.items():
            if f in files:
                hits.setdefault(iid, []).append(ln.strip())
    return hits


def attribute_tests(failed, items, files_by_item, project):
    hits = {}
    for t in failed:
        owner = None
        for iid in files_by_item:
            req = items[iid].get('tests_required') or []
            if any(t.get('fullname') == r or (t.get('fullname') or '').endswith('.' + r) or t.get('name') == r
                   for r in req):
                owner = iid
                break
        if owner is None:
            cls = (t.get('classname') or t.get('fullname') or '').split('.')[-1 if t.get('classname') else -2]
            for iid, files in files_by_item.items():
                for f in files:
                    if f.endswith('.cs') and (Path(project) / f).exists() and \
                            re.search(rf'\bclass\s+{re.escape(cls)}\b', (Path(project) / f).read_text(errors='replace')):
                        owner = iid
                        break
                if owner:
                    break
        if owner is None:
            st = attribute_paths((t.get('stack') or '').splitlines(), files_by_item, project)
            owner = next(iter(st), None)
        if owner:
            hits.setdefault(owner, []).append(f"test {t.get('fullname')} failed: {t.get('message', '')[:400]}")
    return hits


def read_lines(path):
    try:
        return Path(path).read_text(errors='replace').splitlines()
    except (FileNotFoundError, TypeError):
        return []


def attribute(rec, items, files_by_item, project):
    g = rec['gate']
    if g in ('compile', 'build-mac'):
        return attribute_paths(read_lines(rec.get('errors_file')), files_by_item, project)
    if g in ('editmode', 'playmode-smoke'):
        failed = C.read_json(rec.get('failed_file'), []) or []
        hits = attribute_tests(failed, items, files_by_item, project)
        if g == 'playmode-smoke' and not hits:
            hits = attribute_paths(read_lines(rec.get('exceptions_file')) + read_lines(rec.get('log')), files_by_item,
                                   project)
        return hits
    return {}


def ensure_int(int_dir, base_sha, branch):
    int_dir = Path(int_dir)
    if not int_dir.exists():
        int_dir.parent.mkdir(parents=True, exist_ok=True)
        code, out, err = C.git('worktree', 'add', '--detach', str(int_dir), base_sha)
        if code != 0:
            raise RuntimeError(f'worktree add {int_dir}: {err.strip()}')
        lib = C.MAIN / 'Library'
        if lib.exists() and not (int_dir / 'Library').exists():  # APFS clone: no extra disk, saves the full re-import
            subprocess.run(['cp', '-cR', str(lib), str(int_dir / 'Library')], capture_output=True)
    for args in (['reset', '-q', '--hard'], ['clean', '-fdq'], ['checkout', '-q', '-B', branch, base_sha],
                 ['reset', '-q', '--hard', base_sha]):
        code, out, err = C.git(*args, cwd=int_dir)
        if code != 0:
            raise RuntimeError(f'git {" ".join(args)} in {int_dir}: {err.strip()}')


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('--batch', required=True, type=int)
    ap.add_argument('--items', required=True)
    ap.add_argument('--run-tag', default='')
    ap.add_argument('--items-dir', default=str(C.ITEMS_DIR))
    ap.add_argument('--local', action='store_true')
    ap.add_argument('--no-pr', action='store_true')
    ap.add_argument('--gates', default=','.join(DEFAULT_GATES))
    ap.add_argument('--deadline-min', type=float, default=40)
    ap.add_argument('--int-dir', default=str(C.INT))
    ap.add_argument('--drop', action='append', default=[], help='ID=reason: the integrator attributes a red by hand')
    ap.add_argument('--max-returns', type=int, default=C.MAX_RETURNS)
    a = ap.parse_args(argv)
    t0 = time.time()
    ids = [i.strip() for i in a.items.split(',') if i.strip()]
    if len(ids) != len(set(ids)) or not ids or len(ids) > 6:
        print(json.dumps({'error': 'items must be 1 to 6 distinct ids'}))
        return 2
    tag = a.run_tag or C.utc().replace('-', '').replace(':', '')[:13]
    items = {i: C.load_item(Path(a.items_dir) / f'{i}.md') for i in ids}
    rec = C.lock_acquire(f'integrator-batch-{a.batch}', purpose=f'batch {a.batch}: {",".join(ids)}')
    if not rec:
        print(json.dumps({'error': 'compile lock held', 'lock': C.lock_status()}))
        return 75
    env = {**os.environ, 'CM_LOCK_TOKEN': rec['token']}
    try:
        res = run(a, ids, items, tag, env, t0)
    except Exception as e:
        res = {'batch': a.batch, 'result': 'error', 'error': f'{type(e).__name__}: {e}'}
    finally:
        C.lock_release(rec['token'])
    report = C.LOGS / f'batch-{tag}-{a.batch}.json'
    C.write_json(report, res)
    summary = {k: res.get(k) for k in ('batch', 'result', 'landed', 'pr', 'error', 'flags', 'skipped_gates')}
    summary['dropped'] = [{k: d[k] for k in ('id', 'gate', 'returns', 'parked', 'failure_file', 'counted')}
                          for d in res.get('dropped', [])]
    summary['report'] = str(report)
    summary['minutes'] = round((time.time() - t0) / 60, 1)
    print(json.dumps(summary))
    return 1 if res.get('result') == 'error' else 0


def run(a, ids, items, tag, env, t0):
    base = 'main' if a.local else 'origin/main'
    ref = (lambda i: f'loop/{i}') if a.local else (lambda i: f'origin/loop/{i}')
    if not a.local:
        C.git('fetch', '-q', 'origin', '--prune')
    base_sha = C.branch_sha(base)
    branch = f'loop/int-{tag}-{a.batch}'
    int_dir = Path(a.int_dir)
    gates = [g for g in a.gates.split(',') if g]
    active, dropped, rounds, flags, skipped = list(ids), [], [], [], set()

    def drop(iid, gate, text, counted=True):
        r = C.return_item(iid, items[iid].get('title', iid), f'{tag}-{a.batch}', gate, text, counted, a.max_returns)
        dropped.append({'id': iid, 'gate': gate, 'counted': counted, 'text': text[:2000], **r})
        if iid in active:
            active.remove(iid)
        if not a.local:  # the branch is pushed; the next builder gets a fresh lane on top of it
            C.lane_remove(iid)
        title = items[iid].get('title', iid)
        if r['parked']:
            C.livelog(f'"{title}" failed the {gate} gate again and is parked for the architect.')
        else:
            C.livelog(f'"{title}" was dropped from batch {a.batch} at the {gate} gate'
                      + (f' and goes back to the queue (return {r["returns"]} of {a.max_returns}).' if counted else
                         ' without counting against it.'))

    for spec in a.drop:
        iid, _, why = spec.partition('=')
        if iid in active:
            drop(iid, 'integrator', why or 'dropped by the integrator')
    for iid in list(active):
        if not C.branch_sha(ref(iid)):
            drop(iid, 'merge', f'branch {ref(iid)} does not exist (the builder never pushed it)', counted=False)

    green = False
    while active:
        if (time.time() - t0) / 60 > a.deadline_min:
            for iid in list(active):
                drop(iid, 'time-box', f'batch passed {a.deadline_min:.0f} minutes and was cut', counted=False)
            break
        rnd = {'round': len(rounds) + 1, 'items': list(active), 'gates': []}
        rounds.append(rnd)
        ensure_int(int_dir, base_sha, branch)
        for iid in list(active):
            code, out, err = C.git('merge', '--no-ff', '--no-edit', '-m', f'Merge {iid} into batch {a.batch}',
                                   ref(iid), cwd=int_dir)
            if code != 0:
                conf = C.git('diff', '--name-only', '--diff-filter=U', cwd=int_dir)[1].split()
                C.git('merge', '--abort', cwd=int_dir)
                drop(iid, 'merge', f'merge conflict with the batch in: {", ".join(conf) or (out + err).strip()[-300:]}')
        for iid in list(active):
            s = run_gate('scope', C.MAIN, ['--item', iid, '--ref', ref(iid), '--base', base_sha,
                                           '--items-dir', a.items_dir], env)
            rnd['gates'].append(s)
            if s['status'] != 'pass':
                drop(iid, 'scope', json.dumps(s))
        if not active:
            break
        if len(active) < len(rnd['items']):  # something left the batch before the compile: rebuild the tree
            rnd['rebuilt'] = True
            continue
        files_by_item = item_files_map(active, ref, base_sha, C.MAIN)
        red = False
        for g in gates:
            if g == 'parity':
                for iid in [i for i in active if items[i].get('art_row') not in (None, 'none')]:
                    r = run_gate('parity', int_dir, ['--item', iid, '--items-dir', a.items_dir], env)
                    rnd['gates'].append(r)
                    if r['status'] == 'skipped':
                        skipped.add(f'parity: {r["detail"]}')
                    elif r['status'] != 'pass':
                        drop(iid, 'parity', json.dumps(r))
                        red = True
                if red:
                    break
                continue
            r = run_gate(g, int_dir, env=env)
            rnd['gates'].append(r)
            if r['status'] == 'skipped' and r['exit'] == C.SKIP_EXIT:
                skipped.add(f'{g}: {r["detail"]}')
                continue
            if r['status'] in ('reported', 'flagged'):
                if r['status'] == 'flagged':
                    flags.append(r['detail'])
                continue
            if r['status'] == 'pass' and r['exit'] == 0:
                continue
            hits = attribute(r, items, files_by_item, int_dir)
            if not hits:
                flags.append(f'{g} red with no item attributed: {r.get("detail")}')
                for iid in list(active):
                    drop(iid, g, f'unattributed red at {g}: {json.dumps(r)}', counted=False)
                red = True
                break
            for iid, lines in hits.items():
                drop(iid, g, f'{g} gate red: {r.get("detail")}\n' + '\n'.join(lines[:40]))
            red = True
            break
        if not red:
            green = True
            break

    res = {'batch': a.batch, 'tag': tag, 'branch': branch, 'base': base_sha, 'rounds': rounds, 'dropped': dropped,
           'flags': flags, 'skipped_gates': sorted(skipped), 'landed': [], 'green_items': list(active) if green else []}
    if not green or not active:
        res['result'] = 'all-dropped'
        return res
    if a.local or a.no_pr:
        res['result'] = 'green-local' if a.local else 'green-no-pr'
        return res
    # ── land: one PR to main, squash, prove ──
    titles = [f'- {items[i].get("title")} ({i})' for i in active]
    markers = [f'loop-landed: {i}' for i in active]
    gate_lines = [json.dumps({k: g.get(k) for k in ('gate', 'status', 'exit', 'detail')}) for g in rounds[-1]['gates']]
    body = '\n'.join([f'Batch {a.batch} ({tag}): code, tests and docs together.', '', *titles, '',
                      'Gates (last round):', *gate_lines, '',
                      *([f'SKIPPED (not a pass): {s}' for s in sorted(skipped)]), '',
                      *([f'Dropped: {items[d["id"]].get("title")} ({d["id"]}) at {d["gate"]}' for d in dropped]),
                      '', *markers, f'loop-batch: {tag}-{a.batch}', ''])
    bf = C.LOGS / f'pr-body-{tag}-{a.batch}.md'
    bf.write_text(body)
    code, out, err = C.git('push', '-q', '-u', 'origin', branch, cwd=int_dir)
    if code != 0:
        res.update(result='error', error=f'push {branch}: {err.strip()}')
        return res
    pr = C.pr_open(branch, f'loop batch {a.batch}: ' + ', '.join(items[i].get('title', i) for i in active)[:200], bf)
    if not pr['ok']:
        res.update(result='error', error=pr['error'])
        return res
    res['pr'] = pr['number']
    mg = C.pr_merge(pr['number'], f'loop batch {a.batch} ({len(active)} items) (#{pr["number"]})', bf)
    if not mg['ok']:
        res.update(result='error', error=f'merge: {mg}')
        return res
    lm = C.landed_map('origin/main')
    res['landed'] = [i for i in active if lm.get(i) == mg['merge_commit']]
    res['result'] = 'landed' if len(res['landed']) == len(active) else 'error'
    if res['result'] == 'error':
        res['error'] = f'merged but not proven landed: {sorted(set(active) - set(res["landed"]))}'
    for i in res['landed']:
        C.lane_remove(i)
        C.git('push', '-q', 'origin', '--delete', f'loop/{i}')
    C.livelog(f'Batch {a.batch} landed on main in PR #{pr["number"]}: '
              + ', '.join(f'"{items[i].get("title")}"' for i in res['landed']) + '.')
    if res['landed']:
        C.notify_enqueue('batch-landed', f'batch-{tag}-{a.batch}', C.batch_card(a.batch, res['landed'], items,
                                                                                 f'#{pr["number"]}'))
        res['notify'] = C.notify_flush()
    return res


if __name__ == '__main__':
    sys.exit(main())
