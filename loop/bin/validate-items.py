#!/usr/bin/env python3
"""Validate loop item files (loop/items/<ID>.md). Exit 0 = every item valid, 1 = at least one rejected.

An item is rejected when (decision 1, U-011):
  - a front-matter field is missing: id, title, phase, owner, waits_on, files_allowed, files_forbidden,
    tests_required, pass_check, art_row (or a value is malformed: owner loop|max, art_row none|p03..p15, ...)
  - its method is missing (its "## Method" section; for items written without one, every "##" section except
    "## Out of scope"), or contains the words "or", "either", "TBD" or the phrase "if installed"
    (an item carries one method; a builder never chooses between options)
  - files_allowed reaches ProjectSettings/, Packages/ or Controls.inputactions without a front-matter line
    `architect_approved: U-###`
Fixture items under loop/items/_fixtures/ are never part of the queue; validate them with --dir.
"""
import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cmloop as C  # noqa: E402

BANNED = [(re.compile(r'\bor\b', re.I), '"or"'), (re.compile(r'\beither\b', re.I), '"either"'),
          (re.compile(r'\bif\s+installed\b', re.I), '"if installed"'), (re.compile(r'\bTBD\b', re.I), '"TBD"')]


def validate(path, known_ids=None):
    fm, body, errors = C.parse_item(path)
    errs = list(errors)
    for f in C.REQUIRED:
        if f not in fm:
            errs.append(f'missing field: {f}')
    iid = fm.get('id')
    if isinstance(iid, str):
        if not C.ID_RE.match(iid):
            errs.append(f'id {iid!r} is not like F-01, F-02a or CM-30')
        if Path(path).stem != iid:
            errs.append(f'id {iid!r} does not match the file name {Path(path).name}')
    for f in C.LIST_FIELDS:
        if f in fm and not isinstance(fm[f], list):
            errs.append(f'{f} must be a list, e.g. [a, b] or []')
    for f in ('id', 'title', 'phase', 'owner', 'pass_check', 'art_row'):
        if f in fm and (isinstance(fm[f], list) or not str(fm[f]).strip()):
            errs.append(f'{f} must be a single non-empty value')
    if 'owner' in fm and fm['owner'] not in ('loop', 'max'):
        errs.append(f'owner must be loop or max, not {fm["owner"]!r}')
    if 'phase' in fm and str(fm['phase']) not in ('0', '1', '2', '3'):
        errs.append(f'phase must be 0, 1, 2 or 3 (U-012), not {fm["phase"]!r}')
    if 'art_row' in fm and not re.match(r'^(none|p(0[3-9]|1[0-5]))$', str(fm['art_row'])):
        errs.append(f'art_row must be none or a bible row p03..p15, not {fm["art_row"]!r}')
    if isinstance(fm.get('files_allowed'), list) and not fm['files_allowed']:
        errs.append('files_allowed is empty: an item must name the files it may touch')
    if isinstance(fm.get('tests_required'), list) and not fm['tests_required'] and fm.get('art_row') in (None, 'none'):
        errs.append('tests_required is empty: name the NUnit tests (only an art_row item may have none)')
    for t in (fm['tests_required'] if isinstance(fm.get('tests_required'), list) else []):
        if not re.match(r'^[A-Za-z_][\w.]*$', t):
            errs.append(f'tests_required entry {t!r} is not an NUnit test name (Namespace.Class.Method)')
    if 'tightly_defined' in fm and str(fm['tightly_defined']).lower() not in ('true', 'false'):
        errs.append('tightly_defined must be true or false')
    approved = fm.get('architect_approved')
    if approved is not None and not re.match(r'^U-\d{3}$', str(approved)):
        errs.append(f'architect_approved must be U-### , not {approved!r}')
    guarded = sorted({g for g in (fm.get('files_allowed') or []) if isinstance(g, str)
                      for s in C.GUARDED_SAMPLES if C.glob_match(s, g) or C.is_guarded(g)})
    if guarded and not (approved and re.match(r'^U-\d{3}$', str(approved))):
        errs.append('files_allowed reaches ProjectSettings/, Packages/ or Controls.inputactions without '
                    f'architect_approved: U-### ({", ".join(guarded)})')
    if known_ids is not None:
        for w in (fm['waits_on'] if isinstance(fm.get('waits_on'), list) else []):
            if w not in known_ids:
                errs.append(f'waits_on {w!r} is not an item in this folder')
    method = C.method_text(body)
    if method is None or not method.strip():
        errs.append('no method: a "## Method" section, or "##" sections other than "Out of scope"')
    else:
        for n, ln in enumerate(method.splitlines(), 1):
            for rx, word in BANNED:
                if rx.search(ln):
                    errs.append(f'method line {n} contains {word}: {ln.strip()[:100]!r}')
    return errs


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('--dir', default=str(C.ITEMS_DIR))
    ap.add_argument('--json', action='store_true')
    ap.add_argument('--quiet', action='store_true', help='print only rejected items and the summary line')
    ap.add_argument('files', nargs='*')
    a = ap.parse_args(argv)
    files = [Path(f) for f in a.files] or C.item_files(a.dir)
    known = {p.stem for p in C.item_files(a.dir)} | {Path(f).stem for f in files}
    results = {str(p): validate(p, known) for p in files}
    bad = {p: e for p, e in results.items() if e}
    if a.json:
        print(json.dumps({'checked': len(results), 'rejected': len(bad), 'errors': bad}, indent=2))
    else:
        for p, errs in results.items():
            if errs:
                print(f'REJECT {Path(p).name}')
                for e in errs:
                    print(f'  - {e}')
            elif not a.quiet:
                print(f'OK     {Path(p).name}')
        print(f'validate-items: {len(results)} checked, {len(bad)} rejected ({a.dir if not a.files else "files"})')
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
