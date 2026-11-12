import importlib.util
from pathlib import Path

import cmloop as C

_spec = importlib.util.spec_from_file_location('validate_items', Path(C.HERE).parent / 'validate-items.py')
V = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(V)

GOOD = """---
id: F-02
title: Seeded random number generator
phase: 0
owner: loop
waits_on: []
files_allowed: [Assets/_Game/Sim/Rng.cs, Assets/_Game/Sim.Tests/RngTests.cs]
files_forbidden: [Assets/_Game/Runtime/**]
tests_required: [CriticalMass.Sim.Tests.RngTests.SameSeedSameSequence]
pass_check: RngTests pass in the EditMode gate.
art_row: none
---
## Method
Port sim/rng.ts line for line into Rng.cs. Seed 1337 is the default. This text is for the builder.
## Notes
Either way, notes may say or. Only the method section is checked.
"""


def write(tmp, name, text):
    p = Path(tmp) / f'{name}.md'
    p.write_text(text)
    return p


def test_valid_item_passes(tmp_path):
    assert V.validate(write(tmp_path, 'F-02', GOOD), {'F-02'}) == []


def test_each_rule_rejects(tmp_path):
    bad = GOOD.replace('art_row: none\n', '').replace(
        'Seed 1337 is the default.', 'Use A or B, either one, Blender if installed, numbers TBD.').replace(
        'files_allowed: [', 'files_allowed: [ProjectSettings/**, Packages/manifest.json, '
                           'Assets/_Game/Settings/Controls.inputactions, ')
    errs = '\n'.join(V.validate(write(tmp_path, 'F-02', bad), {'F-02'}))
    for needle in ('missing field: art_row', '"or"', '"either"', '"if installed"', '"TBD"', 'architect_approved'):
        assert needle in errs, needle


def test_guarded_path_allowed_with_approval(tmp_path):
    ok = GOOD.replace('files_allowed: [', 'files_allowed: [Packages/manifest.json, ').replace(
        'art_row: none\n', 'art_row: none\narchitect_approved: U-022\n')
    assert V.validate(write(tmp_path, 'F-02', ok), {'F-02'}) == []


def test_wide_glob_reaching_guarded_paths_needs_approval(tmp_path):
    wide = GOOD.replace('files_allowed: [', 'files_allowed: [**/*, ')
    assert any('architect_approved' in e for e in V.validate(write(tmp_path, 'F-02', wide), {'F-02'}))


def test_word_boundaries_do_not_flag_for_or_order(tmp_path):
    t = GOOD.replace('Seed 1337 is the default.', 'Order the tests for the Sim; forward the seed.')
    assert V.validate(write(tmp_path, 'F-02', t), {'F-02'}) == []


def test_unknown_waits_on_and_bad_values(tmp_path):
    t = GOOD.replace('waits_on: []', 'waits_on: [F-99]').replace('owner: loop', 'owner: bob').replace(
        'phase: 0', 'phase: 7')
    errs = '\n'.join(V.validate(write(tmp_path, 'F-02', t), {'F-02'}))
    assert 'F-99' in errs and 'owner must be loop or max' in errs and 'phase must be' in errs


def test_filename_must_match_id(tmp_path):
    assert any('does not match the file name' in e for e in V.validate(write(tmp_path, 'F-03', GOOD), {'F-03'}))


def test_no_method_section(tmp_path):
    t = GOOD.split('## Method')[0]
    assert any('no method' in e for e in V.validate(write(tmp_path, 'F-02', t), {'F-02'}))


def test_sectioned_item_without_method_heading_checks_all_but_out_of_scope(tmp_path):
    t = GOOD.replace('## Method\n', '## Create\n').replace('## Notes\nEither way, notes may say or.',
                                                           '## Out of scope\nCameras or menus.')
    assert V.validate(write(tmp_path, 'F-02', t), {'F-02'}) == []
    t2 = t.replace('Seed 1337 is the default.', 'Seed 1337 or 42.')
    assert any('"or"' in e for e in V.validate(write(tmp_path, 'F-02', t2), {'F-02'}))
