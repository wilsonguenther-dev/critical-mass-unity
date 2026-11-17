# Critical Mass (Unity)

**Tier: product.**

This repo is the Unity project for Critical Mass, Drivia's game. The Unity project is the repo root
(`Assets/`, `Packages/`, `ProjectSettings/`). It is the full game: a deterministic 16v16 Sim, the Runtime views,
HUD, menus and online play, URP (Forward+, linear colour space) and the new Input System. `Boot.unity` is the only
scene and is empty on disk; the match and every view spawn at runtime. Agents start with [CLAUDE.md](CLAUDE.md). The
browser demo lives in a separate repo, [wilsonguenther-dev/critical-mass](https://github.com/wilsonguenther-dev/critical-mass).

## Documents

| Document | What it is |
| --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | How the game is put together: Sim / Runtime / Editor, data flow, ground grid, camera, the `CM/Toon` shader contract, the art pipeline, the folder map and the Runtime Dependencies table |
| [DECISIONS.md](DECISIONS.md) | The architect's decisions, U-001 to U-079, and which browser-era decisions still apply |
| [PARITY.md](PARITY.md) | The parity gate against the asset bible (U-010): capture, checks, rows p03 to p15, pass rule |
| [loop/README.md](loop/README.md) | The build-loop rules (U-011) and the launch checklist |
| [MAX-ITEMS.md](MAX-ITEMS.md) | Max's mechanics work list and the foundation items |
| [CLAUDE.md](CLAUDE.md) | Rules for AI builders: sim rules, GitHub CI commands, the see-it-in-the-Editor rule |
| [tools/editor-eyes/RUNBOOK.md](tools/editor-eyes/RUNBOOK.md) | Driving the live Unity Editor from the command line: Play mode, captures, Inspector dumps, tests |

## Unity version

Unity **6000.3.28f1** (Unity 6.3), installed through Unity Hub at
`/Applications/Unity/Hub/Editor/6000.3.28f1/`. Set `UNITY_PATH` to point the scripts at another editor binary.

## Commands

All three run Unity headless (`-batchmode -nographics`), write the editor log to `Logs/`, and exit with
Unity's real exit code.

```bash
tools/unity-compile.sh    # open the project, compile every assembly, quit; fails on any compiler error
tools/unity-test.sh       # EditMode tests -> Logs/editmode-results.xml; fails if a test fails or zero tests ran
tools/unity-build-mac.sh  # CriticalMass.Editor.Build.MacOS -> Builds/macOS/CriticalMass.app
```

`tools/unity-build-windows.sh` runs `CriticalMass.Editor.Build.Windows` (output `Builds/Windows/CriticalMass.exe`; the
Windows build module is installed). These scripts refuse a project that an Editor already has open. GitHub runs the
same gates without the Mac: `sim-tests` and `unity-ci` (see CLAUDE.md). `CriticalMass.Editor.ProjectSetup.Apply` re-applies the
project settings and URP assets; `CriticalMass.Editor.ProjectSetup.LogPipeline` logs the active pipeline.

## Layout

| Folder | Purpose |
| --- | --- |
| `Assets/_Game/Sim` | `CriticalMass.Sim`: pure C# simulation, no UnityEngine (`noEngineReferences`) |
| `Assets/_Game/Sim.Tests` | `CriticalMass.Sim.Tests`: EditMode NUnit tests for Sim |
| `Assets/_Game/Runtime` | `CriticalMass.Runtime`: Unity-side game code, references Sim |
| `Assets/_Game/Editor` | `CriticalMass.Editor`: editor-only build and setup scripts |
| `Assets/_Game/Net` | online play (relay, replay) on top of the Sim |
| `Assets/_Game/Runtime.Tests`, `Runtime.PlayTests` | EditMode and PlayMode tests for Runtime |
| `Assets/_Game/Art`, `Audio`, `Resources`, `Scenes`, `Settings` | content, `Boot.unity`, URP assets |

## For teammates

Start with [MAX-ITEMS.md](MAX-ITEMS.md): setup, workflow, the architecture rules, the controls and the mechanics work list.
Work on a branch named `max/<item-id>-short-name` (or your own prefix), one item per pull request into the current
batch candidate branch (`integration/batch-<N>-cand<K>`), not `main`; candidates reach `main` after review.
Run `tools/unity-compile.sh` and `tools/unity-test.sh` before every pull request and paste the result lines.
Game rules go in `Assets/_Game/Sim` with NUnit tests in `Assets/_Game/Sim.Tests`; the view goes in `Assets/_Game/Runtime`.
Never push to `main` directly.

## Git

Binary assets go through Git LFS (`git lfs install` once per machine). Unity YAML files are merged with
UnityYAMLMerge; register the driver once per clone:

```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver '"/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/Helpers/UnityYAMLMerge" merge -p %O %B %A %A'
git config merge.unityyamlmerge.recursive binary
```
