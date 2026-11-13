#!/usr/bin/env python3
"""Machine-safety acceptance checks (DG-43, DG-44, DG-45; TECH-HARDENING-PLAN-2026-11-12). macOS only, dummy jobs only:
nothing here starts Unity or Blender. Every job is `true`, `sleep`, or `sleep` renamed with `exec -a`.

    python3 loop/tests/machine/run_machine_safety_tests.py [--quick]

cm-heavy runs with a private state dir (CM_HEAVY_STATE_DIR), a private incidents dir (CM_INCIDENTS_DIR) and a private
old-lock dir, so these tests never take the fleet's real slots. Two signals are machine-wide by design and are used as
the plan's checks describe: a dummy process named "Critical Mass" (owner-play mode; real cm-heavy jobs on this Mac also
wait while it runs, about 70 s) and `pgrep -x Unity/Blender`. --quick skips the two slow checks (owner play, 3 jobs).
Takes about 3 minutes in full. Exit 0 = all passed.
"""
import ctypes
import ctypes.util
import json
import os
import signal
import subprocess
import sys
import tempfile
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
CM_HEAVY = os.environ.get('CM_HEAVY') or str(Path.home() / 'bin' / 'cm-heavy')
GUARD = os.environ.get('CM_MACHINE_GUARD') or str(Path.home() / 'bin' / 'cm-machine-guard')
TMP = Path(tempfile.mkdtemp(prefix='cm-machine-safety-'))
ENV = {**os.environ, 'CM_HEAVY_STATE_DIR': str(TMP / 'state'), 'CM_HEAVY_LOG': str(TMP / 'cm-heavy.jsonl'),
       'CM_INCIDENTS_DIR': str(TMP / 'inc'), 'CM_HEAVY_OLD_LOCKS': str(TMP / 'old-locks'),
       'CM_HEAVY_TEST_PGREP_NAMES': 'cmtest-real-unity,cmtest-real-blender'}
(TMP / 'inc').mkdir()
L = ctypes.CDLL(ctypes.util.find_library('c'))
RESULTS = []
STARTED = []   # every Popen this file makes, so nothing outlives the run


def pids_named(name):
    """In-process `pgrep -x` equivalent (kernel name or argv[0] basename); no subprocess per sample."""
    n = L.proc_listallpids(None, 0)
    buf = (ctypes.c_int * (n + 512))()
    n = L.proc_listallpids(buf, ctypes.sizeof(buf))
    out, nm, args = [], ctypes.create_string_buffer(256), ctypes.create_string_buffer(8192)
    for p in buf[:max(n, 0)]:
        if p <= 0:
            continue
        if L.proc_name(p, nm, 256) > 0 and nm.value.decode() == name:
            out.append(p)
            continue
        mib = (ctypes.c_int * 3)(1, 49, p)
        sz = ctypes.c_size_t(8192)
        if L.sysctl(mib, 3, args, ctypes.byref(sz), None, 0) == 0 and sz.value > 8:
            rest = args.raw[4:sz.value]
            rest = rest[rest.find(b'\0'):].lstrip(b'\0')
            if rest.split(b'\0', 1)[0].rsplit(b'/', 1)[-1].decode('utf-8', 'replace') == name:
                out.append(p)
    return out


def heavy(kind, *cmd, env=None, **kw):
    p = subprocess.Popen([CM_HEAVY, kind, *cmd], env={**ENV, **(env or {})}, stdout=subprocess.PIPE,
                         stderr=subprocess.PIPE, text=True, **kw)
    STARTED.append(p)
    return p


def dummy(name, seconds, ignore_term=False):
    trap = 'trap "" TERM; ' if ignore_term else ''
    return ['/bin/bash', '-c', f'{trap}exec -a "{name}" /bin/sleep {seconds}']


def check(name, ok, detail=''):
    RESULTS.append((name, bool(ok), detail))
    print(f'{"PASS" if ok else "FAIL"}  {name}  {detail}', flush=True)


def wait_until(pred, limit, step=0.5):
    end = time.time() + limit
    while time.time() < end:
        if pred():
            return True
        time.sleep(step)
    return pred()


def slot_held(slot='unity'):
    import fcntl
    f = TMP / 'state' / f'slot-{slot}.lock'
    if not f.exists():
        return False
    fd = os.open(f, os.O_RDWR)
    try:
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        fcntl.flock(fd, fcntl.LOCK_UN)
        return False
    except BlockingIOError:
        return True
    finally:
        os.close(fd)


def t_wrappers_present():
    r = subprocess.run('grep -L cm-heavy tools/unity-*.sh tools/art-sync.sh', shell=True, cwd=REPO,
                       capture_output=True, text=True)
    check('DG-44 grep -L cm-heavy tools/unity-*.sh tools/art-sync.sh prints nothing', r.stdout.strip() == '',
          r.stdout.strip())
    gate = (REPO / 'loop/gates/gate.py').read_text()
    check('DG-44 gate.py starts Unity only through cm-heavy', gate.count("cm_heavy(), 'unity', unity_binary(proj)") == 2)
    bare = subprocess.run(r'grep -n -E "^[[:space:]]*(perl[^#]*)?\"\$UNITY\" " tools/*.sh', shell=True, cwd=REPO,
                          capture_output=True, text=True)
    check('DG-44 no bare "$UNITY" launch left in tools/*.sh', bare.stdout.strip() == '', bare.stdout.strip())


def t_timeout():
    t = time.time()
    p = heavy('unity', '/bin/sleep', '9999', env={'CM_HEAVY_TIMEOUT': '10'})
    rc = p.wait(30)
    took = time.time() - t
    rec = [json.loads(x) for x in (TMP / 'cm-heavy.jsonl').read_text().splitlines() if '"grant"' in x][-1]
    gone = wait_until(lambda: subprocess.run(['/bin/kill', '-0', str(rec['pgid'])], capture_output=True).returncode != 0, 3)
    check('DG-44 CM_HEAVY_TIMEOUT=10 cm-heavy unity sleep 9999 exits non-zero within 15 s, no sleep left',
          rc != 0 and took < 15 and gone, f'rc={rc} took={took:.1f}s job_gone={gone}')


def t_kill9_wrapper():
    p = heavy('unity', *dummy('cmtest-orphan', 300, ignore_term=True))
    ok_start = wait_until(lambda: pids_named("cmtest-orphan"), 45)
    os.kill(p.pid, signal.SIGKILL)
    p.wait()
    time.sleep(1)
    held_while_child = bool(pids_named('cmtest-orphan')) and slot_held()
    gone = wait_until(lambda: not pids_named('cmtest-orphan'), 10)
    freed = wait_until(lambda: not slot_held(), 5)
    check('DG-44 kill -9 of the wrapper: no orphan within 10 s; slot held until the child is gone',
          ok_start and held_while_child and gone and freed,
          f'started={ok_start} held_while_child_alive={held_while_child} orphan_gone={gone} slot_freed={freed}')


def t_three_jobs():
    (TMP / 'state' / 'last-unity-end').unlink(missing_ok=True)
    ps = [heavy('unity', *dummy('cmtest-one', 3)) for _ in range(3)]
    peak, t = 0, time.time()
    while any(p.poll() is None for p in ps) and time.time() - t < 240:
        peak = max(peak, len(pids_named('cmtest-one')))
        time.sleep(1)
    rcs = [p.poll() for p in ps]
    check('DG-44 three dummy jobs started together never have more than 1 alive (sampled every second)',
          peak == 1 and rcs == [0, 0, 0], f'peak={peak} rcs={rcs} took={time.time() - t:.0f}s (20 s gap between)')


def t_unity_gap_and_blender_spacing():
    (TMP / 'state' / 'last-unity-end').write_text(f'{time.time():.3f}\n')
    p = heavy('unity', '/usr/bin/true', env={'CM_HEAVY_MAX_WAIT': '5'})
    rc = p.wait(30)
    err = p.stderr.read()
    check('DG-44 a Unity job waits 20 s after the previous one ended', rc == 75 and 'minimum gap 20 s' in err,
          f'rc={rc}')
    (TMP / 'state' / 'last-unity-end').unlink(missing_ok=True)
    (TMP / 'state' / 'last-blender-start').unlink(missing_ok=True)
    a = heavy('blender', '/usr/bin/true')
    ra = a.wait(30)
    b = heavy('blender', '/usr/bin/true', env={'CM_HEAVY_MAX_WAIT': '5'})
    rb = b.wait(30)
    err = b.stderr.read()
    check('coordinator: one Blender start per 120 s machine-wide (second start waits)',
          ra == 0 and rb == 75 and 'one Blender start per 120 s' in err, f'first={ra} second={rb}')
    (TMP / 'state' / 'last-blender-start').unlink(missing_ok=True)
    c = heavy('blender', '/nonexistent/Blender', '--python', 'x.py')
    rc = c.wait(10)
    check('coordinator: a Blender command line without -b is refused (exit 64) and never started', rc == 64, f'rc={rc}')


def t_blender_cap_while_unity():
    (TMP / 'state' / 'last-unity-end').unlink(missing_ok=True)
    (TMP / 'state' / 'last-blender-start').unlink(missing_ok=True)
    u = heavy('unity', *dummy('cmtest-u', 20))
    wait_until(lambda: pids_named('cmtest-u'), 20)
    b1 = heavy('blender', *dummy('cmtest-b', 15))
    wait_until(lambda: pids_named('cmtest-b'), 20)
    (TMP / 'state' / 'last-blender-start').unlink(missing_ok=True)   # take the 120 s spacing out of this check
    b2 = heavy('blender', '/usr/bin/true', env={'CM_HEAVY_MAX_WAIT': '5'})
    r2 = b2.wait(30)
    err = b2.stderr.read()
    for p in (u, b1):
        p.wait(60)
    check('DG-44 Blender cap is 1 while a Unity job runs', r2 == 75 and 'cap 1 while a Unity job' in err, f'rc={r2}')


def t_real_process_check():
    (TMP / 'state' / 'last-unity-end').unlink(missing_ok=True)
    real = subprocess.Popen(dummy('cmtest-real-unity', 60))
    STARTED.append(real)
    wait_until(lambda: pids_named('cmtest-real-unity'), 10)
    p = heavy('unity', '/usr/bin/true', env={'CM_HEAVY_MAX_WAIT': '5'})
    rc = p.wait(30)
    err = p.stderr.read()
    real.kill()
    real.wait()
    check('DG-44 a real Unity process outside the lock (pgrep -x) holds new Unity jobs', rc == 75 and 'pgrep -x' in err,
          f'rc={rc}')


def t_holds():
    p = heavy('unity', '/usr/bin/true', env={'CM_HEAVY_TEST_UPTIME_S': '300', 'CM_HEAVY_MAX_WAIT': '5'})
    rc = p.wait(30)
    check('DG-43 with an uptime under 15 minutes a heavy job waits', rc == 75 and 'booted' in p.stderr.read(), f'rc={rc}')
    for marker in ('STOP-DISPATCH', 'PAUSED-AFTER-UNCLEAN-RESTART'):
        (TMP / 'inc' / marker).write_text('test marker\n')
        p = heavy('blender', '/usr/bin/true', env={'CM_HEAVY_MAX_WAIT': '5'})
        rc = p.wait(30)
        err = p.stderr.read()
        sys.path.insert(0, str(REPO / 'loop/bin'))
        os.environ['CM_INCIDENTS_DIR'] = str(TMP / 'inc')
        import importlib
        import cmloop
        importlib.reload(cmloop)
        hold = cmloop.machine_hold()
        check(f'DG-43 {marker}: cm-heavy waits and the dispatcher hold is on', rc == 75 and marker in err and hold
              and marker in hold, f'rc={rc} hold={hold!r}')
        (TMP / 'inc' / marker).unlink()


def t_owner_play():
    (TMP / 'state' / 'last-unity-end').unlink(missing_ok=True)
    player = subprocess.Popen(dummy('Critical Mass', 300))
    STARTED.append(player)
    seen = wait_until(lambda: pids_named('Critical Mass'), 10)
    sys.path.insert(0, str(REPO / 'loop/bin'))
    import cmloop
    hold = cmloop.machine_hold()
    job = heavy('unity', '/usr/bin/true')
    time.sleep(60)
    still = job.poll() is None
    player.terminate()
    player.wait(10)
    t = time.time()
    try:
        rc = job.wait(60)
    except subprocess.TimeoutExpired:
        rc = None
    err = job.stderr.read() if rc is not None else ''
    check('DG-43 dummy "Critical Mass" running: cm-heavy unity true still waiting after 60 s; dispatcher refuses',
          seen and still and hold and 'owner playing' in hold and 'owner playing' in err, f'hold={hold!r}')
    check('DG-43 dummy killed: the job starts within 60 s', rc == 0, f'rc={rc} after {time.time() - t:.0f}s')


def t_guard():
    env = {**ENV, 'CM_GUARD_DISK_FLOOR_GB': '100000', 'CM_GUARD_MAX_NEW_PER_MIN': '100000'}
    t = time.time()
    r = subprocess.run([GUARD, '--once'], env=env, capture_output=True, text=True, timeout=60)
    stop = TMP / 'inc' / 'STOP-DISPATCH'
    ok = stop.exists() and 'GB disk free' in stop.read_text()
    check('DG-45 CM_GUARD_DISK_FLOOR_GB above the real free space: STOP-DISPATCH within 60 s',
          ok and time.time() - t < 60, f'rc={r.returncode} {stop.read_text().splitlines()[0] if ok else ""}')
    stop.unlink(missing_ok=True)
    # a synthetic burst of forks at well over 400 a minute, measured over a 5 s sample (60 forks, not 400)
    g = subprocess.Popen([GUARD, '--once'], env={**ENV, 'CM_GUARD_MAX_NEW_PER_MIN': '400'})
    STARTED.append(g)
    end = time.time() + 4
    while time.time() < end:
        pid = os.fork()
        if pid == 0:
            os._exit(0)
        os.waitpid(pid, 0)
        time.sleep(0.05)
    g.wait(60)
    ok = stop.exists() and 'new processes per minute' in stop.read_text()
    check('DG-45 a synthetic fork burst over 400 a minute trips STOP-DISPATCH', ok,
          stop.read_text().splitlines()[0] if ok else '')
    lines = (TMP / 'inc' / 'machine-guard.jsonl').read_text().splitlines()
    rec = json.loads(lines[-1])
    want = {'new_procs_per_min', 'ps_ms', 'launchctl_ms', 'load', 'mem_free_gb', 'swap_used_gb', 'disk_free_gb',
            'thermal', 'unity', 'blender', 'player'}
    check('DG-45 every sample line carries the fields', want <= set(rec), str(sorted(want - set(rec))))
    stop.unlink(missing_ok=True)


def main():
    quick = '--quick' in sys.argv
    if sys.platform != 'darwin':
        print('macOS only')
        return 0
    try:
        t_wrappers_present()
        t_timeout()
        t_kill9_wrapper()
        t_unity_gap_and_blender_spacing()
        t_blender_cap_while_unity()
        t_real_process_check()
        t_holds()
        t_guard()
        if not quick:
            t_three_jobs()
            t_owner_play()
    finally:
        for p in STARTED:
            if p.poll() is None:
                p.kill()
                p.wait()
    bad = [r for r in RESULTS if not r[1]]
    import shutil
    shutil.rmtree(TMP, ignore_errors=True)   # the run deletes what it brought in
    print(f'\n{len(RESULTS) - len(bad)}/{len(RESULTS)} passed')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
