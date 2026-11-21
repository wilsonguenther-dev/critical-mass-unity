# Balance compare

- base: base 61e3f9f5 (61e3f9f5, none (the game's values))
- head: head  (a7433909, none (the game's values))
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

17 of 208 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.500 (0.387 to 0.613), n 72 | 0.639 (0.524 to 0.740), n 72 | 0.139 | +-0.156 | within noise |  |
| Crimson win share | 0.486 (0.374 to 0.599), n 72 | 0.361 (0.260 to 0.476), n 72 | -0.125 | +-0.156 | within noise |  |
| Draw share | 0.014 (0.002 to 0.075), n 72 | 0.000 (0.000 to 0.051), n 72 | -0.014 | +-0.044 | within noise |  |
| West spawn (pad x=8) win share | 0.500 (0.387 to 0.613), n 72 | 0.639 (0.524 to 0.740), n 72 | 0.139 | +-0.156 | within noise |  |
| East spawn (pad x=104) win share | 0.486 (0.374 to 0.599), n 72 | 0.361 (0.260 to 0.476), n 72 | -0.125 | +-0.156 | within noise |  |
| Match length | 480.0 s (480.0 to 480.0), n 72 | 480.0 s (480.0 to 480.0), n 72 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.125 (0.067 to 0.221), n 72 | 0.125 (0.067 to 0.221), n 72 | 0.000 | +-0.109 | within noise | <= 0.150: met / met |
| Hill lead changes per match (median; spread = p25 to p75) | 4.00 (3.00 to 5.00), n 72 | 4.00 (3.00 to 5.00), n 72 | 0.000 | +-1.41 | within noise | >= 5.00: missed / missed |
| Hill lead changes per match (mean) | 4.40 (3.75 to 5.06), n 72 | 4.29 (3.78 to 4.81), n 72 | -0.111 | +-0.835 | within noise |  |
| Result decided at (last lead change) | 282.0 s (176.0 to 335.0), n 72 | 326.5 s (178.0 to 381.0), n 72 | 44.50 | +-128.9 | within noise |  |
| Result decided at, share of the match | 0.529 (0.457 to 0.601), n 72 | 0.566 (0.492 to 0.641), n 72 | 0.037 | +-0.104 | within noise |  |
| Leader at half time wins | 0.606 (0.489 to 0.711), n 71 | 0.458 (0.348 to 0.573), n 72 | -0.147 | +-0.158 | within noise | 0.600 to 0.800: met / missed |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.950 (0.946 to 0.954), n 72 | 0.952 (0.948 to 0.957), n 72 | 0.002 | +-0.006 | within noise |  |
| Longest unbroken hill hold | 108.8 s (96.83 to 124.9), n 72 | 108.1 s (94.10 to 123.4), n 72 | -0.683 | +-20.28 | within noise |  |
| Warrior: dead or getting up | 0.109 (0.107 to 0.112), n 72 | 0.107 (0.106 to 0.109), n 72 | -0.002 | +-0.003 | within noise |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.315 (0.310 to 0.319), n 72 | 0.317 (0.313 to 0.322), n 72 | 0.003 | +-0.006 | within noise |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.339 (0.334 to 0.343), n 72 | 0.339 (0.335 to 0.343), n 72 | 0.001 | +-0.006 | within noise |  |
| Warrior: dead or walking | 0.424 (0.419 to 0.429), n 72 | 0.425 (0.419 to 0.430), n 72 | 0.001 | +-0.008 | within noise |  |
| Warrior: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.250 (0.246 to 0.254), n 72 | 0.254 (0.250 to 0.258), n 72 | 0.003 | +-0.006 | within noise |  |
| Warrior: dead or walking back after a respawn | 0.337 (0.332 to 0.343), n 72 | 0.338 (0.333 to 0.343), n 72 | 0.001 | +-0.007 | within noise |  |
| Farmer: dead or getting up | 0.072 (0.069 to 0.075), n 72 | 0.069 (0.067 to 0.071), n 72 | -0.003 | +-0.003 | within noise |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.308 (0.302 to 0.315), n 72 | 0.306 (0.299 to 0.313), n 72 | -0.002 | +-0.010 | within noise |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.271 (0.266 to 0.275), n 72 | 0.268 (0.264 to 0.272), n 72 | -0.002 | +-0.006 | within noise |  |
| Farmer: dead or walking | 0.380 (0.372 to 0.389), n 72 | 0.375 (0.366 to 0.384), n 72 | -0.005 | +-0.012 | within noise |  |
| Farmer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.152 (0.148 to 0.156), n 72 | 0.151 (0.147 to 0.155), n 72 | -0.001 | +-0.006 | within noise |  |
| Farmer: dead or walking back after a respawn | 0.210 (0.204 to 0.216), n 72 | 0.206 (0.201 to 0.212), n 72 | -0.004 | +-0.008 | within noise |  |
| Miner: dead or getting up | 0.070 (0.067 to 0.072), n 72 | 0.067 (0.065 to 0.069), n 72 | -0.003 | +-0.004 | within noise |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.388 (0.384 to 0.392), n 72 | 0.393 (0.389 to 0.397), n 72 | 0.005 | +-0.006 | within noise |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.159 (0.154 to 0.164), n 72 | 0.163 (0.158 to 0.167), n 72 | 0.004 | +-0.007 | within noise |  |
| Miner: dead or walking | 0.457 (0.452 to 0.462), n 72 | 0.460 (0.455 to 0.465), n 72 | 0.003 | +-0.007 | within noise |  |
| Miner: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.152 (0.146 to 0.157), n 72 | 0.131 (0.127 to 0.136), n 72 | -0.020 | +-0.007 | **OUTSIDE noise** |  |
| Miner: dead or walking back after a respawn | 0.208 (0.201 to 0.215), n 72 | 0.185 (0.179 to 0.191), n 72 | -0.023 | +-0.009 | **OUTSIDE noise** |  |
| Demolitions: dead or getting up | 0.111 (0.108 to 0.114), n 72 | 0.107 (0.105 to 0.109), n 72 | -0.004 | +-0.003 | **OUTSIDE noise** |  |
| Demolitions: walking (moving, not fighting, empty-handed, off the hill) | 0.319 (0.314 to 0.324), n 72 | 0.322 (0.317 to 0.327), n 72 | 0.003 | +-0.007 | within noise |  |
| Demolitions: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.355 (0.351 to 0.359), n 72 | 0.354 (0.351 to 0.358), n 72 | -0.001 | +-0.005 | within noise |  |
| Demolitions: dead or walking | 0.430 (0.424 to 0.436), n 72 | 0.429 (0.423 to 0.435), n 72 | -0.001 | +-0.009 | within noise |  |
| Demolitions: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.227 (0.223 to 0.232), n 72 | 0.227 (0.222 to 0.232), n 72 | -0.000 | +-0.007 | within noise |  |
| Demolitions: dead or walking back after a respawn | 0.316 (0.310 to 0.322), n 72 | 0.312 (0.306 to 0.318), n 72 | -0.004 | +-0.009 | within noise |  |
| Builder: dead or getting up | 0.078 (0.075 to 0.081), n 72 | 0.074 (0.071 to 0.077), n 72 | -0.004 | +-0.004 | within noise |  |
| Builder: walking (moving, not fighting, empty-handed, off the hill) | 0.341 (0.328 to 0.354), n 72 | 0.340 (0.327 to 0.353), n 72 | -0.001 | +-0.018 | within noise |  |
| Builder: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.165 (0.158 to 0.172), n 72 | 0.167 (0.159 to 0.175), n 72 | 0.002 | +-0.010 | within noise |  |
| Builder: dead or walking | 0.419 (0.404 to 0.433), n 72 | 0.414 (0.401 to 0.428), n 72 | -0.005 | +-0.020 | within noise |  |
| Builder: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.158 (0.150 to 0.166), n 72 | 0.155 (0.148 to 0.163), n 72 | -0.003 | +-0.011 | within noise |  |
| Builder: dead or walking back after a respawn | 0.221 (0.211 to 0.232), n 72 | 0.215 (0.206 to 0.225), n 72 | -0.006 | +-0.014 | within noise |  |
| Healer: dead or getting up | 0.142 (0.139 to 0.145), n 72 | 0.138 (0.136 to 0.141), n 72 | -0.003 | +-0.004 | within noise |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.392 (0.386 to 0.397), n 72 | 0.396 (0.391 to 0.401), n 72 | 0.004 | +-0.007 | within noise |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.216 (0.214 to 0.218), n 72 | 0.216 (0.214 to 0.218), n 72 | -0.000 | +-0.003 | within noise |  |
| Healer: dead or walking | 0.533 (0.526 to 0.540), n 72 | 0.534 (0.528 to 0.541), n 72 | 0.001 | +-0.009 | within noise |  |
| Healer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.326 (0.320 to 0.331), n 72 | 0.328 (0.323 to 0.333), n 72 | 0.002 | +-0.008 | within noise |  |
| Healer: dead or walking back after a respawn | 0.438 (0.431 to 0.445), n 72 | 0.436 (0.430 to 0.443), n 72 | -0.001 | +-0.010 | within noise |  |
| all: dead or getting up | 0.097 (0.095 to 0.100), n 72 | 0.094 (0.093 to 0.096), n 72 | -0.003 | +-0.003 | **OUTSIDE noise** |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.334 (0.330 to 0.338), n 72 | 0.336 (0.332 to 0.339), n 72 | 0.002 | +-0.005 | within noise |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.275 (0.272 to 0.278), n 72 | 0.275 (0.273 to 0.277), n 72 | 0.000 | +-0.003 | within noise |  |
| all: dead or walking | 0.431 (0.426 to 0.436), n 72 | 0.430 (0.426 to 0.435), n 72 | -0.001 | +-0.007 | within noise |  |
| all: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.214 (0.211 to 0.217), n 72 | 0.213 (0.210 to 0.216), n 72 | -0.002 | +-0.004 | within noise |  |
| all: dead or walking back after a respawn | 0.292 (0.288 to 0.296), n 72 | 0.287 (0.284 to 0.291), n 72 | -0.005 | +-0.006 | within noise | <= 0.280: missed / missed |
| Warrior: damage dealt to fighters | 2689 HP (2648 to 2729), n 72 | 2696 HP (2662 to 2730), n 72 | 7.42 | +-52.99 | within noise |  |
| Warrior: kills | 14.88 (14.52 to 15.23), n 72 | 14.60 (14.28 to 14.92), n 72 | -0.278 | +-0.477 | within noise |  |
| Warrior: deaths | 13.53 (13.30 to 13.77), n 72 | 13.63 (13.43 to 13.83), n 72 | 0.097 | +-0.309 | within noise |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 169.9 s (167.8 to 172.0), n 72 | 169.8 s (167.5 to 172.1), n 72 | -0.136 | +-3.11 | within noise |  |
| Warrior: units delivered to the egg | 0.063 (0.030 to 0.095), n 72 | 0.078 (0.043 to 0.113), n 72 | 0.015 | +-0.048 | within noise |  |
| Warrior: damage dealt to the Kaiju | 134.3 HP (118.1 to 150.5), n 72 | 138.7 HP (123.6 to 153.7), n 72 | 4.41 | +-22.13 | within noise |  |
| Warrior: share of all kills (whole class) | 0.413 (0.405 to 0.420), n 72 | 0.404 (0.397 to 0.411), n 72 | -0.009 | +-0.010 | within noise |  |
| Warrior: share of all fighter damage (whole class) | 0.428 (0.424 to 0.433), n 72 | 0.429 (0.425 to 0.434), n 72 | 0.001 | +-0.006 | within noise | <= 0.400: missed / missed |
| Farmer: damage dealt to fighters | 1909 HP (1867 to 1950), n 72 | 1895 HP (1849 to 1940), n 72 | -13.73 | +-61.29 | within noise |  |
| Farmer: kills | 11.85 (11.52 to 12.18), n 72 | 12.06 (11.71 to 12.40), n 72 | 0.212 | +-0.476 | within noise |  |
| Farmer: deaths | 8.41 (8.15 to 8.68), n 72 | 8.35 (8.12 to 8.58), n 72 | -0.064 | +-0.351 | within noise |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 82.48 s (79.90 to 85.05), n 72 | 86.05 s (83.28 to 88.83), n 72 | 3.58 | +-3.78 | within noise |  |
| Farmer: units delivered to the egg | 15.22 (14.94 to 15.50), n 72 | 14.81 (14.59 to 15.02), n 72 | -0.415 | +-0.354 | **OUTSIDE noise** |  |
| Farmer: damage dealt to the Kaiju | 829.8 HP (779.0 to 880.5), n 72 | 811.6 HP (759.8 to 863.4), n 72 | -18.19 | +-72.49 | within noise |  |
| Farmer: share of all kills (whole class) | 0.263 (0.257 to 0.270), n 72 | 0.267 (0.260 to 0.273), n 72 | 0.003 | +-0.009 | within noise |  |
| Farmer: share of all fighter damage (whole class) | 0.243 (0.239 to 0.248), n 72 | 0.241 (0.237 to 0.245), n 72 | -0.002 | +-0.006 | within noise |  |
| Miner: damage dealt to fighters | 1211 HP (1169 to 1252), n 72 | 1256 HP (1209 to 1302), n 72 | 45.01 | +-62.17 | within noise |  |
| Miner: kills | 8.32 (7.94 to 8.70), n 72 | 8.81 (8.38 to 9.23), n 72 | 0.486 | +-0.566 | within noise |  |
| Miner: deaths | 7.95 (7.69 to 8.22), n 72 | 7.91 (7.64 to 8.18), n 72 | -0.045 | +-0.378 | within noise |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 63.50 s (60.51 to 66.50), n 72 | 66.13 s (63.61 to 68.64), n 72 | 2.62 | +-3.91 | within noise |  |
| Miner: units delivered to the egg | 0.455 (0.336 to 0.573), n 72 | 0.458 (0.344 to 0.572), n 72 | 0.003 | +-0.164 | within noise |  |
| Miner: damage dealt to the Kaiju | 182.2 HP (156.3 to 208.0), n 72 | 174.3 HP (151.6 to 197.0), n 72 | -7.85 | +-34.39 | within noise |  |
| Miner: share of all kills (whole class) | 0.092 (0.088 to 0.097), n 72 | 0.097 (0.093 to 0.102), n 72 | 0.005 | +-0.006 | within noise |  |
| Miner: share of all fighter damage (whole class) | 0.077 (0.075 to 0.080), n 72 | 0.080 (0.077 to 0.083), n 72 | 0.003 | +-0.004 | within noise |  |
| Demolitions: damage dealt to fighters | 3298 HP (3236 to 3360), n 72 | 3292 HP (3237 to 3346), n 72 | -6.05 | +-82.78 | within noise |  |
| Demolitions: kills | 17.43 (16.77 to 18.09), n 72 | 17.39 (16.79 to 17.99), n 72 | -0.038 | +-0.888 | within noise |  |
| Demolitions: deaths | 13.67 (13.39 to 13.95), n 72 | 13.58 (13.33 to 13.83), n 72 | -0.097 | +-0.376 | within noise |  |
| Demolitions: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Demolitions: objective credit (hill seconds scored, Actor.HillSeconds) | 149.3 s (146.2 to 152.3), n 72 | 149.4 s (146.0 to 152.8), n 72 | 0.116 | +-4.58 | within noise |  |
| Demolitions: units delivered to the egg | 0.417 (0.329 to 0.504), n 72 | 0.368 (0.281 to 0.455), n 72 | -0.049 | +-0.123 | within noise |  |
| Demolitions: damage dealt to the Kaiju | 190.8 HP (168.4 to 213.1), n 72 | 186.5 HP (167.4 to 205.6), n 72 | -4.24 | +-29.40 | within noise |  |
| Demolitions: share of all kills (whole class) | 0.193 (0.187 to 0.200), n 72 | 0.192 (0.186 to 0.198), n 72 | -0.001 | +-0.009 | within noise |  |
| Demolitions: share of all fighter damage (whole class) | 0.210 (0.207 to 0.213), n 72 | 0.210 (0.206 to 0.213), n 72 | -0.001 | +-0.005 | within noise |  |
| Builder: damage dealt to fighters | 1123 HP (1061 to 1184), n 72 | 1118 HP (1055 to 1181), n 72 | -4.96 | +-88.32 | within noise |  |
| Builder: kills | 6.77 (6.16 to 7.38), n 72 | 6.99 (6.43 to 7.55), n 72 | 0.215 | +-0.826 | within noise |  |
| Builder: deaths | 8.77 (8.38 to 9.16), n 72 | 8.64 (8.26 to 9.02), n 72 | -0.132 | +-0.546 | within noise |  |
| Builder: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Builder: objective credit (hill seconds scored, Actor.HillSeconds) | 94.47 s (90.77 to 98.16), n 72 | 95.99 s (92.43 to 99.54), n 72 | 1.52 | +-5.13 | within noise |  |
| Builder: units delivered to the egg | 4.33 (3.39 to 5.28), n 72 | 4.45 (3.50 to 5.41), n 72 | 0.118 | +-1.34 | within noise |  |
| Builder: damage dealt to the Kaiju | 183.1 HP (160.6 to 205.7), n 72 | 187.3 HP (163.6 to 211.0), n 72 | 4.15 | +-32.74 | within noise |  |
| Builder: share of all kills (whole class) | 0.037 (0.034 to 0.041), n 72 | 0.039 (0.036 to 0.041), n 72 | 0.001 | +-0.004 | within noise |  |
| Builder: share of all fighter damage (whole class) | 0.036 (0.034 to 0.037), n 72 | 0.035 (0.034 to 0.037), n 72 | -0.000 | +-0.003 | within noise |  |
| Healer: damage dealt to fighters | 78.77 HP (72.29 to 85.26), n 72 | 75.76 HP (69.29 to 82.22), n 72 | -3.02 | +-9.16 | within noise |  |
| Healer: kills | 0.063 (0.032 to 0.093), n 72 | 0.118 (0.079 to 0.157), n 72 | 0.056 | +-0.049 | **OUTSIDE noise** |  |
| Healer: deaths | 17.96 (17.66 to 18.26), n 72 | 18.02 (17.73 to 18.32), n 72 | 0.063 | +-0.423 | within noise |  |
| Healer: healing done (Healer mends and pulses) | 4897 HP (4826 to 4968), n 72 | 4899 HP (4819 to 4980), n 72 | 1.84 | +-107.3 | within noise |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 133.3 s (129.7 to 136.8), n 72 | 133.8 s (130.2 to 137.4), n 72 | 0.537 | +-5.08 | within noise |  |
| Healer: units delivered to the egg | 0.031 (-0.005 to 0.067), n 72 | 0.021 (-0.000 to 0.042), n 72 | -0.010 | +-0.042 | within noise |  |
| Healer: damage dealt to the Kaiju | 8.23 HP (4.99 to 11.47), n 72 | 6.12 HP (3.77 to 8.47), n 72 | -2.11 | +-4.00 | within noise |  |
| Healer: share of all kills (whole class) | 0.001 (0.000 to 0.001), n 72 | 0.001 (0.001 to 0.002), n 72 | 0.001 | +-0.001 | **OUTSIDE noise** |  |
| Healer: share of all fighter damage (whole class) | 0.005 (0.005 to 0.005), n 72 | 0.005 (0.004 to 0.005), n 72 | -0.000 | +-0.001 | within noise |  |
| Top-10 share of all fighter damage (10 of 32 bodies) | 0.501 (0.497 to 0.504), n 72 | 0.498 (0.494 to 0.502), n 72 | -0.002 | +-0.005 | within noise | <= 0.500: missed / met |
| Top-10 share of all kills | 0.546 (0.539 to 0.553), n 72 | 0.540 (0.531 to 0.549), n 72 | -0.006 | +-0.011 | within noise |  |
| Time to kill: Warrior on a passive Warrior | 4.93 s (4.93 to 4.93), n 8 | 4.93 s (4.93 to 4.93), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Farmer | 4.03 s (4.03 to 4.03), n 8 | 4.03 s (4.03 to 4.03), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Miner | 4.03 s (4.03 to 4.03), n 8 | 4.03 s (4.03 to 4.03), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Demolitions | 4.93 s (4.93 to 4.93), n 8 | 4.93 s (4.93 to 4.93), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Builder | 4.93 s (4.93 to 4.93), n 8 | 4.93 s (4.93 to 4.93), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Healer | 4.93 s (4.93 to 4.93), n 8 | 4.93 s (4.93 to 4.93), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Warrior | 2.97 s (2.97 to 2.97), n 8 | 2.97 s (2.97 to 2.97), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Farmer | 2.97 s (2.97 to 2.97), n 8 | 2.97 s (2.97 to 2.97), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Miner | 2.97 s (2.97 to 2.97), n 8 | 2.97 s (2.97 to 2.97), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Demolitions | 3.63 s (3.63 to 3.63), n 8 | 3.63 s (3.63 to 3.63), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Builder | 3.63 s (3.63 to 3.63), n 8 | 3.63 s (3.63 to 3.63), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Healer | 2.97 s (2.97 to 2.97), n 8 | 2.97 s (2.97 to 2.97), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Warrior | 3.57 s (3.57 to 3.57), n 8 | 3.57 s (3.57 to 3.57), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Farmer | 4.23 s (4.23 to 4.23), n 8 | 4.23 s (4.23 to 4.23), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Miner | 4.23 s (4.23 to 4.23), n 8 | 4.23 s (4.23 to 4.23), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Demolitions | 4.90 s (4.90 to 4.90), n 8 | 4.90 s (4.90 to 4.90), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Builder | 3.57 s (3.57 to 3.57), n 8 | 3.57 s (3.57 to 3.57), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Healer | 4.23 s (4.23 to 4.23), n 8 | 4.23 s (4.23 to 4.23), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Warrior | 5.13 s (5.13 to 5.13), n 8 | 5.13 s (5.13 to 5.13), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Farmer | 4.43 s (4.43 to 4.43), n 8 | 4.43 s (4.43 to 4.43), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Miner | 3.73 s (3.73 to 3.73), n 8 | 3.73 s (3.73 to 3.73), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Demolitions | 5.13 s (5.13 to 5.13), n 8 | 5.13 s (5.13 to 5.13), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Builder | 5.13 s (5.13 to 5.13), n 8 | 5.13 s (5.13 to 5.13), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Healer | 4.43 s (4.43 to 4.43), n 8 | 4.43 s (4.43 to 4.43), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Warrior | 3.97 s (3.97 to 3.97), n 8 | 3.97 s (3.97 to 3.97), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Farmer | 3.07 s (3.07 to 3.07), n 8 | 3.07 s (3.07 to 3.07), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Miner | 3.07 s (3.07 to 3.07), n 8 | 3.07 s (3.07 to 3.07), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Demolitions | 3.97 s (3.97 to 3.97), n 8 | 3.97 s (3.97 to 3.97), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Builder | 3.97 s (3.97 to 3.97), n 8 | 3.97 s (3.97 to 3.97), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Healer | 3.97 s (3.97 to 3.97), n 8 | 3.97 s (3.97 to 3.97), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Warrior | 6.67 s (6.67 to 6.67), n 8 | 6.67 s (6.67 to 6.67), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Farmer | 5.87 s (5.87 to 5.87), n 8 | 5.87 s (5.87 to 5.87), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Miner | 5.87 s (5.87 to 5.87), n 8 | 5.87 s (5.87 to 5.87), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Demolitions | 6.67 s (6.67 to 6.67), n 8 | 6.67 s (6.67 to 6.67), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Builder | 6.67 s (6.67 to 6.67), n 8 | 6.67 s (6.67 to 6.67), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Healer | 6.67 s (6.67 to 6.67), n 8 | 6.67 s (6.67 to 6.67), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill, median over the matchups that can kill | 4.23 s (3.97 to 4.93), n 36 | 4.23 s (3.97 to 4.93), n 36 | 0.000 | +-0.684 | within noise | 3.00 to 6.00: met / met |
| Matchups whose time to kill is within 3 to 6 s | 28, n 36 | 28, n 36 | 0.000 |  | not judged |  |
| Duel: Warrior beats Farmer | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Warrior beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Warrior beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Warrior beats Builder | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Warrior beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Warrior | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Builder | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Warrior | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Farmer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Builder | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Warrior | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Farmer | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Builder | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Warrior | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Farmer | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Healer | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Warrior | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Farmer | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Demolitions | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Builder | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Classes that win every duel (more than half of each matchup) | 1, n 6 | 1, n 6 | 0.000 |  | not judged | <= 0.000: missed / missed |
| Matches where the egg hatched | 1.00 (0.949 to 1.000), n 72 | 1.00 (0.949 to 1.000), n 72 | 0.000 | +-0.036 | within noise |  |
| Elder share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| Grove share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| Brute share of hatches | 0.319 (0.223 to 0.434), n 72 | 0.333 (0.235 to 0.448), n 72 | 0.014 | +-0.150 | within noise |  |
| Timber share of hatches | 0.389 (0.285 to 0.504), n 72 | 0.403 (0.297 to 0.518), n 72 | 0.014 | +-0.156 | within noise |  |
| Mire share of hatches | 0.292 (0.199 to 0.405), n 72 | 0.264 (0.176 to 0.376), n 72 | -0.028 | +-0.143 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.741 (0.611 to 0.839), n 54 | 0.883 (0.778 to 0.942), n 60 | 0.143 | +-0.141 | **OUTSIDE noise** |  |
| Berserk share of hatches | 0.236 (0.153 to 0.346), n 72 | 0.167 (0.098 to 0.269), n 72 | -0.069 | +-0.129 | within noise |  |
| Hut level 1 reached at (team-matches that reached it) | 21.02 s (16.50 to 25.10), n 144 | 16.50 s (16.37 to 16.63), n 144 | -4.52 | +-4.30 | **OUTSIDE noise** |  |
| Hut level 2 reached at (team-matches that reached it) | 76.75 s (70.70 to 83.03), n 144 | 62.83 s (62.73 to 62.97), n 144 | -13.92 | +-6.17 | **OUTSIDE noise** |  |
| Hut level 3 reached at (team-matches that reached it) | 135.9 s (130.9 to 140.7), n 144 | 122.4 s (118.4 to 131.3), n 144 | -13.48 | +-8.15 | **OUTSIDE noise** |  |
| Hut level 4 reached at (team-matches that reached it) | 316.8 s (304.5 to 329.1), n 139 | 284.1 s (276.0 to 297.7), n 143 | -32.70 | +-16.37 | **OUTSIDE noise** |  |
| Hut level 5 reached at (team-matches that reached it) | 457.1 s (444.0 to 465.9), n 25 | 437.4 s (426.3 to 448.1), n 53 | -19.67 | +-15.49 | **OUTSIDE noise** |  |
| Team-matches reaching level 5 | 0.174 (0.120 to 0.244), n 144 | 0.368 (0.294 to 0.449), n 144 | 0.194 | +-0.099 | **OUTSIDE noise** |  |
| Builder walls built | 4.14 (3.51 to 4.77), n 72 | 3.26 (2.74 to 3.79), n 72 | -0.875 | +-0.824 | **OUTSIDE noise** |  |
| Builder wall cells broken or crumbled | 3.81 (3.19 to 4.43), n 72 | 3.06 (2.51 to 3.60), n 72 | -0.750 | +-0.824 | within noise |  |
| Ladders and bridges placed | 4.83 (4.32 to 5.34), n 72 | 5.26 (4.69 to 5.83), n 72 | 0.431 | +-0.765 | within noise |  |
| Ladders and bridges destroyed | 0.722 (0.539 to 0.905), n 72 | 0.806 (0.617 to 0.994), n 72 | 0.083 | +-0.263 | within noise |  |
| Barricades placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades broken | 12.63 (10.74 to 14.51), n 72 | 10.53 (8.75 to 12.31), n 72 | -2.10 | +-2.59 | within noise |  |
| Decorations toppled | 0.014 (-0.013 to 0.041), n 72 | 0.000 (0.000 to 0.000), n 72 | -0.014 | +-0.027 | within noise |  |
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 12.00 (10.00 to 14.00), n 72 | 4.00 (3.00 to 5.00), n 72 | -8.00 | +-2.24 | **OUTSIDE noise** | < 5.00: missed / met |
| Stuck episodes per match (mean) | 13.11 (11.27 to 14.95), n 72 | 7.89 (4.92 to 10.85), n 72 | -5.22 | +-3.49 | **OUTSIDE noise** |  |
| Sim tick cost, mean (wall clock, this machine) | 75.65 us (72.85 to 78.46), n 72 | 79.84 us (76.94 to 82.73), n 72 | 4.18 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 728.4 us (721.2 to 734.2), n 72 | 742.5 us (732.9 to 749.5), n 72 | 14.16 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
