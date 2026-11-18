# Balance report

- label: base origin/integration/batch-13-cand1
- commit: 1ea0ff78
- source: 24 bot matches 16v16, seeds 1337,4242,7,99,2024,31337,1,2,3,4,5,6,8,9,10,11,12,13,14,15,16,17,18,19
- rules profile: Full
- switches: none (the game's values)
- config: GameConfig.CreateBrowser5v5().ApplyRules(Full) + switches, the balance tests' config; ConfigHash 1566d27733ff6869
- actor 0: on bot autopilot (every body is a bot)
- wall time: 25.9 s for the matches (1.1 s each), 5.6 s for the duels, one thread, 4 CPUs seen
- generated: 2026-11-18 06:41 UTC

Every number shows its sample size n. Rates carry a 95% Wilson interval, means a 95% t interval, medians a 95% rank interval; spread is p25 to p75 over matches. Definitions: Assets/_Game/Sim.Tests/Harness/BalanceProbe.cs.

## Targets (docs/balance/TARGETS.json)

| Target | Row | Value | 95% CI | n | Band | Verdict |
|---|---|---|---|---|---|---|
| Time to kill 3 to 6 s (median 1v1 matchup) | `ttk.median` | 5.47 s | 4.80 to 6.13 | 30 | 3.00 to 6.00 | **met** (95% interval crosses the bound) |
| No class wins every duel | `duel.unbeaten` | 1 |  | 6 | <= 0.000 | **missed** |
| Top-10 share of damage at most 0.50 | `dmg.top10` | 0.622 | 0.615 to 0.630 | 24 | <= 0.500 | **missed** |
| Blowouts (2:1 on hill time) at most 0.15 | `blowout` | 0.458 | 0.279 to 0.649 | 24 | <= 0.150 | **missed** |
| Lead changes at least 5 a match (median) | `lead.changes` | 3.00 | 2.00 to 4.00 | 24 | >= 5.00 | **missed** |
| Leader at half wins 0.60 to 0.80 | `leader.half.win` | 0.583 | 0.388 to 0.755 | 24 | 0.600 to 0.800 | **missed** (95% interval crosses the bound) |
| Dead or walking at most 0.28 of a match | `time.deadwalk.all` | 0.456 | 0.449 to 0.463 | 24 | <= 0.280 | **missed** |
| Stuck episodes under 5 a match (median) | `stuck` | 9.00 | 6.00 to 13.00 | 24 | < 5.00 | **missed** |


## Outcome

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Azure win share | 0.375 | 0.212 to 0.573 | 24 |  |  |
| Crimson win share | 0.625 | 0.427 to 0.788 | 24 |  |  |
| Draw share | 0.000 | 0.000 to 0.138 | 24 |  |  |
| West spawn (pad x=8) win share | 0.375 | 0.212 to 0.573 | 24 |  | Azure always spawns west in this build: the same matches as the team row |
| East spawn (pad x=104) win share | 0.625 | 0.427 to 0.788 | 24 |  | Crimson always spawns east |
| Match length | 480.0 s | 480.0 to 480.0 | 24 | 480.0 to 480.0 |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.458 | 0.279 to 0.649 | 24 |  |  |
| Hill lead changes per match (median; spread = p25 to p75) | 3.00 | 2.00 to 4.00 | 24 | 1.75 to 4.00 |  |
| Hill lead changes per match (mean) | 2.88 | 2.04 to 3.71 | 24 | 1.75 to 4.00 |  |
| Result decided at (last lead change) | 206.0 s | 70.00 to 309.0 | 24 | 63.75 to 309.0 |  |
| Result decided at, share of the match | 0.425 | 0.294 to 0.556 | 24 | 0.133 to 0.644 |  |
| Leader at half time wins | 0.583 | 0.388 to 0.755 | 24 |  | matches with a leader at 4:00 and a winner |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.952 | 0.946 to 0.958 | 24 | 0.942 to 0.963 |  |
| Longest unbroken hill hold | 117.4 s | 94.40 to 156.5 | 24 | 94.10 to 159.3 |  |

## Time per class (share of each body's match)

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Warrior: dead or getting up | 0.131 | 0.127 to 0.135 | 24 | 0.125 to 0.137 |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.316 | 0.309 to 0.322 | 24 | 0.308 to 0.326 |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.305 | 0.300 to 0.311 | 24 | 0.300 to 0.312 |  |
| Warrior: dead or walking | 0.447 | 0.437 to 0.457 | 24 | 0.437 to 0.459 |  |
| Farmer: dead or getting up | 0.106 | 0.102 to 0.110 | 24 | 0.099 to 0.112 |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.337 | 0.326 to 0.348 | 24 | 0.319 to 0.357 |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.167 | 0.161 to 0.173 | 24 | 0.158 to 0.177 |  |
| Farmer: dead or walking | 0.443 | 0.430 to 0.455 | 24 | 0.420 to 0.461 |  |
| Miner: dead or getting up | 0.058 | 0.054 to 0.061 | 24 | 0.054 to 0.063 |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.368 | 0.361 to 0.375 | 24 | 0.354 to 0.384 |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.117 | 0.111 to 0.123 | 24 | 0.108 to 0.130 |  |
| Miner: dead or walking | 0.426 | 0.417 to 0.435 | 24 | 0.407 to 0.445 |  |
| Demolitions: dead or getting up | 0.156 | 0.152 to 0.160 | 24 | 0.150 to 0.162 |  |
| Demolitions: walking (moving, not fighting, empty-handed, off the hill) | 0.353 | 0.347 to 0.358 | 24 | 0.347 to 0.363 |  |
| Demolitions: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.264 | 0.259 to 0.269 | 24 | 0.256 to 0.274 |  |
| Demolitions: dead or walking | 0.508 | 0.499 to 0.517 | 24 | 0.500 to 0.519 |  |
| Builder: dead or getting up | 0.078 | 0.071 to 0.085 | 24 | 0.066 to 0.084 |  |
| Builder: walking (moving, not fighting, empty-handed, off the hill) | 0.340 | 0.318 to 0.363 | 24 | 0.301 to 0.380 |  |
| Builder: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.145 | 0.134 to 0.157 | 24 | 0.124 to 0.165 |  |
| Builder: dead or walking | 0.418 | 0.394 to 0.443 | 24 | 0.375 to 0.462 |  |
| Healer: dead or getting up | 0.142 | 0.135 to 0.149 | 24 | 0.135 to 0.150 |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.356 | 0.345 to 0.368 | 24 | 0.332 to 0.371 |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.175 | 0.172 to 0.178 | 24 | 0.171 to 0.178 |  |
| Healer: dead or walking | 0.498 | 0.482 to 0.515 | 24 | 0.474 to 0.522 |  |
| all: dead or getting up | 0.117 | 0.114 to 0.120 | 24 | 0.112 to 0.122 |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.339 | 0.334 to 0.344 | 24 | 0.330 to 0.349 |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.216 | 0.213 to 0.219 | 24 | 0.211 to 0.221 |  |
| all: dead or walking | 0.456 | 0.449 to 0.463 | 24 | 0.445 to 0.466 |  |

## Per class (per body per match)

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Warrior: damage dealt to fighters | 4058 HP | 3928 to 4187 | 24 | 3872 to 4254 |  |
| Warrior: kills | 17.80 | 17.20 to 18.40 | 24 | 17.08 to 18.70 |  |
| Warrior: deaths | 10.99 | 10.65 to 11.33 | 24 | 10.40 to 11.53 |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP | 0.000 to 0.000 | 24 | 0.000 to 0.000 |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 162.9 s | 158.8 to 167.0 | 24 | 160.0 to 168.2 |  |
| Warrior: units delivered to the egg | 0.071 | 0.017 to 0.124 | 24 | 0.000 to 0.100 |  |
| Warrior: damage dealt to the Kaiju | 592.6 HP | 526.0 to 659.1 | 24 | 458.4 to 727.6 |  |
| Warrior: share of all kills (whole class) | 0.599 | 0.587 to 0.611 | 24 | 0.575 to 0.622 |  |
| Farmer: damage dealt to fighters | 867.1 HP | 811.1 to 923.0 | 24 | 771.3 to 979.0 |  |
| Farmer: kills | 4.35 | 4.00 to 4.71 | 24 | 3.84 to 4.78 |  |
| Farmer: deaths | 8.91 | 8.53 to 9.28 | 24 | 8.25 to 9.38 |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP | 0.000 to 0.000 | 24 | 0.000 to 0.000 |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 87.35 s | 82.49 to 92.21 | 24 | 81.16 to 92.63 |  |
| Farmer: units delivered to the egg | 14.42 | 13.91 to 14.92 | 24 | 13.72 to 14.97 |  |
| Farmer: damage dealt to the Kaiju | 322.3 HP | 279.2 to 365.3 | 24 | 235.6 to 389.3 |  |
| Farmer: share of all kills (whole class) | 0.117 | 0.109 to 0.125 | 24 | 0.106 to 0.128 |  |
| Miner: damage dealt to fighters | 991.1 HP | 935.3 to 1047 | 24 | 891.7 to 1056 |  |
| Miner: kills | 5.14 | 4.61 to 5.66 | 24 | 4.44 to 5.75 |  |
| Miner: deaths | 4.86 | 4.57 to 5.16 | 24 | 4.50 to 5.31 |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP | 0.000 to 0.000 | 24 | 0.000 to 0.000 |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 54.37 s | 50.60 to 58.13 | 24 | 49.72 to 59.95 |  |
| Miner: units delivered to the egg | 0.552 | 0.360 to 0.744 | 24 | 0.000 to 0.750 |  |
| Miner: damage dealt to the Kaiju | 222.1 HP | 180.1 to 264.1 | 24 | 143.1 to 288.8 |  |
| Miner: share of all kills (whole class) | 0.069 | 0.062 to 0.076 | 24 | 0.061 to 0.078 |  |
| Demolitions: damage dealt to fighters | 3139 HP | 3032 to 3247 | 24 | 2930 to 3368 |  |
| Demolitions: kills | 13.22 | 12.43 to 14.01 | 24 | 11.94 to 14.31 |  |
| Demolitions: deaths | 12.99 | 12.66 to 13.32 | 24 | 12.50 to 13.50 |  |
| Demolitions: healing done (Healer mends and pulses) | 0.000 HP | 0.000 to 0.000 | 24 | 0.000 to 0.000 |  |
| Demolitions: objective credit (hill seconds scored, Actor.HillSeconds) | 142.0 s | 137.6 to 146.3 | 24 | 136.6 to 147.3 |  |
| Demolitions: units delivered to the egg | 0.292 | 0.134 to 0.449 | 24 | 0.000 to 0.500 |  |
| Demolitions: damage dealt to the Kaiju | 140.9 HP | 109.2 to 172.7 | 24 | 82.94 to 213.3 |  |
| Demolitions: share of all kills (whole class) | 0.178 | 0.169 to 0.186 | 24 | 0.159 to 0.187 |  |
| Builder: damage dealt to fighters | 1127 HP | 1026 to 1229 | 24 | 986.2 to 1231 |  |
| Builder: kills | 5.35 | 4.66 to 6.05 | 24 | 4.50 to 6.25 |  |
| Builder: deaths | 6.63 | 6.07 to 7.18 | 24 | 5.50 to 7.00 |  |
| Builder: healing done (Healer mends and pulses) | 0.000 HP | 0.000 to 0.000 | 24 | 0.000 to 0.000 |  |
| Builder: objective credit (hill seconds scored, Actor.HillSeconds) | 86.82 s | 81.57 to 92.07 | 24 | 81.14 to 94.73 |  |
| Builder: units delivered to the egg | 4.56 | 2.63 to 6.50 | 24 | 0.500 to 6.50 |  |
| Builder: damage dealt to the Kaiju | 374.7 HP | 313.9 to 435.5 | 24 | 279.7 to 422.0 |  |
| Builder: share of all kills (whole class) | 0.036 | 0.031 to 0.041 | 24 | 0.029 to 0.045 |  |
| Healer: damage dealt to fighters | 76.10 HP | 67.54 to 84.66 | 24 | 62.27 to 91.77 |  |
| Healer: kills | 0.073 | 0.024 to 0.122 | 24 | 0.000 to 0.250 |  |
| Healer: deaths | 11.88 | 11.28 to 12.47 | 24 | 11.25 to 12.50 |  |
| Healer: healing done (Healer mends and pulses) | 5157 HP | 5014 to 5300 | 24 | 4938 to 5396 |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 143.9 s | 136.1 to 151.6 | 24 | 135.1 to 154.4 |  |
| Healer: units delivered to the egg | 0.000 | 0.000 to 0.000 | 24 | 0.000 to 0.000 |  |
| Healer: damage dealt to the Kaiju | 0.296 HP | -0.042 to 0.634 | 24 | 0.000 to 0.000 |  |
| Healer: share of all kills (whole class) | 0.001 | 0.000 to 0.002 | 24 | 0.000 to 0.003 |  |

## Concentration

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Top-10 share of all fighter damage (10 of 32 bodies) | 0.622 | 0.615 to 0.630 | 24 | 0.608 to 0.635 |  |
| Top-10 share of all kills | 0.647 | 0.637 to 0.657 | 24 | 0.636 to 0.661 |  |

## Kaiju

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Matches where the egg hatched | 1.00 | 0.862 to 1.000 | 24 |  |  |
| Elder share of hatches | 0.042 | 0.007 to 0.202 | 24 |  |  |
| Grove share of hatches | 0.000 | 0.000 to 0.138 | 24 |  |  |
| Brute share of hatches | 0.375 | 0.212 to 0.573 | 24 |  |  |
| Timber share of hatches | 0.333 | 0.180 to 0.533 | 24 |  |  |
| Mire share of hatches | 0.250 | 0.120 to 0.449 | 24 |  |  |
| Kaiju owner (side at the hatch) wins | 0.762 | 0.549 to 0.894 | 21 |  | hatched, owned by a team, with a winner |
| Berserk share of hatches | 0.125 | 0.043 to 0.310 | 24 |  |  |

## Blacksmith hut

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Hut level 1 reached at (team-matches that reached it) | 17.68 s | 16.50 to 19.17 | 48 | 16.50 to 19.67 |  |
| Hut level 2 reached at (team-matches that reached it) | 77.38 s | 68.83 to 86.13 | 48 | 66.75 to 88.28 |  |
| Hut level 3 reached at (team-matches that reached it) | 141.1 s | 135.8 to 149.5 | 48 | 126.6 to 153.6 |  |
| Hut level 4 reached at (team-matches that reached it) | 287.0 s | 276.0 to 295.8 | 48 | 268.0 to 303.0 |  |
| Hut level 5 reached at (team-matches that reached it) | 449.5 s | 436.8 to 455.7 | 31 | 436.4 to 461.6 |  |
| Team-matches reaching level 5 | 0.646 | 0.504 to 0.766 | 48 |  |  |

## Structures (per match)

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Builder walls built | 2.96 | 2.09 to 3.83 | 24 | 1.75 to 4.00 |  |
| Builder wall cells broken or crumbled | 2.71 | 1.81 to 3.61 | 24 | 1.00 to 4.00 |  |
| Ladders and bridges placed | 6.79 | 5.78 to 7.80 | 24 | 5.00 to 8.00 |  |
| Ladders and bridges destroyed | 1.00 | 0.606 to 1.39 | 24 | 0.000 to 1.25 |  |
| Barricades placed | 0.000 | 0.000 to 0.000 | 24 | 0.000 to 0.000 |  |
| Barricades broken | 9.25 | 6.31 to 12.19 | 24 | 3.00 to 14.00 |  |
| Decorations toppled | 0.000 | 0.000 to 0.000 | 24 | 0.000 to 0.000 |  |

## Bots and cost

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 9.00 | 6.00 to 13.00 | 24 | 6.00 to 13.00 |  |
| Stuck episodes per match (mean) | 9.88 | 7.07 to 12.68 | 24 | 6.00 to 13.00 |  |
| Sim tick cost, mean (wall clock, this machine) | 71.71 us | 65.51 to 77.92 | 24 | 67.45 to 71.26 |  |
| Sim tick cost, p99 within a match (median over matches) | 691.6 us | 683.3 to 700.3 | 24 | 682.2 to 701.1 |  |
| Sim events lost to the ring buffer (must be 0) | 0 |  | 24 |  |  |
| Replays that stopped at a fingerprint mismatch | 0 |  | 24 |  |  |

## Duels (1v1, no bots)

Played on an empty map at the hill, every other body removed; primary attack only (held, aimed, walking in to 0.8 x range); no abilities, dodge or hut levels; 8 start angles per cell; 30 s cap. The sim is deterministic, so the angles are the only spread: n counts tries, not independent samples.

- Time to kill, median over the matchups that can kill: 5.47 s (95% CI 4.80 to 6.13, n 30) (n = matchups)
- Matchups whose time to kill is within 3 to 6 s: 18 of 30
- Classes that win every duel (more than half of each matchup): 1 of 6 (Warrior)

### Time to kill (s): row attacks a passive column (median over start angles; kills / tries)

| attacker \ target | Warrior | Farmer | Miner | Demolitions | Builder | Healer |
|---|---|---|---|---|---|---|
| Warrior | 5.77 (8/8) | 3.67 (8/8) | 4.37 (8/8) | 4.37 (8/8) | 4.37 (8/8) | 3.67 (8/8) |
| Farmer | 8.53 * (8/8) | 5.53 (8/8) | 6.13 * (8/8) | 6.13 * (8/8) | 6.13 * (8/8) | 6.13 * (8/8) |
| Miner | 6.80 * (8/8) | 4.80 (8/8) | 5.47 (8/8) | 5.47 (8/8) | 5.47 (8/8) | 4.80 (8/8) |
| Demolitions | 10.00 * (8/8) | 5.40 (8/8) | 6.93 * (8/8) | 6.93 * (8/8) | 6.93 * (8/8) | 6.17 * (8/8) |
| Builder | 6.80 * (8/8) | 4.80 (8/8) | 5.47 (8/8) | 5.47 (8/8) | 5.47 (8/8) | 4.80 (8/8) |
| Healer | no kill (0/8) | no kill (0/8) | no kill (0/8) | no kill (0/8) | no kill (0/8) | no kill (0/8) |

`*` = outside 3 to 6 s.

### Duel win share: row beats column (wins / tries; draws are timeouts or trades; mirror matches not shown)

| row \ column | Warrior | Farmer | Miner | Demolitions | Builder | Healer |
|---|---|---|---|---|---|---|
| Warrior | - | 1.00 (8/8) | 1.00 (8/8) | 1.00 (8/8) | 1.00 (8/8) | 1.00 (8/8) |
| Farmer | 0.000 (0/8) | - | 0.000 (0/8) | 0.000 (0/8) | 0.000 (0/8) | 1.00 (8/8) |
| Miner | 0.000 (0/8) | 1.00 (8/8) | - | 1.00 (8/8) | 0.000 (0/8) | 1.00 (8/8) |
| Demolitions | 0.000 (0/8) | 1.00 (8/8) | 0.000 (0/8) | - | 0.000 (0/8) | 1.00 (8/8) |
| Builder | 0.000 (0/8) | 1.00 (8/8) | 0.000 (0/8) | 1.00 (8/8) | - | 1.00 (8/8) |
| Healer | 0.000 (0/8) | 0.000 (0/8) | 0.000 (0/8) | 0.000 (0/8) | 0.000 (0/8) | - |

### Stuck episodes: top cells (all matches)

| Cell (x, z) | Episodes |
|---|---|
| (66, 36) | 11 |
| (67, 28) | 9 |
| (18, 38) | 9 |
| (47, 41) | 9 |
| (68, 27) | 8 |
| (50, 38) | 6 |
| (49, 40) | 6 |
| (46, 42) | 5 |
| (45, 43) | 5 |
| (93, 48) | 5 |

## Needs a sim counter (left out)

- Hut choice pick and win rates: this build has no hut choice (it lands with feat/b15-comeback-hut); the row appears once a HutChoice event or state exists.

## Matches

| Source | Winner | Hill Azure | Hill Crimson | Lead changes | Decided at (s) | Blowout | Kaiju | Owner | Stuck | Tick mean (us) |
|---|---|---|---|---|---|---|---|---|---|---|
| bot seed 1337 | Crimson | 222.0 | 231.7 | 0 | 11.00 | no | Brute | Azure | 3 | 136.9 |
| bot seed 4242 | Crimson | 122.7 | 317.3 | 2 | 93.00 | yes | Brute | Berserk | 13 | 58.94 |
| bot seed 7 | Crimson | 150.5 | 295.8 | 4 | 309.0 | no | Mire | Crimson | 7 | 69.87 |
| bot seed 99 | Crimson | 129.9 | 294.5 | 4 | 273.0 | yes | Brute | Crimson | 8 | 61.89 |
| bot seed 2024 | Azure | 291.5 | 149.4 | 7 | 285.0 | no | Timber | Berserk | 28 | 76.95 |
| bot seed 31337 | Azure | 377.1 | 80.23 | 5 | 163.0 | yes | Timber | Azure | 2 | 68.81 |
| bot seed 1 | Azure | 252.9 | 189.3 | 3 | 417.0 | no | Mire | Azure | 17 | 68.55 |
| bot seed 2 | Crimson | 145.0 | 299.4 | 2 | 235.0 | yes | Mire | Crimson | 3 | 71.56 |
| bot seed 3 | Crimson | 87.77 | 353.6 | 0 | 11.00 | yes | Brute | Berserk | 22 | 70.02 |
| bot seed 4 | Azure | 282.9 | 158.6 | 5 | 309.0 | no | Brute | Azure | 10 | 71.16 |
| bot seed 5 | Crimson | 217.0 | 224.9 | 0 | 11.00 | no | Timber | Azure | 7 | 69.74 |
| bot seed 6 | Crimson | 212.8 | 239.4 | 0 | 11.00 | no | Timber | Azure | 0 | 62.91 |
| bot seed 8 | Crimson | 164.4 | 273.2 | 2 | 341.0 | no | Timber | Crimson | 6 | 67.88 |
| bot seed 9 | Crimson | 115.7 | 321.7 | 4 | 177.0 | yes | Brute | Crimson | 8 | 73.47 |
| bot seed 10 | Crimson | 188.4 | 260.6 | 4 | 395.0 | no | Brute | Crimson | 14 | 71.06 |
| bot seed 11 | Azure | 340.9 | 103.0 | 5 | 148.0 | yes | Mire | Azure | 12 | 72.39 |
| bot seed 12 | Azure | 280.8 | 161.2 | 3 | 275.0 | no | Timber | Azure | 12 | 67.66 |
| bot seed 13 | Crimson | 114.5 | 339.4 | 4 | 173.0 | yes | Timber | Crimson | 11 | 64.31 |
| bot seed 14 | Crimson | 118.1 | 319.7 | 4 | 237.0 | yes | Mire | Crimson | 16 | 66.81 |
| bot seed 15 | Crimson | 44.60 | 397.1 | 2 | 45.00 | yes | Timber | Crimson | 1 | 68.87 |
| bot seed 16 | Azure | 254.5 | 193.4 | 1 | 420.0 | no | Brute | Azure | 12 | 68.12 |
| bot seed 17 | Crimson | 135.7 | 320.6 | 0 | 11.00 | yes | Brute | Azure | 6 | 69.30 |
| bot seed 18 | Azure | 228.7 | 224.7 | 3 | 477.0 | no | Elder | Azure | 6 | 81.44 |
| bot seed 19 | Azure | 233.9 | 206.4 | 5 | 70.00 | no | Mire | Crimson | 13 | 62.50 |
