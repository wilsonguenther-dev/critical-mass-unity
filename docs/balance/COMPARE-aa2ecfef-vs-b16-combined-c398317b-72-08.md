# Balance compare

- base: base aa2ecfef (aa2ecfef, MatchSeconds=240; HatchSeconds=120)
- head: head HEAD (19af4ffd, MatchSeconds=240; HatchSeconds=120)
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

46 of 139 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.389 (0.285 to 0.504), n 72 | 0.500 (0.387 to 0.613), n 72 | 0.111 | +-0.157 | within noise |  |
| Crimson win share | 0.611 (0.496 to 0.715), n 72 | 0.486 (0.374 to 0.599), n 72 | -0.125 | +-0.157 | within noise | 0.400 to 0.600: missed / met |
| Draw share | 0.000 (0.000 to 0.051), n 72 | 0.014 (0.002 to 0.075), n 72 | 0.014 | +-0.044 | within noise |  |
| West spawn (pad x=8) win share | 0.389 (0.285 to 0.504), n 72 | 0.500 (0.387 to 0.613), n 72 | 0.111 | +-0.157 | within noise |  |
| East spawn (pad x=104) win share | 0.611 (0.496 to 0.715), n 72 | 0.486 (0.374 to 0.599), n 72 | -0.125 | +-0.157 | within noise |  |
| Match length | 240.0 s (240.0 to 240.0), n 72 | 240.0 s (240.0 to 240.0), n 72 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.236 (0.153 to 0.346), n 72 | 0.264 (0.176 to 0.376), n 72 | 0.028 | +-0.139 | within noise | <= 0.150: missed / missed |
| Hill lead changes per match (median; spread = p25 to p75) | 3.00 (2.00 to 4.00), n 72 | 3.00 (2.00 to 3.00), n 72 | 0.000 | +-1.12 | within noise | >= 4.00: missed / missed |
| Hill lead changes per match (mean) | 3.22 (2.76 to 3.69), n 72 | 2.78 (2.40 to 3.16), n 72 | -0.444 | +-0.601 | within noise |  |
| Result decided at (last lead change) | 156.5 s (131.0 to 171.0), n 72 | 144.5 s (104.0 to 168.0), n 72 | -12.00 | +-37.74 | within noise |  |
| Result decided at, share of the match | 0.581 (0.518 to 0.644), n 72 | 0.545 (0.474 to 0.615), n 72 | -0.036 | +-0.095 | within noise |  |
| Leader at half time wins | 0.493 (0.380 to 0.607), n 71 | 0.500 (0.386 to 0.614), n 70 | 0.007 | +-0.161 | within noise | 0.600 to 0.800: missed / missed |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.879 (0.870 to 0.889), n 72 | 0.925 (0.917 to 0.933), n 72 | 0.045 | +-0.012 | **OUTSIDE noise** |  |
| Longest unbroken hill hold | 61.30 s (54.03 to 68.63), n 72 | 80.95 s (69.13 to 88.50), n 72 | 19.65 | +-12.13 | **OUTSIDE noise** |  |
| Warrior: dead or getting up | 0.072 (0.070 to 0.074), n 72 | 0.092 (0.089 to 0.095), n 72 | 0.020 | +-0.004 | **OUTSIDE noise** |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.286 (0.280 to 0.293), n 72 | 0.276 (0.271 to 0.281), n 72 | -0.011 | +-0.008 | **OUTSIDE noise** |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.326 (0.319 to 0.333), n 72 | 0.332 (0.325 to 0.339), n 72 | 0.006 | +-0.010 | within noise |  |
| Warrior: dead or walking | 0.358 (0.351 to 0.366), n 72 | 0.368 (0.361 to 0.374), n 72 | 0.009 | +-0.010 | within noise |  |
| Warrior: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.191 (0.186 to 0.196), n 72 | 0.173 (0.169 to 0.178), n 72 | -0.018 | +-0.007 | **OUTSIDE noise** |  |
| Warrior: dead or walking back after a respawn | 0.246 (0.239 to 0.252), n 72 | 0.251 (0.245 to 0.258), n 72 | 0.006 | +-0.010 | within noise |  |
| Farmer: dead or getting up | 0.044 (0.042 to 0.046), n 72 | 0.059 (0.056 to 0.062), n 72 | 0.015 | +-0.004 | **OUTSIDE noise** |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.290 (0.283 to 0.296), n 72 | 0.281 (0.274 to 0.289), n 72 | -0.008 | +-0.010 | within noise |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.276 (0.271 to 0.282), n 72 | 0.267 (0.261 to 0.274), n 72 | -0.009 | +-0.009 | **OUTSIDE noise** |  |
| Farmer: dead or walking | 0.334 (0.326 to 0.341), n 72 | 0.340 (0.331 to 0.348), n 72 | 0.006 | +-0.011 | within noise |  |
| Farmer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.109 (0.103 to 0.114), n 72 | 0.091 (0.086 to 0.096), n 72 | -0.018 | +-0.007 | **OUTSIDE noise** |  |
| Farmer: dead or walking back after a respawn | 0.142 (0.136 to 0.149), n 72 | 0.141 (0.134 to 0.149), n 72 | -0.001 | +-0.010 | within noise |  |
| Miner: dead or getting up | 0.034 (0.032 to 0.035), n 72 | 0.048 (0.045 to 0.051), n 72 | 0.014 | +-0.003 | **OUTSIDE noise** |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.395 (0.390 to 0.399), n 72 | 0.375 (0.369 to 0.382), n 72 | -0.019 | +-0.008 | **OUTSIDE noise** |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.113 (0.109 to 0.118), n 72 | 0.121 (0.114 to 0.127), n 72 | 0.007 | +-0.008 | within noise |  |
| Miner: dead or walking | 0.428 (0.423 to 0.434), n 72 | 0.423 (0.416 to 0.431), n 72 | -0.005 | +-0.009 | within noise |  |
| Miner: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.074 (0.071 to 0.078), n 72 | 0.064 (0.060 to 0.068), n 72 | -0.010 | +-0.005 | **OUTSIDE noise** |  |
| Miner: dead or walking back after a respawn | 0.100 (0.096 to 0.105), n 72 | 0.106 (0.100 to 0.112), n 72 | 0.006 | +-0.008 | within noise |  |
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
| Healer: dead or getting up | 0.107 (0.104 to 0.110), n 72 | 0.150 (0.146 to 0.154), n 72 | 0.043 | +-0.005 | **OUTSIDE noise** |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.375 (0.367 to 0.382), n 72 | 0.363 (0.355 to 0.371), n 72 | -0.012 | +-0.011 | **OUTSIDE noise** |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.217 (0.214 to 0.220), n 72 | 0.198 (0.194 to 0.201), n 72 | -0.019 | +-0.004 | **OUTSIDE noise** |  |
| Healer: dead or walking | 0.481 (0.472 to 0.491), n 72 | 0.513 (0.502 to 0.523), n 72 | 0.031 | +-0.014 | **OUTSIDE noise** |  |
| Healer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.279 (0.272 to 0.287), n 72 | 0.270 (0.263 to 0.277), n 72 | -0.010 | +-0.010 | within noise |  |
| Healer: dead or walking back after a respawn | 0.360 (0.350 to 0.369), n 72 | 0.396 (0.386 to 0.406), n 72 | 0.036 | +-0.014 | **OUTSIDE noise** |  |
| all: dead or getting up | 0.060 (0.058 to 0.061), n 72 | 0.080 (0.078 to 0.082), n 72 | 0.020 | +-0.002 | **OUTSIDE noise** |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.325 (0.322 to 0.329), n 72 | 0.313 (0.309 to 0.317), n 72 | -0.012 | +-0.005 | **OUTSIDE noise** |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.247 (0.244 to 0.250), n 72 | 0.246 (0.243 to 0.249), n 72 | -0.001 | +-0.004 | within noise |  |
| all: dead or walking | 0.385 (0.381 to 0.389), n 72 | 0.393 (0.388 to 0.397), n 72 | 0.008 | +-0.006 | **OUTSIDE noise** |  |
| all: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.152 (0.149 to 0.155), n 72 | 0.137 (0.135 to 0.140), n 72 | -0.015 | +-0.004 | **OUTSIDE noise** |  |
| all: dead or walking back after a respawn | 0.198 (0.194 to 0.202), n 72 | 0.206 (0.202 to 0.210), n 72 | 0.008 | +-0.006 | **OUTSIDE noise** | <= 0.280: met / met |
| Warrior: damage dealt to fighters | 1046 HP (1023 to 1070), n 72 | 1009 HP (988.9 to 1029), n 72 | -37.64 | +-30.50 | **OUTSIDE noise** |  |
| Warrior: kills | 5.64 (5.40 to 5.87), n 72 | 5.54 (5.31 to 5.77), n 72 | -0.097 | +-0.327 | within noise |  |
| Warrior: deaths | 5.32 (5.17 to 5.47), n 72 | 4.38 (4.26 to 4.50), n 72 | -0.937 | +-0.192 | **OUTSIDE noise** |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 82.20 s (80.94 to 83.45), n 72 | 86.59 s (85.16 to 88.03), n 72 | 4.40 | +-1.91 | **OUTSIDE noise** |  |
| Warrior: units delivered to the egg | 0.035 (0.009 to 0.061), n 72 | 0.046 (0.016 to 0.077), n 72 | 0.012 | +-0.040 | within noise |  |
| Warrior: damage dealt to the Kaiju | 122.7 HP (110.6 to 134.7), n 72 | 147.7 HP (128.9 to 166.5), n 72 | 25.05 | +-22.34 | **OUTSIDE noise** |  |
| Warrior: share of all kills (whole class) | 0.518 (0.503 to 0.533), n 72 | 0.591 (0.573 to 0.609), n 72 | 0.073 | +-0.023 | **OUTSIDE noise** |  |
| Warrior: share of all fighter damage (whole class) | 0.559 (0.550 to 0.568), n 72 | 0.618 (0.609 to 0.628), n 72 | 0.059 | +-0.013 | **OUTSIDE noise** | <= 0.400: missed / missed |
| Farmer: damage dealt to fighters | 821.9 HP (799.5 to 844.3), n 72 | 684.1 HP (659.1 to 709.1), n 72 | -137.8 | +-33.55 | **OUTSIDE noise** |  |
| Farmer: kills | 5.04 (4.81 to 5.27), n 72 | 4.19 (3.96 to 4.42), n 72 | -0.851 | +-0.325 | **OUTSIDE noise** |  |
| Farmer: deaths | 3.15 (3.01 to 3.29), n 72 | 2.58 (2.46 to 2.71), n 72 | -0.569 | +-0.188 | **OUTSIDE noise** |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 41.66 s (40.16 to 43.16), n 72 | 43.44 s (41.74 to 45.15), n 72 | 1.79 | +-2.27 | within noise |  |
| Farmer: units delivered to the egg | 10.99 (10.72 to 11.26), n 72 | 11.08 (10.77 to 11.39), n 72 | 0.097 | +-0.413 | within noise |  |
| Farmer: damage dealt to the Kaiju | 524.6 HP (498.8 to 550.4), n 72 | 491.3 HP (457.1 to 525.6), n 72 | -33.26 | +-42.89 | within noise |  |
| Farmer: share of all kills (whole class) | 0.312 (0.297 to 0.326), n 72 | 0.300 (0.283 to 0.316), n 72 | -0.012 | +-0.022 | within noise |  |
| Farmer: share of all fighter damage (whole class) | 0.293 (0.286 to 0.301), n 72 | 0.279 (0.270 to 0.288), n 72 | -0.014 | +-0.012 | **OUTSIDE noise** |  |
| Miner: damage dealt to fighters | 371.2 HP (351.3 to 391.1), n 72 | 204.0 HP (190.8 to 217.1), n 72 | -167.2 | +-23.88 | **OUTSIDE noise** |  |
| Miner: kills | 2.70 (2.52 to 2.88), n 72 | 1.44 (1.30 to 1.58), n 72 | -1.26 | +-0.226 | **OUTSIDE noise** |  |
| Miner: deaths | 2.30 (2.20 to 2.40), n 72 | 2.03 (1.91 to 2.14), n 72 | -0.271 | +-0.153 | **OUTSIDE noise** |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 72 | 0.000 HP (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 21.07 s (19.67 to 22.47), n 72 | 22.06 s (20.26 to 23.86), n 72 | 0.985 | +-2.28 | within noise |  |
| Miner: units delivered to the egg | 0.049 (0.019 to 0.079), n 72 | 0.063 (0.022 to 0.103), n 72 | 0.014 | +-0.051 | within noise |  |
| Miner: damage dealt to the Kaiju | 102.9 HP (87.78 to 118.1), n 72 | 100.5 HP (83.03 to 117.9), n 72 | -2.48 | +-23.11 | within noise |  |
| Miner: share of all kills (whole class) | 0.167 (0.156 to 0.178), n 72 | 0.104 (0.093 to 0.115), n 72 | -0.063 | +-0.015 | **OUTSIDE noise** |  |
| Miner: share of all fighter damage (whole class) | 0.132 (0.125 to 0.139), n 72 | 0.083 (0.078 to 0.088), n 72 | -0.049 | +-0.008 | **OUTSIDE noise** |  |
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
| Healer: damage dealt to fighters | 88.33 HP (80.85 to 95.81), n 72 | 95.91 HP (86.87 to 105.0), n 72 | 7.58 | +-11.74 | within noise |  |
| Healer: kills | 0.132 (0.077 to 0.187), n 72 | 0.146 (0.080 to 0.211), n 72 | 0.014 | +-0.085 | within noise |  |
| Healer: deaths | 8.08 (7.86 to 8.29), n 72 | 7.40 (7.24 to 7.57), n 72 | -0.674 | +-0.271 | **OUTSIDE noise** |  |
| Healer: healing done (Healer mends and pulses) | 1668 HP (1630 to 1707), n 72 | 1487 HP (1443 to 1530), n 72 | -181.7 | +-57.81 | **OUTSIDE noise** |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 67.69 s (65.82 to 69.57), n 72 | 65.41 s (62.98 to 67.85), n 72 | -2.28 | +-3.07 | within noise |  |
| Healer: units delivered to the egg | 0.000 (0.000 to 0.000), n 72 | 0.056 (-0.010 to 0.121), n 72 | 0.056 | +-0.066 | within noise |  |
| Healer: damage dealt to the Kaiju | 8.65 HP (6.12 to 11.17), n 72 | 11.67 HP (8.44 to 14.89), n 72 | 3.02 | +-4.10 | within noise |  |
| Healer: share of all kills (whole class) | 0.004 (0.002 to 0.006), n 72 | 0.005 (0.003 to 0.007), n 72 | 0.001 | +-0.003 | within noise |  |
| Healer: share of all fighter damage (whole class) | 0.016 (0.014 to 0.017), n 72 | 0.020 (0.018 to 0.021), n 72 | 0.004 | +-0.002 | **OUTSIDE noise** |  |
| Top-10 share of all fighter damage (10 of 16 bodies) | 0.866 (0.861 to 0.871), n 72 | 0.901 (0.895 to 0.906), n 72 | 0.034 | +-0.007 | **OUTSIDE noise** | <= 0.500: missed / missed |
| Top-10 share of all kills | 0.911 (0.904 to 0.919), n 72 | 0.942 (0.935 to 0.950), n 72 | 0.031 | +-0.010 | **OUTSIDE noise** |  |
| Matches where the egg hatched | 1.00 (0.949 to 1.000), n 72 | 1.00 (0.949 to 1.000), n 72 | 0.000 | +-0.036 | within noise |  |
| Elder share of hatches | 0.069 (0.030 to 0.152), n 72 | 0.014 (0.002 to 0.075), n 72 | -0.056 | +-0.071 | within noise |  |
| Grove share of hatches | 0.403 (0.297 to 0.518), n 72 | 0.681 (0.566 to 0.777), n 72 | 0.278 | +-0.153 | **OUTSIDE noise** |  |
| Brute share of hatches | 0.069 (0.030 to 0.152), n 72 | 0.000 (0.000 to 0.051), n 72 | -0.069 | +-0.066 | **OUTSIDE noise** |  |
| Timber share of hatches | 0.264 (0.176 to 0.376), n 72 | 0.083 (0.039 to 0.170), n 72 | -0.181 | +-0.120 | **OUTSIDE noise** |  |
| Mire share of hatches | 0.194 (0.120 to 0.300), n 72 | 0.222 (0.142 to 0.331), n 72 | 0.028 | +-0.131 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.845 (0.743 to 0.911), n 71 | 0.829 (0.724 to 0.899), n 70 | -0.016 | +-0.121 | within noise | <= 0.700: missed / missed |
| Berserk share of hatches | 0.000 (0.000 to 0.051), n 72 | 0.000 (0.000 to 0.051), n 72 | 0.000 | +-0.036 | within noise |  |
| Hut level 1 reached at (team-matches that reached it) | 16.28 s (16.27 to 16.30), n 144 | 16.25 s (16.23 to 16.27), n 144 | -0.033 | +-0.024 | **OUTSIDE noise** |  |
| Hut level 2 reached at (team-matches that reached it) | 61.82 s (61.43 to 63.00), n 144 | 62.10 s (61.60 to 65.83), n 144 | 0.283 | +-2.26 | within noise |  |
| Hut level 3 reached at (team-matches that reached it) | 128.2 s (122.3 to 131.2), n 144 | 131.3 s (126.9 to 132.3), n 144 | 3.03 | +-5.23 | within noise |  |
| Hut level 4 reached at (team-matches that reached it) | 232.2 s (228.8 to 234.1), n 39 | 227.6 s (221.2 to 233.5), n 23 | -4.63 | +-6.73 | within noise |  |
| Hut level 5 reached at (team-matches that reached it) | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Team-matches reaching level 5 | 0.000 (0.000 to 0.026), n 144 | 0.000 (0.000 to 0.026), n 144 | 0.000 | +-0.018 | within noise |  |
| Builder walls built | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Builder wall cells broken or crumbled | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Ladders and bridges placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Ladders and bridges destroyed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades placed | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Barricades broken | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Decorations toppled | 0.000 (0.000 to 0.000), n 72 | 0.000 (0.000 to 0.000), n 72 | 0.000 | +-0.000 | within noise |  |
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 1.00 (0.000 to 1.00), n 72 | 1.00 (0.000 to 1.00), n 72 | 0.000 | +-0.707 | within noise | < 5.00: met / met |
| Stuck episodes per match (mean) | 1.68 (1.08 to 2.28), n 72 | 0.903 (0.623 to 1.18), n 72 | -0.778 | +-0.659 | **OUTSIDE noise** |  |
| Sim tick cost, mean (wall clock, this machine) | 23.85 us (21.63 to 26.07), n 72 | 25.82 us (22.23 to 29.41), n 72 | 1.97 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 220.5 us (213.0 to 267.8), n 72 | 224.3 us (217.1 to 302.1), n 72 | 3.89 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 72 | 0, n 72 | 0.000 |  | not judged |  |
