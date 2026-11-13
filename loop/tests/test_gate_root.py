"""DG-05: a gate run without --project works in the worktree it is invoked from, never in the main checkout."""
import importlib.util
import subprocess
from pathlib import Path

import cmloop as C


def _gate():
    spec = importlib.util.spec_from_file_location('gate_root_under_test', str(C.REPO / 'loop' / 'gates' / 'gate.py'))
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def _git(cwd, *args):
    subprocess.run(['git', '-C', str(cwd), *args], check=True, capture_output=True, text=True)


def _repo_with_worktree(tmp_path):
    main = tmp_path / 'main'
    main.mkdir()
    _git(main, 'init', '-q', '-b', 'main')
    _git(main, '-c', 'user.email=t@t', '-c', 'user.name=t', 'commit', '-q', '--allow-empty', '-m', 'root')
    wt = tmp_path / 'wt'
    _git(main, 'worktree', 'add', '-q', '-b', 'feat/x', str(wt))
    (wt / 'sub' / 'deeper').mkdir(parents=True)
    return main.resolve(), wt.resolve()


def test_cwd_inside_a_worktree_of_the_repo_resolves_to_that_worktree(tmp_path):
    G = _gate()
    main, wt = _repo_with_worktree(tmp_path)
    assert G.default_project(cwd=wt / 'sub' / 'deeper', repo=main) == wt
    assert G.default_project(cwd=wt, repo=main) == wt
    # and the other way round: gates copied in the worktree, invoked from the main checkout, run in main
    assert G.default_project(cwd=main, repo=wt) == main


def test_cwd_outside_the_repo_falls_back_to_the_scripts_own_checkout(tmp_path):
    G = _gate()
    main, wt = _repo_with_worktree(tmp_path)
    outside = tmp_path / 'not-a-repo'
    outside.mkdir()
    assert G.default_project(cwd=outside, repo=wt) == wt
    other = tmp_path / 'other'
    other.mkdir()
    _git(other, 'init', '-q')
    assert G.default_project(cwd=other, repo=wt) == wt  # an unrelated repository is never adopted


def test_cli_default_is_the_invoking_worktree_not_the_main_checkout(tmp_path, monkeypatch):
    G = _gate()
    main, wt = _repo_with_worktree(tmp_path)
    seen = {}
    monkeypatch.setattr(G, 'default_project', lambda: wt)
    monkeypatch.setattr(G, 'g_budgets', lambda a: seen.setdefault('project', a.project) and 0)
    G.main(['budgets'])
    assert seen['project'] == str(wt)
    assert seen['project'] != str(main)


def test_this_checkout_resolves_to_itself_when_run_from_here(monkeypatch):
    """The regression: from a linked worktree the old default (C.MAIN) named the main checkout."""
    G = _gate()
    monkeypatch.chdir(C.REPO)
    assert G.default_project() == C.REPO.resolve()
