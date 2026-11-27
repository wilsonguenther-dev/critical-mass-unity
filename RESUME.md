# b15 wave two integrator: resume point

Branch integration/batch-15-cand1, head 2ad99208 (sim head 73249b0f; after it: docs/balance reports 4e5d688f, test-only slow scoping 2ad99208).
Schema 4, rules digest 0xC3FD3B160874096B, Full golden 0x5259C4A7686D44C6 / 0x3EB7BE7F99175B3B (Classic, PhysicsOnly unchanged).

Done:
- merges #215, #227, #219; wiring d1654e54
- RallySeconds 12 -> 6 (7fbcbddc); Jump / Terraform / Barricades warms (no first-use allocation)
- re-pin f94f17ad + 73249b0f
- balance compare 44857648994, committed under docs/balance/*-b15-wave2-73249b0f-*
- fast sim 44857640742: only the allowed MovementShiver KaijuOut is red
- cross-play online-smoke 44857984926 with drop and rejoin 900:3000: MATCH
- slow sim 44857709950: 9 reds (see the final report)

Pending:
- slow rerun on 2ad99208: check that the physics soak and HutTests lead changes turn green
- unity-ci all 44857982412 on 73249b0f: both players and the Windows smoke pass, PlayMode shards 1 and 2 pass, shard 3 has only the allowed FighterVisibility and OwnerWalkHud reds
- EditMode had 3 new reds, fixed in d9a464d3 (class card HP, two timeouts); the EditMode rerun on d9a464d3 is running
- final report to the coordinator

Update:
- head is now 61e3f9f5 (test-only changes after 73249b0f: physics soak staging, HutTests revert, class card row, timeouts, BotBrainBlowout moved to the slow suite)
- slow 44860611840 on 2ad99208: 8 reds; the physics soak is now green
- EditMode 44862751989 on d9a464d3: class card green; IslandGeometry and TerrainEditMesh timing flakes (both passed on 44857982412); CombatFeel TickCost and BotBrainBlowout ran past their timeouts on a slow runner
- running now: an EditMode rerun and a slow rerun on 61e3f9f5

Final reads:
- slow 44871935552 on 61e3f9f5: 8 reds
- EditMode 44872173510 on 61e3f9f5: 4 reds (mesh timing x2, CombatFeel TickCost timeout, MovementShiver KaijuOut)
- the final report has been sent; nothing is left running

## b15 fix round (stamped 2026-11-21T07:39Z)
Head d7ee90de (sim code a7433909). Schema 5, rules digest 0x597B21BFA247E913, Full golden 0x48A817A4B2B3F068 / 0x1978E881F4005F28; Classic / PhysicsOnly unchanged.
Fixes: BotMine route steering to the next flow cell centre + body-square StraightWalk (stuck median 12 -> 4 at 72 seeds); Kaiju squeeze ignores Builder ladder cells (seed 38; scan 0 of 40).
Test-only: CombatFeel TickCost arms to slow, mesh timing median-of-batches, KaijuType / TerrainHeight 144-seed shares, Hut lead changes re-baselined, sim-tests timeout 90.
Listed not fixed: idle 5v5 floor (idle team 110 vs 91 with actor 0 on autopilot, probe 44880549876: comeback hold fatigue and contested time favour the smaller team); Miner duel (3 levers, none clean); near-misses (2 arms, none clean); CombatFeelBalance hot cell 39 at hill-rim TNT craters / rim walls, Fight and Unstick steering (probe 44881041524); item 8 not started.
Final gates running: fast 44881184125, slow 44881186102, unity-ci all 44881188229, cross-play smoke 44881190050. Balance compare 44880556552 committed under docs/balance/COMPARE-61e3f9f5-vs-b15-fixround-a7433909-72.*
Probe branches: ci/balance/b15-fix-probe1, ci/b15-fix-probe2 (never merge).

## b15 fix round 2 (coordinator: Editor review items, stamped 2026-11-21T08:55Z)
Head a15d48a6. (b) dig/place to the aimed cell when its square reaches the 1.6 u circle (Full only, MinerTerraform.DigAimedCell; test added); no golden or digest change (fast 44881961553). (a) Miner wind-up arms at 72 seeds (run 44881560593): 0.35 blowouts 0.125 -> 0.264; 0.40 blowouts 0.153, top-10 0.498 -> 0.519, leader-at-half 0.458 -> 0.611; both make the Farmer the unbeaten class. NOT applied.
Round-one gates: fast 44881184125 (MovementShiver only), smoke 44881190050 MATCH with drop and rejoin, unity-ci 44881188229 (mesh timing x2 still red at median 6.5 ms, PerfSmooth sliced build 317 ms, FighterVisibility, OwnerWalkHud, MovementShiver), slow 44881186102 cancelled.
Round-two: fast 44881961553 (MovementShiver only), slow 44881963787 (7 reds), unity-ci 44887055933 and smoke 44887058125 running.
Final (2026-11-21T09:47Z): unity-ci 44887055933 (EditMode rerun after the GitHub 403): EditMode MovementShiver only; PlayMode 3of3 FighterVisibility + OwnerWalkHud + HatchFrame inconclusive; builds and Windows smoke pass. Cross-play smoke 44887058125 MATCH with drop and rejoin. Nothing left running.
