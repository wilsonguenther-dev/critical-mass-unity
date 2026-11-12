"""Integrator attribution and returns, the digest sender, landed detection, PR refusals, the stall alarm."""
import fcntl
import json
import os
import subprocess
from pathlib import Path

import cmloop as C
import integrate as I
import watch as W


# ── integrator ──
def test_compile_error_attributed_to_the_item_that_touched_the_file():
    files = {'FX-RED': {'Assets/_Game/Sim/FixtureRed.cs'}, 'FX-GREEN': {'Assets/_Game/Sim/FixtureGreen.cs'}}
    lines = ['/x/_int/Assets/_Game/Sim/FixtureRed.cs(6,33): error CS1002: ; expected']
    hits = I.attribute_paths(lines, files, '/x/_int')
    assert list(hits) == ['FX-RED'] and 'CS1002' in hits['FX-RED'][0]


def test_failed_test_attributed_by_tests_required():
    items = {'A-01': {'tests_required': ['CriticalMass.Sim.Tests.RngTests.SameSeed']}, 'B-01': {'tests_required': []}}
    failed = [{'name': 'SameSeed', 'fullname': 'CriticalMass.Sim.Tests.RngTests.SameSeed',
               'classname': 'CriticalMass.Sim.Tests.RngTests', 'message': 'Expected 1 But was 2', 'stack': ''}]
    hits = I.attribute_tests(failed, items, {'A-01': set(), 'B-01': set()}, '/nowhere')
    assert list(hits) == ['A-01']


def test_unattributable_error_names_no_item():
    files = {'FX-RED': {'Assets/_Game/Sim/FixtureRed.cs'}}
    assert I.attribute_paths(['Assets/_Game/Sim/Other.cs(1,1): error CS0246: x'], files, '/p') == {}


def test_two_returns_then_parked():
    rs = [C.return_item('PK-01', 'Parked thing', 'b1', 'compile', f'red {i}') for i in range(3)]
    assert [r['returns'] for r in rs] == [1, 2, 3]
    assert [r['parked'] for r in rs] == [False, False, True]
    assert 'red 0' in Path(rs[0]['failure_file']).read_text()
    assert 'PK-01' in C.parked_ids()


def test_uncounted_drop_does_not_count():
    r = C.return_item('UC-01', 'Uncounted', 'b1', 'time-box', 'cut', counted=False)
    assert r['returns'] == 0 and not r['parked']


# ── texting ──
def test_cards_must_carry_titles():
    assert C.card_problems('Batch 2 landed: CM-30, CM-31.')
    assert C.card_problems('Try: (CM-62)')
    assert C.card_problems('Landed: F-02a.')
    assert not C.card_problems('Landed: Per-team values (F-02a).')
    assert not C.card_problems(C.batch_card(2, ['F-03'], {'F-03': {'title': 'Ground grid with heights'}}, '#4'))


def test_one_text_per_key_and_one_flusher(tmp_path, monkeypatch):
    monkeypatch.setattr(C, 'quiet_hours', lambda now=None: False)  # the test is about keys and the lock, not the clock
    sent = []
    C.notify_enqueue('batch-landed', 'batch-T-1', 'Batch 1 landed: Ground grid (F-03).')
    C.notify_enqueue('batch-landed', 'batch-T-1', 'Batch 1 landed: Ground grid (F-03).')
    lf = open(C.STATE / 'notify.lock', 'w')
    fcntl.flock(lf, fcntl.LOCK_EX)
    assert C.notify_flush(transport=lambda t: (sent.append(t), (0, 'ok', ''))[1])['reason'].startswith('another')
    fcntl.flock(lf, fcntl.LOCK_UN)
    lf.close()
    C.notify_flush(transport=lambda t: (sent.append(t), (0, 'ok', ''))[1])
    C.notify_flush(transport=lambda t: (sent.append(t), (0, 'ok', ''))[1])
    assert sent == ['Batch 1 landed: Ground grid (F-03).']


def test_no_real_send_without_running_loop(capsys):
    C.notify_enqueue('halt', 'halt-x-1', 'Critical Mass Unity loop stopped.')
    assert C.notify_flush()['flushed'] == 0
    assert 'not sent' in capsys.readouterr().out


def test_only_two_kinds():
    import pytest
    with pytest.raises(SystemExit):
        C.notify_enqueue('slip', 'k', 'Something slipped.')


# ── landed: full id, not reverted ──
def test_landed_full_token_and_revert(tmp_path):
    r = tmp_path / 'repo'
    r.mkdir()
    g = lambda *a: subprocess.run(['git', '-C', str(r), *a], capture_output=True, text=True, check=True).stdout
    g('init', '-q', '-b', 'main')
    g('-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '--allow-empty', '-m', 'b1\n\nloop-landed: CM-40')
    sha = g('rev-parse', 'HEAD').strip()
    g('-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '--allow-empty', '-m', 'b2\n\nloop-landed: F-01')
    g('-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '--allow-empty', '-m',
      f'Revert b2\n\nloop-reverted: F-01')
    m = C.landed_map('main', cwd=r)
    assert m == {'CM-40': sha}  # CM-4 is not landed by CM-40; F-01 was reverted
    assert 'CM-4' not in m


# ── PRs ──
def test_pr_refuses_non_main_base():
    assert 'every PR targets main' in C.pr_open('loop/int-x-1', 't', '/dev/null', base='loop/int-x-0')['error']


def test_pr_refuses_docs_only_while_batch_open(monkeypatch):
    monkeypatch.setattr(C, 'changed_files', lambda *a, **k: ['README.md', 'loop/README.md'])
    runner = lambda cmd: (0, json.dumps([{'number': 7, 'headRefName': 'loop/int-T-3', 'baseRefName': 'main'}]), '')
    assert 'docs-only PR while batch PR #7' in C.pr_open('docs/x', 't', '/dev/null', runner=runner)['error']


def test_pr_merge_refuses_non_main_base():
    runner = lambda cmd: (0, json.dumps({'baseRefName': 'loop/int-T-1', 'state': 'OPEN', 'headRefName': 'x'}), '')
    assert 'not main' in C.pr_merge(9, 's', '/dev/null', runner=runner)['error']


# ── compile lock + stall alarm ──
class Args:
    dry_run = True
    minutes = 0  # anything quiet for 0 minutes would alarm
    lock_max_min = 60


def test_stall_quiet_while_live_lock_then_alarms_when_owner_dead(capsys):
    rec = C.lock_acquire('integrator-batch-1')  # this test process is the live owner
    try:
        assert W.stall_tick(Args()) == 'lock-held'
    finally:
        C.lock_release(rec['token'])
    dead = subprocess.Popen(['true'])
    dead.wait()
    rec = C.lock_acquire('integrator-batch-2', pid=dead.pid)
    try:
        assert W.stall_tick(Args()) == 'alarm-stall'
        assert 'WOULD ALARM' in capsys.readouterr().out
        assert not (C.STATE / 'STALL').exists()  # dry run writes nothing
    finally:
        C.lock_release(rec['token'])


def test_dead_owner_lock_is_reclaimed():
    dead = subprocess.Popen(['true'])
    dead.wait()
    assert C.lock_acquire('integrator-batch-3', pid=dead.pid)
    rec = C.lock_acquire('integrator-batch-4')
    assert rec and rec['owner'] == 'integrator-batch-4'
    C.lock_release(rec['token'])


def test_builders_cannot_take_the_lock():
    import pytest
    with pytest.raises(SystemExit):
        C.lock_acquire('builder-F-01')


# ── U-026 batches, ruling 6 blockers, ruling 7 quiet hours ──
ORDER_MD = """# Phase 0 order

## 2. Dependency graph

```
F-01 -> F-02a -> F-03
F-03 -> {F-05a, F-05b}   (coupled: one compile unit)
```

## 3. Batches (no two items in a batch share a path)

| Batch | Items | Builders |
| --- | --- | --- |
| 1 | F-01, F-02a, F-03 | 3 |
| 2 | F-05a, F-05b | 2 |

## 4. Next
"""


def test_batches_from_order_md(tmp_path):
    (tmp_path / '_ORDER.md').write_text(ORDER_MD)
    assert C.order_batches(tmp_path) == [{'batch': 1, 'items': ['F-01', 'F-02a', 'F-03'], 'coupled': False},
                                         {'batch': 2, 'items': ['F-05a', 'F-05b'], 'coupled': True}]


def test_real_order_md_marks_batches_2_3_4_coupled():
    b = C.order_batches()
    if b:  # loop/items/_ORDER.md exists on this checkout
        assert [x['coupled'] for x in b] == [False, True, True, True]


def test_real_order_md_batch_membership_covers_every_loop_item():
    # panel revision 2026-11-12: 'F-05a .. F-05j' used to parse as its two endpoints only
    b = C.order_batches()
    if not b:
        return
    assert b[1]['items'] == ['F-05a', 'F-05b', 'F-05c', 'F-05d', 'F-05e', 'F-05f', 'F-05g', 'F-05h', 'F-05i', 'F-05j']
    assert [len(x['items']) for x in b] == [5, 10, 7, 4]
    assert C.batch_coverage(b, C.load_items()) == []


def test_letter_range_expands_and_coverage_catches_orphans_and_duplicates():
    tok = __import__('re').compile(r'(?<![A-Za-z0-9-])[A-Z][A-Z0-9]*-\d+[a-z]?(?![A-Za-z0-9-])')
    assert C.expand_ranges('F-05a .. F-05d', tok) == ['F-05a', 'F-05b', 'F-05c', 'F-05d']
    assert C.expand_ranges('F-01, F-05a..F-05b, F-07', tok) == ['F-01', 'F-05a', 'F-05b', 'F-07']
    items = {'F-01': {'owner': 'loop'}, 'F-02': {'owner': 'loop'}, 'CM-30': {'owner': 'max'}}
    bat = [{'batch': 1, 'items': ['F-01']}, {'batch': 2, 'items': ['F-01']}]
    assert C.batch_coverage(bat, items) == ['F-02 is in no batch', 'F-01 in batches 1 and 2']


def test_select_batches_keeps_only_the_named_batches():
    bat = [{'batch': 1, 'items': ['F-01']}, {'batch': 2, 'items': ['F-05a']}]
    assert C.select_batches(bat, '1') == [bat[0]]
    assert C.select_batches(bat, None) == bat
    try:
        C.select_batches(bat, '9')
        raise AssertionError('an unknown batch must refuse')
    except ValueError:
        pass


def test_required_tests_must_be_declared_and_passed(tmp_path):
    (tmp_path / 'A.cs').write_text('class ATests {\n [Test]\n public void Good() {}\n [Test, Ignore("x")]\n public void Ign() {}\n'
                                   ' [Test, Explicit("perf")]\n public void Perf() {}\n [Test]\n public void NotRun() {}\n}\n')
    xml = tmp_path / 'r.xml'
    xml.write_text('<test-run><test-case fullname="CriticalMass.Sim.Tests.ATests.Good" result="Passed"/>'
                   '<test-case fullname="CriticalMass.Sim.Tests.ATests.Ign" result="Skipped"/></test-run>')
    items = {'X-01': {'tests_required': ['ATests.Good', 'ATests.Perf']},
             'X-02': {'tests_required': ['ATests.Ign', 'ATests.NotRun', 'ATests.Missing']}}
    fbi = {'X-01': {'A.cs'}, 'X-02': {'A.cs'}}
    hits = I.required_tests_missing(items, ['X-01', 'X-02'], fbi, tmp_path, str(xml))
    assert 'X-01' not in hits
    assert [h.split(' ')[1] for h in hits['X-02']] == ['ATests.Ign', 'ATests.NotRun', 'ATests.Missing']


def test_chain_dependants_drop_with_the_red_item():
    items = {'F-01': {}, 'F-02a': {'waits_on': ['F-01']}, 'F-03': {'waits_on': ['F-02a']}, 'X-01': {}}
    assert I.dependants('F-01', ['F-01', 'F-02a', 'F-03', 'X-01'], items) == ['F-02a', 'F-03']
    assert I.dependants('F-03', ['F-01', 'F-02a', 'F-03'], items) == []


def test_review_blocking_needs_item_or_waiver(tmp_path):
    f = C.review_add_blocking('T-1', 'RT-1-1', 'Sim reads the wall clock in Rng.cs:12.', tmp_path)
    C.review_add_blocking('T-1', 'RT-1-2', 'Runtime writes World.Tick.', tmp_path)
    assert [b['key'] for b in C.review_blocking(tmp_path)] == ['RT-1-1', 'RT-1-2']
    with open(f, 'a') as o:
        o.write('- RESOLVED RT-1-1: item F-10\n- WAIVED RT-1-2: no reason\n')
    assert [b['key'] for b in C.review_blocking(tmp_path)] == ['RT-1-2']  # a waiver must cite U-###
    with open(f, 'a') as o:
        o.write('- WAIVED RT-1-2: U-030\n')
    assert C.review_blocking(tmp_path) == []


def test_quiet_hours_hold_digests_but_halts_always_text(monkeypatch):
    monkeypatch.setattr(C, 'quiet_hours', lambda now=None: True)
    (C.STATE / 'notify-outbox.jsonl').write_text('')
    sent = []
    C.notify_enqueue('batch-landed', 'batch-Q-1', 'Batch 1 landed: Ground grid (F-03).')
    C.notify_enqueue('halt', 'halt-Q-1', 'Critical Mass Unity loop stopped: disk is low.')
    r = C.notify_flush(transport=lambda t: (sent.append(t), (0, 'ok', ''))[1])
    assert sent == ['Critical Mass Unity loop stopped: disk is low.'] and r['held'] >= 1


def test_setup_red_from_compile_errors_names_the_item(tmp_path):
    ef = tmp_path / 'e.txt'
    ef.write_text('Assets/_Game/Sim/FixtureCb.cs(5,37): error CS1002: ; expected\n')
    hits = I.attribute({'gate': 'setup', 'errors_file': str(ef)}, {}, {'FX-CB': {'Assets/_Game/Sim/FixtureCb.cs'}}, '/p')
    assert list(hits) == ['FX-CB']


# ── U-027 / U-030: generated Unity files ──
def _gate_module():
    import importlib.util
    spec = importlib.util.spec_from_file_location('gate', str(C.REPO / 'loop' / 'gates' / 'gate.py'))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def test_scope_rejects_hand_written_unity_data_even_inside_files_allowed():
    G = _gate_module()
    it = {'files_allowed': ['Assets/_Game/Runtime/**', 'Assets/_Game/Settings/Controls.inputactions*'],
          'files_forbidden': [], 'architect_approved': 'U-006'}
    v = G.scope_violations(it, ['Assets/_Game/Runtime/Foo.cs', 'Assets/_Game/Runtime/Foo.cs.meta',
                                'Assets/_Game/Runtime/Arena.unity', 'Assets/_Game/Runtime/Bot.prefab',
                                'Assets/_Game/Runtime/Floor.mat', 'Assets/_Game/Runtime/Data.asset',
                                'Assets/_Game/Settings/Controls.inputactions',
                                'Assets/_Game/Settings/Controls.inputactions.meta'])
    bad = sorted(x.split(':')[0] for x in v)
    assert bad == ['Assets/_Game/Runtime/Arena.unity', 'Assets/_Game/Runtime/Bot.prefab', 'Assets/_Game/Runtime/Data.asset',
                   'Assets/_Game/Runtime/Floor.mat', 'Assets/_Game/Runtime/Foo.cs.meta']
    assert all('U-027' in x and 'Unity generates metas' in x for x in v)


def test_project_settings_asset_still_needs_architect_approved():
    G = _gate_module()
    it = {'files_allowed': ['ProjectSettings/EditorBuildSettings.asset'], 'files_forbidden': []}
    assert 'architect_approved' in G.scope_violations(it, ['ProjectSettings/EditorBuildSettings.asset'])[0]
    assert G.scope_violations({**it, 'architect_approved': 'U-006'}, ['ProjectSettings/EditorBuildSettings.asset']) == []


def test_generated_set_is_metas_plus_what_setup_wrote_in_its_dirs():
    after_setup = [('??', 'Assets/_Game/Sim/New.cs.meta'), (' M', 'ProjectSettings/QualitySettings.asset'),
                   ('??', 'Assets/_Game/Settings/Urp.asset'), (' M', 'Packages/packages-lock.json')]
    after_compile = after_setup + [('??', 'Assets/_Game/Sim/Dir.meta'), (' D', 'Assets/_Game/Sim/Old.cs.meta'),
                                   (' M', 'ProjectSettings/ProjectSettings.asset'), ('??', 'Assets/_Game/Sim/Stray.txt')]
    accept, reject = C.classify_generated(after_setup, after_compile)
    assert accept == ['Assets/_Game/Settings/Urp.asset', 'Assets/_Game/Sim/Dir.meta', 'Assets/_Game/Sim/New.cs.meta',
                      'Assets/_Game/Sim/Old.cs.meta', 'ProjectSettings/QualitySettings.asset']
    # not written by ProjectSetup.Apply (or outside its dirs): never committed by the harness
    assert reject == ['?? Assets/_Game/Sim/Stray.txt', 'M Packages/packages-lock.json', 'M ProjectSettings/ProjectSettings.asset']


def test_setup_is_the_first_unity_gate_and_clean_reimport_follows_compile():
    assert I.gate_order_error(I.DEFAULT_GATES) is None
    assert I.DEFAULT_GATES[:3] == ['setup', 'compile', 'clean-reimport']
    assert 'first Unity gate' in I.gate_order_error(['compile', 'setup'])
    assert I.gate_order_error(['setup', 'clean-reimport', 'compile'])
    assert I.gate_order_error(['budgets']) is None  # no Unity gate, nothing to order


def test_order_sentence_is_the_same_in_integrate_and_the_workflow():
    norm = lambda t: ' '.join(t.split())
    assert norm(C.SETUP_ORDER) in norm(I.__doc__)
    js = (C.REPO / 'loop' / 'workflow' / 'critical-mass-unity.js').read_text()
    assert f"const SETUP_ORDER = '{C.SETUP_ORDER}'" in js


def test_item_supplied_tools_switch_gates_from_skipped_to_real(tmp_path, monkeypatch):
    """F-08c / F-09 commit tools/unity-playtest.sh and tools/unity-soak.sh (maybe without the exec bit): the gates
    run them through bash as soon as they exist in the tree under test."""
    G = _gate_module()
    a = type('A', (), {'project': str(tmp_path)})()
    assert G.g_soak(a) == C.SKIP_EXIT and G.g_playmode(a) == C.SKIP_EXIT
    calls = []
    monkeypatch.setattr(G, 'refuse_unity', lambda g, p: None)
    monkeypatch.setattr(G, 'run_locked', lambda g, cmd, p: (calls.append(cmd), (0, 'soak: OK', '', 'gate-soak'))[1])
    (tmp_path / 'tools').mkdir()
    (tmp_path / G.SOAK_TOOL).write_text('echo soak: OK\n')  # no chmod +x
    assert G.g_soak(a) == 0 and calls[-1] == ['bash', str(tmp_path / G.SOAK_TOOL)]
