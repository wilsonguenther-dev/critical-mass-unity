# Player-seat balance (px S1, 2026-11-22)

Owner, after 1.0.1409: "I can be in a player's face, swing, and I see nothing to indicate I've hit... You were building a
game for bots. This is for real players. The bots are fillers." Combat is now judged from the human's seat, not by
bot-vs-bot statistics. Every rule below is Full rules only (`CombatFeel.On`); Classic and PhysicsOnly are unchanged.

## How it is measured
The human-seat probe (`~/cm-incidents/player-experience-rebuild-2026-11-22/data/s1-combat/probe/S1Probe.cs`) drives seat 0
through `MatchTick.AdvanceHumans` like a player: it aims where the enemy was 6 ticks (0.2 s) ago, closes to 0.8 x reach,
presses in reach, and raises the Warrior shield by the bots' own rule. One Normal bot, empty map, 8 start angles x 36
match-ups, 30 s, a draw is not a win. The permanent version is the gate suite `Sim.Tests/PlayerSeat/` (lane G).

## Rules (CombatFeelConfig block H, plus the changed class numbers)
| rule | fields | why (measured) |
|---|---|---|
| The swing tracks the aim during the wind-up | `SwingTrackDegPerSecond` 360, `SwingTrackLockTicks` 2, `SwingTrackDegPerSecondByClass` | a 0.2 s human landed 37%; 73% with it, 100% on a juking strafer |
| Bots by human limits | `BotHumanLimits`, `BotMeleeAimDelayTicks` 5/8/2, `BotMeleeAimErrorDeg` 20/35/5, `BotSwingTrackScale` 0.1/0/0.5, `BotPressConeIsClassArc`, `BotMeleeDodgeChance` 0.05/0.10/0.60 | a Normal bot landed 93-98% on a strafing human; now 0.73 (Easy 0.64, Hard 0.93) |
| A light hit does not eat a wind-up | `LightHitsKeepWindup`, `LightHitKeepFromShare` 0, `LightHitInterrupts` (Farmer only) | 14-20% of human swings were cancelled; 0.10-0.13 now. The Farmer keeps her interrupt or the bot duel order breaks |
| Wounded | `WoundedSeconds` 2, `WoundedHealShare` 0.3, `WoundedFountainShare` 0.25 | one Healer bot out-healed a 50% human in 25 of 36 match-ups |
| A hurt, wounded body is catchable | `WoundedSlowHpShare` 0.3, `WoundedSlowMultiplier` 0.75 | a fleeing 29%-HP bot was caught by a human Warrior 5/48; now 19/48 |
| Warrior wins his duels at equal skill | `SoloGuard` Warrior 0.45 (was 0.2), `WhiffExtraSeconds` Warrior 0 (was 0.25) | a 50%-landing human Warrior: 0-3 of 8; now 6-8 of 8 vs every other class |
| Miner | damage 17 -> 19, `MinerHeavyPierce` 1.4 -> 1.25 | a human Miner won 10 of 48 |
| Tap buffer | `BufferSeconds` 0.20 -> 0.35 | taps in rhythm were dropped (hit-feel audit P2) |

## Result (lag-6 human vs Normal bot, 288 duels; tables in `data/s1-combat/final/`)
| | before (b8870d1c) | after |
|---|---|---|
| swings landed | 0.37 | 0.84 |
| duels won, non-mirror share | 0.20 | 0.50 |
| swings cancelled by stagger | 0.14 | 0.13 |
| human Warrior vs each other class | 15 of 48 in all | 8,8,8,8,8 of 8; mirror 7 of 8 |
| human Healer | 0 of 48 | at most 3 of 8 except vs the Miner (5 of 8) |
| Normal bot land rate on a strafing human | 0.93 | 0.73 |

Trade-offs to watch in real matches: a lone attacker needs about 8 s on a Warrior (SoloGuard 0.45); Normal bots track
their swing at a tenth of the human rate; healing on a target under attack is cut to 30%.
