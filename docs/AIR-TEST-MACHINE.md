# The Air as a test machine for the Sim tests

The owner's MacBook Air (Apple M1, 8 GB) runs the no-Unity Sim tests, the slow set included, instead of GitHub's
hosted runners. It costs nothing, needs no account or secret, and GitHub has no way onto the machine: the main Mac
drives it over ssh with the kit that already runs the play-tests (`tools/remote/`). Opt-in until the coordinator
switches the default.

## Use

```
loop/gates/remote sim --suite slow --on air              # the slow set on the Air, split over 4 processes
loop/gates/remote sim --suite fast --on air              # the per-batch set
loop/gates/remote sim --suite slow --on air --jobs 1     # one process, exactly tools/ci/sim-tests.sh --slow
```

Same command, same JSON line (also in `loop/logs/gates.jsonl`) and same result files as the GitHub path: the line has
`"runner":"air"` and `"run":"air-<id>"`, and `loop/logs/remote/air-<id>/` holds `sim-results/ci/sim-results.xml` and
`sim-build.log` (the layout of the workflow's `sim-results` artifact) plus `run.json`. Exit codes as before: 0 pass,
1 fail (tests or compile), 75 could not start or timed out.

What is tested is the committed tree of the current commit (uncommitted edits are not, as on GitHub). The branch is
still pushed first so a fallback can use it; `--no-push` skips that.

Options: `--jobs N` (default 4, the fastest measured; the box lowers it when free memory is short, about 300 MB of free memory per process; one peaked at 178 MB),
`--air-wait MIN` (how long to wait for the box when another job holds it, default 30), `--no-fallback`.
`CM_REMOTE_SIM_ON=air` makes `--on air` the default; `CM_AIR_JOBS` sets the default split.

## What happens

1. Three quick reachability tries, 20 s apart (`CM_AIR_TRIES`, `CM_AIR_TRY_GAP`).
2. `git archive` of the commit (Sim, Sim.Tests, Net, tools, docs, ProjectSettings: about 2 MB) goes to
   `~/cm-remote/runs/<id>/` on the Air.
3. `air-run.sh` takes the box's queue lock (`~/cm-remote/lock.d`: one job at a time, play-tests included). A second
   caller prints `remote: waiting in the queue, the box is busy: ...` and waits; past `--air-wait` it gives up and the
   gate runs the tests on GitHub instead. The box refuses on battery under 25 % or under 12 GB free disk.
4. `air-sim-job.sh` syncs the tree into `~/cm-remote/sim/src` (only changed files are written, so the build stays
   incremental and always fresh), compiles with `tools/ci/sim-tests.sh --compile-only`, lists the tests, splits the
   fixture classes over N processes by the weights in `tools/remote/air-sim-weights.tsv` (longest first), runs them,
   and merges the parts into one result file. If the merged total is not the listed total, the run fails.
5. Hard limit: 75 minutes (or `--max-wait` if lower). The whole process group is stopped and the gate reports
   `timed-out`. Ctrl-C does the same.
6. Results are copied back and the run folder on the Air is deleted.

Falls back to GitHub (and says `"fallback_from":"air","fallback_reason":...` in the line) when the Air is
unreachable, busy past the wait, refuses, or drops off mid-run. It never falls back after a test result.

Refresh the split weights after the test set changes a lot:
`python3 tools/remote/air-sim-weights.py loop/logs/remote/air-<id>/sim-results/ci/sim-results.xml > tools/remote/air-sim-weights.tsv`
(a class missing from the table counts 60 s, so a stale table only costs balance, never tests).

## Measured (2026-11-21)

Same Sim content as integration/batch-15-cand1 aa2ecfef (no Sim, test, golden or harness change since a15d48a6, so
GitHub run 44881963787 is the reference). The Air: M1 (4 fast + 4 efficiency cores), 8 GB, macOS 27.2.1, on its
charger, the owner's apps left open (about 14.6 GB of swap already in use before any test, 1.3 to 2.3 GB free).

| run | where | processes | wall | peak memory (all dotnet) | result |
|---|---|---|---|---|---|
| fast set (747 tests) | GitHub 44881961553 | 1 | 3.6 min job, 190 s of tests | | 734 pass, 1 fail, 12 skipped |
| fast set | Air, as CI | 1 | 155 s (150 s of tests) | 295 MB | identical |
| slow set (64 tests) | GitHub 44881963787 | 1 | 58 min job, 3,440 s of tests | | 57 pass, 7 fail |
| slow set, run 1 | Air, as CI | 1 | 53.9 min (3,235 s) | 178 MB, one core at 100 % | identical |
| slow set, run 2 | Air, through the gate | 1 | 54.1 min (3,247 s), 55 min gate | 460 MB with the build | identical |
| slow set | Air, through the gate | 2 | E_WALL | E_MEM | identical |
| slow set | Air, through the gate | 3 | 20.6 min (1,236 s) | 279 MB | identical |
| slow set | Air, through the gate | 4 | **16.6 min (995 s)**, 17 min gate | 339 MB | identical |

One process on the Air is only about 6 % faster than GitHub on the slow set (27 % on the fast set). The win is the
split: 4 processes take the slow set from 54 to 16.6 minutes and never queue behind GitHub's other branches. Each
process slows by about 20 % when four run at once (shared cores and memory bandwidth on a fanless machine), so 4 is
the knee; more would land on the efficiency cores.

Determinism: in every run, every test has the same result as on GitHub, and every game number printed by the slow
tests (wins, blowouts, lead changes, kill rates, shares) is identical to the digit between Linux x64 and macOS arm64.
Only timing numbers differ (microseconds per tick, seconds, cost ratios). One timing sub-check is load-sensitive:
`B15RulesSoakTests.ComebackAndHutChoices...` prints "both on sim cost +1.8 % (limit +3 %) PASS" on GitHub, +2.9 % and
+2.5 % PASS with one process on the Air, and +3.6 % FAIL (3 processes) and +13.5 % FAIL (4 processes) in the splits. That test fails on every machine for other
reasons today; treat its cost line (and every tick-cost budget) as timing-flaky in a split run.

## Why not a GitHub self-hosted runner

The repository is public. GitHub's hardening guide: self-hosted runners "should almost never be used for public
repositories on GitHub, because any user can open pull requests against the repository and compromise the
environment", and they "can be persistently compromised by untrusted code in a workflow". The approval gate for
outside contributors depends on a person reading every workflow change, and this machine is the owner's own
computer with his accounts on it. The ssh path gives GitHub, and anyone who can open a pull request, no way onto it.

## What runs there, and what it could still reach

Only our code: the gate names the commit, and `air-sim.sh` refuses (exit 77, the gate then uses GitHub) unless
`origin` is `wilsonguenther-dev/critical-mass-unity` and the commit is reachable from the named branch as it is on
GitHub right now (`git ls-remote` + `merge-base --is-ancestor`). No fork, pull-request ref or unpushed commit is ever
sent. ssh never forwards the agent, X11 or ports (`_remote-lib.sh`). On the Air the job re-runs itself under
`env -i` with HOME, TMPDIR, the build output and every temp file inside `~/cm-remote`, and no inherited variables.

That keeps honest code in its folder. It is not a sandbox. The tests run as the owner's own macOS user, so a
malicious test (one that got merged into one of our branches) could still: read any file of his by absolute path
(Documents, browser profiles under `~/Library`, iCloud Drive files that are downloaded, the Air's own ssh key
`~/.ssh/id_ed25519`), ask the logged-in Keychain for saved passwords, including the GitHub login that `gh` and git
keep there (account `wilsonguenther-dev`: write access to his repos), open network connections to the internet and
the home network, and reach the four repos on the Air that hold unpushed work. It could not reach the main Mac over
ssh with the Air's key (that key is not in the main Mac's `authorized_keys` and is not registered on GitHub;
checked 2026-11-21) and it gains nothing from the main Mac's key (never forwarded). FileVault and the firewall are on.
The real fix is a separate standard user on the Air with no Apple ID, no Keychain logins and no keys, which needs
the owner (admin rights); the kit runs unchanged under such a user (`CM_REMOTE_USER=cmworker`).

## Remove it from the Air

What this adds on the Air, all under `~/cm-remote`: `sim/` (the synced tree and its build output, about 60 MB),
`bin/air-sim-job.sh`, `bin/air-sim-weights.tsv`, and short-lived `runs/<id>/` folders. From the main Mac:

```
ssh wilsons-macbook-air.local 'rm -rf ~/cm-remote/sim ~/cm-remote/bin/air-sim-job.sh ~/cm-remote/bin/air-sim-weights.tsv'
```

The rest of `~/cm-remote` (the .NET SDK, ffmpeg, the Python venv, the play-test runner, and the tunnel program in
`bin/`) belongs to the older kit in `tools/remote/README.md`; `rm -rf ~/cm-remote` removes all of it. Nothing outside
`~/cm-remote` is ever written, no settings are changed, and nothing needs sudo. On the main Mac, deleting
`tools/remote/air-sim*` and the `--on air` block in `loop/gates/remote` removes the feature.
