# Skybreak game score: analyzer report

Written by `tools/audio/skybreak/analyze_score.py` from the committed files (no rebuild). Tagger: AudioSet AST, 1 s windows, banned groups at >= 0.05 in the top five. Result: **PASS**.

## Mix states (whole 12-section plan, 4 min 48 s, as the game layers them)

| state | key (r) | r F minor | LUFS | true peak | on grid | tempo | events/bar | spectrum dev dB | low/mid/high | ear: banned (top) |
|---|---|---|---|---|---|---|---|---|---|---|
| calm | F minor (0.802) | 0.802 | -21.78 | -7.92 | 87% | 161.5 | 10.66 | 4.16 | 0.567/0.43/0.003 | 0 (Music (96)) |
| contested | F minor (0.685) | 0.685 | -20.03 | -4.1 | 92% | 161.5 | 10.22 | 4.96 | 0.631/0.367/0.002 | 0 (Music (96)) |
| leading | F minor (0.717) | 0.717 | -19.49 | -4.81 | 89% | 161.5 | 10.52 | 4.71 | 0.568/0.431/0.002 | 0 (Music (96)) |
| trailing | F minor (0.803) | 0.803 | -20.1 | -5.65 | 90% | 161.5 | 10.5 | 4.12 | 0.558/0.439/0.003 | 0 (Music (96)) |
| rally | F minor (0.629) | 0.629 | -17.89 | -3.56 | 96% | 161.5 | 9.56 | 5.03 | 0.65/0.349/0.002 | 0 (Music (96)) |
| egg | F minor (0.776) | 0.776 | -21.23 | -6.03 | 71% | 112.3 | 11.36 | 5.32 | 0.746/0.253/0.002 | 0 (Music (96)) |
| final | F minor (0.598) | 0.598 | -17.09 | -2.61 | 97% | 161.5 | 9.16 | 5.33 | 0.695/0.303/0.001 | 0 (Music (96)) |
| berserk | F minor (0.577) | 0.577 | -17.82 | -3.13 | 96% | 161.5 | 9.47 | 5.33 | 0.689/0.309/0.001 | 0 (Music (96)) |
| contested+feed | F minor (0.63) | 0.63 | -20.13 | -4.53 | 92% | 161.5 | 10.19 | 4.87 | 0.645/0.353/0.002 | 0 (Music (96)) |
| leading+feed | F minor (0.656) | 0.656 | -19.63 | -4.25 | 89% | 161.5 | 10.54 | 4.73 | 0.585/0.413/0.002 | 0 (Music (96)) |
| final+feed | F minor (0.552) | 0.552 | -17.15 | -2.94 | 97% | 161.5 | 9.04 | 5.23 | 0.704/0.294/0.001 | 0 (Music (96)) |

## Title / menu loop

48.0 s (16 bars), F minor (r 0.887), -19.98 LUFS, true peak -6.96 dBTP, loop-point click ratio 0.67 (limit 3), ear {'windows': 48, 'banned': 0, 'top': ['Music (48)']}.

## Section joins

108 joins checked (every plan step and the wrap, every layer variant); worst high-passed step ratio 1.1 at D->A bed-feed (limit 3).

## Stems (each 24 s section + 2.5 s tail)

| stem | key (r) | r F minor | LUFS | true peak | on grid | events/bar | spectrum dev dB |
|---|---|---|---|---|---|---|---|
| sb-A-bed-feed | F minor (0.693) | 0.693 | -20.85 | -8.43 | 36% | 11.75 | 6.09 |
| sb-A-bed-minor | F minor (0.721) | 0.721 | -20.97 | -7.79 | 38% | 12.75 | 6.08 |
| sb-A-rhythm-drive | C major (0.339) | 0.291 | -20.23 | -5.29 | 100% | 8.12 | 7.25 |
| sb-A-rhythm-heart | F minor (0.583) | 0.583 | -22.92 | -9.85 | 96% | 9.38 | 10.91 |
| sb-A-rhythm-march | C major (0.448) | 0.369 | -24.69 | -5.28 | 98% | 8.88 | 7.55 |
| sb-A-rhythm-pulse | F minor (0.649) | 0.649 | -28.91 | -7.95 | 99% | 8.88 | 5.78 |
| sb-A-theme-corrupt | F major (0.697) | 0.626 | -24.94 | -11.01 | 28% | 9.0 | 8.47 |
| sb-A-theme-feed | Db minor (0.511) | 0.403 | -25.45 | -12.3 | 31% | 9.25 | 13.12 |
| sb-A-theme-minor | F minor (0.691) | 0.691 | -24.2 | -10.72 | 29% | 9.62 | 13.52 |
| sb-B-bed-feed | F minor (0.839) | 0.839 | -21.36 | -7.79 | 27% | 12.25 | 4.56 |
| sb-B-bed-minor | F minor (0.857) | 0.857 | -21.38 | -7.97 | 26% | 12.38 | 4.46 |
| sb-B-rhythm-drive | C major (0.403) | 0.296 | -20.69 | -5.96 | 100% | 8.12 | 7.22 |
| sb-B-rhythm-heart | F minor (0.699) | 0.699 | -23.15 | -8.75 | 96% | 9.12 | 10.76 |
| sb-B-rhythm-march | Db major (0.374) | 0.309 | -24.79 | -5.23 | 99% | 9.12 | 7.47 |
| sb-B-rhythm-pulse | F minor (0.861) | 0.861 | -28.65 | -6.89 | 99% | 9.25 | 5.77 |
| sb-B-theme-corrupt | F major (0.721) | 0.617 | -22.77 | -10.69 | 33% | 9.88 | 8.43 |
| sb-B-theme-feed | A minor (0.651) | 0.132 | -23.36 | -12.18 | 34% | 10.12 | 12.37 |
| sb-B-theme-minor | Ab major (0.711) | 0.588 | -22.66 | -11.73 | 31% | 10.38 | 12.84 |
| sb-C-bed-feed | F minor (0.653) | 0.653 | -21.3 | -7.83 | 44% | 11.62 | 5.69 |
| sb-C-bed-minor | F minor (0.698) | 0.698 | -21.15 | -6.91 | 39% | 11.88 | 5.78 |
| sb-C-rhythm-drive | C major (0.351) | 0.325 | -20.43 | -4.72 | 100% | 8.12 | 7.24 |
| sb-C-rhythm-heart | F minor (0.555) | 0.555 | -23.09 | -7.95 | 95% | 9.75 | 10.89 |
| sb-C-rhythm-march | C minor (0.368) | 0.298 | -25.17 | -6.89 | 98% | 9.25 | 7.32 |
| sb-C-rhythm-pulse | F minor (0.686) | 0.686 | -29.07 | -8.68 | 99% | 8.75 | 5.75 |
| sb-C-theme-corrupt | F major (0.752) | 0.673 | -24.85 | -10.35 | 35% | 8.88 | 8.11 |
| sb-C-theme-feed | F major (0.657) | 0.496 | -25.24 | -11.72 | 32% | 9.38 | 12.74 |
| sb-C-theme-minor | F minor (0.704) | 0.704 | -23.81 | -10.18 | 30% | 8.38 | 12.95 |
| sb-D-bed-feed | F minor (0.607) | 0.607 | -20.77 | -8.38 | 26% | 11.12 | 7.45 |
| sb-D-bed-minor | F minor (0.655) | 0.655 | -20.69 | -6.41 | 32% | 13.0 | 7.32 |
| sb-D-rhythm-drive | Db major (0.383) | 0.307 | -20.62 | -5.37 | 100% | 8.12 | 7.23 |
| sb-D-rhythm-heart | C minor (0.513) | 0.469 | -24.27 | -9.18 | 95% | 9.25 | 10.02 |
| sb-D-rhythm-march | C minor (0.522) | 0.315 | -25.14 | -6.17 | 99% | 8.75 | 7.34 |
| sb-D-rhythm-pulse | C minor (0.66) | 0.582 | -29.62 | -8.79 | 99% | 8.75 | 5.66 |
| sb-D-theme-corrupt | F major (0.641) | 0.489 | -23.18 | -10.26 | 37% | 8.0 | 8.75 |
| sb-D-theme-feed | A minor (0.766) | 0.196 | -24.23 | -11.62 | 33% | 8.5 | 13.77 |
| sb-D-theme-minor | F minor (0.687) | 0.687 | -24.31 | -11.56 | 35% | 8.38 | 13.11 |

Stems are layers, not mixes: a lone rhythm or theme stem may read another key (few pitches); the gate is on the mix states.

## Stings and hatch stingers

| file | s | key (r) | LUFS | true peak | ear: banned (top) |
|---|---|---|---|---|---|
| sting-defeat | 9.0 | F minor (0.884) | -16.04 | -3.9 | 0 (Music (9)) |
| sting-draw | 7.91 | F major (0.826) | -16.04 | -3.53 | 0 (Music (7)) |
| sting-hatch-brute | 6.15 | F minor (0.836) | -16.06 | -1.7 | 0 (Music (6)) |
| sting-hatch-elder | 6.14 | F minor (0.886) | -16.15 | -1.73 | 0 (Music (6)) |
| sting-hatch-grove | 6.06 | F major (0.831) | -16.06 | -1.65 | 0 (Music (5), Violin, fiddle (1)) |
| sting-hatch-mire | 6.52 | F major (0.688) | -16.05 | -2.43 | 0 (Music (6)) |
| sting-hatch-timber | 6.21 | F minor (0.876) | -16.11 | -1.52 | 0 (Music (6)) |
| sting-victory | 7.66 | F major (0.866) | -16.08 | -1.66 | 0 (Music (7)) |

## Effects

| file | s | peak dBFS | true peak | centroid Hz | through 180 Hz high-pass dB | ear: banned (top) |
|---|---|---|---|---|---|---|
| sb_behind_tick | 0.22 | -3.39 | -3.36 | 687 | -0.0 | 0 (Sound effect (1)) |
| sb_block_place_0 | 0.34 | -3.49 | -3.48 | 154 | -6.4 | 0 (Sound effect (1)) |
| sb_block_place_1 | 0.34 | -3.36 | -3.36 | 159 | -5.8 | 0 (Sound effect (1)) |
| sb_bridge_place | 0.9 | -3.67 | -3.66 | 168 | -4.1 | 0 (Drum (1)) |
| sb_climb_step_0 | 0.12 | -3.31 | -3.3 | 136 | -7.3 | 0 (Sound effect (1)) |
| sb_climb_step_1 | 0.12 | -3.47 | -3.47 | 143 | -6.4 | 0 (Sound effect (1)) |
| sb_dodge | 0.34 | -4.31 | -4.15 | 2857 | -0.0 | 0 (Sound effect (1)) |
| sb_hut_level | 1.7 | -3.43 | -3.43 | 370 | -1.7 | 0 (Music (1)) |
| sb_ladder_place | 0.5 | -3.64 | -3.63 | 310 | -0.3 | 0 (Sound effect (1)) |
| sb_ore_bank | 0.8 | -3.46 | -3.46 | 208 | -5.6 | 0 (Music (1)) |
| sb_ore_hit_0 | 0.22 | -3.67 | -3.64 | 136 | -5.8 | 0 (Sound effect (1)) |
| sb_ore_hit_1 | 0.22 | -3.44 | -3.42 | 161 | -2.8 | 0 (Sound effect (1)) |
| sb_ore_hit_2 | 0.22 | -3.59 | -3.59 | 144 | -5.2 | 0 (Sound effect (1)) |
| sb_pit_dig | 0.36 | -3.91 | -3.9 | 409 | -3.8 | 0 (Bang (1)) |
| sb_wall_break | 1.1 | -4.45 | -4.41 | 124 | -11.1 | 0 (Music (1)) |
| sb_wall_hit_0 | 0.22 | -3.35 | -3.35 | 247 | -4.9 | 0 (Sound effect (1)) |
| sb_wall_hit_1 | 0.22 | -3.61 | -3.6 | 232 | -4.3 | 0 (Sound effect (1)) |
| sb_wall_place_0 | 0.45 | -3.54 | -3.51 | 171 | -3.3 | 0 (Sound effect (1)) |
| sb_wall_place_1 | 0.45 | -3.9 | -3.89 | 175 | -3.0 | 0 (Sound effect (1)) |

Closest different cues (13 cues): sb_bridge_place / sb_climb_step MFCC 14.9, centroid x1.24; sb_ore_hit / sb_wall_place MFCC 26.0, centroid x1.26; sb_block_place / sb_bridge_place MFCC 34.7, centroid x1.09

