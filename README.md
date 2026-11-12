# Critical Mass (Unity)

**Tier: product.**

This repo is the Unity project for Critical Mass, Drivia's game. The Unity project is the repo root
(`Assets/`, `Packages/`, `ProjectSettings/`). It is currently an empty, verified shell: one empty boot
scene, URP (Forward+, linear colour space), the new Input System, and three command-line gates. The
browser demo lives in a separate repo, [wilsonguenther-dev/critical-mass](https://github.com/wilsonguenther-dev/critical-mass).

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

`CriticalMass.Editor.Build.Windows` (output `Builds/Windows/CriticalMass.exe`) exits with code 3 until the
Windows build module is installed in Unity Hub. `CriticalMass.Editor.ProjectSetup.Apply` re-applies the
project settings and URP assets; `CriticalMass.Editor.ProjectSetup.LogPipeline` logs the active pipeline.

## Layout

| Folder | Purpose |
| --- | --- |
| `Assets/_Game/Sim` | `CriticalMass.Sim`: pure C# simulation, no UnityEngine (`noEngineReferences`) |
| `Assets/_Game/Sim.Tests` | `CriticalMass.Sim.Tests`: EditMode NUnit tests for Sim |
| `Assets/_Game/Runtime` | `CriticalMass.Runtime`: Unity-side game code, references Sim |
| `Assets/_Game/Editor` | `CriticalMass.Editor`: editor-only build and setup scripts |
| `Assets/_Game/Art`, `Audio`, `Scenes`, `Settings` | content, `Boot.unity`, URP assets |

## For teammates

Start with [MAX-ITEMS.md](MAX-ITEMS.md): setup, workflow, the architecture rules, the controls and the mechanics work list.
Work on a branch named `max/<item-id>-short-name` (or your own prefix), one item per pull request into `main`.
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
