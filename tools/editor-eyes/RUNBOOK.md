# Critical Mass: Editor "eyes" runbook

Written 2026-11-17 against Unity 6000.3.28f1, Unity CLI 1.0.0-beta.13 (`~/.unity/bin/unity`), `com.unity.pipeline` 1.0.0.
The Editor checkout is `~/cm-editor` (a git worktree of `~/critical-mass-unity`). The helper scripts are in
`~/cm-editor-eyes/scripts/` (copies in the repo at `tools/editor-eyes/`). Evidence goes in `~/cm-editor-eyes/<UTC stamp>/`.

## Hard rules

- **Exactly one Unity Editor on this Mac.** Check before any launch:
  `ps -axo pid,command | grep '[U]nity.app/Contents/MacOS/Unity'` must print nothing. Never start a second one.
- **Unity Hub must be running and signed in before the Editor starts.** The Editor takes its account from the Hub. Without
  it, Package Manager shows a modal "Unity Package Manager Error ... Please sign in using the Hub" and the Editor hangs on
  it. Never click or key that dialog; close the Editor (below) and ask the owner to sign in to the Hub.
- Drive the Editor **only** through the CLI. No keystrokes, no clicks, no bringing its window forward.
- Every command: stdin closed (`</dev/null`), a hard `timeout`, and `--project-path ~/cm-editor`. The wrapper
  `~/cm-editor-eyes/bin/u <seconds> <unity command args>` does all three. Use it.
- Never drive an Editor whose project is not `~/cm-editor`. Every other worktree is someone else's.
- **Never run**: `unity setup ...`, `unity skill install ...` (least of all `--all`), `unity mcp configure`, `unity mcp`,
  `unity bug`, `unity self-update`, `unity terms accept --yes`, `unity console clear` (`console clear` wipes the owner's
  console too), `unity license ...`, `unity auth ...`, or anything that writes agent or client config.
- Never read or print licence files, `.ulf` files, tokens, or `~/Library/Application Support/Unity*` contents.
- Never `pkill` by pattern. Stop only a PID you started.

## Open and attach

```bash
ps -axo pid,command | grep '[U]nity.app/Contents/MacOS/Unity'      # must be empty
pgrep -x "Unity Hub" >/dev/null || echo "start Unity Hub and sign in first"
TS=$(date -u +%Y%m%dT%H%MZ); E=~/cm-editor-eyes/$TS; mkdir -p $E
timeout 60 ~/.unity/bin/unity open ~/cm-editor --non-interactive --format json --args "-logFile $E/editor.log" </dev/null
# returns at once with launchedPid. This CLI version has NO `open --until-ready`. Wait (background, one loop):
timeout 1300 bash -c 'until timeout 25 ~/.unity/bin/unity status --project-path ~/cm-editor --format json </dev/null | grep -q "\"state\": *\"ready\""; do sleep 20; done'
~/cm-editor-eyes/bin/u 30 editor status            # status ready, playMode stopped
```

A warm start (Library already imported) is ready in about 2 minutes; a cold import much longer. If `~/cm-editor/Library`
is missing, `cp -cR ~/critical-mass-unity/Library ~/cm-editor/Library` (APFS clone, instant, no disk) before opening.
Register it in the Hub list once: `unity projects add ~/cm-editor`.

## Before you drive it

```bash
U=~/cm-editor-eyes/bin/u
$U 30 console status --format json        # note "cursor": read only your own entries later with --since <cursor>
$U 30 editor status                        # playMode must be "stopped" (otherwise it is someone's session: read only)
$U 60 script eval --caller plugin --skill unity-cli 'var s = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Game/Scenes/Boot.unity"); return s.path;'
```

Boot.unity is empty on disk (0 roots). The whole game (world, views, HUD, camera) is spawned at runtime, so hierarchy and
Inspector values only exist in Play mode.

## Play mode

```bash
$U 30 editor set_auto_tick                         # keep ticking while unfocused (REQUIRED, every Play session)
$U 30 editor.playmode set_run_in_background
$U 90 editor.playmode start
timeout 200 ~/.unity/bin/unity status --until-ready --timeout 180 --project-path ~/cm-editor </dev/null
$U 30 job wait -- --condition '{"member":"UnityEngine.Time.frameCount","op":"changed"}' --timeout 10   # proof frames advance
# ... work ...
$U 60 editor.playmode stop
```

In the Editor the title flow is off, so Play starts a 16v16 match at once: Warrior, seed 1337. To pick a class:

```bash
S=~/cm-editor-eyes/scripts
$U 60 script run -- --file $S/Match.cs --entry Match.Start --args '[4, 1337]'   # 0 Warrior 1 Farmer 2 Miner 3 Demolitions 4 Builder 5 Healer
$U 30 script eval --caller plugin --skill unity-cli -- --file $S/state.cs      # tick, class, position, camera
$U 30 script run -- --file $S/Match.cs --entry Match.Autopilot --args '[true]' # bot brain drives actor 0 (false = hold still)
```

The game hands an idle player to the bot brain after 45 s (HumanControlGuard). An agent sends no device input, so
`Match.Autopilot false` also switches idle autopilot off; otherwise actor 0 walks away mid-capture.

## Run an editor method / C#

```bash
$U 30 script eval --caller plugin --skill unity-cli 'return UnityEngine.Time.frameCount;'          # one-liner
$U 60 script eval --caller plugin --skill unity-cli -- --file $S/state.cs                           # a script file
$U 60 script run -- --file $S/Eyes.cs --entry Eyes.MainCam                                          # a static entry point
$U 170 script eval --caller plugin --skill unity-cli --timeout 170 -- --file $S/wait-miner.cs --timeout 160000   # long await
```

Keep script files OUTSIDE `Assets/` (they then never compile into the game). Flags for the Editor command go after `--`
(the CLI's own `--timeout` is seconds; `script eval`'s is milliseconds). `Wait.Until(...)` gives up after 10 s: loop it.

## Look: hierarchy, Inspector values, what draws a cell

```bash
$U 120 script run -- --file $S/Eyes.cs --entry Eyes.Hierarchy --args "[\"$E/hierarchy.txt\", 6, 25]"           # depth, max children
$U 60  script run -- --file $S/Eyes.cs --entry Eyes.Inspect   --args "[\"Hut Azure\", \"$E/inspector.txt\", 2, 3]" # name prefix, hits, depth
$U 60  script run -- --file $S/Eyes.cs --entry Eyes.Column    --args "[11.5, -38.5, \"$E/column.txt\"]"         # renderers over world x,z
```

`Inspect` prints transform, mesh (verts, tris, vertex colours, UV3), renderer and every material property.
World x = sim x, world z = -sim z (`SimSpace.ToWorld`).

## Screenshots

```bash
# The real gameplay camera, full HD, HUD included:
$U 60 capture game_view -- --source camera --camera "Main Camera" --width 1920 --height 1080 --output-path $E/shot.png
# A close-up at the gameplay angle: a temporary disabled camera "Eyes Cam" copying the gameplay lens
$U 30 script run -- --file $S/Eyes.cs --entry Eyes.EyesCam --args '[x, y, z, 55, 225, 8]'    # target, pitch, yaw, distance
$U 60 capture game_view -- --source camera --camera "Eyes Cam" --overlay false --width 1600 --height 900 --output-path $E/close.png
$U 30 script run -- --file $S/Eyes.cs --entry Eyes.EyesCamOff
# Scene view at a pose (works while the Scene tab is hidden behind the Game tab):
$U 30 script run -- --file $S/Eyes.cs --entry Eyes.Look --args '[x, y, z, 30, 135, 2.5]'     # pivot, pitch, yaw, half-height
$U 60 capture scene_view -- --output-path $E/scene.png --width 1600 --height 900
```

Gameplay camera: pitch 55, yaw 225, FOV 35, about 21 u from the player. `--source screen` gives the Game tab's own size
(691 x 344 here, blurry HUD); prefer `--source camera` with an explicit size. `scene_view --source screen` and
`editor_window` front a background tab: do not use them. Open every PNG yourself before calling anything done.

## Drive the player's own input (Builder dig and place)

```bash
$U 150 script eval --caller plugin --skill unity-cli --timeout 150 -- --file $S/dig-now.cs   --timeout 140000
$U 150 script eval --caller plugin --skill unity-cli --timeout 150 -- --file $S/place-now.cs --timeout 140000
```

They let autopilot walk actor 0 to a legal cell, take control back, aim, and press DigPlace (what F does) through
`HumanInputState`. `HumanInputSource.Update` rewrites the aim from the mouse ray every frame, so the scripts disable that
component for the press only (a few frames); it is re-enabled in a `finally`.

## Console

```bash
$U 30 console status --format json                                    # counts, compilationFailed, cursor
$U 60 console get -- --since <cursor> --level warn --tail 200 --format json
```

`--level` is a floor (`log` < `warn` < `error`). Never `console clear`.

## Tests

```bash
$U 60 test run -- --mode editor --async-tests true       # inside the open Editor (unity test refuses an open project)
$U 30 test status --format json                          # poll once a minute until it is not "running"
$U 60 test run -- --mode editor --filter HutTests        # a narrow synchronous run (default timeout 600 s)
```

The full EditMode suite includes slow soak tests; expect many minutes. Never run `unity test ~/cm-editor` while the Editor
is open (exit 6). On GitHub: see the repo CLAUDE.md.

## Close the Editor

```bash
timeout 60 ~/.unity/bin/unity close ~/cm-editor --timeout 45 </dev/null      # graceful, exits WITHOUT saving
# only if that fails and the PID is the one YOU launched:  kill <launchedPid>
```

Opening the Editor rewrites about 400 texture `.meta` files and `ArtCatalog.asset` in `~/cm-editor` (the project's
import settings differ from what is committed). Never commit those by accident: stage files by name.

## What hung or failed, and how to avoid it

- `unity analytics --help` and other prompts wait on stdin forever: always `</dev/null` and `timeout`.
- `unity open --until-ready` does not exist in CLI 1.0.0-beta.13 (the plugin docs describe a newer CLI): open, then poll
  `unity status`.
- Editor started without the Hub: modal "Unity Package Manager Error" for `com.unity.pipeline`; the Pipeline server never
  starts and `unity status` says STATUS_NO_INSTANCES. Fix: Hub running and signed in, then relaunch.
- `script eval` with `Wait.Until` throws `WaitTimeoutException` after 10 s: loop short waits.
- `??` on Unity objects (`GetComponent<T>() ?? Add...`) returns the destroyed/fake-null object: use `if (x == null)`.
- A capture right after a scene reload can fail with "No camera found to capture" (logged as a console error): wait a
  second and retry.
- On load the console shows 3 Input System errors (`Could not create a device for 'Keyboard (OSX)'`, NRE resolving
  `CameraView:<Gamepad>/select` with the MultiTap interaction) and `Keyboard.current` is null in this Editor session.
