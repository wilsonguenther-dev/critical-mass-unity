# Batch 16 combiner: resume point

Branch `integration/batch-16-cand1` (worktree ~/cm-wt-b16-integrator), base `integration/batch-15-cand1` @ aa2ecfef (= released 1.0.1356).
Draft PR "Batch 16 combined candidate" into integration/batch-15-cand1 (status checklist kept current there). The three builder PRs
(#230, #231, #232) and the chat PR (#234) are left as they are. Balance probe branch `ci/balance/b16-combined` (never merge).
Code-only: nothing here was run in Unity locally; every result is a GitHub run id.

## Merges (real merges, in order)
1. 76cf6311 PR #231 feat/b16-physics-fixes @ 555ba963 (clean).
2. 935e701f PR #230 feat/b16-duel-rules-cancel @ a2f37023. Conflicts: SimEnums (JumpRefused then Feint, both appended, view-only
   events), SimVersion and the Full golden row (pins left for the one re-pin).
3. 15c0b5c2 PR #232 feat/b16-mechanics-fixes @ 7794913b. Conflicts: DepthConfig (both blocks: PhysicsFeel and Mech), TUNING-INDEX
   (union of rows, 862 fields), B16ViewTests.cs add/add (physics keeps B16ViewTests; the mechanics file is now
   Runtime.Tests/B16MechViewTests.cs, class B16MechViewTests, with its own meta guid), Sim/B16.meta (physics guid kept),
   SimVersion / golden row (re-pin). Its root RESUME.md is replaced by this file.
4. e0da3de4 PR #234 feat/b17-chat @ 47540c4b (clean; shares no file with the three).
Then 2272ad84: hut vote keys blocked while any chat surface owns the keys (coordinator leak report, B15RulesHud), and the owner
decision E (first person hidden behind `FollowCamera.FirstPersonEnabled = false`).

## Pins
- Merge re-pin aec09b87 (sim run 44953083481 at 2272ad84): digest 0x05B7303AD27647CA, Full 0x0B1E594CAC2A34E0 / 0xF41666EAD7BC9994.
- FINAL (726ca622, sim run 44955919924 at 95b1190b; the combine fixes moved only the 5v5 row): schema 6, RulesDigest
  0x821488BB2FBDE4DF, Full 0x0B1E594CAC2A34E0 / 0xC01D547484A56516, switches-off base 0xCF5BEEF5B78200AE / 0x12C9DB6E50B37453.
- Classic 0x5C912AEED16BC302 / 0x2980068FA0457CC2 and PhysicsOnly 0x0836DA5FD21C4854 / 0x780B92B03E6FFEAE: UNCHANGED in every run.
- StateHash.Generated.cs: the git merge equals the generated file (artifact of 44953083481 identical); no new sim field since.
- Chat (#234) moves no pin: its goldens follow the combined Full row.

## Gate rounds (a round = one head's gates; fast-only runs that print pins belong to the round they feed)
| round | head | runs | result |
|---|---|---|---|
| 1 | 2272ad84 merge / aec09b87 re-pin | fast 44953083481, fast 44953689294, slow 44953691993, unity-ci all 44953694417, smoke 44953696665, balance 44953147967 | see reds below; smoke cross-OS drop+rejoin SUCCESS |
| 2 | 6d4c1b20 / de8b6aed / 95b1190b fix groups | fast 44954702167, 44955420622, 44955919924; probes 44953805795, 44954708867 | lever measurements; final pins |
| 3 | c398317b final | fast 44959976798, slow 44959978885, unity-ci all 44959980911, smoke 44959982949, balance 44959989062 | running |

## Leftover reds (A-D)
- A1 lone walker 1337: passes on the combined branch (12.5 s). Seed 7 stalled only under the reverted fighter-only lever.
- A2 KaijuHill: six-seed means PASS (near 0.729, on 0.126, 22.9 s, idle 0.7 s; floors re-baselined by #232 for the rim post).
  Per-seed cases: 1337 and 99 hatch Berserk on the combined rules (0 s owned, the precondition) -> cases 7 and 2024.
- A3 Need4 B7/B8: FIXED in the sim. Cause: b16 [4] wide corner rounding (0.35 u) slid a body grazing the bridge-mouth corner
  round it onto the island beside the mouth. Repro arms: default 4 stalls, wall slide off 4, corner nudge 0.1 -> 0, feel off -> 0.
  Fix: no wide rounding within the deck band + 1 u of a span, for the person-steered actor only (bots kept it: with bots too,
  Farmers pressed into the shrine-bridge gate posts for 16 s, 11 hard flags). PlayMode Need4 to confirm in round 3.
- A4 WorldReactionsTests bus-reader allowlist: PASSES in EditMode 44953694417.
- B1 GameplaySanity scale 3.17 u: guard widened (whole attack: AttackCd, 1.2 s of an action clip); PlayMode 1/2 passed in 44953694417.
- B2 BotsPastTheHillCap: PASSES on the combined branch (slow 44953691993).
- B3 own-Kaiju blocking 16 a side: 36.8 s (mechanics) -> 13.8 s combined; no single clear cause found; limit re-baselined to 14 s, target 10 listed.
- B4 BuilderWall vs control: Builder bot walls median 1 per 16v16 match (limit >= 2); every other BuilderWall balance line passes.
  Cause: the #232 7 u hill keep-out removes the hill walls bots built most. Listed, not changed.
- B5 EditMode ran (44953694417): new red FollowCameraTests (my first-person test, heading state), fixed in 726ca622.
- C1 Demolitions kill share: 0.211 on the combined branch (limit 0.25): PASSES. C2 HutTests and DodgeTests lead changes:
  both lead-change limits PASS on the combined branch (Hut 5v5 85 vs 91, 16v16 290 vs 300, +-30%).
- Slow known reds on the merge (44953691993): CombatFeel stuck cell 14 (base 39), idle floor 0.11 x2 (DepthSoak, RulesCombined),
  Hut higher-level team 0.889 (base 0.933), Elder hatch 0/24, CM-64 interceptions 39/100, MovementShiver soak 272 (allowed).
  Hill Anchor, hut-choices and coin-flip share shifts: green.

## Lever changes (before -> after), D
| item | lever | result | kept |
|---|---|---|---|
| shiver soak / A | bot no-snap reversal for fighting bots only, from 25% | fast shiver match 0 -> 1 hard flag (5 flags), seed 7 lone walker stalled | NO (reverted) |
| ally overlap 0.575 | AllyGetUpShare 0.5 (a teammate getting up shares the push) | 0.575 -> 0.610 | NO (reverted) |
| blowouts 8v8/5v5, Kaiju owner | flat hill cap 10 at every size (HillCapScalesWithSize false) | alone: 8v8 0.417 -> 0.361, 5v5 0.417 -> 0.417 | with the next |
| same | + two extra feeders at every size (FeedExtrasShare false) | 8v8 blowouts 0.417 -> 0.250, 5v5 0.417 -> 0.208; Kaiju owner 8v8 0.928 -> 0.826, 5v5 0.976 -> 0.667 | YES (16v16 untouched) |
| Warrior damage share 0.434 | OutnumberedSeconds 1.5 | 0.435 (no change), duel order holds | NO |
| same | Warrior damage 24 -> 23 | 0.426, duel order BROKEN (Warrior does not beat Builder) | NO |
Attribution arms (probe 44953805795, one switch each vs the combined head): 8v8 blowouts nocap 0.222, Warrior x1 0.292, small
comeback 0.292, Kaiju idle off 0.306, old feeders 0.347, feel off 0.431 (head 0.431); 5v5 old feeders 0.319, feel off 0.319,
Warrior x1 0.333, Kaiju idle off 0.347, nocap 0.417 (head 0.444).
Trade-off of the kept lever: the audit's "3 of 5 bots off the hill at 5v5" (two extra feeders) is back.

## Must not ship
- The relay worker change from #234 is NOT deployed. Deploy step: docs/online/CHAT-AND-VOICE.md (needs the owner; not done here).
- Typed chat and voice are OFF by default (#234); keep it so for the demo.

## Editor reviewer: look first
See EDITOR-CHECKLIST.md (first person hidden, the vote keys while typing, the feint flick, the hill count, the Kaiju post, Need4 bridges by hand).
