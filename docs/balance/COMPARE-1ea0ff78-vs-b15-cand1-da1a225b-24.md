# Balance compare

- base: base origin/integration/batch-13-cand1 (1ea0ff78, none (the game's values))
- head: head  (da1a225b, none (the game's values))
- noise rule: |head - base| > sqrt(h_base^2 + h_head^2), h = 95% half-width of each row; tick cost and counts are shown, not judged.

0 of 188 rows moved outside the noise.

| Stat | Base (95% CI, n) | Head (95% CI, n) | Head - base | Noise | Verdict | Target: base / head |
|---|---|---|---|---|---|---|
| Azure win share | 0.375 (0.212 to 0.573), n 24 | 0.375 (0.212 to 0.573), n 24 | 0.000 | +-0.255 | within noise |  |
| Crimson win share | 0.625 (0.427 to 0.788), n 24 | 0.625 (0.427 to 0.788), n 24 | 0.000 | +-0.255 | within noise |  |
| Draw share | 0.000 (0.000 to 0.138), n 24 | 0.000 (0.000 to 0.138), n 24 | 0.000 | +-0.098 | within noise |  |
| West spawn (pad x=8) win share | 0.375 (0.212 to 0.573), n 24 | 0.375 (0.212 to 0.573), n 24 | 0.000 | +-0.255 | within noise |  |
| East spawn (pad x=104) win share | 0.625 (0.427 to 0.788), n 24 | 0.625 (0.427 to 0.788), n 24 | 0.000 | +-0.255 | within noise |  |
| Match length | 480.0 s (480.0 to 480.0), n 24 | 480.0 s (480.0 to 480.0), n 24 | 0.000 | +-0.000 | within noise |  |
| Blowout share (winner's hill time at least 2x the loser's) | 0.458 (0.279 to 0.649), n 24 | 0.458 (0.279 to 0.649), n 24 | 0.000 | +-0.262 | within noise | <= 0.150: missed / missed |
| Hill lead changes per match (median; spread = p25 to p75) | 3.00 (2.00 to 4.00), n 24 | 3.00 (2.00 to 4.00), n 24 | 0.000 | +-1.41 | within noise | >= 5.00: missed / missed |
| Hill lead changes per match (mean) | 2.88 (2.04 to 3.71), n 24 | 2.88 (2.04 to 3.71), n 24 | 0.000 | +-1.19 | within noise |  |
| Result decided at (last lead change) | 206.0 s (70.00 to 309.0), n 24 | 206.0 s (70.00 to 309.0), n 24 | 0.000 | +-169.0 | within noise |  |
| Result decided at, share of the match | 0.425 (0.294 to 0.556), n 24 | 0.425 (0.294 to 0.556), n 24 | 0.000 | +-0.185 | within noise |  |
| Leader at half time wins | 0.583 (0.388 to 0.755), n 24 | 0.583 (0.388 to 0.755), n 24 | 0.000 | +-0.259 | within noise | 0.600 to 0.800: missed / missed |
| Hill held (not contested, not neutral), share of samples after 0:30 | 0.952 (0.946 to 0.958), n 24 | 0.952 (0.946 to 0.958), n 24 | 0.000 | +-0.008 | within noise |  |
| Longest unbroken hill hold | 117.4 s (94.40 to 156.5), n 24 | 117.4 s (94.40 to 156.5), n 24 | 0.000 | +-43.91 | within noise |  |
| Warrior: dead or getting up | 0.131 (0.127 to 0.135), n 24 | 0.131 (0.127 to 0.135), n 24 | 0.000 | +-0.006 | within noise |  |
| Warrior: walking (moving, not fighting, empty-handed, off the hill) | 0.316 (0.309 to 0.322), n 24 | 0.316 (0.309 to 0.322), n 24 | 0.000 | +-0.009 | within noise |  |
| Warrior: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.305 (0.300 to 0.311), n 24 | 0.305 (0.300 to 0.311), n 24 | 0.000 | +-0.008 | within noise |  |
| Warrior: dead or walking | 0.447 (0.437 to 0.457), n 24 | 0.447 (0.437 to 0.457), n 24 | 0.000 | +-0.014 | within noise |  |
| Farmer: dead or getting up | 0.106 (0.102 to 0.110), n 24 | 0.106 (0.102 to 0.110), n 24 | 0.000 | +-0.006 | within noise |  |
| Farmer: walking (moving, not fighting, empty-handed, off the hill) | 0.337 (0.326 to 0.348), n 24 | 0.337 (0.326 to 0.348), n 24 | 0.000 | +-0.015 | within noise |  |
| Farmer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.167 (0.161 to 0.173), n 24 | 0.167 (0.161 to 0.173), n 24 | 0.000 | +-0.008 | within noise |  |
| Farmer: dead or walking | 0.443 (0.430 to 0.455), n 24 | 0.443 (0.430 to 0.455), n 24 | 0.000 | +-0.018 | within noise |  |
| Miner: dead or getting up | 0.058 (0.054 to 0.061), n 24 | 0.058 (0.054 to 0.061), n 24 | 0.000 | +-0.005 | within noise |  |
| Miner: walking (moving, not fighting, empty-handed, off the hill) | 0.368 (0.361 to 0.375), n 24 | 0.368 (0.361 to 0.375), n 24 | 0.000 | +-0.010 | within noise |  |
| Miner: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.117 (0.111 to 0.123), n 24 | 0.117 (0.111 to 0.123), n 24 | 0.000 | +-0.009 | within noise |  |
| Miner: dead or walking | 0.426 (0.417 to 0.435), n 24 | 0.426 (0.417 to 0.435), n 24 | 0.000 | +-0.012 | within noise |  |
| Demolitions: dead or getting up | 0.156 (0.152 to 0.160), n 24 | 0.156 (0.152 to 0.160), n 24 | 0.000 | +-0.006 | within noise |  |
| Demolitions: walking (moving, not fighting, empty-handed, off the hill) | 0.353 (0.347 to 0.358), n 24 | 0.353 (0.347 to 0.358), n 24 | 0.000 | +-0.008 | within noise |  |
| Demolitions: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.264 (0.259 to 0.269), n 24 | 0.264 (0.259 to 0.269), n 24 | 0.000 | +-0.007 | within noise |  |
| Demolitions: dead or walking | 0.508 (0.499 to 0.517), n 24 | 0.508 (0.499 to 0.517), n 24 | 0.000 | +-0.012 | within noise |  |
| Builder: dead or getting up | 0.078 (0.071 to 0.085), n 24 | 0.078 (0.071 to 0.085), n 24 | 0.000 | +-0.009 | within noise |  |
| Builder: walking (moving, not fighting, empty-handed, off the hill) | 0.340 (0.318 to 0.363), n 24 | 0.340 (0.318 to 0.363), n 24 | 0.000 | +-0.032 | within noise |  |
| Builder: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.145 (0.134 to 0.157), n 24 | 0.145 (0.134 to 0.157), n 24 | 0.000 | +-0.016 | within noise |  |
| Builder: dead or walking | 0.418 (0.394 to 0.443), n 24 | 0.418 (0.394 to 0.443), n 24 | 0.000 | +-0.035 | within noise |  |
| Healer: dead or getting up | 0.142 (0.135 to 0.149), n 24 | 0.142 (0.135 to 0.149), n 24 | 0.000 | +-0.010 | within noise |  |
| Healer: walking (moving, not fighting, empty-handed, off the hill) | 0.356 (0.345 to 0.368), n 24 | 0.356 (0.345 to 0.368), n 24 | 0.000 | +-0.016 | within noise |  |
| Healer: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.175 (0.172 to 0.178), n 24 | 0.175 (0.172 to 0.178), n 24 | 0.000 | +-0.004 | within noise |  |
| Healer: dead or walking | 0.498 (0.482 to 0.515), n 24 | 0.498 (0.482 to 0.515), n 24 | 0.000 | +-0.023 | within noise |  |
| all: dead or getting up | 0.117 (0.114 to 0.120), n 24 | 0.117 (0.114 to 0.120), n 24 | 0.000 | +-0.004 | within noise |  |
| all: walking (moving, not fighting, empty-handed, off the hill) | 0.339 (0.334 to 0.344), n 24 | 0.339 (0.334 to 0.344), n 24 | 0.000 | +-0.007 | within noise |  |
| all: fighting (hit dealt or taken, or a swing, in the last 1 s) | 0.216 (0.213 to 0.219), n 24 | 0.216 (0.213 to 0.219), n 24 | 0.000 | +-0.004 | within noise |  |
| all: dead or walking | 0.456 (0.449 to 0.463), n 24 | 0.456 (0.449 to 0.463), n 24 | 0.000 | +-0.010 | within noise | <= 0.280: missed / missed |
| Warrior: damage dealt to fighters | 4058 HP (3928 to 4187), n 24 | 4058 HP (3928 to 4187), n 24 | 0.000 | +-183.6 | within noise |  |
| Warrior: kills | 17.80 (17.20 to 18.40), n 24 | 17.80 (17.20 to 18.40), n 24 | 0.000 | +-0.849 | within noise |  |
| Warrior: deaths | 10.99 (10.65 to 11.33), n 24 | 10.99 (10.65 to 11.33), n 24 | 0.000 | +-0.481 | within noise |  |
| Warrior: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 24 | 0.000 HP (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Warrior: objective credit (hill seconds scored, Actor.HillSeconds) | 162.9 s (158.8 to 167.0), n 24 | 162.9 s (158.8 to 167.0), n 24 | 0.000 | +-5.83 | within noise |  |
| Warrior: units delivered to the egg | 0.071 (0.017 to 0.124), n 24 | 0.071 (0.017 to 0.124), n 24 | 0.000 | +-0.076 | within noise |  |
| Warrior: damage dealt to the Kaiju | 592.6 HP (526.0 to 659.1), n 24 | 592.6 HP (526.0 to 659.1), n 24 | 0.000 | +-94.16 | within noise |  |
| Warrior: share of all kills (whole class) | 0.599 (0.587 to 0.611), n 24 | 0.599 (0.587 to 0.611), n 24 | 0.000 | +-0.017 | within noise |  |
| Farmer: damage dealt to fighters | 867.1 HP (811.1 to 923.0), n 24 | 867.1 HP (811.1 to 923.0), n 24 | 0.000 | +-79.12 | within noise |  |
| Farmer: kills | 4.35 (4.00 to 4.71), n 24 | 4.35 (4.00 to 4.71), n 24 | 0.000 | +-0.500 | within noise |  |
| Farmer: deaths | 8.91 (8.53 to 9.28), n 24 | 8.91 (8.53 to 9.28), n 24 | 0.000 | +-0.527 | within noise |  |
| Farmer: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 24 | 0.000 HP (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Farmer: objective credit (hill seconds scored, Actor.HillSeconds) | 87.35 s (82.49 to 92.21), n 24 | 87.35 s (82.49 to 92.21), n 24 | 0.000 | +-6.87 | within noise |  |
| Farmer: units delivered to the egg | 14.42 (13.91 to 14.92), n 24 | 14.42 (13.91 to 14.92), n 24 | 0.000 | +-0.718 | within noise |  |
| Farmer: damage dealt to the Kaiju | 322.3 HP (279.2 to 365.3), n 24 | 322.3 HP (279.2 to 365.3), n 24 | 0.000 | +-60.89 | within noise |  |
| Farmer: share of all kills (whole class) | 0.117 (0.109 to 0.125), n 24 | 0.117 (0.109 to 0.125), n 24 | 0.000 | +-0.012 | within noise |  |
| Miner: damage dealt to fighters | 991.1 HP (935.3 to 1047), n 24 | 991.1 HP (935.3 to 1047), n 24 | 0.000 | +-78.97 | within noise |  |
| Miner: kills | 5.14 (4.61 to 5.66), n 24 | 5.14 (4.61 to 5.66), n 24 | 0.000 | +-0.745 | within noise |  |
| Miner: deaths | 4.86 (4.57 to 5.16), n 24 | 4.86 (4.57 to 5.16), n 24 | 0.000 | +-0.418 | within noise |  |
| Miner: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 24 | 0.000 HP (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Miner: objective credit (hill seconds scored, Actor.HillSeconds) | 54.37 s (50.60 to 58.13), n 24 | 54.37 s (50.60 to 58.13), n 24 | 0.000 | +-5.33 | within noise |  |
| Miner: units delivered to the egg | 0.552 (0.360 to 0.744), n 24 | 0.552 (0.360 to 0.744), n 24 | 0.000 | +-0.271 | within noise |  |
| Miner: damage dealt to the Kaiju | 222.1 HP (180.1 to 264.1), n 24 | 222.1 HP (180.1 to 264.1), n 24 | 0.000 | +-59.39 | within noise |  |
| Miner: share of all kills (whole class) | 0.069 (0.062 to 0.076), n 24 | 0.069 (0.062 to 0.076), n 24 | 0.000 | +-0.010 | within noise |  |
| Demolitions: damage dealt to fighters | 3139 HP (3032 to 3247), n 24 | 3139 HP (3032 to 3247), n 24 | 0.000 | +-152.6 | within noise |  |
| Demolitions: kills | 13.22 (12.43 to 14.01), n 24 | 13.22 (12.43 to 14.01), n 24 | 0.000 | +-1.12 | within noise |  |
| Demolitions: deaths | 12.99 (12.66 to 13.32), n 24 | 12.99 (12.66 to 13.32), n 24 | 0.000 | +-0.469 | within noise |  |
| Demolitions: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 24 | 0.000 HP (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Demolitions: objective credit (hill seconds scored, Actor.HillSeconds) | 142.0 s (137.6 to 146.3), n 24 | 142.0 s (137.6 to 146.3), n 24 | 0.000 | +-6.09 | within noise |  |
| Demolitions: units delivered to the egg | 0.292 (0.134 to 0.449), n 24 | 0.292 (0.134 to 0.449), n 24 | 0.000 | +-0.223 | within noise |  |
| Demolitions: damage dealt to the Kaiju | 140.9 HP (109.2 to 172.7), n 24 | 140.9 HP (109.2 to 172.7), n 24 | 0.000 | +-44.91 | within noise |  |
| Demolitions: share of all kills (whole class) | 0.178 (0.169 to 0.186), n 24 | 0.178 (0.169 to 0.186), n 24 | 0.000 | +-0.012 | within noise |  |
| Builder: damage dealt to fighters | 1127 HP (1026 to 1229), n 24 | 1127 HP (1026 to 1229), n 24 | 0.000 | +-143.3 | within noise |  |
| Builder: kills | 5.35 (4.66 to 6.05), n 24 | 5.35 (4.66 to 6.05), n 24 | 0.000 | +-0.983 | within noise |  |
| Builder: deaths | 6.63 (6.07 to 7.18), n 24 | 6.63 (6.07 to 7.18), n 24 | 0.000 | +-0.789 | within noise |  |
| Builder: healing done (Healer mends and pulses) | 0.000 HP (0.000 to 0.000), n 24 | 0.000 HP (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Builder: objective credit (hill seconds scored, Actor.HillSeconds) | 86.82 s (81.57 to 92.07), n 24 | 86.82 s (81.57 to 92.07), n 24 | 0.000 | +-7.43 | within noise |  |
| Builder: units delivered to the egg | 4.56 (2.63 to 6.50), n 24 | 4.56 (2.63 to 6.50), n 24 | 0.000 | +-2.74 | within noise |  |
| Builder: damage dealt to the Kaiju | 374.7 HP (313.9 to 435.5), n 24 | 374.7 HP (313.9 to 435.5), n 24 | 0.000 | +-86.00 | within noise |  |
| Builder: share of all kills (whole class) | 0.036 (0.031 to 0.041), n 24 | 0.036 (0.031 to 0.041), n 24 | 0.000 | +-0.007 | within noise |  |
| Healer: damage dealt to fighters | 76.10 HP (67.54 to 84.66), n 24 | 76.10 HP (67.54 to 84.66), n 24 | 0.000 | +-12.10 | within noise |  |
| Healer: kills | 0.073 (0.024 to 0.122), n 24 | 0.073 (0.024 to 0.122), n 24 | 0.000 | +-0.069 | within noise |  |
| Healer: deaths | 11.88 (11.28 to 12.47), n 24 | 11.88 (11.28 to 12.47), n 24 | 0.000 | +-0.837 | within noise |  |
| Healer: healing done (Healer mends and pulses) | 5157 HP (5014 to 5300), n 24 | 5157 HP (5014 to 5300), n 24 | 0.000 | +-201.8 | within noise |  |
| Healer: objective credit (hill seconds scored, Actor.HillSeconds) | 143.9 s (136.1 to 151.6), n 24 | 143.9 s (136.1 to 151.6), n 24 | 0.000 | +-10.99 | within noise |  |
| Healer: units delivered to the egg | 0.000 (0.000 to 0.000), n 24 | 0.000 (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Healer: damage dealt to the Kaiju | 0.296 HP (-0.042 to 0.634), n 24 | 0.296 HP (-0.042 to 0.634), n 24 | 0.000 | +-0.478 | within noise |  |
| Healer: share of all kills (whole class) | 0.001 (0.000 to 0.002), n 24 | 0.001 (0.000 to 0.002), n 24 | 0.000 | +-0.001 | within noise |  |
| Top-10 share of all fighter damage (10 of 32 bodies) | 0.622 (0.615 to 0.630), n 24 | 0.622 (0.615 to 0.630), n 24 | 0.000 | +-0.011 | within noise | <= 0.500: missed / missed |
| Top-10 share of all kills | 0.647 (0.637 to 0.657), n 24 | 0.647 (0.637 to 0.657), n 24 | 0.000 | +-0.015 | within noise |  |
| Time to kill: Warrior on a passive Warrior | 5.77 s (5.77 to 5.77), n 8 | 5.77 s (5.77 to 5.77), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Farmer | 3.67 s (3.67 to 3.67), n 8 | 3.67 s (3.67 to 3.67), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Miner | 4.37 s (4.37 to 4.37), n 8 | 4.37 s (4.37 to 4.37), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Demolitions | 4.37 s (4.37 to 4.37), n 8 | 4.37 s (4.37 to 4.37), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Builder | 4.37 s (4.37 to 4.37), n 8 | 4.37 s (4.37 to 4.37), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Warrior on a passive Healer | 3.67 s (3.67 to 3.67), n 8 | 3.67 s (3.67 to 3.67), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Warrior | 8.53 s (8.53 to 8.53), n 8 | 8.53 s (8.53 to 8.53), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Farmer | 5.53 s (5.53 to 5.53), n 8 | 5.53 s (5.53 to 5.53), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Miner | 6.13 s (6.13 to 6.13), n 8 | 6.13 s (6.13 to 6.13), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Demolitions | 6.13 s (6.13 to 6.13), n 8 | 6.13 s (6.13 to 6.13), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Builder | 6.13 s (6.13 to 6.13), n 8 | 6.13 s (6.13 to 6.13), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Farmer on a passive Healer | 6.13 s (6.13 to 6.13), n 8 | 6.13 s (6.13 to 6.13), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Warrior | 6.80 s (6.80 to 6.80), n 8 | 6.80 s (6.80 to 6.80), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Farmer | 4.80 s (4.80 to 4.80), n 8 | 4.80 s (4.80 to 4.80), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Miner | 5.47 s (5.47 to 5.47), n 8 | 5.47 s (5.47 to 5.47), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Demolitions | 5.47 s (5.47 to 5.47), n 8 | 5.47 s (5.47 to 5.47), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Builder | 5.47 s (5.47 to 5.47), n 8 | 5.47 s (5.47 to 5.47), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Miner on a passive Healer | 4.80 s (4.80 to 4.80), n 8 | 4.80 s (4.80 to 4.80), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Warrior | 10.00 s (10.00 to 10.00), n 8 | 10.00 s (10.00 to 10.00), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Farmer | 5.40 s (5.40 to 5.40), n 8 | 5.40 s (5.40 to 5.40), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Miner | 6.93 s (6.93 to 6.93), n 8 | 6.93 s (6.93 to 6.93), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Demolitions | 6.93 s (6.93 to 6.93), n 8 | 6.93 s (6.93 to 6.93), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Builder | 6.93 s (6.93 to 6.93), n 8 | 6.93 s (6.93 to 6.93), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Demolitions on a passive Healer | 6.17 s (6.17 to 6.17), n 8 | 6.17 s (6.17 to 6.17), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Warrior | 6.80 s (6.80 to 6.80), n 8 | 6.80 s (6.80 to 6.80), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Farmer | 4.80 s (4.80 to 4.80), n 8 | 4.80 s (4.80 to 4.80), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Miner | 5.47 s (5.47 to 5.47), n 8 | 5.47 s (5.47 to 5.47), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Demolitions | 5.47 s (5.47 to 5.47), n 8 | 5.47 s (5.47 to 5.47), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Builder | 5.47 s (5.47 to 5.47), n 8 | 5.47 s (5.47 to 5.47), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Builder on a passive Healer | 4.80 s (4.80 to 4.80), n 8 | 4.80 s (4.80 to 4.80), n 8 | 0.000 | +-0.000 | within noise |  |
| Time to kill: Healer on a passive Warrior | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Time to kill: Healer on a passive Farmer | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Time to kill: Healer on a passive Miner | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Time to kill: Healer on a passive Demolitions | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Time to kill: Healer on a passive Builder | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Time to kill: Healer on a passive Healer | n/a, n 0 | n/a, n 0 | n/a |  | no data |  |
| Time to kill, median over the matchups that can kill | 5.47 s (4.80 to 6.13), n 30 | 5.47 s (4.80 to 6.13), n 30 | 0.000 | +-0.943 | within noise | 3.00 to 6.00: met / met |
| Matchups whose time to kill is within 3 to 6 s | 18, n 30 | 18, n 30 | 0.000 |  | not judged |  |
| Duel: Warrior beats Farmer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Warrior beats Miner | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Warrior beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Warrior beats Builder | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Warrior beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Warrior | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Demolitions | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Builder | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Farmer beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Warrior | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Farmer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Builder | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Miner beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Warrior | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Farmer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Builder | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Demolitions beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Warrior | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Farmer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Demolitions | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Builder beats Healer | 1.00 (0.676 to 1.00), n 8 | 1.00 (0.676 to 1.00), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Warrior | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Farmer | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Miner | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Demolitions | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Duel: Healer beats Builder | 0.000 (0.000 to 0.324), n 8 | 0.000 (0.000 to 0.324), n 8 | 0.000 | +-0.229 | within noise |  |
| Classes that win every duel (more than half of each matchup) | 1, n 6 | 1, n 6 | 0.000 |  | not judged | <= 0.000: missed / missed |
| Matches where the egg hatched | 1.00 (0.862 to 1.000), n 24 | 1.00 (0.862 to 1.000), n 24 | 0.000 | +-0.098 | within noise |  |
| Elder share of hatches | 0.042 (0.007 to 0.202), n 24 | 0.042 (0.007 to 0.202), n 24 | 0.000 | +-0.138 | within noise |  |
| Grove share of hatches | 0.000 (0.000 to 0.138), n 24 | 0.000 (0.000 to 0.138), n 24 | 0.000 | +-0.098 | within noise |  |
| Brute share of hatches | 0.375 (0.212 to 0.573), n 24 | 0.375 (0.212 to 0.573), n 24 | 0.000 | +-0.255 | within noise |  |
| Timber share of hatches | 0.333 (0.180 to 0.533), n 24 | 0.333 (0.180 to 0.533), n 24 | 0.000 | +-0.250 | within noise |  |
| Mire share of hatches | 0.250 (0.120 to 0.449), n 24 | 0.250 (0.120 to 0.449), n 24 | 0.000 | +-0.233 | within noise |  |
| Kaiju owner (side at the hatch) wins | 0.762 (0.549 to 0.894), n 21 | 0.762 (0.549 to 0.894), n 21 | 0.000 | +-0.244 | within noise |  |
| Berserk share of hatches | 0.125 (0.043 to 0.310), n 24 | 0.125 (0.043 to 0.310), n 24 | 0.000 | +-0.189 | within noise |  |
| Hut level 1 reached at (team-matches that reached it) | 17.68 s (16.50 to 19.17), n 48 | 17.68 s (16.50 to 19.17), n 48 | 0.000 | +-1.89 | within noise |  |
| Hut level 2 reached at (team-matches that reached it) | 77.38 s (68.83 to 86.13), n 48 | 77.38 s (68.83 to 86.13), n 48 | 0.000 | +-12.23 | within noise |  |
| Hut level 3 reached at (team-matches that reached it) | 141.1 s (135.8 to 149.5), n 48 | 141.1 s (135.8 to 149.5), n 48 | 0.000 | +-9.69 | within noise |  |
| Hut level 4 reached at (team-matches that reached it) | 287.0 s (276.0 to 295.8), n 48 | 287.0 s (276.0 to 295.8), n 48 | 0.000 | +-14.02 | within noise |  |
| Hut level 5 reached at (team-matches that reached it) | 449.5 s (436.8 to 455.7), n 31 | 449.5 s (436.8 to 455.7), n 31 | 0.000 | +-13.36 | within noise |  |
| Team-matches reaching level 5 | 0.646 (0.504 to 0.766), n 48 | 0.646 (0.504 to 0.766), n 48 | 0.000 | +-0.185 | within noise |  |
| Builder walls built | 2.96 (2.09 to 3.83), n 24 | 2.96 (2.09 to 3.83), n 24 | 0.000 | +-1.23 | within noise |  |
| Builder wall cells broken or crumbled | 2.71 (1.81 to 3.61), n 24 | 2.71 (1.81 to 3.61), n 24 | 0.000 | +-1.28 | within noise |  |
| Ladders and bridges placed | 6.79 (5.78 to 7.80), n 24 | 6.79 (5.78 to 7.80), n 24 | 0.000 | +-1.43 | within noise |  |
| Ladders and bridges destroyed | 1.00 (0.606 to 1.39), n 24 | 1.00 (0.606 to 1.39), n 24 | 0.000 | +-0.557 | within noise |  |
| Barricades placed | 0.000 (0.000 to 0.000), n 24 | 0.000 (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Barricades broken | 9.25 (6.31 to 12.19), n 24 | 9.25 (6.31 to 12.19), n 24 | 0.000 | +-4.16 | within noise |  |
| Decorations toppled | 0.000 (0.000 to 0.000), n 24 | 0.000 (0.000 to 0.000), n 24 | 0.000 | +-0.000 | within noise |  |
| Stuck episodes per match (DG-02 window: 2 s of held move, under 0.05 u, no enemy touching) | 9.00 (6.00 to 13.00), n 24 | 9.00 (6.00 to 13.00), n 24 | 0.000 | +-4.95 | within noise | < 5.00: missed / missed |
| Stuck episodes per match (mean) | 9.88 (7.07 to 12.68), n 24 | 9.88 (7.07 to 12.68), n 24 | 0.000 | +-3.97 | within noise |  |
| Sim tick cost, mean (wall clock, this machine) | 73.12 us (66.41 to 79.82), n 24 | 77.35 us (68.31 to 86.38), n 24 | 4.23 |  | not judged |  |
| Sim tick cost, p99 within a match (median over matches) | 703.6 us (693.5 to 714.7), n 24 | 747.2 us (733.5 to 751.9), n 24 | 43.62 |  | not judged |  |
| Sim events lost to the ring buffer (must be 0) | 0, n 24 | 0, n 24 | 0.000 |  | not judged |  |
| Replays that stopped at a fingerprint mismatch | 0, n 24 | 0, n 24 | 0.000 |  | not judged |  |
