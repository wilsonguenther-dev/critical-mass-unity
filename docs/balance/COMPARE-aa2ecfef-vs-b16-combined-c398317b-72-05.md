# Balance compare

- base: base aa2ecfef (aa2ecfef, MatchSeconds=240; HatchSeconds=120)
- head: head HEAD (19af4ffd, MatchSeconds=240; HatchSeconds=120)
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

28 of 139 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.417 (0.310 to 0.532), n 72 | 0.444 (0.335 to 0.559), n 72 | 0.028 | +-0.158 | within noise |  |
| Crimson win share | 0.569 (0.454 to 0.677), n 72 | 0.542 (0.427 to 0.652), n 72 | -0.028 | +-0.158 | within noise | 0.400 to 0.600: met / met |
| Draw share | 0.014 (0.002 to 0.075), n 72 | 0.014 (0.002 to 0.075), n 72 | 0.000 | +-0.051 | within noise |  |
| West spawn (pad x=8) win share | 0.417 (0.310 to 0.532), n 72 | 0.444 (0.335 to 0.559), n 72 | 0.028 | +-0.158 | within noise |  |
| East spawn (pad x=104) win share | 0.569 (0.454 to 0.677), n 72 | 0.542 (0.427 to 0.652), n 72 | -0.028 | +-0.158 | within noise |  |
| Match length | 240.0 s (240.0 to 240.0), n 72 | 240.0 s (240.0 to 240.0), n 72 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.236 (0.153 to 0.346), n 72 | 0.222 (0.142 to 0.331), n 72 | -0.014 | +-0.135 | within noise | <= 0.200: missed / missed |
| Hill lead changes per match (median; spread = p25 to p75) | 2.00 (2.00 to 2.00), n 72 | 2.00 (2.00 to 3.00), n 72 | 0.000 | +-0.500 | within noise | >= 3.00: missed / missed |
| Hill lead changes per match (mean) | 2.17 (1.77 to 2.57), n 72 | 2.28 (1.92 to 2.63), n 72 | 0.111 | +-0.533 | within noise |  |
| Result decided at (last lead change) | 140.0 s (34.00 to 181.0), n 72 | 188.5 s (164.0 to 203.0), n 72 | 48.50 | +-76.04 | within noise |  |
| Result decided at, share of the match | 0.489 (0.402 to 0.576), n 72 | 0.617 (0.533 to 0.701), n 72 | 0.128 | +-0.121 | **OUTSIDE noise** |  |
| Leader at half time wins | 0.606 (0.489 to 0.711), n 71 | 0.521 (0.407 to 0.633), n 71 | -0.085 | +-0.158 | within noise | 0.600 to 0.850: met / missed |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.850 (0.833 to 0.866), n 72 | 0.903 (0.889 to 0.917), n 72 | 0.054 | +-0.022 | **OUTSIDE noise** |  |
| Longest unbroken hill hold | 49.02 s (41.57 to 55.43), n 72 | 55.95 s (50.60 to 64.33), n 72 | 6.93 | +-9.76 | within noise |  |
| Warrior: dead or getting up | 0.050 (0.047 to 0.053), n 72 | 0.042 (0.040 to 0.045), n 72 | -0.008 | +-0.004 | **OUTSIDE noise** |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.238 (0.230 to 0.245), n 72 | 0.237 (0.230 to 0.244), n 72 | -0.001 | +-0.010 | within noise |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.346 (0.333 to 0.359), n 72 | 0.359 (0.345 to 0.373), n 72 | 0.013 | +-0.019 | within noise |  |
| Warrior: dead or walking | 0.288 (0.278 to 0.297), n 72 | 0.279 (0.270 to 0.287), n 72 | -0.009 | +-0.012 | within noise |  |
| Warrior: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.125 (0.118 to 0.131), n 72 | 0.109 (0.103 to 0.114), n 72 | -0.016 | +-0.009 | **OUTSIDE noise** |  |
| Warrior: dead or walking back after a respawn | 0.163 (0.155 to 0.172), n 72 | 0.141 (0.134 to 0.148), n 72 | -0.022 | +-0.011 | **OUTSIDE noise** |  |
| Farmer: dead or getting up | 0.033 (0.031 to 0.035), n 72 | 0.029 (0.027 to 0.031), n 72 | -0.004 | +-0.003 | **OUTSIDE noise** |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.280 (0.271 to 0.288), n 72 | 0.268 (0.262 to 0.275), n 72 | -0.011 | +-0.010 | **OUTSIDE noise** |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.255 (0.246 to 0.263), n 72 | 0.261 (0.251 to 0.272), n 72 | 0.006 | +-0.014 | within noise |  |
| Farmer: dead or walking | 0.313 (0.303 to 0.322), n 72 | 0.297 (0.290 to 0.305), n 72 | -0.015 | +-0.012 | **OUTSIDE noise** |  |
| Farmer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.075 (0.070 to 0.080), n 72 | 0.068 (0.063 to 0.074), n 72 | -0.007 | +-0.008 | within noise |  |
| Farmer: dead or walking back after a respawn | 0.101 (0.094 to 0.108), n 72 | 0.091 (0.084 to 0.098), n 72 | -0.010 | +-0.010 | within noise |  |
| Miner: dead or getting up | 0.008 (0.006 to 0.009), n 72 | 0.008 (0.006 to 0.009), n 72 | 0.000 | +-0.002 | within noise |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.345 (0.339 to 0.351), n 72 | 0.347 (0.341 to 0.354), n 72 | 0.002 | +-0.009 | within noise |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.052 (0.046 to 0.057), n 72 | 0.054 (0.048 to 0.061), n 72 | 0.002 | +-0.008 | within noise |  |
| Miner: dead or walking | 0.353 (0.347 to 0.360), n 72 | 0.355 (0.348 to 0.363), n 72 | 0.002 | +-0.010 | within noise |  |
| Miner: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.013 (0.010 to 0.016), n 72 | 0.012 (0.009 to 0.015), n 72 | -0.001 | +-0.004 | within noise |  |
| Miner: dead or walking back after a respawn | 0.019 (0.015 to 0.023), n 72 | 0.019 (0.015 to 0.022), n 72 | -0.001 | +-0.006 | within noise |  |
| Demolitions: dead or getting up | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: walking (moving, not fighting, empty-handed, off the hill) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: fighting (hit dealt or taken, or a swing, in the last 1 s) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: dead or walking | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: dead or walking back after a respawn | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: dead or getting up | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: walking (moving, not fighting, empty-handed, off the hill) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: fighting (hit dealt or taken, or a swing, in the last 1 s) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: dead or walking | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: dead or walking back after a respawn | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Healer: dead or getting up | 0.090 (0.086 to 0.094), n 72 | 0.088 (0.083 to 0.093), n 72 | -0.002 | +-0.006 | within noise |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.330 (0.320 to 0.340), n 72 | 0.319 (0.307 to 0.331), n 72 | -0.011 | +-0.016 | within noise |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.222 (0.217 to 0.228), n 72 | 0.211 (0.205 to 0.217), n 72 | -0.011 | +-0.008 | **OUTSIDE noise** |  |
| Healer: dead or walking | 0.420 (0.408 to 0.433), n 72 | 0.406 (0.391 to 0.422), n 72 | -0.014 | +-0.020 | within noise |  |
| Healer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.225 (0.215 to 0.235), n 72 | 0.215 (0.204 to 0.227), n 72 | -0.010 | +-0.015 | within noise |  |
| Healer: dead or walking back after a respawn | 0.294 (0.281 to 0.307), n 72 | 0.282 (0.267 to 0.297), n 72 | -0.012 | +-0.019 | within noise |  |
| all: dead or getting up | 0.046 (0.044 to 0.048), n 72 | 0.042 (0.040 to 0.044), n 72 | -0.004 | +-0.003 | **OUTSIDE noise** |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.286 (0.281 to 0.291), n 72 | 0.282 (0.277 to 0.286), n 72 | -0.005 | +-0.007 | within noise |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.244 (0.238 to 0.250), n 72 | 0.249 (0.242 to 0.256), n 72 | 0.005 | +-0.010 | within noise |  |
| all: dead or walking | 0.332 (0.326 to 0.339), n 72 | 0.323 (0.318 to 0.329), n 72 | -0.009 | +-0.008 | **OUTSIDE noise** |  |
| all: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.113 (0.108 to 0.117), n 72 | 0.103 (0.098 to 0.107), n 72 | -0.010 | +-0.006 | **OUTSIDE noise** |  |
| all: dead or walking back after a respawn | 0.148 (0.143 to 0.154), n 72 | 0.135 (0.129 to 0.140), n 72 | -0.013 | +-0.008 | **OUTSIDE noise** | <= 0.280: met / met |
| Warrior: damage dealt to fighters | 961.0 HP (919.4 to 1003), n 72 | 931.5 HP (895.1 to 967.8), n 72 | -29.50 | +-55.20 | within noise |  |
| Warrior: kills | 4.69 (4.37 to 5.02), n 72 | 4.63 (4.36 to 4.89), n 72 | -0.069 | +-0.417 | within noise |  |
| Warrior: deaths | 3.57 (3.39 to 3.74), n 72 | 2.99 (2.83 to 3.14), n 72 | -0.580 | +-0.235 | **OUTSIDE noise** |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 84.45 s (82.24 to 86.67), n 72 | 89.86 s (88.14 to 91.58), n 72 | 5.40 | +-2.80 | **OUTSIDE noise** |  |
| Warrior: units delivered to the egg | 0.465 (0.381 to 0.549), n 72 | 0.587 (0.496 to 0.677), n 72 | 0.122 | +-0.124 | within noise |  |
| Warrior: damage dealt to the Kaiju | 257.1 HP (230.7 to 283.5), n 72 | 309.7 HP (286.3 to 333.1), n 72 | 52.53 | +-35.29 | **OUTSIDE noise** |  |
| Warrior: share of all kills (whole class) | 0.622 (0.592 to 0.652), n 72 | 0.656 (0.634 to 0.679), n 72 | 0.034 | +-0.038 | within noise |  |
| Warrior: share of all fighter damage (whole class) | 0.623 (0.607 to 0.640), n 72 | 0.652 (0.639 to 0.666), n 72 | 0.029 | +-0.022 | **OUTSIDE noise** | <= 0.400: missed / missed |
| Farmer: damage dealt to fighters | 833.8 HP (790.9 to 876.7), n 72 | 749.4 HP (709.3 to 789.4), n 72 | -84.44 | +-58.68 | **OUTSIDE noise** |  |
| Farmer: kills | 4.28 (3.88 to 4.68), n 72 | 3.84 (3.51 to 4.17), n 72 | -0.438 | +-0.523 | within noise |  |
| Farmer: deaths | 2.19 (2.04 to 2.35), n 72 | 1.94 (1.81 to 2.08), n 72 | -0.250 | +-0.207 | **OUTSIDE noise** |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 42.94 s (40.84 to 45.03), n 72 | 48.31 s (46.60 to 50.01), n 72 | 5.37 | +-2.70 | **OUTSIDE noise** |  |
| Farmer: units delivered to the egg | 13.67 (12.95 to 14.39), n 72 | 13.78 (13.05 to 14.51), n 72 | 0.111 | +-1.02 | within noise |  |
| Farmer: damage dealt to the Kaiju | 417.0 HP (368.6 to 465.4), n 72 | 361.3 HP (320.1 to 402.5), n 72 | -55.74 | +-63.58 | within noise |  |
| Farmer: share of all kills (whole class) | 0.290 (0.263 to 0.316), n 72 | 0.276 (0.252 to 0.301), n 72 | -0.013 | +-0.036 | within noise |  |
| Farmer: share of all fighter damage (whole class) | 0.274 (0.259 to 0.289), n 72 | 0.265 (0.251 to 0.278), n 72 | -0.010 | +-0.020 | within noise |  |
| Miner: damage dealt to fighters | 165.9 HP (146.5 to 185.4), n 72 | 101.2 HP (88.16 to 114.1), n 72 | -64.80 | +-23.38 | **OUTSIDE noise** |  |
| Miner: kills | 1.12 (0.942 to 1.29), n 72 | 0.715 (0.552 to 0.878), n 72 | -0.403 | +-0.240 | **OUTSIDE noise** |  |
| Miner: deaths | 0.542 (0.440 to 0.644), n 72 | 0.535 (0.435 to 0.634), n 72 | -0.007 | +-0.143 | within noise |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 9.16 s (7.84 to 10.48), n 72 | 10.05 s (8.54 to 11.56), n 72 | 0.893 | +-2.00 | within noise |  |
| Miner: units delivered to the egg | 0.035 (0.005 to 0.064), n 72 | 0.014 (-0.005 to 0.033), n 72 | -0.021 | +-0.035 | within noise |  |
| Miner: damage dealt to the Kaiju | 44.15 HP (28.63 to 59.67), n 72 | 22.67 HP (7.18 to 38.16), n 72 | -21.49 | +-21.93 | within noise |  |
| Miner: share of all kills (whole class) | 0.076 (0.064 to 0.088), n 72 | 0.054 (0.041 to 0.067), n 72 | -0.022 | +-0.017 | **OUTSIDE noise** |  |
| Miner: share of all fighter damage (whole class) | 0.055 (0.048 to 0.061), n 72 | 0.036 (0.031 to 0.041), n 72 | -0.019 | +-0.008 | **OUTSIDE noise** |  |
| Demolitions: damage dealt to fighters | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: kills | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: deaths | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: healing done (Healer mends and pulses) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: objective credit (hill seconds scored, Actor.HillSeconds) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: units delivered to the egg | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: damage dealt to the Kaiju | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Demolitions: share of all kills (whole class) | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Demolitions: share of all fighter damage (whole class) | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Builder: damage dealt to fighters | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: kills | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: deaths | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: healing done (Healer mends and pulses) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: objective credit (hill seconds scored, Actor.HillSeconds) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: units delivered to the egg | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: damage dealt to the Kaiju | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Builder: share of all kills (whole class) | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Builder: share of all fighter damage (whole class) | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Healer: damage dealt to fighters | 145.9 HP (134.4 to 157.4), n 72 | 134.5 HP (124.0 to 145.1), n 72 | -11.35 | +-15.58 | within noise |  |
| Healer: kills | 0.167 (0.092 to 0.242), n 72 | 0.188 (0.116 to 0.259), n 72 | 0.021 | +-0.103 | within noise |  |
| Healer: deaths | 6.57 (6.30 to 6.84), n 72 | 6.38 (6.03 to 6.72), n 72 | -0.194 | +-0.436 | within noise |  |
| Healer: healing done (Healer mends and pulses) | 1220 HP (1179 to 1261), n 72 | 1135 HP (1092 to 1177), n 72 | -85.53 | +-59.13 | **OUTSIDE noise** |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 71.18 s (68.37 to 74.00), n 72 | 75.76 s (73.13 to 78.39), n 72 | 4.58 | +-3.86 | **OUTSIDE noise** |  |
| Healer: units delivered to the egg | 0.194 (0.082 to 0.307), n 72 | 0.139 (0.063 to 0.215), n 72 | -0.056 | +-0.135 | within noise |  |
| Healer: damage dealt to the Kaiju | 10.21 HP (7.20 to 13.22), n 72 | 12.40 HP (7.62 to 17.17), n 72 | 2.19 | +-5.65 | within noise |  |
| Healer: share of all kills (whole class) | 0.012 (0.007 to 0.018), n 72 | 0.014 (0.008 to 0.019), n 72 | 0.001 | +-0.008 | within noise |  |
| Healer: share of all fighter damage (whole class) | 0.048 (0.044 to 0.051), n 72 | 0.047 (0.044 to 0.051), n 72 | -0.000 | +-0.005 | within noise |  |
| Top-10 share of all fighter damage (10 of 10 bodies) | 1.00 (1.00 to 1.00), n 72 | 1.00 (1.00 to 1.00), n 72 | 0.000 | +-0.000 | within noise | <= 0.500: missed / missed |
| Top-10 share of all kills | 1.00 (1.00 to 1.00), n 72 | 1.00 (1.00 to 1.00), n 72 | 0.000 | +-0.000 | within noise |  |
| Matches where the egg hatched | 1.00 (0.949 to 1.000), n 72 | 1.00 (0.949 to 1.000), n 72 | 0.000 | +-0.036 | within noise |  |
| Elder share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.069 (0.030 to 0.152), n 72 | 0.069 | +-0.066 | **OUTSIDE noise** |  |
| Grove share of hatches | 0.514 (0.401 to 0.626), n 72 | 0.583 (0.468 to 0.690), n 72 | 0.069 | +-0.158 | within noise |  |
| Brute share of hatches | 0.028 (0.008 to 0.096), n 72 | 0.014 (0.002 to 0.075), n 72 | -0.014 | +-0.057 | within noise |  |
| Timber share of hatches | 0.236 (0.153 to 0.346), n 72 | 0.194 (0.120 to 0.300), n 72 | -0.042 | +-0.132 | within noise |  |
| Mire share of hatches | 0.222 (0.142 to 0.331), n 72 | 0.139 (0.077 to 0.237), n 72 | -0.083 | +-0.124 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.603 (0.484 to 0.711), n 68 | 0.657 (0.540 to 0.758), n 70 | 0.054 | +-0.157 | within noise | <= 0.750: met / met |
| Berserk share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| Hut level 1 reached at (team-matches that reached it) | 29.50 s (29.50 to 29.50), n 144 | 29.47 s (29.10 to 29.47), n 144 | -0.033 | +-0.183 | within noise |  |
| Hut level 2 reached at (team-matches that reached it) | 126.3 s (126.3 to 126.5), n 144 | 125.6 s (125.5 to 126.0), n 144 | -0.700 | +-0.291 | **OUTSIDE noise** |  |
| Hut level 3 reached at (team-matches that reached it) | 218.2 s (212.8 to 218.3), n 121 | 217.2 s (217.0 to 217.7), n 129 | -0.967 | +-2.80 | within noise |  |
| Hut level 4 reached at (team-matches that reached it) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Hut level 5 reached at (team-matches that reached it) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Team-matches reaching level 5 | 0.000 (0.000 to 0.026), n 144 | 0.000 (0.000 to 0.026), n 144 | 0.000 | +-0.018 | within noise |  |
| Builder walls built | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Builder wall cells broken or crumbled | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Ladders and bridges placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Ladders and bridges destroyed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades broken | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Decorations toppled | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 0.000 (0.000 to 1.00), n 72 | 0.000 (0.000 to 1.00), n 72 | 0.000 | +-0.707 | within noise | < 5.00: met / met |
| Stuck episodes per match (mean) | 0.931 (0.566 to 1.29), n 72 | 1.06 (0.682 to 1.43), n 72 | 0.125 | +-0.522 | within noise |  |
| Sim tick cost, mean (wall clock, this machine) | 26.35 us (24.52 to 28.18), n 72 | 27.91 us (24.97 to 30.85), n 72 | 1.56 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 236.9 us (234.7 to 241.4), n 72 | 234.1 us (230.7 to 241.0), n 72 | -2.80 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
