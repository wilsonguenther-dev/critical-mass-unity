#!/usr/bin/env python3
"""Loop watchers (decision 6): plain processes, no agents. Started by loop/bin/launch.sh, stopped by
loop/bin/shutdown.sh by PID (pidfile loop/state/pids/<name>.pid holds pid + command; never killed by pattern).

  watch.py stall  [--minutes 40] [--lock-max-min 60]   no gate result or commit for N minutes -> STALL + one halt text.
                                                         Quiet while the compile lock is held by a LIVE owner younger
                                                         than --lock-max-min (the integrator's own compile is not a
                                                         stall, R7). Also alarms if the launch ids are never recorded.
  watch.py memory [--max-gb 14]                         RSS by family: Unity editor and its workers, the Unity Hub
                                                         licensing client, node, git, the agent runtime. Reports only.
  watch.py clock                                        expected-by checkpoints from loop/state/schedule.json; marks
                                                         landed items LANDED (full-id check on origin/main), lists
                                                         slips of loop-owned items once per checkpoint in the live log
                                                         (never texted, never MISSED for a landed item, R4/R5);
                                                         at hard_stop writes STOP-DISPATCH.
  watch.py disk   [--floor-gb 15]                       free disk under the floor -> HALT + one halt text.
  watch.py spend                                        spend.py check every interval (STOP-DISPATCH 90 %, HALT 100 %).
Common: --interval S (default 60), --once, --dry-run (prints what it would write or text; writes no stop files).
A watcher exits by itself when its pidfile is gone or names another pid, or when loop/state/STOP-WATCHERS exists.
"""
import argparse
import json
import os
import re
import signal
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bin'))
import cmloop as C  # noqa: E402
import spend as S  # noqa: E402

# Process families. Names observed on this Mac during a batch-mode compile gate, 2026-11-12T04:10Z (ps -axo comm):
#   editor   /Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity
#   workers  .../Unity.app/Contents/Helpers/UnityAutoQuitter, .../Resources/Scripting/NetCoreRuntime/dotnet,
#            .../Resources/PackageManager/Server/UnityPackageManager,
#            .../Resources/BuildPipeline/Compilation/Unity.ILPP.Runner/Unity.ILPP.Runner
#            (bee_backend and UnityShaderCompiler also live under Unity.app; they run on script and shader compiles)
#   licensing  .../Unity.app/Contents/Helpers/UnityLicensingClient.app/.../Unity.Licensing.Client and the Hub's own
#              /Applications/Unity Hub.app/Contents/Frameworks/UnityLicensingClient_V1.app/.../Unity.Licensing.Client
# Unity Hub's Electron helpers and its chrome_crashpad_handler are NOT counted (they are not Chrome and not the loop).
FAMILIES = [
    ('unity-licensing', lambda c, b: b == 'Unity.Licensing.Client'),
    ('unity-editor', lambda c, b: c.endswith('/Unity.app/Contents/MacOS/Unity')),
    ('unity-workers', lambda c, b: '/Unity.app/Contents/' in c),
    ('node', lambda c, b: b == 'node'),
    ('git', lambda c, b: b in ('git', 'git-remote-https', 'git-lfs')),
    ('agent-runtime', lambda c, b: b == 'claude'),
]


def out(msg):
    print(f'{C.utc()} {msg}', flush=True)


def alarm(kind, key, text, sentence, a, stop_file='STALL'):
    if a.dry_run:
        out(f'WOULD ALARM ({kind}): {sentence}')
        return
    f = C.STATE / stop_file
    if f.exists():
        return
    f.write_text(json.dumps({'ts': C.utc(), 'kind': kind, 'detail': sentence}) + '\n')
    out(f'ALARM ({kind}): {sentence}')
    C.livelog(sentence)
    try:
        C.notify_enqueue('halt', key, text)
        out(f'notify: {C.notify_flush()}')
    except SystemExit as e:
        out(f'notify refused: {e}')


# ───────────────────────────── stall ─────────────────────────────
def last_activity():
    cands = []
    for p in (C.LOGS / 'gates.jsonl', C.STATE / 'activity', C.STATE / 'run.json'):
        if p.exists():
            cands.append(p.stat().st_mtime)
    code, o, _ = C.git('for-each-ref', '--format=%(committerdate:unix)', 'refs/heads/loop', 'refs/remotes/origin/loop',
                       'refs/remotes/origin/main')
    cands += [int(x) for x in o.split() if x.isdigit()]
    return max(cands) if cands else 0


def stall_tick(a, now=None):
    now = now or time.time()
    run = C.read_json(C.STATE / 'run.json', {}) or {}
    if run.get('status') == 'running' and not run.get('task_id'):
        started = run.get('started_epoch', now)
        if now - started > 600:
            alarm('launch-ids', f'halt-launchids-{run.get("started")}',
                  'Critical Mass Unity loop: the launch was never recorded, so it cannot be stopped cleanly. '
                  'Nothing new starts until it is.', 'The launch task id was not recorded within 10 minutes.', a)
            return 'alarm-launch-ids'
    ls = C.lock_status()
    if ls.get('held') and ls.get('alive') and ls.get('age_min', 0) < a.lock_max_min:
        out(f'quiet: compile lock held by {ls["owner"]} (pid {ls["pid"]}, alive) since {ls["started"]}; not a stall')
        return 'lock-held'
    quiet = (now - last_activity()) / 60
    if quiet >= a.minutes:
        why = (f'compile lock owner {ls.get("owner")} is dead' if ls.get('held') and not ls.get('alive') else
               f'compile lock held {ls.get("age_min")} min, over {a.lock_max_min}' if ls.get('held') else
               'no compile lock held')
        alarm('stall', f'halt-stall-{run.get("started")}',
              f'Critical Mass Unity loop looks stuck: no gate result or commit for {int(quiet)} minutes. '
              'Nothing new starts until someone looks.',
              f'No gate result or commit for {int(quiet)} minutes ({why}); the stall alarm stopped new work.', a)
        return 'alarm-stall'
    out(f'ok: last activity {quiet:.1f} min ago (alarm at {a.minutes})')
    return 'ok'


# ───────────────────────────── memory ─────────────────────────────
def sample():
    code, o, _ = C.sh(['ps', '-axo', 'pid=,rss=,comm='])
    fam = {f: {'rss_mb': 0, 'n': 0, 'names': set()} for f, _ in FAMILIES}
    for ln in o.splitlines():
        parts = ln.strip().split(None, 2)
        if len(parts) < 3 or not parts[1].isdigit():
            continue
        comm = parts[2]
        base = os.path.basename(comm)
        for f, test in FAMILIES:
            if test(comm, base):
                fam[f]['rss_mb'] += int(parts[1]) / 1024
                fam[f]['n'] += 1
                fam[f]['names'].add(base)
                break
    return {f: {'rss_mb': round(v['rss_mb']), 'n': v['n'], 'names': sorted(v['names'])[:8]} for f, v in fam.items()}


def memory_tick(a, state={}):
    s = sample()
    total_gb = sum(v['rss_mb'] for v in s.values()) / 1024
    over = total_gb > a.max_gb
    line = {'ts': C.utc(), 'total_gb': round(total_gb, 2), 'over': over,
            **{k: v['rss_mb'] for k, v in s.items()}}
    C.append_jsonl(C.LOGS / ('memory-dry.jsonl' if a.dry_run else 'memory.jsonl'), line)
    if over and not state.get('over'):
        msg = f'Memory watch: loop processes use {total_gb:.1f} GB, over {a.max_gb} GB.'
        out('ALERT ' + msg)
        C.livelog(msg, a.dry_run)
    state['over'] = over
    out(f'rss {total_gb:.2f} GB ' + ' '.join(f'{k}={v["rss_mb"]}MB/{v["n"]}' for k, v in s.items() if v['n']))
    return line


# ───────────────────────────── clock ─────────────────────────────
def clock_tick(a, now_iso=None):
    sched = C.read_json(C.STATE / 'schedule.json')
    if not sched:
        out('clock: no schedule.json; nothing to check')
        return 'no-schedule'
    st = C.read_json(C.STATE / 'clock-state.json', {'checkpoints': {}, 'landed': {}}) or {}
    now_iso = now_iso or C.utc()
    if not a.dry_run:
        C.git('fetch', '-q', 'origin', 'main')
    try:
        landed = C.landed_map('origin/main')
    except RuntimeError as e:
        out(f'clock: {e}')
        return 'git-error'
    items = C.load_items()
    for cp in sched.get('checkpoints', []):
        for iid in cp.get('items', []):
            if iid in landed and iid not in st['landed']:
                st['landed'][iid] = {'commit': landed[iid], 'seen': now_iso}
                out(f'clock: {C.title_of(iid, items)} ({iid}) LANDED at {landed[iid][:8]}')
        name = cp['name']
        if now_iso < cp['expected_by'] or name in st['checkpoints']:
            continue
        loop_items = [i for i in cp.get('items', []) if (items.get(i) or {}).get('owner', 'loop') == 'loop']
        slipped = [i for i in loop_items if i not in landed]
        status = 'LANDED' if not slipped else 'SLIPPED'
        st['checkpoints'][name] = {'status': status, 'at': now_iso, 'slipped': slipped}
        if slipped:
            C.livelog(f'Checkpoint "{name}" is due and {len(slipped)} item(s) have not landed: '
                      + ', '.join(f'{C.title_of(i, items)}' for i in slipped) + '.', a.dry_run)
        else:
            C.livelog(f'Checkpoint "{name}": everything due has landed.', a.dry_run)
        out(f'clock: checkpoint {name} {status} {slipped}')
    hs = sched.get('hard_stop')
    if hs and now_iso >= hs and not st.get('hard_stop_done'):
        if a.dry_run:
            out(f'WOULD write STOP-DISPATCH: hard stop {hs} reached')
        else:
            (C.STATE / 'STOP-DISPATCH').write_text(f'hard stop {hs} reached at {now_iso}\n')
            C.livelog('The hard stop time has come; no new work starts and the loop winds down.')
            st['hard_stop_done'] = now_iso
    if not a.dry_run:
        C.write_json(C.STATE / 'clock-state.json', st)
    return st


# ───────────────────────────── disk ─────────────────────────────
def disk_tick(a):
    g = C.free_gb()
    if g < a.floor_gb:
        alarm('disk', f'halt-disk-{C.utc()[:13]}',
              f'Critical Mass Unity loop stopped: the Mac is down to {g:.0f} GB free disk. Free some space and '
              'tell me to go on.', f'Disk is down to {g:.1f} GB free, under the {a.floor_gb} GB floor; the loop halts.',
              a, stop_file='HALT')
        return 'alarm-disk'
    out(f'disk ok: {g:.1f} GB free (floor {a.floor_gb})')
    return 'ok'


def spend_tick(a):
    run = C.read_json(C.STATE / 'run.json', {}) or {}
    if not run.get('run_id') or not run.get('cap'):
        out('spend: no run id recorded yet (record-launch); nothing to measure')
        return 'no-run'
    rec = S.check(run['run_id'], run['cap'], dry=a.dry_run)
    out(f'spend: {json.dumps(rec)}')
    return rec


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('name', choices=C.WATCHERS)
    ap.add_argument('--interval', type=int, default=60)
    ap.add_argument('--once', action='store_true')
    ap.add_argument('--dry-run', action='store_true')
    ap.add_argument('--minutes', type=float, default=40)
    ap.add_argument('--lock-max-min', type=float, default=60)
    ap.add_argument('--max-gb', type=float, default=14)
    ap.add_argument('--floor-gb', type=float, default=C.DISK_FLOOR_GB)
    a = ap.parse_args(argv)
    tick = {'stall': stall_tick, 'memory': memory_tick, 'clock': clock_tick, 'disk': disk_tick,
            'spend': spend_tick}[a.name]
    if a.once:
        tick(a)
        return 0
    me = os.getpid()
    pf = C.pidfile(a.name)
    pf.parent.mkdir(parents=True, exist_ok=True)
    C.write_json(pf, {'pid': me, 'name': a.name, 'script': str(Path(__file__).resolve()), 'started': C.utc(),
                      'dry': a.dry_run, 'argv': sys.argv[1:]})

    def bye(*_):
        d = C.read_json(pf) or {}
        if d.get('pid') == me:
            pf.unlink(missing_ok=True)
        out(f'{a.name} watcher pid {me} exiting')
        sys.exit(0)

    signal.signal(signal.SIGTERM, bye)
    signal.signal(signal.SIGINT, bye)
    out(f'{a.name} watcher pid {me} started (interval {a.interval}s, dry={a.dry_run})')
    while True:
        try:
            tick(a)
        except Exception as e:  # a watcher keeps watching; the error is logged
            out(f'{a.name} tick error: {type(e).__name__}: {e}')
        for _ in range(a.interval):
            d = C.read_json(pf) or {}
            if d.get('pid') != me or (C.STATE / 'STOP-WATCHERS').exists():
                bye()
            time.sleep(1)


if __name__ == '__main__':
    sys.exit(main())
