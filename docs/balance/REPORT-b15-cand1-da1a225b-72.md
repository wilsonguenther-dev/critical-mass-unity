# Balance report

- label: ci/balance/b15-cand1
- commit: da1a225b
- source: 72 bot matches 16v16, seeds 1337,4242,7,99,2024,31337,1,2,3,4,5,6,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,65,66,67
- rules profile: Full
- switches: none (the game's values)
- config: GameConfig.CreateBrowser5v5().ApplyRules(Full) + switches, the balance tests' config; ConfigHash 2305cd0f8b5fdeac
- actor 0: on bot autopilot (every body is a bot)
- wall time: 68.9 s for the matches (1.0 s each), 5.4 s for the duels, one thread, 4 CPUs seen
- generated: 2026-11-20 23:21 UTC

Every number shows its sample size n. Rates carry a 95% Wilson interval, means a 95% t interval, medians a 95% rank interval; spread is p25 to p75 over matches. Definitions: Assets/_Game/Sim.Tests/Harness/BalanceProbe.cs.

## Targets (docs/balance/TARGETS.json)

| Target | Row | Value | 95% CI | n | Band | Verdict |
|---|---|---|---|---|---|---|
| Time to kill 3 to 6 s (median 1v1 matchup) | `ttk.median` | 5.47 s | 4.80 to 6.13 | 30 | 3.00 to 6.00 | **met** (95% interval crosses the bound) |
| No class wins every duel | `duel.unbeaten` | 1 |  | 6 | <= 0.000 | **missed** |
| Top-10 share of damage at most 0.50 | `dmg.top10` | 0.624 | 0.620 to 0.627 | 72 | <= 0.500 | **missed** |
| Blowouts (2:1 on hill time) at most 0.15 | `blowout` | 0.528 | 0.414 to 0.639 | 72 | <= 0.150 | **missed** |
| Lead changes at least 5 a match (median) | `lead.changes` | 2.00 | 2.00 to 3.00 | 72 | >= 5.00 | **missed** |
| Leader at half wins 0.60 to 0.80 | `leader.half.win` | 0.514 | 0.401 to 0.626 | 72 | 0.600 to 0.800 | **missed** (95% interval crosses the bound) |
| Dead or walking at most 0.28 of a match | `time.deadwalk.all` | 0.453 | 0.449 to 0.457 | 72 | <= 0.280 | **missed** |
| Stuck episodes under 5 a match (median) | `stuck` | 8.00 | 6.00 to 9.00 | 72 | < 5.00 | **missed** |


## Outcome

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Azure win share | 0.417 | 0.310 to 0.532 | 72 |  |  |
| Crimson win share | 0.583 | 0.468 to 0.690 | 72 |  |  |
| Draw share | 0.000 | 0.000 to 0.051 | 72 |  |  |
| West spawn (pad x=8) win share | 0.417 | 0.310 to 0.532 | 72 |  | Azure always spawns west in this build: the same matches as the team row |
| East spawn (pad x=104) win share | 0.583 | 0.468 to 0.690 | 72 |  | Crimson always spawns east |
| Match length | 480.0 s | 480.0 to 480.0 | 72 | 480.0 to 480.0 |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.528 | 0.414 to 0.639 | 72 |  |  |
| Hill lead changes per match (median; spread = p25 to p75) | 2.00 | 2.00 to 3.00 | 72 | 1.00 to 4.00 |  |
| Hill lead changes per match (mean) | 2.65 | 2.19 to 3.12 | 72 | 1.00 to 4.00 |  |
| Result decided at (last lead change) | 236.0 s | 140.0 to 289.0 | 72 | 58.50 to 341.3 |  |
| Result decided at, share of the match | 0.450 | 0.375 to 0.525 | 72 | 0.122 to 0.711 |  |
| Leader at half time wins | 0.514 | 0.401 to 0.626 | 72 |  | matches with a leader at 4:00 and a winner |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.958 | 0.954 to 0.961 | 72 | 0.948 to 0.971 |  |
| Longest unbroken hill hold | 166.4 s | 123.9 to 186.4 | 72 | 103.3 to 217.7 |  |

## Time per class (share of each body's match)

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Warrior: dead or getting up | 0.132 | 0.130 to 0.134 | 72 | 0.126 to 0.138 |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.315 | 0.311 to 0.319 | 72 | 0.304 to 0.326 |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.305 | 0.301 to 0.308 | 72 | 0.296 to 0.316 |  |
| Warrior: dead or walking | 0.447 | 0.442 to 0.452 | 72 | 0.434 to 0.462 |  |
| Farmer: dead or getting up | 0.105 | 0.103 to 0.108 | 72 | 0.097 to 0.114 |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.330 | 0.323 to 0.336 | 72 | 0.312 to 0.346 |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.167 | 0.164 to 0.169 | 72 | 0.157 to 0.177 |  |
| Farmer: dead or walking | 0.435 | 0.427 to 0.443 | 72 | 0.415 to 0.456 |  |
| Miner: dead or getting up | 0.058 | 0.056 to 0.059 | 72 | 0.054 to 0.062 |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.366 | 0.363 to 0.370 | 72 | 0.356 to 0.380 |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.115 | 0.112 to 0.119 | 72 | 0.106 to 0.126 |  |
| Miner: dead or walking | 0.424 | 0.420 to 0.429 | 72 | 0.410 to 0.440 |  |
| Demolitions: dead or getting up | 0.154 | 0.152 to 0.157 | 72 | 0.145 to 0.162 |  |
| Demolitions: walking (moving, not fighting, empty-handed, off the hill) | 0.351 | 0.348 to 0.355 | 72 | 0.341 to 0.364 |  |
| Demolitions: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.266 | 0.263 to 0.269 | 72 | 0.257 to 0.274 |  |
| Demolitions: dead or walking | 0.506 | 0.500 to 0.512 | 72 | 0.491 to 0.522 |  |
| Builder: dead or getting up | 0.079 | 0.075 to 0.083 | 72 | 0.066 to 0.086 |  |
| Builder: walking (moving, not fighting, empty-handed, off the hill) | 0.339 | 0.326 to 0.352 | 72 | 0.301 to 0.385 |  |
| Builder: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.142 | 0.136 to 0.148 | 72 | 0.124 to 0.160 |  |
| Builder: dead or walking | 0.418 | 0.404 to 0.432 | 72 | 0.379 to 0.462 |  |
| Healer: dead or getting up | 0.141 | 0.138 to 0.144 | 72 | 0.133 to 0.150 |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.356 | 0.350 to 0.362 | 72 | 0.337 to 0.373 |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.175 | 0.174 to 0.177 | 72 | 0.171 to 0.180 |  |
| Healer: dead or walking | 0.497 | 0.489 to 0.505 | 72 | 0.474 to 0.521 |  |
| all: dead or getting up | 0.117 | 0.115 to 0.118 | 72 | 0.112 to 0.122 |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.336 | 0.333 to 0.339 | 72 | 0.328 to 0.347 |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.215 | 0.213 to 0.217 | 72 | 0.210 to 0.220 |  |
| all: dead or walking | 0.453 | 0.449 to 0.457 | 72 | 0.437 to 0.466 |  |

## Per class (per body per match)

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Warrior: damage dealt to fighters | 4074 HP | 4008 to 4139 | 72 | 3898 to 4228 |  |
| Warrior: kills | 17.96 | 17.62 to 18.31 | 72 | 17.20 to 18.90 |  |
| Warrior: deaths | 11.05 | 10.87 to 11.24 | 72 | 10.48 to 11.60 |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP | 0.000 to 0.000 | 72 | 0.000 to 0.000 |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 164.7 s | 162.0 to 167.3 | 72 | 157.9 to 172.7 |  |
| Warrior: units delivered to the egg | 0.064 | 0.029 to 0.099 | 72 | 0.000 to 0.100 |  |
| Warrior: damage dealt to the Kaiju | 565.5 HP | 524.4 to 606.6 | 72 | 386.0 to 719.7 |  |
| Warrior: share of all kills (whole class) | 0.604 | 0.598 to 0.610 | 72 | 0.583 to 0.627 |  |
| Farmer: damage dealt to fighters | 882.5 HP | 851.5 to 913.6 | 72 | 771.1 to 997.6 |  |
| Farmer: kills | 4.36 | 4.13 to 4.59 | 72 | 3.50 to 4.91 |  |
| Farmer: deaths | 8.84 | 8.63 to 9.05 | 72 | 8.25 to 9.53 |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP | 0.000 to 0.000 | 72 | 0.000 to 0.000 |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 88.97 s | 86.27 to 91.68 | 72 | 81.96 to 96.69 |  |
| Farmer: units delivered to the egg | 14.48 | 14.23 to 14.72 | 72 | 13.84 to 15.13 |  |
| Farmer: damage dealt to the Kaiju | 301.5 HP | 276.3 to 326.7 | 72 | 193.2 to 381.1 |  |
| Farmer: share of all kills (whole class) | 0.117 | 0.112 to 0.122 | 72 | 0.101 to 0.129 |  |
| Miner: damage dealt to fighters | 966.6 HP | 935.7 to 997.6 | 72 | 876.0 to 1042 |  |
| Miner: kills | 5.06 | 4.77 to 5.34 | 72 | 4.25 to 5.75 |  |
| Miner: deaths | 4.86 | 4.71 to 5.01 | 72 | 4.50 to 5.25 |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP | 0.000 to 0.000 | 72 | 0.000 to 0.000 |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 54.17 s | 52.11 to 56.23 | 72 | 47.72 to 60.45 |  |
| Miner: units delivered to the egg | 0.566 | 0.446 to 0.686 | 72 | 0.000 to 0.750 |  |
| Miner: damage dealt to the Kaiju | 231.1 HP | 203.8 to 258.3 | 72 | 132.8 to 302.3 |  |
| Miner: share of all kills (whole class) | 0.068 | 0.064 to 0.072 | 72 | 0.057 to 0.078 |  |
| Demolitions: damage dealt to fighters | 3186 HP | 3124 to 3248 | 72 | 3003 to 3363 |  |
| Demolitions: kills | 12.97 | 12.59 to 13.35 | 72 | 11.75 to 14.25 |  |
| Demolitions: deaths | 12.89 | 12.67 to 13.12 | 72 | 12.25 to 13.56 |  |
| Demolitions: healing done (Healer mends and pulses) | 0.000 HP | 0.000 to 0.000 | 72 | 0.000 to 0.000 |  |
| Demolitions: objective credit (hill seconds scored, Actor.HillSeconds) | 141.8 s | 138.6 to 145.0 | 72 | 131.8 to 151.7 |  |
| Demolitions: units delivered to the egg | 0.347 | 0.260 to 0.435 | 72 | 0.000 to 0.500 |  |
| Demolitions: damage dealt to the Kaiju | 134.5 HP | 118.3 to 150.7 | 72 | 79.95 to 179.4 |  |
| Demolitions: share of all kills (whole class) | 0.175 | 0.170 to 0.180 | 72 | 0.159 to 0.190 |  |
| Builder: damage dealt to fighters | 1112 HP | 1053 to 1170 | 72 | 947.6 to 1266 |  |
| Builder: kills | 5.23 | 4.77 to 5.69 | 72 | 4.00 to 6.00 |  |
| Builder: deaths | 6.65 | 6.33 to 6.97 | 72 | 5.50 to 7.50 |  |
| Builder: healing done (Healer mends and pulses) | 0.000 HP | 0.000 to 0.000 | 72 | 0.000 to 0.000 |  |
| Builder: objective credit (hill seconds scored, Actor.HillSeconds) | 90.96 s | 88.01 to 93.91 | 72 | 83.30 to 97.69 |  |
| Builder: units delivered to the egg | 4.79 | 3.78 to 5.81 | 72 | 0.500 to 8.25 |  |
| Builder: damage dealt to the Kaiju | 338.8 HP | 307.3 to 370.2 | 72 | 231.8 to 413.8 |  |
| Builder: share of all kills (whole class) | 0.035 | 0.032 to 0.038 | 72 | 0.027 to 0.045 |  |
| Healer: damage dealt to fighters | 77.74 HP | 72.99 to 82.48 | 72 | 62.16 to 92.19 |  |
| Healer: kills | 0.073 | 0.045 to 0.101 | 72 | 0.000 to 0.250 |  |
| Healer: deaths | 11.78 | 11.53 to 12.03 | 72 | 11.25 to 12.50 |  |
| Healer: healing done (Healer mends and pulses) | 5226 HP | 5132 to 5320 | 72 | 4948 to 5489 |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 146.7 s | 142.5 to 151.0 | 72 | 135.2 to 160.2 |  |
| Healer: units delivered to the egg | 0.014 | -0.005 to 0.033 | 72 | 0.000 to 0.000 |  |
| Healer: damage dealt to the Kaiju | 0.525 HP | 0.261 to 0.789 | 72 | 0.000 to 0.000 |  |
| Healer: share of all kills (whole class) | 0.001 | 0.001 to 0.001 | 72 | 0.000 to 0.003 |  |

## Concentration

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Top-10 share of all fighter damage (10 of 32 bodies) | 0.624 | 0.620 to 0.627 | 72 | 0.612 to 0.635 |  |
| Top-10 share of all kills | 0.657 | 0.651 to 0.663 | 72 | 0.640 to 0.674 |  |

## Kaiju

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Matches where the egg hatched | 1.00 | 0.949 to 1.000 | 72 |  |  |
| Elder share of hatches | 0.014 | 0.002 to 0.075 | 72 |  |  |
| Grove share of hatches | 0.000 | 0.000 to 0.051 | 72 |  |  |
| Brute share of hatches | 0.319 | 0.223 to 0.434 | 72 |  |  |
| Timber share of hatches | 0.389 | 0.285 to 0.504 | 72 |  |  |
| Mire share of hatches | 0.278 | 0.188 to 0.390 | 72 |  |  |
| Kaiju owner (side at the hatch) wins | 0.894 | 0.797 to 0.948 | 66 |  | hatched, owned by a team, with a winner |
| Berserk share of hatches | 0.083 | 0.039 to 0.170 | 72 |  |  |

## Blacksmith hut

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Hut level 1 reached at (team-matches that reached it) | 17.68 s | 16.50 to 18.87 | 144 | 16.50 to 19.67 |  |
| Hut level 2 reached at (team-matches that reached it) | 77.47 s | 71.50 to 80.27 | 144 | 67.54 to 88.74 |  |
| Hut level 3 reached at (team-matches that reached it) | 140.5 s | 136.5 to 147.8 | 144 | 127.2 to 156.2 |  |
| Hut level 4 reached at (team-matches that reached it) | 286.1 s | 281.0 to 294.0 | 144 | 268.2 to 311.3 |  |
| Hut level 5 reached at (team-matches that reached it) | 451.1 s | 446.4 to 456.0 | 93 | 435.9 to 464.2 |  |
| Team-matches reaching level 5 | 0.646 | 0.565 to 0.719 | 144 |  |  |

## Structures (per match)

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Builder walls built | 3.17 | 2.67 to 3.66 | 72 | 2.00 to 4.00 |  |
| Builder wall cells broken or crumbled | 2.88 | 2.38 to 3.37 | 72 | 1.00 to 4.00 |  |
| Ladders and bridges placed | 6.51 | 5.98 to 7.05 | 72 | 5.00 to 8.00 |  |
| Ladders and bridges destroyed | 1.04 | 0.783 to 1.30 | 72 | 0.000 to 1.00 |  |
| Barricades placed | 0.000 | 0.000 to 0.000 | 72 | 0.000 to 0.000 |  |
| Barricades broken | 9.57 | 7.99 to 11.15 | 72 | 5.00 to 14.00 |  |
| Decorations toppled | 0.000 | 0.000 to 0.000 | 72 | 0.000 to 0.000 |  |

## Bots and cost

| Stat | Value | 95% CI | n | Spread | Note |
|---|---|---|---|---|---|
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 8.00 | 6.00 to 9.00 | 72 | 4.00 to 12.00 |  |
| Stuck episodes per match (mean) | 9.19 | 7.14 to 11.25 | 72 | 4.00 to 12.00 |  |
| Sim tick cost, mean (wall clock, this machine) | 64.68 us | 61.39 to 67.97 | 72 | 59.78 to 65.66 |  |
| Sim tick cost, p99 within a match (median over matches) | 668.0 us | 663.8 to 672.6 | 72 | 654.3 to 680.2 |  |
| Sim events lost to the ring buffer (must be 0) | 0 |  | 72 |  |  |
| Replays that stopped at a fingerprint mismatch | 0 |  | 72 |  |  |

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
| (65, 41) | 28 |
| (66, 41) | 25 |
| (67, 39) | 18 |
| (63, 42) | 17 |
| (66, 36) | 14 |
| (67, 37) | 12 |
| (44, 40) | 10 |
| (46, 42) | 10 |
| (65, 51) | 10 |
| (67, 28) | 9 |

## Needs a sim counter (left out)

- Hut choice pick and win rates: this build has no hut choice (it lands with feat/b15-comeback-hut); the row appears once a HutChoice event or state exists.

## Matches

| Source | Winner | Hill Azure | Hill Crimson | Lead changes | Decided at (s) | Blowout | Kaiju | Owner | Stuck | Tick mean (us) |
|---|---|---|---|---|---|---|---|---|---|---|
| bot seed 1337 | Crimson | 222.0 | 231.7 | 0 | 11.00 | no | Brute | Azure | 3 | 175.4 |
| bot seed 4242 | Crimson | 122.7 | 317.3 | 2 | 93.00 | yes | Brute | Berserk | 13 | 57.61 |
| bot seed 7 | Crimson | 150.5 | 295.8 | 4 | 309.0 | no | Mire | Crimson | 7 | 64.02 |
| bot seed 99 | Crimson | 129.9 | 294.5 | 4 | 273.0 | yes | Brute | Crimson | 8 | 55.55 |
| bot seed 2024 | Azure | 291.5 | 149.4 | 7 | 285.0 | no | Timber | Berserk | 28 | 69.01 |
| bot seed 31337 | Azure | 377.1 | 80.23 | 5 | 163.0 | yes | Timber | Azure | 2 | 61.86 |
| bot seed 1 | Azure | 252.9 | 189.3 | 3 | 417.0 | no | Mire | Azure | 17 | 62.67 |
| bot seed 2 | Crimson | 145.0 | 299.4 | 2 | 235.0 | yes | Mire | Crimson | 3 | 65.16 |
| bot seed 3 | Crimson | 87.77 | 353.6 | 0 | 11.00 | yes | Brute | Berserk | 22 | 62.28 |
| bot seed 4 | Azure | 282.9 | 158.6 | 5 | 309.0 | no | Brute | Azure | 10 | 63.31 |
| bot seed 5 | Crimson | 217.0 | 224.9 | 0 | 11.00 | no | Timber | Azure | 7 | 61.79 |
| bot seed 6 | Crimson | 212.8 | 239.4 | 0 | 11.00 | no | Timber | Azure | 0 | 55.52 |
| bot seed 8 | Crimson | 164.4 | 273.2 | 2 | 341.0 | no | Timber | Crimson | 6 | 58.81 |
| bot seed 9 | Crimson | 115.7 | 321.7 | 4 | 177.0 | yes | Brute | Crimson | 8 | 64.86 |
| bot seed 10 | Crimson | 188.4 | 260.6 | 4 | 395.0 | no | Brute | Crimson | 14 | 62.75 |
| bot seed 11 | Azure | 340.9 | 103.0 | 5 | 148.0 | yes | Mire | Azure | 12 | 65.45 |
| bot seed 12 | Azure | 280.8 | 161.2 | 3 | 275.0 | no | Timber | Azure | 12 | 60.73 |
| bot seed 13 | Crimson | 114.5 | 339.4 | 4 | 173.0 | yes | Timber | Crimson | 11 | 56.47 |
| bot seed 14 | Crimson | 118.1 | 319.7 | 4 | 237.0 | yes | Mire | Crimson | 16 | 59.94 |
| bot seed 15 | Crimson | 44.60 | 397.1 | 2 | 45.00 | yes | Timber | Crimson | 1 | 62.82 |
| bot seed 16 | Azure | 254.5 | 193.4 | 1 | 420.0 | no | Brute | Azure | 12 | 62.27 |
| bot seed 17 | Crimson | 135.7 | 320.6 | 0 | 11.00 | yes | Brute | Azure | 6 | 62.56 |
| bot seed 18 | Azure | 228.7 | 224.7 | 3 | 477.0 | no | Elder | Azure | 6 | 76.11 |
| bot seed 19 | Azure | 233.9 | 206.4 | 5 | 70.00 | no | Mire | Crimson | 13 | 57.09 |
| bot seed 20 | Crimson | 5.67 | 455.2 | 0 | 11.00 | yes | Brute | Crimson | 6 | 69.14 |
| bot seed 21 | Azure | 226.0 | 219.6 | 3 | 475.0 | no | Timber | Azure | 8 | 55.22 |
| bot seed 22 | Crimson | 148.5 | 285.6 | 6 | 257.0 | no | Mire | Crimson | 8 | 63.95 |
| bot seed 23 | Azure | 306.1 | 136.2 | 3 | 311.0 | yes | Timber | Azure | 0 | 61.71 |
| bot seed 24 | Crimson | 154.0 | 289.4 | 2 | 336.0 | no | Mire | Crimson | 5 | 59.30 |
| bot seed 25 | Azure | 274.5 | 175.6 | 3 | 382.0 | no | Mire | Azure | 11 | 67.06 |
| bot seed 26 | Crimson | 71.87 | 385.5 | 0 | 11.00 | yes | Timber | Crimson | 3 | 57.52 |
| bot seed 27 | Crimson | 113.4 | 338.3 | 8 | 256.0 | yes | Timber | Crimson | 5 | 68.91 |
| bot seed 28 | Crimson | 118.6 | 324.4 | 6 | 266.0 | yes | Timber | Crimson | 6 | 63.26 |
| bot seed 29 | Azure | 377.5 | 60.13 | 1 | 23.00 | yes | Timber | Azure | 5 | 55.24 |
| bot seed 30 | Crimson | 209.1 | 237.8 | 2 | 398.0 | no | Timber | Berserk | 2 | 52.87 |
| bot seed 31 | Azure | 299.5 | 130.5 | 1 | 312.0 | yes | Timber | Azure | 10 | 62.62 |
| bot seed 32 | Crimson | 38.20 | 416.2 | 0 | 11.00 | yes | Timber | Berserk | 9 | 65.70 |
| bot seed 33 | Crimson | 78.97 | 382.7 | 2 | 105.0 | yes | Timber | Crimson | 9 | 79.62 |
| bot seed 34 | Crimson | 165.9 | 286.5 | 2 | 356.0 | no | Timber | Crimson | 5 | 63.43 |
| bot seed 35 | Azure | 347.1 | 98.87 | 5 | 233.0 | yes | Timber | Azure | 20 | 64.22 |
| bot seed 36 | Azure | 227.9 | 227.6 | 2 | 35.00 | no | Brute | Azure | 13 | 66.49 |
| bot seed 37 | Azure | 284.3 | 159.5 | 1 | 356.0 | no | Timber | Azure | 7 | 62.17 |
| bot seed 38 | Azure | 253.0 | 199.6 | 5 | 428.0 | no | Mire | Azure | 9 | 74.79 |
| bot seed 39 | Azure | 245.9 | 197.7 | 1 | 433.0 | no | Timber | Azure | 8 | 57.25 |
| bot seed 40 | Azure | 278.0 | 172.2 | 3 | 327.0 | no | Mire | Azure | 6 | 60.13 |
| bot seed 41 | Azure | 316.1 | 140.9 | 1 | 306.0 | yes | Timber | Azure | 16 | 62.94 |
| bot seed 42 | Crimson | 75.80 | 374.6 | 4 | 147.0 | yes | Timber | Crimson | 1 | 58.61 |
| bot seed 43 | Crimson | 87.33 | 352.1 | 2 | 21.00 | yes | Timber | Berserk | 13 | 64.81 |
| bot seed 44 | Crimson | 50.50 | 390.4 | 0 | 11.00 | yes | Mire | Crimson | 17 | 63.34 |
| bot seed 45 | Azure | 381.5 | 64.50 | 3 | 124.0 | yes | Brute | Azure | 0 | 61.59 |
| bot seed 46 | Crimson | 82.07 | 366.4 | 2 | 78.00 | yes | Brute | Crimson | 4 | 60.93 |
| bot seed 47 | Azure | 324.2 | 127.9 | 7 | 285.0 | yes | Brute | Azure | 9 | 62.82 |
| bot seed 48 | Crimson | 203.5 | 253.1 | 0 | 11.00 | no | Brute | Azure | 3 | 66.92 |
| bot seed 49 | Azure | 357.5 | 82.30 | 5 | 63.00 | yes | Brute | Azure | 9 | 61.69 |
| bot seed 50 | Crimson | 224.0 | 231.7 | 4 | 442.0 | no | Brute | Crimson | 2 | 66.59 |
| bot seed 51 | Crimson | 218.8 | 237.9 | 0 | 11.00 | no | Timber | Azure | 42 | 62.52 |
| bot seed 52 | Crimson | 157.6 | 297.0 | 2 | 342.0 | no | Brute | Crimson | 5 | 65.62 |
| bot seed 53 | Crimson | 155.1 | 284.1 | 6 | 276.0 | no | Mire | Crimson | 9 | 65.08 |
| bot seed 54 | Azure | 245.7 | 188.3 | 3 | 413.0 | no | Mire | Azure | 3 | 56.50 |
| bot seed 55 | Crimson | 80.03 | 375.8 | 4 | 166.0 | yes | Mire | Crimson | 12 | 79.16 |
| bot seed 56 | Azure | 344.9 | 93.43 | 5 | 229.0 | yes | Mire | Azure | 5 | 68.76 |
| bot seed 57 | Crimson | 216.1 | 226.8 | 4 | 470.0 | no | Mire | Crimson | 6 | 68.75 |
| bot seed 58 | Crimson | 97.70 | 349.4 | 2 | 101.0 | yes | Mire | Crimson | 4 | 66.23 |
| bot seed 59 | Crimson | 46.57 | 408.4 | 2 | 81.00 | yes | Brute | Crimson | 8 | 61.66 |
| bot seed 60 | Crimson | 55.67 | 398.5 | 0 | 11.00 | yes | Timber | Crimson | 0 | 70.59 |
| bot seed 61 | Azure | 259.6 | 182.3 | 1 | 404.0 | no | Brute | Azure | 8 | 56.21 |
| bot seed 62 | Crimson | 86.43 | 371.6 | 2 | 140.0 | yes | Mire | Crimson | 15 | 65.65 |
| bot seed 63 | Crimson | 223.5 | 231.1 | 2 | 473.0 | no | Brute | Crimson | 4 | 57.83 |
| bot seed 64 | Azure | 415.5 | 40.43 | 1 | 26.00 | yes | Brute | Azure | 3 | 68.54 |
| bot seed 65 | Azure | 317.4 | 125.4 | 1 | 289.0 | yes | Mire | Azure | 58 | 59.16 |
| bot seed 66 | Crimson | 81.70 | 371.7 | 0 | 11.00 | yes | Timber | Crimson | 3 | 58.73 |
| bot seed 67 | Azure | 262.0 | 183.5 | 3 | 395.0 | no | Brute | Azure | 11 | 61.04 |
