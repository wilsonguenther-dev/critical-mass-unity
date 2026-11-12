#!/usr/bin/env python3
"""The loop's gates (decision 4). Each gate prints ONE JSON line, appends it to loop/logs/gates.jsonl and exits with
the real exit code: 0 pass, the tool's own non-zero code on fail, 78 skipped. "skipped" is never exit 0, so nothing
that reads exit codes can mistake a skip for a pass. `budgets` exits 0 with status "reported" or "flagged": it never
fails an item; a flag goes to the architect.

  gate.py setup          [--project DIR]                    -executeMethod CriticalMass.Editor.ProjectSetup.Apply (U-029)
  gate.py compile        [--project DIR]                    tools/unity-compile.sh
  gate.py editmode       [--project DIR]                    tools/unity-test.sh, category "!Slow" (fails on zero tests)
  gate.py playmode-smoke [--project DIR]                    tools/unity-playtest.sh (assembly Runtime.PlayTests, F-08c)
  gate.py soak           [--project DIR]                    tools/unity-soak.sh: the "Slow" tests, after a batch lands (U-028)
  gate.py build-mac      [--project DIR]                    tools/unity-build-mac.sh
  gate.py build-windows  [--project DIR]                    tools/unity-build-windows.sh (skipped 78 without the module)
  gate.py parity  --item ID [--project DIR]                 tools/unity-capture.sh (art_row items only)
  gate.py scope   --item ID --ref REF [--base REF]          git only: the diff touches only files_allowed
  gate.py budgets        [--project DIR]                    frame time, draw calls, shader variants: report only

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
BUDGETS_FILE = C.LOOP / 'budgets.json'
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
    if code == 0 and total > 0:
        return emit('editmode', 'pass', 0, f'{passed}/{total} EditMode tests passed (category !Slow)', total=total,
                    failed=0, category='!Slow',
                    results=str(res), out=outf, lock_owner=owner)
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
        return emit('build-mac', 'pass', 0, 'Builds/macOS/CriticalMass.app built', log=str(log), out=outf,
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
        return emit('build-windows', 'pass', 0, 'Builds/Windows/CriticalMass.exe built', log=str(log), out=outf,
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


def g_budgets(a):
    proj = Path(a.project).resolve()
    if not (proj / SMOKE_SCENE).exists():
        return emit('budgets', 'skipped', C.SKIP_EXIT, f'skipped: no game scene ({SMOKE_SCENE} does not exist)')
    limits = C.read_json(BUDGETS_FILE, {}) or {}
    perf = C.read_json(proj / 'Logs/perf.json', {}) or {}  # written by the Smoke PlayMode test (an item's job)
    log = proj / 'Logs/build-mac.log'
    variants = None
    if log.exists():
        nums = [int(x) for x in re.findall(r'(\d+)\s+(?:shader\s+)?variants', log.read_text(errors='replace'))]
        variants = sum(nums) if nums else None
    meas = {'frame_ms': perf.get('frame_ms'), 'draw_calls': perf.get('draw_calls'), 'shader_variants': variants}
    over = [k for k, v in meas.items() if v is not None and limits.get(k) is not None and v > limits[k]]
    missing = [k for k, v in meas.items() if v is None]
    st = 'flagged' if over else 'reported'
    if over:
        C.append_jsonl(C.STATE / 'architect-flags.jsonl', {'ts': C.utc(), 'kind': 'budget', 'over': over, **meas})
    return emit('budgets', st, 0, (f'over budget: {", ".join(over)}; flagged for the architect' if over else
                                   'measured and reported') + (f'; not measured: {", ".join(missing)}' if missing else ''),
                measured=meas, limits=limits)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('gate', choices=['setup', 'clean-reimport', 'compile', 'editmode', 'playmode-smoke', 'build-mac', 'build-windows',
                                     'parity', 'scope', 'budgets', 'soak'])
    ap.add_argument('--project', default=str(C.MAIN))
    ap.add_argument('--item')
    ap.add_argument('--items-dir', default=str(C.ITEMS_DIR))
    ap.add_argument('--ref')
    ap.add_argument('--base', default='origin/main')
    a = ap.parse_args(argv)
    if a.gate in ('parity', 'scope') and not a.item:
        ap.error(f'{a.gate} needs --item')
    if a.gate == 'scope' and not a.ref:
        ap.error('scope needs --ref')
    fn = {'setup': g_setup, 'clean-reimport': g_clean_reimport, 'soak': g_soak, 'compile': g_compile, 'editmode': g_editmode, 'playmode-smoke': g_playmode, 'build-mac': g_build_mac,
          'build-windows': g_build_windows, 'parity': g_parity, 'scope': g_scope, 'budgets': g_budgets}[a.gate]
    try:
        return fn(a)
    except Exception as e:  # an internal error is a fail with its own code, never a pass
        return emit(a.gate, 'fail', 70, f'gate internal error: {type(e).__name__}: {e}')


if __name__ == '__main__':
    sys.exit(main())
