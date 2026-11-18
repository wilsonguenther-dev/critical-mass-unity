# Sound effects credits (Unity: Assets/_Game/Audio/Resources/CMSfx)

Copied unchanged (same bytes) from the browser repo `~/critical-mass` at 09e0904, `public/sfx/v1/` (2026-11-12, combat-feel
branch). The text below is that folder's CREDITS.md as it was; `credits.json` next to this file is its machine-readable list.
Where it says "here" it now means `Assets/_Game/Audio/Resources/CMSfx/`.

## Original credits (public/sfx/v1)

Every clip here is **CC0 1.0** (public domain dedication) from Kenney (www.kenney.nl). Credit is not required; we give it anyway.
Each pack page states "License | Creative Commons CC0" and each zip's `License.txt` says "License: (Creative Commons Zero, CC0) http://creativecommons.org/publicdomain/zero/1.0/".
Downloaded 2026-10-28 (UTC) into `/Users/wilsonguenther/critical-mass-assets/downloads/kenney-audio/` (outside the repo); only the chosen clips are copied here.

Processing: Kenney ships Ogg Vorbis 44.1/48 kHz STEREO; each chosen clip is re-encoded MONO with
`ffmpeg -i <original> -ac 1 -c:a libvorbis -q:a 4 -map_metadata -1 <file>` (names unchanged).
The machine-readable list is `credits.json`; `npm run assets:check` fails on any audio file under `public/` that is not listed there as CC0-1.0 with a Kenney URL and the pack zip's sha256, or that is not mono.

The wind ambience (`amb_wind`) has no file: it is procedural filtered noise (`src/audio/sfx.ts`). The Kaiju roar and the berserk lock are layered at runtime from two heavy Impact Sounds hits at playbackRate 0.5-0.6 plus a procedural 55 Hz swell.

## Kenney Impact Sounds

- Page: https://kenney.nl/assets/impact-sounds
- Licence: CC0 1.0 (page + License.txt)
- Zip sha256: `029d734af1582474edf3a694d1b0cebc97c1c152f2f39fa34d4c2bafc5de77f8`
- Files (31): `footstep_grass_000.ogg`, `footstep_grass_001.ogg`, `footstep_grass_002.ogg`, `footstep_wood_000.ogg`, `footstep_wood_001.ogg`, `footstep_wood_002.ogg`, `footstep_concrete_000.ogg`, `footstep_concrete_001.ogg`, `footstep_concrete_002.ogg`, `impactPunch_medium_000.ogg`, `impactPunch_medium_001.ogg`, `impactPlate_medium_000.ogg`, `impactPlate_medium_001.ogg`, `impactPlate_light_000.ogg`, `impactPlate_light_002.ogg`, `impactPunch_heavy_000.ogg`, `impactPunch_heavy_001.ogg`, `impactWood_heavy_000.ogg`, `impactWood_heavy_001.ogg`, `impactPunch_heavy_002.ogg`, `impactPlate_heavy_001.ogg`, `impactMining_000.ogg`, `impactMining_001.ogg`, `impactSoft_heavy_000.ogg`, `impactSoft_heavy_001.ogg`, `impactWood_medium_000.ogg`, `impactPlank_medium_000.ogg`, `impactPlank_medium_001.ogg`, `impactMining_002.ogg`, `impactMining_003.ogg`, `impactWood_heavy_003.ogg`

## Kenney RPG Audio

- Page: https://kenney.nl/assets/rpg-audio
- Licence: CC0 1.0 (page + License.txt)
- Zip sha256: `6dbeaf8544da958d8f2adcb4a4a4b76c1ade34a05f8ab9edccd327da7375f38b`
- Files (3): `knifeSlice.ogg`, `knifeSlice2.ogg`, `handleCoins.ogg`

## Kenney Interface Sounds

- Page: https://kenney.nl/assets/interface-sounds
- Licence: CC0 1.0 (page + License.txt)
- Zip sha256: `f2193d072726d6758a5f7871b2dcc54dcce0d5c35c6f0a62f92549b327c81232`
- Files (7): `confirmation_002.ogg`, `bong_001.ogg`, `maximize_003.ogg`, `error_004.ogg`, `toggle_001.ogg`, `confirmation_003.ogg`, `click_001.ogg`

## Kenney UI Audio

- Page: https://kenney.nl/assets/ui-audio
- Licence: CC0 1.0 (page + License.txt)
- Zip sha256: `946fc23a63d535d693eb31b2eabb80c8c28d6351e2186b344ceb71b2cb1d5eb6`
- Files (1): `click3.ogg`

# Music, stings, UI sounds and voice credits

Every audio file the game ships is listed here with its source page, author, licence and the sha256 of the file
that was downloaded. Nothing with unclear rights ships. The music, stings and Skybreak effects are original (next
section). The UI sounds below are **CC0 1.0** (public domain
dedication, https://creativecommons.org/publicdomain/zero/1.0/): free to ship in a sold game, credit not required;
we give it anyway. Licences were read on each source page on 2026-11-12 (UTC) (the OpenGameArt "License(s)" field,
the Kenney page's "License: CC0" and the pack's `License.txt`).

Processing (tools/audio/prepare_assets.py and tools/audio/bake_voice.py, shared helpers in tools/audio/audionorm.py):
two-pass EBU R128 loudness normalisation with ffmpeg loudnorm (one linear gain), then Ogg Vorbis. Targets: voice
and stings -16 LUFS, music -20 LUFS, UI clicks peak-normalised to -9 dBTP; true peak at or under -1 dBTP on every
file. The gate is `tools/audio-loudness-check.sh` (ffmpeg ebur128 on every file under Assets/_Game/Audio).

The sound effects in `Resources/CMSfx` have their own section at the top of this file.

## Music, stings and Skybreak effects: original (b15, 2026-11-17)

The game's music is ONE original score, "Skybreak", in F minor at 80 BPM, written for the story intro film and
extended for the match. No library music, no samples, no soundfonts, no generated-music or voice service. Every file
below is rendered from code in this repository (`tools/audio/skybreak/build_game_score.py`, on the instruments of
`tools/audio/skybreak/score_intro.py`): oscillators, filtered noise and one synthetic hall, so there is no third-party
audio in it and nothing to credit or license. The four OpenGameArt and Kenney library loops and stings that shipped
before b15 (qubodup, cynicmusic, Emma_MA, nene, RandomMind, Kenney "Music Jingles") are removed from the build.

| Folder | What it holds |
|---|---|
| `Assets/_Game/Audio/Music/Resources/CMMusic` | `music-title.ogg`: the 16-bar title loop (menus, quietly under the results) |
| `Assets/_Game/Audio/Music/Resources/CMScore` | 36 stems: 4 eight-bar sections x bed (minor, feed), rhythm (pulse, march, drive, heart), theme (minor, feed, corrupt) |
| `Assets/_Game/Audio/Music/Resources/CMStings` | victory, defeat and draw stings; one hatch stinger per Kaiju type (elder, grove, brute, timber, mire) |
| `Assets/_Game/Audio/Resources/CMSfxScore` | 13 short effects (19 files with their variations) in the score's key: dodge, wall and block placed, wall hit and break, hut level-up, ore hit and banked, ladder, climb step, bridge, pit dig, the behind-you tick |

Tools used to make them (none ships in the game): NumPy and SciPy (BSD-3-Clause), soundfile (BSD-3-Clause), librosa
(ISC, the analyzer only), FFmpeg (LGPL/GPL command-line encoder, run as a tool; its output carries no licence), and the
analyzer's ear, the AudioSet AST classifier `MIT/ast-finetuned-audioset-10-10-0.4593` (BSD-3-Clause, run locally as a
check only). `tools/audio/skybreak/ANALYZER-REPORT.md` holds the key, grid, loudness, balance, click and tagger results.

## UI sounds (Assets/_Game/Audio/Ui/Resources/CMUi)

- Page: https://kenney.nl/assets/interface-sounds
- Licence: CC0 1.0 (each source page; details in the table below)
- Files (5): `ui-hover.ogg`, `ui-step.ogg`, `ui-confirm.ogg`, `ui-back.ogg`, `ui-error.ogg`

From Kenney "Interface Sounds", https://kenney.nl/assets/interface-sounds, CC0 1.0 (page and `License.txt`), the same
download the browser game used (`~/critical-mass-assets/downloads/kenney-audio/kenney_interface-sounds.zip`, zip
sha256 `f2193d072726d6758a5f7871b2dcc54dcce0d5c35c6f0a62f92549b327c81232`). Re-encoded mono.

| Shipped file | Cue | Original file |
|---|---|---|
| `ui-hover.ogg` | focus moves (hover, keyboard, controller) | `Audio/tick_002.ogg` |
| `ui-step.ogg` | a stepper or slider changes value | `Audio/tick_004.ogg` |
| `ui-confirm.ogg` | press, open | `Audio/confirmation_001.ogg` |
| `ui-back.ogg` | back, close, resume | `Audio/back_002.ogg` |
| `ui-error.ogg` | a switched-off control, a rebind conflict | `Audio/error_006.ogg` |

## Announcer and narrator voice (Assets/_Game/Audio/Voice/Resources/CMVoice)

Generated on this Mac with Kokoro-82M (https://huggingface.co/hexgrad/Kokoro-82M, model and voices Apache-2.0,
commercial use allowed), stock voice `bm_george`, announcer lines at 0.95 speed and narrator lines at 0.9. A stock
voice of the engine; it imitates no real person. The words are the game's own (words pack
`Assets/_Game/Content/Lore/announcer.json` and `story.json`, DG-04). `Assets/_Game/Audio/Voice/voice-manifest.json`
lists every line's id, text, voice, speed and length. The owner picks the final narrator voice by ear from the
audition in `Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/voice-tests/` (am_onyx, am_fenrir, bm_george);
changing `VOICE` in tools/audio/bake_voice.py and running it again re-bakes every line.

## Procedural sounds (sound coverage, 2026-11-12)

No files and no third-party code or service: every cue in `Runtime/Audio/Sound/SoundTable.cs` that the 42 Kenney clips
above do not cover (explosions, fuses, mines, the Kaiju's roar, hatch, wind-up, slam and steps, berserk loop, heals,
builder pieces, pillar topple, pickups, level up, hill stings, pings, big-moment stingers, the low-frequency layer, wind,
water and birds) is rendered at load by the game's own deterministic synthesiser (`Runtime/Audio/Sound/SoundSynth.cs`:
oscillators, xorshift noise, filters and envelopes, recipe ids seed the noise). Nothing imitates a person or an animal.
`SoundTable.SynthOnly()` lists the cues to replace with recorded CC0 sounds later.

## Story intro film (story intro 3)

The intro film's soundtrack (`Assets/StreamingAssets/Story/intro-film-1.mp4`, built offline by `tools/film/build_soundtrack.py`)
is mixed from the repo's own music (`music-final.ogg`, `music-title.ogg`), the repo's narration lines, the game's synthesis
recipes, and these CC0 1.0 Kenney clips, read from the same pack downloads listed above (nothing new is copied into the repo
as a loose file; they exist only inside the film's mixed AAC track): Kenney Impact Sounds (`impactGlass_heavy_000`,
`impactGlass_heavy_002`, `impactMetal_medium_002`, `impactMining_001`, `impactSoft_medium_001`, `impactWood_heavy_000`,
`impactWood_heavy_001`, `impactWood_heavy_003`, `footstep_grass_001`) and Kenney RPG Audio (cloth, creak and draw-knife
clips named in the builder). Credit is not required; we give it anyway.
