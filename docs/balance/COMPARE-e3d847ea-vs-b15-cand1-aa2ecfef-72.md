# Balance compare

- base: base aa2ecfef1f8a51a5d59adcd1c9d8a3c24208640d (aa2ecfef, none (the game's values))
- head: head e3d847ea20304e5b8e878b13cc042cc2781f2005 (e3d847ea, none (the game's values))
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

16 of 208 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.639 (0.524 to 0.740), n 72 | 0.528 (0.414 to 0.639), n 72 | -0.111 | +-0.156 | within noise |  |
| Crimson win share | 0.361 (0.260 to 0.476), n 72 | 0.458 (0.348 to 0.573), n 72 | 0.097 | +-0.156 | within noise |  |
| Draw share | 0.000 (0.000 to 0.051), n 72 | 0.014 (0.002 to 0.075), n 72 | 0.014 | +-0.044 | within noise |  |
| West spawn (pad x=8) win share | 0.639 (0.524 to 0.740), n 72 | 0.528 (0.414 to 0.639), n 72 | -0.111 | +-0.156 | within noise |  |
| East spawn (pad x=104) win share | 0.361 (0.260 to 0.476), n 72 | 0.458 (0.348 to 0.573), n 72 | 0.097 | +-0.156 | within noise |  |
| Match length | 480.0 s (480.0 to 480.0), n 72 | 480.0 s (480.0 to 480.0), n 72 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.125 (0.067 to 0.221), n 72 | 0.097 (0.048 to 0.187), n 72 | -0.028 | +-0.104 | within noise | <= 0.150: met / met |
| Hill lead changes per match (median; spread = p25 to p75) | 4.00 (3.00 to 5.00), n 72 | 5.00 (4.00 to 6.00), n 72 | 1.00 | +-1.41 | within noise | >= 5.00: missed / met |
| Hill lead changes per match (mean) | 4.29 (3.78 to 4.81), n 72 | 4.86 (4.25 to 5.47), n 72 | 0.569 | +-0.801 | within noise |  |
| Result decided at (last lead change) | 326.5 s (178.0 to 381.0), n 72 | 290.0 s (235.0 to 352.0), n 72 | -36.50 | +-117.2 | within noise |  |
| Result decided at, share of the match | 0.566 (0.492 to 0.641), n 72 | 0.580 (0.515 to 0.646), n 72 | 0.014 | +-0.099 | within noise |  |
| Leader at half time wins | 0.458 (0.348 to 0.573), n 72 | 0.479 (0.367 to 0.593), n 71 | 0.021 | +-0.159 | within noise | 0.600 to 0.800: missed / missed |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.952 (0.948 to 0.957), n 72 | 0.950 (0.946 to 0.954), n 72 | -0.002 | +-0.006 | within noise |  |
| Longest unbroken hill hold | 108.1 s (94.10 to 123.4), n 72 | 111.8 s (93.47 to 127.1), n 72 | 3.75 | +-22.32 | within noise |  |
| Warrior: dead or getting up | 0.107 (0.106 to 0.109), n 72 | 0.105 (0.104 to 0.107), n 72 | -0.002 | +-0.002 | within noise |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.317 (0.313 to 0.322), n 72 | 0.312 (0.308 to 0.316), n 72 | -0.005 | +-0.006 | within noise |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.339 (0.335 to 0.343), n 72 | 0.349 (0.345 to 0.353), n 72 | 0.010 | +-0.006 | **OUTSIDE noise** |  |
| Warrior: dead or walking | 0.425 (0.419 to 0.430), n 72 | 0.417 (0.413 to 0.422), n 72 | -0.007 | +-0.007 | **OUTSIDE noise** |  |
| Warrior: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.254 (0.250 to 0.258), n 72 | 0.250 (0.247 to 0.253), n 72 | -0.004 | +-0.005 | within noise |  |
| Warrior: dead or walking back after a respawn | 0.338 (0.333 to 0.343), n 72 | 0.333 (0.329 to 0.337), n 72 | -0.005 | +-0.006 | within noise |  |
| Farmer: dead or getting up | 0.069 (0.067 to 0.071), n 72 | 0.069 (0.066 to 0.071), n 72 | -0.000 | +-0.003 | within noise |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.306 (0.299 to 0.313), n 72 | 0.304 (0.298 to 0.310), n 72 | -0.002 | +-0.009 | within noise |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.268 (0.264 to 0.272), n 72 | 0.279 (0.275 to 0.282), n 72 | 0.010 | +-0.006 | **OUTSIDE noise** |  |
| Farmer: dead or walking | 0.375 (0.366 to 0.384), n 72 | 0.373 (0.365 to 0.380), n 72 | -0.002 | +-0.011 | within noise |  |
| Farmer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.151 (0.147 to 0.155), n 72 | 0.152 (0.147 to 0.156), n 72 | 0.001 | +-0.006 | within noise |  |
| Farmer: dead or walking back after a respawn | 0.206 (0.201 to 0.212), n 72 | 0.207 (0.201 to 0.213), n 72 | 0.000 | +-0.008 | within noise |  |
| Miner: dead or getting up | 0.067 (0.065 to 0.069), n 72 | 0.066 (0.064 to 0.069), n 72 | -0.000 | +-0.003 | within noise |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.393 (0.389 to 0.397), n 72 | 0.387 (0.383 to 0.391), n 72 | -0.006 | +-0.006 | within noise |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.163 (0.158 to 0.167), n 72 | 0.168 (0.164 to 0.173), n 72 | 0.006 | +-0.006 | within noise |  |
| Miner: dead or walking | 0.460 (0.455 to 0.465), n 72 | 0.453 (0.448 to 0.459), n 72 | -0.006 | +-0.008 | within noise |  |
| Miner: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.131 (0.127 to 0.136), n 72 | 0.132 (0.127 to 0.136), n 72 | 0.000 | +-0.006 | within noise |  |
| Miner: dead or walking back after a respawn | 0.185 (0.179 to 0.191), n 72 | 0.185 (0.179 to 0.191), n 72 | -0.000 | +-0.009 | within noise |  |
| Demolitions: dead or getting up | 0.107 (0.105 to 0.109), n 72 | 0.105 (0.103 to 0.108), n 72 | -0.002 | +-0.003 | within noise |  |
| Demolitions: walking (moving, not fighting, empty-handed, off the hill) | 0.322 (0.317 to 0.327), n 72 | 0.315 (0.310 to 0.320), n 72 | -0.007 | +-0.007 | **OUTSIDE noise** |  |
| Demolitions: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.354 (0.351 to 0.358), n 72 | 0.366 (0.363 to 0.370), n 72 | 0.012 | +-0.005 | **OUTSIDE noise** |  |
| Demolitions: dead or walking | 0.429 (0.423 to 0.435), n 72 | 0.420 (0.414 to 0.427), n 72 | -0.009 | +-0.009 | **OUTSIDE noise** |  |
| Demolitions: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.227 (0.222 to 0.232), n 72 | 0.224 (0.219 to 0.228), n 72 | -0.004 | +-0.007 | within noise |  |
| Demolitions: dead or walking back after a respawn | 0.312 (0.306 to 0.318), n 72 | 0.307 (0.301 to 0.313), n 72 | -0.005 | +-0.009 | within noise |  |
| Builder: dead or getting up | 0.074 (0.071 to 0.077), n 72 | 0.073 (0.070 to 0.076), n 72 | -0.001 | +-0.005 | within noise |  |
| Builder: walking (moving, not fighting, empty-handed, off the hill) | 0.340 (0.327 to 0.353), n 72 | 0.339 (0.326 to 0.351), n 72 | -0.001 | +-0.018 | within noise |  |
| Builder: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.167 (0.159 to 0.175), n 72 | 0.173 (0.165 to 0.180), n 72 | 0.006 | +-0.011 | within noise |  |
| Builder: dead or walking | 0.414 (0.401 to 0.428), n 72 | 0.412 (0.398 to 0.425), n 72 | -0.003 | +-0.019 | within noise |  |
| Builder: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.155 (0.148 to 0.163), n 72 | 0.153 (0.145 to 0.161), n 72 | -0.002 | +-0.011 | within noise |  |
| Builder: dead or walking back after a respawn | 0.215 (0.206 to 0.225), n 72 | 0.212 (0.201 to 0.222), n 72 | -0.003 | +-0.014 | within noise |  |
| Healer: dead or getting up | 0.138 (0.136 to 0.141), n 72 | 0.138 (0.135 to 0.141), n 72 | -0.000 | +-0.004 | within noise |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.396 (0.391 to 0.401), n 72 | 0.390 (0.384 to 0.397), n 72 | -0.006 | +-0.008 | within noise |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.216 (0.214 to 0.218), n 72 | 0.220 (0.218 to 0.222), n 72 | 0.004 | +-0.003 | **OUTSIDE noise** |  |
| Healer: dead or walking | 0.534 (0.528 to 0.541), n 72 | 0.528 (0.520 to 0.537), n 72 | -0.006 | +-0.011 | within noise |  |
| Healer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.328 (0.323 to 0.333), n 72 | 0.326 (0.320 to 0.332), n 72 | -0.002 | +-0.008 | within noise |  |
| Healer: dead or walking back after a respawn | 0.436 (0.430 to 0.443), n 72 | 0.434 (0.426 to 0.442), n 72 | -0.002 | +-0.010 | within noise |  |
| all: dead or getting up | 0.094 (0.093 to 0.096), n 72 | 0.093 (0.092 to 0.095), n 72 | -0.001 | +-0.002 | within noise |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.336 (0.332 to 0.339), n 72 | 0.331 (0.328 to 0.334), n 72 | -0.005 | +-0.005 | within noise |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.275 (0.273 to 0.277), n 72 | 0.284 (0.282 to 0.286), n 72 | 0.009 | +-0.003 | **OUTSIDE noise** |  |
| all: dead or walking | 0.430 (0.426 to 0.435), n 72 | 0.425 (0.420 to 0.429), n 72 | -0.006 | +-0.006 | within noise |  |
| all: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.213 (0.210 to 0.216), n 72 | 0.211 (0.208 to 0.214), n 72 | -0.002 | +-0.004 | within noise |  |
| all: dead or walking back after a respawn | 0.287 (0.284 to 0.291), n 72 | 0.285 (0.281 to 0.289), n 72 | -0.003 | +-0.005 | within noise | <= 0.280: missed / missed |
| Warrior: damage dealt to fighters | 2696 HP (2662 to 2730), n 72 | 2682 HP (2645 to 2720), n 72 | -13.60 | +-50.41 | within noise |  |
| Warrior: kills | 14.60 (14.28 to 14.92), n 72 | 14.62 (14.29 to 14.94), n 72 | 0.017 | +-0.457 | within noise |  |
| Warrior: deaths | 13.63 (13.43 to 13.83), n 72 | 13.44 (13.25 to 13.63), n 72 | -0.189 | +-0.276 | within noise |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 169.8 s (167.5 to 172.1), n 72 | 170.2 s (167.9 to 172.5), n 72 | 0.421 | +-3.25 | within noise |  |
| Warrior: units delivered to the egg | 0.078 (0.043 to 0.113), n 72 | 0.056 (0.024 to 0.087), n 72 | -0.022 | +-0.047 | within noise |  |
| Warrior: damage dealt to the Kaiju | 138.7 HP (123.6 to 153.7), n 72 | 131.7 HP (113.9 to 149.4), n 72 | -7.02 | +-23.29 | within noise |  |
| Warrior: share of all kills (whole class) | 0.404 (0.397 to 0.411), n 72 | 0.406 (0.398 to 0.413), n 72 | 0.002 | +-0.010 | within noise |  |
| Warrior: share of all fighter damage (whole class) | 0.429 (0.425 to 0.434), n 72 | 0.425 (0.421 to 0.429), n 72 | -0.004 | +-0.006 | within noise | <= 0.400: missed / missed |
| Farmer: damage dealt to fighters | 1895 HP (1849 to 1940), n 72 | 1919 HP (1882 to 1956), n 72 | 24.18 | +-58.41 | within noise |  |
| Farmer: kills | 12.06 (11.71 to 12.40), n 72 | 12.05 (11.69 to 12.42), n 72 | -0.005 | +-0.502 | within noise |  |
| Farmer: deaths | 8.35 (8.12 to 8.58), n 72 | 8.37 (8.12 to 8.63), n 72 | 0.023 | +-0.344 | within noise |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 86.05 s (83.28 to 88.83), n 72 | 85.59 s (82.68 to 88.50), n 72 | -0.463 | +-4.02 | within noise |  |
| Farmer: units delivered to the egg | 14.81 (14.59 to 15.02), n 72 | 14.96 (14.71 to 15.21), n 72 | 0.153 | +-0.331 | within noise |  |
| Farmer: damage dealt to the Kaiju | 811.6 HP (759.8 to 863.4), n 72 | 829.5 HP (767.8 to 891.2), n 72 | 17.92 | +-80.55 | within noise |  |
| Farmer: share of all kills (whole class) | 0.267 (0.260 to 0.273), n 72 | 0.268 (0.261 to 0.275), n 72 | 0.001 | +-0.010 | within noise |  |
| Farmer: share of all fighter damage (whole class) | 0.241 (0.237 to 0.245), n 72 | 0.243 (0.239 to 0.247), n 72 | 0.002 | +-0.006 | within noise |  |
| Miner: damage dealt to fighters | 1256 HP (1209 to 1302), n 72 | 1268 HP (1224 to 1313), n 72 | 12.89 | +-64.34 | within noise |  |
| Miner: kills | 8.81 (8.38 to 9.23), n 72 | 8.51 (8.13 to 8.89), n 72 | -0.295 | +-0.569 | within noise |  |
| Miner: deaths | 7.91 (7.64 to 8.18), n 72 | 7.92 (7.65 to 8.19), n 72 | 0.014 | +-0.379 | within noise |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 66.13 s (63.61 to 68.64), n 72 | 66.28 s (63.87 to 68.68), n 72 | 0.152 | +-3.48 | within noise |  |
| Miner: units delivered to the egg | 0.458 (0.344 to 0.572), n 72 | 0.479 (0.365 to 0.594), n 72 | 0.021 | +-0.162 | within noise |  |
| Miner: damage dealt to the Kaiju | 174.3 HP (151.6 to 197.0), n 72 | 170.0 HP (144.1 to 195.8), n 72 | -4.32 | +-34.40 | within noise |  |
| Miner: share of all kills (whole class) | 0.097 (0.093 to 0.102), n 72 | 0.095 (0.090 to 0.099), n 72 | -0.003 | +-0.006 | within noise |  |
| Miner: share of all fighter damage (whole class) | 0.080 (0.077 to 0.083), n 72 | 0.080 (0.078 to 0.083), n 72 | 0.000 | +-0.004 | within noise |  |
| Demolitions: damage dealt to fighters | 3292 HP (3237 to 3346), n 72 | 3323 HP (3269 to 3377), n 72 | 30.88 | +-76.76 | within noise |  |
| Demolitions: kills | 17.39 (16.79 to 17.99), n 72 | 17.28 (16.79 to 17.77), n 72 | -0.108 | +-0.774 | within noise |  |
| Demolitions: deaths | 13.58 (13.33 to 13.83), n 72 | 13.39 (13.15 to 13.64), n 72 | -0.184 | +-0.349 | within noise |  |
| Demolitions: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Demolitions: objective credit (hill seconds scored, Actor.HillSeconds) | 149.4 s (146.0 to 152.8), n 72 | 150.5 s (147.0 to 154.0), n 72 | 1.10 | +-4.89 | within noise |  |
| Demolitions: units delivered to the egg | 0.368 (0.281 to 0.455), n 72 | 0.396 (0.302 to 0.489), n 72 | 0.028 | +-0.127 | within noise |  |
| Demolitions: damage dealt to the Kaiju | 186.5 HP (167.4 to 205.6), n 72 | 166.1 HP (147.1 to 185.2), n 72 | -20.39 | +-26.94 | within noise |  |
| Demolitions: share of all kills (whole class) | 0.192 (0.186 to 0.198), n 72 | 0.192 (0.187 to 0.197), n 72 | -0.000 | +-0.008 | within noise |  |
| Demolitions: share of all fighter damage (whole class) | 0.210 (0.206 to 0.213), n 72 | 0.211 (0.208 to 0.214), n 72 | 0.001 | +-0.005 | within noise |  |
| Builder: damage dealt to fighters | 1118 HP (1055 to 1181), n 72 | 1136 HP (1073 to 1199), n 72 | 18.00 | +-89.35 | within noise |  |
| Builder: kills | 6.99 (6.43 to 7.55), n 72 | 6.96 (6.44 to 7.47), n 72 | -0.028 | +-0.760 | within noise |  |
| Builder: deaths | 8.64 (8.26 to 9.02), n 72 | 8.54 (8.13 to 8.95), n 72 | -0.097 | +-0.558 | within noise |  |
| Builder: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Builder: objective credit (hill seconds scored, Actor.HillSeconds) | 95.99 s (92.43 to 99.54), n 72 | 96.84 s (92.75 to 100.9), n 72 | 0.856 | +-5.42 | within noise |  |
| Builder: units delivered to the egg | 4.45 (3.50 to 5.41), n 72 | 4.63 (3.63 to 5.62), n 72 | 0.174 | +-1.38 | within noise |  |
| Builder: damage dealt to the Kaiju | 187.3 HP (163.6 to 211.0), n 72 | 181.9 HP (157.3 to 206.4), n 72 | -5.42 | +-34.12 | within noise |  |
| Builder: share of all kills (whole class) | 0.039 (0.036 to 0.041), n 72 | 0.039 (0.036 to 0.041), n 72 | 0.000 | +-0.004 | within noise |  |
| Builder: share of all fighter damage (whole class) | 0.035 (0.034 to 0.037), n 72 | 0.036 (0.034 to 0.038), n 72 | 0.001 | +-0.003 | within noise |  |
| Healer: damage dealt to fighters | 75.76 HP (69.29 to 82.22), n 72 | 74.59 HP (68.63 to 80.56), n 72 | -1.16 | +-8.80 | within noise |  |
| Healer: kills | 0.118 (0.079 to 0.157), n 72 | 0.111 (0.071 to 0.151), n 72 | -0.007 | +-0.056 | within noise |  |
| Healer: deaths | 18.02 (17.73 to 18.32), n 72 | 17.99 (17.67 to 18.31), n 72 | -0.028 | +-0.435 | within noise |  |
| Healer: healing done (Healer mends and pulses) | 4899 HP (4819 to 4980), n 72 | 4984 HP (4897 to 5071), n 72 | 84.60 | +-118.4 | within noise |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 133.8 s (130.2 to 137.4), n 72 | 134.3 s (129.8 to 138.8), n 72 | 0.533 | +-5.76 | within noise |  |
| Healer: units delivered to the egg | 0.021 (-0.000 to 0.042), n 72 | 0.024 (0.002 to 0.046), n 72 | 0.003 | +-0.031 | within noise |  |
| Healer: damage dealt to the Kaiju | 6.12 HP (3.77 to 8.47), n 72 | 7.45 HP (4.50 to 10.39), n 72 | 1.33 | +-3.77 | within noise |  |
| Healer: share of all kills (whole class) | 0.001 (0.001 to 0.002), n 72 | 0.001 (0.001 to 0.002), n 72 | -0.000 | +-0.001 | within noise |  |
| Healer: share of all fighter damage (whole class) | 0.005 (0.004 to 0.005), n 72 | 0.005 (0.004 to 0.005), n 72 | -0.000 | +-0.001 | within noise |  |
| Top-10 share of all fighter damage (10 of 32 bodies) | 0.498 (0.494 to 0.502), n 72 | 0.497 (0.494 to 0.500), n 72 | -0.001 | +-0.005 | within noise | <= 0.500: met / met |
| Top-10 share of all kills | 0.540 (0.531 to 0.549), n 72 | 0.542 (0.534 to 0.550), n 72 | 0.002 | +-0.012 | within noise |  |
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
| Time to kill: Demolitions on a passive Warrior | 5.13 s (5.13 to 5.13), n 8 | 5.83 s (5.83 to 5.83), n 8 | 0.700 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Farmer | 4.43 s (4.43 to 4.43), n 8 | 5.13 s (5.13 to 5.13), n 8 | 0.700 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Miner | 3.73 s (3.73 to 3.73), n 8 | 4.43 s (4.43 to 4.43), n 8 | 0.700 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Demolitions | 5.13 s (5.13 to 5.13), n 8 | 5.83 s (5.83 to 5.83), n 8 | 0.700 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Builder | 5.13 s (5.13 to 5.13), n 8 | 5.83 s (5.83 to 5.83), n 8 | 0.700 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Healer | 4.43 s (4.43 to 4.43), n 8 | 5.13 s (5.13 to 5.13), n 8 | 0.700 | +-0.000 | **OUTSIDE noise** |  |
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
| Brute share of hatches | 0.333 (0.235 to 0.448), n 72 | 0.319 (0.223 to 0.434), n 72 | -0.014 | +-0.150 | within noise |  |
| Timber share of hatches | 0.403 (0.297 to 0.518), n 72 | 0.417 (0.310 to 0.532), n 72 | 0.014 | +-0.157 | within noise |  |
| Mire share of hatches | 0.264 (0.176 to 0.376), n 72 | 0.264 (0.176 to 0.376), n 72 | 0.000 | +-0.141 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.883 (0.778 to 0.942), n 60 | 0.842 (0.726 to 0.915), n 57 | -0.041 | +-0.125 | within noise |  |
| Berserk share of hatches | 0.167 (0.098 to 0.269), n 72 | 0.194 (0.120 to 0.300), n 72 | 0.028 | +-0.124 | within noise |  |
| Hut level 1 reached at (team-matches that reached it) | 16.50 s (16.37 to 16.63), n 144 | 16.63 s (16.33 to 16.63), n 144 | 0.133 | +-0.201 | within noise |  |
| Hut level 2 reached at (team-matches that reached it) | 62.83 s (62.73 to 62.97), n 144 | 62.73 s (62.20 to 63.00), n 144 | -0.100 | +-0.417 | within noise |  |
| Hut level 3 reached at (team-matches that reached it) | 122.4 s (118.4 to 131.3), n 144 | 130.5 s (122.1 to 133.3), n 144 | 8.08 | +-8.57 | within noise |  |
| Hut level 4 reached at (team-matches that reached it) | 284.1 s (276.0 to 297.7), n 143 | 283.7 s (277.2 to 295.0), n 141 | -0.333 | +-14.02 | within noise |  |
| Hut level 5 reached at (team-matches that reached it) | 437.4 s (426.3 to 448.1), n 53 | 439.8 s (424.3 to 453.1), n 56 | 2.37 | +-18.05 | within noise |  |
| Team-matches reaching level 5 | 0.368 (0.294 to 0.449), n 144 | 0.389 (0.313 to 0.470), n 144 | 0.021 | +-0.111 | within noise |  |
| Builder walls built | 3.26 (2.74 to 3.79), n 72 | 3.88 (3.26 to 4.49), n 72 | 0.611 | +-0.811 | within noise |  |
| Builder wall cells broken or crumbled | 3.06 (2.51 to 3.60), n 72 | 3.50 (2.89 to 4.11), n 72 | 0.444 | +-0.814 | within noise |  |
| Ladders and bridges placed | 5.26 (4.69 to 5.83), n 72 | 4.83 (4.22 to 5.45), n 72 | -0.431 | +-0.836 | within noise |  |
| Ladders and bridges destroyed | 0.806 (0.617 to 0.994), n 72 | 0.611 (0.404 to 0.818), n 72 | -0.194 | +-0.280 | within noise |  |
| Barricades placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades broken | 10.53 (8.75 to 12.31), n 72 | 11.90 (9.87 to 13.93), n 72 | 1.38 | +-2.70 | within noise |  |
| Decorations toppled | 0.000 (0.000 to 0.000), n 72 | 0.014 (-0.013 to 0.041), n 72 | 0.014 | +-0.027 | within noise |  |
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 4.00 (3.00 to 5.00), n 72 | 2.50 (2.00 to 3.00), n 72 | -1.50 | +-1.12 | **OUTSIDE noise** | < 5.00: met / met |
| Stuck episodes per match (mean) | 7.89 (4.92 to 10.85), n 72 | 3.31 (2.54 to 4.07), n 72 | -4.58 | +-3.06 | **OUTSIDE noise** |  |
| Sim tick cost, mean (wall clock, this machine) | 58.10 us (56.20 to 60.00), n 72 | 59.44 us (56.29 to 62.59), n 72 | 1.34 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 578.7 us (571.2 to 585.5), n 72 | 595.4 us (586.3 to 601.7), n 72 | 16.74 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
