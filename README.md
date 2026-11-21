# Critical Mass (Unity)

**Tier: product.**

## START HERE

Critical Mass is a top-down team action game: two teams of 16 (people and bots) fight over one map, each player in a
class (Warrior, Farmer, Miner, Demolitions, Builder or Healer), gathering biomass for their Kaiju and holding the hill until one side wins.
It runs on Mac and Windows, solo against bots or online with a room code. The game rules are a deterministic
simulation (the same inputs always give the same match), and everything you see is a separate view on top of it.

### Get a build

| You want | Do this |
| --- | --- |
| To play the latest test build | open the repo's **Releases** page and download the newest `demo-v1.0.N` pre-release (a Mac DMG and a Windows zip) |
| To test on Windows for the first time | follow [docs/testing/WINDOWS-FIRST-TEST.md](docs/testing/WINDOWS-FIRST-TEST.md) |
| A build of a branch | run the `unity-ci` workflow on that branch with `jobs: builds` (see "Run the tests on GitHub"); the players are the run's artifacts `player-StandaloneOSX` and `player-StandaloneWindows64` |

### How work flows

```
feature branch  ->  DRAFT PR into integration/batch-N-candK  ->  candidate build 1.0.N  ->  owner plays it
     feat/bNN-...         (one batch of items, one compile)        (pre-release demo-v1.0.N)
                                                                          |
                                     main is brought up to date from a released candidate (owner's call)
```

- Builders work on `feat/...` or `fix/...` branches cut from the current candidate branch and open a **draft** PR into
  it. Nobody pushes to `main` or to an `integration/...` branch directly, and nobody force-pushes.
- A batch's items land on `integration/batch-N-candK`; GitHub compiles and tests it once; when it is green apart from
  the known reds below, it becomes a candidate with a version number (`1.0.N`) and a pre-release `demo-v1.0.N`.
- `main` lags behind the candidates and is updated only from a released candidate, by the owner.
- The batch rules (one compile per batch, what drops on a red, metas, gates) are in [loop/README.md](loop/README.md).
  Its section 3 still says "every PR goes straight to main": the candidate branches above replaced that.

### Run the tests on GitHub

Nothing needs Unity on your machine. From a terminal with the GitHub CLI:

```bash
# Unity EditMode + PlayMode tests (the per-batch set, Slow excluded) on your branch:
gh workflow run unity-ci.yml --ref <your-branch> -f jobs=tests
# The fast simulation tests (proves a change did not move the game rules or their checksums):
gh workflow run sim-tests.yml --ref <your-branch> -f suite=fast
# Then read the results:
gh run list --workflow unity-ci.yml --branch <your-branch> -L 1
gh run view <run-id>
```

`unity-ci` also builds players (`-f jobs=builds`, `build-windows`, `build-mac`). `crossplay` and `online-smoke` test
online play between Windows and Mac; [docs/windows-crossplay.md](docs/windows-crossplay.md) explains them. Both
`unity-ci` and `sim-tests` also run by themselves on pull requests into `main`.

### Known reds (expected today; anything else is new)

| Where | What | Why |
| --- | --- | --- |
| `unity-ci` PlayMode | `RenderTexture.Create failed` in `FighterVisibilityPlayTests` and `OwnerWalkHudPlayTests` | the GitHub runner has no graphics card (PlayMode runs `-nographics`); these tests need one |
| `unity-ci` EditMode | the job exits red on 5 inconclusive `PerfSmooth` tests while every other test passes | frame-time tests cannot measure on a shared runner, report inconclusive, and the runner counts that as a failure |

When you report a run, give its id and name each red; a red not in this table is yours until shown otherwise.

### Where things live

| What | Where |
| --- | --- |
| How the game is built (Sim, Runtime, Editor, data flow, camera, shader, art pipeline, dependencies) | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Decisions and why | [DECISIONS.md](DECISIONS.md) |
| The visual asset bible and how the game is measured against it | the bible itself is kept outside this repo; [PARITY.md](PARITY.md) is the method, the rows and the pass rule |
| Audits (blind-spot audits, batch orders, incident notes) | kept outside this repo with the batch orders; findings that need code arrive as items in `loop/items/` |
| Batch items, reviews and open findings | [loop/items/](loop/items), [loop/review/](loop/review) (`_OPEN.md` lists what is unresolved) |
| Controllers | [docs/CONTROLLERS.md](docs/CONTROLLERS.md) |
| Testing with people (first Windows test, first ten matches, minimum spec draft) | [docs/testing/](docs/testing) |
| Making a tester download | [tools/release/README.md](tools/release/README.md) |
| Mechanics work list for teammates | [MAX-ITEMS.md](MAX-ITEMS.md) |

## Unity version

Unity **6000.3.28f1** (Unity 6.3). The scripts look for it where Unity Hub installs it on a Mac; set `UNITY_PATH` to
point them at another editor binary.

## Local commands (only if you have Unity installed)

All three run Unity headless (`-batchmode -nographics`), write the editor log to `Logs/`, and exit with Unity's real
exit code. Batch builders do not run these: GitHub compiles and tests.

```bash
tools/unity-compile.sh    # open the project, compile every assembly, quit; fails on any compiler error
tools/unity-test.sh       # EditMode tests -> Logs/editmode-results.xml; fails if a test fails or zero tests ran
tools/unity-build-mac.sh  # CriticalMass.Editor.Build.MacOS -> Builds/macOS/CriticalMass.app
```

## Layout

| Folder | Purpose |
| --- | --- |
| `Assets/_Game/Sim` | `CriticalMass.Sim`: pure C# simulation, no UnityEngine (`noEngineReferences`) |
| `Assets/_Game/Sim.Tests` | `CriticalMass.Sim.Tests`: EditMode NUnit tests for Sim |
| `Assets/_Game/Runtime` | `CriticalMass.Runtime`: Unity-side game code (view, menus, HUD, input, online), references Sim |
| `Assets/_Game/Runtime.Tests`, `Runtime.PlayTests` | EditMode and PlayMode tests for the view |
| `Assets/_Game/Net` | online rooms and lockstep |
| `server/relay` | the relay server online rooms connect through |
| `docs/` | controllers, Windows and cross-play, testing with people |
| `loop/` | batch rules, items, gates and reviews |
| `tools/` | build, capture, release and CI helper scripts |
| `Assets/_Game/Editor` | `CriticalMass.Editor`: editor-only build and setup scripts |
| `Assets/_Game/Art`, `Audio`, `Scenes`, `Settings` | content, `Boot.unity`, URP assets |

## Git

Binary assets go through Git LFS (`git lfs install` once per machine). Unity YAML files are merged with
UnityYAMLMerge; register the driver once per clone:

```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver '"/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/Helpers/UnityYAMLMerge" merge -p %O %B %A %A'
git config merge.unityyamlmerge.recursive binary
```
