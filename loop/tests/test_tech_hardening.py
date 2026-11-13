"""Technical hardening (DG-54 to DG-57, DG-66): the perf gate fails closed, the soak and smoke summaries score what they
measured, the Windows screenshot check rejects a black frame. No Unity, no player: synthetic run folders only."""
import importlib.util
import json
import os
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

import cmloop as C

TOOLS = C.REPO / 'tools'


def _gate():
    spec = importlib.util.spec_from_file_location('gate_tech_under_test', str(C.REPO / 'loop' / 'gates' / 'gate.py'))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def _smoke_run(tmp, perf, log_extra='', idle_cpu_s_per_s=0.15, title_ms=1000):
    out = tmp / 'run'
    out.mkdir()
    (out / 'Player.log').write_text(f'[CM.Boot] title on screen 2.1 s after engine start, 2.6 s after process start, utc_ms={title_ms}\n'
                                    '[CM.Autoplay] MATCH END winner=Azure\n' + log_extra)
    rows = ['ms\trss_kb\tcputime\ttitle\tmatch_ends']
    for i in range(16):
        cpu = 5 + i * idle_cpu_s_per_s
        rows.append(f'{title_ms + i * 1000}\t{900 * 1024}\t0:{cpu:05.2f}\t1\t{1 if i >= 14 else 0}')
    (out / 'samples.tsv').write_text('\n'.join(rows) + '\n')
    (out / 'perf.json').write_text(json.dumps(perf))
    return out


def _perf(**kw):
    p = {'quality': 'High', 'quality_requested': 'High', 'safe_mode': False, 'data_isolated': True,
         'frame_ms': {'p95': 15.0, 'worst': 120.0}, 'render_pixels': 2_000_000,
         'markers': [{'name': 'Batches Count', 'max': 900}, {'name': 'Triangles Count', 'max': 3_000_000}]}
    p.update(kw)
    return p


def _summarise(out, extra_env=None):
    env = {**os.environ, 'CM_SMOKE_EXIT': '0', 'CM_SMOKE_LAUNCH_MS': '0', **(extra_env or {})}
    p = subprocess.run(['bash', str(TOOLS / 'demo-smoke.sh'), '--matches', '1', '--summarise-only', str(out)],
                       capture_output=True, text=True, env=env)
    return p.returncode, json.loads((out / 'summary.json').read_text())


def test_smoke_passes_a_run_within_budget(tmp_path):
    code, s = _summarise(_smoke_run(tmp_path, _perf()))
    assert s['not_measured'] == [] and s['over_budget'] == [] and s['problems'] == [], s
    assert code == 0 and s['pass'] is True
    assert 14 <= s['measured']['idle_cpu_pct'] <= 16


def test_smoke_fails_closed_when_a_number_is_missing(tmp_path):
    perf = _perf()
    perf['markers'] = [{'name': 'Batches Count', 'max': 900}]  # no triangle count
    code, s = _summarise(_smoke_run(tmp_path, perf))
    assert code == 1 and s['pass'] is False
    assert 'triangles' in s['not_measured']


def test_smoke_triangle_budget_is_per_quality_level(tmp_path):
    code, s = _summarise(_smoke_run(tmp_path, _perf(quality='Low', quality_requested='Low',
                                                    markers=[{'name': 'Batches Count', 'max': 900},
                                                             {'name': 'Triangles Count', 'max': 3_000_000}])),
                         {'CM_PROFILE': 'default'})
    # --quality defaults to High under the default profile, so this also reports the quality mismatch
    assert code == 1
    assert 'triangles' in s['over_budget'], s['budgets']['triangles']


def test_smoke_rejects_safe_mode_and_shared_settings(tmp_path):
    code, s = _summarise(_smoke_run(tmp_path, _perf(safe_mode=True, data_isolated=False)))
    assert code == 1
    assert 'started in safe mode' in s['problems']
    assert any('own data folder' in p for p in s['problems'])


def test_smoke_counts_exceptions(tmp_path):
    code, s = _summarise(_smoke_run(tmp_path, _perf(), log_extra='NullReferenceException: Object reference not set\n'))
    assert code == 1 and s['exception_lines'] == 1


def test_budgets_file_has_every_section_and_tiered_triangles():
    b = json.loads((TOOLS / 'budgets.json').read_text())
    for k in ('peak_rss_mb', 'growth_mb', 'p95_ms', 'worst_hitch_ms', 'idle_cpu_pct', 'batches', 'triangles', 'render_pixels_max'):
        assert k in b['default'], k
    assert b['default']['triangles']['Low'] < b['default']['triangles']['Medium'] < b['default']['triangles']['High']
    assert b['default']['idle_cpu_pct'] == 35
    assert {'gpu_p95_ms', 'cpu_main_p95_ms'} <= set(b['floor'])
    assert b['soak']['min_matches'] >= 20 and b['soak']['min_seconds'] >= 7200


def test_perf_gate_fails_when_the_tool_or_a_pass_is_missing(tmp_path, monkeypatch):
    G = _gate()
    monkeypatch.setattr(G, 'emit', lambda gate, status, code, detail, **kw: (status, code, detail))
    st, code, _ = G.g_perf(SimpleNamespace(project=str(tmp_path)))
    assert st == 'fail' and code != 0
    (tmp_path / 'tools').mkdir()
    (tmp_path / 'tools' / 'demo-smoke.sh').write_text('#!/usr/bin/env bash\necho \'{"pass": false, "not_measured": ["triangles"]}\'\nexit 1\n')
    monkeypatch.setenv('CM_PERF_PASSES', 'window')
    st, code, detail = G.g_perf(SimpleNamespace(project=str(tmp_path)))
    assert st == 'fail' and 'not measured: triangles' in detail


def test_perf_gate_passes_only_on_a_pass_summary(tmp_path, monkeypatch):
    G = _gate()
    monkeypatch.setattr(G, 'emit', lambda gate, status, code, detail, **kw: (status, code, detail))
    (tmp_path / 'tools').mkdir()
    (tmp_path / 'tools' / 'demo-smoke.sh').write_text('#!/usr/bin/env bash\necho noise\necho \'{"pass": true}\'\n')
    monkeypatch.setenv('CM_PERF_PASSES', 'window,floor')
    st, code, _ = G.g_perf(SimpleNamespace(project=str(tmp_path)))
    assert st == 'pass' and code == 0


def test_editmode_counts_come_from_the_results_file(tmp_path):
    G = _gate()
    x = tmp_path / 'r.xml'
    x.write_text('<test-run><test-suite>'
                 '<test-case fullname="A.ok" result="Passed"/><test-case fullname="A.skip" result="Skipped"/>'
                 '<test-case fullname="A.inc" result="Inconclusive"/><test-case fullname="A.bad" result="Failed"/>'
                 '</test-suite></test-run>')
    c = G.result_counts(str(x))
    assert (c['total'], c['passed'], c['failed'], c['skipped'], c['inconclusive']) == (4, 1, 1, 1, 1)
    assert c['skipped_names'] == ['A.skip'] and c['inconclusive_names'] == ['A.inc']
    assert G.result_counts(str(tmp_path / 'missing.xml')) is None


def test_gate_choices_have_perf_and_keep_the_soak_alias():
    src = (C.REPO / 'loop' / 'gates' / 'gate.py').read_text()
    assert "'perf'" in src and "'player-soak'" in src and "'sim-long'" in src and "'soak': g_soak" in src
    assert 'g_budgets' not in src
    assert not (C.REPO / 'loop' / 'budgets.json').exists()


def _soak_run(tmp, matches, slope_mb, seconds, objects_equal=True):
    out = tmp / 'soak'
    out.mkdir()
    rows = ['s\trss_kb\tlog_kb\topen_files\tthreads\tmatch_ends']
    step = seconds / (matches * 4)
    for i in range(matches * 4 + 1):
        m = i // 4
        rows.append(f'{int(i * step)}\t{int((800 + m * slope_mb) * 1024)}\t{200 + i}\t120\t40\t{m}')
    (out / 'soak-samples.tsv').write_text('\n'.join(rows) + '\n')
    (out / 'Player.log').write_text('[CM.Autoplay] MATCH END\n' * matches)
    for k in range(1, matches + 1):
        objs = {'meshes': 400, 'materials': 90} if objects_equal or k < 3 else {'meshes': 460, 'materials': 90}
        (out / ('perf.json' if k == 1 else f'perf-m{k}.json')).write_text(json.dumps({'frame_ms': {'worst': 90}, 'objects': objs}))
    return out


def _soak(out, matches, min_s):
    p = subprocess.run(['bash', str(TOOLS / 'player-soak.sh'), '--matches', str(matches), '--min-seconds', str(min_s),
                        '--summarise-only', str(out)], capture_output=True, text=True, env={**os.environ, 'CM_SOAK_EXIT': '0'})
    return p.returncode, json.loads((out / 'soak.json').read_text())


def test_soak_passes_a_flat_two_hour_run(tmp_path):
    code, s = _soak(_soak_run(tmp_path, 20, 0.5, 7300), 20, 7200)
    assert code == 0 and s['pass'] is True, s['checks']


def test_soak_fails_on_a_leak_or_a_short_run(tmp_path):
    code, s = _soak(_soak_run(tmp_path, 20, 6.0, 7300, objects_equal=False), 20, 7200)
    assert code == 1
    assert s['checks']['rss_slope'] is False and s['checks']['objects_m3_equal_m2'] is False
    short = tmp_path / 'short'
    short.mkdir()
    code, s = _soak(_soak_run(short, 20, 0.5, 3000), 20, 7200)
    assert code == 1 and s['checks']['seconds'] is False


def test_screenshot_check_rejects_black_and_flat_frames():
    sys.path.insert(0, str(TOOLS / 'tests'))
    import check_screenshot as S
    assert S.verdict([(0, 0, 0)] * 1000)[0] is False
    assert S.verdict([(200, 200, 200)] * 1000)[0] is False, 'one flat colour'
    scene = [(30, 120, 60)] * 900 + [(220, 40, 40)] * 100
    assert S.verdict(scene)[0] is True
    assert S.verdict([])[0] is False


def test_every_tool_script_parses():
    for name in ('demo-smoke.sh', 'player-soak.sh', 'sim-hash.sh', 'package.sh', 'play-latest.sh'):
        p = subprocess.run(['bash', '-n', str(TOOLS / name)], capture_output=True, text=True)
        assert p.returncode == 0, (name, p.stderr)


def test_smoke_isolates_the_owners_settings():
    src = (TOOLS / 'demo-smoke.sh').read_text()
    assert '-dataDir' in src and 'defaults export' in src and 'defaults import' in src and 'trap restore_prefs EXIT' in src
    soak = (TOOLS / 'player-soak.sh').read_text()
    assert '-dataDir' in soak and 'defaults export' in soak and 'STOP-DISPATCH' in soak
