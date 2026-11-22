# Balance compare

- base: base aa2ecfef (aa2ecfef, none (the game's values))
- head: head HEAD (19af4ffd, none (the game's values))
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

110 of 221 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.639 (0.524 to 0.740), n 72 | 0.583 (0.468 to 0.690), n 72 | -0.056 | +-0.155 | within noise |  |
| Crimson win share | 0.361 (0.260 to 0.476), n 72 | 0.417 (0.310 to 0.532), n 72 | 0.056 | +-0.155 | within noise | 0.400 to 0.600: missed / met |
| Draw share | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| West spawn (pad x=8) win share | 0.639 (0.524 to 0.740), n 72 | 0.583 (0.468 to 0.690), n 72 | -0.056 | +-0.155 | within noise |  |
| East spawn (pad x=104) win share | 0.361 (0.260 to 0.476), n 72 | 0.417 (0.310 to 0.532), n 72 | 0.056 | +-0.155 | within noise |  |
| Match length | 480.0 s (480.0 to 480.0), n 72 | 480.0 s (480.0 to 480.0), n 72 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.125 (0.067 to 0.221), n 72 | 0.181 (0.109 to 0.285), n 72 | 0.056 | +-0.117 | within noise | <= 0.150: met / missed |
| Hill lead changes per match (median; spread = p25 to p75) | 4.00 (3.00 to 5.00), n 72 | 3.00 (2.00 to 4.00), n 72 | -1.00 | +-1.41 | within noise | >= 5.00: missed / missed |
| Hill lead changes per match (mean) | 4.29 (3.78 to 4.81), n 72 | 3.42 (2.92 to 3.91), n 72 | -0.875 | +-0.717 | **OUTSIDE noise** |  |
| Result decided at (last lead change) | 326.5 s (178.0 to 381.0), n 72 | 260.0 s (152.0 to 316.0), n 72 | -66.50 | +-130.5 | within noise |  |
| Result decided at, share of the match | 0.566 (0.492 to 0.641), n 72 | 0.506 (0.433 to 0.578), n 72 | -0.061 | +-0.104 | within noise |  |
| Leader at half time wins | 0.458 (0.348 to 0.573), n 72 | 0.542 (0.427 to 0.652), n 72 | 0.083 | +-0.159 | within noise | 0.600 to 0.800: missed / missed |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.952 (0.948 to 0.957), n 72 | 0.942 (0.937 to 0.947), n 72 | -0.010 | +-0.007 | **OUTSIDE noise** |  |
| Longest unbroken hill hold | 108.1 s (94.10 to 123.4), n 72 | 101.7 s (93.53 to 120.7), n 72 | -6.38 | +-19.97 | within noise |  |
| Warrior: dead or getting up | 0.107 (0.106 to 0.109), n 72 | 0.128 (0.126 to 0.130), n 72 | 0.021 | +-0.003 | **OUTSIDE noise** |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.317 (0.313 to 0.322), n 72 | 0.295 (0.291 to 0.299), n 72 | -0.023 | +-0.006 | **OUTSIDE noise** |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.339 (0.335 to 0.343), n 72 | 0.330 (0.326 to 0.334), n 72 | -0.009 | +-0.005 | **OUTSIDE noise** |  |
| Warrior: dead or walking | 0.425 (0.419 to 0.430), n 72 | 0.423 (0.418 to 0.427), n 72 | -0.002 | +-0.007 | within noise |  |
| Warrior: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.254 (0.250 to 0.258), n 72 | 0.228 (0.225 to 0.231), n 72 | -0.026 | +-0.005 | **OUTSIDE noise** |  |
| Warrior: dead or walking back after a respawn | 0.338 (0.333 to 0.343), n 72 | 0.338 (0.334 to 0.342), n 72 | -0.000 | +-0.006 | within noise |  |
| Farmer: dead or getting up | 0.069 (0.067 to 0.071), n 72 | 0.098 (0.095 to 0.100), n 72 | 0.029 | +-0.003 | **OUTSIDE noise** |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.306 (0.299 to 0.313), n 72 | 0.320 (0.313 to 0.326), n 72 | 0.014 | +-0.009 | **OUTSIDE noise** |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.268 (0.264 to 0.272), n 72 | 0.265 (0.260 to 0.270), n 72 | -0.003 | +-0.006 | within noise |  |
| Farmer: dead or walking | 0.375 (0.366 to 0.384), n 72 | 0.418 (0.411 to 0.425), n 72 | 0.043 | +-0.011 | **OUTSIDE noise** |  |
| Farmer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.151 (0.147 to 0.155), n 72 | 0.153 (0.149 to 0.157), n 72 | 0.002 | +-0.005 | within noise |  |
| Farmer: dead or walking back after a respawn | 0.206 (0.201 to 0.212), n 72 | 0.238 (0.233 to 0.244), n 72 | 0.032 | +-0.008 | **OUTSIDE noise** |  |
| Miner: dead or getting up | 0.067 (0.065 to 0.069), n 72 | 0.087 (0.084 to 0.090), n 72 | 0.020 | +-0.004 | **OUTSIDE noise** |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.393 (0.389 to 0.397), n 72 | 0.378 (0.374 to 0.382), n 72 | -0.015 | +-0.006 | **OUTSIDE noise** |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.163 (0.158 to 0.167), n 72 | 0.162 (0.157 to 0.167), n 72 | -0.001 | +-0.006 | within noise |  |
| Miner: dead or walking | 0.460 (0.455 to 0.465), n 72 | 0.465 (0.460 to 0.470), n 72 | 0.005 | +-0.007 | within noise |  |
| Miner: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.131 (0.127 to 0.136), n 72 | 0.121 (0.117 to 0.125), n 72 | -0.010 | +-0.006 | **OUTSIDE noise** |  |
| Miner: dead or walking back after a respawn | 0.185 (0.179 to 0.191), n 72 | 0.197 (0.191 to 0.203), n 72 | 0.012 | +-0.009 | **OUTSIDE noise** |  |
| Demolitions: dead or getting up | 0.107 (0.105 to 0.109), n 72 | 0.133 (0.130 to 0.135), n 72 | 0.025 | +-0.003 | **OUTSIDE noise** |  |
| Demolitions: walking (moving, not fighting, empty-handed, off the hill) | 0.322 (0.317 to 0.327), n 72 | 0.348 (0.344 to 0.353), n 72 | 0.026 | +-0.007 | **OUTSIDE noise** |  |
| Demolitions: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.354 (0.351 to 0.358), n 72 | 0.335 (0.329 to 0.341), n 72 | -0.019 | +-0.006 | **OUTSIDE noise** |  |
| Demolitions: dead or walking | 0.429 (0.423 to 0.435), n 72 | 0.481 (0.476 to 0.486), n 72 | 0.051 | +-0.008 | **OUTSIDE noise** |  |
| Demolitions: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.227 (0.222 to 0.232), n 72 | 0.209 (0.205 to 0.214), n 72 | -0.018 | +-0.007 | **OUTSIDE noise** |  |
| Demolitions: dead or walking back after a respawn | 0.312 (0.306 to 0.318), n 72 | 0.323 (0.317 to 0.329), n 72 | 0.011 | +-0.009 | **OUTSIDE noise** |  |
| Builder: dead or getting up | 0.074 (0.071 to 0.077), n 72 | 0.092 (0.088 to 0.096), n 72 | 0.018 | +-0.005 | **OUTSIDE noise** |  |
| Builder: walking (moving, not fighting, empty-handed, off the hill) | 0.340 (0.327 to 0.353), n 72 | 0.322 (0.310 to 0.334), n 72 | -0.018 | +-0.017 | **OUTSIDE noise** |  |
| Builder: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.167 (0.159 to 0.175), n 72 | 0.153 (0.145 to 0.161), n 72 | -0.014 | +-0.011 | **OUTSIDE noise** |  |
| Builder: dead or walking | 0.414 (0.401 to 0.428), n 72 | 0.414 (0.402 to 0.426), n 72 | -0.000 | +-0.018 | within noise |  |
| Builder: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.155 (0.148 to 0.163), n 72 | 0.135 (0.128 to 0.142), n 72 | -0.020 | +-0.010 | **OUTSIDE noise** |  |
| Builder: dead or walking back after a respawn | 0.215 (0.206 to 0.225), n 72 | 0.216 (0.206 to 0.225), n 72 | 0.000 | +-0.014 | within noise |  |
| Healer: dead or getting up | 0.138 (0.136 to 0.141), n 72 | 0.172 (0.169 to 0.175), n 72 | 0.034 | +-0.004 | **OUTSIDE noise** |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.396 (0.391 to 0.401), n 72 | 0.424 (0.420 to 0.428), n 72 | 0.028 | +-0.006 | **OUTSIDE noise** |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.216 (0.214 to 0.218), n 72 | 0.206 (0.204 to 0.208), n 72 | -0.010 | +-0.003 | **OUTSIDE noise** |  |
| Healer: dead or walking | 0.534 (0.528 to 0.541), n 72 | 0.596 (0.592 to 0.601), n 72 | 0.062 | +-0.008 | **OUTSIDE noise** |  |
| Healer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.328 (0.323 to 0.333), n 72 | 0.294 (0.290 to 0.299), n 72 | -0.033 | +-0.007 | **OUTSIDE noise** |  |
| Healer: dead or walking back after a respawn | 0.436 (0.430 to 0.443), n 72 | 0.442 (0.435 to 0.448), n 72 | 0.005 | +-0.009 | within noise |  |
| all: dead or getting up | 0.094 (0.093 to 0.096), n 72 | 0.119 (0.117 to 0.121), n 72 | 0.025 | +-0.002 | **OUTSIDE noise** |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.336 (0.332 to 0.339), n 72 | 0.336 (0.333 to 0.339), n 72 | 0.000 | +-0.005 | within noise |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.275 (0.273 to 0.277), n 72 | 0.267 (0.265 to 0.269), n 72 | -0.008 | +-0.003 | **OUTSIDE noise** |  |
| all: dead or walking | 0.430 (0.426 to 0.435), n 72 | 0.455 (0.452 to 0.459), n 72 | 0.025 | +-0.006 | **OUTSIDE noise** |  |
| all: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.213 (0.210 to 0.216), n 72 | 0.196 (0.194 to 0.198), n 72 | -0.016 | +-0.004 | **OUTSIDE noise** |  |
| all: dead or walking back after a respawn | 0.287 (0.284 to 0.291), n 72 | 0.299 (0.296 to 0.302), n 72 | 0.011 | +-0.005 | **OUTSIDE noise** | <= 0.280: missed / missed |
| Warrior: damage dealt to fighters | 2696 HP (2662 to 2730), n 72 | 2438 HP (2402 to 2473), n 72 | -258.2 | +-48.98 | **OUTSIDE noise** |  |
| Warrior: kills | 14.60 (14.28 to 14.92), n 72 | 13.31 (13.01 to 13.61), n 72 | -1.29 | +-0.436 | **OUTSIDE noise** |  |
| Warrior: deaths | 13.63 (13.43 to 13.83), n 72 | 10.96 (10.83 to 11.09), n 72 | -2.67 | +-0.235 | **OUTSIDE noise** |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 169.8 s (167.5 to 172.1), n 72 | 169.4 s (167.0 to 171.8), n 72 | -0.402 | +-3.31 | within noise |  |
| Warrior: units delivered to the egg | 0.078 (0.043 to 0.113), n 72 | 0.039 (0.019 to 0.059), n 72 | -0.039 | +-0.041 | within noise |  |
| Warrior: damage dealt to the Kaiju | 138.7 HP (123.6 to 153.7), n 72 | 123.4 HP (101.7 to 145.1), n 72 | -15.28 | +-26.42 | within noise |  |
| Warrior: share of all kills (whole class) | 0.404 (0.397 to 0.411), n 72 | 0.431 (0.424 to 0.439), n 72 | 0.028 | +-0.011 | **OUTSIDE noise** |  |
| Warrior: share of all fighter damage (whole class) | 0.429 (0.425 to 0.434), n 72 | 0.440 (0.436 to 0.444), n 72 | 0.011 | +-0.006 | **OUTSIDE noise** | <= 0.400: missed / missed |
| Farmer: damage dealt to fighters | 1895 HP (1849 to 1940), n 72 | 1639 HP (1608 to 1671), n 72 | -255.6 | +-55.14 | **OUTSIDE noise** |  |
| Farmer: kills | 12.06 (11.71 to 12.40), n 72 | 10.07 (9.78 to 10.35), n 72 | -1.99 | +-0.447 | **OUTSIDE noise** |  |
| Farmer: deaths | 8.35 (8.12 to 8.58), n 72 | 7.82 (7.63 to 8.00), n 72 | -0.535 | +-0.297 | **OUTSIDE noise** |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 86.05 s (83.28 to 88.83), n 72 | 57.07 s (54.92 to 59.23), n 72 | -28.98 | +-3.51 | **OUTSIDE noise** |  |
| Farmer: units delivered to the egg | 14.81 (14.59 to 15.02), n 72 | 15.06 (14.81 to 15.32), n 72 | 0.259 | +-0.334 | within noise |  |
| Farmer: damage dealt to the Kaiju | 811.6 HP (759.8 to 863.4), n 72 | 692.6 HP (642.8 to 742.4), n 72 | -119.0 | +-71.86 | **OUTSIDE noise** |  |
| Farmer: share of all kills (whole class) | 0.267 (0.260 to 0.273), n 72 | 0.261 (0.254 to 0.269), n 72 | -0.005 | +-0.009 | within noise |  |
| Farmer: share of all fighter damage (whole class) | 0.241 (0.237 to 0.245), n 72 | 0.237 (0.232 to 0.241), n 72 | -0.004 | +-0.006 | within noise |  |
| Miner: damage dealt to fighters | 1256 HP (1209 to 1302), n 72 | 723.0 HP (693.7 to 752.2), n 72 | -532.6 | +-54.76 | **OUTSIDE noise** |  |
| Miner: kills | 8.81 (8.38 to 9.23), n 72 | 4.66 (4.40 to 4.92), n 72 | -4.14 | +-0.495 | **OUTSIDE noise** |  |
| Miner: deaths | 7.91 (7.64 to 8.18), n 72 | 6.84 (6.62 to 7.06), n 72 | -1.07 | +-0.345 | **OUTSIDE noise** |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 66.13 s (63.61 to 68.64), n 72 | 62.42 s (59.93 to 64.92), n 72 | -3.70 | +-3.55 | **OUTSIDE noise** |  |
| Miner: units delivered to the egg | 0.458 (0.344 to 0.572), n 72 | 0.427 (0.332 to 0.522), n 72 | -0.031 | +-0.148 | within noise |  |
| Miner: damage dealt to the Kaiju | 174.3 HP (151.6 to 197.0), n 72 | 149.2 HP (123.3 to 175.1), n 72 | -25.10 | +-34.43 | within noise |  |
| Miner: share of all kills (whole class) | 0.097 (0.093 to 0.102), n 72 | 0.061 (0.057 to 0.064), n 72 | -0.037 | +-0.006 | **OUTSIDE noise** |  |
| Miner: share of all fighter damage (whole class) | 0.080 (0.077 to 0.083), n 72 | 0.052 (0.050 to 0.054), n 72 | -0.028 | +-0.003 | **OUTSIDE noise** |  |
| Demolitions: damage dealt to fighters | 3292 HP (3237 to 3346), n 72 | 3105 HP (3040 to 3171), n 72 | -186.4 | +-85.28 | **OUTSIDE noise** |  |
| Demolitions: kills | 17.39 (16.79 to 17.99), n 72 | 15.64 (15.13 to 16.14), n 72 | -1.76 | +-0.781 | **OUTSIDE noise** |  |
| Demolitions: deaths | 13.58 (13.33 to 13.83), n 72 | 11.22 (11.03 to 11.40), n 72 | -2.36 | +-0.312 | **OUTSIDE noise** |  |
| Demolitions: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Demolitions: objective credit (hill seconds scored, Actor.HillSeconds) | 149.4 s (146.0 to 152.8), n 72 | 98.90 s (96.24 to 101.6), n 72 | -50.49 | +-4.34 | **OUTSIDE noise** |  |
| Demolitions: units delivered to the egg | 0.368 (0.281 to 0.455), n 72 | 0.715 (0.561 to 0.870), n 72 | 0.347 | +-0.177 | **OUTSIDE noise** |  |
| Demolitions: damage dealt to the Kaiju | 186.5 HP (167.4 to 205.6), n 72 | 145.1 HP (120.2 to 170.1), n 72 | -41.38 | +-31.37 | **OUTSIDE noise** |  |
| Demolitions: share of all kills (whole class) | 0.192 (0.186 to 0.198), n 72 | 0.203 (0.197 to 0.210), n 72 | 0.011 | +-0.009 | **OUTSIDE noise** |  |
| Demolitions: share of all fighter damage (whole class) | 0.210 (0.206 to 0.213), n 72 | 0.224 (0.220 to 0.229), n 72 | 0.015 | +-0.006 | **OUTSIDE noise** |  |
| Builder: damage dealt to fighters | 1118 HP (1055 to 1181), n 72 | 1041 HP (976.5 to 1106), n 72 | -76.82 | +-90.45 | within noise |  |
| Builder: kills | 6.99 (6.43 to 7.55), n 72 | 6.31 (5.73 to 6.89), n 72 | -0.674 | +-0.808 | within noise |  |
| Builder: deaths | 8.64 (8.26 to 9.02), n 72 | 6.89 (6.56 to 7.22), n 72 | -1.75 | +-0.503 | **OUTSIDE noise** |  |
| Builder: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Builder: objective credit (hill seconds scored, Actor.HillSeconds) | 95.99 s (92.43 to 99.54), n 72 | 96.98 s (93.08 to 100.9), n 72 | 0.992 | +-5.27 | within noise |  |
| Builder: units delivered to the egg | 4.45 (3.50 to 5.41), n 72 | 4.49 (3.53 to 5.44), n 72 | 0.035 | +-1.35 | within noise |  |
| Builder: damage dealt to the Kaiju | 187.3 HP (163.6 to 211.0), n 72 | 133.9 HP (107.0 to 160.9), n 72 | -53.35 | +-35.89 | **OUTSIDE noise** |  |
| Builder: share of all kills (whole class) | 0.039 (0.036 to 0.041), n 72 | 0.041 (0.037 to 0.044), n 72 | 0.002 | +-0.005 | within noise |  |
| Builder: share of all fighter damage (whole class) | 0.035 (0.034 to 0.037), n 72 | 0.037 (0.035 to 0.040), n 72 | 0.002 | +-0.003 | within noise |  |
| Healer: damage dealt to fighters | 75.76 HP (69.29 to 82.22), n 72 | 132.4 HP (123.9 to 140.9), n 72 | 56.66 | +-10.69 | **OUTSIDE noise** |  |
| Healer: kills | 0.118 (0.079 to 0.157), n 72 | 0.208 (0.158 to 0.259), n 72 | 0.090 | +-0.064 | **OUTSIDE noise** |  |
| Healer: deaths | 18.02 (17.73 to 18.32), n 72 | 15.16 (14.95 to 15.37), n 72 | -2.86 | +-0.360 | **OUTSIDE noise** |  |
| Healer: healing done (Healer mends and pulses) | 4899 HP (4819 to 4980), n 72 | 4565 HP (4488 to 4641), n 72 | -334.5 | +-111.1 | **OUTSIDE noise** |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 133.8 s (130.2 to 137.4), n 72 | 86.66 s (84.10 to 89.22), n 72 | -47.14 | +-4.42 | **OUTSIDE noise** |  |
| Healer: units delivered to the egg | 0.021 (-0.000 to 0.042), n 72 | 0.045 (0.006 to 0.084), n 72 | 0.024 | +-0.044 | within noise |  |
| Healer: damage dealt to the Kaiju | 6.12 HP (3.77 to 8.47), n 72 | 6.72 HP (4.54 to 8.90), n 72 | 0.599 | +-3.21 | within noise |  |
| Healer: share of all kills (whole class) | 0.001 (0.001 to 0.002), n 72 | 0.003 (0.002 to 0.003), n 72 | 0.001 | +-0.001 | **OUTSIDE noise** |  |
| Healer: share of all fighter damage (whole class) | 0.005 (0.004 to 0.005), n 72 | 0.010 (0.009 to 0.010), n 72 | 0.005 | +-0.001 | **OUTSIDE noise** |  |
| Top-10 share of all fighter damage (10 of 32 bodies) | 0.498 (0.494 to 0.502), n 72 | 0.523 (0.520 to 0.527), n 72 | 0.025 | +-0.005 | **OUTSIDE noise** | <= 0.500: met / missed |
| Top-10 share of all kills | 0.540 (0.531 to 0.549), n 72 | 0.573 (0.565 to 0.580), n 72 | 0.033 | +-0.012 | **OUTSIDE noise** |  |
| Time to kill: Warrior on a passive Warrior | 4.93 s (4.93 to 4.93), n 8 | 4.93 s (4.93 to 4.93), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Farmer | 4.03 s (4.03 to 4.03), n 8 | 4.03 s (4.03 to 4.03), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Miner | 4.03 s (4.03 to 4.03), n 8 | 4.03 s (4.03 to 4.03), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Demolitions | 4.93 s (4.93 to 4.93), n 8 | 4.03 s (4.03 to 4.03), n 8 | -0.900 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Warrior on a passive Builder | 4.93 s (4.93 to 4.93), n 8 | 4.03 s (4.03 to 4.03), n 8 | -0.900 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Warrior on a passive Healer | 4.93 s (4.93 to 4.93), n 8 | 4.03 s (4.03 to 4.03), n 8 | -0.900 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Warrior | 2.97 s (2.97 to 2.97), n 8 | 4.33 s (4.33 to 4.33), n 8 | 1.37 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Farmer | 2.97 s (2.97 to 2.97), n 8 | 3.00 s (3.00 to 3.00), n 8 | 0.033 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Miner | 2.97 s (2.97 to 2.97), n 8 | 3.00 s (3.00 to 3.00), n 8 | 0.033 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Demolitions | 3.63 s (3.63 to 3.63), n 8 | 3.67 s (3.67 to 3.67), n 8 | 0.033 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Builder | 3.63 s (3.63 to 3.63), n 8 | 3.67 s (3.67 to 3.67), n 8 | 0.033 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Farmer on a passive Healer | 2.97 s (2.97 to 2.97), n 8 | 3.00 s (3.00 to 3.00), n 8 | 0.033 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Warrior | 3.57 s (3.57 to 3.57), n 8 | 3.73 s (3.73 to 3.73), n 8 | 0.167 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Farmer | 4.23 s (4.23 to 4.23), n 8 | 3.73 s (3.73 to 3.73), n 8 | -0.500 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Miner | 4.23 s (4.23 to 4.23), n 8 | 3.73 s (3.73 to 3.73), n 8 | -0.500 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Demolitions | 4.90 s (4.90 to 4.90), n 8 | 4.40 s (4.40 to 4.40), n 8 | -0.500 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Builder | 3.57 s (3.57 to 3.57), n 8 | 3.07 s (3.07 to 3.07), n 8 | -0.500 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Miner on a passive Healer | 4.23 s (4.23 to 4.23), n 8 | 3.73 s (3.73 to 3.73), n 8 | -0.500 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Warrior | 5.13 s (5.13 to 5.13), n 8 | 5.83 s (5.83 to 5.83), n 8 | 0.700 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Farmer | 4.43 s (4.43 to 4.43), n 8 | 4.43 s (4.43 to 4.43), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Miner | 3.73 s (3.73 to 3.73), n 8 | 3.73 s (3.73 to 3.73), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Demolitions | 5.13 s (5.13 to 5.13), n 8 | 5.13 s (5.13 to 5.13), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Builder | 5.13 s (5.13 to 5.13), n 8 | 5.13 s (5.13 to 5.13), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Healer | 4.43 s (4.43 to 4.43), n 8 | 4.43 s (4.43 to 4.43), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Warrior | 3.97 s (3.97 to 3.97), n 8 | 4.87 s (4.87 to 4.87), n 8 | 0.900 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Builder on a passive Farmer | 3.07 s (3.07 to 3.07), n 8 | 3.07 s (3.07 to 3.07), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Miner | 3.07 s (3.07 to 3.07), n 8 | 3.07 s (3.07 to 3.07), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Demolitions | 3.97 s (3.97 to 3.97), n 8 | 3.97 s (3.97 to 3.97), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Builder | 3.97 s (3.97 to 3.97), n 8 | 3.97 s (3.97 to 3.97), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Healer | 3.97 s (3.97 to 3.97), n 8 | 3.97 s (3.97 to 3.97), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Warrior | 6.67 s (6.67 to 6.67), n 8 | 8.27 s (8.27 to 8.27), n 8 | 1.60 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Healer on a passive Farmer | 5.87 s (5.87 to 5.87), n 8 | 5.87 s (5.87 to 5.87), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Miner | 5.87 s (5.87 to 5.87), n 8 | 5.87 s (5.87 to 5.87), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Demolitions | 6.67 s (6.67 to 6.67), n 8 | 6.67 s (6.67 to 6.67), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Builder | 6.67 s (6.67 to 6.67), n 8 | 6.67 s (6.67 to 6.67), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Healer | 6.67 s (6.67 to 6.67), n 8 | 6.67 s (6.67 to 6.67), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill, median over the matchups that can kill | 4.23 s (3.97 to 4.93), n 36 | 4.03 s (3.73 to 4.43), n 36 | -0.200 | +-0.597 | within noise | 3.00 to 6.00: met / met |
| Matchups whose time to kill is within 3 to 6 s | 28, n 36 | 32, n 36 | 4.00 |  | not judged |  |
| Duel: Warrior beats Farmer | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Warrior beats Miner | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Warrior beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Warrior beats Builder | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Warrior beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Warrior | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Farmer beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Farmer beats Builder | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Warrior | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Miner beats Farmer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Miner beats Builder | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Warrior | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Farmer | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Demolitions beats Miner | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Demolitions beats Builder | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Warrior | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Builder beats Farmer | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Healer | 0.000 (0.000 to 0.324), n 8 | 1.00 (0.676 to 1.00), n 8 | 1.00 | +-0.229 | **OUTSIDE noise** |  |
| Duel: Healer beats Warrior | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Farmer | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Demolitions | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Builder | 1.00 (0.676 to 1.00), n 8 | 0.000 (0.000 to 0.324), n 8 | -1.00 | +-0.229 | **OUTSIDE noise** |  |
| Classes that win every duel (more than half of each matchup) | 1, n 6 | 1, n 6 | 0.000 |  | not judged |  |
| Duel order broken (Warrior beats all, all beat the Healer, no unbeaten or winless class among Miner, Builder, Farmer, Demolitions) | 6, n 18 | 0, n 18 | -6.00 |  | not judged | <= 0.000: missed / met |
| Escape from a chasing Warrior (runner flees from 4 u, alive at 20 u within 12 s): Farmer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Escape from a chasing Warrior (runner flees from 4 u, alive at 20 u within 12 s): Miner | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Escape from a chasing Warrior (runner flees from 4 u, alive at 20 u within 12 s): Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Escape from a chasing Warrior (runner flees from 4 u, alive at 20 u within 12 s): Builder | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Escape from a chasing Warrior (runner flees from 4 u, alive at 20 u within 12 s): Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Escape from a chasing Warrior (runner flees from 4 u, alive at 20 u within 12 s): all five classes | 1.00 (0.912 to 1.00), n 40 | 1.00 (0.912 to 1.00), n 40 | 0.000 | +-0.062 | within noise |  |
| Escape from a chasing Warrior, runner dodging too: Farmer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Escape from a chasing Warrior, runner dodging too: Miner | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Escape from a chasing Warrior, runner dodging too: Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Escape from a chasing Warrior, runner dodging too: Builder | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Escape from a chasing Warrior, runner dodging too: Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Escape from a chasing Warrior, runner dodging too: all five classes | 1.00 (0.912 to 1.00), n 40 | 1.00 (0.912 to 1.00), n 40 | 0.000 | +-0.062 | within noise |  |
| Matches where the egg hatched | 1.00 (0.949 to 1.000), n 72 | 1.00 (0.949 to 1.000), n 72 | 0.000 | +-0.036 | within noise |  |
| Elder share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.069 (0.030 to 0.152), n 72 | 0.069 | +-0.066 | **OUTSIDE noise** |  |
| Grove share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.111 (0.057 to 0.204), n 72 | 0.111 | +-0.078 | **OUTSIDE noise** |  |
| Brute share of hatches | 0.333 (0.235 to 0.448), n 72 | 0.153 (0.088 to 0.253), n 72 | -0.181 | +-0.135 | **OUTSIDE noise** |  |
| Timber share of hatches | 0.403 (0.297 to 0.518), n 72 | 0.389 (0.285 to 0.504), n 72 | -0.014 | +-0.156 | within noise |  |
| Mire share of hatches | 0.264 (0.176 to 0.376), n 72 | 0.278 (0.188 to 0.390), n 72 | 0.014 | +-0.142 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.883 (0.778 to 0.942), n 60 | 0.807 (0.687 to 0.889), n 57 | -0.076 | +-0.130 | within noise | <= 0.700: missed / missed |
| Berserk share of hatches | 0.167 (0.098 to 0.269), n 72 | 0.208 (0.131 to 0.316), n 72 | 0.042 | +-0.126 | within noise |  |
| Hut level 1 reached at (team-matches that reached it) | 16.50 s (16.37 to 16.63), n 144 | 16.48 s (16.33 to 16.63), n 144 | -0.017 | +-0.201 | within noise |  |
| Hut level 2 reached at (team-matches that reached it) | 62.83 s (62.73 to 62.97), n 144 | 62.73 s (62.47 to 63.87), n 144 | -0.100 | +-0.710 | within noise |  |
| Hut level 3 reached at (team-matches that reached it) | 122.4 s (118.4 to 131.3), n 144 | 131.6 s (127.4 to 135.2), n 144 | 9.25 | +-7.57 | **OUTSIDE noise** |  |
| Hut level 4 reached at (team-matches that reached it) | 284.1 s (276.0 to 297.7), n 143 | 297.6 s (289.2 to 307.3), n 141 | 13.53 | +-14.09 | within noise |  |
| Hut level 5 reached at (team-matches that reached it) | 437.4 s (426.3 to 448.1), n 53 | 440.0 s (427.8 to 453.3), n 36 | 2.52 | +-16.81 | within noise |  |
| Team-matches reaching level 5 | 0.368 (0.294 to 0.449), n 144 | 0.250 (0.186 to 0.327), n 144 | -0.118 | +-0.105 | **OUTSIDE noise** |  |
| Builder walls built | 3.26 (2.74 to 3.79), n 72 | 1.64 (1.21 to 2.07), n 72 | -1.63 | +-0.678 | **OUTSIDE noise** |  |
| Builder wall cells broken or crumbled | 3.06 (2.51 to 3.60), n 72 | 1.57 (1.15 to 1.99), n 72 | -1.49 | +-0.684 | **OUTSIDE noise** |  |
| Ladders and bridges placed | 5.26 (4.69 to 5.83), n 72 | 6.78 (6.16 to 7.39), n 72 | 1.51 | +-0.840 | **OUTSIDE noise** |  |
| Ladders and bridges destroyed | 0.806 (0.617 to 0.994), n 72 | 1.14 (0.875 to 1.40), n 72 | 0.333 | +-0.324 | **OUTSIDE noise** |  |
| Barricades placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades broken | 10.53 (8.75 to 12.31), n 72 | 4.92 (3.66 to 6.17), n 72 | -5.61 | +-2.17 | **OUTSIDE noise** |  |
| Decorations toppled | 0.000 (0.000 to 0.000), n 72 | 0.042 (-0.005 to 0.088), n 72 | 0.042 | +-0.046 | within noise |  |
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 4.00 (3.00 to 5.00), n 72 | 3.00 (2.00 to 5.00), n 72 | -1.00 | +-1.80 | within noise | < 5.00: met / met |
| Stuck episodes per match (mean) | 7.89 (4.92 to 10.85), n 72 | 4.10 (3.18 to 5.01), n 72 | -3.79 | +-3.10 | **OUTSIDE noise** |  |
| Sim tick cost, mean (wall clock, this machine) | 94.35 us (91.45 to 97.24), n 72 | 114.5 us (110.8 to 118.1), n 72 | 20.12 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 857.0 us (850.9 to 865.7), n 72 | 929.3 us (925.7 to 933.9), n 72 | 72.29 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
