# Critical Mass (Unity): build-loop rules

Written 2026-11-12T03:08:23Z (date -u). The harness scripts that enforce these rules live in this folder and are
committed (U-011); section 10 lists them (added 2026-11-12T04:15Z).

Sources: decision **U-011** ([../DECISIONS.md](../DECISIONS.md)) and the launch rules section of the browser loop's
harness post-mortem (`Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/harness-postmortem-2026-11-11.md`, section 7).
Each rule is one line, followed by the defect it prevents. `R##` numbers are that post-mortem's defect table.

A **seat** is one agent with one job in the loop (builder, integrator, lander, reviewer). A **batch** is the set of
finished items the integrator compiles and gates together. A **gate** is a check a batch must pass before it lands.

## 1. Items

| Rule | Prevents |
| --- | --- |
| Every item carries its method, files, numbers, keys and pass check; no "or" choices. | Builders deciding design: CM-32's jump key, CM-40's mask method, CM-37 landing three times (post-mortem section 3). |
| A builder that meets a missing answer reports it as an open question with a recommendation and does not decide it. | At least 30 builder-written decision entries in the browser log. |
| Decision ids are `U-###`, allocated only by the orchestrator; builders never append to DECISIONS.md. | D-067 used five times, D-065 and D-066 three times each (R17). |
| Only the item tools (merge mode, add-item) write the items file; it is never wiped. | Lost item state between runs. |
| Mechanics belong to Max and the look to the loop, fixed before launch (U-012). The owner took every Max item for the loop on 2026-11-12 (U-052, replaces U-045); Max reviews each PR afterwards. All art is the loop's, made in Blender by script; no art waits on Max (U-043). | The ownership split decided mid-run after mechanics had been coded and re-landed (R22). |

## 2. Builders, batches, gates

| Rule | Prevents |
| --- | --- |
| Builders write code and tests only, commit and push a branch; they never compile, run Unity or build. | Several Unity instances fighting over one machine; the browser's ~29-minute verify per bounce (R3). |
| One compile per batch (up to 12 items, U-037), and never more than one Unity compile running at a time across the session. | Compile-lock contention and false "compile overdue" alarms (R7). |
| On the first red, the red item is dropped from the batch back to a builder and the rest land; items in the batch that wait on it drop with it, uncounted. | One bounce per full re-verify round; 144 minutes for 2 items; revert and reapply churn (R3). |
| Coupling is per item (U-026, U-033): each `_ORDER.md` batch row names its **coupled set**. All builders finish (in the batch's waves under the launch's `--builders N`, ceiling 12, U-037), one compile; a red in the coupled set goes to one Opus fix seat that repairs only the red item's `files_allowed`, at most 2 fix rounds, then HALT; the coupled set lands whole or not at all. An uncoupled item of the same batch (F-01b, F-07, F-09, V-01) drops alone on its first red and is retried after the set lands. A coupled item never waits on an uncoupled sibling (preflight). | Cyclic ported modules that cannot compile item by item (O-21, O-22). |
| Order (U-030): CriticalMass.Editor.ProjectSetup.Apply runs on the integration branch after every item branch of the batch is merged and before the compile gate, on every batch (it is idempotent); nothing runs Unity on that branch before it. Non-zero fails the batch (U-029). | Project settings that differ between trees. |
| Builders never write `.meta`, `.unity`, `.prefab`, `.asset` or `.mat` files (U-027; `Controls.inputactions` and its meta excepted); the scope gate fails a branch that carries one. After the compile the harness commits what Unity generated (every `*.meta` under `Assets/`, and what ProjectSetup.Apply changed under `Assets/_Game/Settings/`, `Assets/_Game/Resources/`, `ProjectSettings/`), attributed to the harness, not an item; the `clean-reimport` gate then proves a second import leaves `git status` clean (U-030). | Metas that never reach main, GUIDs chosen by hand, a tree dirty after every import. |
| Tests in category "Slow" are excluded from the per-batch EditMode gate (`-testCategory "!Slow"`) and run by the `soak` gate (`tools/unity-soak.sh`) once after each batch lands; the soak lane REPORTS: a red writes an ADVISORY entry for the architect and flags the batch, it never halts or blocks a batch (U-034, replaces U-028's BLOCKING rule). | Slow outcome tests multiplying every batch's gate time. |
| Review findings marked BLOCKING go to `loop/review/<batch>.md`; the next batch does not start while one is unresolved (`- RESOLVED <key>: item <ID>` or `- WAIVED <key>: U-###`, architect only). | Review findings nobody acts on. |
| A batch that runs past 40 minutes is cut. | Batches that never finish (Decisions 2026-11-08, never carried into the browser loop, R3). |
| Gates, all on this Mac: compile; EditMode tests (the sim oracle); PlayMode smoke test (boot, 16v16 bots, 60 simulated seconds, zero exceptions); Mac build; parity capture for art items. | Rules that broke in play but no builder could see it, such as the jump leaking to every bot (R19). |
| Shared limits (frame time, draw calls, shader variants) are measured by the gate and reported per batch; no builder is asked to raise one. | Most browser bounces: crowd draw calls over 900, rim, terrain and shader-program budgets a builder could not measure (R16). |
| Parity is measured only by the U-010 method in [../PARITY.md](../PARITY.md); a builder never designs the measure. | A parity measure that measured the background and reported false PASS lines (R18). |
| No play-testing by agents; Wilson and Jeisil play every build; Wilson runs F-08a's human play check (U-041). Nothing is sent to Travis until the demo is completely done; internal grey-box builds for the team's own play checks are fine (U-044). | Agents reporting a feel they cannot observe. |

## 3. PRs and landing

| Rule | Prevents |
| --- | --- |
| Every PR goes straight to `main`; no stacked PRs, no integration base branches. | A stacked PR merged into its integration base instead of main, so a batch "merged" but never reached main (R1). |
| Docs ride in the item's own PR; no separate docs PRs while a batch is open. | Docs PRs merged under an open batch PR, making it conflict and stopping the run (R2). |
| "Landed" means the item's marker is in a merged PR whose commit is an ancestor of `origin/main` and not reverted, checked by full id. | Items marked missed or landed by subject or prefix match (CM-4 matching CM-40, R5). |
| Never delete an unlanded branch; bundle it first. | Lost work from dead seats. |

## 4. Spend

| Rule | Prevents |
| --- | --- |
| The spend cap counts the workflow's own reported total tokens (U-014), never the output-only counter. | A governor counting output only, 6 to 17 times under real use (R11). |
| Wilson sets the cap; it is never raised without his word. | Silent overspend. |
| No idle or dispatch agents; the loop reacts to events without per-lane wait seats. | 489 idle and dispatch agents burning 59 M tokens in one day (R12); a COST stop during a productive hour (R10). |
| Wilson's weekly usage percentage is logged at every launch and the delta at every close. | Not knowing what a run cost. |

**Decided (U-014).** The cap counts the workflow's own reported total tokens (the measure that read 39.4M on Monday
for about 14 landed items, about 2.8M per item), not the old output-only counter. Wilson sets the budget number.

## 5. Pre-launch checklist (every launch)

| Rule | Prevents |
| --- | --- |
| One loop at a time on this Mac. | Two loops sharing CPU, disk, lanes and the compile lock. |
| Disk: `df -g /System/Volumes/Data` shows at least 30 GB free, plus a stale-artifact sweep; the launch card records free GB before and after; builds pause under 15 GB. | A run dying on a full disk (each Unity lane carries its own `Library/`). |
| Power: a 70 W or 96 W charger, or at least 50 % battery. | A run stopping when the laptop sleeps or throttles. |
| Network and GitHub reachable (`gh auth status`), `main` clean, pending docs landed first. | Launching onto a dirty or unreachable main. |
| Plan audit written before launch (play check, captures, files, size, coverage, ticks, holds). | Items launched with missing answers. |
| Dry run of the harness passes, and the harness files match the committed, dry-run-verified version. | A helper edited mid-run that no longer matches what was tested (R20). |
| The stop file is absent before launch. | Launching into a stopped state. |
| Sweep orphaned processes from earlier runs by recorded pid before every relaunch. | The browser watchdog (pid 55496) still running two days after a "killed" shutdown log (R13). |
| Launch fresh, never resumed; record the launch task id and the run id. | Resume caches that replay by position, and a run that cannot be stopped. |
| Every agent prompt carries the guardrail block: no Agent/Task/Workflow tools, a tool-call cap, one agent per task. | Nested agents running away with the budget. |

## 6. While running

| Rule | Prevents |
| --- | --- |
| Arm a one-line background stall alarm at launch (log quiet 40 minutes, or the stop file appears). | A loop that stalls silently. |
| Arm a memory watchdog at launch that samples RSS every 60 s, with **Unity in its process list**: the Unity editor and its child processes (asset import workers, shader compiler, package manager, the build backend), alongside node and Chrome. | The browser watchdog's process list (node, cargo, rustc, wilson-voice, Chrome) never seeing Unity or its compilers. |
| Write an expected-by schedule in plain Central times at launch and at every batch start, and arm a clock per checkpoint. | Slips nobody notices until the end. |
| An end time Wilson gives is a hard stop; it never moves and is never re-estimated. | Runs that overrun the time he set. |
| Build-only by default (no users): one review per landed batch by a single Opus reviewer, and no second review stage (U-039). | Review seats spending more than the builders on a product nobody plays yet. |
| Never block on a wait; after dispatching, end the turn and react to the notification. | An orchestrator burning tokens and context sitting on a running task. |
| Agents use headless browsers only, and a browser-count monitor runs during agent work. | Windows opening on Wilson's screen. |
| The Obsidian live log is written live, one plain sentence per event, stamped from `date -u`. | Logs reconstructed afterwards from memory. |

**OPEN:** the memory watchdog's thresholds and the exact process names for Unity on this Mac (the browser rule was
node plus Chrome RSS over 10 GB or free pages under 50k; Unity was not running when the names were checked). Note that
Unity Hub is an Electron app and ships its own `chrome_crashpad_handler`, which a Chrome-count monitor must not count.

## 7. Seats and models

| Rule | Prevents |
| --- | --- |
| At most 12 agents running at once (U-037, replaces U-015's 6): the builders of a wave run together up to `--builders N` (1..12, default 5); the integrator, the fix seat and the reviewer each run alone after them, so the peak is the largest wave. | Memory and rate exhaustion on this Mac. |
| Every seat is Opus (U-046): builders, fixer, integrator (high effort), reviewer, parity feature reviewer. No seat that builds, fixes or integrates code ever runs on Sonnet or Haiku; `tightly_defined` in an item no longer affects the model (information only); `seat()` in the workflow refuses any non-Opus model and halts. Subagents are never Fable. | Weak models on judgement seats, and expensive models on mechanical ones. |
| No third-party model in any seat; a second opinion is a second Opus with a different lens. | Reviews from models the team does not use. |
| A mechanical seat that returns nothing is retried once; a second empty return halts the run. | A usage limit read as a result. |

**Decided (U-037, replaces U-015).** At most 12 agents at once; the builder limit is a launch argument (`--builders N`, ceiling 12).

## 8. Texting

| Rule | Prevents |
| --- | --- |
| Status texts to Wilson go by iMessage/SMS through the notify script, not email. | Status lost in an inbox. |
| One card per landed batch, sent by the helper itself. | Batches landing without Wilson knowing where to play them. |
| On a stop, one plain sentence. | Walls of text at the moment Wilson needs a decision. |
| Quiet hours 23:00 to 07:30 Chicago hold digests only; a HALT always texts (ruling 7). | Texts at night, and a halt nobody hears about. |
| The team group chat gets signal only: build state, answers the team owes, direction, slips, final. | 8 group texts in 2 hours of schedule slips (R4). |
| Times are plain Central clock times. | Wilson unable to read the times. |
| Never text Jeisil without Wilson's yes. | Messages the team did not agree to. |
| A question to Wilson arms a reply monitor, and the listener reads his own sends. | Missed replies (R8: the listener skipped every message from his phone). |

## 9. Close

| Rule | Prevents |
| --- | --- |
| Loop end is teardown: delete lane clones and worktrees (bundle unpushed work first), each lane's generated folders (for Unity: `Library/`, `Temp/`, `Logs/`, `Builds/`), and headless browser profiles. | Disk leaks from loop clones. |
| Close deletes only what the run created. | A close deleting shared inputs (the browser-era model cache). |
| Close stops every process it started by recorded pid, never by name pattern, and proves each pid is gone before the log says so. | A shutdown log claiming the watchdog was killed while it ran on (R13). |
| Dead seats' lanes are reset before the next build. | Dirty lanes that make recon refuse builds (R15). |
| Close reports the usage delta and the landed list (landed = merged into main and an ancestor of `origin/main`). | Counting items that never reached main. |

**OPEN:** three browser defects that are still open have no written rule in the decisions or the launch rules: R4
(the schedule clock texted slips for every not-started item at every check, including person-owned items), R6 (two
card flushers raced and sent a duplicate digest) and R14 (a dead integrator's compile lock blocked the next run).
Recommended rules: slips go out as one digest per checkpoint and skip person-owned items; exactly one process flushes
cards; the compile lock records its owner pid and a lock whose pid is dead is reclaimed at arm.

## 9b. Automated runs never show a window (HARD)

Any player started with an automation flag (`-autoplay`, `-quitAfterMatch`, `-autoplayMatches`, `-autoplayQualities`,
`-perfLog`, `-shots`, `-simHashOut`, `-capture`, `-soak`, `-traceStates`, `-debugHangSeconds`, `-automation`) hides
itself: no Dock icon, no Dock bounce, windows transparent and click-through, app hidden, audio muted, fullscreen requests
turned into a hidden window, and re-hidden on the same frame whenever Unity re-shows or activates it (it does so at boot
and at every match load: that is why `open -g -j` alone failed on 2026-11-13). This lives in the game
(`Assets/_Game/Runtime/Game/AutomationWindow.cs`, tests `Runtime.Tests/AutomationWindowTests.cs`), so no script can
forget it. A person who wants to watch passes `-showWindow` (demo-smoke: `CM_SMOKE_VISIBLE=1`). The player log says
`[CM.Window] automated run: window hidden`; if a test window is ever on the owner's screen, the build predates this or
`-showWindow` was passed: find the launcher, never the owner's problem. Builds older than this change still pop a window:
do not launch them for automation.

## 10. The harness (scripts in this folder)

Python 3 stdlib and bash only; nothing is "found on the machine" beyond Unity, git, `gh`, `xmllint` and node (for the
workflow dry run). `loop/logs/` and `loop/state/` are git-ignored.

| Path | Job |
| --- | --- |
| `items/<ID>.md` | One item per file. Front-matter: `id, title, phase, owner (loop/max), waits_on, files_allowed, files_forbidden, tests_required, pass_check, art_row`; optional `tightly_defined`, `architect_approved: U-###`. Files starting with `_` (conventions) are not items. Fixtures go in `items/_fixtures/` and are never queued. |
| `bin/validate-items.py` | Rejects a missing field, "or" / "either" / "if installed" / "TBD" in the method (every `##` section except `## Out of scope`, ruling 1; no `## Method` heading needed), and any reach into `ProjectSettings/`, `Packages/` or `Controls.inputactions` without `architect_approved: U-###`. |
| `bin/cmloop.py` | The one shared library: paths, compile lock (owner, pid, start; dead pid reclaimed), landed check (full-id `loop-landed: <ID>` line on origin/main, not reverted), lanes, returns and parking, the Obsidian live log, the single digest sender, PR open/merge (base must be `main`; no docs-only PR while a batch PR is open), halt check, preflight, plan, shutdown. |
| `bin/integrate.py` | The integrator's mechanical part: merge the batch onto `loop/int-<tag>-<N>` cut from origin/main, scope-check, run setup then the gates once per round; uncoupled: drop the red item (and its in-batch dependants) with its failure text and re-run; `--coupled`: no drop, write the evidence for the fix seat, halt after 2 fix rounds; green: one PR to main, squash, prove landed, one card, then the soak gate on the new main. |
| `bin/spend.py` | Spend governor on the workflow's own `totalTokens` (U-014); live transcript estimate until that file exists. 90 % writes `STOP-DISPATCH`, 100 % writes `HALT`. |
| `bin/launch.sh`, `bin/shutdown.sh` | Launch checklist and watcher start; teardown by pidfile, lanes pushed or bundled, resume note. Both take `--dry-run`. |
| `bin/halt-check.sh` | First step of every seat: exit 3 halt/stall, 4 stop-dispatch, 5 launch ids not recorded. |
| `gates/{setup,clean-reimport,compile,editmode,playmode-smoke,build-mac,build-windows,parity,scope,budgets,soak}` | One JSON line each, real exit codes; `skipped` exits 78, never 0. `build-windows` (U-051, the shipping build) runs on the new main after a landed batch once batch 4's coupled set is on main; skipped without the editor's Windows module; a red is a BLOCKING review entry. `budgets` only reports or flags (no limits yet, ruling 4). `playmode-smoke` calls `tools/unity-playtest.sh` (assembly `Assets/_Game/Runtime.PlayTests`, F-08c); `soak` calls `tools/unity-soak.sh`; both report skipped until those scripts exist. |
| `gates/precompile` | ci/offload (2026-11-13), the FIRST gate: all six assemblies type-checked by Unity's bundled compiler with the arguments Unity last wrote to `Library/Bee/artifacts/*/<Assembly>.rsp`, the worktree's current sources, no editor, no cm-heavy slot, ~3 s. A pass means Unity's compiler accepts the sources; a new asmdef or package still needs the Unity compile gate. |
| `../tools/ci/sim-tests-mac.sh`, `../tools/ci/sim-tests.sh` | The no-Unity Sim gate: Sim + Sim.Tests compiled (C# 9; CI: .NET Standard 2.1) and run without the editor. Mac: Unity's bundled csc + Mono (~30 s). CI and any machine with a .NET SDK: NUnitLite (`.github/workflows/sim-tests.yml`). |
| `gates/remote` | Run a gate on GitHub instead of the Mac (one check per 120 s, LOW-CHURN-NOTICE). `sim` needs nothing; `editmode`, `playmode`, `tests`, `builds`, `all` use `.github/workflows/unity-ci.yml` (GameCI, Unity licence secrets). `build-mac`, `build-windows` and `blender` delegate to `../tools/ci/offload.sh` (section 11): the DEFAULT build path. |
| `RequiresGpu` (NUnit category) | ci/playmode-triage, 2026-11-13: a PlayMode test that needs a real GPU (a rendered frame, a read-back) carries `[Category("RequiresGpu")]`. `unity-ci.yml` always adds `;!RequiresGpu` to the category filter (GitHub's runners have no GPU), while the Mac integrator (`tools/unity-playtest.sh`, `gates/gate.py playmode-smoke`) passes no such exclusion and still runs them. GitHub runs PlayMode `-nographics` like the Mac gate, in 3 parallel shards by fixture, with `-cmTestTimeScale 4` (wall-clock wait budgets in tests go through `TestClock.Budget`, scale 1 on the Mac; product promises are never scaled). `gates/remote playmode --graphics software` runs it under llvmpipe instead (diagnosis: the batch-mode Editor never runs WaitForEndOfFrame, so still captures cannot complete there). |
| `../tools/library-seed.sh` | Seeds a new worktree's `Library/` from the most recent one with APFS clones (`cp -c`): 3.4 GB in ~4 s for ~15 MB of disk; Bee, BuildPlayerData and ScriptAssemblies dropped (old absolute paths). Integrators call it right after `git worktree add`, instead of moving a Library by hand. |
| `review/<batch>.md` | Review BLOCKING and ADVISORY entries (ruling 6); soak reports as ADVISORY (U-034). |
| `logs/ledger.jsonl` | U-042: one row per item state change (dropped, parked, landed) and, at shutdown, exactly one final row per item of the run (landed with its `loop-landed:` commit, dropped with its failure file, parked with the reason, including "not reached"). Survives shutdown; `cmloop.py ledger audit` checks it. |
| `logs/batch-report-<run>-<N>.{json,md}` | U-040: tokens per seat kind, integrator runs, compile and fix rounds for each batch (the reviewer writes it with `spend.py batch-report`; shutdown fills any missing). Baseline: batch 1 = 7 agents, 808,397 subagent tokens, 162 tool uses, 17.6 min, about 162k tokens per landed item. |
| `watchers/watch.py {stall,memory,clock,disk,spend}` | Plain processes with pidfiles. Stall stays quiet while a live owner holds the compile lock. |
| `workflow/critical-mass-unity.js` | The Workflow script. Scheduling is plain code: the batch's waves under the builder limit (1..12), then the integrator; stable item order, `agent()` null = halt, every seat Opus, payloads under 40 KB. |
| `tests/` | `python3 -m pytest -q loop/tests` and `node loop/tests/js/sim-workflow.mjs` (the workflow run against mock agents). |

**Launch (orchestrator):** `loop/bin/launch.sh --cap <tokens> [--batches 2,3,4] [--builders 12] [--rebuild]` (U-050: an item whose pushed `origin/loop/<ID>` merges cleanly with origin/main and was never returned is reused, not rebuilt; `plan --show` lists them, record-launch writes their `reused` ledger rows; `--rebuild` turns that off) (the batch table with coupled sets, waves and peak agents is printed by `cmloop.py plan --show`) (`--batches` writes only those `_ORDER.md` batches into the args file, so the run stops after them; the batch-1 cost run uses `--batches 1`); then `Workflow({scriptPath: loop/workflow/critical-mass-unity.js, args: <loop/logs/launch-args.json>})`, fresh, never resumed; then at once `python3 loop/bin/cmloop.py record-launch --task-id <w...> --run-id <wf_...>` (builders wait at most 120 s for it). **Stop:** `TaskStop(<task id>)`, then `loop/bin/shutdown.sh`.

The defects in the OPEN note above are now handled: R4 (slips go to the live log once per checkpoint, person-owned
items skipped, never texted), R6 (one flusher under a lock, one text per key ever), R14 (dead-pid locks reclaimed).
The Unity process names in section 6's OPEN were measured during a compile and are listed in `watchers/watch.py`; the
memory threshold (14 GB of loop RSS, report only) is still the architect's to set.

## 11. Builds and Blender jobs run on GitHub now (ci/offload-2, 2026-11-13)

Unity player builds and scripted Blender jobs run on GitHub's standard runners (free: the repository is public), not in
the Mac's one heavy slot. **Local Unity builds (`gates/build-mac`, `tools/unity-build-mac.sh`) only when GitHub Actions is
down** (https://www.githubstatus.com). Kit guide, costs, limits and traps: `../docs/CI-OFFLOAD-KIT.md`.

| Command | What it does |
| --- | --- |
| `loop/gates/remote build-mac --ref <branch>` | Dispatches `unity-build.yml` from main for that branch (Linux runner, GameCI mac-mono, the same `CriticalMass.Editor.Build.MacOS` as the local gate), waits, downloads to `~/cm-ci-out/`, unpacks, fixes exec bits, quarantine and the ad-hoc signature, checks the stamp's commit; prints the `.app` path in the JSON line (`app`). `--push` pushes HEAD to the branch first. |
| `loop/gates/remote build-mac --ref <branch> --then-playtest [--mode smoke\|captures]` | The same, then `tools/remote-playtest.sh` on the Air (default `captures`: one match plus shots). Push to play-tested build without the main Mac's heavy slot. |
| `loop/gates/remote build-windows --ref <branch>` | Windows player, unpacked into `~/cm-ci-out/.../player/`. |
| `tools/install-build.sh <app>` | Integrators: installs into `~/critical-mass-unity/Builds` (old one kept as `.prev`); installs as `.app.new` if the game is running from there. |
| `loop/gates/remote blender --ref <branch> --script <path> [--blend <path>] [--args '...'] --out <dir>` | Headless Blender 5.2.3 on GitHub (`blender.yml`): scripted modelling, rigging and deformation metrics, FBX/GLB exports, verification renders (Workbench and EEVEE on llvmpipe software GL, Cycles CPU). `--plan '[{...},{...}]'` runs several scripts in one process; `--inputs <file or dir>` ships files that are not in git (draft release `ci-inputs`, deleted after the job); `--outputs '<glob>'` brings back files the script wrote into the repo. |

Stays on the Mac: the image-to-3D tool (needs Apple GPU memory), PlayMode tests tagged `RequiresGpu`, anything that
needs the owner's screen. Public: everything committed (LFS too), job logs and artifacts (7 days); input bundles on the
draft release are not public.
