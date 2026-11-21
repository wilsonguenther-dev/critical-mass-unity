# Balance compare

- base: base aa2ecfef (aa2ecfef, none (the game's values))
- head: head feat/b16-duel-rules-cancel (910a395d, none (the game's values))
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

66 of 221 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.639 (0.524 to 0.740), n 72 | 0.583 (0.468 to 0.690), n 72 | -0.056 | +-0.155 | within noise |  |
| Crimson win share | 0.361 (0.260 to 0.476), n 72 | 0.417 (0.310 to 0.532), n 72 | 0.056 | +-0.155 | within noise |  |
| Draw share | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| West spawn (pad x=8) win share | 0.639 (0.524 to 0.740), n 72 | 0.583 (0.468 to 0.690), n 72 | -0.056 | +-0.155 | within noise |  |
| East spawn (pad x=104) win share | 0.361 (0.260 to 0.476), n 72 | 0.417 (0.310 to 0.532), n 72 | 0.056 | +-0.155 | within noise |  |
| Match length | 480.0 s (480.0 to 480.0), n 72 | 480.0 s (480.0 to 480.0), n 72 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.125 (0.067 to 0.221), n 72 | 0.111 (0.057 to 0.204), n 72 | -0.014 | +-0.106 | within noise | <= 0.150: met / met |
| Hill lead changes per match (median; spread = p25 to p75) | 4.00 (3.00 to 5.00), n 72 | 4.00 (3.00 to 5.00), n 72 | 0.000 | +-1.41 | within noise | >= 5.00: missed / missed |
| Hill lead changes per match (mean) | 4.29 (3.78 to 4.81), n 72 | 4.25 (3.65 to 4.85), n 72 | -0.042 | +-0.793 | within noise |  |
| Result decided at (last lead change) | 326.5 s (178.0 to 381.0), n 72 | 261.5 s (134.0 to 339.0), n 72 | -65.00 | +-144.3 | within noise |  |
| Result decided at, share of the match | 0.566 (0.492 to 0.641), n 72 | 0.511 (0.434 to 0.589), n 72 | -0.055 | +-0.107 | within noise |  |
| Leader at half time wins | 0.458 (0.348 to 0.573), n 72 | 0.653 (0.538 to 0.752), n 72 | 0.194 | +-0.155 | **OUTSIDE noise** | 0.600 to 0.800: missed / met |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.952 (0.948 to 0.957), n 72 | 0.948 (0.943 to 0.952), n 72 | -0.005 | +-0.006 | within noise |  |
| Longest unbroken hill hold | 108.1 s (94.10 to 123.4), n 72 | 115.6 s (96.33 to 125.0), n 72 | 7.55 | +-20.51 | within noise |  |
| Warrior: dead or getting up | 0.107 (0.106 to 0.109), n 72 | 0.101 (0.099 to 0.103), n 72 | -0.007 | +-0.003 | **OUTSIDE noise** |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.317 (0.313 to 0.322), n 72 | 0.314 (0.309 to 0.318), n 72 | -0.004 | +-0.006 | within noise |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.339 (0.335 to 0.343), n 72 | 0.346 (0.342 to 0.350), n 72 | 0.007 | +-0.006 | **OUTSIDE noise** |  |
| Warrior: dead or walking | 0.425 (0.419 to 0.430), n 72 | 0.414 (0.409 to 0.420), n 72 | -0.010 | +-0.008 | **OUTSIDE noise** |  |
| Warrior: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.254 (0.250 to 0.258), n 72 | 0.248 (0.244 to 0.252), n 72 | -0.005 | +-0.006 | within noise |  |
| Warrior: dead or walking back after a respawn | 0.338 (0.333 to 0.343), n 72 | 0.328 (0.323 to 0.333), n 72 | -0.011 | +-0.007 | **OUTSIDE noise** |  |
| Farmer: dead or getting up | 0.069 (0.067 to 0.071), n 72 | 0.068 (0.065 to 0.070), n 72 | -0.001 | +-0.003 | within noise |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.306 (0.299 to 0.313), n 72 | 0.307 (0.300 to 0.314), n 72 | 0.001 | +-0.010 | within noise |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.268 (0.264 to 0.272), n 72 | 0.273 (0.269 to 0.276), n 72 | 0.004 | +-0.006 | within noise |  |
| Farmer: dead or walking | 0.375 (0.366 to 0.384), n 72 | 0.375 (0.366 to 0.383), n 72 | -0.000 | +-0.012 | within noise |  |
| Farmer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.151 (0.147 to 0.155), n 72 | 0.150 (0.146 to 0.154), n 72 | -0.001 | +-0.006 | within noise |  |
| Farmer: dead or walking back after a respawn | 0.206 (0.201 to 0.212), n 72 | 0.204 (0.199 to 0.210), n 72 | -0.002 | +-0.008 | within noise |  |
| Miner: dead or getting up | 0.067 (0.065 to 0.069), n 72 | 0.068 (0.065 to 0.070), n 72 | 0.001 | +-0.003 | within noise |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.393 (0.389 to 0.397), n 72 | 0.396 (0.392 to 0.400), n 72 | 0.003 | +-0.006 | within noise |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.163 (0.158 to 0.167), n 72 | 0.162 (0.157 to 0.167), n 72 | -0.001 | +-0.007 | within noise |  |
| Miner: dead or walking | 0.460 (0.455 to 0.465), n 72 | 0.463 (0.458 to 0.468), n 72 | 0.003 | +-0.007 | within noise |  |
| Miner: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.131 (0.127 to 0.136), n 72 | 0.137 (0.132 to 0.142), n 72 | 0.006 | +-0.007 | within noise |  |
| Miner: dead or walking back after a respawn | 0.185 (0.179 to 0.191), n 72 | 0.191 (0.184 to 0.198), n 72 | 0.006 | +-0.009 | within noise |  |
| Demolitions: dead or getting up | 0.107 (0.105 to 0.109), n 72 | 0.105 (0.103 to 0.107), n 72 | -0.002 | +-0.003 | within noise |  |
| Demolitions: walking (moving, not fighting, empty-handed, off the hill) | 0.322 (0.317 to 0.327), n 72 | 0.321 (0.317 to 0.326), n 72 | -0.001 | +-0.007 | within noise |  |
| Demolitions: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.354 (0.351 to 0.358), n 72 | 0.352 (0.350 to 0.355), n 72 | -0.002 | +-0.004 | within noise |  |
| Demolitions: dead or walking | 0.429 (0.423 to 0.435), n 72 | 0.426 (0.421 to 0.432), n 72 | -0.003 | +-0.008 | within noise |  |
| Demolitions: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.227 (0.222 to 0.232), n 72 | 0.227 (0.222 to 0.231), n 72 | -0.000 | +-0.007 | within noise |  |
| Demolitions: dead or walking back after a respawn | 0.312 (0.306 to 0.318), n 72 | 0.310 (0.304 to 0.316), n 72 | -0.002 | +-0.008 | within noise |  |
| Builder: dead or getting up | 0.074 (0.071 to 0.077), n 72 | 0.071 (0.069 to 0.074), n 72 | -0.003 | +-0.004 | within noise |  |
| Builder: walking (moving, not fighting, empty-handed, off the hill) | 0.340 (0.327 to 0.353), n 72 | 0.341 (0.329 to 0.353), n 72 | 0.001 | +-0.017 | within noise |  |
| Builder: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.167 (0.159 to 0.175), n 72 | 0.170 (0.162 to 0.178), n 72 | 0.003 | +-0.011 | within noise |  |
| Builder: dead or walking | 0.414 (0.401 to 0.428), n 72 | 0.413 (0.401 to 0.424), n 72 | -0.002 | +-0.018 | within noise |  |
| Builder: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.155 (0.148 to 0.163), n 72 | 0.151 (0.145 to 0.158), n 72 | -0.004 | +-0.010 | within noise |  |
| Builder: dead or walking back after a respawn | 0.215 (0.206 to 0.225), n 72 | 0.209 (0.201 to 0.217), n 72 | -0.006 | +-0.012 | within noise |  |
| Healer: dead or getting up | 0.138 (0.136 to 0.141), n 72 | 0.137 (0.134 to 0.140), n 72 | -0.001 | +-0.003 | within noise |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.396 (0.391 to 0.401), n 72 | 0.396 (0.390 to 0.401), n 72 | -0.000 | +-0.007 | within noise |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.216 (0.214 to 0.218), n 72 | 0.206 (0.204 to 0.208), n 72 | -0.010 | +-0.003 | **OUTSIDE noise** |  |
| Healer: dead or walking | 0.534 (0.528 to 0.541), n 72 | 0.533 (0.526 to 0.540), n 72 | -0.001 | +-0.010 | within noise |  |
| Healer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.328 (0.323 to 0.333), n 72 | 0.329 (0.323 to 0.334), n 72 | 0.001 | +-0.007 | within noise |  |
| Healer: dead or walking back after a respawn | 0.436 (0.430 to 0.443), n 72 | 0.436 (0.429 to 0.443), n 72 | -0.000 | +-0.010 | within noise |  |
| all: dead or getting up | 0.094 (0.093 to 0.096), n 72 | 0.092 (0.090 to 0.093), n 72 | -0.003 | +-0.002 | **OUTSIDE noise** |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.336 (0.332 to 0.339), n 72 | 0.335 (0.331 to 0.339), n 72 | -0.001 | +-0.005 | within noise |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.275 (0.273 to 0.277), n 72 | 0.277 (0.275 to 0.279), n 72 | 0.002 | +-0.003 | within noise |  |
| all: dead or walking | 0.430 (0.426 to 0.435), n 72 | 0.427 (0.422 to 0.432), n 72 | -0.004 | +-0.007 | within noise |  |
| all: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.213 (0.210 to 0.216), n 72 | 0.211 (0.209 to 0.214), n 72 | -0.001 | +-0.004 | within noise |  |
| all: dead or walking back after a respawn | 0.287 (0.284 to 0.291), n 72 | 0.284 (0.280 to 0.287), n 72 | -0.004 | +-0.005 | within noise | <= 0.280: missed / missed |
| Warrior: damage dealt to fighters | 2696 HP (2662 to 2730), n 72 | 2732 HP (2698 to 2767), n 72 | 36.35 | +-48.43 | within noise |  |
| Warrior: kills | 14.60 (14.28 to 14.92), n 72 | 15.71 (15.38 to 16.03), n 72 | 1.11 | +-0.455 | **OUTSIDE noise** |  |
| Warrior: deaths | 13.63 (13.43 to 13.83), n 72 | 12.89 (12.68 to 13.09), n 72 | -0.743 | +-0.286 | **OUTSIDE noise** |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 169.8 s (167.5 to 172.1), n 72 | 171.8 s (169.8 to 173.9), n 72 | 2.06 | +-3.08 | within noise |  |
| Warrior: units delivered to the egg | 0.078 (0.043 to 0.113), n 72 | 0.036 (0.014 to 0.058), n 72 | -0.042 | +-0.041 | **OUTSIDE noise** |  |
| Warrior: damage dealt to the Kaiju | 138.7 HP (123.6 to 153.7), n 72 | 167.0 HP (147.5 to 186.6), n 72 | 28.36 | +-24.66 | **OUTSIDE noise** |  |
| Warrior: share of all kills (whole class) | 0.404 (0.397 to 0.411), n 72 | 0.446 (0.438 to 0.453), n 72 | 0.042 | +-0.011 | **OUTSIDE noise** |  |
| Warrior: share of all fighter damage (whole class) | 0.429 (0.425 to 0.434), n 72 | 0.442 (0.437 to 0.447), n 72 | 0.013 | +-0.007 | **OUTSIDE noise** | <= 0.400: missed / missed |
| Farmer: damage dealt to fighters | 1895 HP (1849 to 1940), n 72 | 1788 HP (1744 to 1831), n 72 | -107.2 | +-62.77 | **OUTSIDE noise** |  |
| Farmer: kills | 12.06 (11.71 to 12.40), n 72 | 10.83 (10.51 to 11.15), n 72 | -1.23 | +-0.470 | **OUTSIDE noise** |  |
| Farmer: deaths | 8.35 (8.12 to 8.58), n 72 | 8.25 (8.01 to 8.49), n 72 | -0.099 | +-0.337 | within noise |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 86.05 s (83.28 to 88.83), n 72 | 86.44 s (83.42 to 89.45), n 72 | 0.384 | +-4.10 | within noise |  |
| Farmer: units delivered to the egg | 14.81 (14.59 to 15.02), n 72 | 15.04 (14.84 to 15.23), n 72 | 0.233 | +-0.291 | within noise |  |
| Farmer: damage dealt to the Kaiju | 811.6 HP (759.8 to 863.4), n 72 | 827.1 HP (777.7 to 876.5), n 72 | 15.53 | +-71.57 | within noise |  |
| Farmer: share of all kills (whole class) | 0.267 (0.260 to 0.273), n 72 | 0.245 (0.239 to 0.252), n 72 | -0.021 | +-0.009 | **OUTSIDE noise** |  |
| Farmer: share of all fighter damage (whole class) | 0.241 (0.237 to 0.245), n 72 | 0.231 (0.226 to 0.236), n 72 | -0.010 | +-0.006 | **OUTSIDE noise** |  |
| Miner: damage dealt to fighters | 1256 HP (1209 to 1302), n 72 | 804.5 HP (769.2 to 839.8), n 72 | -451.1 | +-58.20 | **OUTSIDE noise** |  |
| Miner: kills | 8.81 (8.38 to 9.23), n 72 | 5.20 (4.88 to 5.52), n 72 | -3.61 | +-0.530 | **OUTSIDE noise** |  |
| Miner: deaths | 7.91 (7.64 to 8.18), n 72 | 8.11 (7.83 to 8.39), n 72 | 0.205 | +-0.386 | within noise |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 66.13 s (63.61 to 68.64), n 72 | 63.75 s (61.06 to 66.44), n 72 | -2.38 | +-3.68 | within noise |  |
| Miner: units delivered to the egg | 0.458 (0.344 to 0.572), n 72 | 0.490 (0.381 to 0.599), n 72 | 0.031 | +-0.158 | within noise |  |
| Miner: damage dealt to the Kaiju | 174.3 HP (151.6 to 197.0), n 72 | 186.1 HP (158.6 to 213.5), n 72 | 11.76 | +-35.62 | within noise |  |
| Miner: share of all kills (whole class) | 0.097 (0.093 to 0.102), n 72 | 0.059 (0.055 to 0.063), n 72 | -0.038 | +-0.006 | **OUTSIDE noise** |  |
| Miner: share of all fighter damage (whole class) | 0.080 (0.077 to 0.083), n 72 | 0.052 (0.050 to 0.054), n 72 | -0.028 | +-0.004 | **OUTSIDE noise** |  |
| Demolitions: damage dealt to fighters | 3292 HP (3237 to 3346), n 72 | 3521 HP (3466 to 3576), n 72 | 229.1 | +-77.40 | **OUTSIDE noise** |  |
| Demolitions: kills | 17.39 (16.79 to 17.99), n 72 | 18.07 (17.52 to 18.63), n 72 | 0.681 | +-0.815 | within noise |  |
| Demolitions: deaths | 13.58 (13.33 to 13.83), n 72 | 13.44 (13.20 to 13.68), n 72 | -0.139 | +-0.349 | within noise |  |
| Demolitions: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Demolitions: objective credit (hill seconds scored, Actor.HillSeconds) | 149.4 s (146.0 to 152.8), n 72 | 149.4 s (146.3 to 152.5), n 72 | 0.000 | +-4.62 | within noise |  |
| Demolitions: units delivered to the egg | 0.368 (0.281 to 0.455), n 72 | 0.389 (0.311 to 0.466), n 72 | 0.021 | +-0.116 | within noise |  |
| Demolitions: damage dealt to the Kaiju | 186.5 HP (167.4 to 205.6), n 72 | 206.7 HP (181.8 to 231.5), n 72 | 20.14 | +-31.31 | within noise |  |
| Demolitions: share of all kills (whole class) | 0.192 (0.186 to 0.198), n 72 | 0.205 (0.199 to 0.211), n 72 | 0.013 | +-0.008 | **OUTSIDE noise** |  |
| Demolitions: share of all fighter damage (whole class) | 0.210 (0.206 to 0.213), n 72 | 0.228 (0.225 to 0.231), n 72 | 0.018 | +-0.005 | **OUTSIDE noise** |  |
| Builder: damage dealt to fighters | 1118 HP (1055 to 1181), n 72 | 1289 HP (1235 to 1344), n 72 | 171.3 | +-83.48 | **OUTSIDE noise** |  |
| Builder: kills | 6.99 (6.43 to 7.55), n 72 | 7.64 (7.06 to 8.22), n 72 | 0.653 | +-0.806 | within noise |  |
| Builder: deaths | 8.64 (8.26 to 9.02), n 72 | 8.30 (7.99 to 8.61), n 72 | -0.340 | +-0.491 | within noise |  |
| Builder: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Builder: objective credit (hill seconds scored, Actor.HillSeconds) | 95.99 s (92.43 to 99.54), n 72 | 95.26 s (91.72 to 98.81), n 72 | -0.722 | +-5.02 | within noise |  |
| Builder: units delivered to the egg | 4.45 (3.50 to 5.41), n 72 | 4.33 (3.36 to 5.29), n 72 | -0.125 | +-1.35 | within noise |  |
| Builder: damage dealt to the Kaiju | 187.3 HP (163.6 to 211.0), n 72 | 197.3 HP (170.3 to 224.4), n 72 | 10.02 | +-35.97 | within noise |  |
| Builder: share of all kills (whole class) | 0.039 (0.036 to 0.041), n 72 | 0.043 (0.040 to 0.047), n 72 | 0.005 | +-0.004 | **OUTSIDE noise** |  |
| Builder: share of all fighter damage (whole class) | 0.035 (0.034 to 0.037), n 72 | 0.042 (0.040 to 0.043), n 72 | 0.006 | +-0.003 | **OUTSIDE noise** |  |
| Healer: damage dealt to fighters | 75.76 HP (69.29 to 82.22), n 72 | 84.69 HP (77.25 to 92.14), n 72 | 8.94 | +-9.86 | within noise |  |
| Healer: kills | 0.118 (0.079 to 0.157), n 72 | 0.132 (0.089 to 0.175), n 72 | 0.014 | +-0.058 | within noise |  |
| Healer: deaths | 18.02 (17.73 to 18.32), n 72 | 17.99 (17.69 to 18.29), n 72 | -0.031 | +-0.419 | within noise |  |
| Healer: healing done (Healer mends and pulses) | 4899 HP (4819 to 4980), n 72 | 4893 HP (4808 to 4979), n 72 | -5.91 | +-117.2 | within noise |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 133.8 s (130.2 to 137.4), n 72 | 132.9 s (129.1 to 136.7), n 72 | -0.896 | +-5.25 | within noise |  |
| Healer: units delivered to the egg | 0.021 (-0.000 to 0.042), n 72 | 0.017 (-0.003 to 0.038), n 72 | -0.003 | +-0.029 | within noise |  |
| Healer: damage dealt to the Kaiju | 6.12 HP (3.77 to 8.47), n 72 | 6.94 HP (4.32 to 9.56), n 72 | 0.820 | +-3.52 | within noise |  |
| Healer: share of all kills (whole class) | 0.001 (0.001 to 0.002), n 72 | 0.001 (0.001 to 0.002), n 72 | 0.000 | +-0.001 | within noise |  |
| Healer: share of all fighter damage (whole class) | 0.005 (0.004 to 0.005), n 72 | 0.005 (0.005 to 0.006), n 72 | 0.001 | +-0.001 | **OUTSIDE noise** |  |
| Top-10 share of all fighter damage (10 of 32 bodies) | 0.498 (0.494 to 0.502), n 72 | 0.523 (0.520 to 0.527), n 72 | 0.025 | +-0.005 | **OUTSIDE noise** | <= 0.500: met / missed |
| Top-10 share of all kills | 0.540 (0.531 to 0.549), n 72 | 0.564 (0.556 to 0.571), n 72 | 0.024 | +-0.011 | **OUTSIDE noise** |  |
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
| Time to kill: Demolitions on a passive Warrior | 5.13 s (5.13 to 5.13), n 8 | 5.13 s (5.13 to 5.13), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Farmer | 4.43 s (4.43 to 4.43), n 8 | 3.73 s (3.73 to 3.73), n 8 | -0.700 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Miner | 3.73 s (3.73 to 3.73), n 8 | 3.03 s (3.03 to 3.03), n 8 | -0.700 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Demolitions | 5.13 s (5.13 to 5.13), n 8 | 4.43 s (4.43 to 4.43), n 8 | -0.700 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Builder | 5.13 s (5.13 to 5.13), n 8 | 4.43 s (4.43 to 4.43), n 8 | -0.700 | +-0.000 | **OUTSIDE noise** |  |
| Time to kill: Demolitions on a passive Healer | 4.43 s (4.43 to 4.43), n 8 | 3.73 s (3.73 to 3.73), n 8 | -0.700 | +-0.000 | **OUTSIDE noise** |  |
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
| Time to kill, median over the matchups that can kill | 4.23 s (3.97 to 4.93), n 36 | 4.00 s (3.73 to 4.40), n 36 | -0.233 | +-0.587 | within noise | 3.00 to 6.00: met / met |
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
| Elder share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.042 (0.014 to 0.115), n 72 | 0.042 | +-0.057 | within noise |  |
| Grove share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| Brute share of hatches | 0.333 (0.235 to 0.448), n 72 | 0.306 (0.211 to 0.420), n 72 | -0.028 | +-0.149 | within noise |  |
| Timber share of hatches | 0.403 (0.297 to 0.518), n 72 | 0.375 (0.272 to 0.490), n 72 | -0.028 | +-0.155 | within noise |  |
| Mire share of hatches | 0.264 (0.176 to 0.376), n 72 | 0.278 (0.188 to 0.390), n 72 | 0.014 | +-0.142 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.883 (0.778 to 0.942), n 60 | 0.724 (0.598 to 0.822), n 58 | -0.159 | +-0.139 | **OUTSIDE noise** |  |
| Berserk share of hatches | 0.167 (0.098 to 0.269), n 72 | 0.194 (0.120 to 0.300), n 72 | 0.028 | +-0.124 | within noise |  |
| Hut level 1 reached at (team-matches that reached it) | 16.50 s (16.37 to 16.63), n 144 | 16.50 s (16.37 to 16.63), n 144 | 0.000 | +-0.189 | within noise |  |
| Hut level 2 reached at (team-matches that reached it) | 62.83 s (62.73 to 62.97), n 144 | 62.58 s (62.47 to 62.87), n 144 | -0.250 | +-0.232 | **OUTSIDE noise** |  |
| Hut level 3 reached at (team-matches that reached it) | 122.4 s (118.4 to 131.3), n 144 | 123.3 s (119.2 to 131.3), n 144 | 0.883 | +-8.86 | within noise |  |
| Hut level 4 reached at (team-matches that reached it) | 284.1 s (276.0 to 297.7), n 143 | 283.6 s (275.4 to 296.1), n 138 | -0.483 | +-14.98 | within noise |  |
| Hut level 5 reached at (team-matches that reached it) | 437.4 s (426.3 to 448.1), n 53 | 445.5 s (434.8 to 452.9), n 53 | 8.07 | +-14.18 | within noise |  |
| Team-matches reaching level 5 | 0.368 (0.294 to 0.449), n 144 | 0.368 (0.294 to 0.449), n 144 | 0.000 | +-0.110 | within noise |  |
| Builder walls built | 3.26 (2.74 to 3.79), n 72 | 3.74 (3.09 to 4.38), n 72 | 0.472 | +-0.831 | within noise |  |
| Builder wall cells broken or crumbled | 3.06 (2.51 to 3.60), n 72 | 3.26 (2.66 to 3.87), n 72 | 0.208 | +-0.812 | within noise |  |
| Ladders and bridges placed | 5.26 (4.69 to 5.83), n 72 | 5.01 (4.48 to 5.55), n 72 | -0.250 | +-0.780 | within noise |  |
| Ladders and bridges destroyed | 0.806 (0.617 to 0.994), n 72 | 0.528 (0.312 to 0.744), n 72 | -0.278 | +-0.286 | within noise |  |
| Barricades placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades broken | 10.53 (8.75 to 12.31), n 72 | 11.22 (9.28 to 13.17), n 72 | 0.694 | +-2.64 | within noise |  |
| Decorations toppled | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 4.00 (3.00 to 5.00), n 72 | 4.00 (2.00 to 5.00), n 72 | 0.000 | +-1.80 | within noise | < 5.00: met / met |
| Stuck episodes per match (mean) | 7.89 (4.92 to 10.85), n 72 | 6.83 (4.87 to 8.79), n 72 | -1.06 | +-3.56 | within noise |  |
| Sim tick cost, mean (wall clock, this machine) | 78.40 us (76.15 to 80.64), n 72 | 76.11 us (73.35 to 78.87), n 72 | -2.29 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 740.2 us (735.4 to 748.1), n 72 | 729.3 us (723.3 to 735.5), n 72 | -10.88 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
