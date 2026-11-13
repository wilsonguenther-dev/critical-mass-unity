# Audio credits

Every audio file the game ships is listed here with its source page, author, licence and the sha256 of the file
that was downloaded. Nothing with unclear rights ships. All music and UI sounds below are **CC0 1.0** (public domain
dedication, https://creativecommons.org/publicdomain/zero/1.0/): free to ship in a sold game, credit not required;
we give it anyway. Licences were read on each source page on 2026-11-12 (UTC) (the OpenGameArt "License(s)" field,
the Kenney page's "License: CC0" and the pack's `License.txt`).

Processing (tools/audio/prepare_assets.py and tools/audio/bake_voice.py, shared helpers in tools/audio/audionorm.py):
two-pass EBU R128 loudness normalisation with ffmpeg loudnorm (one linear gain), then Ogg Vorbis. Targets: voice
and stings -16 LUFS, music -20 LUFS, UI clicks peak-normalised to -9 dBTP; true peak at or under -1 dBTP on every
file. The gate is `tools/audio-loudness-check.sh` (ffmpeg ebur128 on every file under Assets/_Game/Audio).

Note for the integrator: the combat-feel branch (feat/combat-feel) adds its own section for the sound effects in
`Resources/CMSfx`. When both land, keep both sections in this one file.

## Music (Assets/_Game/Audio/Music/Resources/CMMusic)

| Shipped file | Used as | Title, author | Source page | Downloaded file | sha256 of the download | Licence |
|---|---|---|---|---|---|---|
| `music-title.ogg` | title and menus, quietly under results | "Dark Shrine Loop", qubodup | https://opengameart.org/content/dark-shrine-loop | `qubodup-yd-DarkShrineLoop-OpenGameArt.ogg` | `9580618dc851f70c3a11b5ff87672867de44cd38b35dabf926d6befde20e78d0` | CC0 1.0 |
| `music-match.ogg` | match | "Battle Theme A", cynicmusic | https://opengameart.org/content/battle-theme-a | `battleThemeA.mp3` | `6042399782e581d753d616bc703e66483d5eccb5fb687a20c9a552d68c49e620` | CC0 1.0 |
| `music-final.ogg` | final-minute lift | "Determined Pursuit (Epic Orchestra Loop)", Emma_MA | https://opengameart.org/content/determined-pursuit-epic-orchestra-loop | `determined_pursuit_loop.wav` | `e4f3be098b50213b56a60aefe60fffae79cd9f7c9008088f93c187ef1bbc856b` | CC0 1.0 |
| `music-berserk.ogg` | berserk Kaiju | "Boss Battle #2 (Symphonic Metal)", nene (loop part only) | https://opengameart.org/content/boss-battle-2-symphonic-metal | `boss_battle_#2_metal_pack.zip` → `boss_battle_#2_metal_loop.wav` | zip `27845e601593ec3348488b0479111cda656a97127ef69a066256f7c967e125ab` | CC0 1.0 |

## Stings (Assets/_Game/Audio/Music/Resources/CMStings)

| Shipped file | Title, author | Source page | Downloaded file | sha256 of the download | Licence |
|---|---|---|---|---|---|
| `sting-victory.ogg` (first 9 s, 2 s fade) | "Medieval: Victory Theme", RandomMind | https://opengameart.org/content/medieval-victory-theme | `victory.wav` | `91f35879d31091cdeb38721fb4d985a0e7363c0caa97d738ec879bf96e8538f4` | CC0 1.0 |
| `sting-defeat.ogg` (first 9 s, 2.5 s fade) | "Medieval: Defeat Theme", RandomMind | https://opengameart.org/content/medieval-defeat-theme | `defeat.wav` | `832a9062921000cde7044f18838d11682c578067db0c60a124d95473bb122fc8` | CC0 1.0 |
| `sting-draw.ogg` | "Music Jingles", Kenney (`Pizzicato jingles/jingles_PIZZI07.ogg`) | https://kenney.nl/assets/music-jingles | `kenney_music-jingles.zip` | zip `b729ba57959bd58793d2c5cafa348aaf2655d354f3da35ec4729e03ec77197b8` | CC0 1.0 |

## UI sounds (Assets/_Game/Audio/Ui/Resources/CMUi)

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
