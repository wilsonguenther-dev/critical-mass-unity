# b16 mechanics fixes: resume note

Branch `feat/b16-mechanics-fixes`, draft PR #232 into `integration/batch-15-cand1` (base aa2ecfef).

## State (2026-11-21, after CI round 3 start)
- All items 1-9 written (sim: Sim/B16/MechRules.cs, Sim/Config/MechRulesConfig.cs, Sim/Kaiju.Idle.cs, Sim/Bots/TeamBrain.HillCap.cs,
  hooks marked `// b16-hook`; view: Runtime/Hud/Mechanics/B16MechHud.cs, GameHud CapWord, AffordanceCore HillKeepoutWord,
  WallView keep-out ring, MatchCounting in Progression/MatchRewards.cs, OnlineMatchOverlay HostLeftTitle).
- Tests: Sim.Tests/B16MechanicsTests.cs, Runtime.Tests/B16ViewTests.cs. TARGETS.json per size (bySize), loader in BalanceStats.cs.
- CI rounds used: 3 of 6 (r1 compile error, r2 compiles: Classic/PhysicsOnly unchanged, 10 reds, r3 = tick cost diagnosis).
- Open: SimTickCostTests trimmed 0.546 > 0.52 (median 61.5 us vs ~34.5 us on sibling branches) -> r3 prints per-switch cost
  (`[b16 tick cost]`). Fix, then re-pin ONCE: SimVersion schema 6 + digest, SimPhysicsSoakTests Full goldens,
  B15RulesTests / CombatFeelTests "switches off" hash (0xC0D8429C9458B5F1 at r2), StateHash.Generated from artifact.
- r3 result: the cost is the intercept behaviour (cap off 53 us, bots stay 55 us, all on 80 us). r4 (ade18a5d) spreads
  intercepts on 3 stand-off tiers; r4 runs: sim all 44930367452, unity-ci tests 44930370904, probe balance 44930392810
  (branch ci/balance/b16-mech-r1, worktree ~/cm-wt-b16-probe). r5 = re-pin once + sim fast + unity-ci all; r6 = online-smoke
  cross_os + drop_rejoin.
- Then: remove the diagnosis test (done), probe branch `ci/balance/b16-mech-r1` (files staged in the session scratchpad: AuditProbe.cs with
  empty-frame idle + offHillHits + b16 counters by reflection, b16-arms.sh, balance-report.yml; also needs audit-mech-r1's
  Program.cs/csproj probe hooks), slow suite, unity-ci, one cross-play smoke with drop+rejoin, docs/balance reports.

## Final state (2026-11-21, all 6 CI rounds used, work stopped)
- Head 5381804e (279ab89f code + docs/balance compares). PR #232 body has the final numbers.
- r6: sim 44944695244 (823 tests, 10 reds), unity-ci 44944697639, online-smoke 44944700113 green (drop 604 / rejoin 767), probe 44944706183.
- Open for the next round: own-Kaiju blocking 36.8 s at 16 a side (target 10); BotsPastTheHillCap 0.147 vs 0.118;
  BuilderWall control, Dodge 0.925; Kaiju owner win 5v5 0.603 -> 1.000 and 8v8 0.845 -> 0.958; blowouts up at 8v8/5v5;
  GameplaySanity scale 3.17 u (swing-pose bounds; widen the swing guard or exempt the post-swing clip tail).
- Probe worktree ~/cm-wt-b16-probe (branch ci/balance/b16-mech-r1) is a never-merge tool branch.
