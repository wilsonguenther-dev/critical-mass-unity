# Balance compare

- base: base 1ea0ff780ed747332b007056137b45dc5141bf93 (1ea0ff78, none (the game's values))
- head: head  (acc27a69, none (the game's values))
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

112 of 208 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.375 (0.212 to 0.573), n 24 | 0.375 (0.212 to 0.573), n 24 | 0.000 | +-0.255 | within noise |  |
| Crimson win share | 0.625 (0.427 to 0.788), n 24 | 0.583 (0.388 to 0.755), n 24 | -0.042 | +-0.257 | within noise |  |
| Draw share | 0.000 (0.000 to 0.138), n 24 | 0.042 (0.007 to 0.202), n 24 | 0.042 | +-0.119 | within noise |  |
| West spawn (pad x=8) win share | 0.375 (0.212 to 0.573), n 24 | 0.375 (0.212 to 0.573), n 24 | 0.000 | +-0.255 | within noise |  |
| East spawn (pad x=104) win share | 0.625 (0.427 to 0.788), n 24 | 0.583 (0.388 to 0.755), n 24 | -0.042 | +-0.257 | within noise |  |
| Match length | 480.0 s (480.0 to 480.0), n 24 | 480.0 s (480.0 to 480.0), n 24 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.458 (0.279 to 0.649), n 24 | 0.083 (0.023 to 0.258), n 24 | -0.375 | +-0.219 | **OUTSIDE noise** | <= 0.150: missed / met |
| Hill lead changes per match (median; spread = p25 to p75) | 3.00 (2.00 to 4.00), n 24 | 4.00 (2.00 to 6.00), n 24 | 1.00 | +-2.24 | within noise | >= 5.00: missed / missed |
| Hill lead changes per match (mean) | 2.88 (2.04 to 3.71), n 24 | 4.42 (3.27 to 5.56), n 24 | 1.54 | +-1.42 | **OUTSIDE noise** |  |
| Result decided at (last lead change) | 206.0 s (70.00 to 309.0), n 24 | 288.5 s (137.0 to 415.0), n 24 | 82.50 | +-183.3 | within noise |  |
| Result decided at, share of the match | 0.425 (0.294 to 0.556), n 24 | 0.580 (0.449 to 0.710), n 24 | 0.155 | +-0.185 | within noise |  |
| Leader at half time wins | 0.583 (0.388 to 0.755), n 24 | 0.435 (0.256 to 0.632), n 23 | -0.149 | +-0.263 | within noise | 0.600 to 0.800: missed / missed |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.952 (0.946 to 0.958), n 24 | 0.954 (0.946 to 0.962), n 24 | 0.002 | +-0.010 | within noise |  |
| Longest unbroken hill hold | 117.4 s (94.40 to 156.5), n 24 | 111.8 s (98.20 to 160.0), n 24 | -5.60 | +-43.81 | within noise |  |
| Warrior: dead or getting up | 0.131 (0.127 to 0.135), n 24 | 0.109 (0.103 to 0.115), n 24 | -0.022 | +-0.007 | **OUTSIDE noise** |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.316 (0.309 to 0.322), n 24 | 0.312 (0.303 to 0.320), n 24 | -0.004 | +-0.011 | within noise |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.305 (0.300 to 0.311), n 24 | 0.344 (0.335 to 0.352), n 24 | 0.038 | +-0.010 | **OUTSIDE noise** |  |
| Warrior: dead or walking | 0.447 (0.437 to 0.457), n 24 | 0.421 (0.409 to 0.433), n 24 | -0.026 | +-0.016 | **OUTSIDE noise** |  |
| Warrior: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.243 (0.236 to 0.250), n 24 | 0.247 (0.237 to 0.257), n 24 | 0.004 | +-0.012 | within noise |  |
| Warrior: dead or walking back after a respawn | 0.356 (0.346 to 0.367), n 24 | 0.334 (0.321 to 0.348), n 24 | -0.022 | +-0.017 | **OUTSIDE noise** |  |
| Farmer: dead or getting up | 0.106 (0.102 to 0.110), n 24 | 0.072 (0.066 to 0.078), n 24 | -0.034 | +-0.008 | **OUTSIDE noise** |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.337 (0.326 to 0.348), n 24 | 0.307 (0.294 to 0.320), n 24 | -0.029 | +-0.017 | **OUTSIDE noise** |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.167 (0.161 to 0.173), n 24 | 0.268 (0.261 to 0.276), n 24 | 0.102 | +-0.010 | **OUTSIDE noise** |  |
| Farmer: dead or walking | 0.443 (0.430 to 0.455), n 24 | 0.380 (0.361 to 0.398), n 24 | -0.063 | +-0.022 | **OUTSIDE noise** |  |
| Farmer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.179 (0.172 to 0.186), n 24 | 0.154 (0.145 to 0.162), n 24 | -0.026 | +-0.011 | **OUTSIDE noise** |  |
| Farmer: dead or walking back after a respawn | 0.271 (0.260 to 0.281), n 24 | 0.212 (0.199 to 0.225), n 24 | -0.059 | +-0.017 | **OUTSIDE noise** |  |
| Miner: dead or getting up | 0.058 (0.054 to 0.061), n 24 | 0.070 (0.065 to 0.076), n 24 | 0.012 | +-0.007 | **OUTSIDE noise** |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.368 (0.361 to 0.375), n 24 | 0.388 (0.381 to 0.394), n 24 | 0.020 | +-0.009 | **OUTSIDE noise** |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.117 (0.111 to 0.123), n 24 | 0.159 (0.151 to 0.166), n 24 | 0.042 | +-0.010 | **OUTSIDE noise** |  |
| Miner: dead or walking | 0.426 (0.417 to 0.435), n 24 | 0.458 (0.449 to 0.467), n 24 | 0.032 | +-0.013 | **OUTSIDE noise** |  |
| Miner: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.085 (0.080 to 0.090), n 24 | 0.151 (0.141 to 0.161), n 24 | 0.066 | +-0.011 | **OUTSIDE noise** |  |
| Miner: dead or walking back after a respawn | 0.135 (0.128 to 0.143), n 24 | 0.209 (0.194 to 0.223), n 24 | 0.073 | +-0.016 | **OUTSIDE noise** |  |
| Demolitions: dead or getting up | 0.156 (0.152 to 0.160), n 24 | 0.111 (0.106 to 0.116), n 24 | -0.045 | +-0.006 | **OUTSIDE noise** |  |
| Demolitions: walking (moving, not fighting, empty-handed, off the hill) | 0.353 (0.347 to 0.358), n 24 | 0.319 (0.311 to 0.327), n 24 | -0.033 | +-0.010 | **OUTSIDE noise** |  |
| Demolitions: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.264 (0.259 to 0.269), n 24 | 0.352 (0.344 to 0.360), n 24 | 0.088 | +-0.009 | **OUTSIDE noise** |  |
| Demolitions: dead or walking | 0.508 (0.499 to 0.517), n 24 | 0.430 (0.421 to 0.439), n 24 | -0.078 | +-0.013 | **OUTSIDE noise** |  |
| Demolitions: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.258 (0.250 to 0.265), n 24 | 0.224 (0.216 to 0.231), n 24 | -0.034 | +-0.010 | **OUTSIDE noise** |  |
| Demolitions: dead or walking back after a respawn | 0.392 (0.382 to 0.402), n 24 | 0.312 (0.302 to 0.322), n 24 | -0.080 | +-0.014 | **OUTSIDE noise** |  |
| Builder: dead or getting up | 0.078 (0.071 to 0.085), n 24 | 0.077 (0.072 to 0.082), n 24 | -0.001 | +-0.008 | within noise |  |
| Builder: walking (moving, not fighting, empty-handed, off the hill) | 0.340 (0.318 to 0.363), n 24 | 0.342 (0.319 to 0.364), n 24 | 0.001 | +-0.032 | within noise |  |
| Builder: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.145 (0.134 to 0.157), n 24 | 0.162 (0.150 to 0.173), n 24 | 0.016 | +-0.016 | **OUTSIDE noise** |  |
| Builder: dead or walking | 0.418 (0.394 to 0.443), n 24 | 0.419 (0.395 to 0.442), n 24 | 0.000 | +-0.034 | within noise |  |
| Builder: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.141 (0.127 to 0.154), n 24 | 0.155 (0.143 to 0.166), n 24 | 0.014 | +-0.018 | within noise |  |
| Builder: dead or walking back after a respawn | 0.208 (0.189 to 0.227), n 24 | 0.218 (0.203 to 0.232), n 24 | 0.010 | +-0.024 | within noise |  |
| Healer: dead or getting up | 0.142 (0.135 to 0.149), n 24 | 0.142 (0.135 to 0.149), n 24 | -0.000 | +-0.010 | within noise |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.356 (0.345 to 0.368), n 24 | 0.389 (0.380 to 0.397), n 24 | 0.033 | +-0.014 | **OUTSIDE noise** |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.175 (0.172 to 0.178), n 24 | 0.215 (0.211 to 0.219), n 24 | 0.040 | +-0.005 | **OUTSIDE noise** |  |
| Healer: dead or walking | 0.498 (0.482 to 0.515), n 24 | 0.531 (0.519 to 0.542), n 24 | 0.032 | +-0.020 | **OUTSIDE noise** |  |
| Healer: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.247 (0.237 to 0.258), n 24 | 0.324 (0.313 to 0.335), n 24 | 0.077 | +-0.015 | **OUTSIDE noise** |  |
| Healer: dead or walking back after a respawn | 0.370 (0.353 to 0.386), n 24 | 0.437 (0.422 to 0.451), n 24 | 0.067 | +-0.022 | **OUTSIDE noise** |  |
| all: dead or getting up | 0.117 (0.114 to 0.120), n 24 | 0.097 (0.092 to 0.102), n 24 | -0.019 | +-0.006 | **OUTSIDE noise** |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.339 (0.334 to 0.344), n 24 | 0.333 (0.325 to 0.340), n 24 | -0.006 | +-0.009 | within noise |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.216 (0.213 to 0.219), n 24 | 0.275 (0.271 to 0.280), n 24 | 0.060 | +-0.005 | **OUTSIDE noise** |  |
| all: dead or walking | 0.456 (0.449 to 0.463), n 24 | 0.430 (0.419 to 0.441), n 24 | -0.026 | +-0.013 | **OUTSIDE noise** |  |
| all: walking back after a respawn (to hill radius + 6, a hit, a job, 20 s) | 0.203 (0.199 to 0.208), n 24 | 0.213 (0.206 to 0.220), n 24 | 0.009 | +-0.008 | **OUTSIDE noise** |  |
| all: dead or walking back after a respawn | 0.304 (0.297 to 0.311), n 24 | 0.291 (0.281 to 0.301), n 24 | -0.013 | +-0.012 | **OUTSIDE noise** | <= 0.280: missed / missed |
| Warrior: damage dealt to fighters | 4058 HP (3928 to 4187), n 24 | 2685 HP (2608 to 2763), n 24 | -1372 | +-151.1 | **OUTSIDE noise** |  |
| Warrior: kills | 17.80 (17.20 to 18.40), n 24 | 14.72 (14.04 to 15.39), n 24 | -3.09 | +-0.902 | **OUTSIDE noise** |  |
| Warrior: deaths | 10.99 (10.65 to 11.33), n 24 | 13.36 (12.83 to 13.89), n 24 | 2.37 | +-0.628 | **OUTSIDE noise** |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 24 | 0.000 HP (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 162.9 s (158.8 to 167.0), n 24 | 172.3 s (168.0 to 176.5), n 24 | 9.35 | +-5.93 | **OUTSIDE noise** |  |
| Warrior: units delivered to the egg | 0.071 (0.017 to 0.124), n 24 | 0.050 (-0.006 to 0.106), n 24 | -0.021 | +-0.077 | within noise |  |
| Warrior: damage dealt to the Kaiju | 592.6 HP (526.0 to 659.1), n 24 | 144.2 HP (115.6 to 172.8), n 24 | -448.4 | +-72.47 | **OUTSIDE noise** |  |
| Warrior: share of all kills (whole class) | 0.599 (0.587 to 0.611), n 24 | 0.412 (0.398 to 0.426), n 24 | -0.187 | +-0.018 | **OUTSIDE noise** |  |
| Warrior: share of all fighter damage (whole class) | 0.609 (0.599 to 0.619), n 24 | 0.432 (0.424 to 0.440), n 24 | -0.177 | +-0.013 | **OUTSIDE noise** | <= 0.400: missed / missed |
| Farmer: damage dealt to fighters | 867.1 HP (811.1 to 923.0), n 24 | 1873 HP (1804 to 1943), n 24 | 1006 | +-89.22 | **OUTSIDE noise** |  |
| Farmer: kills | 4.35 (4.00 to 4.71), n 24 | 11.83 (11.25 to 12.42), n 24 | 7.48 | +-0.682 | **OUTSIDE noise** |  |
| Farmer: deaths | 8.91 (8.53 to 9.28), n 24 | 8.34 (7.81 to 8.88), n 24 | -0.563 | +-0.651 | within noise |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 24 | 0.000 HP (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 87.35 s (82.49 to 92.21), n 24 | 82.30 s (77.82 to 86.79), n 24 | -5.05 | +-6.61 | within noise |  |
| Farmer: units delivered to the egg | 14.42 (13.91 to 14.92), n 24 | 14.97 (14.51 to 15.44), n 24 | 0.557 | +-0.690 | within noise |  |
| Farmer: damage dealt to the Kaiju | 322.3 HP (279.2 to 365.3), n 24 | 832.3 HP (730.0 to 934.6), n 24 | 510.0 | +-111.0 | **OUTSIDE noise** |  |
| Farmer: share of all kills (whole class) | 0.117 (0.109 to 0.125), n 24 | 0.266 (0.252 to 0.280), n 24 | 0.149 | +-0.016 | **OUTSIDE noise** |  |
| Farmer: share of all fighter damage (whole class) | 0.104 (0.099 to 0.109), n 24 | 0.241 (0.232 to 0.250), n 24 | 0.137 | +-0.010 | **OUTSIDE noise** |  |
| Miner: damage dealt to fighters | 991.1 HP (935.3 to 1047), n 24 | 1223 HP (1157 to 1289), n 24 | 231.9 | +-86.17 | **OUTSIDE noise** |  |
| Miner: kills | 5.14 (4.61 to 5.66), n 24 | 8.41 (7.76 to 9.05), n 24 | 3.27 | +-0.832 | **OUTSIDE noise** |  |
| Miner: deaths | 4.86 (4.57 to 5.16), n 24 | 7.96 (7.46 to 8.46), n 24 | 3.09 | +-0.578 | **OUTSIDE noise** |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 24 | 0.000 HP (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 54.37 s (50.60 to 58.13), n 24 | 64.14 s (59.84 to 68.44), n 24 | 9.77 | +-5.72 | **OUTSIDE noise** |  |
| Miner: units delivered to the egg | 0.552 (0.360 to 0.744), n 24 | 0.417 (0.209 to 0.625), n 24 | -0.135 | +-0.283 | within noise |  |
| Miner: damage dealt to the Kaiju | 222.1 HP (180.1 to 264.1), n 24 | 194.0 HP (146.5 to 241.5), n 24 | -28.12 | +-63.39 | within noise |  |
| Miner: share of all kills (whole class) | 0.069 (0.062 to 0.076), n 24 | 0.094 (0.088 to 0.101), n 24 | 0.025 | +-0.009 | **OUTSIDE noise** |  |
| Miner: share of all fighter damage (whole class) | 0.060 (0.056 to 0.063), n 24 | 0.079 (0.075 to 0.082), n 24 | 0.019 | +-0.005 | **OUTSIDE noise** |  |
| Demolitions: damage dealt to fighters | 3139 HP (3032 to 3247), n 24 | 3250 HP (3133 to 3367), n 24 | 110.8 | +-159.1 | within noise |  |
| Demolitions: kills | 13.22 (12.43 to 14.01), n 24 | 17.15 (15.92 to 18.37), n 24 | 3.93 | +-1.46 | **OUTSIDE noise** |  |
| Demolitions: deaths | 12.99 (12.66 to 13.32), n 24 | 13.50 (13.04 to 13.96), n 24 | 0.510 | +-0.569 | within noise |  |
| Demolitions: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 24 | 0.000 HP (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Demolitions: objective credit (hill seconds scored, Actor.HillSeconds) | 142.0 s (137.6 to 146.3), n 24 | 148.4 s (144.3 to 152.6), n 24 | 6.47 | +-5.97 | **OUTSIDE noise** |  |
| Demolitions: units delivered to the egg | 0.292 (0.134 to 0.449), n 24 | 0.427 (0.264 to 0.590), n 24 | 0.135 | +-0.227 | within noise |  |
| Demolitions: damage dealt to the Kaiju | 140.9 HP (109.2 to 172.7), n 24 | 195.0 HP (155.4 to 234.6), n 24 | 54.09 | +-50.76 | **OUTSIDE noise** |  |
| Demolitions: share of all kills (whole class) | 0.178 (0.169 to 0.186), n 24 | 0.192 (0.180 to 0.203), n 24 | 0.014 | +-0.014 | within noise |  |
| Demolitions: share of all fighter damage (whole class) | 0.189 (0.182 to 0.196), n 24 | 0.209 (0.204 to 0.214), n 24 | 0.020 | +-0.008 | **OUTSIDE noise** |  |
| Builder: damage dealt to fighters | 1127 HP (1026 to 1229), n 24 | 1059 HP (976.6 to 1142), n 24 | -68.38 | +-130.6 | within noise |  |
| Builder: kills | 5.35 (4.66 to 6.05), n 24 | 6.38 (5.68 to 7.07), n 24 | 1.02 | +-0.980 | **OUTSIDE noise** |  |
| Builder: deaths | 6.63 (6.07 to 7.18), n 24 | 8.58 (8.07 to 9.10), n 24 | 1.96 | +-0.757 | **OUTSIDE noise** |  |
| Builder: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 24 | 0.000 HP (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Builder: objective credit (hill seconds scored, Actor.HillSeconds) | 86.82 s (81.57 to 92.07), n 24 | 92.87 s (86.01 to 99.74), n 24 | 6.05 | +-8.64 | within noise |  |
| Builder: units delivered to the egg | 4.56 (2.63 to 6.50), n 24 | 4.46 (2.47 to 6.44), n 24 | -0.104 | +-2.77 | within noise |  |
| Builder: damage dealt to the Kaiju | 374.7 HP (313.9 to 435.5), n 24 | 211.3 HP (172.8 to 249.7), n 24 | -163.5 | +-71.97 | **OUTSIDE noise** |  |
| Builder: share of all kills (whole class) | 0.036 (0.031 to 0.041), n 24 | 0.036 (0.032 to 0.040), n 24 | -0.000 | +-0.006 | within noise |  |
| Builder: share of all fighter damage (whole class) | 0.034 (0.031 to 0.037), n 24 | 0.034 (0.032 to 0.037), n 24 | 0.000 | +-0.004 | within noise |  |
| Healer: damage dealt to fighters | 76.10 HP (67.54 to 84.66), n 24 | 81.94 HP (69.93 to 93.94), n 24 | 5.84 | +-14.74 | within noise |  |
| Healer: kills | 0.073 (0.024 to 0.122), n 24 | 0.042 (0.001 to 0.082), n 24 | -0.031 | +-0.063 | within noise |  |
| Healer: deaths | 11.88 (11.28 to 12.47), n 24 | 17.81 (17.24 to 18.39), n 24 | 5.94 | +-0.824 | **OUTSIDE noise** |  |
| Healer: healing done (Healer mends and pulses) | 5157 HP (5014 to 5300), n 24 | 4924 HP (4780 to 5068), n 24 | -233.1 | +-202.7 | **OUTSIDE noise** |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 143.9 s (136.1 to 151.6), n 24 | 136.0 s (129.7 to 142.3), n 24 | -7.85 | +-9.99 | within noise |  |
| Healer: units delivered to the egg | 0.000 (0.000 to 0.000), n 24 | 0.021 (-0.022 to 0.064), n 24 | 0.021 | +-0.043 | within noise |  |
| Healer: damage dealt to the Kaiju | 0.296 HP (-0.042 to 0.634), n 24 | 12.54 HP (4.22 to 20.85), n 24 | 12.24 | +-8.32 | **OUTSIDE noise** |  |
| Healer: share of all kills (whole class) | 0.001 (0.000 to 0.002), n 24 | 0.000 (0.000 to 0.001), n 24 | -0.000 | +-0.001 | within noise |  |
| Healer: share of all fighter damage (whole class) | 0.005 (0.004 to 0.005), n 24 | 0.005 (0.005 to 0.006), n 24 | 0.001 | +-0.001 | within noise |  |
| Top-10 share of all fighter damage (10 of 32 bodies) | 0.622 (0.615 to 0.630), n 24 | 0.501 (0.495 to 0.506), n 24 | -0.122 | +-0.009 | **OUTSIDE noise** | <= 0.500: missed / missed |
| Top-10 share of all kills | 0.647 (0.637 to 0.657), n 24 | 0.543 (0.532 to 0.554), n 24 | -0.104 | +-0.015 | **OUTSIDE noise** |  |
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
| Matches where the egg hatched | 1.00 (0.862 to 1.000), n 24 | 1.00 (0.862 to 1.000), n 24 | 0.000 | +-0.098 | within noise |  |
| Elder share of hatches | 0.042 (0.007 to 0.202), n 24 | 0.000 (0.000 to 0.138), n 24 | -0.042 | +-0.119 | within noise |  |
| Grove share of hatches | 0.000 (0.000 to 0.138), n 24 | 0.000 (0.000 to 0.138), n 24 | 0.000 | +-0.098 | within noise |  |
| Brute share of hatches | 0.375 (0.212 to 0.573), n 24 | 0.375 (0.212 to 0.573), n 24 | 0.000 | +-0.255 | within noise |  |
| Timber share of hatches | 0.333 (0.180 to 0.533), n 24 | 0.333 (0.180 to 0.533), n 24 | 0.000 | +-0.250 | within noise |  |
| Mire share of hatches | 0.250 (0.120 to 0.449), n 24 | 0.292 (0.149 to 0.492), n 24 | 0.042 | +-0.237 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.762 (0.549 to 0.894), n 21 | 0.765 (0.527 to 0.904), n 17 | 0.003 | +-0.255 | within noise |  |
| Berserk share of hatches | 0.125 (0.043 to 0.310), n 24 | 0.250 (0.120 to 0.449), n 24 | 0.125 | +-0.212 | within noise |  |
| Hut level 1 reached at (team-matches that reached it) | 17.68 s (16.50 to 19.17), n 48 | 20.78 s (16.50 to 25.37), n 48 | 3.10 | +-4.63 | within noise |  |
| Hut level 2 reached at (team-matches that reached it) | 77.38 s (68.83 to 86.13), n 48 | 76.22 s (68.83 to 78.47), n 48 | -1.17 | +-9.90 | within noise |  |
| Hut level 3 reached at (team-matches that reached it) | 141.1 s (135.8 to 149.5), n 48 | 132.9 s (126.3 to 135.9), n 48 | -8.22 | +-8.37 | within noise |  |
| Hut level 4 reached at (team-matches that reached it) | 287.0 s (276.0 to 295.8), n 48 | 310.2 s (293.0 to 329.1), n 47 | 23.18 | +-20.58 | **OUTSIDE noise** |  |
| Hut level 5 reached at (team-matches that reached it) | 449.5 s (436.8 to 455.7), n 31 | 462.8 s (427.2 to 479.9), n 8 | 13.30 | +-27.99 | within noise |  |
| Team-matches reaching level 5 | 0.646 (0.504 to 0.766), n 48 | 0.167 (0.087 to 0.296), n 48 | -0.479 | +-0.167 | **OUTSIDE noise** |  |
| Builder walls built | 2.96 (2.09 to 3.83), n 24 | 3.21 (2.09 to 4.33), n 24 | 0.250 | +-1.42 | within noise |  |
| Builder wall cells broken or crumbled | 2.71 (1.81 to 3.61), n 24 | 2.92 (1.85 to 3.99), n 24 | 0.208 | +-1.40 | within noise |  |
| Ladders and bridges placed | 6.79 (5.78 to 7.80), n 24 | 4.83 (3.90 to 5.77), n 24 | -1.96 | +-1.38 | **OUTSIDE noise** |  |
| Ladders and bridges destroyed | 1.00 (0.606 to 1.39), n 24 | 0.708 (0.346 to 1.07), n 24 | -0.292 | +-0.535 | within noise |  |
| Barricades placed | 0.000 (0.000 to 0.000), n 24 | 0.000 (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Barricades broken | 9.25 (6.31 to 12.19), n 24 | 10.17 (6.81 to 13.52), n 24 | 0.917 | +-4.46 | within noise |  |
| Decorations toppled | 0.000 (0.000 to 0.000), n 24 | 0.000 (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 9.00 (6.00 to 13.00), n 24 | 12.00 (8.00 to 17.00), n 24 | 3.00 | +-5.70 | within noise | < 5.00: missed / missed |
| Stuck episodes per match (mean) | 9.88 (7.07 to 12.68), n 24 | 12.71 (10.11 to 15.30), n 24 | 2.83 | +-3.82 | within noise |  |
| Sim tick cost, mean (wall clock, this machine) | 75.28 us (69.27 to 81.29), n 24 | 79.64 us (70.86 to 88.42), n 24 | 4.36 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 699.0 us (692.6 to 708.8), n 24 | 733.0 us (720.1 to 740.6), n 24 | 34.04 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 24 | 0, n 24 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 24 | 0, n 24 | 0.000 |  | not judged |  |
