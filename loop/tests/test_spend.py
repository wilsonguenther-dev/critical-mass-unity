import json
from pathlib import Path

import cmloop as C
import spend as S

DATA = json.loads((Path(__file__).parent / 'data/monday-spend.json').read_text())


def fake_projects(tmp, run_id, total=None, transcript_lines=None):
    sess = Path(tmp) / '-Users-x' / 'sess-1'
    if total is not None:
        (sess / 'workflows').mkdir(parents=True, exist_ok=True)
        (sess / 'workflows' / f'{run_id}.json').write_text(json.dumps({'runId': run_id, 'totalTokens': total}))
    if transcript_lines is not None:
        d = sess / 'subagents' / 'workflows' / run_id
        d.mkdir(parents=True, exist_ok=True)
        (d / 'agent-a1.jsonl').write_text('\n'.join(json.dumps(l) for l in transcript_lines) + '\n')
    return Path(tmp)


def msg(mid, i, o, cw, cr):
    return {'type': 'assistant', 'message': {'id': mid, 'usage': {
        'input_tokens': i, 'output_tokens': o, 'cache_creation_input_tokens': cw, 'cache_read_input_tokens': cr}}}


def test_monday_runs_classified_against_caps():
    r1, r2, r3 = DATA['runs']
    # Monday's governor said run 3 used 1.32 M of a 2.7 M cap; the U-014 measure says 12.19 M: a halt.
    assert S.classify(r3['totalTokens'], r3['tokenCap'])[0] == 'halt'
    assert S.classify(r3['governor_output_only'], r3['tokenCap'])[0] == 'ok'  # the old R11 blind spot
    assert S.classify(r1['totalTokens'], 6_000_000) == ('stop-dispatch', round(r1['totalTokens'] / 6e6, 4))
    assert S.classify(r1['totalTokens'], 10_000_000)[0] == 'ok'
    monday = sum(r['totalTokens'] for r in DATA['runs'])
    assert monday == 39_364_654  # "39.4 M" in U-014
    assert round(monday / sum(r['landed'] for r in DATA['runs']) / 1e6, 1) == 2.8  # ~2.8 M per landed item


def test_transcript_estimate_errs_high_on_monday():
    for r in DATA['runs']:
        t = r['transcripts']
        est = t['input'] + t['output'] + t['cache_creation']
        assert 1.15 < est / r['totalTokens'] < 1.25  # estimate stops early, never late
        assert est < t['cache_read']  # cache reads are not in the measure


def test_reads_workflow_total_first(tmp_path):
    p = fake_projects(tmp_path, 'wf_aaaa-1', total=5_526_746, transcript_lines=[msg('m1', 1, 2, 3, 4)])
    m = S.measure('wf_aaaa-1', p)
    assert m['source'] == 'workflow-totalTokens' and m['spent'] == 5_526_746


def test_transcript_fallback_dedupes_message_ids(tmp_path):
    lines = [msg('m1', 10, 100, 1000, 99999), msg('m1', 10, 100, 1000, 99999), msg('m2', 1, 2, 3, 4),
             {'type': 'user', 'message': {'id': 'u'}}]
    p = fake_projects(tmp_path, 'wf_bbbb-2', transcript_lines=lines)
    m = S.measure('wf_bbbb-2', p)
    assert m['source'] == 'transcript-estimate' and m['raw'] == 10 + 100 + 1000 + 1 + 2 + 3
    assert m['spent'] == -(-m['raw'] // S.ESTIMATE_FLOOR_RATIO) and m['spent'] > m['raw']  # errs high (panel 2026-11-12)


def test_no_data(tmp_path):
    assert S.measure('wf_none-0', tmp_path)['source'] == 'none'
    assert S.classify(None, 100) == ('no-data', None)


def test_check_writes_stop_then_halt_files(tmp_path):
    for f in ('STOP-DISPATCH', 'HALT'):
        (C.STATE / f).unlink(missing_ok=True)
    p = fake_projects(tmp_path, 'wf_cccc-3', total=920)
    assert S.check('wf_cccc-3', 1000, p)['action'] == 'stop-dispatch'
    assert (C.STATE / 'STOP-DISPATCH').exists() and not (C.STATE / 'HALT').exists()
    p = fake_projects(tmp_path, 'wf_cccc-3', total=1000)
    assert S.check('wf_cccc-3', 1000, p)['action'] == 'halt'
    assert (C.STATE / 'HALT').exists()
    outbox = (C.STATE / 'notify-outbox.jsonl').read_text()
    assert 'halt-spend-wf_cccc-3' in outbox or 'halt-spend-wf_cccc-3' in (C.STATE / 'notify-sent.jsonl').read_text()
    for f in ('STOP-DISPATCH', 'HALT'):
        (C.STATE / f).unlink(missing_ok=True)


def test_dry_run_writes_nothing(tmp_path):
    p = fake_projects(tmp_path, 'wf_dddd-4', total=2000)
    assert S.check('wf_dddd-4', 1000, p, dry=True)['action'] == 'halt'
    assert not (C.STATE / 'HALT').exists()


def test_cap_is_required(tmp_path):
    import pytest
    with pytest.raises(SystemExit):
        S.check('wf_x-1', None, tmp_path)
