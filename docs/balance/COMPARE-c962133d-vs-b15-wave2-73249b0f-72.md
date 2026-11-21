# Balance compare

- base: base c962133d38a66999102091f9f44ca90fc55a3b1c (c962133d, none (the game's values))
- head: head  (acc27a69, none (the game's values))
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

133 of 208 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.417 (0.310 to 0.532), n 72 | 0.500 (0.387 to 0.613), n 72 | 0.083 | +-0.158 | within noise |  |
| Crimson win share | 0.583 (0.468 to 0.690), n 72 | 0.486 (0.374 to 0.599), n 72 | -0.097 | +-0.158 | within noise |  |
| Draw share | 0.000 (0.000 to 0.051), n 72 | 0.014 (0.002 to 0.075), n 72 | 0.014 | +-0.044 | within noise |  |
| West spawn (pad x=8) win share | 0.417 (0.310 to 0.532), n 72 | 0.500 (0.387 to 0.613), n 72 | 0.083 | +-0.158 | within noise |  |
| East spawn (pad x=104) win share | 0.583 (0.468 to 0.690), n 72 | 0.486 (0.374 to 0.599), n 72 | -0.097 | +-0.158 | within noise |  |
| Match length | 480.0 s (480.0 to 480.0), n 72 | 480.0 s (480.0 to 480.0), n 72 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.528 (0.414 to 0.639), n 72 | 0.125 (0.067 to 0.221), n 72 | -0.403 | +-0.136 | **OUTSIDE noise** | <= 0.150: missed / met |
| Hill lead changes per match (median; spread = p25 to p75) | 2.00 (2.00 to 3.00), n 72 | 4.00 (3.00 to 5.00), n 72 | 2.00 | +-1.12 | **OUTSIDE noise** | >= 5.00: missed / missed |
| Hill lead changes per match (mean) | 2.65 (2.19 to 3.12), n 72 | 4.40 (3.75 to 5.06), n 72 | 1.75 | +-0.804 | **OUTSIDE noise** |  |
| Result decided at (last lead change) | 236.0 s (140.0 to 289.0), n 72 | 282.0 s (176.0 to 335.0), n 72 | 46.00 | +-109.0 | within noise |  |
| Result decided at, share of the match | 0.450 (0.375 to 0.525), n 72 | 0.529 (0.457 to 0.601), n 72 | 0.079 | +-0.104 | within noise |  |
| Leader at half time wins | 0.514 (0.401 to 0.626), n 72 | 0.606 (0.489 to 0.711), n 71 | 0.092 | +-0.158 | within noise | 0.600 to 0.800: missed / met |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.958 (0.954 to 0.961), n 72 | 0.950 (0.946 to 0.954), n 72 | -0.007 | +-0.006 | **OUTSIDE noise** |  |
| Longest unbroken hill hold | 166.4 s (123.9 to 186.4), n 72 | 108.8 s (96.83 to 124.9), n 72 | -57.67 | +-34.22 | **OUTSIDE noise** |  |
| Warrior: dead or getting up | 0.132 (0.130 to 0.134), n 72 | 0.109 (0.107 to 0.112), n 72 | -0.023 | +-0.003 | **OUTSIDE noise** |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.315 (0.311 to 0.319), n 72 | 0.315 (0.310 to 0.319), n 72 | -0.000 | +-0.006 | within noise |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.305 (0.301 to 0.308), n 72 | 0.339 (0.334 to 0.343), n 72 | 0.034 | +-0.006 | **OUTSIDE noise** |  |
| Warrior: dead or walking | 0.447 (0.442 to 0.452), n 72 | 0.424 (0.419 to 0.429), n 72 | -0.023 | +-0.008 | **OUTSIDE noise** |  |
| Warrior: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.246 (0.242 to 0.250), n 72 | 0.250 (0.246 to 0.254), n 72 | 0.005 | +-0.006 | within noise |  |
| Warrior: dead or walking back after a respawn | 0.359 (0.353 to 0.365), n 72 | 0.337 (0.332 to 0.343), n 72 | -0.022 | +-0.008 | **OUTSIDE noise** |  |
| Farmer: dead or getting up | 0.105 (0.103 to 0.108), n 72 | 0.072 (0.069 to 0.075), n 72 | -0.033 | +-0.004 | **OUTSIDE noise** |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.330 (0.323 to 0.336), n 72 | 0.308 (0.302 to 0.315), n 72 | -0.021 | +-0.009 | **OUTSIDE noise** |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.167 (0.164 to 0.169), n 72 | 0.271 (0.266 to 0.275), n 72 | 0.104 | +-0.005 | **OUTSIDE noise** |  |
| Farmer: dead or walking | 0.435 (0.427 to 0.443), n 72 | 0.380 (0.372 to 0.389), n 72 | -0.055 | +-0.012 | **OUTSIDE noise** |  |
| Farmer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.179 (0.174 to 0.183), n 72 | 0.152 (0.148 to 0.156), n 72 | -0.027 | +-0.006 | **OUTSIDE noise** |  |
| Farmer: dead or walking back after a respawn | 0.269 (0.263 to 0.275), n 72 | 0.210 (0.204 to 0.216), n 72 | -0.059 | +-0.009 | **OUTSIDE noise** |  |
| Miner: dead or getting up | 0.058 (0.056 to 0.059), n 72 | 0.070 (0.067 to 0.072), n 72 | 0.012 | +-0.003 | **OUTSIDE noise** |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.366 (0.363 to 0.370), n 72 | 0.388 (0.384 to 0.392), n 72 | 0.021 | +-0.005 | **OUTSIDE noise** |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.115 (0.112 to 0.119), n 72 | 0.159 (0.154 to 0.164), n 72 | 0.044 | +-0.006 | **OUTSIDE noise** |  |
| Miner: dead or walking | 0.424 (0.420 to 0.429), n 72 | 0.457 (0.452 to 0.462), n 72 | 0.033 | +-0.007 | **OUTSIDE noise** |  |
| Miner: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.087 (0.084 to 0.090), n 72 | 0.152 (0.146 to 0.157), n 72 | 0.065 | +-0.006 | **OUTSIDE noise** |  |
| Miner: dead or walking back after a respawn | 0.137 (0.133 to 0.141), n 72 | 0.208 (0.201 to 0.215), n 72 | 0.071 | +-0.008 | **OUTSIDE noise** |  |
| Demolitions: dead or getting up | 0.154 (0.152 to 0.157), n 72 | 0.111 (0.108 to 0.114), n 72 | -0.043 | +-0.004 | **OUTSIDE noise** |  |
| Demolitions: walking (moving, not fighting, empty-handed, off the hill) | 0.351 (0.348 to 0.355), n 72 | 0.319 (0.314 to 0.324), n 72 | -0.033 | +-0.006 | **OUTSIDE noise** |  |
| Demolitions: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.266 (0.263 to 0.269), n 72 | 0.355 (0.351 to 0.359), n 72 | 0.089 | +-0.005 | **OUTSIDE noise** |  |
| Demolitions: dead or walking | 0.506 (0.500 to 0.512), n 72 | 0.430 (0.424 to 0.436), n 72 | -0.076 | +-0.008 | **OUTSIDE noise** |  |
| Demolitions: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.255 (0.251 to 0.260), n 72 | 0.227 (0.223 to 0.232), n 72 | -0.028 | +-0.006 | **OUTSIDE noise** |  |
| Demolitions: dead or walking back after a respawn | 0.388 (0.382 to 0.395), n 72 | 0.316 (0.310 to 0.322), n 72 | -0.072 | +-0.009 | **OUTSIDE noise** |  |
| Builder: dead or getting up | 0.079 (0.075 to 0.083), n 72 | 0.078 (0.075 to 0.081), n 72 | -0.001 | +-0.005 | within noise |  |
| Builder: walking (moving, not fighting, empty-handed, off the hill) | 0.339 (0.326 to 0.352), n 72 | 0.341 (0.328 to 0.354), n 72 | 0.002 | +-0.018 | within noise |  |
| Builder: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.142 (0.136 to 0.148), n 72 | 0.165 (0.158 to 0.172), n 72 | 0.023 | +-0.009 | **OUTSIDE noise** |  |
| Builder: dead or walking | 0.418 (0.404 to 0.432), n 72 | 0.419 (0.404 to 0.433), n 72 | 0.001 | +-0.020 | within noise |  |
| Builder: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.141 (0.134 to 0.149), n 72 | 0.158 (0.150 to 0.166), n 72 | 0.017 | +-0.011 | **OUTSIDE noise** |  |
| Builder: dead or walking back after a respawn | 0.209 (0.199 to 0.220), n 72 | 0.221 (0.211 to 0.232), n 72 | 0.012 | +-0.015 | within noise |  |
| Healer: dead or getting up | 0.141 (0.138 to 0.144), n 72 | 0.142 (0.139 to 0.145), n 72 | 0.001 | +-0.004 | within noise |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.356 (0.350 to 0.362), n 72 | 0.392 (0.386 to 0.397), n 72 | 0.036 | +-0.008 | **OUTSIDE noise** |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.175 (0.174 to 0.177), n 72 | 0.216 (0.214 to 0.218), n 72 | 0.041 | +-0.003 | **OUTSIDE noise** |  |
| Healer: dead or walking | 0.497 (0.489 to 0.505), n 72 | 0.533 (0.526 to 0.540), n 72 | 0.036 | +-0.011 | **OUTSIDE noise** |  |
| Healer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.246 (0.241 to 0.251), n 72 | 0.326 (0.320 to 0.331), n 72 | 0.079 | +-0.007 | **OUTSIDE noise** |  |
| Healer: dead or walking back after a respawn | 0.368 (0.360 to 0.375), n 72 | 0.438 (0.431 to 0.445), n 72 | 0.070 | +-0.010 | **OUTSIDE noise** |  |
| all: dead or getting up | 0.117 (0.115 to 0.118), n 72 | 0.097 (0.095 to 0.100), n 72 | -0.019 | +-0.003 | **OUTSIDE noise** |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.336 (0.333 to 0.339), n 72 | 0.334 (0.330 to 0.338), n 72 | -0.002 | +-0.005 | within noise |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.215 (0.213 to 0.217), n 72 | 0.275 (0.272 to 0.278), n 72 | 0.060 | +-0.003 | **OUTSIDE noise** |  |
| all: dead or walking | 0.453 (0.449 to 0.457), n 72 | 0.431 (0.426 to 0.436), n 72 | -0.022 | +-0.007 | **OUTSIDE noise** |  |
| all: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.204 (0.201 to 0.207), n 72 | 0.214 (0.211 to 0.217), n 72 | 0.010 | +-0.004 | **OUTSIDE noise** |  |
| all: dead or walking back after a respawn | 0.304 (0.300 to 0.308), n 72 | 0.292 (0.288 to 0.296), n 72 | -0.012 | +-0.006 | **OUTSIDE noise** | <= 0.280: missed / missed |
| Warrior: damage dealt to fighters | 4074 HP (4008 to 4139), n 72 | 2689 HP (2648 to 2729), n 72 | -1385 | +-77.43 | **OUTSIDE noise** |  |
| Warrior: kills | 17.96 (17.62 to 18.31), n 72 | 14.88 (14.52 to 15.23), n 72 | -3.09 | +-0.493 | **OUTSIDE noise** |  |
| Warrior: deaths | 11.05 (10.87 to 11.24), n 72 | 13.53 (13.30 to 13.77), n 72 | 2.48 | +-0.303 | **OUTSIDE noise** |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 164.7 s (162.0 to 167.3), n 72 | 169.9 s (167.8 to 172.0), n 72 | 5.24 | +-3.39 | **OUTSIDE noise** |  |
| Warrior: units delivered to the egg | 0.064 (0.029 to 0.099), n 72 | 0.063 (0.030 to 0.095), n 72 | -0.001 | +-0.048 | within noise |  |
| Warrior: damage dealt to the Kaiju | 565.5 HP (524.4 to 606.6), n 72 | 134.3 HP (118.1 to 150.5), n 72 | -431.3 | +-44.20 | **OUTSIDE noise** |  |
| Warrior: share of all kills (whole class) | 0.604 (0.598 to 0.610), n 72 | 0.413 (0.405 to 0.420), n 72 | -0.191 | +-0.010 | **OUTSIDE noise** |  |
| Warrior: share of all fighter damage (whole class) | 0.609 (0.604 to 0.613), n 72 | 0.428 (0.424 to 0.433), n 72 | -0.180 | +-0.006 | **OUTSIDE noise** | <= 0.400: missed / missed |
| Farmer: damage dealt to fighters | 882.5 HP (851.5 to 913.6), n 72 | 1909 HP (1867 to 1950), n 72 | 1026 | +-51.57 | **OUTSIDE noise** |  |
| Farmer: kills | 4.36 (4.13 to 4.59), n 72 | 11.85 (11.52 to 12.18), n 72 | 7.48 | +-0.400 | **OUTSIDE noise** |  |
| Farmer: deaths | 8.84 (8.63 to 9.05), n 72 | 8.41 (8.15 to 8.68), n 72 | -0.424 | +-0.336 | **OUTSIDE noise** |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 88.97 s (86.27 to 91.68), n 72 | 82.48 s (79.90 to 85.05), n 72 | -6.50 | +-3.74 | **OUTSIDE noise** |  |
| Farmer: units delivered to the egg | 14.48 (14.23 to 14.72), n 72 | 15.22 (14.94 to 15.50), n 72 | 0.743 | +-0.373 | **OUTSIDE noise** |  |
| Farmer: damage dealt to the Kaiju | 301.5 HP (276.3 to 326.7), n 72 | 829.8 HP (779.0 to 880.5), n 72 | 528.3 | +-56.64 | **OUTSIDE noise** |  |
| Farmer: share of all kills (whole class) | 0.117 (0.112 to 0.122), n 72 | 0.263 (0.257 to 0.270), n 72 | 0.146 | +-0.008 | **OUTSIDE noise** |  |
| Farmer: share of all fighter damage (whole class) | 0.105 (0.102 to 0.108), n 72 | 0.243 (0.239 to 0.248), n 72 | 0.138 | +-0.005 | **OUTSIDE noise** |  |
| Miner: damage dealt to fighters | 966.6 HP (935.7 to 997.6), n 72 | 1211 HP (1169 to 1252), n 72 | 243.9 | +-51.78 | **OUTSIDE noise** |  |
| Miner: kills | 5.06 (4.77 to 5.34), n 72 | 8.32 (7.94 to 8.70), n 72 | 3.26 | +-0.471 | **OUTSIDE noise** |  |
| Miner: deaths | 4.86 (4.71 to 5.01), n 72 | 7.95 (7.69 to 8.22), n 72 | 3.09 | +-0.307 | **OUTSIDE noise** |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 54.17 s (52.11 to 56.23), n 72 | 63.50 s (60.51 to 66.50), n 72 | 9.33 | +-3.63 | **OUTSIDE noise** |  |
| Miner: units delivered to the egg | 0.566 (0.446 to 0.686), n 72 | 0.455 (0.336 to 0.573), n 72 | -0.111 | +-0.169 | within noise |  |
| Miner: damage dealt to the Kaiju | 231.1 HP (203.8 to 258.3), n 72 | 182.2 HP (156.3 to 208.0), n 72 | -48.89 | +-37.58 | **OUTSIDE noise** |  |
| Miner: share of all kills (whole class) | 0.068 (0.064 to 0.072), n 72 | 0.092 (0.088 to 0.097), n 72 | 0.024 | +-0.006 | **OUTSIDE noise** |  |
| Miner: share of all fighter damage (whole class) | 0.058 (0.056 to 0.060), n 72 | 0.077 (0.075 to 0.080), n 72 | 0.019 | +-0.003 | **OUTSIDE noise** |  |
| Demolitions: damage dealt to fighters | 3186 HP (3124 to 3248), n 72 | 3298 HP (3236 to 3360), n 72 | 112.1 | +-87.73 | **OUTSIDE noise** |  |
| Demolitions: kills | 12.97 (12.59 to 13.35), n 72 | 17.43 (16.77 to 18.09), n 72 | 4.46 | +-0.758 | **OUTSIDE noise** |  |
| Demolitions: deaths | 12.89 (12.67 to 13.12), n 72 | 13.67 (13.39 to 13.95), n 72 | 0.781 | +-0.359 | **OUTSIDE noise** |  |
| Demolitions: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Demolitions: objective credit (hill seconds scored, Actor.HillSeconds) | 141.8 s (138.6 to 145.0), n 72 | 149.3 s (146.2 to 152.3), n 72 | 7.45 | +-4.42 | **OUTSIDE noise** |  |
| Demolitions: units delivered to the egg | 0.347 (0.260 to 0.435), n 72 | 0.417 (0.329 to 0.504), n 72 | 0.069 | +-0.124 | within noise |  |
| Demolitions: damage dealt to the Kaiju | 134.5 HP (118.3 to 150.7), n 72 | 190.8 HP (168.4 to 213.1), n 72 | 56.28 | +-27.63 | **OUTSIDE noise** |  |
| Demolitions: share of all kills (whole class) | 0.175 (0.170 to 0.180), n 72 | 0.193 (0.187 to 0.200), n 72 | 0.018 | +-0.009 | **OUTSIDE noise** |  |
| Demolitions: share of all fighter damage (whole class) | 0.191 (0.187 to 0.194), n 72 | 0.210 (0.207 to 0.213), n 72 | 0.020 | +-0.005 | **OUTSIDE noise** |  |
| Builder: damage dealt to fighters | 1112 HP (1053 to 1170), n 72 | 1123 HP (1061 to 1184), n 72 | 11.32 | +-84.90 | within noise |  |
| Builder: kills | 5.23 (4.77 to 5.69), n 72 | 6.77 (6.16 to 7.38), n 72 | 1.54 | +-0.760 | **OUTSIDE noise** |  |
| Builder: deaths | 6.65 (6.33 to 6.97), n 72 | 8.77 (8.38 to 9.16), n 72 | 2.12 | +-0.506 | **OUTSIDE noise** |  |
| Builder: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Builder: objective credit (hill seconds scored, Actor.HillSeconds) | 90.96 s (88.01 to 93.91), n 72 | 94.47 s (90.77 to 98.16), n 72 | 3.50 | +-4.73 | within noise |  |
| Builder: units delivered to the egg | 4.79 (3.78 to 5.81), n 72 | 4.33 (3.39 to 5.28), n 72 | -0.458 | +-1.39 | within noise |  |
| Builder: damage dealt to the Kaiju | 338.8 HP (307.3 to 370.2), n 72 | 183.1 HP (160.6 to 205.7), n 72 | -155.6 | +-38.75 | **OUTSIDE noise** |  |
| Builder: share of all kills (whole class) | 0.035 (0.032 to 0.038), n 72 | 0.037 (0.034 to 0.041), n 72 | 0.002 | +-0.004 | within noise |  |
| Builder: share of all fighter damage (whole class) | 0.033 (0.031 to 0.035), n 72 | 0.036 (0.034 to 0.037), n 72 | 0.003 | +-0.002 | **OUTSIDE noise** |  |
| Healer: damage dealt to fighters | 77.74 HP (72.99 to 82.48), n 72 | 78.77 HP (72.29 to 85.26), n 72 | 1.04 | +-8.04 | within noise |  |
| Healer: kills | 0.073 (0.045 to 0.101), n 72 | 0.063 (0.032 to 0.093), n 72 | -0.010 | +-0.041 | within noise |  |
| Healer: deaths | 11.78 (11.53 to 12.03), n 72 | 17.96 (17.66 to 18.26), n 72 | 6.18 | +-0.394 | **OUTSIDE noise** |  |
| Healer: healing done (Healer mends and pulses) | 5226 HP (5132 to 5320), n 72 | 4897 HP (4826 to 4968), n 72 | -328.8 | +-118.0 | **OUTSIDE noise** |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 146.7 s (142.5 to 151.0), n 72 | 133.3 s (129.7 to 136.8), n 72 | -13.46 | +-5.54 | **OUTSIDE noise** |  |
| Healer: units delivered to the egg | 0.014 (-0.005 to 0.033), n 72 | 0.031 (-0.005 to 0.067), n 72 | 0.017 | +-0.041 | within noise |  |
| Healer: damage dealt to the Kaiju | 0.525 HP (0.261 to 0.789), n 72 | 8.23 HP (4.99 to 11.47), n 72 | 7.70 | +-3.25 | **OUTSIDE noise** |  |
| Healer: share of all kills (whole class) | 0.001 (0.001 to 0.001), n 72 | 0.001 (0.000 to 0.001), n 72 | -0.000 | +-0.001 | within noise |  |
| Healer: share of all fighter damage (whole class) | 0.005 (0.004 to 0.005), n 72 | 0.005 (0.005 to 0.005), n 72 | 0.000 | +-0.001 | within noise |  |
| Top-10 share of all fighter damage (10 of 32 bodies) | 0.624 (0.620 to 0.627), n 72 | 0.501 (0.497 to 0.504), n 72 | -0.123 | +-0.005 | **OUTSIDE noise** | <= 0.500: missed / missed |
| Top-10 share of all kills | 0.657 (0.651 to 0.663), n 72 | 0.546 (0.539 to 0.553), n 72 | -0.111 | +-0.009 | **OUTSIDE noise** |  |
| Time to kill: Warrior on a passive Warrior | 5.77 s (5.77 to 5.77), n 8 | 4.93 s (4.93 to 4.93), n 8 | -0.833 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Warrior on a passive Farmer | 3.67 s (3.67 to 3.67), n 8 | 4.03 s (4.03 to 4.03), n 8 | 0.367 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Warrior on a passive Miner | 4.37 s (4.37 to 4.37), n 8 | 4.03 s (4.03 to 4.03), n 8 | -0.333 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Warrior on a passive Demolitions | 4.37 s (4.37 to 4.37), n 8 | 4.93 s (4.93 to 4.93), n 8 | 0.567 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Warrior on a passive Builder | 4.37 s (4.37 to 4.37), n 8 | 4.93 s (4.93 to 4.93), n 8 | 0.567 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Warrior on a passive Healer | 3.67 s (3.67 to 3.67), n 8 | 4.93 s (4.93 to 4.93), n 8 | 1.27 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Warrior | 8.53 s (8.53 to 8.53), n 8 | 2.97 s (2.97 to 2.97), n 8 | -5.57 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Farmer | 5.53 s (5.53 to 5.53), n 8 | 2.97 s (2.97 to 2.97), n 8 | -2.57 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Miner | 6.13 s (6.13 to 6.13), n 8 | 2.97 s (2.97 to 2.97), n 8 | -3.17 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Demolitions | 6.13 s (6.13 to 6.13), n 8 | 3.63 s (3.63 to 3.63), n 8 | -2.50 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Builder | 6.13 s (6.13 to 6.13), n 8 | 3.63 s (3.63 to 3.63), n 8 | -2.50 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Healer | 6.13 s (6.13 to 6.13), n 8 | 2.97 s (2.97 to 2.97), n 8 | -3.17 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Warrior | 6.80 s (6.80 to 6.80), n 8 | 3.57 s (3.57 to 3.57), n 8 | -3.23 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Farmer | 4.80 s (4.80 to 4.80), n 8 | 4.23 s (4.23 to 4.23), n 8 | -0.567 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Miner | 5.47 s (5.47 to 5.47), n 8 | 4.23 s (4.23 to 4.23), n 8 | -1.23 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Demolitions | 5.47 s (5.47 to 5.47), n 8 | 4.90 s (4.90 to 4.90), n 8 | -0.567 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Builder | 5.47 s (5.47 to 5.47), n 8 | 3.57 s (3.57 to 3.57), n 8 | -1.90 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Healer | 4.80 s (4.80 to 4.80), n 8 | 4.23 s (4.23 to 4.23), n 8 | -0.567 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Warrior | 10.00 s (10.00 to 10.00), n 8 | 5.13 s (5.13 to 5.13), n 8 | -4.87 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Farmer | 5.40 s (5.40 to 5.40), n 8 | 4.43 s (4.43 to 4.43), n 8 | -0.967 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Miner | 6.93 s (6.93 to 6.93), n 8 | 3.73 s (3.73 to 3.73), n 8 | -3.20 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Demolitions | 6.93 s (6.93 to 6.93), n 8 | 5.13 s (5.13 to 5.13), n 8 | -1.80 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Builder | 6.93 s (6.93 to 6.93), n 8 | 5.13 s (5.13 to 5.13), n 8 | -1.80 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Healer | 6.17 s (6.17 to 6.17), n 8 | 4.43 s (4.43 to 4.43), n 8 | -1.73 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Builder on a passive Warrior | 6.80 s (6.80 to 6.80), n 8 | 3.97 s (3.97 to 3.97), n 8 | -2.83 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Builder on a passive Farmer | 4.80 s (4.80 to 4.80), n 8 | 3.07 s (3.07 to 3.07), n 8 | -1.73 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Builder on a passive Miner | 5.47 s (5.47 to 5.47), n 8 | 3.07 s (3.07 to 3.07), n 8 | -2.40 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Builder on a passive Demolitions | 5.47 s (5.47 to 5.47), n 8 | 3.97 s (3.97 to 3.97), n 8 | -1.50 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Builder on a passive Builder | 5.47 s (5.47 to 5.47), n 8 | 3.97 s (3.97 to 3.97), n 8 | -1.50 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Builder on a passive Healer | 4.80 s (4.80 to 4.80), n 8 | 3.97 s (3.97 to 3.97), n 8 | -0.833 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Healer on a passive Warrior | n/a, n 0 | 6.67 s (6.67 to 6.67), n 8 | n/a |  | no data |  |
| Time to kill: Healer on a passive Farmer | n/a, n 0 | 5.87 s (5.87 to 5.87), n 8 | n/a |  | no data |  |
| Time to kill: Healer on a passive Miner | n/a, n 0 | 5.87 s (5.87 to 5.87), n 8 | n/a |  | no data |  |
| Time to kill: Healer on a passive Demolitions | n/a, n 0 | 6.67 s (6.67 to 6.67), n 8 | n/a |  | no data |  |
| Time to kill: Healer on a passive Builder | n/a, n 0 | 6.67 s (6.67 to 6.67), n 8 | n/a |  | no data |  |
| Time to kill: Healer on a passive Healer | n/a, n 0 | 6.67 s (6.67 to 6.67), n 8 | n/a |  | no data |  |
| Time to kill, median over the matchups that can kill | 5.47 s (4.80 to 6.13), n 30 | 4.23 s (3.97 to 4.93), n 36 | -1.23 | +-0.823 | **OUTSIDE noise** | 3.00 to 6.00: met / met |
| Matchups whose time to kill is within 3 to 6 s | 18, n 30 | 28, n 36 | 10.00 |  | not judged |  |
| Duel: Warrior beats Farmer | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Warrior beats Miner | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Warrior beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Warrior beats Builder | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Warrior beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Warrior | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Farmer beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Demolitions | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Farmer beats Builder | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Farmer beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Warrior | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Miner beats Farmer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Builder | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Miner beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Warrior | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Farmer | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Demolitions beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Builder | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Warrior | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Builder beats Farmer | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Builder beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Healer | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Healer beats Warrior | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Farmer | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Demolitions | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Builder | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Classes that win every duel (more than half of each matchup) | 1, n 6 | 1, n 6 | 0.000 |  | not judged | <= 0.000: missed / missed |
| Matches where the egg hatched | 1.00 (0.949 to 1.000), n 72 | 1.00 (0.949 to 1.000), n 72 | 0.000 | +-0.036 | within noise |  |
| Elder share of hatches | 0.014 (0.002 to 0.075), n 72 | 0.000 (0.000 to 0.051), n 72 | -0.014 | +-0.044 | within noise |  |
| Grove share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| Brute share of hatches | 0.319 (0.223 to 0.434), n 72 | 0.319 (0.223 to 0.434), n 72 | 0.000 | +-0.149 | within noise |  |
| Timber share of hatches | 0.389 (0.285 to 0.504), n 72 | 0.389 (0.285 to 0.504), n 72 | 0.000 | +-0.155 | within noise |  |
| Mire share of hatches | 0.278 (0.188 to 0.390), n 72 | 0.292 (0.199 to 0.405), n 72 | 0.014 | +-0.144 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.894 (0.797 to 0.948), n 66 | 0.741 (0.611 to 0.839), n 54 | -0.153 | +-0.137 | **OUTSIDE noise** |  |
| Berserk share of hatches | 0.083 (0.039 to 0.170), n 72 | 0.236 (0.153 to 0.346), n 72 | 0.153 | +-0.117 | **OUTSIDE noise** |  |
| Hut level 1 reached at (team-matches that reached it) | 17.68 s (16.50 to 18.87), n 144 | 21.02 s (16.50 to 25.10), n 144 | 3.33 | +-4.46 | within noise |  |
| Hut level 2 reached at (team-matches that reached it) | 77.47 s (71.50 to 80.27), n 144 | 76.75 s (70.70 to 83.03), n 144 | -0.717 | +-7.57 | within noise |  |
| Hut level 3 reached at (team-matches that reached it) | 140.5 s (136.5 to 147.8), n 144 | 135.9 s (130.9 to 140.7), n 144 | -4.62 | +-7.51 | within noise |  |
| Hut level 4 reached at (team-matches that reached it) | 286.1 s (281.0 to 294.0), n 144 | 316.8 s (304.5 to 329.1), n 139 | 30.70 | +-13.87 | **OUTSIDE noise** |  |
| Hut level 5 reached at (team-matches that reached it) | 451.1 s (446.4 to 456.0), n 93 | 457.1 s (444.0 to 465.9), n 25 | 6.03 | +-11.98 | within noise |  |
| Team-matches reaching level 5 | 0.646 (0.565 to 0.719), n 144 | 0.174 (0.120 to 0.244), n 144 | -0.472 | +-0.099 | **OUTSIDE noise** |  |
| Builder walls built | 3.17 (2.67 to 3.66), n 72 | 4.14 (3.51 to 4.77), n 72 | 0.972 | +-0.803 | **OUTSIDE noise** |  |
| Builder wall cells broken or crumbled | 2.88 (2.38 to 3.37), n 72 | 3.81 (3.19 to 4.43), n 72 | 0.931 | +-0.791 | **OUTSIDE noise** |  |
| Ladders and bridges placed | 6.51 (5.98 to 7.05), n 72 | 4.83 (4.32 to 5.34), n 72 | -1.68 | +-0.740 | **OUTSIDE noise** |  |
| Ladders and bridges destroyed | 1.04 (0.783 to 1.30), n 72 | 0.722 (0.539 to 0.905), n 72 | -0.319 | +-0.317 | **OUTSIDE noise** |  |
| Barricades placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades broken | 9.57 (7.99 to 11.15), n 72 | 12.63 (10.74 to 14.51), n 72 | 3.06 | +-2.47 | **OUTSIDE noise** |  |
| Decorations toppled | 0.000 (0.000 to 0.000), n 72 | 0.014 (-0.013 to 0.041), n 72 | 0.014 | +-0.027 | within noise |  |
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 8.00 (6.00 to 9.00), n 72 | 12.00 (10.00 to 14.00), n 72 | 4.00 | +-2.50 | **OUTSIDE noise** | < 5.00: missed / missed |
| Stuck episodes per match (mean) | 9.19 (7.14 to 11.25), n 72 | 13.11 (11.27 to 14.95), n 72 | 3.92 | +-2.76 | **OUTSIDE noise** |  |
| Sim tick cost, mean (wall clock, this machine) | 73.93 us (71.55 to 76.32), n 72 | 75.96 us (72.57 to 79.36), n 72 | 2.03 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 738.0 us (731.5 to 739.1), n 72 | 727.7 us (722.4 to 734.9), n 72 | -10.30 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
