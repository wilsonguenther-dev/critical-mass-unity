# Critical Mass: game-mechanics audit (rules and systems)

*Started 2026-11-21T16:24Z (from `date -u`). Read-only on the game: no Unity, no sim code changed, nothing merged.*
Build audited: 1.0.1356 = `integration/batch-15-cand1` @ `aa2ecfef` (sim code identical to `a7433909`, the b15 fix round).
Paths are relative to `Assets/_Game/` unless they say otherwise. Physics (movement, collision, jump, knockback) is the other
audit's ground and is not covered. Duel order, Warrior speed and the cancel button are being changed on
`feat/b16-duel-rules-cancel` and are not re-audited; where that work interacts with a finding it is said.

## Ranked top 15

| # | what | number | who notices | severity | fix (size) | sim / view | decision? |
|---|---|---|---|---|---|---|---|
| 1 | **The first four minutes do not matter; the Kaiju decides the 16v16 match** | leading at 2:00 or 4:00 wins 0.46; Kaiju owner wins 0.883; the team behind at the hatch gets it 37 of 60 | skilled player | high | comeback "combo" for Full Battle and 8v8: x1.25 RALLY, cap 45 s, respawn 3 to 8 s, forward return for the trailing team only, trail threshold kept at 5 s. Measured: lead at 2:00 wins 0.59, owner 0.627, blowouts 0.097 (S, config) | sim | owner (differs from his approved set in two places) |
| 2 | **Stacking Farmers (or dropping Warriors) wins; stacking Warriors loses** | 16 Farmers 0.847; 0 Warriors 0.861; 16 Warriors 0.035; one more Warrior 0.292 | skilled player, premade groups | high | give the Warrior a hill job that turns damage into score, plus a per-team class cap; must be measured with the b16 duel changes (M) | sim | design advisor |
| 3 | **Allied Kaiju AI parks on the hill and zeroes its own team** | idle point 1.5 u from the centre (`Sim/Kaiju.Hatch.cs:36`); NO SCORE 60 s a match | new player ("our score stopped") | high | idle point on the hill rim outside the ring, enemy side (S) | sim | no |
| 4 | **Shipped comeback numbers differ from the owner's approval, and the code comments are stale** | shipped x1.5 / 60 s / 5 s / 2 to 5 s vs approved x1.25 / 45 s / 15 s / 3 to 8 s | nobody (owner) | medium | decide per item 1; fix the comments in `ComebackConfig.cs:9-14` (S) | sim | owner |
| 5 | **The Healer is the most valuable class and the worst experience** | 18 deaths a match (others 7.9 to 13.6), dead or walking back 0.436; 4 Healers win 0.632; bots weight her at 1% of her distance | new player | high | soften the bot focus weight (off: deaths 13.6, blowouts 0.264, so not off) and credit healing on the results screen (S) | sim + view | design advisor (he asked for healer focus) |
| 6 | **No AFK rule; one idle human swings a small match** | idle team at Quick 5v5 wins 0.080 (n 200); at 5v5 480 s 0.530 | everyone online | high | autopilot after 15 s idle + feeder extras as a team share (S) | sim | no |
| 7 | **Hut votes are cast by bots before a human can read the cards** | 15 bots close the vote in about 3 s; 0.8% of votes go to the coin flip | new and skilled player | medium | bots wait until every human has voted or 6 s have passed (S) | sim | no |
| 8 | **Kaiju type picker: Grove and Elder never hatch at 16v16; one wood trip decides the type** | Brute 24, Timber 29, Mire 19, Grove 0, Elder 0 of 72; wood over 1% = Timber | skilled player | medium | re-pin `RefShare` to 0.76/0.17/0.03/0.04 and `ElderBelowScore` 1.12: all five types at 16v16, outcomes unchanged (S, config; measured) | sim | no |
| 9 | **Walls can be built on the outer half of the scoring disc** | keep-out 3 u on a 6 u hill; keep-out 7 u: Kaiju owner 0.883 to 0.729, bot walls 3.4 to 0.85 | skilled Builder | medium | keep-out 7 u (S, config) | sim | no |
| 10 | **Dead resources** | water banked 7.0, spent 0; coins 648 a team, no sink anywhere; wood ends at 11.9 unspent | skilled player | low | hide the water and coin counters until they have a use; give wood a second sink (S view / M sim) | view | owner (coins tie to decision C/D) |
| 11 | **Quitting records nothing; a host quit ends everyone's match; three match counters disagree** | leave = no loss (`MatchRewards.cs:112`); HostLeft ends all (`LockstepSession.cs:443-464`); profile and history count practice, class career does not (`MetaRecorder.cs:162`) | new player (counts), everyone (host) | medium | leave = loss for the leaver; "host left, no result" screen; show practice separately (S); host migration later (L) | view + meta | no |
| 12 | **One spot on the map; nobody goes anywhere else** | 56% of living time within 15 u of the hill; enemy half 0.3% | skilled player | medium | a second objective or a resource found only in the enemy half (L, map) | sim | owner |
| 13 | **Bots never switch class or use the terrain verbs** | 0 class switches, 0 raises or lowers, 0 purifies in 72 matches | skilled player | medium | switch at respawn to fill a missing Healer/Farmer, once per 60 s (M) | sim | no |
| 14 | **The rules that decide the match are never taught** | trial (14 steps) skips the feed race, fatigue, RALLY, the hill rule and votes; no "why we lost"; the Kaiju type's cause is hidden; the Miner goal text is stale (`MatchWords.cs:85`) | new player | medium | results-screen turning point ("you lost the egg race by N"), type reason on hatch, fix the Miner line (S to M) | view | no |
| 15 | **Side bias at 8 a side** | Quick 8v8 Crimson wins 0.570 (0.52-0.62, n 400); 16v16 Azure 0.639 (n 72, in noise) | skilled player over many matches | low | measure spawn and node distances per team size, then even them (S to M) | sim | no |


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

### Quick Match (240 s, hatch at 120 s, 5 or 8 a side; `Runtime/Flow/PlayModes.cs:90,170`), 200 matches each

| arm | Azure win | blowout | lead changes med | leader at half wins | decided at (med s) | margin (med s) | ends within 15 s | Kaiju owner wins | trailer at hatch got the Kaiju |
|---|---|---|---|---|---|---|---|---|---|
| Quick 5v5 | 0.450 (0.38-0.52) | 0.200 | 2 | 0.595 | 172 | 19 | 0.375 | 0.576 | 157 of 191 |
| Quick 8v8 | **0.415 (0.35-0.48)** | 0.205 | 3 | 0.515 | 155 | 27 | 0.305 | 0.806 | 125 of 196 |
| Quick 5v5, comeback off | 0.435 | 0.265 | 1 | 0.645 | 28 | 36 | | 0.487 | |
| Quick 5v5, RALLY off | 0.440 | **0.460** | 1 | 0.822 | 23 | 48 | | 0.296 | |
| Quick 5v5, owner-approved comeback numbers | 0.485 | 0.255 | 1 | 0.789 | 23 | 31 | | 0.335 | |

- **Quick Match is healthy** on today's rules: blowouts 0.20, median margin 19 s, 37% of 5v5 matches end within 15 s.
  It depends on RALLY far more than 16v16 does (RALLY off: blowouts 0.46, decided at 23 s).
- The 5v5 arm on the 480 s clock (blowouts 0.445, decided at 28 s, n 200) is not a mode anyone plays (Practice is 5 a
  side on a 1,200 s clock, `PlayModes.cs:92`), so it is not a finding. It was the clock the "idle team wins 110 of 200"
  probe used; see section 5.
- Quick 8v8 favours Crimson (0.585, interval clear of 0.5 at n 200), Quick 5v5 leans the same way, and 16v16 leans
  Azure (0.639 at n 72, inside the noise). A side bias that flips with team size points at spawn or node layout per
  size, not at a rule. Worth a 400-seed check before anyone tunes on side shares.
- Kaiju type at Quick: Grove 102, Timber 50, Mire 44, Brute 4 of 200 (5v5): the type picker behaves very differently by
  size (section 4).

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

### "A team with one idle player wins too often" (5v5 on the 480 s clock, 200 matches; actor 0 gets no input at all)

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
ahead (`AdvantageExtra = 2`) or 10 s behind (`GambitExtra = 2`), i.e. 3 of 5 bots away from the hill for most of the time before the hatch.
Those extras are absolute numbers tuned at 16v16 (6 of 16). So at 5v5 "four bots on the hill" beats "five players with
three at the egg". At 16v16 one idle player is 6%, under the threshold, and nothing happens (0.500).
**On the real Quick clock the effect reverses.** Quick 5v5 (240 s, hatch 120 s) with actor 0 idle: the idle team wins
**0.080** (0.05-0.13, n 200), blowouts 0.400, and the enemy gets the Kaiju (owner wins 0.945). With the hatch at 2:00 the
short-handed plan's single feeder loses the egg race, and the Kaiju decides the match. So the 110-of-200 number was a
480 s-clock artefact; no mode a person plays rewards going idle. What is real in both clocks: one idle human swings a
small match hard (to 0.53 one way at 480 s, to 0.08 the other at 240 s), because the bot plan changes shape around him.
**Rule fix (S, sim):** make the two extra feeders a share of the team (`ceil(0.12 x team)`: 2 at 16, 1 at 5) so the plan
does not flip at 5 a side, and hand an idle human's fighter to the autopilot after `InactiveSeconds` (the same call the
online drop uses, `Sim/Bots/MatchTick.cs` `HandToBot`), which is also the missing AFK rule online (section 10). With the
autopilot taking over, an AFK teammate costs his team nothing and gains it nothing.

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

### 5b. Fix candidates (16v16, 72 matches each, round 2; round 3 below)

| arm | Azure win | blowout | lead changes med / mean | leader at half wins | lead at 2:00 / 4:00 wins | decided at | margin | within 30 s at 6:00 | Kaiju owner wins |
|---|---|---|---|---|---|---|---|---|---|
| control (shipped) | 0.639 | 0.125 | 4 / 4.29 | 0.458 | 0.46 / 0.46 | 326 | 40 | 0.583 | 0.883 |
| owner-approved set (x1.25, cap 45, trail 15 s, respawn 3 to 8) | 0.583 | 0.194 | 3 / 3.00 | **0.681** | **0.61 / 0.68** | 128 | 34 | 0.375 | **0.690** |
| RALLY x1.25 only | 0.528 | 0.208 | 3 / 3.58 | 0.542 | 0.51 / 0.54 | 256 | 53 | | 0.857 |
| fatigue floor 50% (was 25%) | 0.625 | 0.181 | 4 / 4.24 | 0.556 | 0.48 / 0.56 | 299 | 64 | 0.500 | 0.850 |
| RALLY off + floor 50% | 0.472 | 0.208 | 3 / 3.29 | 0.639 | 0.64 / 0.65 | 146 | 45 | | 0.678 |
| FORWARD RETURN for the trailing team only | 0.681 | **0.042** | **5 / 4.74** | 0.414 | 0.46 / 0.41 | 360 | 31 | 0.639 | 0.903 |
| RALLY x1.25 + forward for trailing only | 0.556 | **0.069** | 4 / 3.89 | 0.569 | 0.51 / 0.57 | 286 | **28** | 0.514 | **0.750** |
| Kaiju type reference re-pinned | 0.653 | 0.111 | 4 / 4.08 | 0.500 | 0.49 / 0.50 | 296 | 41 | | 0.883 |
| bots do not focus Healers | 0.583 | 0.264 | 5 / 4.78 | 0.639 | 0.51 / 0.64 | 268 | 53 | | 0.817 |

Reading: the owner-approved set is the only one that makes the first half matter and cuts the Kaiju's say (owner wins
0.69), but it lets more matches run away (blowouts 0.194, decided at 128 s) and it is worse at Quick 5v5 (blowouts
0.255, decided at 23 s). "Forward for the trailing team only" is the best single anti-blowout lever measured (0.042,
lead changes 5 for the first time) but leaves the Kaiju in charge. "RALLY x1.25 + forward for trailing only" is the best
balance in round 2 (blowouts 0.069, margin 28 s, Kaiju owner 0.75), still with a flat first half.

### 5c. Fix candidates, round 3 (16v16 72 matches; Quick 200)

"combo" = RALLY x1.25, cap 45 s, respawn 3 to 8 s (the owner-approved numbers) but the trail threshold kept at 5 s, and
FORWARD RETURN for the trailing team only. "next build" = combo + Kaiju type re-pin (`RefShare` 0.76/0.17/0.03/0.04,
`ElderBelowScore` 1.3) + wall keep-out 7 u.

| arm | Azure win | blowout | lead changes med / mean | leader at half wins | lead at 2:00 / 4:00 wins | decided at | margin | ends within 15 s | Kaiju owner wins |
|---|---|---|---|---|---|---|---|---|---|
| control (shipped) | 0.639 | 0.125 | 4 / 4.29 | 0.458 | 0.46 / 0.46 | 326 | 40 | 0.208 | 0.883 |
| **combo** | 0.625 | **0.097** | 3 / 3.60 | **0.634** | **0.59 / 0.63** | 193 | 42 | 0.236 | **0.627** |
| combo + trail threshold 15 s (= the full approved set + forward rule) | 0.514 | 0.194 | 2 / 2.96 | 0.569 | 0.58 / 0.57 | 244 | 28 | 0.319 | 0.759 |
| wall keep-out 7 u alone | 0.625 | 0.125 | 4 / 4.03 | 0.611 | 0.54 / 0.61 | 250 | 42 | 0.208 | 0.729 |
| type re-pin + Elder 1.3 alone | 0.625 | 0.153 | 4 / 4.15 | 0.500 | 0.52 / 0.50 | 300 | 40 | 0.250 | 0.883 |
| **next build** | 0.639 | 0.111 | 4 / 3.86 | 0.620 | 0.61 / 0.62 | 240 | 45 | 0.250 | 0.724 |
| Quick 5v5 combo | 0.440 | 0.245 | 1 / 1.55 | 0.750 | 0.76 | **27** | 24 | 0.355 | 0.401 |
| Quick 5v5 next build | 0.455 | 0.260 | 1 / 1.57 | 0.730 | 0.74 | **28** | 24 | 0.370 | 0.422 |
| Quick 8v8 combo | 0.495 | 0.215 | 2 / 2.62 | 0.492 | 0.49 | 148 | 25 | 0.345 | 0.803 |
| 4 Healers, 3 Warriors (one team, pooled n 144) | changed team wins **0.632** | 0.278 | | | | | | | |

Reading:
- **combo fixes the 16v16 arc**: the first half now matters (leading at 2:00 wins 0.59, leader-at-half 0.634, inside the
  0.60-0.80 target for the first time), the Kaiju decides less (0.883 to 0.627), and blowouts fall to 0.097. Cost: lead
  changes 4 to 3.
- The 15 s trail threshold is the part of the owner's approved set that hurts (blowouts 0.194); the multiplier, cap and
  respawn are the parts that help.
- **Quick 5v5 must keep today's numbers.** combo there drops the median "decided at" from 172 s to 27 s: at 5 a side the
  x1.5 RALLY is what keeps a 4-minute match alive. Comeback numbers per mode (Full Battle and 8v8: combo; Quick 5v5:
  shipped) is a small change in `PlayModes` rules.
- Wall keep-out 7 u: bots build 3.4 to 0.85 walls a match (most bot walls were on the disc) and the Kaiju owner's edge
  falls to 0.729; no other cost. Do it.
- Type re-pin with Elder 1.3 makes ELDER 22 of 72 at 16v16 but Grove 0, and ELDER 81 of 200 at Quick 5v5 (too many).
  Round 4: 1.12 is right (5d).
- Four Healers beat the standard lineup 0.632: the Healer is the strongest body to add (section 2).

### 5d. Round 4: Kaiju type picker and the side check

| arm (`RefShare` 0.76/0.17/0.03/0.04 + `ElderBelowScore`) | types (Brute / Timber / Mire / Grove / Elder) | Azure win | blowout | Kaiju owner wins |
|---|---|---|---|---|
| shipped, 16v16 | 24 / 29 / 19 / 0 / 0 | 0.639 | 0.125 | 0.883 |
| Elder 1.12, 16v16 (72) | 10 / 26 / 20 / 2 / 14 | 0.653 | 0.125 | 0.900 |
| Elder 1.18, 16v16 (72) | 8 / 26 / 20 / 0 / 18 | 0.639 | 0.153 | 0.883 |
| Elder 1.12, Quick 5v5 (200) | 3 / 43 / 24 / 121 / 9 | 0.445 | 0.195 | 0.571 |
| Elder 1.12, Quick 8v8 (200) | 0 / 18 / 50 / 125 / 7 | 0.420 | 0.190 | 0.801 |
| Elder 1.18, Quick 5v5 (200) | 3 / 43 / 24 / 111 / 19 | 0.455 | 0.190 | 0.571 |

- **Elder 1.12 is the value**: all five types appear at 16v16 and no outcome number moves (the type is flavour, not
  balance). Timber still leads at 16v16 and Grove at Quick, because one off-kind trip still swings a small feed; a
  per-size reference is a later refinement.
- **Side check, Quick 8v8 at 400 matches: Azure 0.430 (0.38-0.48).** Crimson wins 0.570 at 8 a side on today's rules.
  It is real, and it is a layout question (spawn or node distances at 8), not a rule.

---

## 2. Classes as roles

Lineup at 16 (`Sim/Config/DepthConfig.cs:391`): 5 Warriors, 4 Farmers, 2 Miners, 2 Demolitions, 1 Builder, 2 Healers.
Actor 0 (the player's seat) starts as a Warrior (`Sim.Tests/Harness/BalanceProbe.cs` options, `HumanClass = Warrior`).

### What each class is for, and what it actually does (control, 72 matches, per fighter)

| class | job by design | kills / deaths | share of fighter damage | hill credit (s) | dead / walking / fighting share | dead or walking back after a respawn | time on the hill disc |
|---|---|---|---|---|---|---|---|
| Warrior | fight, hold | 14.6 / 13.6 | 0.429 | 170 | 0.107 / 0.317 / 0.339 | 0.338 | 0.568 |
| Farmer | gather, feed (x1.25 allegiance) | 12.1 / 8.4 | 0.241 | 86 | 0.069 / 0.306 / 0.268 | 0.206 | 0.245 |
| Miner | mine ore for the hut | 8.8 / 7.9 | 0.080 | 66 | 0.067 / 0.393 / 0.163 | 0.185 | 0.200 |
| Demolitions | TNT and mines | 17.4 / 13.6 | 0.210 | 149 | 0.107 / 0.322 / 0.354 | 0.312 | 0.492 |
| Builder | walls, ladders, bridges | 7.0 / 8.6 | 0.035 | 96 | 0.074 / 0.340 / 0.167 | 0.215 | 0.278 |
| Healer | mend, pulse, purify | 0.1 / **18.0** | 0.005 (4,899 HP healed) | 134 | **0.138 / 0.396** / 0.216 | **0.436** | 0.440 |

(Zones are "time alive", sampled every 0.5 s; full zone table in section 6.)

### Contribution to winning: one team's lineup changed, both sides pooled (n 144 each, +-0.08)

| arm (16 bodies, W F M D B H) | changed team wins | what moved |
|---|---|---|
| no Healers, 2 more Warriors (7 4 2 2 1 0) | **0.083** | hill 128 vs 223 s; leader-at-half 0.889; blowouts 0.458 |
| one Warrior swapped for a Healer (4 4 2 2 1 3) | **0.604** | |
| no Farmers, 4 more Warriors (9 0 2 2 1 2) | **0.188** | Kaiju to the enemy in 144 of 144 (allegiance 289 vs 1,067) |
| one Farmer swapped for a Warrior (6 3 2 2 1 2) | **0.292** | Kaiju to the enemy 0.68 |
| one Warrior swapped for a Farmer (4 5 2 2 1 2) | 0.486 | |
| **no Warriors, 5 more Farmers (0 9 2 2 1 2)** | **0.861** | hill 218 vs 133 s; wins 82 of 102 even without the Kaiju; median "decided at" 11 s |
| no Demolitions, 2 more Warriors (7 4 2 0 1 2) | 0.375 | |
| one Warrior swapped for Demolitions | 0.493 | |
| no Miners, 2 more Warriors (7 4 0 2 1 2) | 0.500 | ore 0, hut level 0, no loss |
| one Warrior swapped for a Miner | 0.535 | |
| no Builder, 1 more Warrior (6 4 2 2 0 2) | 0.576 | |
| one Warrior swapped for a Builder | 0.458 | |
| all 16 Warriors | **0.035** | |
| **all 16 Farmers** (round 2) | **0.847** | blowouts 0.000, decided at 11 s: the fastest body wins the disc and the egg race at once |
| all 16 Demolitions | 0.201 | |

**What this says.** In the team game the Warrior is the weakest body to add and the Healer the strongest. Every arm that
adds Warriors loses (0.035 to 0.375, the Builder and Miner swaps are within noise); the one arm that removes them all wins
0.861 and settles the hill in the first minute. The score counts bodies on the disc, not damage: a Warrior is the slowest
body (3.8 u/s against 4.0-4.2), spends 0.338 of the match dead or walking back, and his damage (0.43 of all fighter damage)
does not convert into hill seconds. Two Healers are worth more than any two other bodies (4,899 HP healed per match, about
30 Warrior lives). This is bot play; a human Warrior who stands on the disc is better than a bot Warrior who chases, but the
direction is large and consistent.

**Interaction with b16.** The design advisor's ruling makes the Warrior win every duel and move "a touch slower", and
the builder is doing exactly that. The duel matrix is not the problem; the team arms show the Warrior is already the worst
team pick, and slowing him moves him further down. The fix belongs to the score, not the duel: see item 2 of the ranked list
(a Warrior needs a job only he can do on the hill).

### Does each kit hold a decision?
- Warrior: when to raise the shield and whom to cleave (yes, moment-to-moment). No decision about where to be: hill only.
- Farmer: which node, which KIND to feed (it sets the Kaiju type, section 4), when to fight. The richest decision set.
- Miner: none to speak of: mine, haul home, repeat (0.39 of his time walking, every phase). Removing both Miners costs
  nothing (0.500). The hut levels he buys are measured in section 3.
- Demolitions: where to throw (193 TNT a match, 11.8% of all deaths are explosions). A real decision.
- Builder: what to build where; bots stand still 8-9% of the first three minutes waiting for wood and never dig or place.
- Healer: whom to mend, when to pulse, when to purify (0 purifies in 72 matches). Real, but she is the bots' first target
  (`Sim/Bots/TeamBrain.cs:279`, `TargetHealersFirst`), dies 18 times a match (once every 27 s) and is dead or walking back
  0.436 of the match.

### Overlap and traps
- Miner, Demolitions and Builder share one stat row (120 HP, 16 damage, 1.5 range; `GameConfig.cs:54-74`); the combat
  audit already lists it (not re-audited).
- **Trap for a new player: the Healer.** She is the most valuable class to the team and the worst experience: 18 deaths,
  almost half the match dead or walking. A first-time Healer will feel useless while being the reason the team wins, and
  nothing on screen tells her so (the results screen ranks by knock-outs and hill time).
- **Trap for a team: Warriors.** The player's default class is the one whose duplicates lose.

### Class switching
- A dead player may pick a class for the next respawn (`Sim/Respawn.cs:22-48`, the HUD's respawn class tiles); alive at
  the base station only when the station rule is on.
- Bots never switch (`Sim/Bots/BotSteer.cs:172`, `ClassPick = -1`; measured 0 switches in 72 matches). With the class arms
  above, a bot team that loses both Healers or all Farmers stays that way all match.
- Recommendation: bots switch at respawn to fill a missing role (no living Healer, no Farmer before the hatch, an enemy
  Kaiju with no KAIJU HUNTERS), at most once per bot per 60 s. Small, sim, deterministic.

---

## 3. Economy

### Every resource (control, per team per match)

| resource | sources | sinks | measured | verdict |
|---|---|---|---|---|
| Plant | nodes, 6 charges, 20 s regrowth | egg (allegiance) | 49.7 delivered | works |
| Meat | 2 per knock-out | egg | 11.3 delivered | works |
| Wood | 10 groves, supply drops (+4) | Builder walls (1 or 2), ladders/bridges, egg | team stock peaks 14.4, **ends 11.9 unspent**; 3.3 walls and 5.3 pieces a match | hoarded: the Builder bot cannot spend it fast enough |
| Water | 5 springs, supply drops (+2) | egg (+Mire), **the station ladder only** (`Sim/Sources.cs:67-74`, called from `Sim/Stations.cs:116`, off under the hut rule) | stock peaks 7.0, **ends 7.0: never spent** | dead resource at the base |
| Food stock | banked food | Farmers withdraw | peak 0.4 | unused path |
| Ore | 6 veins, 6 charges, 12 s regrowth | hut levels 6/10/14/18/24 (72) | 64.8 banked; L5 in 0.368 of team-matches at 7:17 | works, barely |
| Corruption | 3 nodes | egg (berserk at 100 instability) | 3.5 delivered; berserk 12 of 72 | works |
| Coins | 3 per knock-out, piles, drops | **nothing in the match or in the profile** (no code subtracts coins) | 648 picked per team | still no sink (systems audit #20) |

### The hut and its votes
- Levels reached (release report): L1 0:16, L2 1:03, L3 2:02, L4 4:44, L5 7:17 (in 37% of team-matches). Levels at 6:00
  differ in 13 of 72 matches (section 5).
- Vote rules (`Sim/B15/HutChoices.cs:193-271`): 8 s window, closes early when one card has more than half of the team
  (`2a > members`); a tie or no vote = a seeded coin flip (`DefaultCard`, `:184`); a player may change his vote until it
  closes; AFK players count as members and never vote. Bots vote 1 to 4 s after the vote opens, 80% for the "team need"
  card (`BotNeed`, `:296-326`).
- **A human's vote almost never matters with bots.** With 15 bots at 80% agreement the ninth matching vote lands within
  about 3 s and closes the vote before a human has read both cards. Measured: 625 votes, 5 decided by lot (0.8%). At 5v5,
  95 of 1,309 (7%) by lot. In a 16-human room the window is the full 8 s and ties are a coin flip.
- Pick rates (control): L1 QuickReturn 73 / ToughWalls 71; L2 RichVeins 107 / ShieldWall 37; L3 ShortFuse 72 /
  BlastGuard 72; L4 KaijuHunters 76 / WideMend 67; L5 HillAnchor 37 / SwiftFeet 13.
- **Win share per card cannot be read from these matches**: the bots pick by situation (WIDE MEND when they own the
  Kaiju, SWIFT FEET when behind at L5), so WIDE MEND "wins" 49 of 57 and SWIFT FEET 0 of 7 because of when they are
  picked, not what they do. Measuring card strength needs a forced-pick arm (both teams forced to opposite cards), which
  the vote code has no switch for.
- Hut votes off (flat +5% a level): Azure 0.444, blowouts 0.111, margin 28 s, Kaiju owner wins 0.678 (with votes 0.883).
  The L4 pair is a Kaiju amplifier: the owner takes WIDE MEND, the other side's KAIJU HUNTERS does not rescue it.
- Text drift: the Miner's first-match goal says ore makes "your whole team stronger" (`Runtime/Game/MatchWords.cs:85`),
  but with hut votes on the flat +5% is replaced by the cards (`HutChoices.cs:86`).

### Build costs vs value
- Walls cost 1 (short) or 2 (tall) wood (`Sim/Config/BuildConfig.cs:46-47`), live 45 or 60 s, and are refused only inside
  3 u of the hill centre (`BuildConfig.cs:24`) while the hill is 6 u. Walls off: Kaiju owner wins 0.733 (on 0.883),
  leader-at-half 0.597: the bot walls help whoever holds the hill with the Kaiju.
- TNT: 193 throws, 261 explosions, 50 craters and 68 mines triggered a match. The Demolitions is the busiest system.
- Miner barricades: 0 placed (dead system, systems audit #5).

---

## 4. Kaiju

### Feed, hatch, allegiance
- Hatch is a fixed timer, 240 s, in 72 of 72 matches (no variance: there is no "time to hatch" to distribute).
- Allegiance = whoever delivered more (`Sim/Kaiju.Hatch.cs:63-69`); berserk if the corruption lock fired first. Control:
  Azure 38, Crimson 22, berserk 12, neutral 0.
- **The team behind at the hatch gets the Kaiju 37 times in 60**, because trailing bots send two extra feeders
  (`BotDirector.cs:190`), and then wins 32 of those 37. The owner wins 53 of 60 (0.883). Without RALLY the owner wins 0.610,
  without the hill rule 60 of 60.
- Never feed (scripted: one team's egg-duty bots go to the hill, any food in hand vanishes): that team **leads by 101 s at
  the hatch and still wins only 0.271** (n 144); the enemy gets the Kaiju every time. Not an exploit. It also shows the size
  of the swing: a 101 s lead at 4:00 is worth less than the Kaiju.

### Which type hatches (control, 72)
Brute 24, Timber 29, Mire 19, **Grove 0, Elder 0**. The picker divides each kind's share of the feed by a reference share
(`Sim/Config/KaijuTypesConfig.cs:25`, `RefShare = {0.90, 0.10, 0.01, 0.02}`), pinned to an old control mix. The mix now
is 0.759 plant, 0.173 meat, 0.027 wood, 0.041 water, so plant scores 0.84 and can never win, meat always scores about 1.7, and
**any wood over 1% or water over 2% beats it** (31 and 23 of 72 matches). Elder needs every score under 1.02
(`ElderBelowScore`), which a meat share of 0.17 never allows. So the type is decided by one or two off-kind deliveries:
a single Farmer trip of 3 wood out of ~130 units makes a Timber. At 5v5 all five appear (Grove 44, Elder 21 of 200),
because small teams feed small, lumpy mixes. Round 2: `RefShare` re-pinned to today's mix (0.76, 0.17, 0.03, 0.04) gives
Grove 13, Brute 13, Timber 26, Mire 20, Elder 0 of 72, with no change to who owns it or who wins (owner wins 0.883);
rounds 3 and 4 add `ElderBelowScore` (1.12 gives all five types, section 5d).

### Behaviour and death
- Sided and up 149 s of its 240 s; 66 s of that inside the hill ring; the owner's score is zeroed 60 s a match.
- Knocked out at least once in 96% of matches, first at a median 359 s, for 60 s (`GameConfig.cs:359`); 1.22 knock-outs
  a match. It never dies for good.
- With no target, an allied Kaiju walks to within 1.5 u of the hill centre (`Kaiju.Hatch.cs:36-37`): **its own AI parks it
  on the one spot where the hill rule zeroes its team's score**, and its own bots then escort it off
  (`DepthConfig.cs:117-121` `OwnKaijuEscort`). The rule and the AI fight each other 60 s a match.
- Kaiju kills are 4.2% of all deaths (claw 3.0%, slam 1.2%).

### How much the Kaiju decides the match
- With the Kaiju: leader at 4:00 wins 0.46. Without it (never hatches): 0.90, blowouts 0.417, leader-at-half 0.903.
- So the Kaiju is the comeback mechanic, and it is a strong one: the side that gets it wins 0.88.

### Counterplay for the team that lost it
Knock it out (needs ~1,000+ damage; done in 96% of matches but only after 6:00 on the median), take KAIJU HUNTERS at L4
(+25% damage; taken 76 times), hold the hill while it stands there (the hill rule). The team without a sided Kaiju of its
own wins 12 of 34. There is no way to steal or turn it (TURNCOAT is off, `ComebackConfig.cs:55`, advisor ruling).

### Side effects of the hill rule
- Teams do not avoid feeding: never-feed loses (0.271).
- The Kaiju is not "led off" by the enemy; it is pushed off by its own team, because its AI walks back. See the fix in the
  ranked list (move the allied Kaiju's idle point to the hill rim on the enemy side).

---

## 6. Objectives and map

### Where time is spent (control, share of time alive, 0.5 s samples)

| class | hill disc | hill ring (6-15 u) | own base | bridge cells | flank | own mid | enemy mid | enemy base |
|---|---|---|---|---|---|---|---|---|
| Warrior | 0.568 | 0.145 | 0.140 | 0.121 | 0.005 | 0.020 | 0 | 0 |
| Farmer | 0.245 | 0.186 | 0.116 | 0.173 | **0.199** | 0.067 | 0.008 | 0.005 |
| Miner | 0.200 | 0.148 | **0.304** | 0.173 | 0.052 | 0.123 | 0 | 0 |
| Demolitions | 0.492 | 0.189 | 0.153 | 0.127 | 0.015 | 0.023 | 0 | 0 |
| Builder | 0.278 | 0.127 | **0.271** | 0.155 | 0.138 | 0.032 | 0 | 0 |
| Healer | 0.440 | 0.199 | 0.189 | 0.147 | 0.001 | 0.025 | 0.001 | 0 |
| all | 0.395 | 0.167 | 0.171 | 0.147 | 0.070 | 0.047 | 0.002 | 0.001 |

- **The map is one point.** 56% of all living time is within 15 u of the hill centre; the enemy half outside the
  ring is visited 0.3% of the time. The flanks are Farmer routes to nodes. There is no reason to go to the enemy side:
  no objective, no resource only found there, no steal.
- Activity by phase is flat (walk 0.35 / 0.34 / 0.34 / 0.32 and fight 0.27 / 0.26 / 0.28 / 0.29 for 0-2, 2-4, 4-6,
  6-8 min): the match has no pace change except the hatch. Walking is the biggest single activity in every phase.

### Spawn safety and the walk back
- Deaths within 12 u of the victim's own pad: 0.1%. Deaths within 4 s of a respawn: 0.0%. Spawn protection 3 s and
  stand-up 0.8 s (`GameConfig.cs:232-236`) plus FORWARD RETURN mean **no spawn camping**. Keep.
- Respawn to first hit given or taken, median seconds: Warrior 11.8, Farmer 12.7, Miner 14.9, Demolitions 10.8,
  Builder 11.3, Healer 12.0. Add the 2-5 s respawn timer: a knocked-out player is out of the fight about 15 s, half of it
  walking. FORWARD RETURN (40% of the way) is what keeps that number at 12 s instead of about 18 s (systems audit).

### Terrain use
- Bots: 0 ground raised or lowered (terrain-height arm identical to control), 50 holes a match, all of them TNT craters;
  3.3 walls and 5.3 ladders/bridges. The dig/place/climb verbs the owner approved for bots (2026-11-18) are not in
  `BotBrain` (`BotBrain` clears the Terraform input). A human Miner who digs is doing something no bot does.
- Walls are refused within 3 u of the hill centre (`Sim/Config/BuildConfig.cs:24` `HillKeepout = 3.0`), but the hill is
  6 u (`GameConfig.cs` Hill radius). **A Builder can wall the outer half of the scoring disc.** Bots do not do it on
  purpose; walls off moves Kaiju-owner wins 0.883 to 0.733, so the walls bots do build already help the holder.

---

## 7. Bots as teammates and opponents

- **Pings:** bots ping 102 times a match. A human's ping is an order: up to 3 bots within 40 u answer ATTACK or DEFEND,
  2 answer HELP, and they obey at once (`Sim/PingRules.cs:83,100`, `Sim/Bots/BotFsm.cs:833-849`, `DepthConfig.cs:177-191`).
  Not measured here (the probe sends no human pings). This is a strength for a human leader; keep it.
- **Roles they fill:** hill quota `ceil(0.3 x team)`, egg duty `round(0.25 x team)` + 2 when ahead or 10 s behind
  (`BotDirector.cs:11,183-190`). At 16 that is 6 feeders; at 5 it is 3 of 5 (why one idle human flips a small match, section 5).
- **Healer focus** (measured in round 2, arm off: Healer deaths 18.0 to 13.6 a match, the others +2 to +6%; Azure 0.583, blowouts 0.125 to 0.264, lead changes 4 to 5): `TargetHealersFirst` (`Sim/Bots/TeamBrain.cs:279`) scores a Healer as if she stood at 1% of her
  distance, so every bot in range targets her first: 18 deaths a match against 7.9-13.6 for the others. Round 2 measures
  the arm with it off. The design advisor asked for "target healers / lowest health" (2026-11-19/20), so the rule is on
  purpose; the size of it is the question.
- **What bots never do:** switch class (0), dig or place ground (0), go to the enemy half (0.3%), build a wall where it
  matters, cleanse corruption on purpose (0.14 a match), purify (0).
- **Difficulty** (`Runtime/Menus/MatchSetup.cs:136-160`, `Sim/Bots/BotSkill.cs:25-33`): Easy, Normal, Hard apply to
  **enemy bots only**. The player's own bots are always Normal (`AllyBotDifficulty` is never set by any menu; its enum
  default is Normal, `Sim/Core/SimEnums.cs:22`). Easy = think 1.5x slower, 1.5 u aim error, 40% swing hesitation, half
  the abilities; Hard = twice as fast, no aim error, TNT leads 60%. Not measured here (the balance tool has no switch
  for per-team difficulty; would need a probe arm).
- **Bots as opponents:** they never change plan when the human is the best player on the field (no "focus the human",
  no "push the weakest lane"). They are predictable: everybody on the disc.

---

## 8. Feedback and learning (rules side)

What the HUD tells the player (`Runtime/Hud/Mechanics/B15RulesHud.cs:287-330`, `Runtime/Hud/DeathRecap.cs`,
`Runtime/Game/MatchWords.cs:81-88`):

| rule | is it shown? | gap |
|---|---|---|
| HOLD FATIGUE | "tiring" line while the rate falls | the cost (90 s a match) is never shown; a player sees his lead stall without the cause in the result |
| RALLY | RALLY badge when trailing | fine |
| FORWARD RETURN | forward rally point on the map | fine |
| KAIJU HOLDS NOTHING | NO SCORE pill | fine; the owner team does not learn "lead the Kaiju off", it learns "the score stopped" |
| Hut vote | vote card for 8 s | bots close it in about 3 s; the human sees a result, not a choice |
| Hut result | toast | fine |
| Death | "Knocked out by X" + a tip | fine |
| Kaiju type | name on hatch | the reason (which food decided it) is never shown; players cannot learn to steer it |
| Kaiju owner | colour | fine |
| Why we lost | results screen | no "the Kaiju decided it" or "you lost the hatch race by 40 food"; no turning point |
| First-match goals | one line per class | the Miner's line is stale (section 3) |
| Trial | 14 steps (`Runtime/Onboarding/TrialSteps.cs`) | does not teach hut votes, fatigue, RALLY, the hill rule or the Kaiju feeding race |

What a new player can infer from the screen in his first match: stand on the circle, hit people, the big monster is
important. What he cannot: that feeding decides the Kaiju, that the Kaiju decides the match, and that a Warrior is the
worst body to stack. The biggest learning gap is the one with the biggest effect on winning.

---

## 9. Exploits (top three tested where a script could do it)

| # | exploit | how tested | result | who could use it | fix |
|---|---|---|---|---|---|
| 1 | **Go idle on a small team** | scripted: actor 0 sends no input, 200 matches at 5v5 on both clocks | 480 s clock: idle team wins 0.530 (0.615 with comeback off). Real Quick clock: idle team wins **0.080**. Not an exploit; an AFK teammate ruins a Quick Match, and online nothing replaces him | nobody gains; 9 teammates lose | team-share extras + autopilot after 15 s idle (S, sim) |
| 2 | **Quit to hide a loss** | code | a match left before the end records nothing: no loss, no history row (`Runtime/Progression/MatchRewards.cs:112,160`, `MetaRecorder.cs:84`); a host who quits ends the match for all 32 (`Net/LockstepSession.cs:443-464`), so a losing host voids everyone's result | any player now; matters the day ranked or a public win count exists | record a leave as a loss for the leaver; a host-left match as "no result" for the rest (S, view/meta) |
| 3 | **Wall the outer half of the hill** | code (bots never aim walls; not scriptable in the probe) | walls are refused only within 3 u of the centre (`Sim/Config/BuildConfig.cs:24`) on a 6 u disc; bot walls alone move Kaiju-owner wins 0.733 to 0.883 | a skilled Builder | keep-out = hill radius + 1 u (S, sim) |
| 4 | Pick the Kaiju type with one delivery | measured | any wood over 1% of the feed makes a TIMBER (31 of 72); one Farmer trip of 3 wood decides it | a skilled Farmer | re-pin `RefShare` (round 2) |
| 5 | Never feed, play only the hill | scripted, 144 matches | loses: 0.271 even with a 101 s lead at the hatch | nobody | none needed |
| 6 | **Stack Farmers** | scripted, 144 matches each | **16 Farmers beat the standard lineup 0.847** (blowouts 0, decided at 11 s); no Warriors (9 Farmers) 0.861; all-Warrior 0.035, all-Demolitions 0.201 | a premade team of humans, or anyone who reads this table | the Warrior job plus a class cap per team (ranked item 2) |

---

## 10. Online and meta rules

- **Drop and rejoin:** a dropped player's fighter goes to the autopilot (`Sim/Bots/MatchTick.cs`, `HandToBot`) and comes
  back on rejoin (`BotToHuman`); the cross-play smoke passes with a drop and rejoin (integrator RESUME, runs 44857984926
  and 44887058125). Works.
- **Late join:** refused, "The match already started." (`Net/Room.cs:132`). A match that loses a player stays short a
  human for good (the autopilot fills the seat). Acceptable for now.
- **Host leaving:** ends the match for everyone (`Net/LockstepSession.cs:443-464`, HostLeft). In a 16-human room the
  host's crash or quit cancels 31 people's match and records nothing. Host migration is L; a "host left, result void"
  screen is S.
- **AFK:** nothing online. The sim already measures it (the bot director marks a human inactive after 15 s,
  `BotDirector.cs:11`) but only to plan bot roles. See exploit 1.
- **Profile vs mastery count mismatch, explained:** three counters with three rules. The profile's matches played and
  W/D/L count every completed match, practice included (`MatchRewards.cs:160-168`). The class career counts completed
  non-practice matches only (`MetaRecorder.cs:162`). The match history records every completed match, practice included
  (`MetaRecorder.cs:84-100`). So "profile matches" = "history rows" = sum of class careers + practice matches. Fix: show
  practice separately on the profile, or count it everywhere (S, view).
- **Pay-to-win:** none found. Mastery gives cosmetics and titles only (`Runtime/Progression/Mastery.cs`), capped against
  farming; an idle player earns no finish or win XP (no contribution), though standing on the hill counts as one.
  Coins are picked up in the match (648 per team) and spent on nothing anywhere. Matches the owner's decision C.

---

## 11. What is missing (ranked)

| rank | missing | one-line case | size |
|---|---|---|---|
| 1 | A reason to care about the first four minutes | leading at 2:00 or 4:00 wins 0.46: the early hill fight is wasted effort | M |
| 2 | A Warrior job that turns damage into score | the default class is the worst team pick (all-Warrior 0.035; one more Warrior 0.292) | M |
| 3 | An AFK rule (online and in the sim plan) | an idle human drops his Quick team to 0.08; nothing takes his seat | S |
| 4 | "Why we lost" on the results screen | the Kaiju hatch race decides 0.88 of matches and is never named | S |
| 5 | Bots that switch class and use the verbs | 0 class switches, 0 digs or raises, 0 purifies; a bot team without a Healer stays without | M |
| 6 | A reason to leave the centre | 0.3% of time in the enemy half; flanks are Farmer paths | L (map work) |
| 7 | A coin and water sink | 648 coins and 7 water per team, spent on nothing | S to M |
| 8 | A tutorial for the scoring rules | trial does not teach fatigue, rally, the hill rule, the feed race or votes | M |
| 9 | Host migration | one crash ends 32 people's match | L |
| 10 | Overtime or a mercy rule | 21% of 16v16 matches end within 15 s (good), 12.5% are blowouts with nothing to play for | S |
| 11 | Per-team bot difficulty and a ladder | allies are always Normal; there is no way to make a 1-human team feel carried or tested | S |


---

## Do in the next build (no decision needed)

1. Allied Kaiju idle point on the hill rim, outside the ring (item 3). S, sim.
2. AFK: hand an idle human to the autopilot after 15 s; feeder extras as a team share (item 6). S, sim.
3. Hut vote: bots wait for the humans (all voted, or 6 s) (item 7). S, sim.
4. Wall keep-out 7 u from the hill centre (item 9). S, config; measured.
5. Kaiju type re-pin: `RefShare` 0.76/0.17/0.03/0.04, `ElderBelowScore` 1.12 (item 8). S, config; measured.
6. Small view fixes: Miner goal text, "host left, no result" screen, practice shown apart on the profile, leave = loss
   for the leaver, hide the water and coin counters (items 10, 11, 14). S, view.

Each sim item changes the golden and the rules digest; the builder re-pins and runs the 72-seed compare as usual.
**Interaction with `feat/b16-duel-rules-cancel`:** a slower Warrior raises his walk-back share (already 0.338) and moves
him further down the team-pick order (item 2); land b16 first, then run the class-mix arms again before item 2's fix.

## Needs a decision (with recommendations)

1. **Comeback numbers (owner).** Recommendation: take "combo" for Full Battle and Quick 8v8 (x1.25, cap 45 s, respawn
   3 to 8 s, forward return for the trailing team only, trail threshold kept at 5 s, not 15 s) and keep today's numbers
   for Quick 5v5, where the stronger RALLY is what keeps a 4-minute match alive (combo there: decided at 27 s).
2. **The Warrior's team job (design advisor).** He wins every duel by ruling, but the team that drops him wins 0.861.
   Recommendation: a Warrior on the disc counts as two bodies for ownership (his fighting becomes score), and a cap of
   6 of one class per 16 (2 per 5); measure both after b16 lands.
3. **Healer focus strength (design advisor).** Recommendation: keep "bots target Healers" but as a tie-breaker (weight
   0.5 instead of 0.01), which keeps the ruling's intent and stops 18 deaths a match.
4. **Coins (owner).** Recommendation: hide the in-match coin counter until a cosmetic shop exists (decision C/D), rather
   than invent a power sink.
5. **A reason to leave the centre (owner).** Recommendation: not in the next build; put a second objective on the map
   roadmap item ("map data out of rules, then a second map") already approved.

## Works well, do not break

1. **Spawn safety:** 0.1% of deaths within 12 u of the victim's pad, 0.0% within 4 s of a respawn.
2. **Close finishes and no draws:** 0 draws; 21% of 16v16 and 37% of Quick 5v5 matches end within 15 s; Quick Match
   blowouts 0.20, held up by RALLY.
3. **Drop, rejoin and human orders:** a dropped player's fighter is played by the autopilot and handed back on rejoin
   (cross-play smoke passes); a human's ping is obeyed at once by up to 3 bots.

---

*Probe runs (GitHub Actions, balance-report on probe branches, never merged): round 1 44923985604
(`ci/balance/audit-mech-r1`), round 2 44925053690 (`-r2`), round 3 44925584287 (`-r3`), round 4 44926040627 (`-r4`).
Analysis scripts were scratch only; every number above is from those runs' `audit.jsonl` files.*
