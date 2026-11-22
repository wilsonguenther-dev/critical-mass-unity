# Balance compare

- base: base aa2ecfef (aa2ecfef, MatchSeconds=240; HatchSeconds=120)
- head: head HEAD (f17242a7, MatchSeconds=240; HatchSeconds=120)
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

42 of 139 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.389 (0.285 to 0.504), n 72 | 0.403 (0.297 to 0.518), n 72 | 0.014 | +-0.156 | within noise |  |
| Crimson win share | 0.611 (0.496 to 0.715), n 72 | 0.597 (0.482 to 0.703), n 72 | -0.014 | +-0.156 | within noise | 0.400 to 0.600: missed / met |
| Draw share | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| West spawn (pad x=8) win share | 0.389 (0.285 to 0.504), n 72 | 0.403 (0.297 to 0.518), n 72 | 0.014 | +-0.156 | within noise |  |
| East spawn (pad x=104) win share | 0.611 (0.496 to 0.715), n 72 | 0.597 (0.482 to 0.703), n 72 | -0.014 | +-0.156 | within noise |  |
| Match length | 240.0 s (240.0 to 240.0), n 72 | 240.0 s (240.0 to 240.0), n 72 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.236 (0.153 to 0.346), n 72 | 0.333 (0.235 to 0.448), n 72 | 0.097 | +-0.144 | within noise | <= 0.150: missed / missed |
| Hill lead changes per match (median; spread = p25 to p75) | 3.00 (2.00 to 4.00), n 72 | 2.00 (2.00 to 3.00), n 72 | -1.00 | +-1.12 | within noise | >= 4.00: missed / missed |
| Hill lead changes per match (mean) | 3.22 (2.76 to 3.69), n 72 | 2.75 (2.39 to 3.11), n 72 | -0.472 | +-0.589 | within noise |  |
| Result decided at (last lead change) | 156.5 s (131.0 to 171.0), n 72 | 150.0 s (148.0 to 163.0), n 72 | -6.50 | +-21.36 | within noise |  |
| Result decided at, share of the match | 0.581 (0.518 to 0.644), n 72 | 0.625 (0.581 to 0.669), n 72 | 0.044 | +-0.077 | within noise |  |
| Leader at half time wins | 0.493 (0.380 to 0.607), n 71 | 0.324 (0.227 to 0.439), n 71 | -0.169 | +-0.155 | **OUTSIDE noise** | 0.600 to 0.800: missed / missed |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.879 (0.870 to 0.889), n 72 | 0.835 (0.821 to 0.849), n 72 | -0.044 | +-0.016 | **OUTSIDE noise** |  |
| Longest unbroken hill hold | 61.30 s (54.03 to 68.63), n 72 | 60.23 s (55.57 to 74.17), n 72 | -1.07 | +-11.82 | within noise |  |
| Warrior: dead or getting up | 0.072 (0.070 to 0.074), n 72 | 0.107 (0.105 to 0.110), n 72 | 0.035 | +-0.003 | **OUTSIDE noise** |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.286 (0.280 to 0.293), n 72 | 0.287 (0.282 to 0.292), n 72 | 0.000 | +-0.008 | within noise |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.326 (0.319 to 0.333), n 72 | 0.311 (0.305 to 0.316), n 72 | -0.016 | +-0.009 | **OUTSIDE noise** |  |
| Warrior: dead or walking | 0.358 (0.351 to 0.366), n 72 | 0.394 (0.387 to 0.400), n 72 | 0.035 | +-0.010 | **OUTSIDE noise** |  |
| Warrior: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.191 (0.186 to 0.196), n 72 | 0.193 (0.188 to 0.198), n 72 | 0.002 | +-0.007 | within noise |  |
| Warrior: dead or walking back after a respawn | 0.246 (0.239 to 0.252), n 72 | 0.284 (0.277 to 0.290), n 72 | 0.038 | +-0.009 | **OUTSIDE noise** |  |
| Farmer: dead or getting up | 0.044 (0.042 to 0.046), n 72 | 0.067 (0.064 to 0.070), n 72 | 0.023 | +-0.004 | **OUTSIDE noise** |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.290 (0.283 to 0.296), n 72 | 0.302 (0.294 to 0.310), n 72 | 0.013 | +-0.010 | **OUTSIDE noise** |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.276 (0.271 to 0.282), n 72 | 0.277 (0.271 to 0.284), n 72 | 0.001 | +-0.009 | within noise |  |
| Farmer: dead or walking | 0.334 (0.326 to 0.341), n 72 | 0.369 (0.360 to 0.379), n 72 | 0.036 | +-0.012 | **OUTSIDE noise** |  |
| Farmer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.109 (0.103 to 0.114), n 72 | 0.104 (0.100 to 0.109), n 72 | -0.004 | +-0.007 | within noise |  |
| Farmer: dead or walking back after a respawn | 0.142 (0.136 to 0.149), n 72 | 0.162 (0.155 to 0.169), n 72 | 0.020 | +-0.010 | **OUTSIDE noise** |  |
| Miner: dead or getting up | 0.034 (0.032 to 0.035), n 72 | 0.051 (0.049 to 0.054), n 72 | 0.018 | +-0.003 | **OUTSIDE noise** |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.395 (0.390 to 0.399), n 72 | 0.380 (0.374 to 0.386), n 72 | -0.015 | +-0.008 | **OUTSIDE noise** |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.113 (0.109 to 0.118), n 72 | 0.111 (0.106 to 0.116), n 72 | -0.002 | +-0.006 | within noise |  |
| Miner: dead or walking | 0.428 (0.423 to 0.434), n 72 | 0.431 (0.425 to 0.438), n 72 | 0.003 | +-0.009 | within noise |  |
| Miner: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.074 (0.071 to 0.078), n 72 | 0.069 (0.066 to 0.073), n 72 | -0.005 | +-0.005 | within noise |  |
| Miner: dead or walking back after a respawn | 0.100 (0.096 to 0.105), n 72 | 0.114 (0.108 to 0.120), n 72 | 0.014 | +-0.007 | **OUTSIDE noise** |  |
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
| Healer: dead or getting up | 0.107 (0.104 to 0.110), n 72 | 0.157 (0.154 to 0.161), n 72 | 0.050 | +-0.005 | **OUTSIDE noise** |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.375 (0.367 to 0.382), n 72 | 0.404 (0.399 to 0.410), n 72 | 0.030 | +-0.009 | **OUTSIDE noise** |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.217 (0.214 to 0.220), n 72 | 0.215 (0.213 to 0.218), n 72 | -0.001 | +-0.004 | within noise |  |
| Healer: dead or walking | 0.481 (0.472 to 0.491), n 72 | 0.561 (0.553 to 0.569), n 72 | 0.080 | +-0.012 | **OUTSIDE noise** |  |
| Healer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.279 (0.272 to 0.287), n 72 | 0.279 (0.272 to 0.286), n 72 | -0.000 | +-0.010 | within noise |  |
| Healer: dead or walking back after a respawn | 0.360 (0.350 to 0.369), n 72 | 0.411 (0.402 to 0.420), n 72 | 0.052 | +-0.013 | **OUTSIDE noise** |  |
| all: dead or getting up | 0.060 (0.058 to 0.061), n 72 | 0.089 (0.088 to 0.091), n 72 | 0.030 | +-0.002 | **OUTSIDE noise** |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.325 (0.322 to 0.329), n 72 | 0.329 (0.325 to 0.332), n 72 | 0.003 | +-0.005 | within noise |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.247 (0.244 to 0.250), n 72 | 0.240 (0.238 to 0.243), n 72 | -0.006 | +-0.004 | **OUTSIDE noise** |  |
| all: dead or walking | 0.385 (0.381 to 0.389), n 72 | 0.418 (0.414 to 0.422), n 72 | 0.033 | +-0.006 | **OUTSIDE noise** |  |
| all: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.152 (0.149 to 0.155), n 72 | 0.151 (0.148 to 0.154), n 72 | -0.001 | +-0.004 | within noise |  |
| all: dead or walking back after a respawn | 0.198 (0.194 to 0.202), n 72 | 0.227 (0.223 to 0.231), n 72 | 0.029 | +-0.006 | **OUTSIDE noise** | <= 0.280: met / met |
| Warrior: damage dealt to fighters | 1046 HP (1023 to 1070), n 72 | 954.3 HP (931.3 to 977.4), n 72 | -92.07 | +-32.67 | **OUTSIDE noise** |  |
| Warrior: kills | 5.64 (5.40 to 5.87), n 72 | 5.09 (4.89 to 5.29), n 72 | -0.546 | +-0.308 | **OUTSIDE noise** |  |
| Warrior: deaths | 5.32 (5.17 to 5.47), n 72 | 5.13 (5.01 to 5.24), n 72 | -0.194 | +-0.187 | **OUTSIDE noise** |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 82.20 s (80.94 to 83.45), n 72 | 73.23 s (71.58 to 74.89), n 72 | -8.96 | +-2.08 | **OUTSIDE noise** |  |
| Warrior: units delivered to the egg | 0.035 (0.009 to 0.061), n 72 | 0.000 (0.000 to 0.000), n 72 | -0.035 | +-0.026 | **OUTSIDE noise** |  |
| Warrior: damage dealt to the Kaiju | 122.7 HP (110.6 to 134.7), n 72 | 118.5 HP (106.2 to 130.7), n 72 | -4.20 | +-17.17 | within noise |  |
| Warrior: share of all kills (whole class) | 0.518 (0.503 to 0.533), n 72 | 0.500 (0.484 to 0.517), n 72 | -0.017 | +-0.022 | within noise |  |
| Warrior: share of all fighter damage (whole class) | 0.559 (0.550 to 0.568), n 72 | 0.546 (0.535 to 0.556), n 72 | -0.013 | +-0.014 | within noise | <= 0.400: missed / missed |
| Farmer: damage dealt to fighters | 821.9 HP (799.5 to 844.3), n 72 | 801.4 HP (775.9 to 827.0), n 72 | -20.48 | +-34.00 | within noise |  |
| Farmer: kills | 5.04 (4.81 to 5.27), n 72 | 4.98 (4.75 to 5.20), n 72 | -0.066 | +-0.320 | within noise |  |
| Farmer: deaths | 3.15 (3.01 to 3.29), n 72 | 2.92 (2.79 to 3.06), n 72 | -0.229 | +-0.194 | **OUTSIDE noise** |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 41.66 s (40.16 to 43.16), n 72 | 22.97 s (21.60 to 24.34), n 72 | -18.69 | +-2.03 | **OUTSIDE noise** |  |
| Farmer: units delivered to the egg | 10.99 (10.72 to 11.26), n 72 | 11.25 (10.90 to 11.61), n 72 | 0.267 | +-0.448 | within noise |  |
| Farmer: damage dealt to the Kaiju | 524.6 HP (498.8 to 550.4), n 72 | 527.4 HP (499.9 to 554.9), n 72 | 2.80 | +-37.73 | within noise |  |
| Farmer: share of all kills (whole class) | 0.312 (0.297 to 0.326), n 72 | 0.327 (0.313 to 0.340), n 72 | 0.015 | +-0.020 | within noise |  |
| Farmer: share of all fighter damage (whole class) | 0.293 (0.286 to 0.301), n 72 | 0.306 (0.297 to 0.315), n 72 | 0.013 | +-0.012 | **OUTSIDE noise** |  |
| Miner: damage dealt to fighters | 371.2 HP (351.3 to 391.1), n 72 | 336.5 HP (316.8 to 356.3), n 72 | -34.66 | +-28.06 | **OUTSIDE noise** |  |
| Miner: kills | 2.70 (2.52 to 2.88), n 72 | 2.56 (2.34 to 2.77), n 72 | -0.146 | +-0.280 | within noise |  |
| Miner: deaths | 2.30 (2.20 to 2.40), n 72 | 2.12 (2.02 to 2.23), n 72 | -0.177 | +-0.144 | **OUTSIDE noise** |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 21.07 s (19.67 to 22.47), n 72 | 20.06 s (18.25 to 21.87), n 72 | -1.01 | +-2.29 | within noise |  |
| Miner: units delivered to the egg | 0.049 (0.019 to 0.079), n 72 | 0.066 (0.030 to 0.102), n 72 | 0.017 | +-0.047 | within noise |  |
| Miner: damage dealt to the Kaiju | 102.9 HP (87.78 to 118.1), n 72 | 113.7 HP (99.17 to 128.2), n 72 | 10.74 | +-20.99 | within noise |  |
| Miner: share of all kills (whole class) | 0.167 (0.156 to 0.178), n 72 | 0.168 (0.154 to 0.183), n 72 | 0.001 | +-0.018 | within noise |  |
| Miner: share of all fighter damage (whole class) | 0.132 (0.125 to 0.139), n 72 | 0.128 (0.121 to 0.136), n 72 | -0.004 | +-0.010 | within noise |  |
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
| Healer: damage dealt to fighters | 88.33 HP (80.85 to 95.81), n 72 | 103.8 HP (94.77 to 112.8), n 72 | 15.45 | +-11.71 | **OUTSIDE noise** |  |
| Healer: kills | 0.132 (0.077 to 0.187), n 72 | 0.146 (0.072 to 0.219), n 72 | 0.014 | +-0.092 | within noise |  |
| Healer: deaths | 8.08 (7.86 to 8.29), n 72 | 7.69 (7.53 to 7.84), n 72 | -0.389 | +-0.265 | **OUTSIDE noise** |  |
| Healer: healing done (Healer mends and pulses) | 1668 HP (1630 to 1707), n 72 | 1532 HP (1498 to 1567), n 72 | -136.0 | +-51.52 | **OUTSIDE noise** |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 67.69 s (65.82 to 69.57), n 72 | 40.52 s (38.49 to 42.56), n 72 | -27.17 | +-2.77 | **OUTSIDE noise** |  |
| Healer: units delivered to the egg | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Healer: damage dealt to the Kaiju | 8.65 HP (6.12 to 11.17), n 72 | 10.94 HP (8.13 to 13.74), n 72 | 2.29 | +-3.78 | within noise |  |
| Healer: share of all kills (whole class) | 0.004 (0.002 to 0.006), n 72 | 0.005 (0.002 to 0.007), n 72 | 0.001 | +-0.003 | within noise |  |
| Healer: share of all fighter damage (whole class) | 0.016 (0.014 to 0.017), n 72 | 0.020 (0.018 to 0.022), n 72 | 0.004 | +-0.002 | **OUTSIDE noise** |  |
| Top-10 share of all fighter damage (10 of 16 bodies) | 0.866 (0.861 to 0.871), n 72 | 0.875 (0.869 to 0.881), n 72 | 0.009 | +-0.008 | **OUTSIDE noise** | <= 0.500: missed / missed |
| Top-10 share of all kills | 0.911 (0.904 to 0.919), n 72 | 0.916 (0.908 to 0.924), n 72 | 0.005 | +-0.011 | within noise |  |
| Matches where the egg hatched | 1.00 (0.949 to 1.000), n 72 | 1.00 (0.949 to 1.000), n 72 | 0.000 | +-0.036 | within noise |  |
| Elder share of hatches | 0.069 (0.030 to 0.152), n 72 | 0.000 (0.000 to 0.051), n 72 | -0.069 | +-0.066 | **OUTSIDE noise** |  |
| Grove share of hatches | 0.403 (0.297 to 0.518), n 72 | 0.653 (0.538 to 0.752), n 72 | 0.250 | +-0.154 | **OUTSIDE noise** |  |
| Brute share of hatches | 0.069 (0.030 to 0.152), n 72 | 0.000 (0.000 to 0.051), n 72 | -0.069 | +-0.066 | **OUTSIDE noise** |  |
| Timber share of hatches | 0.264 (0.176 to 0.376), n 72 | 0.153 (0.088 to 0.253), n 72 | -0.111 | +-0.130 | within noise |  |
| Mire share of hatches | 0.194 (0.120 to 0.300), n 72 | 0.194 (0.120 to 0.300), n 72 | 0.000 | +-0.128 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.845 (0.743 to 0.911), n 71 | 0.958 (0.885 to 0.986), n 72 | 0.113 | +-0.098 | **OUTSIDE noise** | <= 0.700: missed / missed |
| Berserk share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| Hut level 1 reached at (team-matches that reached it) | 16.28 s (16.27 to 16.30), n 144 | 16.28 s (16.27 to 16.30), n 144 | 0.000 | +-0.024 | within noise |  |
| Hut level 2 reached at (team-matches that reached it) | 61.82 s (61.43 to 63.00), n 144 | 63.40 s (62.00 to 68.13), n 144 | 1.58 | +-3.17 | within noise |  |
| Hut level 3 reached at (team-matches that reached it) | 128.2 s (122.3 to 131.2), n 144 | 129.5 s (123.9 to 131.9), n 144 | 1.30 | +-6.00 | within noise |  |
| Hut level 4 reached at (team-matches that reached it) | 232.2 s (228.8 to 234.1), n 39 | 230.4 s (227.7 to 233.9), n 36 | -1.75 | +-4.07 | within noise |  |
| Hut level 5 reached at (team-matches that reached it) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Team-matches reaching level 5 | 0.000 (0.000 to 0.026), n 144 | 0.000 (0.000 to 0.026), n 144 | 0.000 | +-0.018 | within noise |  |
| Builder walls built | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Builder wall cells broken or crumbled | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Ladders and bridges placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Ladders and bridges destroyed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades broken | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Decorations toppled | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 1.00 (0.000 to 1.00), n 72 | 0.000 (0.000 to 1.00), n 72 | -1.00 | +-0.707 | **OUTSIDE noise** | < 5.00: met / met |
| Stuck episodes per match (mean) | 1.68 (1.08 to 2.28), n 72 | 1.04 (0.694 to 1.39), n 72 | -0.639 | +-0.691 | within noise |  |
| Sim tick cost, mean (wall clock, this machine) | 32.39 us (30.01 to 34.77), n 72 | 34.96 us (31.07 to 38.84), n 72 | 2.57 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 267.3 us (257.2 to 338.4), n 72 | 344.4 us (260.1 to 361.3), n 72 | 77.12 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
