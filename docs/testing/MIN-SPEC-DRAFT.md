# Minimum PC and Mac: draft (UNVERIFIED)

**Status: UNVERIFIED.** Nothing in this table has been measured on real hardware at the minimum. The engine rows quote
the engine's published player requirements for the version this project uses (Unity 6.3, "System requirements",
docs.unity.com, read 2026-11-17). The game rows are what the project's own budgets (`tools/budgets.json`) imply. Each
row becomes verified only when a real machine at that level has played a full 16 v 16 match and its feedback line
(`frames.p95_ms`) is recorded here.

## Windows

| Part | Draft minimum | Source | Verified |
| --- | --- | --- | --- |
| Operating system | Windows 10 version 21H1 (build 19043) or newer, 64-bit | engine requirements | no |
| Processor | 64-bit (x64) with SSE2; the build is 64-bit only | engine requirements; the Windows build target | no |
| Graphics | a DirectX 11 or DirectX 12 capable graphics card with the maker's current driver | engine requirements; the project's Windows graphics settings | no |
| Memory | 4 GB, 8 GB suggested (the game's budget is 1.5 GB peak) | `tools/budgets.json` `peak_rss_mb` | no |
| Graphics memory | 1 GB | `tools/budgets.json` `gfx_mb` | no |
| Disk | about 2 GB free (game plus replays and logs) | estimate | no |
| Display | 1280 x 720 or larger | the interface's reference layout | no |

## Mac

| Part | Draft minimum | Source | Verified |
| --- | --- | --- | --- |
| Operating system | macOS Monterey 12 or newer | engine requirements | no |
| Processor | Apple silicon, or Intel x64 | engine requirements | no |
| Graphics | Metal capable | engine requirements | no |
| Memory | 8 GB | the team's test Mac (M1, 8 GB) runs the game; not yet measured at the budget | no |

## What "plays well" means here

The project's frame budget is a p95 frame time of 17.5 ms (about 57 frames a second or better for 95% of frames) on
the default quality, with no hitch over 250 ms. A machine below that at the lowest QUALITY setting is under the minimum.

## How to verify a row

1. Play one full PLAY NOW match on the machine at its lowest QUALITY setting, then one at the setting the game picked.
2. Copy the two `match` lines from the feedback file (see [WINDOWS-FIRST-TEST.md](WINDOWS-FIRST-TEST.md), step 10).
3. Add the machine (processor, graphics card, memory, operating system) and the two `p95_ms` values below, and change
   the row's "Verified" to the date.

| Date | Machine | QUALITY | p95 ms | over 33 ms | Result |
| --- | --- | --- | --- | --- | --- |
| | | | | | |
