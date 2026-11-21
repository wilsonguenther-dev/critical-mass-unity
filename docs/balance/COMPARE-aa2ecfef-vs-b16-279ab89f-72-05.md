# Balance compare

- base: base aa2ecfef (aa2ecfef, MatchSeconds=240; HatchSeconds=120)
- head: head HEAD (f17242a7, MatchSeconds=240; HatchSeconds=120)
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

45 of 139 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.417 (0.310 to 0.532), n 72 | 0.431 (0.323 to 0.546), n 72 | 0.014 | +-0.157 | within noise |  |
| Crimson win share | 0.569 (0.454 to 0.677), n 72 | 0.569 (0.454 to 0.677), n 72 | 0.000 | +-0.158 | within noise | 0.400 to 0.600: met / met |
| Draw share | 0.014 (0.002 to 0.075), n 72 | 0.000 (0.000 to 0.051), n 72 | -0.014 | +-0.044 | within noise |  |
| West spawn (pad x=8) win share | 0.417 (0.310 to 0.532), n 72 | 0.431 (0.323 to 0.546), n 72 | 0.014 | +-0.157 | within noise |  |
| East spawn (pad x=104) win share | 0.569 (0.454 to 0.677), n 72 | 0.569 (0.454 to 0.677), n 72 | 0.000 | +-0.158 | within noise |  |
| Match length | 240.0 s (240.0 to 240.0), n 72 | 240.0 s (240.0 to 240.0), n 72 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.236 (0.153 to 0.346), n 72 | 0.375 (0.272 to 0.490), n 72 | 0.139 | +-0.146 | within noise | <= 0.200: missed / missed |
| Hill lead changes per match (median; spread = p25 to p75) | 2.00 (2.00 to 2.00), n 72 | 2.50 (2.00 to 3.00), n 72 | 0.500 | +-0.500 | within noise | >= 3.00: missed / missed |
| Hill lead changes per match (mean) | 2.17 (1.77 to 2.57), n 72 | 2.90 (2.48 to 3.32), n 72 | 0.736 | +-0.578 | **OUTSIDE noise** |  |
| Result decided at (last lead change) | 140.0 s (34.00 to 181.0), n 72 | 149.5 s (134.0 to 173.0), n 72 | 9.50 | +-76.04 | within noise |  |
| Result decided at, share of the match | 0.489 (0.402 to 0.576), n 72 | 0.592 (0.530 to 0.654), n 72 | 0.103 | +-0.107 | within noise |  |
| Leader at half time wins | 0.606 (0.489 to 0.711), n 71 | 0.444 (0.335 to 0.559), n 72 | -0.161 | +-0.157 | **OUTSIDE noise** | 0.600 to 0.850: met / missed |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.850 (0.833 to 0.866), n 72 | 0.577 (0.551 to 0.603), n 72 | -0.272 | +-0.031 | **OUTSIDE noise** |  |
| Longest unbroken hill hold | 49.02 s (41.57 to 55.43), n 72 | 21.67 s (17.43 to 31.07), n 72 | -27.35 | +-9.72 | **OUTSIDE noise** |  |
| Warrior: dead or getting up | 0.050 (0.047 to 0.053), n 72 | 0.055 (0.053 to 0.058), n 72 | 0.005 | +-0.004 | **OUTSIDE noise** |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.238 (0.230 to 0.245), n 72 | 0.237 (0.230 to 0.244), n 72 | -0.000 | +-0.010 | within noise |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.346 (0.333 to 0.359), n 72 | 0.374 (0.362 to 0.386), n 72 | 0.028 | +-0.018 | **OUTSIDE noise** |  |
| Warrior: dead or walking | 0.288 (0.278 to 0.297), n 72 | 0.292 (0.283 to 0.302), n 72 | 0.005 | +-0.013 | within noise |  |
| Warrior: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.125 (0.118 to 0.131), n 72 | 0.139 (0.133 to 0.146), n 72 | 0.014 | +-0.009 | **OUTSIDE noise** |  |
| Warrior: dead or walking back after a respawn | 0.163 (0.155 to 0.172), n 72 | 0.181 (0.173 to 0.190), n 72 | 0.018 | +-0.012 | **OUTSIDE noise** |  |
| Farmer: dead or getting up | 0.033 (0.031 to 0.035), n 72 | 0.038 (0.036 to 0.040), n 72 | 0.005 | +-0.003 | **OUTSIDE noise** |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.280 (0.271 to 0.288), n 72 | 0.296 (0.288 to 0.304), n 72 | 0.016 | +-0.011 | **OUTSIDE noise** |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.255 (0.246 to 0.263), n 72 | 0.267 (0.258 to 0.276), n 72 | 0.012 | +-0.013 | within noise |  |
| Farmer: dead or walking | 0.313 (0.303 to 0.322), n 72 | 0.334 (0.325 to 0.343), n 72 | 0.021 | +-0.013 | **OUTSIDE noise** |  |
| Farmer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.075 (0.070 to 0.080), n 72 | 0.089 (0.083 to 0.094), n 72 | 0.014 | +-0.008 | **OUTSIDE noise** |  |
| Farmer: dead or walking back after a respawn | 0.101 (0.094 to 0.108), n 72 | 0.119 (0.112 to 0.125), n 72 | 0.017 | +-0.010 | **OUTSIDE noise** |  |
| Miner: dead or getting up | 0.008 (0.006 to 0.009), n 72 | 0.010 (0.009 to 0.012), n 72 | 0.003 | +-0.002 | **OUTSIDE noise** |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.345 (0.339 to 0.351), n 72 | 0.342 (0.334 to 0.351), n 72 | -0.003 | +-0.010 | within noise |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.052 (0.046 to 0.057), n 72 | 0.062 (0.055 to 0.068), n 72 | 0.010 | +-0.009 | **OUTSIDE noise** |  |
| Miner: dead or walking | 0.353 (0.347 to 0.360), n 72 | 0.353 (0.344 to 0.362), n 72 | -0.000 | +-0.011 | within noise |  |
| Miner: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.013 (0.010 to 0.016), n 72 | 0.018 (0.015 to 0.021), n 72 | 0.006 | +-0.004 | **OUTSIDE noise** |  |
| Miner: dead or walking back after a respawn | 0.019 (0.015 to 0.023), n 72 | 0.026 (0.022 to 0.031), n 72 | 0.007 | +-0.006 | **OUTSIDE noise** |  |
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
| Healer: dead or getting up | 0.090 (0.086 to 0.094), n 72 | 0.092 (0.089 to 0.095), n 72 | 0.002 | +-0.005 | within noise |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.330 (0.320 to 0.340), n 72 | 0.380 (0.373 to 0.387), n 72 | 0.050 | +-0.012 | **OUTSIDE noise** |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.222 (0.217 to 0.228), n 72 | 0.243 (0.239 to 0.247), n 72 | 0.020 | +-0.007 | **OUTSIDE noise** |  |
| Healer: dead or walking | 0.420 (0.408 to 0.433), n 72 | 0.472 (0.463 to 0.480), n 72 | 0.051 | +-0.015 | **OUTSIDE noise** |  |
| Healer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.225 (0.215 to 0.235), n 72 | 0.240 (0.231 to 0.249), n 72 | 0.015 | +-0.013 | **OUTSIDE noise** |  |
| Healer: dead or walking back after a respawn | 0.294 (0.281 to 0.307), n 72 | 0.310 (0.299 to 0.321), n 72 | 0.016 | +-0.017 | within noise |  |
| all: dead or getting up | 0.046 (0.044 to 0.048), n 72 | 0.050 (0.049 to 0.052), n 72 | 0.004 | +-0.002 | **OUTSIDE noise** |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.286 (0.281 to 0.291), n 72 | 0.298 (0.294 to 0.303), n 72 | 0.012 | +-0.006 | **OUTSIDE noise** |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.244 (0.238 to 0.250), n 72 | 0.264 (0.258 to 0.269), n 72 | 0.020 | +-0.008 | **OUTSIDE noise** |  |
| all: dead or walking | 0.332 (0.326 to 0.339), n 72 | 0.349 (0.343 to 0.354), n 72 | 0.016 | +-0.008 | **OUTSIDE noise** |  |
| all: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.113 (0.108 to 0.117), n 72 | 0.125 (0.121 to 0.129), n 72 | 0.013 | +-0.006 | **OUTSIDE noise** |  |
| all: dead or walking back after a respawn | 0.148 (0.143 to 0.154), n 72 | 0.164 (0.158 to 0.169), n 72 | 0.015 | +-0.008 | **OUTSIDE noise** | <= 0.280: met / met |
| Warrior: damage dealt to fighters | 961.0 HP (919.4 to 1003), n 72 | 1060 HP (1017 to 1102), n 72 | 98.85 | +-59.36 | **OUTSIDE noise** |  |
| Warrior: kills | 4.69 (4.37 to 5.02), n 72 | 5.16 (4.86 to 5.45), n 72 | 0.462 | +-0.438 | **OUTSIDE noise** |  |
| Warrior: deaths | 3.57 (3.39 to 3.74), n 72 | 3.98 (3.80 to 4.17), n 72 | 0.417 | +-0.257 | **OUTSIDE noise** |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 84.45 s (82.24 to 86.67), n 72 | 56.10 s (53.57 to 58.63), n 72 | -28.36 | +-3.36 | **OUTSIDE noise** |  |
| Warrior: units delivered to the egg | 0.465 (0.381 to 0.549), n 72 | 0.059 (-0.026 to 0.144), n 72 | -0.406 | +-0.119 | **OUTSIDE noise** |  |
| Warrior: damage dealt to the Kaiju | 257.1 HP (230.7 to 283.5), n 72 | 170.6 HP (136.2 to 205.1), n 72 | -86.52 | +-43.41 | **OUTSIDE noise** |  |
| Warrior: share of all kills (whole class) | 0.622 (0.592 to 0.652), n 72 | 0.601 (0.577 to 0.626), n 72 | -0.021 | +-0.039 | within noise |  |
| Warrior: share of all fighter damage (whole class) | 0.623 (0.607 to 0.640), n 72 | 0.610 (0.597 to 0.623), n 72 | -0.014 | +-0.021 | within noise | <= 0.400: missed / missed |
| Farmer: damage dealt to fighters | 833.8 HP (790.9 to 876.7), n 72 | 974.2 HP (934.8 to 1014), n 72 | 140.4 | +-58.31 | **OUTSIDE noise** |  |
| Farmer: kills | 4.28 (3.88 to 4.68), n 72 | 5.12 (4.76 to 5.48), n 72 | 0.840 | +-0.539 | **OUTSIDE noise** |  |
| Farmer: deaths | 2.19 (2.04 to 2.35), n 72 | 2.54 (2.40 to 2.68), n 72 | 0.347 | +-0.211 | **OUTSIDE noise** |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 42.94 s (40.84 to 45.03), n 72 | 17.91 s (16.40 to 19.42), n 72 | -25.03 | +-2.58 | **OUTSIDE noise** |  |
| Farmer: units delivered to the egg | 13.67 (12.95 to 14.39), n 72 | 14.72 (13.91 to 15.52), n 72 | 1.05 | +-1.08 | within noise |  |
| Farmer: damage dealt to the Kaiju | 417.0 HP (368.6 to 465.4), n 72 | 274.2 HP (216.3 to 332.0), n 72 | -142.9 | +-75.41 | **OUTSIDE noise** |  |
| Farmer: share of all kills (whole class) | 0.290 (0.263 to 0.316), n 72 | 0.303 (0.281 to 0.325), n 72 | 0.014 | +-0.035 | within noise |  |
| Farmer: share of all fighter damage (whole class) | 0.274 (0.259 to 0.289), n 72 | 0.284 (0.271 to 0.296), n 72 | 0.010 | +-0.019 | within noise |  |
| Miner: damage dealt to fighters | 165.9 HP (146.5 to 185.4), n 72 | 205.3 HP (180.3 to 230.2), n 72 | 39.33 | +-31.65 | **OUTSIDE noise** |  |
| Miner: kills | 1.12 (0.942 to 1.29), n 72 | 1.35 (1.13 to 1.58), n 72 | 0.236 | +-0.287 | within noise |  |
| Miner: deaths | 0.542 (0.440 to 0.644), n 72 | 0.674 (0.584 to 0.763), n 72 | 0.132 | +-0.136 | within noise |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 9.16 s (7.84 to 10.48), n 72 | 7.46 s (5.95 to 8.98), n 72 | -1.69 | +-2.01 | within noise |  |
| Miner: units delivered to the egg | 0.035 (0.005 to 0.064), n 72 | 0.035 (0.005 to 0.064), n 72 | 0.000 | +-0.042 | within noise |  |
| Miner: damage dealt to the Kaiju | 44.15 HP (28.63 to 59.67), n 72 | 36.48 HP (19.65 to 53.31), n 72 | -7.67 | +-22.89 | within noise |  |
| Miner: share of all kills (whole class) | 0.076 (0.064 to 0.088), n 72 | 0.079 (0.066 to 0.092), n 72 | 0.003 | +-0.017 | within noise |  |
| Miner: share of all fighter damage (whole class) | 0.055 (0.048 to 0.061), n 72 | 0.059 (0.052 to 0.067), n 72 | 0.005 | +-0.010 | within noise |  |
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
| Healer: damage dealt to fighters | 145.9 HP (134.4 to 157.4), n 72 | 160.8 HP (150.3 to 171.3), n 72 | 14.88 | +-15.56 | within noise |  |
| Healer: kills | 0.167 (0.092 to 0.242), n 72 | 0.278 (0.198 to 0.357), n 72 | 0.111 | +-0.109 | **OUTSIDE noise** |  |
| Healer: deaths | 6.57 (6.30 to 6.84), n 72 | 6.81 (6.55 to 7.06), n 72 | 0.236 | +-0.376 | within noise |  |
| Healer: healing done (Healer mends and pulses) | 1220 HP (1179 to 1261), n 72 | 1340 HP (1305 to 1375), n 72 | 119.4 | +-53.92 | **OUTSIDE noise** |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 71.18 s (68.37 to 74.00), n 72 | 32.19 s (30.06 to 34.32), n 72 | -39.00 | +-3.53 | **OUTSIDE noise** |  |
| Healer: units delivered to the egg | 0.194 (0.082 to 0.307), n 72 | 0.007 (-0.007 to 0.021), n 72 | -0.188 | +-0.113 | **OUTSIDE noise** |  |
| Healer: damage dealt to the Kaiju | 10.21 HP (7.20 to 13.22), n 72 | 7.29 HP (4.36 to 10.23), n 72 | -2.92 | +-4.21 | within noise |  |
| Healer: share of all kills (whole class) | 0.012 (0.007 to 0.018), n 72 | 0.016 (0.012 to 0.021), n 72 | 0.004 | +-0.007 | within noise |  |
| Healer: share of all fighter damage (whole class) | 0.048 (0.044 to 0.051), n 72 | 0.047 (0.044 to 0.050), n 72 | -0.001 | +-0.005 | within noise |  |
| Top-10 share of all fighter damage (10 of 10 bodies) | 1.00 (1.00 to 1.00), n 72 | 1.00 (1.00 to 1.00), n 72 | 0.000 | +-0.000 | within noise | <= 0.500: missed / missed |
| Top-10 share of all kills | 1.00 (1.00 to 1.00), n 72 | 1.00 (1.00 to 1.00), n 72 | 0.000 | +-0.000 | within noise |  |
| Matches where the egg hatched | 1.00 (0.949 to 1.000), n 72 | 1.00 (0.949 to 1.000), n 72 | 0.000 | +-0.036 | within noise |  |
| Elder share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.069 (0.030 to 0.152), n 72 | 0.069 | +-0.066 | **OUTSIDE noise** |  |
| Grove share of hatches | 0.514 (0.401 to 0.626), n 72 | 0.583 (0.468 to 0.690), n 72 | 0.069 | +-0.158 | within noise |  |
| Brute share of hatches | 0.028 (0.008 to 0.096), n 72 | 0.028 (0.008 to 0.096), n 72 | 0.000 | +-0.062 | within noise |  |
| Timber share of hatches | 0.236 (0.153 to 0.346), n 72 | 0.153 (0.088 to 0.253), n 72 | -0.083 | +-0.127 | within noise |  |
| Mire share of hatches | 0.222 (0.142 to 0.331), n 72 | 0.167 (0.098 to 0.269), n 72 | -0.056 | +-0.128 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.603 (0.484 to 0.711), n 68 | 1.00 (0.923 to 1.00), n 46 | 0.397 | +-0.120 | **OUTSIDE noise** | <= 0.750: met / missed |
| Berserk share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| Hut level 1 reached at (team-matches that reached it) | 29.50 s (29.50 to 29.50), n 144 | 29.50 s (29.40 to 29.50), n 144 | 0.000 | +-0.050 | within noise |  |
| Hut level 2 reached at (team-matches that reached it) | 126.3 s (126.3 to 126.5), n 144 | 126.3 s (126.2 to 126.4), n 142 | -0.033 | +-0.165 | within noise |  |
| Hut level 3 reached at (team-matches that reached it) | 218.2 s (212.8 to 218.3), n 121 | 218.2 s (217.5 to 218.5), n 107 | 0.000 | +-2.83 | within noise |  |
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
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 0.000 (0.000 to 1.00), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.500 | within noise | < 5.00: met / met |
| Stuck episodes per match (mean) | 0.931 (0.566 to 1.29), n 72 | 0.722 (0.390 to 1.05), n 72 | -0.208 | +-0.493 | within noise |  |
| Sim tick cost, mean (wall clock, this machine) | 22.71 us (20.66 to 24.76), n 72 | 22.81 us (19.28 to 26.34), n 72 | 0.099 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 233.0 us (230.5 to 236.7), n 72 | 228.5 us (38.07 to 232.1), n 72 | -4.53 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
