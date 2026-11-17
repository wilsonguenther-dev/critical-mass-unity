# CLAUDE.md: Critical Mass (Unity)

Read this before touching the repo. README.md, ARCHITECTURE.md and DECISIONS.md hold the detail.

## Core loop (five lines)

1. Two teams of 16 (Azure, Crimson), six classes (Warrior, Farmer, Miner, Demolitions, Builder, Healer), floating-island map.
2. Gather (wood, water, food, ore), deliver to your base, feed the Kaiju egg.
3. Hold the hill to score; walls, digs, ladders and bridges change the routes.
4. The Kaiju hatches mid-match and fights for the team that raised it.
5. Most hill score when the clock ends wins.

## Folder map

| Path | What |
| --- | --- |
| `Assets/_Game/Sim` | `CriticalMass.Sim`: the deterministic simulation, pure C#, `noEngineReferences` |
| `Assets/_Game/Sim.Tests` | EditMode NUnit tests for the Sim (also run on .NET without Unity by `sim-tests`) |
| `Assets/_Game/Net` | online play (relay, replay) on top of the Sim |
| `Assets/_Game/Runtime` | `CriticalMass.Runtime`: views, HUD, menus, input, camera, flow; reads the Sim, never steps it by hand |
| `Assets/_Game/Runtime.Tests`, `Runtime.PlayTests` | EditMode and PlayMode tests for Runtime |
| `Assets/_Game/Editor` | editor-only build and setup code (`CriticalMass.Editor.Build.MacOS`, `.Windows`, `ProjectSetup`) |
| `Assets/_Game/Art`, `Audio`, `Resources`, `Settings`, `Scenes` | content; `Scenes/Boot.unity` is the only scene and is empty on disk (everything spawns at runtime) |
| `tools/` | shell harness (compile, test, build, captures), `tools/ci/` for GitHub, `tools/editor-eyes/` for the live Editor |
| `loop/` | build-loop rules, items, gates |

## Naming

- Branches: `fix/<batch><round>-<topic>` (e.g. `fix/b13r3-walls`), `ci/<topic>`, `tools/<topic>`; batch candidates are
  `integration/batch-<N>-cand<K>`. One item per PR, into the current candidate branch, never into `main`.
- C#: namespaces follow folders (`CriticalMass.Sim.Core`, `CriticalMass.Runtime.View.B13`); test names are sentences
  (`HutTests.BalanceAgainstTheSameSeedControlStaysInsideTheLimits`).
- Decisions are `U-0xx` in DECISIONS.md; work items carry their batch id (`b13 item H`) in the summary comment.

## Deterministic-sim rules (never break these)

- `Assets/_Game/Sim` never references `UnityEngine` (the asmdef enforces `noEngineReferences`). Doubles, `JsMath`, fixed tick.
- Nothing touches `Time.timeScale` to speed, slow or pause the sim; the driver advances ticks itself. Views and juice read
  sim state and interpolate; they never write it.
- No allocations per tick in the Sim (static scratch, fixed pools such as `BuilderState.Pieces`); no LINQ, no `new` in hot loops.
- Views make their objects once when a world binds, then only move, scale and toggle them.

## Input, rendering, platforms

- Input bindings: `Assets/_Game/Settings/Controls.inputactions` (Input System 1.20.1); presets and remaps in
  `Runtime/Input/ControlPresets.cs`, glyphs in `InputGlyphs.cs`. Keyboard: F/U DigPlace (Builder dig/place, Miner mine),
  X/K/right mouse Ability (Builder ladder or bridge), Space/C Jump, E/H/V Interact, I switches dig/place.
- Render pipeline: URP 17.3 (Forward+, linear), `Assets/_Game/Settings/CriticalMass_URP*.asset`, shared toon shader
  `Assets/_Game/Art/Shaders/CMToon.shader` (`CM/Toon`); extend it, never fork it.
- Unity 6000.3.28f1, Mono backend. Players: macOS (Apple silicon, unsigned) and Windows x64.

## Compile and test on GitHub (the Mac has one Unity slot: offload)

```bash
R=wilsonguenther-dev/critical-mass-unity
gh workflow run sim-tests.yml -R $R --ref <branch> -f suite=fast        # Sim on .NET, no Unity, 2-5 min (slow|all for soaks)
gh workflow run unity-ci.yml  -R $R --ref <branch> -f jobs=tests        # EditMode + 3 PlayMode shards (GameCI, Linux)
gh workflow run unity-ci.yml  -R $R --ref <branch> -f jobs=editmode     # or playmode | build-mac | build-windows | builds | all
gh run list -R $R --branch <branch> --limit 5
gh run view <id> -R $R --log-failed | grep -E "Failed :|Test Count"
```

`sim-tests` runs by itself only on PRs to `main` that touch the Sim and on pushes to `ci/**`; `unity-ci` runs on PRs to
`main` only with the label `ci:unity`. Everything else is a manual `gh workflow run`.

Known reds on `integration/batch-13-cand1` at a72d2a5e (2026-11-17): `sim-tests` 631 tests, 10 failed
(BotDifficulty DG21 proxy ordering, HutTests balance limit, KaijuTypeTests balance, MovementShiver x4, OutcomesSoak AFK,
RulesCombinedSoak, SimPhysicsSoak); `unity-ci` EditMode and PlayMode shards 2 and 3 red, both player builds green.
A red you did not cause is not yours to "fix" by loosening a limit: name it in the PR.

Local (Mac, headless, only when the Editor is NOT open on that checkout): `tools/unity-compile.sh`, `tools/unity-test.sh`.

## See it before you call it done (new rule, 2026-11-17)

Any change that is visible (a view, mesh, material, VFX, HUD, camera, terrain look) is not done until you have looked at
it in the live Editor from the gameplay camera and opened the PNG yourself. Tests passing is not "looks right".

- The Editor runs on the worktree `~/cm-editor` (one Editor only, never start another). Runbook with every command:
  `~/cm-editor-eyes/RUNBOOK.md` (copy in `tools/editor-eyes/RUNBOOK.md`); helper scripts in `tools/editor-eyes/`.
- Minimum loop: `u 30 editor status` -> `editor set_auto_tick` -> `editor.playmode start` -> pick a class with
  `Match.Start` -> `capture game_view -- --source camera --camera "Main Camera" --width 1920 --height 1080` -> close-up with
  `Eyes.EyesCam` -> `Eyes.Inspect` for the values -> `console get --since <cursor> --level warn` -> `editor.playmode stop`.
- Put the PNGs (and what still looks wrong) in the PR. Judge against the art direction: silhouette, three values, team
  colour on tops, ink outline on solids, no flat black blobs, no holes to the sky, nothing floating or z-fighting.

## Never

- Never push to `main`. Never force-push a shared branch (`integration/*`, anyone else's `fix/*`). Never push to a candidate
  branch directly: PR into it.
- No hand edits to `.meta`, `.unity`, `.prefab` or `.asset` YAML without opening them in the Editor and letting Unity
  write them. Stage files by name: opening the Editor rewrites hundreds of texture `.meta` files you must not commit.
- Never run `unity setup`, `unity skill install`, `unity mcp configure`, `unity console clear`, or anything that writes
  agent config, licences or tokens. Never read or print secrets or licence files.
- Never start a second Unity Editor, never send keys or clicks to it, never `pkill` by pattern.
- Never add per-tick allocations, `UnityEngine` in the Sim, or `Time.timeScale` writes.
