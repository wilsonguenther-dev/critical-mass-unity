#!/usr/bin/env python3
"""Critical Mass (Unity) build loop: shared library and small CLI. Python 3 stdlib only.

Every path, lock, item parser, landed check, lane, live-log line and text the loop uses goes through this file,
so there is exactly one implementation of each rule. Run `cmloop.py -h` for the subcommands.

State lives in <main checkout>/loop/state/ and logs in <main checkout>/loop/logs/ (both git-ignored). Overrides for
tests and dry runs: CM_LOOP_MAIN, CM_LOOP_STATE, CM_LOOP_LOGS, CM_LOOP_LANES, CM_LOOP_LIVELOG_DIR.
"""
import argparse
import datetime as dt
import fcntl
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path, PurePosixPath

HERE = Path(__file__).resolve()
REPO = HERE.parents[2]  # the checkout holding this copy of the harness


def _main_root():
    try:
        out = subprocess.run(['git', '-C', str(REPO), 'rev-parse', '--path-format=absolute', '--git-common-dir'],
                             capture_output=True, text=True, check=True).stdout.strip()
        return Path(out).parent
    except Exception:
        return REPO


MAIN = Path(os.environ.get('CM_LOOP_MAIN') or _main_root())
LOOP = MAIN / 'loop'
LOGS = Path(os.environ.get('CM_LOOP_LOGS') or LOOP / 'logs')
STATE = Path(os.environ.get('CM_LOOP_STATE') or LOOP / 'state')
LANES = Path(os.environ.get('CM_LOOP_LANES') or Path.home() / 'critical-mass-unity-lanes')
INT = LANES / '_int'
ITEMS_DIR = LOOP / 'items'
LIVELOG_DIR = Path(os.environ.get('CM_LOOP_LIVELOG_DIR')
                   or Path.home() / 'Obsidian/Wilson-Brain/Projects/Kaiju-Demo/Loop-Logs')
SLUG = 'wilsonguenther-dev/critical-mass-unity'
SEND_TEXT = Path.home() / 'bin/claude-chat/send-text.sh'  # Wilson's own thread only (chat 76); no route file, no group
LICENSE_FILES = [Path('/Library/Application Support/Unity/Unity_lic.ulf')]
DATA_VOLUME = '/System/Volumes/Data' if os.path.isdir('/System/Volumes/Data') else '/'
DISK_FLOOR_GB = 15
LAUNCH_DISK_GB = 25
MAX_RETURNS = 2
MAX_BUILDERS_CEILING = 12  # U-037: owner raised the machine-wide agent limit for this loop to 12
DEFAULT_BUILDERS = 5
MAX_BATCH_ITEMS = 12
SKIP_EXIT = 78  # gates: "skipped" exits 78, never 0, so no caller can read a skip as a pass
WATCHERS = ['stall', 'memory', 'clock', 'disk', 'spend']
REQUIRED = ['id', 'title', 'phase', 'owner', 'waits_on', 'files_allowed', 'files_forbidden', 'tests_required',
            'pass_check', 'art_row']
LIST_FIELDS = {'waits_on', 'files_allowed', 'files_forbidden', 'tests_required'}
GUARDED_SAMPLES = ['ProjectSettings/ProjectSettings.asset', 'ProjectSettings/ProjectVersion.txt',
                   'Packages/manifest.json', 'Packages/packages-lock.json',
                   'Assets/_Game/Settings/Controls.inputactions']
ID_RE = re.compile(r'^[A-Z][A-Z0-9]*(-[A-Z0-9]+)+[a-z]?$')  # F-01, CM-30, F-02a


# ───────────────────────────── basics ─────────────────────────────
def utc():
    return dt.datetime.now(dt.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')


def sh(cmd, cwd=None, env=None, timeout=None):
    """Run a command bare (no pipe); return (exit code, stdout, stderr). The exit code is the command's own."""
    try:
        p = subprocess.run(cmd, cwd=cwd, env=env, capture_output=True, text=True, timeout=timeout)
        return p.returncode, p.stdout, p.stderr
    except FileNotFoundError as e:
        return 127, '', str(e)
    except subprocess.TimeoutExpired as e:
        return 124, e.stdout or '', f'timeout after {timeout}s'


def git(*a, cwd=None):
    return sh(['git', '-C', str(cwd or MAIN), *a])


def read_json(p, default=None):
    try:
        return json.loads(Path(p).read_text())
    except Exception:
        return default


def write_json(p, obj):
    p = Path(p)
    p.parent.mkdir(parents=True, exist_ok=True)
    tmp = p.with_name(p.name + '.tmp')
    tmp.write_text(json.dumps(obj, indent=2, sort_keys=True) + '\n')
    tmp.replace(p)


def append_jsonl(p, obj):
    p = Path(p)
    p.parent.mkdir(parents=True, exist_ok=True)
    with open(p, 'a') as f:
        f.write(json.dumps(obj, sort_keys=True) + '\n')


def pid_alive(pid):
    try:
        pid = int(pid)
        if pid <= 0:
            return False
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    except (TypeError, ValueError):
        return False


def pid_command(pid):
    code, out, _ = sh(['ps', '-p', str(pid), '-o', 'command='])
    return out.strip() if code == 0 else ''


def free_gb(path=DATA_VOLUME):
    return shutil.disk_usage(path).free / (1024 ** 3)


def is_guarded(path):
    return path.startswith('ProjectSettings/') or path.startswith('Packages/') or path.endswith('Controls.inputactions')


# ── U-027 / U-030: Unity data files are generated, never hand-written ──
UNITY_DATA_EXT = ('.meta', '.unity', '.prefab', '.asset', '.mat')
GENERATED_DIRS = ('Assets/_Game/Settings/', 'Assets/_Game/Resources/', 'ProjectSettings/')
# The one sentence on order (U-030). integrate.py and the workflow's integrator prompt both print exactly this.
SETUP_ORDER = ('CriticalMass.Editor.ProjectSetup.Apply runs on the integration branch after every item branch of the '
               'batch is merged and before the compile gate, on every batch (it is idempotent); nothing runs Unity on '
               'that branch before it.')


def hand_written_unity_data(path, approved=None):
    """Why a builder's branch may not carry this file (U-027), or None. Controls.inputactions (plain JSON) and its
    meta are the exception; a ProjectSettings/*.asset change needs architect_approved (the guarded-path rule)."""
    if path.endswith('Controls.inputactions') or path.endswith('Controls.inputactions.meta'):
        return None
    if path.startswith('ProjectSettings/'):
        return None  # the guarded-path rule already requires architect_approved for every ProjectSettings/ change
    if path.endswith(UNITY_DATA_EXT):
        ext = path.rsplit('.', 1)[-1]
        return (f'hand-written .{ext} (U-027): builders never author .meta, .unity, .prefab, .asset or .mat files and '
                f'never choose GUIDs; Unity generates metas on the integrator\'s import and editor-made assets come '
                f'from CriticalMass.Editor.ProjectSetup.Apply. Delete it from the branch')
    return None


def dirty_files(cwd):
    """[(xy, path)] from git status, every untracked file listed (no collapsed directories)."""
    out = git('status', '--porcelain', '--untracked-files=all', cwd=cwd)[1]
    rows = []
    for ln in out.splitlines():
        if len(ln) > 3:
            path = ln[3:].split(' -> ')[-1].strip('"')
            rows.append((ln[:2], path))
    return rows


def classify_generated(after_setup, after_compile):
    """U-030: which dirty files the harness commits. Accepted: every *.meta under Assets/ (any status: Unity adds and
    removes them on import), and the files ProjectSetup.Apply changed under GENERATED_DIRS (seen dirty right after
    the setup gate). Everything else dirty is rejected (Unity or a gate wrote something outside the generated set)."""
    setup_paths = {p for _, p in after_setup}
    accept, reject = [], []
    for xy, p in after_compile:
        if p.startswith('Assets/') and p.endswith('.meta'):
            accept.append(p)
        elif p in setup_paths and p.startswith(GENERATED_DIRS):
            accept.append(p)
        else:
            reject.append(f'{xy.strip() or "?"} {p}')
    return sorted(accept), sorted(reject)


def glob_match(path, pattern):
    p = PurePosixPath(path)
    try:
        return p.full_match(pattern)  # Python 3.13+: supports **
    except AttributeError:
        import fnmatch
        return fnmatch.fnmatch(path, pattern)


# ───────────────────────────── items ─────────────────────────────
def _scalar(v):
    v = v.strip()
    if len(v) >= 2 and v[0] == v[-1] and v[0] in '"\'':
        v = v[1:-1]
    return v


def parse_item(path):
    """Return (front-matter dict, body text, parse errors). Front-matter: `key: value`, `key: [a, b]` or a block list."""
    text = Path(path).read_text()
    lines = text.splitlines()
    errors, fm = [], {}
    if not lines or lines[0].strip() != '---':
        return {}, text, ['no front-matter: the file must start with a line ---']
    i, cur = 1, None
    while i < len(lines) and lines[i].strip() != '---':
        ln = lines[i]
        if re.match(r'^\s+-\s+', ln) and cur:
            if not isinstance(fm.get(cur), list):
                fm[cur] = []
            fm[cur].append(_scalar(ln.split('-', 1)[1]))
        elif ln.strip() and not ln.lstrip().startswith('#'):
            m = re.match(r'^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$', ln)
            if not m:
                errors.append(f'line {i + 1}: not "key: value": {ln.strip()!r}')
            else:
                k, v = m.group(1), m.group(2).strip()
                cur = k
                if k in fm:
                    errors.append(f'duplicate field {k}')
                if v.startswith('['):
                    if not v.endswith(']'):
                        errors.append(f'{k}: list not closed with ]')
                        fm[k] = []
                    else:
                        fm[k] = [_scalar(x) for x in v[1:-1].split(',') if x.strip()]
                elif v == '':
                    fm[k] = []
                else:
                    fm[k] = _scalar(v)
        i += 1
    if i >= len(lines):
        errors.append('front-matter not closed with a line ---')
    body = '\n'.join(lines[i + 1:])
    return fm, body, errors


def method_text(body):
    """The item's method (architect ruling 1, 2026-11-12): every `##` section of the body except `## Out of scope`,
    which lists what NOT to build and may name alternatives. No `## Method` heading is required. None when empty."""
    parts = re.split(r'^(##\s+.*)$', body, flags=re.M)
    keep, cur = [], None
    for chunk in parts:
        if re.match(r'^##\s+', chunk):
            cur = chunk
            continue
        if cur is not None and not re.match(r'^##\s+out of scope\b', cur, re.I):
            keep.append(chunk)
    return '\n'.join(keep) if keep else None


def section(body, name):
    m = re.search(rf'^##\s+{re.escape(name)}\s*$(.*?)(?=^##\s|\Z)', body, re.M | re.S | re.I)
    return m.group(1) if m else None


def load_item(path):
    fm, body, errors = parse_item(path)
    fm = dict(fm)
    fm['_path'] = str(path)
    fm['_body'] = body
    fm['_parse_errors'] = errors
    return fm


def item_files(items_dir=None, include_fixtures=False):
    d = Path(items_dir or ITEMS_DIR)
    out = []
    for p in sorted(d.glob('*.md')):
        if not p.name.startswith('_'):  # _CONVENTIONS.md, _OPEN.md: shared notes, not items
            out.append(p)
    if include_fixtures:
        out += sorted((d / '_fixtures').glob('*.md'))
    return out


def natural_key(item_id):
    return [int(t) if t.isdigit() else t for t in re.split(r'(\d+)', item_id)]


def load_items(items_dir=None):
    items = {}
    for p in item_files(items_dir):
        it = load_item(p)
        if it.get('id'):
            items[it['id']] = it
    return items


def title_of(item_id, items):
    it = items.get(item_id) or {}
    return it.get('title') or item_id


# ───────────────────────────── compile lock ─────────────────────────────
def lock_path():
    return STATE / 'COMPILE.lock'


def lock_status():
    d = read_json(lock_path())
    if not d:
        return {'held': False}
    alive = pid_alive(d.get('pid'))
    age = (time.time() - float(d.get('started_epoch', time.time()))) / 60
    return {'held': True, 'alive': alive, 'age_min': round(age, 1), **{k: d.get(k) for k in
            ('owner', 'pid', 'started', 'purpose')}}


def lock_acquire(owner, pid=None, purpose=''):
    """One Unity process at a time across the session. A lock whose recorded pid is dead is reclaimed (R14)."""
    if owner.startswith('builder'):
        raise SystemExit('lock: builders never take the compile lock (they never run Unity)')
    STATE.mkdir(parents=True, exist_ok=True)
    rec = {'owner': owner, 'pid': int(pid or os.getpid()), 'started': utc(), 'started_epoch': time.time(),
           'token': f'{owner}-{os.getpid()}-{time.time_ns()}', 'purpose': purpose}
    for _ in range(2):
        try:
            fd = os.open(lock_path(), os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
            with os.fdopen(fd, 'w') as f:
                f.write(json.dumps(rec, indent=2) + '\n')
            return rec
        except FileExistsError:
            d = read_json(lock_path()) or {}
            if not pid_alive(d.get('pid')):
                append_jsonl(LOGS / 'events.jsonl', {'ts': utc(), 'event': 'lock-reclaimed', 'dead': d})
                try:
                    lock_path().unlink()
                except FileNotFoundError:
                    pass
                continue
            return None
    return None


def lock_release(token):
    d = read_json(lock_path())
    if d and d.get('token') == token:
        lock_path().unlink()
        return True
    return False


def unity_open_on(project):
    """Unity editors (GUI or batch) that already have this project open. Read-only process listing."""
    code, out, _ = sh(['ps', '-axo', 'pid=,command='])
    hits = []
    proj = str(Path(project).resolve())
    for ln in out.splitlines():
        pid, _, cmd = ln.strip().partition(' ')
        if '/Unity.app/Contents/MacOS/Unity' in cmd and re.search(r'-projectpath\s+"?' + re.escape(proj) + r'"?(\s|$)',
                                                                   cmd, re.I):
            hits.append({'pid': int(pid), 'command': cmd[:200]})
    return hits


# ───────────────────────────── landed (full id, ancestor of origin/main, not reverted) ─────────────────────────────
def landed_map(ref='origin/main', cwd=None):
    """id -> landing commit. Only commits reachable from `ref` are read, so every hit is an ancestor of it.
    Markers are whole lines `loop-landed: <ID>` matched by full token (CM-4 never matches CM-40, R5).
    A later `loop-reverted: <ID>` line or a `This reverts commit <sha>` of the landing commit un-lands it."""
    code, out, err = git('log', ref, '--format=%H%x00%B%x1e', cwd=cwd)
    if code != 0:
        raise RuntimeError(f'git log {ref} failed: {err.strip()}')
    recs = [r for r in out.split('\x1e') if '\x00' in r]
    state = {}
    for rec in reversed(recs):  # oldest first
        sha, body = rec.lstrip('\n').split('\x00', 1)
        for m in re.finditer(r'This reverts commit ([0-9a-f]{7,40})', body):
            for k, v in list(state.items()):
                if v and v.startswith(m.group(1)):
                    state[k] = None
        for m in re.finditer(r'^loop-reverted:\s*(\S+)\s*$', body, re.M):
            state[m.group(1)] = None
        for m in re.finditer(r'^loop-landed:\s*(\S+)\s*$', body, re.M):
            state[m.group(1)] = sha
    return {k: v for k, v in state.items() if v}


# ───────────────────────────── returns / parked ─────────────────────────────
def returns_read():
    return read_json(STATE / 'returns.json', {}) or {}


def parked_ids():
    return sorted(p.stem for p in (STATE / 'parked').glob('*.json'))


def return_item(item_id, title, batch, gate, text, counted=True, max_returns=MAX_RETURNS):
    """First-red drop (R3): the item leaves the batch with its failure text; at most `max_returns` returns, the next
    red parks it for the architect. Returns {'returns': n, 'parked': bool, 'failure_file': path}."""
    fdir = STATE / 'failures'
    fdir.mkdir(parents=True, exist_ok=True)
    ff = fdir / f'{item_id}.md'
    with open(ff, 'a') as f:
        f.write(f'\n## {utc()} batch {batch}, gate {gate}{"" if counted else " (not counted)"}\n\n```\n'
                f'{text.strip()[:6000]}\n```\n')
    r = returns_read()
    if counted:
        r[item_id] = int(r.get(item_id, 0)) + 1
        write_json(STATE / 'returns.json', r)
    n = int(r.get(item_id, 0))
    parked = counted and n > max_returns
    if parked:
        write_json(STATE / 'parked' / f'{item_id}.json',
                   {'id': item_id, 'title': title, 'parked': utc(), 'reds': n, 'failure_file': str(ff)})
    return {'returns': n, 'parked': parked, 'failure_file': str(ff)}


# ───────────────────────────── live log (Obsidian) ─────────────────────────────
def livelog(sentence, dry=False):
    ts = utc()
    f = LIVELOG_DIR / f'unity-{ts[:10]}.md'
    line = f'- {ts[11:19]}Z {sentence.strip()}'
    LOGS.mkdir(parents=True, exist_ok=True)
    with open(LOGS / ('live-dry.log' if dry else 'live.log'), 'a') as m:
        m.write(f'{ts} {sentence.strip()}\n')
    if dry:
        print(f'[dry-run livelog] would append to {f}: {line}')
        return str(f)
    f.parent.mkdir(parents=True, exist_ok=True)
    new = not f.exists()
    with open(f, 'a') as o:
        if new:
            o.write(f'# Critical Mass Unity loop: live log {ts[:10]}\n\nOne plain sentence per event, '
                    f'stamped from the clock (UTC). Written by loop/bin/cmloop.py livelog.\n\n')
        o.write(line + '\n')
    return str(f)


# ───────────────────────────── ledger: every item's state, once (U-042) ─────────────────────────────
TERMINAL = ('landed', 'dropped', 'parked')


def ledger_path():
    return LOGS / 'ledger.jsonl'  # loop/logs is git-ignored and never wiped by launch or shutdown


def run_key():
    run = read_json(STATE / 'run.json') or {}
    return run.get('run_id') or (f"started-{run['started']}" if run.get('started') else 'no-run')


def ledger_rows(run=None):
    f = ledger_path()
    rows = []
    if f.exists():
        for ln in f.read_text().splitlines():
            try:
                r = json.loads(ln)
            except ValueError:
                continue
            if run is None or r.get('run') == run:
                rows.append(r)
    return rows


def item_state(iid, title, state, detail='', batch='', sentence=None, final=False):
    """Record one state change of an item: a ledger row and, when given, one live-log sentence. The same (run, item,
    state, batch, final) is recorded once only, so a retried call never logs twice. Returns True when written."""
    run = run_key()
    for r in ledger_rows(run):
        if (r['id'], r['state'], str(r.get('batch', '')), bool(r.get('final'))) == (iid, state, str(batch), final):
            return False
    append_jsonl(ledger_path(), {'ts': utc(), 'run': run, 'id': iid, 'title': title, 'state': state,
                                 'detail': str(detail)[:600], 'batch': str(batch), 'final': final})
    if sentence:
        livelog(sentence)
    return True


def ledger_close(stop_reason='', dry=False):
    """Run end: every loop item this run was given ends in exactly one of landed (with its loop-landed commit),
    dropped (with its failure file) or parked (with the reason). Items never reached are parked with the stop reason,
    so no item is left without a record. Returns the final rows written."""
    run = run_key()
    args = read_json(LOGS / 'launch-args.json') or {}
    in_run = {i for b in args.get('batches') or [] for i in b.get('items', [])}
    ids = [r['id'] for r in args.get('items') or [] if r.get('owner') == 'loop' and (not in_run or r['id'] in in_run)
           and r['id'] not in set(args.get('landed') or [])]
    try:
        lm = landed_map()
    except RuntimeError:
        lm = {}
    rows = ledger_rows(run)
    items = load_items()
    out = []
    for iid in ids:
        if any(r['id'] == iid and r.get('final') for r in rows):
            continue
        title = title_of(iid, items)
        parked = STATE / 'parked' / f'{iid}.json'
        question = LOGS / 'questions' / f'{iid}.md'
        failure = STATE / 'failures' / f'{iid}.md'
        if iid in lm:
            st, det, s = 'landed', f'loop-landed: {iid} in {lm[iid]}', f'"{title}" ended the run landed on main.'
        elif parked.exists():
            st, det, s = 'parked', f'parked for the architect: {parked}', f'"{title}" ended the run parked for the architect.'
        elif question.exists():
            st, det, s = 'parked', f'open question: {question}', f'"{title}" ended the run parked on an open question.'
        elif any(r['id'] == iid and r['state'] == 'dropped' for r in rows) and failure.exists():
            st, det, s = 'dropped', f'failure text: {failure}', f'"{title}" ended the run dropped; its failure text is kept.'
        else:
            why = stop_reason or 'the run stopped before it'
            st, det, s = 'parked', f'not reached in run {run}: {why}', f'"{title}" was not reached this run ({why}).'
        if dry:
            print(f'[dry-run ledger] {iid}: {st} ({det})')
        elif item_state(iid, title, st, det, 'final', s, final=True):
            out.append({'id': iid, 'state': st, 'detail': det})
    return out


def ledger_audit(run=None):
    """Problems with the run's record: an item without exactly one final row, a landed row whose commit is not the
    item's loop-landed commit on origin/main, a dropped row without its failure file, a parked row without a reason,
    and a non-final state recorded twice."""
    run = run or run_key()
    rows = ledger_rows(run)
    probs = []
    try:
        lm = landed_map()
    except RuntimeError:
        lm = None
    finals = {}
    seen = {}
    for r in rows:
        key = (r['id'], r['state'], r.get('batch'), bool(r.get('final')))
        seen[key] = seen.get(key, 0) + 1
        if r.get('final'):
            finals.setdefault(r['id'], []).append(r)
    probs += [f'{k[0]} recorded {k[1]} {n} times' for k, n in seen.items() if n > 1]
    ids = {r['id'] for r in rows}
    for iid in sorted(ids, key=natural_key):
        fs = finals.get(iid, [])
        if len(fs) != 1:
            probs.append(f'{iid} has {len(fs)} final rows (needs exactly 1)')
            continue
        f = fs[0]
        if f['state'] not in TERMINAL:
            probs.append(f'{iid} final state {f["state"]!r} is not landed, dropped or parked')
        elif f['state'] == 'landed' and lm is not None and not (lm.get(iid) and lm[iid] in f['detail']):
            probs.append(f'{iid} is recorded landed but origin/main has no loop-landed line for it at that commit')
        elif f['state'] == 'dropped' and not (STATE / 'failures' / f'{iid}.md').exists():
            probs.append(f'{iid} is recorded dropped but its failure file is missing')
        elif f['state'] == 'parked' and not f.get('detail'):
            probs.append(f'{iid} is recorded parked without a reason')
    return probs


# ───────────────────────────── texting: one digest sender ─────────────────────────────
KINDS = {'batch-landed', 'halt'}
TOKEN_RE = re.compile(r'(?<![A-Za-z0-9-])[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+[a-z]?(?![A-Za-z0-9-])')


def card_problems(text, titles=()):
    """A card is plain English: every item id may appear only as '(ID)' right after its title. Ids that are part of an
    item's own title (F-03's "with the CM-31 decoration data") are words of that title, not raw ids (U-048)."""
    probs = []
    for ti in sorted({x for x in titles if x}, key=len, reverse=True):
        text = text.replace(ti, 'T' * len(ti))
    if not text.strip():
        probs.append('empty text')
    if len(text) > 700:
        probs.append(f'{len(text)} chars (cap 700)')
    for m in TOKEN_RE.finditer(text):
        s, e = m.span()
        if not (s > 0 and text[s - 1] == '(' and e < len(text) and text[e] == ')'):
            probs.append(f'raw id {m.group(0)!r} outside "Title (ID)" form')
        else:
            before = text[:s - 1].rstrip()
            if not before or before.endswith((',', ':', ';', '(')):
                probs.append(f'id {m.group(0)!r} has no title in front of it')
    return probs


def notify_enqueue(kind, key, text, titles=()):
    if kind not in KINDS:
        raise SystemExit(f'notify: kind must be one of {sorted(KINDS)} (one text per batch landing, one per halt)')
    probs = card_problems(text, titles)
    if probs:
        raise SystemExit('notify: card refused: ' + '; '.join(probs))
    append_jsonl(STATE / 'notify-outbox.jsonl', {'ts': utc(), 'kind': kind, 'key': key, 'text': text})


def quiet_hours(now=None):
    from zoneinfo import ZoneInfo
    t = (now or dt.datetime.now(ZoneInfo('America/Chicago'))).astimezone(ZoneInfo('America/Chicago'))
    hm = t.hour * 60 + t.minute
    return hm >= 23 * 60 or hm < 7 * 60 + 30


def notify_flush(dry=False, transport=None):
    """Exactly one flusher at a time (flock); a key is texted at most once ever (R6). Quiet hours hold texts."""
    STATE.mkdir(parents=True, exist_ok=True)
    lf = open(STATE / 'notify.lock', 'w')
    try:
        fcntl.flock(lf, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        return {'flushed': 0, 'reason': 'another flusher holds notify.lock'}
    try:
        outbox = STATE / 'notify-outbox.jsonl'
        sent_file = STATE / 'notify-sent.jsonl'
        sent = set()
        if sent_file.exists():
            sent = {json.loads(l)['key'] for l in sent_file.read_text().splitlines() if l.strip()}
        pending = [json.loads(l) for l in outbox.read_text().splitlines() if l.strip()] if outbox.exists() else []
        keep, done, seen = [], [], set()
        # A real text goes out only while a real loop is recorded as running, so tests and dry runs can never text.
        live = transport is not None or ((read_json(STATE / 'run.json') or {}).get('status') == 'running'
                                         and not os.environ.get('CM_LOOP_NO_SEND'))
        for m in pending:
            if m['key'] in sent or m['key'] in seen:
                continue
            seen.add(m['key'])
            if not dry and not live:
                print(f"[not sent: no running loop in {STATE / 'run.json'}] {m['text']}")
                keep.append(m)
                continue
            if dry:
                print(f"[dry-run text to Wilson's own thread] {m['text']}")
                append_jsonl(LOGS / 'notify-dry.jsonl', {**m, 'dry': True, 'flushed': utc()})
                keep.append(m)  # a dry run never consumes the real outbox
                continue
            if m['kind'] != 'halt' and quiet_hours():  # ruling 7: quiet hours hold digests only; a halt always texts
                keep.append(m)
                continue
            last = read_json(STATE / 'notify-last.json', {}).get('epoch', 0)
            gap = float(os.environ.get('CM_LOOP_SEND_GAP', 45))  # never two texts inside 45 s (iMessage->SMS fallback)
            if time.time() - last < gap:
                time.sleep(gap - (time.time() - last))
            code, out, err = (transport or (lambda t: sh([str(SEND_TEXT), t], timeout=90)))(m['text'])
            write_json(STATE / 'notify-last.json', {'epoch': time.time()})
            append_jsonl(sent_file, {**m, 'sent': code == 0, 'exit': code, 'detail': (out + err).strip()[:300],
                                     'flushed': utc()})  # recorded even on failure: never retried, never doubled
            done.append(m['key'])
        tmp = outbox.with_name('notify-outbox.jsonl.tmp')
        tmp.write_text(''.join(json.dumps(m) + '\n' for m in keep))
        tmp.replace(outbox)
        return {'flushed': len(done), 'held': len(keep), 'dry': dry}
    finally:
        fcntl.flock(lf, fcntl.LOCK_UN)
        lf.close()


def card_titles(ids, items):
    return [title_of(i, items) for i in ids]


def batch_card(batch, landed_ids, items, pr=None):
    names = ', '.join(f'{title_of(i, items)} ({i})' for i in landed_ids)
    where = f' in pull request {pr}' if pr else ''
    return (f'Critical Mass Unity: batch {batch} landed on main{where}: {names}. '
            f'Nothing deploys; pull main and open it in Unity to play.')


# ───────────────────────────── lanes ─────────────────────────────
def branch_sha(ref, cwd=None):
    code, out, _ = git('rev-parse', '--verify', '-q', ref, cwd=cwd)
    return out.strip() if code == 0 else ''


def remote_sha(branch):
    code, out, _ = git('ls-remote', 'origin', f'refs/heads/{branch}')
    return out.split()[0] if code == 0 and out.strip() else ''


def lane_create(item_id, base='origin/main'):
    path = LANES / item_id
    if path.exists():
        return {'ok': False, 'error': f'lane {path} already exists (a dead seat? run shutdown or lanes remove)'}
    LANES.mkdir(parents=True, exist_ok=True)
    if base.startswith('origin/'):
        git('fetch', '-q', 'origin', '--prune')
    code, out, err = git('worktree', 'add', '-B', f'loop/{item_id}', str(path), base)
    res = {'ok': code == 0, 'path': str(path), 'branch': f'loop/{item_id}', 'detail': (out + err).strip()[-400:]}
    if code == 0 and base != main_ref(base):
        res.update(merge_main_into_lane(path, main_ref(base)))
    return res


def main_ref(base):
    """The main ref that matches a lane base: origin/main for origin/* bases, main for local ones."""
    return 'origin/main' if base.startswith('origin/') else 'main'


def merge_main_into_lane(path, ref='origin/main'):
    """U-038: a returned item's lane (cut from its own branch) merges main first, so its fixer sees the upstream code
    that landed since. A conflict aborts the merge and fails the lane with the conflicting files named."""
    code, out, err = git('merge', '--no-edit', '-m', f'merge {ref} into the returned lane (U-038)', ref, cwd=path)
    if code == 0:
        return {'merged_main': branch_sha('HEAD', cwd=path)}
    conf = git('diff', '--name-only', '--diff-filter=U', cwd=path)[1].split()
    git('merge', '--abort', cwd=path)
    return {'ok': False, 'error': f'merging {ref} into the lane conflicts in: {", ".join(conf) or (out + err).strip()[-300:]}'}


def lane_remove(name, dry=False):
    """Remove a lane worktree. Its branch is deleted locally only when it is pushed (same sha on origin) or bundled."""
    path = LANES / name
    branch = 'loop/int' if name == '_int' else f'loop/{name}'
    acts = []
    sha = branch_sha(branch) if name != '_int' else ''
    if sha:
        pushed = remote_sha(branch) == sha
        if pushed:
            acts.append(f'{branch} {sha[:8]} is pushed to origin')
        else:
            bdir = LOGS / 'bundles'
            bundle = bdir / f'{name}-{sha[:8]}.bundle'
            acts.append(f'{branch} {sha[:8]} is NOT pushed: bundle to {bundle}')
            if not dry:
                bdir.mkdir(parents=True, exist_ok=True)
                c1, _, e1 = git('bundle', 'create', str(bundle), branch)
                c2, _, e2 = git('bundle', 'verify', str(bundle))
                if c1 != 0 or c2 != 0:
                    return {'ok': False, 'lane': name, 'actions': acts, 'error': f'bundle failed: {e1}{e2}'.strip()}
    if path.exists():
        acts.append(f'remove worktree {path}')
        if not dry:
            code, _, err = git('worktree', 'remove', '--force', str(path))
            if code != 0:
                return {'ok': False, 'lane': name, 'actions': acts, 'error': err.strip()}
    if sha:
        acts.append(f'delete local branch {branch}')
        if not dry:
            git('branch', '-D', branch)
    if not dry:
        git('worktree', 'prune')
    return {'ok': True, 'lane': name, 'actions': acts, 'dry': dry}


def lanes_list():
    return sorted(p.name for p in LANES.iterdir() if p.is_dir()) if LANES.exists() else []


# ───────────────────────────── PRs (base is always main) ─────────────────────────────
def changed_files(base, head, cwd=None):
    code, out, err = git('diff', '--name-only', f'{base}...{head}', cwd=cwd)
    if code != 0:
        raise RuntimeError(f'git diff {base}...{head}: {err.strip()}')
    return [l for l in out.splitlines() if l.strip()]


def is_docs_path(p):
    return p.endswith('.md') or p.startswith('loop/parity-board/')


def pr_open(head, title, body_file, base='main', runner=sh):
    if base != 'main':
        return {'ok': False, 'error': f'refused: every PR targets main, not {base!r} (R1)'}
    files = changed_files('origin/main', f'origin/{head}')
    if not files:
        return {'ok': False, 'error': f'refused: {head} has no changes against main'}
    code, out, err = runner(['gh', 'pr', 'list', '-R', SLUG, '--state', 'open', '--json',
                             'number,headRefName,baseRefName'])
    if code != 0:
        return {'ok': False, 'error': f'gh pr list failed: {err.strip()}'}
    open_prs = json.loads(out or '[]')
    batch_open = [p for p in open_prs if p['headRefName'].startswith('loop/int-')]
    if all(is_docs_path(f) for f in files) and batch_open:
        return {'ok': False, 'error': f'refused: docs-only PR while batch PR #{batch_open[0]["number"]} is open (R2)'}
    if head.startswith('loop/int-') and batch_open:
        return {'ok': False, 'error': f'refused: batch PR #{batch_open[0]["number"]} is still open; one at a time'}
    code, out, err = runner(['gh', 'pr', 'create', '-R', SLUG, '--base', 'main', '--head', head, '--title', title,
                             '--body-file', str(body_file)])
    if code != 0:
        return {'ok': False, 'error': f'gh pr create failed: {err.strip()}'}
    m = re.search(r'/pull/(\d+)', out)
    return {'ok': True, 'number': int(m.group(1)) if m else None, 'url': out.strip()}


def pr_merge(number, subject, body_file, runner=sh):
    code, out, err = runner(['gh', 'pr', 'view', str(number), '-R', SLUG, '--json', 'baseRefName,state,headRefName'])
    if code != 0:
        return {'ok': False, 'error': f'gh pr view failed: {err.strip()}'}
    info = json.loads(out)
    if info['baseRefName'] != 'main':
        return {'ok': False, 'error': f'refused: PR #{number} targets {info["baseRefName"]}, not main (R1)'}
    code, out, err = runner(['gh', 'pr', 'merge', str(number), '-R', SLUG, '--squash', '--delete-branch',
                             '--subject', subject, '--body-file', str(body_file)])
    if code != 0:
        return {'ok': False, 'error': f'gh pr merge failed: {err.strip()}'}
    code, out, err = runner(['gh', 'pr', 'view', str(number), '-R', SLUG, '--json', 'state,mergeCommit'])
    info = json.loads(out or '{}')
    oid = (info.get('mergeCommit') or {}).get('oid', '')
    git('fetch', '-q', 'origin', 'main')
    anc = bool(oid) and git('merge-base', '--is-ancestor', oid, 'origin/main')[0] == 0
    return {'ok': info.get('state') == 'MERGED' and anc, 'state': info.get('state'), 'merge_commit': oid,
            'ancestor_of_origin_main': anc}


# ───────────────────────────── halt check (builders and the integrator run it first) ─────────────────────────────
def halt_check(wait_launch=120):
    for name in ('HALT', 'STALL'):
        f = STATE / name
        if f.exists():
            return 3, f'{name}: {f.read_text().strip()[:300]}'
    if (STATE / 'STOP-DISPATCH').exists():
        return 4, 'STOP-DISPATCH: ' + (STATE / 'STOP-DISPATCH').read_text().strip()[:300]
    blk = review_blocking()
    if blk:
        return 6, f'{len(blk)} unresolved BLOCKING review entr(y/ies), first {blk[0]["key"]} in {blk[0]["file"]}'
    deadline = time.time() + wait_launch
    while True:
        run = read_json(STATE / 'run.json') or {}
        if run.get('status') != 'running':
            return 5, f'no running loop recorded in {STATE / "run.json"} (status {run.get("status")!r})'
        if run.get('task_id') and run.get('run_id'):
            return 0, f'ok: run {run["run_id"]} task {run["task_id"]}'
        if time.time() >= deadline:
            return 5, 'the launch task id and run id were never recorded (record-launch); the loop cannot be stopped'
        time.sleep(5)


# ───────────────────────────── watchers: pidfiles, start, stop ─────────────────────────────
def pidfile(name):
    return STATE / 'pids' / f'{name}.pid'


def watcher_pid(name):
    """Recorded pid if it is alive AND still runs this loop's watcher (guards against a recycled pid)."""
    d = read_json(pidfile(name))
    if not d:
        return 0, None
    pid = int(d.get('pid', 0))
    cmd = pid_command(pid) if pid_alive(pid) else ''
    return (pid if cmd and 'watchers/watch.py' in cmd and f' {name}' in cmd else 0), d


def watchers_stop(snapshot=None):
    """Stop each watcher by its recorded pid and prove it gone with ps -p. `snapshot` = pidfiles read before
    STOP-WATCHERS was written, so a watcher that exits on its own is still accounted for."""
    out = []
    snapshot = snapshot if snapshot is not None else {n: read_json(pidfile(n)) for n in WATCHERS}
    for name in WATCHERS:
        pid, d = watcher_pid(name)
        snap = snapshot.get(name)
        if not d and snap:
            spid = int(snap.get('pid', 0))
            for _ in range(30):
                if not pid_alive(spid):
                    break
                time.sleep(0.1)
            out.append({'watcher': name, 'pid': spid, 'result': 'exited on STOP-WATCHERS, ps -p shows it gone'
                        if not pid_alive(spid) else 'STILL ALIVE without a pidfile'})
            continue
        if not d:
            continue
        if not pid:
            pidfile(name).unlink(missing_ok=True)
            out.append({'watcher': name, 'pid': d.get('pid'), 'result': 'already gone (stale pidfile removed)'})
            continue
        os.kill(pid, 15)
        for _ in range(50):
            if not pid_alive(pid):
                break
            time.sleep(0.1)
        if pid_alive(pid):
            os.kill(pid, 9)
            time.sleep(0.3)
        gone = not pid_alive(pid)
        if gone:
            pidfile(name).unlink(missing_ok=True)
        out.append({'watcher': name, 'pid': pid, 'result': 'stopped, ps -p shows it gone' if gone else 'STILL ALIVE'})
    return out


# ───────────────────────────── preflight (launch checklist) ─────────────────────────────
def preflight(cap, items_dir=None, fetch=True):
    checks = []

    def add(name, ok, detail):
        checks.append({'check': name, 'ok': bool(ok), 'detail': detail})

    add('spend cap given', isinstance(cap, int) and cap > 0, f'cap {cap} total tokens (U-014 measure)')
    g = free_gb()
    add('disk >= 25 GB free', g >= LAUNCH_DISK_GB, f'{g:.1f} GB free on {DATA_VOLUME}')
    code, out, err = sh([sys.executable, str(HERE.parent / 'validate-items.py'), '--quiet']
                        + (['--dir', str(items_dir)] if items_dir else []))
    add('item validator green', code == 0, (out + err).strip().splitlines()[-1] if (out + err).strip() else f'exit {code}')
    n_items = len(item_files(items_dir))
    add('at least one item', n_items > 0, f'{n_items} item file(s) in {items_dir or ITEMS_DIR}')
    stale = [n for n in ('COMPILE.lock', 'STALL', 'HALT', 'STOP-DISPATCH', 'STOP-WATCHERS') if (STATE / n).exists()]
    add('no stale lock/stall/halt files', not stale, ', '.join(stale) or 'none')
    orphans = []
    for name in WATCHERS:
        pid, d = watcher_pid(name)
        if pid:
            orphans.append(f'{name}:{pid}')
    add('no orphan watcher pids', not orphans, ', '.join(orphans) or 'none alive')
    run = read_json(STATE / 'run.json') or {}
    add('one loop at a time', run.get('status') != 'running', f'run.json status {run.get("status")!r}')
    lanes = lanes_list()
    add('no leftover lanes', not lanes, ', '.join(lanes) or f'{LANES} empty')
    if fetch:
        git('fetch', '-q', 'origin', '--prune')
    br = git('rev-parse', '--abbrev-ref', 'HEAD')[1].strip()
    dirty = git('status', '--porcelain')[1].rstrip()
    lm, om = branch_sha('main'), branch_sha('origin/main')
    add('main checked out and clean', br == 'main' and not dirty,
        f'branch {br}; ' + (('dirty: ' + ' '.join(l[3:] for l in dirty.splitlines()[:6])) if dirty else 'clean'))
    add('main equals origin/main', lm and lm == om, f'main {lm[:8]} origin/main {om[:8]}')
    blk = review_blocking()
    add('no unresolved BLOCKING review entry', not blk, ', '.join(b['key'] for b in blk[:5]) or 'none')
    bat = order_batches(items_dir)
    known = {p.stem for p in item_files(items_dir)}
    missing = sorted({i for b in bat for i in b['items']} - known)
    add('every _ORDER.md batch item has an item file', not missing,
        (', '.join(missing[:8]) if missing else f'{len(bat)} batch(es), coupled: '
         + (', '.join(str(b['batch']) for b in bat if b['coupled']) or 'none')))
    cov = batch_coverage(bat, load_items(items_dir))
    add('every loop item sits in exactly one _ORDER.md batch', not cov, '; '.join(cov[:8]) or
        f'{sum(len(b["items"]) for b in bat)} ids in {len(bat)} batch(es)')
    bp = batch_problems(bat, load_items(items_dir))
    add('coupled sets and waves are well formed (U-033)', not bp, '; '.join(bp[:8]) or 'ok')
    lic = [str(p) for p in LICENSE_FILES if p.is_file()]
    add('Unity license present', bool(lic), (lic[0] + ' exists (not read)') if lic else 'no Unity_lic.ulf found')
    hits = unity_open_on(MAIN)
    add('no Unity editor open on the main checkout', not hits, ', '.join(str(h['pid']) for h in hits) or 'none')
    add('build-only (no deploy step exists for this repo)', True, 'build-only')
    return checks


# ───────────────────────────── plan (the workflow's args) ─────────────────────────────
def prebuilt_lane(item_id, returns=0, main='origin/main', cwd=None):
    """U-050: the head of a pushed lane a fresh launch reuses instead of rebuilding, or ''. Reused when
    origin/loop/<ID> exists (the remote-tracking ref; preflight fetched), carries commits beyond `main`, the item was
    never returned (a returned item's builder must read its failure file, U-038), and the head descends from `main`
    or merges with it cleanly (git merge-tree, nothing written to any branch)."""
    if int(returns or 0) > 0:
        return ''
    head, mref = branch_sha(f'refs/remotes/origin/loop/{item_id}', cwd=cwd), branch_sha(main, cwd=cwd)
    if not head or not mref:
        return ''
    code, out, _ = git('rev-list', '--count', f'{mref}..{head}', cwd=cwd)
    if code != 0 or out.strip() in ('', '0'):
        return ''
    if git('merge-base', '--is-ancestor', mref, head, cwd=cwd)[0] == 0:
        return head
    return head if git('merge-tree', '--write-tree', mref, head, cwd=cwd)[0] == 0 else ''


def ledger_reused(args=None):
    """U-050, at record-launch (the run id is known): one 'reused' ledger row per item whose pushed lane the run
    takes straight to integration, and one live-log sentence naming them. Returns the ids recorded."""
    args = read_json(LOGS / 'launch-args.json') if args is None else args
    args = args or {}
    landed = set(args.get('landed') or [])
    in_run = {i for b in args.get('batches') or [] for i in b.get('items', [])}
    out = []
    for r in args.get('items') or []:
        iid = r.get('id')
        if r.get('prebuilt') and r.get('owner') == 'loop' and iid not in landed and (not in_run or iid in in_run):
            if item_state(iid, r.get('title') or iid, 'reused',
                          f"pushed lane origin/loop/{iid} at {r['prebuilt'][:12]} goes to integration unbuilt (U-050)",
                          batch='launch'):
                out.append(iid)
    if out:
        livelog(f'This run reuses {len(out)} already pushed item branch(es) without rebuilding them: '
                f'{", ".join(out)}.')
    return out


def plan(cap, now, items_dir=None, run_tag=None, only_batches=None, max_builders=DEFAULT_BUILDERS, reuse=True):
    items = load_items(items_dir)
    try:
        landed = landed_map()
    except RuntimeError:
        landed = {}
    rets, parked = returns_read(), set(parked_ids())
    rows = []
    for iid in sorted(items, key=lambda i: (str(items[i].get('phase')), natural_key(i))):
        it = items[iid]
        rows.append({'id': iid, 'title': it.get('title'), 'phase': str(it.get('phase')), 'owner': it.get('owner'),
                     'waits_on': it.get('waits_on') or [], 'art_row': it.get('art_row'),
                     'tightly_defined': str(it.get('tightly_defined', 'false')).lower() == 'true',
                     'returns': int(rets.get(iid, 0)),
                     'prebuilt': (prebuilt_lane(iid, rets.get(iid, 0)) if reuse and it.get('owner') == 'loop'
                                  and iid not in landed and iid not in parked else '')})
    head = branch_sha('HEAD')
    nb = clamp_builders(max_builders)
    bats = select_batches(order_batches(items_dir), only_batches)
    for b in bats:
        b['waves'] = plan_waves(b, nb)
    return {'now': now, 'runTag': run_tag or now.replace('-', '').replace(':', '')[:13], 'mainRoot': str(MAIN),
            'lanesRoot': str(LANES), 'repo': SLUG, 'harnessCommit': head, 'capTokens': cap,
            'items': rows, 'landed': sorted(landed), 'parked': sorted(parked),
            'batches': bats, 'onlyBatches': only_batches or None,
            'maxBuilders': nb, 'batchMax': 6, 'maxReturns': MAX_RETURNS}


def clamp_builders(n):
    """U-037: the builder limit is a launch argument, 1..12."""
    try:
        n = int(n)
    except (TypeError, ValueError):
        n = DEFAULT_BUILDERS
    return max(1, min(MAX_BUILDERS_CEILING, n))


def plan_waves(batch, limit):
    """Build waves for one batch under `limit` builders (U-037): the _ORDER.md waves when each fits, else the batch's
    items chunked in table order. Every item sits in exactly one wave."""
    limit = clamp_builders(limit)
    items = list(batch['items'])
    declared = [list(w) for w in batch.get('waves') or [] if w]
    flat = [i for w in declared for i in w]
    if declared and sorted(flat) == sorted(items) and len(flat) == len(set(flat)) and all(len(w) <= limit for w in declared):
        return declared
    return [items[k:k + limit] for k in range(0, len(items), limit)]


def peak_agents(waves):
    """Builders run together; the integrator, fix seat and reviewer each run alone after them, so the peak is the
    largest wave (at least 1, the integrator)."""
    return max([len(w) for w in waves] + [1])



# ───────────────────────────── review blockers (ruling 6) and batches (U-026) ─────────────────────────────
REVIEW_DIR = LOOP / 'review'
_BLOCK_RE = re.compile(r'^\s*-?\s*BLOCKING\s+(\S+?):', re.M)
_RESOLVED_RE = re.compile(r'^\s*-?\s*RESOLVED\s+(\S+?):.*\b[A-Z][A-Z0-9]*-\d+[a-z]?\b', re.M)
_WAIVED_RE = re.compile(r'^\s*-?\s*WAIVED\s+(\S+?):.*\bU-\d{3}\b', re.M)


def review_blocking(review_dir=None):
    """Unresolved BLOCKING entries in loop/review/*.md. An entry `- BLOCKING <key>: <text>` is resolved only by a line
    `- RESOLVED <key>: item <ID>` (the architect turned it into an item) or `- WAIVED <key>: U-###`."""
    d = Path(review_dir or REVIEW_DIR)
    open_ = []
    for f in sorted(d.glob('*.md')) if d.exists() else []:
        t = f.read_text(errors='replace')
        done = set(_RESOLVED_RE.findall(t)) | set(_WAIVED_RE.findall(t))
        open_ += [{'key': k, 'file': str(f)} for k in _BLOCK_RE.findall(t) if k not in done]
    return open_


def review_add_advisory(batch_key, key, text, review_dir=None):
    """An ADVISORY line (U-034 soak reports): visible to the architect, never blocks a batch."""
    d = Path(review_dir or REVIEW_DIR)
    d.mkdir(parents=True, exist_ok=True)
    f = d / f'{batch_key}.md'
    with open(f, 'a') as o:
        o.write(f'- ADVISORY {key}: {text.strip()}\n')
    return str(f)


def review_add_blocking(batch_key, key, text, review_dir=None):
    d = Path(review_dir or REVIEW_DIR)
    d.mkdir(parents=True, exist_ok=True)
    f = d / f'{batch_key}.md'
    new = not f.exists()
    with open(f, 'a') as o:
        if new:
            o.write(f'# Review: batch {batch_key}\n\nResolve each BLOCKING line with `- RESOLVED <key>: item <ID>` or '
                    f'`- WAIVED <key>: U-###` (architect only). The next batch does not start while one is open.\n\n')
        o.write(f'- BLOCKING {key}: {text.strip()}\n')
    return str(f)


_RANGE_RE = re.compile(r'(?<![A-Za-z0-9-])([A-Z][A-Z0-9]*-\d+)([a-z])\s*\.\.\s*\1([a-z])(?![A-Za-z0-9-])')


def expand_ranges(cell, tok):
    """Ids in a batch cell, with a letter range `F-05a .. F-05j` written out as F-05a, F-05b, ..., F-05j (panel
    revision 2026-11-12: the parser used to keep only the two endpoints). A range must share its base id."""
    def rep(m):
        base, lo, hi = m.group(1), m.group(2), m.group(3)
        if hi < lo:
            raise ValueError(f'_ORDER.md batch range runs backwards: {m.group(0)!r}')
        return ', '.join(base + chr(c) for c in range(ord(lo), ord(hi) + 1))
    return tok.findall(_RANGE_RE.sub(rep, cell))


def batch_coverage(batches, items):
    """Problems with batch membership: a loop-owned item in no batch, or an id in more than one batch."""
    seen, dup = {}, []
    for b in batches:
        for i in b['items']:
            if i in seen:
                dup.append(f'{i} in batches {seen[i]} and {b["batch"]}')
            seen.setdefault(i, b['batch'])
    loop_ids = {i for i, it in items.items() if str(it.get('owner', '')).lower() == 'loop'}
    orphan = sorted(loop_ids - set(seen), key=natural_key)
    return ([f'{i} is in no batch' for i in orphan] + dup) if batches else []


def select_batches(batches, only):
    """Keep only the batch numbers in `only` (comma list such as '1' or '1,2'); None keeps every batch."""
    if not only:
        return batches
    want = {int(x) for x in str(only).split(',') if x.strip()}
    out = [b for b in batches if b['batch'] in want]
    if len(out) != len(want):
        raise ValueError(f'--batches {only}: _ORDER.md has batches {[b["batch"] for b in batches]}')
    return out


def order_batches(items_dir=None):
    """Batches from loop/items/_ORDER.md, the one source of truth. Section 3's table gives each batch's items in order.
    Coupling is per item (U-033): a `Coupled set` column names the items that must land together ("none" = empty);
    without that column (older tables) the coupled set is the batch's items that sit, two or more, on one
    dependency-graph line marked "(coupled". A `Waves` column ("wave 1: A, B; wave 2: C") gives the build plan.
    Returns [{'batch', 'items', 'coupled' (any coupled item), 'coupled_set', 'waves'}]; [] when the table is absent."""
    f = Path(items_dir or ITEMS_DIR) / '_ORDER.md'
    if not f.exists():
        return []
    t = f.read_text()
    tok = re.compile(r'(?<![A-Za-z0-9-])[A-Z][A-Z0-9]*-\d+[a-z]?(?![A-Za-z0-9-])')
    coupled_lines = [set(expand_ranges(l, tok)) for l in t.splitlines() if '(coupled' in l]
    m = re.search(r'^##\s+\d*\.?\s*Batches\b.*?$(.*?)(?=^##\s|\Z)', t, re.M | re.S)
    sec = m.group(1) if m else ''
    out, header = [], []
    for l in sec.splitlines():
        cells = [c.strip() for c in l.strip().strip('|').split('|')]
        if cells and cells[0].lower() == 'batch':
            header = [c.lower() for c in cells]
            continue
        if len(cells) >= 2 and cells[0].isdigit():
            ids = expand_ranges(cells[1], tok)
            ci = next((k for k, h in enumerate(header) if h.startswith('coupled set')), None)
            if ci is not None and ci < len(cells):
                cset = [i for i in expand_ranges(cells[ci], tok)]
            else:
                cset = [i for i in ids if any(i in cs and len(set(ids) & cs) >= 2 for cs in coupled_lines)]
            wi = next((k for k, h in enumerate(header) if h.startswith('waves')), None)
            waves = parse_waves(cells[wi], tok) if wi is not None and wi < len(cells) else []
            out.append({'batch': int(cells[0]), 'items': ids, 'coupled': bool(cset), 'coupled_set': cset,
                        'waves': waves})
    return out


def parse_waves(cell, tok):
    """'wave 1: F-01, F-02; wave 2: F-03' -> [['F-01', 'F-02'], ['F-03']]; a cell without 'wave' gives []."""
    if 'wave' not in cell.lower():
        return []
    return [expand_ranges(part.split(':', 1)[1], tok) for part in cell.split(';') if ':' in part]


def batch_problems(batches, items):
    """U-033 shape checks beyond coverage: the coupled set lies inside the batch, the waves (when given) hold every item
    exactly once, no coupled item waits on an uncoupled item of its own batch, and no batch is over MAX_BATCH_ITEMS."""
    probs = []
    for b in batches:
        n, ids, cset = b['batch'], b['items'], set(b.get('coupled_set') or [])
        if len(ids) > MAX_BATCH_ITEMS:
            probs.append(f'batch {n} has {len(ids)} items (at most {MAX_BATCH_ITEMS})')
        for i in sorted(cset - set(ids)):
            probs.append(f'batch {n}: coupled-set id {i} is not in the batch')
        flat = [i for w in b.get('waves') or [] for i in w]
        if flat and (sorted(flat) != sorted(ids) or len(flat) != len(set(flat))):
            probs.append(f'batch {n}: the waves do not hold every item exactly once')
        for c in sorted(cset):
            for w in (items.get(c) or {}).get('waits_on') or []:
                if w in ids and w not in cset:
                    probs.append(f'batch {n}: coupled item {c} waits on uncoupled {w} of the same batch')
    return probs


# ───────────────────────────── shutdown ─────────────────────────────
def shutdown(dry=False):
    """Loop end = teardown. Watchers are stopped for real even in a dry run (they are this run's processes and must
    be proven gone); lane removal and state changes are only printed in a dry run."""
    ts = utc()
    STATE.mkdir(parents=True, exist_ok=True)
    snapshot = {n: read_json(pidfile(n)) for n in WATCHERS}
    (STATE / 'STOP-WATCHERS').write_text(ts + '\n')
    stopped = watchers_stop(snapshot)
    for s in stopped:
        print(f"watcher {s['watcher']} pid {s['pid']}: {s['result']}")
    left = [n for n in WATCHERS if watcher_pid(n)[0]] + [s['watcher'] for s in stopped if 'STILL' in s['result']]
    lanes = [lane_remove(n, dry) for n in lanes_list()]
    for l in lanes:
        print(('[dry-run] ' if dry else '') + f"lane {l['lane']}: " + '; '.join(l.get('actions', []))
              + (f" ERROR {l['error']}" if not l.get('ok') else ''))
    if not lanes:
        print(f'lanes: none under {LANES}')
    run = read_json(STATE / 'run.json', {}) or {}
    stop = ', '.join(f'{n}: {(STATE / n).read_text().strip()[:120]}' for n in ('HALT', 'STALL', 'STOP-DISPATCH')
                     if (STATE / n).exists()) or 'the workflow ended'
    finals = ledger_close(stop, dry)
    audit = [] if dry else ledger_audit()
    reports = []
    if not dry and run.get('run_id'):
        try:
            import spend  # noqa: E402 (spend imports this module; imported late on purpose)
            reports = spend.missing_batch_reports(run['run_id'])
        except Exception as e:  # a report failure never blocks teardown; it is written into the resume note
            reports = [f'batch reports failed: {type(e).__name__}: {e}']
    try:
        lm = landed_map()
    except RuntimeError:
        lm = {}
    items = load_items()
    landed = [i for i in items if i in lm]
    note = LOGS / f'RESUME-{ts.replace(":", "")}{"-dry" if dry else ""}.md'
    note.parent.mkdir(parents=True, exist_ok=True)
    note.write_text('\n'.join([
        f'# Critical Mass Unity loop: resume note {ts}', '',
        f'Run: {run.get("run_id")} task {run.get("task_id")} started {run.get("started")} cap {run.get("cap")}', '',
        f'Landed on origin/main ({len(landed)}): ' + (', '.join(f'{title_of(i, items)} ({i})' for i in landed) or 'none'),
        f'Parked for the architect: ' + (', '.join(parked_ids()) or 'none'),
        f'Returns so far: {json.dumps(returns_read())}',
        f'Spend: {json.dumps(read_json(STATE / "spend.json"))}',
        f'Watchers stopped: {json.dumps(stopped)}; still alive: {left or "none"}',
        f'Lanes: {json.dumps(lanes)}',
        f'Disk free after: {free_gb():.1f} GB',
        f'Ledger (U-042, {ledger_path()}): {len(finals)} final row(s) written; audit: ' + ('; '.join(audit) or 'clean'),
        f'Batch reports written at shutdown: {json.dumps(reports)}', '',
        'Next launch: fresh, never resumed (positional resume cache). Run loop/bin/launch.sh --cap N.', '']))
    print(f'resume note: {note}')
    if not dry:
        if run:
            run.update({'status': 'stopped', 'stopped': ts})
            write_json(STATE / 'run.json', run)
        livelog('The loop was shut down: watchers stopped by pid, lanes removed, resume note written.')
    else:
        livelog('Dry-run shutdown: watchers stopped by pid; lane removal only printed.', dry=True)
        if run.get('dry') or not run:  # a dry launch leaves nothing behind
            for f in ('run.json', 'schedule.json', 'clock-state.json'):
                (STATE / f).unlink(missing_ok=True)
    (STATE / 'STOP-WATCHERS').unlink(missing_ok=True)
    return 0 if not left and all(l.get('ok') for l in lanes) else 1


# ───────────────────────────── CLI ─────────────────────────────
def main(argv=None):
    ap = argparse.ArgumentParser(prog='cmloop.py', description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest='cmd', required=True)
    s = sub.add_parser('lock', help='compile lock: status | acquire | release | run -- CMD')
    s.add_argument('action', choices=['status', 'acquire', 'release', 'run'])
    s.add_argument('--owner', default='')
    s.add_argument('--pid', type=int)
    s.add_argument('--token', default='')
    s = sub.add_parser('landed', help='landed ids (full-id marker in origin/main, not reverted)')
    s.add_argument('ids', nargs='*')
    s.add_argument('--ref', default='origin/main')
    s = sub.add_parser('lanes', help='create ID | remove ID | list')
    s.add_argument('action', choices=['create', 'remove', 'list'])
    s.add_argument('id', nargs='?')
    s.add_argument('--base', default='origin/main')
    s.add_argument('--dry-run', action='store_true')
    s = sub.add_parser('livelog', help='append one plain sentence to the Obsidian live log')
    s.add_argument('sentence')
    s.add_argument('--dry-run', action='store_true')
    s = sub.add_parser('notify', help='enqueue KIND KEY TEXT | flush')
    s.add_argument('action', choices=['enqueue', 'flush'])
    s.add_argument('--kind')
    s.add_argument('--key')
    s.add_argument('--text')
    s.add_argument('--dry-run', action='store_true')
    s = sub.add_parser('batches', help='batches from loop/items/_ORDER.md (coupled flag included)')
    s.add_argument('--items-dir')
    s = sub.add_parser('review-blocking', help='unresolved BLOCKING entries in loop/review/*.md (exit 6 if any)')
    s = sub.add_parser('halt-check', help='exit 0 go; 3 halt/stall, 4 stop-dispatch, 5 no recorded launch, 6 BLOCKING')
    s.add_argument('--wait', type=int, default=120)
    s = sub.add_parser('record-launch', help='record the Workflow taskId (w...) and runId (wf_...)')
    s.add_argument('--task-id', required=True)
    s.add_argument('--run-id', required=True)
    s = sub.add_parser('preflight')
    s.add_argument('--cap', type=int)
    s.add_argument('--items-dir')
    s.add_argument('--no-fetch', action='store_true')
    s = sub.add_parser('plan')
    s.add_argument('--cap', type=int, required=True)
    s.add_argument('--now', required=True)
    s.add_argument('--items-dir')
    s.add_argument('--out')
    s.add_argument('--batches', help="run only these _ORDER.md batch numbers, e.g. '1' (the workflow then stops)")
    s.add_argument('--builders', type=int, default=DEFAULT_BUILDERS, help='builder limit, 1..12 (U-037)')
    s.add_argument('--show', action='store_true', help='print the batch table: items, coupled set, waves, peak agents')
    s.add_argument('--rebuild', action='store_true',
                   help='U-050: rebuild every item; never reuse a pushed origin/loop/<ID> lane')
    s = sub.add_parser('pr-open')
    s.add_argument('--head', required=True)
    s.add_argument('--title', required=True)
    s.add_argument('--body-file', required=True)
    s.add_argument('--base', default='main')
    s = sub.add_parser('pr-merge')
    s.add_argument('--number', required=True, type=int)
    s.add_argument('--subject', required=True)
    s.add_argument('--body-file', required=True)
    s = sub.add_parser('ledger', help='close | audit | show: the per-item record of this run (U-042)')
    s.add_argument('action', choices=['close', 'audit', 'show'])
    s.add_argument('--reason', default='')
    s.add_argument('--dry-run', action='store_true')
    s = sub.add_parser('watchers-stop')
    s = sub.add_parser('shutdown', help='stop watchers by pidfile, remove lanes (pushed or bundled), resume note')
    s.add_argument('--dry-run', action='store_true')
    argv = list(sys.argv[1:] if argv is None else argv)
    rest = argv[argv.index('--') + 1:] if '--' in argv else []
    a = ap.parse_args(argv[:argv.index('--')] if '--' in argv else argv)
    a.rest = rest
    if a.cmd == 'shutdown':
        return shutdown(a.dry_run)

    if a.cmd == 'lock':
        if a.action == 'status':
            print(json.dumps(lock_status()))
            return 0
        if a.action == 'acquire':
            rec = lock_acquire(a.owner or 'manual', a.pid)
            print(json.dumps(rec or {'acquired': False, 'lock': lock_status()}))
            return 0 if rec else 75
        if a.action == 'release':
            ok = lock_release(a.token)
            print(json.dumps({'released': ok}))
            return 0 if ok else 1
        cmd = a.rest
        cur = read_json(lock_path()) or {}
        if cur.get('token') and cur.get('token') == os.environ.get('CM_LOCK_TOKEN'):
            return subprocess.run(cmd).returncode
        rec = lock_acquire(a.owner or 'lock-run')
        if not rec:
            print(json.dumps({'acquired': False, 'lock': lock_status()}))
            return 75
        try:
            return subprocess.run(cmd, env={**os.environ, 'CM_LOCK_TOKEN': rec['token']}).returncode
        finally:
            lock_release(rec['token'])
    if a.cmd == 'landed':
        m = landed_map(a.ref)
        ids = a.ids or sorted(m)
        print(json.dumps({i: m.get(i) for i in ids}))
        return 0
    if a.cmd == 'lanes':
        if a.action == 'list':
            print(json.dumps(lanes_list()))
            return 0
        r = lane_create(a.id, a.base) if a.action == 'create' else lane_remove(a.id, a.dry_run)
        print(json.dumps(r))
        return 0 if r.get('ok') else 1
    if a.cmd == 'livelog':
        livelog(a.sentence, a.dry_run)
        return 0
    if a.cmd == 'notify':
        if a.action == 'enqueue':
            notify_enqueue(a.kind, a.key, a.text)
            print(json.dumps({'queued': a.key}))
            return 0
        print(json.dumps(notify_flush(a.dry_run)))
        return 0
    if a.cmd == 'batches':
        print(json.dumps(order_batches(a.items_dir), indent=1))
        return 0
    if a.cmd == 'review-blocking':
        b = review_blocking()
        print(json.dumps(b))
        return 6 if b else 0
    if a.cmd == 'halt-check':
        code, msg = halt_check(a.wait)
        print(msg)
        return code
    if a.cmd == 'record-launch':
        if not re.match(r'^w[a-z0-9]{6,}$', a.task_id) or not re.match(r'^wf_[0-9a-f-]+$', a.run_id):
            print('record-launch: task id must look like w8lmzevii and run id like wf_2f3407a6-e27')
            return 2
        run = read_json(STATE / 'run.json') or {}
        run.update({'task_id': a.task_id, 'run_id': a.run_id, 'recorded': utc()})
        write_json(STATE / 'run.json', run)
        (LOGS / 'launch-ids.txt').parent.mkdir(parents=True, exist_ok=True)
        with open(LOGS / 'launch-ids.txt', 'a') as f:
            f.write(f'{utc()} task_id={a.task_id} run_id={a.run_id}\n')
        run['reused'] = ledger_reused()
        print(json.dumps(run))
        return 0
    if a.cmd == 'preflight':
        checks = preflight(a.cap, a.items_dir, not a.no_fetch)
        for c in checks:
            print(f"{'PASS' if c['ok'] else 'FAIL'}  {c['check']}: {c['detail']}")
        return 0 if all(c['ok'] for c in checks) else 1
    if a.cmd == 'plan':
        p = plan(a.cap, a.now, a.items_dir, only_batches=a.batches, max_builders=a.builders, reuse=not a.rebuild)
        if a.out:
            write_json(a.out, p)
        if a.show:
            for b in p['batches']:
                print(f"batch {b['batch']}: items {', '.join(b['items'])} | coupled set "
                      f"{', '.join(b['coupled_set']) or 'none'} | waves "
                      + '; '.join(f'wave {k + 1}: {len(w)}' for k, w in enumerate(b['waves']))
                      + f" | peak agents {peak_agents(b['waves'])} of {MAX_BUILDERS_CEILING}")
            print(f"builder limit {p['maxBuilders']}")
            reused = [r['id'] for r in p['items'] if r.get('prebuilt')]
            print(f"reused pushed lanes (no rebuild, U-050): {', '.join(reused) or 'none'}")
        print(json.dumps({'items': len(p['items']), 'landed': len(p['landed']), 'parked': len(p['parked']),
                          'batches': [b['batch'] for b in p['batches']], 'out': a.out}))
        return 0
    if a.cmd == 'pr-open':
        r = pr_open(a.head, a.title, a.body_file, a.base)
        print(json.dumps(r))
        return 0 if r['ok'] else 2
    if a.cmd == 'pr-merge':
        r = pr_merge(a.number, a.subject, a.body_file)
        print(json.dumps(r))
        return 0 if r['ok'] else 2
    if a.cmd == 'ledger':
        if a.action == 'close':
            print(json.dumps(ledger_close(a.reason, a.dry_run)))
            return 0
        if a.action == 'audit':
            pr = ledger_audit()
            print(json.dumps(pr))
            return 1 if pr else 0
        print(json.dumps(ledger_rows(run_key()), indent=1))
        return 0
    if a.cmd == 'watchers-stop':
        print(json.dumps(watchers_stop()))
        return 0
    return 2


if __name__ == '__main__':
    sys.exit(main())
