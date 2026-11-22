# Player-seat telemetry

The player-seat gate (`docs/testing/PLAYER-SEAT-GATE.md`) answers yes or no: does a press land, does it matter, can the
player see it. This tool answers **why**. It writes one record per input edge of one seat: what the player pressed, where
they stood and aimed, what the press resolved to in the sim, how the swing ended and why it missed, what the world did,
and (in Unity) every feedback cue the player was really shown, with its delay. A summary script turns many logs into
per-class tables and diffs two runs, so a tuning change to hits, movement, building, digging, healing or any class verb
can be measured before and after from the player's seat.

It is permanent: the recorder is part of the Sim assembly, the Unity logger ships in Editor and development builds, the
probe and the summary live under `tools/px-telemetry/`.

## The pieces

| Piece | File | What it does |
| --- | --- | --- |
| Recorder (shared) | `Assets/_Game/Sim/Diagnostics/PlayerSeatLog.cs` | Plain C#, no Unity types. Fed once per tick with the World and the frame the seat was given; pairs each press with its answer from the event ring; writes JSON lines to a sink. Never writes the World. Not part of the hashed state. |
| Unity logger | `Assets/_Game/Runtime/Game/PlayerSeatLogger.cs` | Editor and development builds only (compiled out of release), off by default. Adds the wall clock and the `PlayerFeedback` ledger; one file per match. |
| Hook | `PlayerSeatLogHook.AfterTick` (same file), raised from `SimDriverBehaviour.Tick` | The only line added to existing code. Null unless the logger is on. |
| Editor switch | `Assets/_Game/Editor/PlayerSeatLogMenu.cs` | Menu `Critical Mass / Dev / Player-seat log` (a checkmark). |
| Headless probe | `tools/px-telemetry/run.sh`, `PlayerSeatTelemetryProbe.cs` | Plays duels or full matches with the gate's human model, no Editor. |
| Second Mac sweep | `tools/px-telemetry/air.sh`, `PxTelemetry.csproj` | The same probe built with .NET on the second Mac. |
| Summary | `tools/px-telemetry/summarize.py` | Python 3 standard library. Tables per class, dead-press list, `--compare A B`. |
| Tests | `Sim.Tests/Diagnostics/PlayerSeatLogTests.cs`, `Runtime.Tests/PlayerSeatLoggerTests.cs` | The recorder never changes a match (StateHash equal with and without it, Full, Classic and PhysicsOnly); every scripted press gets exactly one record with a resolution; the Unity logger is off by default and allocates nothing when off. |

## Switch it on

**In the Editor.** Tick `Critical Mass / Dev / Player-seat log`. It sets the PlayerPrefs key `cm.dev.playerSeatLog = 1`;
every match you play after that writes a file. While in play mode the tick also starts or stops it at once. The Console
prints `[CM.PlayerSeatLog] recording the local player to <path>` at the start of a match and the record count at the end.

**In a development build.** Start it with the flag: `"Critical Mass.app/Contents/MacOS/Critical Mass" -playerSeatLog`
(Windows: `CriticalMass.exe -playerSeatLog`). The PlayerPrefs key works too. A release build has no logger at all
(`#if UNITY_EDITOR || DEVELOPMENT_BUILD`), and `Wanted()` also checks `Debug.isDebugBuild`.

Files go to `Application.persistentDataPath/PlayerSeatLogs/<utc yyyyMMdd-HHmmss>-<class>.jsonl`. On this Mac that is
`~/Library/Application Support/<company>/<product>/PlayerSeatLogs/`; the Console line prints the exact path.

From code (a test, a debug console): `PlayerSeatLogger.Enable()` / `Disable()`; `PlayerSeatLogger.CurrentPath`.

Limits of the Unity logger: it follows `LocalSeat.Actor` in single-player and practice matches (the path through
`SimDriverBehaviour.Tick`). Online rooms step the world elsewhere and are not hooked. The ledger is read from its ring
(512 entries); a frame with more than 512 cues loses the oldest.

**Headless (no Unity at all).**

```
tools/px-telemetry/run.sh --class Warrior --vs all --seeds 8 --reaction 6        # 48 duels, about 1 s
tools/px-telemetry/run.sh --class all --vs all --seeds 8                          # 288 duels, about 5 s
tools/px-telemetry/run.sh --class Miner --mode 16v16 --seeds 4 --ticks 5400       # four 3-minute 16v16 matches
python3 tools/px-telemetry/summarize.py <the out folder it printed>
```

| Option | Default | Meaning |
| --- | --- | --- |
| `--class` | Warrior | the seat's class; `all` or a comma list |
| `--vs` | all | duel opponents (a real `BotBrain` bot) |
| `--mode` | duel | `duel` (the gate's staged duel: both bodies on the hill, everyone else removed), `5v5`, `16v16` (a full match, seat 0 fights the nearest enemy) |
| `--seeds` | 8 | runs per class pair (duel: start angle k/8 and seed 4242+k; match: seed `--seed0`+k) |
| `--reaction` | 6 | the human model's tracking lag in ticks (6 = 0.2 s) |
| `--aim-help` | 0 | the shipped aim help modelled on the attack aim: 0 Off, 1 Low, 2 High |
| `--skill` | normal | `first` (every other swing aimed 90 degrees off), `normal`, `strong` (feints, hit-cancels, dodges the wind-up) |
| `--verbs` | all | `all` also presses ability every 4 s, F every 3 s, interact held 1 s every 6 s, jump every 5 s, dodge every 7 s, cancel every 5 s; `attack` = the primary only |
| `--bots` | Normal | the opponents' difficulty |
| `--ticks` | duel 30 s, match full length | the cap |
| `--out` | `~/cm-incidents/player-experience-rebuild-2026-11-22/data/t-telemetry/runs/<utc stamp>` | the folder (one file per class pair) |

`run.sh --root DIR` runs against another checkout (for a before/after on two branches).

## How a press is paired with its answer

The recorder reads, after every tick, the frame the seat was given (not `w.Inputs`: the step clears the one-tick buttons
Dodge, Cancel and Jump there) and every event pushed during that tick.

- A **press** is a rising edge of Attack, Ability, F (`Terraform`, shown as `dig_place`), Interact, Dodge, Cancel or Jump;
  a **release** is a falling edge. A swing that starts while Attack stays held, with no new edge, is a `repeat` record.
- The press waits up to `ResolveWindowTicks` (14 = 0.47 s: the 0.35 s sim buffer plus slack) for the first event that
  answers it. Answer events, by button: Attack: `AttackStart` (melee), `HealCast` mend, `TntThrown`, `ActionRefused`;
  Ability: `AttackStart` (ability kinds), `Ability` (code > 0 cast, < 0 refused), `HealCast` pulse, `MinePlaced`,
  `PiecePlaced`, `BarricadePlaced`; F: `Terraform` (dug / placed / negative refused), `OreMined`, `WallBuilt`,
  `WorldAction` dig / place / build / mine, a Demolitions `TntThrown`; Interact: `WorldAction` gather / bank / feed ...,
  `Gather`, `SourceGather`, deliveries, `BankDeposit`, `Handoff`, `PiecePickedUp`, `DeliverRefused`; Dodge: `Dodge`;
  Cancel: `Feint` (1 feint, 2 hit-cancel); Jump: the body leaves the ground (`AirLen > 0`) or `JumpRefused`. Any of them
  with `ActionRefused` for that button (A = seat, B = `PlayerReasons.Button*`). Nothing in the window: `nothing`.
- `resolve_ticks` = answer tick minus press tick. A melee `AttackStart` after the press tick is `buffered`.
- A swing stays open until its `AttackContact` (or `AttackCancel` = `interrupted`, a `Feint` = `cancelled`). At contact
  the same tick's `Hit` (B = seat), `HitBlocked`, `StructureHit`, `CriticalHit` and `Death` give damage, tier, critical
  kind, blocked reason and kills. With no hit, no block and no structure it is a `miss`, and the nearest living enemy at
  contact gives the reason: `out_of_range` (by `d - (range + body)` u), `outside_arc` (by `|angle off the locked facing|
  - (arc/2 + asin(body/d))` degrees), `range_and_arc`, `target_dodged` (the target started a dodge between the press and
  the contact and ended out of reach or arc), `no_target`, `other` (inside both: a cap, a wall or a rule the geometry
  does not see). Positions are read after the step, as the gate's probe does: a sub-tick approximation.
- After its last sim answer a record stays open `CueWindowTicks` (15 = 0.5 s) so view cues can attach, then it is written.
  Records are therefore written in close order, not press order: sort by `tick` when reading by hand.
- In Unity each `PlayerFeedback` ledger entry attaches to the newest open press (incoming channels `OwnHurtVignette`,
  `DamageDirection`, `OwnHurtSound`, `LowHealthCue` to the newest hit-taken record). Delay = cue time minus press time in
  ms and frames (and ticks when the ledger's `SimTick` is stamped). Headless runs have no view: `cues` is `[]` and
  `cue_source` is `none`.
- Ticks while the idle autopilot drives the seat count as no input (`autopilot_ticks` in the summary); the human model
  never idles in practice.

## Schema (version 1)

One JSON object per line. `schema` changes only on a rename or a change of meaning; new fields can appear at any time.
Angles in degrees, positions in sim units (x, z), `t` in sim seconds since the match started, `tick` = the tick that
consumed the input.

**header**: `schema, source (headless|unity), label, seed, rules (Full|Classic|PhysicsOnly|Custom), mode, seat, class,
sim_hz, tick, actors, resolve_window_ticks, cue_window_ticks, move_sample_ticks`.

**press / repeat**: `tick, t, class, button, pos, facing_deg, aim, move, hp, state {stun_ticks, in_air, knocked_down,
winding_up} (after the press tick's step), resolved, resolve_ticks`, then:
- `resolved`: `swing_started | buffered | refused | ability | tnt | mend | pulse | dodge | feint | hit_cancel | jump |
  dig | place | mine | build | mine_placed | piece_placed | barricade | gather | bank | feed | corrupt | purify | repair |
  Gather | SourceGather | DeliverBiomass | BankDeposit | Handoff | PiecePickedUp ... (event names) | dead (pressed while dead) | died (killed before the answer came) | nothing`
- refused: `reason` (the `PlayerReasons.Refused*` code, or the system's own negative code) and `reason_name`
  (`cooldown, no_target, not_your_class, no_resource, blocked_place, staggered, carry_full, out_of_range,
  nothing_to_cancel, team_limit, carrying, in_air, rule_off`, or `terraform:someone_there`, `piece:no_target`,
  `mine:occupied`, `ore:no_vein`, `wall:wood`, `jump:no_landing` ...). Otherwise `detail` may name the ability.
- `target`: the nearest living enemy at the press: `id, class, dist, reach (own range + its body), in_reach,
  off_aim_deg, half_arc_deg, in_arc` (or `null`).
- `swing` (melee only): `start_tick, windup, contact_tick, range, arc_deg, outcome (hit|miss|blocked|structure|
  interrupted|cancelled|lost|pending_at_end), targets_hit, damage, tier, critical (none|counter|punish|back_hit), kills`;
  a miss adds `miss_reason, miss_target, miss_dist, out_of_range_by, outside_arc_by_deg`; a block adds `blocked,
  blocked_name (spawn_protect|dodge|ward|wall|passive|immune|shield|glancing)`; `interrupted_by`; `structures,
  structure_damage`.
- `damage, kills` for a non-melee press that hurt (TNT).
- `world` (when anything changed): `cells_dug, cells_placed, ore, walls, pieces, mines, explosions, structure_damage, heal`.
- `events`: the other events about the seat in the window, `Type:B:Value` (at most 24).
- `cues`: `[{ch, actor, value, reason?, delay_ticks, delay_ms, delay_frames, after_contact_ticks?}]`, `cue_count`, `cue_source`.

**release**: `tick, t, class, button, held_ticks`.

**hit_taken**: `tick, t, class, from (actor id, -1 none, -2 Kaiju), from_class, damage, tier, cause (Melee, Explosion,
Fall, KaijuMelee ...), from_dir_deg (world), from_rel_deg (0 = in front of my facing), from_side (front|side|back),
knock (u), killed, pos, hp_after, cues`.

**move** (one per `MoveSampleTicks` = 0.5 s of held movement; a shorter tail of at least a third is kept): `tick, t,
ticks, class, pos (start), intent (mean unit input), disp, dist, along_intent, angle_error_deg, speed_share (distance /
(class MoveSpeed x time)), surface (flat | stair_up | stair_down | stair | climb | drop | ladder | bridge | air),
level_from, level_to, stuck (speed_share < 0.2), reversals (per-tick displacement turned more than 120 degrees: shiver),
intent_reversals, air_ticks, swing_ticks (in a wind-up or recovery), stun_ticks`. "Slope" does not exist in the sim:
height is whole levels, so a one-level change is a stair and two or more a climb or a drop.

**summary**: `presses, releases, repeats, hits_taken, move_samples, refused, nothing, autopilot_ticks, orphan_cues,
kills, alive`.

## The summary script

```
python3 tools/px-telemetry/summarize.py DIR_OR_FILES...            # per class
python3 tools/px-telemetry/summarize.py DIR --by-opponent           # per class x opponent
python3 tools/px-telemetry/summarize.py DIR --dead 40               # longer dead-press list
python3 tools/px-telemetry/summarize.py --compare BEFORE_DIR AFTER_DIR
```

Per class it prints: presses per button and the share answered / buffered / refused / nothing, with the top
resolutions and the refusal reasons; swings, land rate, miss / blocked / interrupted / cancelled shares and critical
kinds; the miss reasons ranked with the median distance out of range and degrees outside the arc; damage per minute,
kills, wins and losses, median time to the first kill, hits taken per minute and from which side; world changes;
movement: seconds held, share stuck, reversals per minute, and the same per surface with the mean speed share; feedback:
share of presses with at least one cue and the median cue delay (Unity logs only); and the **dead presses**: a press the
sim answered with nothing, refused with no known reason, or (Unity) that raised no cue at all. `--compare` prints every
number for A and B and the difference, flagged with `<`.

## Three worked examples

**1. Tune a hit (the Builder misses at the edge of reach).** Run the baseline, change one number, run again, compare:

```
tools/px-telemetry/run.sh --class Builder --vs all --out /tmp/px-before
# edit the Builder's AttackRange in its class row (a rules change: re-pin Full in the same PR)
tools/px-telemetry/run.sh --class Builder --vs all --out /tmp/px-after
python3 tools/px-telemetry/summarize.py --compare /tmp/px-before /tmp/px-after
```

Read `land_rate`, `miss_out_of_range` and `damage_per_min`, and check the opponents did not flip (`--by-opponent`). A miss
reason "out of range by median 0.22 u" says the swing fell short by a fifth of a unit: the model presses inside reach, so
the target walked out during the wind-up; the lever is reach, wind-up length, or the tracking lock. "Outside the arc by
median 18 deg" points at the arc or the swing tracking instead. In the Editor the same records carry `cues`: a landed hit
whose cues list has no `HitMarker` or `DamageNumber`, or whose `delay_ms` is over 100 ms, is a feedback bug, not a balance
one.

**2. Find dead buttons.** `summarize.py DIR` ends each class with "dead presses". Headless that means silent in the
sim. Example from the first use: a Jump pressed in the last tick of a stagger resolved to `nothing` (no jump and no
`ActionRefused`). In the Editor, play a class for two minutes pressing everything, then run the script on
`PlayerSeatLogs/`: any press with `cue_count` 0 is a button the player pressed and saw nothing for, with its tick,
button and what the sim did, so you can open the frame strip at that tick.

**3. Find where movement sticks.** The movement block lists seconds held, the share stuck and the mean speed per surface.
For a full map rather than the hill duel use a match: `run.sh --class Builder --mode 16v16 --seeds 4 --ticks 5400`. A
surface with a high stuck share (a stair or a ladder) is where to look; the `move` records give the position (`pos`) and
the intent, so `grep '"stuck":true' file | grep '"surface":"stair_up"'` lists the exact places. `reversals` counts
shiver (the body flipping direction tick to tick); `swing_ticks` and `stun_ticks` separate "I was slowed by my own swing
or a stagger" from "the ground held me".

## Big sweeps on the second Mac

`tools/px-telemetry/air.sh` takes the same probe options:

```
git commit ...                                  # the box runs HEAD, not your working tree
tools/px-telemetry/air.sh --class all --vs all --seeds 24 --skill strong --aim-help 2
```

It packs the committed Sim, Net, rig and probe sources with `git archive`, sends them through `tools/remote-run.sh` (the
box's queue lock, so it never runs during a play-test), builds `PxTelemetry.csproj` with the box's .NET SDK, runs the
probe, copies `out/` back to `data/t-telemetry/runs/air-<sha>-<stamp>/` (or `--out DIR`), then deletes its folder on the
box. The box's address and login come from `tools/remote/env.sh` like every other remote job; never write them anywhere.
The local probe is fast (288 duels in about 5 s), so the second Mac pays off for full 16v16 matches at many seeds.

## Rules for changing it

- The recorder must never change a result. `PlayerSeatLogTests.TheRecorderNeverChangesTheMatch...` compares StateHash
  checkpoints with and without it under Full, Classic and PhysicsOnly. It lives in the Sim assembly, so it obeys the Sim
  source rules (no `File.`, clock or `Environment`, transcendentals through `JsMath`, no `.XxxTicks` config field reads).
- Add a field: write it, document it here, leave `SchemaVersion` alone. Rename or change a meaning: bump `SchemaVersion`
  and `SUPPORTED_SCHEMA` in `summarize.py`.
- A new sim event that answers a press: add it to `Answer` (which button it answers) or `Effects` (what it changed).
