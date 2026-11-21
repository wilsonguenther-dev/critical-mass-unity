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
- Then: remove the diagnosis test, probe branch `ci/balance/b16-mech-r1` (files staged in the session scratchpad: AuditProbe.cs with
  empty-frame idle + offHillHits + b16 counters by reflection, b16-arms.sh, balance-report.yml; also needs audit-mech-r1's
  Program.cs/csproj probe hooks), slow suite, unity-ci, one cross-play smoke with drop+rejoin, docs/balance reports.
