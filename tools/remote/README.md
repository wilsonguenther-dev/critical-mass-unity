# The second Mac: play-test, capture and worker box

The owner's MacBook Air (Apple M1, 8 cores, 8 GB, macOS 26.3, `wilsons-macbook-air.local`, key-auth ssh as the
owner's user) runs the BUILT game's hidden play-tests ("smoke") and captures, so those runs no longer take the main
Mac's single heavy-job lock (`~/bin/cm-heavy`) for 10 to 25 minutes. It also takes small non-Unity jobs.

Everything on the box lives under `~/cm-remote` (nothing else on it is touched by these tools):

| path | what |
|---|---|
| `bin/air-run.sh` | the runner (copied from `tools/remote/air-run.sh` on every call) |
| `env.sh` | PATH for user-space tools (copied from `tools/remote/env.sh`) |
| `builds/<commit>/CriticalMass.app` | uploaded builds; the newest two are kept, older ones deleted by the runner |
| `runs/<id>/` | one job's files until the caller copies them back (then deleted; uncollected runs go after 2 days) |
| `lock.d/` | the queue lock: one job at a time (`owner` says who holds it) |
| `tools/bin` `venv/` `dotnet/` `nuget/` | static ffmpeg and ffprobe, Python 3.9 venv (Pillow, numpy), .NET SDK |
| `work/` | working folder for `tools/remote-run.sh` |

## Play-tests and captures

```
tools/remote-playtest.sh --host wilsons-macbook-air.local --app Builds/macOS/CriticalMass.app \
  --mode smoke --quality Medium --matches 1 --out Logs/remote/smoke-medium
tools/remote-playtest.sh --app Builds/macOS/CriticalMass.app --mode captures --quality Medium --matches 1 --out Logs/remote/cap
```

Flags: `--mode smoke|captures`, `--quality High|Medium|Low`, `--matches N`, `--out DIR`, `--profile default|floor`
(floor adds the 30 fps cap and the floor budgets), `--speed X`, `--novsync`, `--width W --height H`,
`--title-seconds S`, `--timeout S` (per match), `--lock-wait S` (queue wait, default 3600), `--keep N` (builds kept).

What happens: the .app is rsynced (only changed files; 937 MB the first time, about 25 s on the home Wi-Fi) to
`builds/<commit>/` (the commit is read from the build's own BuildInfo, `commit=` in `resources.assets`). The runner is
started detached with `nohup`, so a dropped ssh connection does not kill the run. On the box it takes the queue lock
(a second caller prints `remote: waiting in the queue, the box is busy: pid=... id=... since=...` and waits), refuses
on battery under 25 % or under 12 GB free disk, mutes the output for the run and restores it, keeps the Mac awake with
`caffeinate -w <player pid>`, exports and restores the game's saved settings (defaults domain
`com.drivia.criticalmass`), clears any quarantine attribute, and starts the player with the same automation flags as
`tools/demo-smoke.sh` (`-autoplay -quitAfterMatch -autoplayMatches -autoplayTitleSeconds -autoplayTimeout -quality
-dataDir -logFile -perfLog`, plus `-shots` in captures mode). It samples memory and CPU once a second like the local
smoke. The caller polls every 10 s, copies the run folder back (Player.log, perf*.json, samples.tsv, run.env,
runner.log, data/), deletes it on the box, and judges it here with `tools/demo-smoke.sh --summarise-only` (same
budgets, `tools/budgets.json`; `summary.json` in OUT). Exit 0 pass, 1 fail (exception lines, budget over or not
measured), 4 match timeout, 124 overall timeout, 75 queue wait ran out, 67 refused, 69 unreachable, 66 no app.
Ctrl-C asks the box to stop the job (it restores mute and frees the lock).

Launching: proven on 2026-11-13 that the player started straight from the ssh-born shell gets a Metal device and a
window in the logged-in console session (`Initializing Metal device caps: Apple M1`, title on screen, shot rendered).
No `open`, `launchctl asuser` or `osascript` is needed, and the player's exit code survives. The build is ad-hoc signed
and rsync adds no quarantine flag, so Gatekeeper never prompts; the runner still runs `xattr -dr com.apple.quarantine`.
The game window is visible on the Air's own screen during a run (nobody uses that screen).

Captures: the player's `-shots` writes `shot-title.png` (2 s after the title) and `shot-mid.png` (60 s of match time)
at the window size; they land in `OUT/shots/`. Screen grabs from outside the game do not work over ssh (macOS answers
`could not create image from display`: no screen-recording grant for sshd), so the Editor capture walks
(`tools/unity-owner-walk-capture.sh`, `unity-batch*-capture.sh`) stay on a machine with the Unity Editor.

## Any other command (same queue)

```
tools/remote-run.sh -- uname -a
tools/remote-run.sh --put path/to/clip.mov --get out.mp4 Logs/ -- ffmpeg -y -i clip.mov -vf scale=1280:-2 out.mp4
tools/remote-run.sh --put some/script.py -- python3 script.py
```

The command runs with bash in `~/cm-remote/work` with `env.sh` sourced (ffmpeg, the venv's python3, dotnet on PATH),
inside the same lock as the play-tests, so it never runs during a smoke. `--put` copies into `work/` first, `--get`
copies a path relative to `work/` back. Output is printed; the exit code is the command's.

## Air numbers (first low-spec data point)

Build 7f8bfd8 (integration batch 5, v1.0.316), 2026-11-13, one 16v16 bot match (480 s of match time, about 511 s
wall), Medium, 1920x1080 window, 60 fps cap, M1 8 GB on its charger, nothing else running on the box.

| run | frame p50 / p95 / p99 ms | frames over 33 ms | worst frame | GPU p50 / p95 ms | main CPU p50 / p95 ms | peak memory (RSS) | boot to title | title CPU | judged |
|---|---|---|---|---|---|---|---|---|---|
| smoke, Medium | 16.67 / 16.75 / 17.64 | 112 of 28,465 (0.39 %) | 1,767 ms at 0.4 s (match start load) | 8.3 / 11.7 | 15.6 / 18.9 | 688 MB | 4.87 s | 50 % of a core | FAIL: idle CPU, worst hitch |
| captures, Medium | 16.67 / 16.74 / 17.59 | 140 of 28,516 (0.49 %) | 1,650 ms at match start | 8.2 / 11.6 | 16.3 / 18.5 | 819 MB | 4.68 s | 63 % of a core | FAIL: idle CPU, worst hitch |

Batches 673 to 698, triangles about 1.0 M, 55 to 60 shadow casters, sim time dropped 0.4 to 0.6 s per match, 0
exceptions, 0 errors, player exit 0 both times. The two budget misses are the same on every machine class so far:
the title screen burns 50 to 63 % of a core (budget 35 %) and one 1.6 to 1.8 s hitch when the match loads (budget
250 ms). Everything else is inside the High-machine budgets.

Verdict: the game is playable on an 8 GB M1 at Medium: it holds the 60 fps cap (p95 16.75 ms, under 0.5 % of frames
over 33 ms), with GPU headroom (p95 11.7 ms) and the main thread as the limit (p95 about 18.5 to 18.9 ms). Fighters
render with bodies there: the shot at 60 s of match time shows the player's warrior DUNE as a full model (hair, armour,
weapon), not a ring. The title and mid-match shots are the only player-side captures, so the match-start moment
(where the main Mac showed rings without bodies) is not captured by this tool.

## What the Air can and cannot take (8 GB)

Installed on 2026-11-13 by `tools/remote-run.sh --put tools/remote/setup-worker.sh -- bash setup-worker.sh` (user
space, idempotent, about 1.7 GB under `~/cm-remote` including the uploaded build): static arm64 ffmpeg and ffprobe
9.0.2, a Python 3.9.6 venv with Pillow 11.3 and numpy 2.0, the .NET 8 SDK (8.0.428), and a sparse clone of this repo
(`~/cm-remote/repo`: only the Sim, its tests, `tools/ci`, `tools/golden`, `ProjectSettings`).

Good fits (no Unity, a few hundred MB to 2 GB of memory): BUILT-player smoke, soak and capture runs (Medium uses
about 0.7 to 0.8 GB, leaving room); ffmpeg work for the intro film, trailers and clip encodes; image processing with
Pillow and numpy for HUD and intro art (resizing, contact sheets, luma and overlap checks); the no-Unity Sim tests and
compile check (proven 2026-11-13: `tools/remote-run.sh -- "cd ~/cm-remote/repo && tools/ci/sim-tests.sh"` built and ran 456 tests in 24 s wall, 455 passed, 0 failed, 1 explicit skipped; refresh the clone first with `setup-worker.sh repo`); any script that only needs bash, python3, ffmpeg or dotnet.

Not on this box: the Unity Editor (EditMode and PlayMode tests, player builds, the Editor capture walks, clean
reimports: 8 GB is under the Editor's working set for this project, and it is not installed), the 3D model tools,
and heavy Blender jobs (renders, LOD bakes, rig and export batches). Builds still come from the main Mac or the GitHub
Unity CI (`.github/workflows/unity-ci.yml`) and are then tested here.
