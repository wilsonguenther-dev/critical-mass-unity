# Critical Mass: game-mechanics audit (rules and systems)

*Started 2026-11-21T16:24Z (from `date -u`). Read-only on the game: no Unity, no sim code changed, nothing merged.*
Build audited: 1.0.1356 = `integration/batch-15-cand1` @ `aa2ecfef` (sim code identical to `a7433909`, the b15 fix round).
Paths are relative to `Assets/_Game/` unless they say otherwise. Physics (movement, collision, jump, knockback) is the other
audit's ground and is not covered. Duel order, Warrior speed and the cancel button are being changed on
`feat/b16-duel-rules-cancel` and are not re-audited; where that work interacts with a finding it is said.

**How it was measured.** The headless balance tool (`tools/balance/balance-report.sh`, GitHub runner, no Unity) with an
audit-only observer added on the probe branch `ci/balance/audit-mech-r1` (`tools/ci/BalanceReport/AuditProbe.cs`; never
merged). The observer reads the sim from the outside and adds: zones by class, activity by minute, hill seconds lost to each
comeback lever, the lead at every minute, Kaiju feed mix and hill time, hut votes and picks, team stocks, death causes and
places, respawn-to-first-hit, terrain use, pings, class switches. It also plays scripted arms: actor 0 idle, one team's
lineup changed, one team that never feeds. Every arm is 72 bot matches at 16v16 (the balance pool) unless it says 5v5
(200 matches). The control reproduces the release report row for row (Azure 0.639, blowout 0.125, lead changes 4,
leader-at-half 0.458), so the observer does not disturb the sim.

**Noise.** At n 72 a win share has a 95% interval of about +-0.11; at n 144 (an asymmetric arm played on both sides)
+-0.08; at n 200 +-0.07. The control's own side split is 0.639 / 0.361 at n 72, which is inside the noise of 0.5
(earlier run of the same rules: 0.500), so **side-pooled** numbers are used for every class arm.

All numbers are bot matches. Bots never switch class, never dig or place, and vote the hut by a fixed rule, so several
"player choice" systems are measured as the bots play them, not as people will.

---

## 1. Core loop and match arc

### What the rules are
- One way to score: more living bodies inside radius 6 of (56,40) owns the hill (`Sim/Core/Hill.cs:16-80`); equal bodies
  is contested unless one team has HILL ANCHOR (`Hill.cs:43-48`).
- Fixed 480 s, hatch at 240 s (`Sim/Config/GameConfig.cs:24,26`). The higher total wins; within 0.1 s is a draw
  (`Sim/Core/Match.cs:22-40`). **No overtime, no sudden death, no mercy rule.** Draws measured: 0 of 72.
- Four score modifiers now sit on the hill clock (`Sim/B15/Comeback.cs`, defaults in `Sim/Config/ComebackConfig.cs`):
  HOLD FATIGUE (after 20 s of unbroken ownership the owner scores less, down to 25% after 40 s more, `:73-76`), RALLY
  (a team behind by more than 5 s scores x1.5 while it owns, cap 60 s, `:26-32`), KAIJU HOLDS NOTHING (a team scores 0
  while its own sided Kaiju stands in the hill ring, `:67`), and HILL ANCHOR (hut level 5 card).
- Respawn 2 s rising to 5 s over the match (`ComebackConfig.cs:35-39`), then 0.8 s get-up; FORWARD RETURN puts a trailing
  team, or any team whose hill the enemy holds, 40% of the way to the hill (`:84-87`).
- Map events every 60 s from 0:50 (`Sim/Config/DepthConfig.cs:140-141`): supply drop, egg exposed, and the bridge turn,
  which is a quiet minute because collapse is off (`Sim/MapEvents.cs:315-320`). After the hatch the egg slot becomes a
  supply drop.

### What a body does, minute by minute (control, share of body time; observer every 0.5 s)

| class | 0-1 min | 1-3 min | 3-6 min | 6-8 min |
|---|---|---|---|---|
| Warrior | fight 0.36, walk 0.38, hill 0.19 | fight 0.39, walk 0.29, hill 0.22 | fight 0.33, walk 0.31, dead 0.11 | fight 0.28, walk 0.32, dead 0.14 |
| Farmer | carry 0.43, walk 0.38 | carry 0.45, walk 0.34 | fight 0.32, walk 0.28, carry 0.17 | fight 0.40, walk 0.26, hill 0.21 |
| Miner | carry 0.49, walk 0.43 | carry 0.39, walk 0.37 | walk 0.39, fight 0.21, carry 0.20 | walk 0.39, fight 0.19, carry 0.17 |
| Demolitions | walk 0.42, fight 0.31 | fight 0.39, walk 0.31 | fight 0.36, walk 0.31 | fight 0.33, walk 0.31 |
| Builder | carry 0.43, walk 0.43, **standing 0.09** | carry 0.41, walk 0.39, standing 0.08 | walk 0.35, fight 0.21, hill 0.18 | hill 0.29, walk 0.29, fight 0.26 |
| Healer | walk 0.46, fight 0.22, hill 0.21, dead 0.10 | walk 0.39, fight 0.24, dead 0.13 | walk 0.38, hill 0.25, dead 0.14 | walk 0.38, hill 0.24, dead 0.16 |
| all | walk 0.41, fight 0.21, carry 0.20 | walk 0.33, fight 0.26, carry 0.19 | walk 0.32, fight 0.29, hill 0.18 | walk 0.32, fight 0.29, hill 0.22 |

("fight" = a hit dealt or taken or a swing in the last 1 s; "hill" = alive on the disc and not fighting; "walk" = moving,
empty-handed, not fighting; "carry" = holding cargo.) Dead or walking back after a respawn: 0.287 of all body time (target
0.28, report row `time.deadwalkback.all`). Respawn to the first hit dealt or taken: median 10.8 to 13.1 s by class (mean up
to 22 s for Miners, who go mining). **Walking is the largest single activity in every phase (0.32 to 0.41).** That is the
dead time: a third of the match is travel, and the Healer spends 0.38 to 0.46 of it walking plus 0.10 to 0.16 dead.

### Is there always a meaningful choice?
- 0:00-1:00: yes for carriers (which node, which kind to feed, which decides the Kaiju type), weak for fighters (walk to
  the hill). Builders stand still 9% of the first three minutes (bots wait for wood).
- 1:00-4:00: the hill fight plus the egg race. **But the score in this stretch barely predicts the result** (next table).
- 4:00-6:00: the Kaiju decides. 6:00-8:00: mostly settled.

### When the match is decided vs when it ends (control, 72)

| minute | 1:00 | 2:00 | 3:00 | 4:00 | 5:00 | 6:00 | 7:00 |
|---|---|---|---|---|---|---|---|
| the team leading then wins | 0.44 | 0.46 | 0.46 | 0.46 | 0.56 | 0.79 | 0.88 |

- Leading at any point in the first four minutes is worth **nothing or slightly less than nothing** (0.44 to 0.46, noise
  about +-0.12). The comeback levers plus the Kaiju now hand the match to whoever does better after the hatch.
- Last lead change ("decided at"): quartiles 117 / 326 / 412 s; 42% of matches are decided after 6:00, 26% before 2:00.
- Final margin quartiles 19 / 40 / 79 s; 21% of matches end within 15 s. Blowouts (2:1) 0.125.
- Scored hill seconds per match (both teams) median 358 of 480: about 90 s a match are removed by HOLD FATIGUE.

### Match length
Always exactly 480 s (all 72). Nothing can end a match early and nothing extends a close one.

### 5v5 (Quick Match) is a different, worse game (control5, 200 matches)
- Blowouts **0.445** (16v16: 0.125), median margin 103 s, lead changes median 1, leader at half wins **0.865**, 82% of
  matches decided before 2:00 (median "decided at" 28 s).
- Quick Match is 5v5 (`Runtime/Flow/PlayModes.cs`), and so is the guided first match, so the mode a new player meets first is
  the least balanced one. All b15 tuning was done at 16v16.

> Correction to the 5v5 note above: Quick Match runs 240 s with the hatch at 120 s (`Runtime/Flow/PlayModes.cs:86-90`,
> sizes 5 or 8). The control5 arm used the 480 s clock; the Quick clock is measured in section 5b (round 2).

---

## 5. Comeback and scoring (measured before 2-4 so the later sections can lean on it)

### Each lever switched off alone (16v16, 72 matches each; noise on a share about +-0.12)

| arm | Azure win | blowout | lead changes med / mean | leader at half wins | lead at 2:00 / 4:00 / 6:00 wins | decided at (med s) | margin (med s) | Kaiju owner wins | fatigue removes (s/match) | rally adds (s/match) |
|---|---|---|---|---|---|---|---|---|---|---|
| control (all on) | 0.639 | 0.125 | 4 / 4.29 | 0.458 | 0.46 / 0.46 / 0.79 | 326 | 40 | 0.883 (n 60) | 90 | 49 |
| all comeback off | 0.611 | **0.403** | 2 / 2.03 | 0.472 | 0.56 / 0.47 / 0.69 | 305 | 125 | 0.887 | 0 | 0 |
| HOLD FATIGUE off | 0.639 | 0.236 | 4 / 3.86 | 0.486 | 0.44 / 0.49 / 0.72 | 292 | 81 | 0.883 | 0 | 51 |
| RALLY off | 0.458 | 0.194 | 3 / 3.26 | **0.718** | **0.68 / 0.72** / 0.84 | 133 | 54 | **0.610** | 94 | 0 |
| FORWARD RETURN off | 0.611 | 0.139 | 3 / 2.97 | 0.542 | 0.54 / 0.55 / 0.89 | 260 | 47 | 0.776 | 131 | 48 |
| respawn curve off (flat 5 s) | 0.472 | 0.083 | 4 / 3.81 | 0.549 | 0.51 / 0.55 / 0.82 | 277 | 46 | 0.774 | 99 | 49 |
| KAIJU HOLDS NOTHING off | 0.653 | 0.208 | 4 / 4.26 | 0.417 | 0.38 / 0.42 / 0.86 | 300 | 61 | **1.000** (60 of 60) | 91 | 48 |
| no Kaiju (never hatches) | 0.528 | **0.417** | 3 / 3.39 | **0.903** | 0.75 / **0.90** / 0.97 | 116 | 123 | n/a | 68 | 42 |
| hut votes off (flat +5%) | 0.444 | 0.111 | 4 / 4.08 | 0.556 | 0.49 / 0.56 / 0.81 | 305 | 28 | 0.678 | 99 | 51 |
| hut off | 0.458 | 0.194 | 2 / 2.62 | 0.778 | 0.74 / 0.78 / 0.87 | 54 | 43 | 0.603 | 115 | 48 |
| Builder walls off | 0.639 | 0.194 | 4 / 4.07 | 0.597 | 0.52 / 0.60 / 0.74 | 254 | 36 | 0.733 | 90 | 49 |
| terrain height off | identical to the control in every row (bots never raise or lower ground, so it never fires) | | | | | | | | | |

### What each lever does (control numbers)
- **HOLD FATIGUE** is the biggest by seconds: it removes 90 s a match, **71.5 s from the eventual winner and 18.7 s from
  the loser**. It is what keeps blowouts down (0.125 with it, 0.236 without). It does not create lead changes on its own.
- **RALLY** adds 49 s a match (winner 27.3, loser 21.6: both teams trail at some point). It is the lever that **erases the
  first half**: with RALLY on, leading at 2:00 or 4:00 wins 0.46; with it off 0.68 and 0.72, and leader-at-half moves from
  0.458 (target 0.60-0.80, missed) to 0.718 (inside). The reason is the Kaiju: the team behind at the hatch gets the Kaiju
  37 times in 60 (bots that trail send two extra feeders, `Sim/Bots/BotDirector.cs:11,190`), and a trailing team that holds
  the hill with its Kaiju also scores x1.5. Kaiju owner wins 0.883 with RALLY, 0.610 without.
- **FORWARD RETURN** is on for a team 285 of 480 s (it fires whenever the enemy owns the hill, `ComebackConfig.cs:87`), so it
  is no longer a comeback lever but the normal respawn for whichever team is off the hill. Off: fatigue rises to 131 s
  (longer holds), Kaiju owner wins 0.776.
- **Respawn curve** (2 to 5 s): small; off (flat 5 s) blowouts 0.083.
- **KAIJU HOLDS NOTHING**: zeroes the owner's score 60 s a match. Without it the Kaiju owner won 60 of 60. With it 0.883:
  it is the only thing that lets the team without the Kaiju win at all.
- **The Kaiju is the real comeback mechanic.** With no Kaiju the hill fight snowballs: the team leading at 4:00 wins 0.90,
  blowouts 0.417. With it, the team that trailed at the hatch gets it most of the time and wins 32 of 37 of those.

### The owner-approved numbers are not the shipped numbers
Approved 2026-11-17 (growth decision A): RALLY x1.25 capped at 45 s, respawn 3 s rising to 8 s. Shipped
(`ComebackConfig.cs:28-37`): RALLY **x1.5**, cap **60 s**, threshold **5 s** (the summary comment at `:9-14` still says
15 s and x1.25), respawn **2 to 5 s**. Each change has a measured reason in the b15 sweep, but the owner never saw the
final set, and the in-code explanation of the threshold no longer matches the value. Round 2 measures the approved set.

### "A team with one idle player wins too often" (5v5, 200 matches; actor 0 gets no input at all)

| arm (idle team = Azure) | idle team wins | blowout |
|---|---|---|
| control5 (nobody idle) | 0.505 (Azure) | 0.445 |
| idle, all rules | 0.530 (0.46-0.60) | 0.145 |
| idle, HOLD FATIGUE off | 0.500 | 0.260 |
| idle, RALLY off | 0.585 | 0.285 |
| idle, FORWARD RETURN off | 0.545 | 0.195 |
| idle, respawn curve off | 0.530 | 0.140 |
| idle, all comeback off | **0.615 (0.55-0.68)** | 0.430 |
| idle at 16v16 (72) | 0.500; all comeback off 0.458 | |

**Mechanism.** The comeback levers are not the root: with all of them off the idle team wins *more* (0.615, the only
share here clear of 0.5). The root is the bot plan. The director counts a human with no input for 15 s as inactive
(`BotDirector.cs:11` `InactiveSeconds = 15`), so at 5v5 the idle team is "short-handed" (1 of 5 is over
`ShortHandedMinShare` 0.15, `DepthConfig.cs:423`) and switches to the short-handed plan: one egg-duty bot, everyone else on
the hill (`BotDirector.cs:186`). The normal plan sends `round(0.25 x 5) = 1` feeder **plus two more** whenever the team is
ahead (`AdvantageExtra = 2`) or 10 s behind (`GambitExtra = 2`), i.e. 3 of 5 bots away from the hill most of the match.
Those extras are absolute numbers tuned at 16v16 (6 of 16). So at 5v5 "four bots on the hill" beats "five players with
three at the egg". At 16v16 one idle player is 6%, under the threshold, and nothing happens (0.500).
**Rule fix (S, sim):** make the two extras a share of the team (`ceil(0.12 x team)`: 2 at 16, 1 at 5), and treat an idle
human as absent outright: after `InactiveSeconds` hand the fighter to the autopilot (the same call the online drop uses,
`Sim/Bots/MatchTick.cs:95-101`), which is also the missing AFK rule online (section 10).

### Hut-lead snowball
Hut levels at 6:00 differ in only 13 of 72 matches, and the higher team wins 11 of 13 (0.85; hut votes off: 13 of 13;
all comeback off: 28 differ, 26 won). At 4:00 they differ in 12 of 72 and the higher team wins 6 (a coin flip). The
"0.933" snowball is real but rare and late: the level gap appears only once a match is already being won, so it is a
symptom, not a cause. No fix needed beyond what the Kaiju and RALLY items change.

### Lead changes 4 vs the target 5
Median 4 (mean 4.29). Only the arms that make the late game swing more raise it, and none reaches 5. The target counts
sign flips of the cumulative difference; HOLD FATIGUE and RALLY already cut the cumulative lead from 125 s to 40 s, so the
lead is small but rarely flips. Recommendation: replace the target with "share of matches within 30 s at 6:00" (control:
measured in round 2) or "lead at 2:00 wins 0.55-0.70" — both say what the owner cares about (the early game matters, the
end is not certain).

