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

## Pins (re-pinned once, aec09b87, from sim run 44953083481 at 2272ad84)
- Schema 6 (one number for the combined rules; released 1.0.1356 is schema 5), RulesDigest 0x05B7303AD27647CA.
- Full golden 0x0B1E594CAC2A34E0 / 0xF41666EAD7BC9994 (SimPhysicsSoakTests, ChatB17Tests).
- Classic 0x5C912AEED16BC302 / 0x2980068FA0457CC2 and PhysicsOnly 0x0836DA5FD21C4854 / 0x780B92B03E6FFEAE: UNCHANGED (printed by
  the same run). Switches-off base 0xCF5BEEF5B78200AE (the mechanics pin) unchanged.
- StateHash.Generated.cs: the git merge of the three branches equals the generated file (run 44953083481 artifact identical).
- Chat: its goldens equalled the base pins on its own; after the re-pin it follows the combined Full row (no extra move).

## Gate rounds
| round | head | runs | result |
|---|---|---|---|
| 1 | 2272ad84 | sim fast 44953083481 | 5 reds: 3 pin reds (expected before the re-pin), MovementShiver KaijuHill(1337) precondition (the Kaiju hatches Berserk, owned 0 s), PhysicsFeel Items3678 ally overlap 0.575 (D) |
| 1b | 0b00e1ec (probe = 2272ad84 sim) | balance 44953147967 | 72-seed compares at 16/8/5 vs aa2ecfef, see Balance |
| 2 | aec09b87 | fast 44953689294, slow 44953691993, unity-ci all 44953694417, online-smoke cross-OS drop 900:3000 44953696665 | running |
| 2b | probe r2 | balance arms 44953805795 | attribution arms at 8v8 / 5v5 (cap off, Warrior x1, Kaiju idle off, old feed extras, 8v8 small comeback, physics feel off) |

## Leftover reds (A-D): status
- A1 lone walker seed 1337: PASSES on the combined branch (gathered and delivered in 12.5 s, run 44953083481); no change made.
- A2 KaijuHill on-share: slow, pending round 2 (mechanics re-baselined the six-seed floors for the rim post).
  New on the combined branch (fast): KaijuHill(1337) per-seed precondition, the Kaiju hatches Berserk, owned 0 s.
- A3 Need4 bridges B7/B8 at 60 deg assist off: Sim repro added (BridgeCrossingSweepTests.TheAcceptanceCrossingAt60Degrees...), result pending.
- A4 WorldReactionsTests bus-reader allowlist: EditMode, pending round 2.
- B1 GameplaySanity scale 3.17 u: swing guard widened to the whole attack (AttackCd > 0 or an action clip under 1.2 s); idle and
  walking fighters still read. PlayMode pending round 2.
- B2 BotsPastTheHillCap, B3 own-Kaiju blocking, B4 BuilderWall vs control: slow, pending round 2.
- B5 EditMode: running in round 2.
- C Demolitions kill share, lead-change bands: pending round 2.
- D balance: see below.

## Balance, combined (2272ad84) vs aa2ecfef, 72 seeds (run 44953147967)
| stat | 16v16 | 8v8 | 5v5 |
|---|---|---|---|
| Crimson win | 0.361 -> 0.417 | 0.611 -> 0.444 | 0.569 -> 0.569 |
| blowouts | 0.125 -> 0.181 (noise) | 0.236 -> 0.431 (WORSE) | 0.236 -> 0.444 (WORSE) |
| lead changes median | 4 -> 3 | 3 -> 3 | 2 -> 3 |
| half-time leader wins | 0.458 -> 0.542 | 0.493 -> 0.444 | 0.606 -> 0.536 |
| Kaiju owner wins | 0.883 -> 0.807 (n 57) | 0.845 -> 0.930 | 0.603 -> 1.000 (n 38 of 72 hatch with an owner) |
| stuck median (mean) | 4 -> 3 (7.89 -> 4.10) | 1 -> 0 | 0 -> 0 |
| Warrior damage share | 0.429 -> 0.440 | 0.559 -> 0.599 | 0.623 -> 0.647 |
| Healer deaths | 18.0 -> 15.2 | 8.08 -> 7.47 | 6.57 -> 6.79 |
| duel order violations | 6 -> 0 | | |

## Lever changes (before / after)
(none yet)

## Editor reviewer: look first
See EDITOR-CHECKLIST.md.
