#!/usr/bin/env python3
"""The loop's gates (decision 4). Each gate prints ONE JSON line, appends it to loop/logs/gates.jsonl and exits with
the real exit code: 0 pass, the tool's own non-zero code on fail, 78 skipped. "skipped" is never exit 0, so nothing
that reads exit codes can mistake a skip for a pass. DG-54 (2026-11-12 panel): the old `budgets` gate (it pointed at a
scene that does not exist, was skipped every time and by design could never fail) is gone; `perf` runs the built player
and FAILS on any budget over OR not measured.

  --project defaults to the worktree the gate is invoked from (cwd's checkout of this repo, else this script's
  checkout), never the main checkout (DG-05).

  gate.py setup          [--project DIR]                    -executeMethod CriticalMass.Editor.ProjectSetup.Apply (U-029)
  gate.py compile        [--project DIR]                    tools/unity-compile.sh
  gate.py editmode       [--project DIR]                    tools/unity-test.sh, category "!Slow" (fails on zero tests)
  gate.py playmode-smoke [--project DIR]                    tools/unity-playtest.sh (assembly Runtime.PlayTests, F-08c)
  gate.py sim-long       [--project DIR]                    tools/unity-soak.sh: the "Slow" NUnit tests (was "soak"; "soak" still works)
  gate.py perf           [--project DIR]                    tools/demo-smoke.sh on the built player: 1080p window, native
                                                            fullscreen, CM_PROFILE=floor (CM_PERF_PASSES=window,fullscreen,floor)
  gate.py player-soak    [--project DIR]                    tools/player-soak.sh: 20 matches, 2 hours, the shipped player (DG-55)
  gate.py build-mac      [--project DIR]                    tools/unity-build-mac.sh
  gate.py build-windows  [--project DIR]                    tools/unity-build-windows.sh (skipped 78 without the module)
  gate.py parity  --item ID [--project DIR]                 tools/unity-capture.sh (art_row items only)
  gate.py scope   --item ID --ref REF [--base REF]          git only: the diff touches only files_allowed

Unity gates refuse to run inside a builder lane (builders never run Unity), refuse when a Unity editor already has the
project open, refuse under the 15 GB disk floor, and run under the compile lock (one Unity process at a time).
"""
import argparse
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bin'))
import cmloop as C  # noqa: E402

SMOKE_SCENE = 'Assets/_Game/Scenes/Arena.unity'  # the game scene (ARCHITECTURE.md folder map, F-08)
PLAYTEST_TOOL = 'tools/unity-playtest.sh'  # runs Assets/_Game/Runtime.PlayTests (ruling 4; F-08c creates both)
SOAK_TOOL = 'tools/unity-soak.sh'  # runs the NUnit "Slow" tests (U-028)
CAPTURE_TOOL = 'tools/unity-capture.sh'
PERF_TOOL = 'tools/demo-smoke.sh'  # DG-54
PLAYER_SOAK_TOOL = 'tools/player-soak.sh'  # DG-55
PERF_PASSES = {'window': ([], {}), 'fullscreen': (['--fullscreen'], {}), 'floor': ([], {'CM_PROFILE': 'floor'})}
EXC_RE = re.compile(r'^\s*(?:[\w.]+\.)?\w*Exception\b[^\n]*', re.M)


def emit(gate, status, code, detail, **kw):
    rec = {'gate': gate, 'status': status, 'exit': code, 'ts': C.utc(), 'detail': detail, **kw}
    line = json.dumps(rec, sort_keys=True)
    print(line, flush=True)
    C.append_jsonl(C.LOGS / 'gates.jsonl', rec)
    return code


def unity_binary(project):
    ver = re.search(r'^m_EditorVersion:\s*(\S+)', (Path(project) / 'ProjectSettings/ProjectVersion.txt').read_text(),
                    re.M).group(1)
    return os.environ.get('UNITY_PATH') or f'/Applications/Unity/Hub/Editor/{ver}/Unity.app/Contents/MacOS/Unity'


def refuse_unity(gate, project):
    p = Path(project).resolve()
    lanes = C.LANES.resolve()
    if lanes in p.parents and p != C.INT.resolve() and C.INT.resolve() not in p.parents:
        return emit(gate, 'fail', 2, f'refused: {p} is a builder lane; builders never run Unity', project=str(p))
    hits = C.unity_open_on(p)
    if hits:
        return emit(gate, 'fail', 3, f'refused: a Unity editor already has {p} open (pid {hits[0]["pid"]})',
                    project=str(p))
    g = C.free_gb()
    if g < C.DISK_FLOOR_GB:
        return emit(gate, 'fail', 4, f'refused: {g:.1f} GB free, under the {C.DISK_FLOOR_GB} GB floor', project=str(p))
    return None


def run_locked(gate, cmd, cwd):
    """Run cmd under the compile lock. Inside the integrator (CM_LOCK_TOKEN matches the lock) it runs directly."""
    cur = C.read_json(C.lock_path()) or {}
    if cur.get('token') and cur.get('token') == os.environ.get('CM_LOCK_TOKEN'):
        p = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True)
        return p.returncode, p.stdout, p.stderr, cur.get('owner')
    rec = C.lock_acquire(f'gate-{gate}', purpose=' '.join(cmd)[:120])
    if not rec:
        st = C.lock_status()
        return 75, '', f'compile lock held by {st.get("owner")} pid {st.get("pid")} since {st.get("started")}', None
    try:
        p = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True,
                           env={**os.environ, 'CM_LOCK_TOKEN': rec['token']})
        return p.returncode, p.stdout, p.stderr, rec['owner']
    finally:
        C.lock_release(rec['token'])


def save_out(gate, out, err):
    f = C.LOGS / f'gate-{gate}.out'
    f.parent.mkdir(parents=True, exist_ok=True)
    f.write_text(out + ('\n--- stderr ---\n' + err if err else ''))
    return str(f)


def cs_errors(log):
    try:
        txt = Path(log).read_text(errors='replace')
    except FileNotFoundError:
        return []
    return sorted(set(l.strip() for l in txt.splitlines() if re.search(r'error CS\d+', l)))


def g_compile(a):
    proj = Path(a.project).resolve()
    r = refuse_unity('compile', proj)
    if r is not None:
        return r
    code, out, err, owner = run_locked('compile', [str(proj / 'tools/unity-compile.sh')], proj)
    outf = save_out('compile', out, err)
    log = proj / 'Logs/compile.log'
    if code == 0:
        return emit('compile', 'pass', 0, 'unity-compile: OK', project=str(proj), log=str(log), out=outf,
                    lock_owner=owner)
    errs = cs_errors(log)
    ef = C.LOGS / 'gate-compile-errors.txt'
    ef.write_text('\n'.join(errs) + '\n')
    detail = errs[0] if errs else ((err or out).strip().splitlines() or ['no output'])[-1]
    return emit('compile', 'fail', code, detail, errors=len(errs), errors_file=str(ef), project=str(proj), log=str(log), out=outf, lock_owner=owner)


def failed_tests(xml_path):
    try:
        root = ET.parse(xml_path).getroot()
    except Exception:
        return []
    out = []
    for tc in root.iter('test-case'):
        if tc.get('result') == 'Failed':
            msg = tc.find('./failure/message')
            st = tc.find('./failure/stack-trace')
            out.append({'name': tc.get('name'), 'fullname': tc.get('fullname'), 'classname': tc.get('classname'),
                        'message': (msg.text or '').strip()[:800] if msg is not None else '',
                        'stack': (st.text or '').strip()[:1500] if st is not None else ''})
    return out


def result_counts(xml_path):
    """DG-54: total, passed, failed, skipped and inconclusive test cases (with names) from an NUnit 3 results file."""
    try:
        root = ET.parse(xml_path).getroot()
    except Exception:
        return None
    c = {'total': 0, 'passed': 0, 'failed': 0, 'skipped': 0, 'inconclusive': 0, 'skipped_names': [], 'inconclusive_names': []}
    for tc in root.iter('test-case'):
        c['total'] += 1
        r = tc.get('result') or ''
        name = tc.get('fullname') or tc.get('name') or '?'
        if r == 'Passed':
            c['passed'] += 1
        elif r == 'Failed':
            c['failed'] += 1
        elif r == 'Skipped':
            c['skipped'] += 1
            c['skipped_names'].append(name)
        else:
            c['inconclusive'] += 1
            c['inconclusive_names'].append(name)
    return c


def g_editmode(a):
    proj = Path(a.project).resolve()
    r = refuse_unity('editmode', proj)
    if r is not None:
        return r
    code, out, err, owner = run_locked('editmode', [str(proj / 'tools/unity-test.sh')], proj)
    outf = save_out('editmode', out, err)
    res = proj / 'Logs/editmode-results.xml'
    m = re.search(r'total=(\d+) passed=(\d+) failed=(\d+)', out + err)
    total, passed, failed = (int(x) for x in m.groups()) if m else (0, 0, 0)
    ft = failed_tests(res)
    ff = C.LOGS / 'gate-editmode-failed.json'
    ff.write_text(json.dumps(ft, indent=1))
    counts = result_counts(res)  # DG-54: the real numbers from the results file, never a hard-coded failed=0
    if counts:
        total, passed, failed = counts['total'], counts['passed'], counts['failed']
    honest = bool(counts) and counts['total'] == counts['passed'] + counts['failed'] + counts['skipped'] + counts['inconclusive']
    if code == 0 and total > 0 and failed == 0 and honest:
        return emit('editmode', 'pass', 0, f'{passed}/{total} EditMode tests passed (category !Slow); '
                    f'{counts["skipped"]} skipped, {counts["inconclusive"]} inconclusive', total=total, passed=passed,
                    failed=failed, skipped=counts['skipped'], inconclusive=counts['inconclusive'],
                    skipped_names=counts['skipped_names'][:40], inconclusive_names=counts['inconclusive_names'][:40],
                    category='!Slow', results=str(res), out=outf, lock_owner=owner)
    if code == 0 and total > 0 and failed == 0 and not honest:
        return emit('editmode', 'fail', 1, 'results do not add up: total != passed + failed + skipped + inconclusive',
                    counts=counts, results=str(res), out=outf, lock_owner=owner)
    if total == 0:
        detail = 'zero tests ran'
    elif failed:
        detail = f'{failed} of {total} EditMode tests failed'
    else:
        detail = ((err or out).strip().splitlines() or [f'Unity exit {code}'])[-1]
    return emit('editmode', 'fail', code or 1, detail, total=total, failed=failed, failed_file=str(ff),
                results=str(res), out=outf, lock_owner=owner)


def g_playmode(a):
    proj = Path(a.project).resolve()
    if not (proj / PLAYTEST_TOOL).exists():
        return emit('playmode-smoke', 'skipped', C.SKIP_EXIT,
                    f'skipped: {PLAYTEST_TOOL} does not exist yet (F-08c creates it with Runtime.PlayTests)',
                    project=str(proj))
    r = refuse_unity('playmode-smoke', proj)
    if r is not None:
        return r
    code, out, err, owner = run_locked('playmode-smoke', ['bash', str(proj / PLAYTEST_TOOL)], proj)
    outf = save_out('playmode-smoke', out, err)
    res, log = proj / 'Logs/playmode-results.xml', proj / 'Logs/playmode-tests.log'
    pf = C.LOGS / 'gate-playmode-failed.json'
    pf.write_text(json.dumps(failed_tests(res), indent=1))
    excs = sorted(set(m.group(0).strip()[:300] for m in EXC_RE.finditer(log.read_text(errors='replace'))))[:40] \
        if log.exists() else []
    ef = C.LOGS / 'gate-playmode-exceptions.txt'
    ef.write_text('\n'.join(excs) + '\n')
    if code == 0 and not excs:
        return emit('playmode-smoke', 'pass', 0, ((out.strip().splitlines() or ['ok'])[-1])[:200], out=outf,
                    lock_owner=owner)
    why = f'{len(excs)} exception line(s) in {log.name}' if code == 0 else \
        ((err or out).strip().splitlines() or [f'exit {code}'])[-1][:200]
    return emit('playmode-smoke', 'fail', code or 1, why, failed_file=str(pf), exceptions_file=str(ef), log=str(log),
                out=outf, lock_owner=owner)


def g_soak(a):
    proj = Path(a.project).resolve()
    if not (proj / SOAK_TOOL).exists():
        return emit('soak', 'skipped', C.SKIP_EXIT, f'skipped: {SOAK_TOOL} does not exist yet (U-028)', project=str(proj))
    r = refuse_unity('soak', proj)
    if r is not None:
        return r
    code, out, err, owner = run_locked('soak', ['bash', str(proj / SOAK_TOOL)], proj)
    outf = save_out('soak', out, err)
    last = ((err or out).strip().splitlines() or [f'exit {code}'])[-1][:200]
    return emit('soak', 'pass' if code == 0 else 'fail', code, last, out=outf, lock_owner=owner)


def g_setup(a):
    proj = Path(a.project).resolve()
    r = refuse_unity('setup', proj)
    if r is not None:
        return r
    log = proj / 'Logs/setup-gate.log'
    cmd = [unity_binary(proj), '-batchmode', '-nographics', '-projectPath', str(proj), '-executeMethod',
           'CriticalMass.Editor.ProjectSetup.Apply', '-logFile', str(log), '-quit']
    code, out, err, owner = run_locked('setup', cmd, proj)
    dirty = [p for _, p in C.dirty_files(proj)]
    if code == 0:
        return emit('setup', 'pass', 0, 'ProjectSetup.Apply exit 0', log=str(log), changed_files=dirty[:20],
                    lock_owner=owner)
    errs = cs_errors(log)
    ef = C.LOGS / 'gate-setup-errors.txt'
    ef.write_text('\n'.join(errs) + '\n')
    return emit('setup', 'fail', code, (errs[:1] or [f'ProjectSetup.Apply: Unity exit {code}'])[0], log=str(log),
                errors_file=str(ef), lock_owner=owner)


def g_clean_reimport(a):
    """U-030: after the harness committed the generated files, a second import (with ProjectSetup.Apply, which runs on
    every batch) must leave git status clean; anything dirty is a generated file the commit missed."""
    proj = Path(a.project).resolve()
    r = refuse_unity('clean-reimport', proj)
    if r is not None:
        return r
    before = C.dirty_files(proj)
    if before:
        return emit('clean-reimport', 'fail', 1, f'tree not clean before the re-import: {len(before)} file(s)',
                    dirty=[f'{x.strip()} {p}' for x, p in before][:30])
    log = proj / 'Logs/reimport-gate.log'
    cmd = [unity_binary(proj), '-batchmode', '-nographics', '-projectPath', str(proj), '-executeMethod',
           'CriticalMass.Editor.ProjectSetup.Apply', '-logFile', str(log), '-quit']
    code, out, err, owner = run_locked('clean-reimport', cmd, proj)
    after = C.dirty_files(proj)
    if code != 0:
        return emit('clean-reimport', 'fail', code, f'Unity exit {code} on the re-import', log=str(log), lock_owner=owner)
    if after:
        return emit('clean-reimport', 'fail', 1, f'the re-import changed {len(after)} file(s) the batch commit missed: '
                    + ', '.join(p for _, p in after[:5]), dirty=[f'{x.strip()} {p}' for x, p in after][:30],
                    log=str(log), lock_owner=owner)
    return emit('clean-reimport', 'pass', 0, 'second import + ProjectSetup.Apply left git status clean', log=str(log),
                head=C.git('rev-parse', 'HEAD', cwd=proj)[1].strip(), lock_owner=owner)


def g_build_mac(a):
    proj = Path(a.project).resolve()
    r = refuse_unity('build-mac', proj)
    if r is not None:
        return r
    code, out, err, owner = run_locked('build-mac', [str(proj / 'tools/unity-build-mac.sh')], proj)
    outf = save_out('build-mac', out, err)
    log = proj / 'Logs/build-mac.log'
    if code == 0:
        return emit('build-mac', 'pass', 0, f'{proj}/Builds/macOS/CriticalMass.app built', log=str(log), out=outf,
                    lock_owner=owner)
    errs = cs_errors(log)
    ef = C.LOGS / 'gate-build-mac-errors.txt'
    ef.write_text('\n'.join(errs) + '\n')
    return emit('build-mac', 'fail', code, (errs[:1] or [((err or out).strip().splitlines() or ['?'])[-1]])[0],
                errors_file=str(ef), log=str(log), out=outf, lock_owner=owner)


def windows_module(project):
    """U-051: the editor's Windows Build Support module, next to Unity.app (Hub layout <editor>/PlaybackEngines/)."""
    return Path(unity_binary(project)).parents[3] / 'PlaybackEngines' / 'WindowsStandaloneSupport'


def g_build_windows(a):
    """U-051: the shipping build (the demo is a Windows build; build-mac is our own test build). Same shape as
    build-mac; skipped (78) when the editor has no Windows Build Support module."""
    proj = Path(a.project).resolve()
    mod = windows_module(proj)
    if not mod.is_dir():
        return emit('build-windows', 'skipped', C.SKIP_EXIT, f'skipped: no Windows Build Support module at {mod}',
                    project=str(proj))
    r = refuse_unity('build-windows', proj)
    if r is not None:
        return r
    code, out, err, owner = run_locked('build-windows', [str(proj / 'tools/unity-build-windows.sh')], proj)
    outf = save_out('build-windows', out, err)
    log = proj / 'Logs/build-windows.log'
    if code == 0:
        return emit('build-windows', 'pass', 0, f'{proj}/Builds/Windows/CriticalMass.exe built', log=str(log), out=outf,
                    lock_owner=owner)
    errs = cs_errors(log)
    ef = C.LOGS / 'gate-build-windows-errors.txt'
    ef.write_text('\n'.join(errs) + '\n')
    return emit('build-windows', 'fail', code, (errs[:1] or [((err or out).strip().splitlines() or ['?'])[-1]])[0],
                errors_file=str(ef), log=str(log), out=outf, lock_owner=owner)


def g_parity(a):
    proj = Path(a.project).resolve()
    it = C.load_item(Path(a.items_dir) / f'{a.item}.md')
    row = it.get('art_row', 'none')
    if row in (None, 'none'):
        return emit('parity', 'skipped', C.SKIP_EXIT, f'skipped: {a.item} has no art_row', item=a.item)
    if not (proj / CAPTURE_TOOL).exists():
        return emit('parity', 'skipped', C.SKIP_EXIT, f'skipped: {CAPTURE_TOOL} does not exist yet (U-010, U-017)',
                    item=a.item, art_row=row)
    r = refuse_unity('parity', proj)
    if r is not None:
        return r
    code, out, err, owner = run_locked('parity', [str(proj / CAPTURE_TOOL), '--row', row], proj)
    short = C.git('rev-parse', '--short', 'HEAD', cwd=proj)[1].strip()
    rj = proj / f'Logs/parity/{short}/{row}/results.json'  # U-017 output layout
    res = C.read_json(rj)
    if code != 0 or res is None:
        return emit('parity', 'fail', code or 1, f'capture failed or wrote no {rj}', item=a.item, art_row=row,
                    lock_owner=owner)
    fails = [f for f in (res.get('framings') or []) if not f.get('measured_pass')]
    if fails:
        return emit('parity', 'fail', 1, f'{len(fails)} framing(s) fail the measured checks', item=a.item,
                    art_row=row, results=str(rj))
    return emit('parity', 'pass', 0, 'measured checks pass: the row is ready for the team (never a row PASS, U-010)',
                item=a.item, art_row=row, results=str(rj))


def scope_violations(item, files):
    allowed = item.get('files_allowed') or []
    forbidden = item.get('files_forbidden') or []
    approved = item.get('architect_approved')
    v = []
    for f in files:
        if C.hand_written_unity_data(f, approved):  # U-027 first: a files_allowed glob like Foo.cs* never admits a meta
            v.append(f'{f}: {C.hand_written_unity_data(f, approved)}')
        elif any(C.glob_match(f, g) for g in forbidden):
            v.append(f'{f}: matches files_forbidden')
        elif not any(C.glob_match(f, g) for g in allowed):
            v.append(f'{f}: not in files_allowed')
        elif C.is_guarded(f) and not approved:
            v.append(f'{f}: guarded path without architect_approved')
        elif f.startswith('DECISIONS.md'):
            v.append(f'{f}: builders never write DECISIONS.md (U-011)')
    return v


def g_scope(a):
    it = C.load_item(Path(a.items_dir) / f'{a.item}.md')
    try:
        files = C.changed_files(a.base, a.ref, cwd=a.project)
    except RuntimeError as e:
        return emit('scope', 'fail', 2, str(e), item=a.item, ref=a.ref)
    v = scope_violations(it, files)
    if v:
        return emit('scope', 'fail', 1, f'{len(v)} file(s) outside the item scope', item=a.item, ref=a.ref,
                    base=a.base, violations=v[:30])
    return emit('scope', 'pass', 0, f'{len(files)} file(s) changed, all inside files_allowed', item=a.item,
                ref=a.ref, base=a.base, files=files[:30])


def _summary_line(out):
    for line in reversed((out or '').strip().splitlines()):
        line = line.strip()
        if line.startswith('{'):
            try:
                return json.loads(line)
            except ValueError:
                return None
    return None


def g_perf(a):
    """DG-54: the built player against tools/budgets.json; pass only when every pass ran and every number is measured
    and within budget. A missing app, a missing perf.json or a missing number is a fail, never a skip."""
    proj = Path(a.project).resolve()
    tool = proj / PERF_TOOL
    if not tool.exists():
        return emit('perf', 'fail', 66, f'{PERF_TOOL} missing', project=str(proj))
    passes = [p.strip() for p in os.environ.get('CM_PERF_PASSES', 'window,fullscreen,floor').split(',') if p.strip()]
    results, worst = {}, 0
    for name in passes:
        if name not in PERF_PASSES:
            return emit('perf', 'fail', 64, f'unknown perf pass {name}', passes=passes)
        extra, env = PERF_PASSES[name]
        out_dir = proj / 'Logs' / 'perf-gate' / f'{C.utc().replace(":", "")}-{name}'
        p = subprocess.run(['bash', str(tool), '--out', str(out_dir), *extra], cwd=proj, capture_output=True, text=True,
                           env={**os.environ, **env})
        summ = _summary_line(p.stdout)
        ok = p.returncode == 0 and bool(summ) and summ.get('pass') is True
        results[name] = {'exit': p.returncode, 'pass': ok, 'summary': summ, 'out': str(out_dir),
                         'stderr': (p.stderr or '').strip().splitlines()[-3:]}
        if not ok:
            worst = worst or (p.returncode or 1)
    failed = [n for n, r in results.items() if not r['pass']]
    if failed:
        nm = sorted({k for n in failed for k in ((results[n]['summary'] or {}).get('not_measured') or [])})
        over = sorted({k for n in failed for k in ((results[n]['summary'] or {}).get('over_budget') or [])})
        return emit('perf', 'fail', worst, f'failed: {", ".join(failed)}' + (f'; not measured: {", ".join(nm)}' if nm else '') +
                    (f'; over budget: {", ".join(over)}' if over else ''), passes=results)
    return emit('perf', 'pass', 0, f'{len(passes)} pass(es) measured and within budget', passes=results)


def g_player_soak(a):
    """DG-55: tools/player-soak.sh (2 hours); pass only on soak.json pass=true."""
    proj = Path(a.project).resolve()
    tool = proj / PLAYER_SOAK_TOOL
    if not tool.exists():
        return emit('player-soak', 'fail', 66, f'{PLAYER_SOAK_TOOL} missing', project=str(proj))
    p = subprocess.run(['bash', str(tool)], cwd=proj, capture_output=True, text=True)
    summ = _summary_line(p.stdout)
    ok = p.returncode == 0 and bool(summ) and summ.get('pass') is True
    return emit('player-soak', 'pass' if ok else 'fail', 0 if ok else (p.returncode or 1),
                json.dumps(summ)[:300] if summ else ((p.stderr or 'no summary').strip().splitlines() or ['no summary'])[-1][:200],
                summary=summ)


def _common_dir(path):
    """The absolute git common dir of the checkout holding path, or None outside git."""
    try:
        out = subprocess.run(['git', '-C', str(path), 'rev-parse', '--path-format=absolute', '--git-common-dir'],
                             capture_output=True, text=True, check=True).stdout.strip()
        return Path(out).resolve()
    except Exception:
        return None


def default_project(cwd=None, repo=None):
    """DG-05: the checkout a gate runs in when --project is not given. It is the git worktree holding cwd when that
    worktree belongs to this repository (same git common dir), else the checkout holding this copy of the gates
    (C.REPO). Never the main checkout by default: before DG-05 the default was C.MAIN, so `loop/gates/build-mac` run
    from a worktree built in the main checkout."""
    repo = Path(repo or C.REPO).resolve()
    cwd = Path(cwd or os.getcwd()).resolve()
    common = _common_dir(repo)
    if common is not None and _common_dir(cwd) == common:
        try:
            top = subprocess.run(['git', '-C', str(cwd), 'rev-parse', '--show-toplevel'], capture_output=True,
                                 text=True, check=True).stdout.strip()
            return Path(top).resolve()
        except Exception:
            pass
    return repo


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('gate', choices=['setup', 'clean-reimport', 'compile', 'editmode', 'playmode-smoke', 'build-mac', 'build-windows',
                                     'parity', 'scope', 'perf', 'player-soak', 'sim-long', 'soak'])
    ap.add_argument('--project', default=None, help='default: the worktree invoked from (default_project)')
    ap.add_argument('--item')
    ap.add_argument('--items-dir', default=str(C.ITEMS_DIR))
    ap.add_argument('--ref')
    ap.add_argument('--base', default='origin/main')
    a = ap.parse_args(argv)
    if a.project is None:
        a.project = str(default_project())
    if a.gate in ('parity', 'scope') and not a.item:
        ap.error(f'{a.gate} needs --item')
    if a.gate == 'scope' and not a.ref:
        ap.error('scope needs --ref')
    fn = {'setup': g_setup, 'clean-reimport': g_clean_reimport, 'soak': g_soak, 'compile': g_compile, 'editmode': g_editmode, 'playmode-smoke': g_playmode, 'build-mac': g_build_mac,
          'build-windows': g_build_windows, 'parity': g_parity, 'scope': g_scope, 'perf': g_perf,
          'player-soak': g_player_soak, 'sim-long': g_soak}[a.gate]
    try:
        return fn(a)
    except Exception as e:  # an internal error is a fail with its own code, never a pass
        return emit(a.gate, 'fail', 70, f'gate internal error: {type(e).__name__}: {e}')


if __name__ == '__main__':
    sys.exit(main())
