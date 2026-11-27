# b16 physics fixes: where this stopped

- Branch `feat/b16-physics-fixes`, draft PR #231. Final head 97d73c22 is the re-pin commit; it has the same sim code as e3d847ea.
- Pinned: schema 6, RulesDigest 0x4024C8D689B75551, Full golden 0xC105B9BEE8D229B0 / 0x0522ACA2572C8806. Classic and PhysicsOnly are unchanged (asserted).
- Final gates: sim fast 44939996663, sim slow 44939998839, unity-ci all 44940001460, online-smoke cross-OS drop+rejoin 44940003858. Balance compare 44932808328 ran on e3d847ea and is committed under docs/balance/COMPARE-e3d847ea-vs-b15-cand1-aa2ecfef-72.*.
- The 6 CI rounds are used up.

## Known regressions, with the next fix

The bot no-snap-reversal rule (`PhysicsFeelConfig.BotNoSnapReversal`, BotSteer, the `else if` after the CM-21 turn limit) also applies to gatherers and to bots walking to a point. Suspected effects:

- The seed 1337 lone walker never gathers (stuck at 79.1, 54.0).
- Pressed/Unstick flags rose in the seed 1337 hatch-60 soak.
- The KaijuHill six-seed on-share fell to 0.153 (needs at least 0.20).

Next fix: restrict the rule to `a.AiState == AiState.Fight`, with the threshold back at TurnLimitFraction. In round 4 the same rule ran from 25% speed, and the soak was 192 flags against 276 in round 5.

## Still shivering

A ToHill Farmer pressed against the Kaiju at contact (1.7 u) slides along the body, alternating side by about ±0.04 u a tick. The bot's field wish points almost through the Kaiju's centre, so its sideways part flips sign. Candidate fixes: tangent hysteresis in the KaijuBody slide, or in the bot's wish at Kaiju contact.

## Final gate reds introduced by this branch (for the combiner)
- `CreatorAcceptancePlayTests.Need4` (PlayMode 3of3): bridges B7 and B8 stall at a 60° stick with assist off. The likely cause is the wall slide [12] or the corner graze [4] at the bridge mouth. Try `WallSlideFadeDeg` lower, or exempt the deck band.
- `WorldReactionsTests.NoNewReaderOfWorldEventsOutsideTheBus`: fixed in 555ba963 (an allowlist entry). This fix has not been run in CI.
