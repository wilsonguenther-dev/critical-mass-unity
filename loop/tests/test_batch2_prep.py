"""Architect batch-2 prep (2026-11-12): U-033 per-item coupling, U-034 soak report, U-037 builder limit, U-038 returned
lanes and upstream reds, U-042 ledger, U-048 notify never fatal, scoped tests_required."""
import json
import subprocess
from pathlib import Path

import cmloop as C
import integrate as I


def test_real_order_md_carries_a_coupled_set_per_batch():
    b = {x['batch']: x for x in C.order_batches()}
    f05 = [f'F-05{x}' for x in 'abcdefghij']
    assert b[2]['coupled_set'] == ['F-02c'] + f05 and 'F-01b' not in b[2]['coupled_set']
    assert 'F-07' not in b[3]['coupled_set'] and len(b[3]['coupled_set']) == 6
    assert b[4]['coupled_set'] == ['F-08a', 'F-08b', 'F-08c'] and b[1]['coupled_set'] == b[5]['coupled_set'] == []
    assert C.batch_problems(list(b.values()), C.load_items()) == []
    assert C.batch_coverage(list(b.values()), C.load_items()) == []


def test_closure_rule_catches_a_coupled_item_waiting_on_an_uncoupled_sibling():
    items = {'A-1': {'waits_on': []}, 'B-1': {'waits_on': ['A-1']}}
    bat = [{'batch': 2, 'items': ['A-1', 'B-1'], 'coupled_set': ['B-1'], 'waves': [['A-1'], ['B-1', 'X-9']]}]
    pr = C.batch_problems(bat, items)
    assert any('coupled item B-1 waits on uncoupled A-1' in x for x in pr)
    assert any('waves do not hold every item exactly once' in x for x in pr)


def test_builder_limit_clamps_to_12_and_waves_rechunk_under_a_lower_limit():
    assert C.clamp_builders(20) == 12 and C.clamp_builders(0) == 1 and C.clamp_builders(None) == 5
    b = {x['batch']: x for x in C.order_batches()}[2]
    assert C.plan_waves(b, 12) == [b['items']] and C.peak_agents(C.plan_waves(b, 12)) == 12
    assert [len(w) for w in C.plan_waves(b, 10)] == [10, 2]
    p = C.plan(1, '2026-11-12T18:00:00Z', max_builders=12)
    assert p['maxBuilders'] == 12 and [C.peak_agents(x['waves']) for x in p['batches']] == [5, 12, 7, 5, 4]


def _repo(tmp):
    def g(*a):
        return subprocess.run(['git', '-C', str(tmp), *a], capture_output=True, text=True, check=True).stdout
    g('init', '-q', '-b', 'main')
    g('config', 'user.email', 't@t'), g('config', 'user.name', 't')
    (tmp / 'a.txt').write_text('base\n')
    g('add', '.'), g('commit', '-qm', 'base')
    g('checkout', '-qb', 'loop/X')
    (tmp / 'x.txt').write_text('item\n')
    g('add', '.'), g('commit', '-qm', 'item')
    g('checkout', '-q', 'main')
    (tmp / 'up.txt').write_text('upstream landed\n')
    g('add', '.'), g('commit', '-qm', 'upstream')
    g('checkout', '-q', 'loop/X')
    return g


def test_returned_lane_merges_main_first_and_a_conflict_fails_the_lane(tmp_path):
    g = _repo(tmp_path)
    r = C.merge_main_into_lane(tmp_path, 'main')
    assert r.get('merged_main') and (tmp_path / 'up.txt').exists()
    (tmp_path / 'up.txt').write_text('lane edit\n')
    g('commit', '-qam', 'lane edit')
    g('checkout', '-q', 'main')
    (tmp_path / 'up.txt').write_text('main edit\n')
    g('commit', '-qam', 'main edit')
    g('checkout', '-q', 'loop/X')
    r = C.merge_main_into_lane(tmp_path, 'main')
    assert r['ok'] is False and 'up.txt' in r['error']
    assert 'MERGE_HEAD' not in g('rev-parse', '--git-path', 'MERGE_HEAD') or not (tmp_path / '.git/MERGE_HEAD').exists()


def test_a_missing_upstream_type_does_not_count_against_the_downstream_item(tmp_path):
    (tmp_path / 'U-1.md').write_text('---\nid: U-1\n---\n\n## Create\n\n`public static class SimEnumMap { ToMatchResult }`\n')
    items = {'D-1': {'waits_on': ['U-1']}}
    ok = ["Assets/_Game/Sim/Hill.cs(4,9): error CS0103: The name 'SimEnumMap' does not exist in the current context"]
    assert I.upstream_missing(ok, 'D-1', items, tmp_path) is True
    own = ok + ["Assets/_Game/Sim/Hill.cs(9,1): error CS1002: ; expected"]
    assert I.upstream_missing(own, 'D-1', items, tmp_path) is False
    other = ["Assets/_Game/Sim/Hill.cs(4,9): error CS0246: The type or namespace name 'Nowhere' could not be found"]
    assert I.upstream_missing(other, 'D-1', items, tmp_path) is False


def test_playmode_requirements_are_checked_against_playmode_results_not_editmode(tmp_path):
    d = tmp_path / 'Assets/_Game/Runtime.PlayTests'
    d.mkdir(parents=True)
    (d / 'P.cs').write_text('class SmokeTests {\n [UnityTest]\n public IEnumerator Boot() { yield return null; }\n}\n')
    (tmp_path / 'E.cs').write_text('class ETests {\n [Test]\n public void Good() {}\n}\n')
    edit, play = tmp_path / 'e.xml', tmp_path / 'p.xml'
    edit.write_text('<test-run><test-case fullname="X.ETests.Good" result="Passed"/></test-run>')
    play.write_text('<test-run><test-case fullname="X.SmokeTests.Boot" result="Passed"/></test-run>')
    items = {'F-08c': {'tests_required': ['SmokeTests.Boot', 'ETests.Good']}}
    fbi = {'F-08c': {'Assets/_Game/Runtime.PlayTests/P.cs', 'E.cs'}}
    assert I.required_tests_missing(items, ['F-08c'], fbi, tmp_path, str(edit), 'edit') == {}
    assert I.required_tests_missing(items, ['F-08c'], fbi, tmp_path, str(play), 'play') == {}
    assert I.required_tests_missing(items, ['F-08c'], fbi, tmp_path, str(edit), 'play') != {}


def test_card_accepts_ids_inside_an_items_own_title_and_a_refusal_is_never_fatal():
    items = {'F-03': {'title': 'Ground grid and the Skybreak Basin map (with the CM-31 decoration data)'}}
    card = C.batch_card(1, ['F-03'], items, '#1')
    assert C.card_problems(card) and not C.card_problems(card, C.card_titles(['F-03'], items))
    res = {'flags': []}
    out = I.safe_notify('batch-landed', 'k-bad', 'raw F-99 id', [], res)
    assert out['flushed'] == 0 and 'refused' in res['flags'][0]


def test_soak_red_is_a_report_never_a_blocking_entry(tmp_path, monkeypatch):
    monkeypatch.setattr(I, 'run_gate', lambda *a, **k: {'gate': 'soak', 'status': 'fail', 'exit': 1, 'detail': 'band A red'})

    class A:
        batch, review_dir = 4, str(tmp_path)
    res = {'flags': [], 'skipped_gates': []}
    st = I.run_soak(A, 'T', tmp_path, {}, res)
    assert st['report_only'] and C.review_blocking(tmp_path) == []
    assert 'ADVISORY' in (tmp_path / 'T-4.md').read_text()


def test_ledger_records_each_state_once_and_closes_every_item_in_exactly_one_terminal_state(monkeypatch):
    C.write_json(C.STATE / 'run.json', {'run_id': 'wf_test-ledger', 'status': 'stopped'})
    C.write_json(C.LOGS / 'launch-args.json', {'items': [{'id': 'L-1', 'owner': 'loop'}, {'id': 'L-2', 'owner': 'loop'},
                                                         {'id': 'L-3', 'owner': 'loop'}, {'id': 'M-1', 'owner': 'max'}],
                                               'batches': [{'items': ['L-1', 'L-2', 'L-3']}], 'landed': []})
    monkeypatch.setattr(C, 'landed_map', lambda *a, **k: {'L-1': 'abc123'})
    (C.STATE / 'failures').mkdir(parents=True, exist_ok=True)
    (C.STATE / 'failures' / 'L-2.md').write_text('compile red\n')
    assert C.item_state('L-2', 'Two', 'dropped', 'f', 'T-2', 'Two was dropped.') is True
    assert C.item_state('L-2', 'Two', 'dropped', 'f', 'T-2', 'Two was dropped.') is False  # never twice
    finals = {r['id']: r['state'] for r in C.ledger_close('HALT: test')}
    assert finals == {'L-1': 'landed', 'L-2': 'dropped', 'L-3': 'parked'}
    assert C.ledger_close('again') == []  # closing twice writes nothing
    assert C.ledger_audit() == []
    live = (C.LOGS / 'live.log').read_text()
    assert live.count('Two was dropped.') == 1 and live.count('was not reached this run (HALT: test)') == 1


# ── U-050: a fresh launch reuses pushed lanes instead of rebuilding them ──
def _lanes_repo(tmp):
    def g(*a, cwd=tmp):
        return subprocess.run(['git', '-C', str(cwd), *a], capture_output=True, text=True, check=True).stdout.strip()
    g('init', '-q', '-b', 'main')
    g('config', 'user.email', 't@t'); g('config', 'user.name', 't')
    (tmp / 'a.txt').write_text('a\n'); g('add', '.'); g('commit', '-qm', 'base')
    base = g('rev-parse', 'HEAD')
    for name, f, text in (('X-1', 'x.txt', 'x\n'), ('X-2', 'a.txt', 'lane\n'), ('X-3', 'y.txt', 'y\n')):
        g('checkout', '-q', '-b', f'loop/{name}', base)
        (tmp / f).write_text(text); g('add', '.'); g('commit', '-qm', f'{name}: work')
        g('update-ref', f'refs/remotes/origin/loop/{name}', 'HEAD')
    g('checkout', '-q', 'main')
    (tmp / 'a.txt').write_text('main moved\n'); (tmp / 'z.txt').write_text('z\n'); g('add', '.'); g('commit', '-qm', 'main')
    g('update-ref', 'refs/remotes/origin/main', 'HEAD')
    g('update-ref', 'refs/remotes/origin/loop/X-4', 'HEAD')  # a lane with nothing beyond main
    return g


def test_prebuilt_lane_reuses_a_clean_merging_pushed_lane_and_never_a_returned_conflicting_or_empty_one(tmp_path):
    g = _lanes_repo(tmp_path)
    x1 = C.prebuilt_lane('X-1', 0, cwd=tmp_path)
    assert x1 == g('rev-parse', 'refs/remotes/origin/loop/X-1')       # behind main, merges cleanly: reused
    assert C.prebuilt_lane('X-2', 0, cwd=tmp_path) == ''              # conflicts with main in a.txt: rebuilt
    assert C.prebuilt_lane('X-3', 1, cwd=tmp_path) == ''              # returned once: its builder reads the failure
    assert C.prebuilt_lane('X-4', 0, cwd=tmp_path) == ''              # nothing beyond main
    assert C.prebuilt_lane('X-9', 0, cwd=tmp_path) == ''              # never pushed
    g('checkout', '-q', 'loop/X-3'); g('merge', '-q', '--no-edit', 'main')
    g('update-ref', 'refs/remotes/origin/loop/X-3', 'HEAD'); g('checkout', '-q', 'main')
    assert C.prebuilt_lane('X-3', 0, cwd=tmp_path) == g('rev-parse', 'refs/remotes/origin/loop/X-3')  # descends from main
    assert g('status', '--porcelain') == '' and g('rev-parse', 'main') == g('rev-parse', 'refs/remotes/origin/main')


def test_plan_marks_prebuilt_lanes_and_rebuild_turns_it_off(monkeypatch):
    monkeypatch.setattr(C, 'prebuilt_lane', lambda iid, returns=0, **k: 'f00d' if iid == 'F-05a' and not returns else '')
    monkeypatch.setattr(C, 'returns_read', lambda: {'F-01b': 1})
    rows = {r['id']: r for r in C.plan(1, '2026-11-12T18:00:00Z', max_builders=12)['items']}
    assert rows['F-05a']['prebuilt'] == 'f00d' and rows['F-01b']['prebuilt'] == '' and rows['F-01b']['returns'] == 1
    assert all(r['prebuilt'] == '' for r in C.plan(1, '2026-11-12T18:00:00Z', reuse=False)['items'])


def test_record_launch_writes_one_reused_ledger_row_per_prebuilt_item_once():
    C.write_json(C.STATE / 'run.json', {'run_id': 'wf_test-reuse', 'status': 'running'})
    args = {'items': [{'id': 'R-1', 'owner': 'loop', 'title': 'One', 'prebuilt': 'a' * 40},
                      {'id': 'R-2', 'owner': 'loop', 'title': 'Two', 'prebuilt': ''},
                      {'id': 'R-3', 'owner': 'loop', 'title': 'Three', 'prebuilt': 'b' * 40},
                      {'id': 'R-4', 'owner': 'loop', 'title': 'Four', 'prebuilt': 'c' * 40}],
            'landed': ['R-3'], 'batches': [{'batch': 2, 'items': ['R-1', 'R-2', 'R-3']}]}
    assert C.ledger_reused(args) == ['R-1']                       # R-3 landed, R-4 not in this run's batches
    assert C.ledger_reused(args) == []                            # recorded once
    rows = [r for r in C.ledger_rows('wf_test-reuse') if r['state'] == 'reused']
    assert [r['id'] for r in rows] == ['R-1'] and 'aaaaaaaaaaaa' in rows[0]['detail'] and not rows[0]['final']
    assert 'reuses 1 already pushed item branch(es) without rebuilding them: R-1.' in (C.LOGS / 'live.log').read_text()


# ── U-051: build-windows, the shipping build, beside build-mac; due once batch 4's coupled set is on main ──
GATES = Path(__file__).resolve().parents[1] / 'gates'


def test_build_windows_gate_skips_with_78_without_the_module_and_never_reports_a_pass(tmp_path):
    proj = tmp_path / 'proj'
    (proj / 'ProjectSettings').mkdir(parents=True)
    (proj / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.3.28f1\n')
    fake = tmp_path / 'Editor/Unity.app/Contents/MacOS/Unity'  # an editor folder with no PlaybackEngines
    p = subprocess.run([str(GATES / 'build-windows'), '--project', str(proj)], capture_output=True, text=True,
                       env={**__import__('os').environ, 'UNITY_PATH': str(fake)})
    rec = json.loads([l for l in p.stdout.splitlines() if l.startswith('{')][-1])
    assert p.returncode == C.SKIP_EXIT == 78 and rec['status'] == 'skipped' and rec['exit'] == 78
    assert 'WindowsStandaloneSupport' in rec['detail'] and rec['gate'] == 'build-windows'
    assert (GATES / 'build-mac').read_text().replace('build-mac', 'X') == (GATES / 'build-windows').read_text().replace('build-windows', 'X')


def test_windows_build_is_due_once_batch_4s_coupled_set_is_on_main_and_after_every_later_batch():
    cset = {x['batch']: x for x in C.order_batches()}[I.WINDOWS_AFTER_BATCH]['coupled_set']
    assert cset == ['F-08a', 'F-08b', 'F-08c']
    assert I.windows_due(['F-05a'], landed=[]) is False
    assert I.windows_due(['F-08a', 'F-08b'], landed=['F-07']) is False
    assert I.windows_due(['F-08a', 'F-08b', 'F-08c'], landed=[]) is True       # the batch that lands the set
    assert I.windows_due(['A-02'], landed=['F-08a', 'F-08b', 'F-08c']) is True  # every later batch


def test_windows_build_red_is_a_blocking_entry_skipped_is_recorded_and_not_due_never_runs(tmp_path, monkeypatch):
    class A:
        batch, review_dir, items_dir = 4, str(tmp_path), None
    calls = []
    for status, code in (('fail', 1), ('skipped', 78), ('pass', 0)):
        monkeypatch.setattr(I, 'run_gate', lambda name, *a, _s=status, _c=code, **k: calls.append(name) or
                            {'gate': name, 'status': _s, 'exit': _c, 'detail': f'{_s} detail', 'out': '/tmp/o'})
        res = {'flags': [], 'skipped_gates': []}
        st = I.run_build_windows(A, 'T', tmp_path, {}, res, ['F-08a', 'F-08b', 'F-08c'], landed=[])
        assert st['status'] == status and st['exit'] == code
        if status == 'fail':
            assert [b['key'] for b in C.review_blocking(tmp_path)] == ['WT-4'] and 'build-windows red' in res['flags'][0]
        if status == 'skipped':
            assert res['skipped_gates'] == ['build-windows: skipped detail'] and not res['flags']
    assert calls == ['build-windows'] * 3
    assert I.run_build_windows(A, 'T', tmp_path, {}, {'flags': [], 'skipped_gates': []}, ['F-05a'], landed=[]) is None
    assert calls == ['build-windows'] * 3
