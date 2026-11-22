# Balance compare

- base: base aa2ecfef (aa2ecfef, none (the game's values))
- head: head HEAD (f17242a7, none (the game's values))
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

81 of 208 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.639 (0.524 to 0.740), n 72 | 0.639 (0.524 to 0.740), n 72 | 0.000 | +-0.153 | within noise |  |
| Crimson win share | 0.361 (0.260 to 0.476), n 72 | 0.361 (0.260 to 0.476), n 72 | 0.000 | +-0.153 | within noise | 0.400 to 0.600: missed / missed |
| Draw share | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| West spawn (pad x=8) win share | 0.639 (0.524 to 0.740), n 72 | 0.639 (0.524 to 0.740), n 72 | 0.000 | +-0.153 | within noise |  |
| East spawn (pad x=104) win share | 0.361 (0.260 to 0.476), n 72 | 0.361 (0.260 to 0.476), n 72 | 0.000 | +-0.153 | within noise |  |
| Match length | 480.0 s (480.0 to 480.0), n 72 | 480.0 s (480.0 to 480.0), n 72 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.125 (0.067 to 0.221), n 72 | 0.167 (0.098 to 0.269), n 72 | 0.042 | +-0.115 | within noise | <= 0.150: met / missed |
| Hill lead changes per match (median; spread = p25 to p75) | 4.00 (3.00 to 5.00), n 72 | 4.00 (3.00 to 5.00), n 72 | 0.000 | +-1.41 | within noise | >= 5.00: missed / missed |
| Hill lead changes per match (mean) | 4.29 (3.78 to 4.81), n 72 | 4.08 (3.48 to 4.68), n 72 | -0.208 | +-0.791 | within noise |  |
| Result decided at (last lead change) | 326.5 s (178.0 to 381.0), n 72 | 281.0 s (185.0 to 338.0), n 72 | -45.50 | +-127.1 | within noise |  |
| Result decided at, share of the match | 0.566 (0.492 to 0.641), n 72 | 0.526 (0.455 to 0.596), n 72 | -0.041 | +-0.102 | within noise |  |
| Leader at half time wins | 0.458 (0.348 to 0.573), n 72 | 0.583 (0.468 to 0.690), n 72 | 0.125 | +-0.158 | within noise | 0.600 to 0.800: missed / missed |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.952 (0.948 to 0.957), n 72 | 0.947 (0.943 to 0.951), n 72 | -0.005 | +-0.006 | within noise |  |
| Longest unbroken hill hold | 108.1 s (94.10 to 123.4), n 72 | 103.9 s (87.30 to 114.0), n 72 | -4.13 | +-19.82 | within noise |  |
| Warrior: dead or getting up | 0.107 (0.106 to 0.109), n 72 | 0.133 (0.131 to 0.136), n 72 | 0.026 | +-0.003 | **OUTSIDE noise** |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.317 (0.313 to 0.322), n 72 | 0.303 (0.299 to 0.307), n 72 | -0.014 | +-0.006 | **OUTSIDE noise** |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.339 (0.335 to 0.343), n 72 | 0.311 (0.308 to 0.315), n 72 | -0.028 | +-0.005 | **OUTSIDE noise** |  |
| Warrior: dead or walking | 0.425 (0.419 to 0.430), n 72 | 0.436 (0.432 to 0.441), n 72 | 0.012 | +-0.007 | **OUTSIDE noise** |  |
| Warrior: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.254 (0.250 to 0.258), n 72 | 0.235 (0.231 to 0.238), n 72 | -0.019 | +-0.005 | **OUTSIDE noise** |  |
| Warrior: dead or walking back after a respawn | 0.338 (0.333 to 0.343), n 72 | 0.349 (0.345 to 0.354), n 72 | 0.011 | +-0.007 | **OUTSIDE noise** |  |
| Farmer: dead or getting up | 0.069 (0.067 to 0.071), n 72 | 0.100 (0.097 to 0.102), n 72 | 0.031 | +-0.003 | **OUTSIDE noise** |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.306 (0.299 to 0.313), n 72 | 0.325 (0.320 to 0.331), n 72 | 0.019 | +-0.009 | **OUTSIDE noise** |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.268 (0.264 to 0.272), n 72 | 0.256 (0.252 to 0.260), n 72 | -0.012 | +-0.006 | **OUTSIDE noise** |  |
| Farmer: dead or walking | 0.375 (0.366 to 0.384), n 72 | 0.425 (0.419 to 0.432), n 72 | 0.050 | +-0.011 | **OUTSIDE noise** |  |
| Farmer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.151 (0.147 to 0.155), n 72 | 0.161 (0.157 to 0.164), n 72 | 0.010 | +-0.005 | **OUTSIDE noise** |  |
| Farmer: dead or walking back after a respawn | 0.206 (0.201 to 0.212), n 72 | 0.247 (0.242 to 0.253), n 72 | 0.041 | +-0.008 | **OUTSIDE noise** |  |
| Miner: dead or getting up | 0.067 (0.065 to 0.069), n 72 | 0.085 (0.082 to 0.088), n 72 | 0.018 | +-0.004 | **OUTSIDE noise** |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.393 (0.389 to 0.397), n 72 | 0.381 (0.377 to 0.386), n 72 | -0.012 | +-0.006 | **OUTSIDE noise** |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.163 (0.158 to 0.167), n 72 | 0.155 (0.150 to 0.160), n 72 | -0.008 | +-0.007 | **OUTSIDE noise** |  |
| Miner: dead or walking | 0.460 (0.455 to 0.465), n 72 | 0.466 (0.460 to 0.472), n 72 | 0.007 | +-0.008 | within noise |  |
| Miner: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.131 (0.127 to 0.136), n 72 | 0.122 (0.118 to 0.127), n 72 | -0.009 | +-0.006 | **OUTSIDE noise** |  |
| Miner: dead or walking back after a respawn | 0.185 (0.179 to 0.191), n 72 | 0.196 (0.190 to 0.203), n 72 | 0.011 | +-0.009 | **OUTSIDE noise** |  |
| Demolitions: dead or getting up | 0.107 (0.105 to 0.109), n 72 | 0.135 (0.132 to 0.138), n 72 | 0.028 | +-0.004 | **OUTSIDE noise** |  |
| Demolitions: walking (moving, not fighting, empty-handed, off the hill) | 0.322 (0.317 to 0.327), n 72 | 0.357 (0.351 to 0.362), n 72 | 0.034 | +-0.007 | **OUTSIDE noise** |  |
| Demolitions: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.354 (0.351 to 0.358), n 72 | 0.328 (0.323 to 0.332), n 72 | -0.027 | +-0.005 | **OUTSIDE noise** |  |
| Demolitions: dead or walking | 0.429 (0.423 to 0.435), n 72 | 0.491 (0.486 to 0.497), n 72 | 0.062 | +-0.008 | **OUTSIDE noise** |  |
| Demolitions: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.227 (0.222 to 0.232), n 72 | 0.216 (0.211 to 0.220), n 72 | -0.011 | +-0.007 | **OUTSIDE noise** |  |
| Demolitions: dead or walking back after a respawn | 0.312 (0.306 to 0.318), n 72 | 0.332 (0.326 to 0.338), n 72 | 0.020 | +-0.009 | **OUTSIDE noise** |  |
| Builder: dead or getting up | 0.074 (0.071 to 0.077), n 72 | 0.094 (0.090 to 0.098), n 72 | 0.020 | +-0.005 | **OUTSIDE noise** |  |
| Builder: walking (moving, not fighting, empty-handed, off the hill) | 0.340 (0.327 to 0.353), n 72 | 0.332 (0.319 to 0.344), n 72 | -0.008 | +-0.018 | within noise |  |
| Builder: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.167 (0.159 to 0.175), n 72 | 0.140 (0.133 to 0.148), n 72 | -0.027 | +-0.011 | **OUTSIDE noise** |  |
| Builder: dead or walking | 0.414 (0.401 to 0.428), n 72 | 0.425 (0.412 to 0.439), n 72 | 0.011 | +-0.019 | within noise |  |
| Builder: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.155 (0.148 to 0.163), n 72 | 0.142 (0.135 to 0.148), n 72 | -0.013 | +-0.010 | **OUTSIDE noise** |  |
| Builder: dead or walking back after a respawn | 0.215 (0.206 to 0.225), n 72 | 0.224 (0.215 to 0.233), n 72 | 0.009 | +-0.013 | within noise |  |
| Healer: dead or getting up | 0.138 (0.136 to 0.141), n 72 | 0.168 (0.165 to 0.171), n 72 | 0.030 | +-0.004 | **OUTSIDE noise** |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.396 (0.391 to 0.401), n 72 | 0.426 (0.421 to 0.431), n 72 | 0.030 | +-0.007 | **OUTSIDE noise** |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.216 (0.214 to 0.218), n 72 | 0.212 (0.210 to 0.214), n 72 | -0.004 | +-0.003 | **OUTSIDE noise** |  |
| Healer: dead or walking | 0.534 (0.528 to 0.541), n 72 | 0.594 (0.589 to 0.599), n 72 | 0.060 | +-0.008 | **OUTSIDE noise** |  |
| Healer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.328 (0.323 to 0.333), n 72 | 0.291 (0.286 to 0.295), n 72 | -0.037 | +-0.007 | **OUTSIDE noise** |  |
| Healer: dead or walking back after a respawn | 0.436 (0.430 to 0.443), n 72 | 0.434 (0.428 to 0.440), n 72 | -0.002 | +-0.009 | within noise |  |
| all: dead or getting up | 0.094 (0.093 to 0.096), n 72 | 0.121 (0.119 to 0.123), n 72 | 0.026 | +-0.003 | **OUTSIDE noise** |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.336 (0.332 to 0.339), n 72 | 0.342 (0.339 to 0.346), n 72 | 0.006 | +-0.005 | **OUTSIDE noise** |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.275 (0.273 to 0.277), n 72 | 0.257 (0.255 to 0.259), n 72 | -0.018 | +-0.003 | **OUTSIDE noise** |  |
| all: dead or walking | 0.430 (0.426 to 0.435), n 72 | 0.463 (0.460 to 0.467), n 72 | 0.033 | +-0.006 | **OUTSIDE noise** |  |
| all: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.213 (0.210 to 0.216), n 72 | 0.201 (0.198 to 0.204), n 72 | -0.011 | +-0.004 | **OUTSIDE noise** |  |
| all: dead or walking back after a respawn | 0.287 (0.284 to 0.291), n 72 | 0.305 (0.302 to 0.309), n 72 | 0.018 | +-0.005 | **OUTSIDE noise** | <= 0.280: missed / missed |
| Warrior: damage dealt to fighters | 2696 HP (2662 to 2730), n 72 | 2368 HP (2335 to 2402), n 72 | -327.6 | +-47.60 | **OUTSIDE noise** |  |
| Warrior: kills | 14.60 (14.28 to 14.92), n 72 | 12.66 (12.38 to 12.93), n 72 | -1.94 | +-0.423 | **OUTSIDE noise** |  |
| Warrior: deaths | 13.63 (13.43 to 13.83), n 72 | 11.52 (11.37 to 11.68), n 72 | -2.11 | +-0.253 | **OUTSIDE noise** |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 169.8 s (167.5 to 172.1), n 72 | 168.4 s (166.1 to 170.6), n 72 | -1.44 | +-3.19 | within noise |  |
| Warrior: units delivered to the egg | 0.078 (0.043 to 0.113), n 72 | 0.053 (0.024 to 0.082), n 72 | -0.025 | +-0.046 | within noise |  |
| Warrior: damage dealt to the Kaiju | 138.7 HP (123.6 to 153.7), n 72 | 101.7 HP (85.13 to 118.2), n 72 | -37.00 | +-22.38 | **OUTSIDE noise** |  |
| Warrior: share of all kills (whole class) | 0.404 (0.397 to 0.411), n 72 | 0.403 (0.395 to 0.410), n 72 | -0.001 | +-0.010 | within noise |  |
| Warrior: share of all fighter damage (whole class) | 0.429 (0.425 to 0.434), n 72 | 0.423 (0.419 to 0.427), n 72 | -0.006 | +-0.006 | **OUTSIDE noise** | <= 0.400: missed / missed |
| Farmer: damage dealt to fighters | 1895 HP (1849 to 1940), n 72 | 1761 HP (1725 to 1797), n 72 | -133.6 | +-57.88 | **OUTSIDE noise** |  |
| Farmer: kills | 12.06 (11.71 to 12.40), n 72 | 11.00 (10.71 to 11.30), n 72 | -1.06 | +-0.454 | **OUTSIDE noise** |  |
| Farmer: deaths | 8.35 (8.12 to 8.58), n 72 | 8.11 (7.90 to 8.31), n 72 | -0.245 | +-0.310 | within noise |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 86.05 s (83.28 to 88.83), n 72 | 54.21 s (51.84 to 56.58), n 72 | -31.84 | +-3.65 | **OUTSIDE noise** |  |
| Farmer: units delivered to the egg | 14.81 (14.59 to 15.02), n 72 | 14.95 (14.66 to 15.25), n 72 | 0.149 | +-0.368 | within noise |  |
| Farmer: damage dealt to the Kaiju | 811.6 HP (759.8 to 863.4), n 72 | 694.3 HP (641.9 to 746.8), n 72 | -117.2 | +-73.68 | **OUTSIDE noise** |  |
| Farmer: share of all kills (whole class) | 0.267 (0.260 to 0.273), n 72 | 0.280 (0.274 to 0.286), n 72 | 0.013 | +-0.009 | **OUTSIDE noise** |  |
| Farmer: share of all fighter damage (whole class) | 0.241 (0.237 to 0.245), n 72 | 0.251 (0.247 to 0.255), n 72 | 0.011 | +-0.006 | **OUTSIDE noise** |  |
| Miner: damage dealt to fighters | 1256 HP (1209 to 1302), n 72 | 1156 HP (1109 to 1203), n 72 | -99.41 | +-66.14 | **OUTSIDE noise** |  |
| Miner: kills | 8.81 (8.38 to 9.23), n 72 | 7.88 (7.47 to 8.30), n 72 | -0.924 | +-0.592 | **OUTSIDE noise** |  |
| Miner: deaths | 7.91 (7.64 to 8.18), n 72 | 6.70 (6.50 to 6.91), n 72 | -1.20 | +-0.338 | **OUTSIDE noise** |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 66.13 s (63.61 to 68.64), n 72 | 66.18 s (63.46 to 68.89), n 72 | 0.050 | +-3.70 | within noise |  |
| Miner: units delivered to the egg | 0.458 (0.344 to 0.572), n 72 | 0.413 (0.315 to 0.512), n 72 | -0.045 | +-0.151 | within noise |  |
| Miner: damage dealt to the Kaiju | 174.3 HP (151.6 to 197.0), n 72 | 141.0 HP (119.4 to 162.6), n 72 | -33.30 | +-31.32 | **OUTSIDE noise** |  |
| Miner: share of all kills (whole class) | 0.097 (0.093 to 0.102), n 72 | 0.100 (0.095 to 0.105), n 72 | 0.003 | +-0.007 | within noise |  |
| Miner: share of all fighter damage (whole class) | 0.080 (0.077 to 0.083), n 72 | 0.083 (0.079 to 0.086), n 72 | 0.003 | +-0.004 | within noise |  |
| Demolitions: damage dealt to fighters | 3292 HP (3237 to 3346), n 72 | 2870 HP (2815 to 2925), n 72 | -422.1 | +-77.51 | **OUTSIDE noise** |  |
| Demolitions: kills | 17.39 (16.79 to 17.99), n 72 | 14.31 (13.84 to 14.79), n 72 | -3.08 | +-0.763 | **OUTSIDE noise** |  |
| Demolitions: deaths | 13.58 (13.33 to 13.83), n 72 | 11.44 (11.23 to 11.66), n 72 | -2.14 | +-0.331 | **OUTSIDE noise** |  |
| Demolitions: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Demolitions: objective credit (hill seconds scored, Actor.HillSeconds) | 149.4 s (146.0 to 152.8), n 72 | 96.03 s (92.81 to 99.25), n 72 | -53.36 | +-4.71 | **OUTSIDE noise** |  |
| Demolitions: units delivered to the egg | 0.368 (0.281 to 0.455), n 72 | 0.750 (0.577 to 0.923), n 72 | 0.382 | +-0.193 | **OUTSIDE noise** |  |
| Demolitions: damage dealt to the Kaiju | 186.5 HP (167.4 to 205.6), n 72 | 119.5 HP (99.00 to 140.1), n 72 | -66.98 | +-28.02 | **OUTSIDE noise** |  |
| Demolitions: share of all kills (whole class) | 0.192 (0.186 to 0.198), n 72 | 0.182 (0.176 to 0.188), n 72 | -0.010 | +-0.008 | **OUTSIDE noise** |  |
| Demolitions: share of all fighter damage (whole class) | 0.210 (0.206 to 0.213), n 72 | 0.205 (0.202 to 0.208), n 72 | -0.005 | +-0.005 | within noise |  |
| Builder: damage dealt to fighters | 1118 HP (1055 to 1181), n 72 | 827.9 HP (773.0 to 882.9), n 72 | -290.0 | +-83.87 | **OUTSIDE noise** |  |
| Builder: kills | 6.99 (6.43 to 7.55), n 72 | 5.15 (4.63 to 5.68), n 72 | -1.83 | +-0.768 | **OUTSIDE noise** |  |
| Builder: deaths | 8.64 (8.26 to 9.02), n 72 | 7.13 (6.80 to 7.46), n 72 | -1.51 | +-0.501 | **OUTSIDE noise** |  |
| Builder: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Builder: objective credit (hill seconds scored, Actor.HillSeconds) | 95.99 s (92.43 to 99.54), n 72 | 95.51 s (92.03 to 98.99), n 72 | -0.478 | +-4.97 | within noise |  |
| Builder: units delivered to the egg | 4.45 (3.50 to 5.41), n 72 | 3.99 (3.17 to 4.82), n 72 | -0.458 | +-1.26 | within noise |  |
| Builder: damage dealt to the Kaiju | 187.3 HP (163.6 to 211.0), n 72 | 120.7 HP (97.55 to 143.9), n 72 | -66.58 | +-33.14 | **OUTSIDE noise** |  |
| Builder: share of all kills (whole class) | 0.039 (0.036 to 0.041), n 72 | 0.033 (0.029 to 0.036), n 72 | -0.006 | +-0.004 | **OUTSIDE noise** |  |
| Builder: share of all fighter damage (whole class) | 0.035 (0.034 to 0.037), n 72 | 0.029 (0.028 to 0.031), n 72 | -0.006 | +-0.003 | **OUTSIDE noise** |  |
| Healer: damage dealt to fighters | 75.76 HP (69.29 to 82.22), n 72 | 123.1 HP (114.7 to 131.6), n 72 | 47.39 | +-10.62 | **OUTSIDE noise** |  |
| Healer: kills | 0.118 (0.079 to 0.157), n 72 | 0.167 (0.120 to 0.213), n 72 | 0.049 | +-0.061 | within noise |  |
| Healer: deaths | 18.02 (17.73 to 18.32), n 72 | 14.74 (14.50 to 14.97), n 72 | -3.28 | +-0.375 | **OUTSIDE noise** |  |
| Healer: healing done (Healer mends and pulses) | 4899 HP (4819 to 4980), n 72 | 4571 HP (4500 to 4643), n 72 | -327.9 | +-107.4 | **OUTSIDE noise** |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 133.8 s (130.2 to 137.4), n 72 | 88.04 s (85.57 to 90.52), n 72 | -45.76 | +-4.37 | **OUTSIDE noise** |  |
| Healer: units delivered to the egg | 0.021 (-0.000 to 0.042), n 72 | 0.038 (0.004 to 0.073), n 72 | 0.017 | +-0.040 | within noise |  |
| Healer: damage dealt to the Kaiju | 6.12 HP (3.77 to 8.47), n 72 | 11.39 HP (7.52 to 15.27), n 72 | 5.27 | +-4.54 | **OUTSIDE noise** |  |
| Healer: share of all kills (whole class) | 0.001 (0.001 to 0.002), n 72 | 0.002 (0.002 to 0.003), n 72 | 0.001 | +-0.001 | **OUTSIDE noise** |  |
| Healer: share of all fighter damage (whole class) | 0.005 (0.004 to 0.005), n 72 | 0.009 (0.008 to 0.009), n 72 | 0.004 | +-0.001 | **OUTSIDE noise** |  |
| Top-10 share of all fighter damage (10 of 32 bodies) | 0.498 (0.494 to 0.502), n 72 | 0.500 (0.496 to 0.504), n 72 | 0.001 | +-0.006 | within noise | <= 0.500: met / met |
| Top-10 share of all kills | 0.540 (0.531 to 0.549), n 72 | 0.552 (0.544 to 0.561), n 72 | 0.012 | +-0.012 | **OUTSIDE noise** |  |
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
| Elder share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.139 (0.077 to 0.237), n 72 | 0.139 | +-0.084 | **OUTSIDE noise** |  |
| Grove share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.042 (0.014 to 0.115), n 72 | 0.042 | +-0.057 | within noise |  |
| Brute share of hatches | 0.333 (0.235 to 0.448), n 72 | 0.167 (0.098 to 0.269), n 72 | -0.167 | +-0.137 | **OUTSIDE noise** |  |
| Timber share of hatches | 0.403 (0.297 to 0.518), n 72 | 0.361 (0.260 to 0.476), n 72 | -0.042 | +-0.155 | within noise |  |
| Mire share of hatches | 0.264 (0.176 to 0.376), n 72 | 0.292 (0.199 to 0.405), n 72 | 0.028 | +-0.143 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.883 (0.778 to 0.942), n 60 | 0.833 (0.720 to 0.907), n 60 | -0.050 | +-0.124 | within noise | <= 0.700: missed / missed |
| Berserk share of hatches | 0.167 (0.098 to 0.269), n 72 | 0.167 (0.098 to 0.269), n 72 | 0.000 | +-0.121 | within noise |  |
| Hut level 1 reached at (team-matches that reached it) | 16.50 s (16.37 to 16.63), n 144 | 16.50 s (16.37 to 16.63), n 144 | 0.000 | +-0.189 | within noise |  |
| Hut level 2 reached at (team-matches that reached it) | 62.83 s (62.73 to 62.97), n 144 | 63.22 s (62.93 to 65.93), n 144 | 0.383 | +-1.50 | within noise |  |
| Hut level 3 reached at (team-matches that reached it) | 122.4 s (118.4 to 131.3), n 144 | 133.1 s (128.4 to 136.7), n 144 | 10.72 | +-7.72 | **OUTSIDE noise** |  |
| Hut level 4 reached at (team-matches that reached it) | 284.1 s (276.0 to 297.7), n 143 | 304.5 s (294.6 to 318.5), n 143 | 20.47 | +-16.14 | **OUTSIDE noise** |  |
| Hut level 5 reached at (team-matches that reached it) | 437.4 s (426.3 to 448.1), n 53 | 444.7 s (430.5 to 453.2), n 38 | 7.30 | +-15.75 | within noise |  |
| Team-matches reaching level 5 | 0.368 (0.294 to 0.449), n 144 | 0.264 (0.199 to 0.341), n 144 | -0.104 | +-0.106 | within noise |  |
| Builder walls built | 3.26 (2.74 to 3.79), n 72 | 1.54 (1.18 to 1.91), n 72 | -1.72 | +-0.639 | **OUTSIDE noise** |  |
| Builder wall cells broken or crumbled | 3.06 (2.51 to 3.60), n 72 | 1.50 (1.13 to 1.87), n 72 | -1.56 | +-0.657 | **OUTSIDE noise** |  |
| Ladders and bridges placed | 5.26 (4.69 to 5.83), n 72 | 6.46 (5.88 to 7.04), n 72 | 1.19 | +-0.814 | **OUTSIDE noise** |  |
| Ladders and bridges destroyed | 0.806 (0.617 to 0.994), n 72 | 0.958 (0.715 to 1.20), n 72 | 0.153 | +-0.308 | within noise |  |
| Barricades placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades broken | 10.53 (8.75 to 12.31), n 72 | 4.65 (3.50 to 5.80), n 72 | -5.88 | +-2.12 | **OUTSIDE noise** |  |
| Decorations toppled | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 4.00 (3.00 to 5.00), n 72 | 4.00 (3.00 to 6.00), n 72 | 0.000 | +-1.80 | within noise | < 5.00: met / met |
| Stuck episodes per match (mean) | 7.89 (4.92 to 10.85), n 72 | 5.36 (4.24 to 6.49), n 72 | -2.53 | +-3.17 | within noise |  |
| Sim tick cost, mean (wall clock, this machine) | 76.66 us (74.35 to 78.96), n 72 | 91.23 us (87.96 to 94.49), n 72 | 14.57 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 729.4 us (724.9 to 736.4), n 72 | 749.5 us (746.2 to 754.3), n 72 | 20.01 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
