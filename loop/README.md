# Critical Mass (Unity): build-loop rules

Written 2026-11-12T03:08:23Z (date -u). Rules only; no harness scripts exist yet. When they are written they live in
this folder and are committed (U-011).

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
| Mechanics belong to Max and the look to the loop, fixed before launch (U-012). | The ownership split decided mid-run after mechanics had been coded and re-landed (R22). |

## 2. Builders, batches, gates

| Rule | Prevents |
| --- | --- |
| Builders write code and tests only, commit and push a branch; they never compile, run Unity or build. | Several Unity instances fighting over one machine; the browser's ~29-minute verify per bounce (R3). |
| One compile per batch of about 6 items, and never more than one Unity compile running at a time across the session. | Compile-lock contention and false "compile overdue" alarms (R7). |
| On the first red, the red item is dropped from the batch back to a builder and the rest land. | One bounce per full re-verify round; 144 minutes for 2 items; revert and reapply churn (R3). |
| A batch that runs past 40 minutes is cut. | Batches that never finish (Decisions 2026-11-08, never carried into the browser loop, R3). |
| Gates, all on this Mac: compile; EditMode tests (the sim oracle); PlayMode smoke test (boot, 16v16 bots, 60 simulated seconds, zero exceptions); Mac build; parity capture for art items. | Rules that broke in play but no builder could see it, such as the jump leaking to every bot (R19). |
| Shared limits (frame time, draw calls, shader variants) are measured by the gate and reported per batch; no builder is asked to raise one. | Most browser bounces: crowd draw calls over 900, rim, terrain and shader-program budgets a builder could not measure (R16). |
| Parity is measured only by the U-010 method in [../PARITY.md](../PARITY.md); a builder never designs the measure. | A parity measure that measured the background and reported false PASS lines (R18). |
| No play-testing by agents; Wilson and Jeisil play every build. | Agents reporting a feel they cannot observe. |

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
| The spend cap counts all tokens: input, output and cache. | A governor counting output only, 6 to 17 times under real use (R11). |
| Wilson sets the cap; it is never raised without his word. | Silent overspend. |
| No idle or dispatch agents; the loop reacts to events without per-lane wait seats. | 489 idle and dispatch agents burning 59 M tokens in one day (R12); a COST stop during a productive hour (R10). |
| Wilson's weekly usage percentage is logged at every launch and the delta at every close. | Not knowing what a run cost. |

**OPEN:** the decision file says to size the budget at "roughly 2.8M total tokens per landed item (Monday's real
figure)". That figure is the workflow's `totalTokens` per landed item, which the post-mortem says is close to cache
writes plus output. All-in usage counting cache reads was about 543 M for 14 items, about 39 M per landed item. A cap
that counts cache (the rule above) sized at 2.8 M per item would stop the loop almost at once. The architect needs to
say which number the cap counts.

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
| Build-only by default (no users): no multi-Opus review chain; one review pass (two Opus lenses) and one fix seat after everything lands. | Review seats spending more than the builders on a product nobody plays yet. |
| Never block on a wait; after dispatching, end the turn and react to the notification. | An orchestrator burning tokens and context sitting on a running task. |
| Agents use headless browsers only, and a browser-count monitor runs during agent work. | Windows opening on Wilson's screen. |
| The Obsidian live log is written live, one plain sentence per event, stamped from `date -u`. | Logs reconstructed afterwards from memory. |

**OPEN:** the memory watchdog's thresholds and the exact process names for Unity on this Mac (the browser rule was
node plus Chrome RSS over 10 GB or free pages under 50k; Unity was not running when the names were checked). Note that
Unity Hub is an Electron app and ships its own `chrome_crashpad_handler`, which a Chrome-count monitor must not count.

## 7. Seats and models

| Rule | Prevents |
| --- | --- |
| At most 6 agents running at once, counting every seat. | Memory and rate exhaustion on this Mac. |
| Builder lanes are Opus; the integrator is Opus at high effort; the lander and mechanical seats are Haiku; the sanity seat is Opus and read-only; the parity feature reviewer is Opus. Subagents are never Fable. | Weak models on judgement seats, and expensive models on mechanical ones. |
| No third-party model in any seat; a second opinion is a second Opus with a different lens. | Reviews from models the team does not use. |
| A mechanical seat that returns nothing is retried once; a second empty return halts the run. | A usage limit read as a result. |

**OPEN:** "6 agents at once, every seat counted" leaves room for 5 builders beside an integrator, while U-011's batch is
"about 6" and the browser START text allowed 6 builders plus 1-2 review/land seats. The architect says which holds.

## 8. Texting

| Rule | Prevents |
| --- | --- |
| Status texts to Wilson go by iMessage/SMS through the notify script, not email. | Status lost in an inbox. |
| One card per landed batch, sent by the helper itself. | Batches landing without Wilson knowing where to play them. |
| On a stop, one plain sentence. | Walls of text at the moment Wilson needs a decision. |
| Quiet hours 23:00 to 07:30 Chicago. | Texts at night. |
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
