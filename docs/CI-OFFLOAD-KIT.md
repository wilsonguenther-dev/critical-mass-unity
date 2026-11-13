# CI offload kit: Unity builds and Blender jobs on GitHub's runners

Written 2026-11-13 (ci/offload-2). Reusable for any Unity project and any repo with scripted Blender work. The kit is
four workflow files, two shell scripts, one Python helper and one config file. Everything project-specific is in the
config file and in the two thin caller workflows.

## What runs where

| Job | Where | Why |
| --- | --- | --- |
| Unity EditMode, PlayMode (`-nographics`), player builds (Windows, macOS Mono, Linux) | GitHub, `ubuntu-latest` + GameCI Docker image | No GPU needed; parallel; free on public repos |
| Scripted Blender: modelling, rigging and deformation metrics, FBX/GLB exports, verification renders (Workbench, EEVEE on software GL, Cycles CPU) | GitHub, `ubuntu-latest` + official Blender Linux build | Same Blender version, no GPU needed |
| Play-tests of a built macOS player (smoke match, captures) | The second Mac (play-test box) over ssh | Needs a real Mac with a window server and a GPU |
| Image-to-3D generation, anything needing Apple GPU memory, PlayMode tests tagged `RequiresGpu` | The main Mac (through its heavy-job lock) | Needs the Apple GPU |
| Any Unity build when GitHub Actions is down | The main Mac, the old local gates | Fallback only |

## Files to copy into a new repo (under 15 minutes)

1. `.github/workflows/reusable-unity-build.yml` and `.github/workflows/reusable-blender.yml`: copy unchanged.
2. `.github/workflows/unity-build.yml` and `.github/workflows/blender.yml`: copy, then edit the project values (build
   method names, output folders, Blender version). Nothing else in them is project-specific.
3. `tools/ci/offload.sh`, `tools/ci/blender_plan.py`, `tools/install-build.sh`: copy unchanged.
4. `tools/ci/offload.conf`: set `CI_REPO`, the `.app` name, the process name, the install folder, the play-test host and
   the play-test command (any script that takes `--host H --app APP --mode M --out DIR`).
5. Add the Unity build method: a static editor method that builds the player into a fixed folder (example:
   `Assets/_Game/Editor/Build.cs`, `CriticalMass.Editor.Build.MacOS`). It may read `-buildCommit SHA -buildCount N`
   for a version stamp; the caller passes them.
6. Add the secrets (next section), commit to the default branch (a `workflow_dispatch` workflow can only be started
   once its file is on the default branch), then run `tools/ci/offload.sh unity-build --ref main --target mac`.

## Unity licence: the three secrets and how activation works

- `UNITY_LICENSE`: the full contents of the `.ulf` licence file (macOS:
  `/Library/Application Support/Unity/Unity_lic.ulf`, made by Unity Hub when you sign in with a Personal licence).
- `UNITY_EMAIL` and `UNITY_PASSWORD`: the Unity ID that owns the licence.
- GameCI (`game-ci/unity-builder@v4`, `unity-test-runner@v4`) activates inside the container on every job and returns
  nothing afterwards (return is for Pro seats only). It uses the `.ulf` file first and falls back to email plus password
  when the file is rejected ("Machine bindings don't match"), so set all three. Source: https://game.ci/docs/github/activation
- Set them with `gh secret set UNITY_LICENSE < Unity_lic.ulf` etc. from an interactive terminal (see the trap below).
- Unity removed manual `.alf` activation for Personal licences, so the Hub-made `.ulf` is the only file path.

## Cost: public versus private (fetched 2026-11-13)

From https://docs.github.com/en/billing/concepts/product-billing/github-actions (no last-updated date on the page):

- **Public repositories: standard GitHub-hosted runners are free** and do not use the included amount. Larger runners
  are billed even on public repos (the kit never uses them).
- **Private repositories** spend an included monthly dollar amount: Free $12, Pro $18, Team $18, Enterprise Cloud $300.
  Per-minute list prices: Linux 2-core $0.006, Linux 2-core arm64 $0.005, Windows 2-core $0.010, macOS $0.062.
  So a Windows minute costs about 1.7 Linux minutes and a macOS minute about 10.3. On Pro, $18 is about 3,000 Linux
  minutes, 1,800 Windows minutes or 290 macOS minutes a month.
- Artifact storage included: Free 500 MB, Pro 1 GB, Team 2 GB, Enterprise 50 GB (overage $0.25 per GB-month). Cache:
  10 GB per repository on every plan (overage $0.07 per GB-month); the oldest caches are evicted past that.
- **Git LFS bandwidth counts**: Actions downloads of LFS files use the owner's LFS bandwidth: 10 GiB a month on Free and
  Pro, 250 GiB on Team and Enterprise (https://docs.github.com/en/billing/concepts/product-billing/git-lfs). Without a
  payment method, going over disables LFS for the account until next month. The kit caches `.git/lfs/objects` keyed by
  the LFS object list, so a warm job downloads only new objects.

Runner sizes (https://docs.github.com/en/actions/reference/runners/github-hosted-runners): Linux and Windows x64 get
4 vCPU, 16 GB RAM, 14 GB SSD on public repos (2 vCPU, 8 GB on private); macOS arm64 (`macos-latest`) 3 vCPU, 7 GB;
macOS Intel 4 vCPU, 14 GB. No GPU on standard runners.

Concurrency (https://docs.github.com/en/actions/reference/limits): Free 20 jobs at once (5 macOS), Pro 40 (5 macOS),
Team 60 (5 macOS), Enterprise 500 (50 macOS). A job may run 6 hours; a workflow run 35 days. GitHub says these limits may
change.

## How the pieces fit

- The thin caller is always dispatched **from the default branch** with the branch to build as an input
  (`gh workflow run unity-build.yml --ref main -f ref=feature/x`). Two reasons: every branch is built by today's
  workflow, and caches are saved in the default branch's scope, which every branch can read (a cache saved by a branch
  run is visible only to that branch and its children).
- The helper finds its own run by a request id in the run name, then waits with one `gh run watch --interval 120`.
- Player artifacts are a `.tgz` inside the artifact zip. The helper unpacks it, clears quarantine, re-signs ad hoc when the
  signature is invalid (`codesign --force --deep -s -`), checks `codesign -v`, the architectures (`lipo -archs`) and the
  version stamp against the commit it asked for.
- Blender input bundles: `--inputs PATH` packs the files, uploads them as an asset on the DRAFT release `ci-inputs`
  (created on first use; drafts are listed only to users with push access:
  https://docs.github.com/en/rest/releases/releases), the job downloads it into `$CI_INPUTS`, and the helper deletes the
  asset afterwards. Release assets: under 2 GiB per file, up to 1,000 per release, no total or bandwidth limit
  (https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases). Never publish that release.
- Several Blender scripts in one process: `--plan '[{"script":"a.py"},{"open":"x.blend","script":"b.py","argv":["{out}"]}]'`.

## What becomes public on a public repo

Everything committed on any branch (including LFS files), every workflow log, and every artifact (players, renders,
exports) for its retention period (7 days in the kit): any signed-in GitHub user can download artifacts of a public
repository. Draft releases and secrets are not public. If the art or the build must stay private, keep the repo private
and budget the minutes above.

## Known traps (all hit on 2026-11-13)

1. **Zip artifacts drop executable bits.** `actions/upload-artifact` zips files without Unix modes, so a macOS `.app`
   arrives with a non-executable `Contents/MacOS/<exe>` and will not start. Tar the player in the job, untar after
   download (https://github.com/actions/upload-artifact, "Permission Loss").
2. **Coverage flag.** `game-ci/unity-test-runner@v4.4.0` turns `coverageEnabled: false` into `--no-coverageEnabled`,
   which the GameCI CLI (v0.1.74, strict parsing) rejects in 1 s with "Unknown argument: noCoverageEnabled". Leave the
   input unset.
3. **Empty secret from a non-TTY shell.** `gh secret set NAME` with no `--body` and no stdin redirect, run by an agent
   or script without a terminal, stores an EMPTY value and reports success; the job then fails activation. Always pipe
   the value in (`gh secret set NAME < file`) or run it in a real terminal, and check the job's "licence present" step.
4. **No-graphics runners fail RenderTexture tests.** Runners have no GPU; under `-nographics` anything that renders or
   reads back pixels (RenderTexture, ReadPixels, screenshots) fails or returns black. Tag those tests
   `[Category("RequiresGpu")]` and exclude the category on CI; run them on a Mac.
5. **WaitForEndOfFrame in batch mode.** A batch-mode Editor never reaches end of frame, so a PlayMode test that yields
   `WaitForEndOfFrame` hangs until the timeout. Yield `null` (next frame) or use a frame counter in tests.
6. **Unsigned arm64 player.** Unity cross-building StandaloneOSX on Linux cannot sign; Apple silicon refuses to run an
   unsigned arm64 binary. The helper signs ad hoc; for distribution you need a Developer ID and notarisation on a Mac.
7. **Git inside the GameCI container** can refuse the mounted checkout (owned by another user), so a build method that
   shells out to `git` for its version stamp gets nothing. Pass the commit and count as arguments (the caller does).
8. **Workflow files only dispatchable from the default branch.** A new `workflow_dispatch` file on a branch cannot be
   started by hand until it is merged.

## Extracting the kit into its own repository later

Move the two `reusable-*.yml` files into a repo such as `<owner>/ci-offload-kit` under `.github/workflows/`, tag it
(`v1`), and change the callers to `uses: <owner>/ci-offload-kit/.github/workflows/reusable-unity-build.yml@v1`
(secrets still `inherit`; a public kit repo can be used by private repos). Ship `offload.sh`, `blender_plan.py` and
`install-build.sh` from the same repo (a `curl` installer or a git submodule at `tools/ci/kit`). In the Blender workflow
the `{kit}` checkout then has to point at the kit repo (`repository: <owner>/ci-offload-kit`, `ref: v1`) instead of the
caller's commit. Keep project values in each project's `offload.conf` only.
