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
