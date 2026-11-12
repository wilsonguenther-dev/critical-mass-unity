# Critical Mass (Unity): decisions log

Written 2026-11-12T03:08:23Z (date -u). Source: the architect's decision file
`Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/architect-decisions.md` (decided 2026-11-11 Central), with the
facts behind each "why" from the spec recon and harness post-mortem in the same folder.

Rules for this file:
- Ids are `U-###` and are allocated **only by the orchestrator** (U-011). Builders never add an entry; they report open
  questions with a recommendation. (The browser log let every builder append its own D-0xx entry and ended with D-067
  used five times, harness post-mortem R17.)
- `D-###` ids refer to the browser repo's `DECISIONS.md` ([wilsonguenther-dev/critical-mass](https://github.com/wilsonguenther-dev/critical-mass)).
- Status says whether the decision is built yet. "Decided" means no builder may change it.

---

## U-001 Engine and repo

**Decision.** Unity 6000.3.28f1, URP Forward+, linear colour. Repo `wilsonguenther-dev/critical-mass-unity`, separate
from the browser demo. The browser demo is frozen: no more items land there; it is the spec and the test oracle.

**Why.** D-052 (2026-11-07) already said the browser build is a demo only and production is Steam on a real engine,
Unity preferred. Max proposed moving to Unity "sooner rather than later" and packaging a local build for Travis. A
frozen browser demo gives the port a fixed target to match.

**Replaces.** D-001 (Next.js / React Three Fiber / Vercel stack), D-006 (repo, hosting, deploy discipline), D-008
(Vercel project).

**Status.** Decided. Built: the empty verified project (commit `81ba463`), with compile, test and Mac build gates.

## U-002 Rules are separate from Unity

**Decision.** `CriticalMass.Sim`: pure C#, no UnityEngine, 30 Hz fixed tick, seeded RNG, numbers in `double`. Ported
file by file from `src/sim` and `src/bots`, with each vitest file ported to NUnit as the oracle. Ticks, counts and
outcomes must match the browser tests exactly; positions within 1e-4 (the browser stored collision positions as 32-bit
floats). `CriticalMass.Runtime` is view and input only and never changes game state.

**Why.** The browser Sim already imports nothing nondeterministic (no `Math.random`, `Date.now`, `performance.now`,
`window` or `document` in any file), so it can be ported rule for rule and its tests reused as the oracle. Keeping
UnityEngine out lets the compiler enforce that.

**Replaces.** The TypeScript `src/sim` and `src/bots` (D-001 stack). Carries D-012 (collision tests the Float32 value it
stores) as the reason for the 1e-4 position tolerance.

**Status.** Decided. Built: the `CriticalMass.Sim` assembly with `noEngineReferences: true` and its EditMode test
assembly. The port itself is F-01 to F-06 and F-09.

## U-003 Ground

**Decision.** Grid of cells with heights, owned by the Sim. Runtime rebuilds chunk meshes. Bots path on the ported flow
field. No NavMesh, no Unity physics in rules.

**Why.** Travis's must-haves (miners digging and placing ground, CM-30; knock-down structures, CM-31) change the ground
itself, so the ground has to be game state, not scenery. The browser's flow field and grid collision already work and
have tests.

**Replaces.** Nothing is dropped: the browser's grid (`sim/grid.ts`, `sim/map.ts`) is ported and gains heights. D-059
(dig/place rules) and D-020 (hill ring layout) still apply.

**Status.** Decided. Not built (F-03, F-04, F-08).

## U-004 Match defaults

**Decision.** 16v16 (1 human + 31 bots) at the 2x pace Travis asked for is the default. 5v5 and 1x remain as options in
the pause menu and as command-line arguments. 8-minute match. Performance bar: 60 fps at 16v16 in the Mac build on
this Mac.

**Why.** Travis on Nov 4: "The times two felt like a much better pace ... especially times two with the 16 versus 16.
That was a lot of enjoyable chaos." D-067 item 6 made 16v16 the default in the browser (held, not in main).

**Replaces.** D-002 (5v5 default; already superseded by D-067(6)). The browser's `?size=5` and `?speed=1` URL options.
D-022 (laptop render budget and degrade ladder for WebKit) is replaced by the 60 fps bar.

**Status.** Decided. Not built (CM-66). The exact command-line argument names are not decided.

## U-005 Camera

**Decision.** Orthographic, yaw 45°, pitch 38°, 20 world units tall, never rotates. Damped-spring follow with 0.25 s
look-ahead capped at 2 units. Cinemachine 3. Free camera is out of the first playable.

**Why.** These are the browser's camera numbers (config `render`, D-045 spring and look-ahead). Bible p13: "fixed
elevated three-quarter angle with bounded follow and no normal-play rotation". The browser's free camera (CM-65) was
not discoverable: Wilson could not find it.

**Replaces.** D-067 item 3 (free-moving camera) and CM-65 for the first playable. D-045 still applies.

**Status.** Decided. Not built (F-08).

## U-006 Controls

**Decision.** Fixed table in `MAX-ITEMS.md` (WASD, Space jump, left mouse attack, right mouse ability, E carry/interact,
F dig/place, Tab scoreboard, Esc pause; gamepad mapped). No item adds or moves a binding.

**Why.** In the browser, builders picked keys: CM-32's builder put jump on F, CM-38's builder also used F for TNT, and
jump moved to Q at integration (post-mortem section 3, items 3 and 9). A fixed table removes that choice.

**Replaces.** D-062's jump key Q (the jump rules, landing guard and "bots never jump" still apply) and the browser's
Space for the class ability.

**Status.** Decided. Not built (F-07, `Assets/_Game/Settings/Controls.inputactions`).

## U-007 Class rules

**Decision.** D-067 stands: class and upgrade changes happen at base stations (own base pad, radius 3.75, E opens the
panel). Builder's class ability builds ladders and bridges. Carried pieces need empty hands and drop on death.
Demolitions TNT is on F / left trigger. Dig/place keeps the browser's one-key carry rule. CM-58 numbers (2 biomass, 600
ticks, 120 HP, 2 per Builder) are provisional and marked so in GameConfig. Heavy/light attack (CM-67) stays a team pick.

**Why.** D-067 item 5 recorded Travis's base stations (swap class and upgrade with biomass at your base). The Builder's
ability cannot both spend upgrades (the CM-39 branch) and build pieces (CM-58), and upgrades now live at the station.
Fixing the TNT key and the dig/place rule removes the two open control questions in `MAX-ITEMS.md`.

**Replaces.** D-053 (class switch on the death screen; already superseded by D-067(5)), CM-54 (its cooldown is kept).
The CM-39 branch's "hold the ability near an ally" spending.

**Status.** Decided. Not built (CM-30, CM-38, CM-39, CM-58, CM-59, CM-84). CM-67 waits on the team.

## U-008 Look

**Decision.** 3D models built to Jeisil's asset bible under ONE shared shader `CM/Toon`: flat bible-palette colours, a
two-band light ramp, an inverted-hull outline (width set per asset), a team-colour mask channel, and a rim light for
team read. No glossy or metallic response anywhere. Mechanics work uses team-coloured capsules and cubes and never
waits on art.

**Why.** The bible asks for "chunky, rounded, toy-like forms" with "clean cel shading and restrained highlights", and
"never communicate team, corruption, or objective state by color alone". The browser's stand-in bodies with procedural
gear scored 3 to 4 out of 10 on every parity row.

**Replaces.** D-048 (2 CSS px outlines) by a per-asset outline width. D-017 (per-state emissive gate) is moot. Carries
the idea of D-047 (team rim mixed toward the team colour). D-004 and D-064 (bible palette, fonts) still apply.

**Status.** Decided. Not built (Phase 2 starts with `CM/Toon`). The shader's property names and mask storage are not
decided (see ARCHITECTURE.md section 5).

## U-009 How characters get made

**Decision.** All three classes share the KayKit 41-joint rig so all 76 CC0 clips work on every class (the browser kept
32; Unity keeps all 76). Each character's source lives in the repo under `Assets/_Game/Art/Characters/_source/` (.blend
plus FBX export) with a build note. Warrior: Max's Blender model is the starting mesh IF his file passes audit
(rig-compatible, clean topology); it is then corrected to bible p06: round shield (the class identifier, never
removed), stone cleaver with glowing edge, dark-brown topknot with brass ring, grey stone slab armour with cyan cracks,
flat toon shading. A human-sculpted mesh corrected to the bible beats scripted geometry, which is what scored 3 to 4 in
the browser. Farmer, Miner and Kaiju follow the same route: sculpt in Blender to the bible page, never procedural
stand-ins. The headless-Blender script route (`scripts/sculpt/kaiju_adult.py`) is kept only for props and environment
kit pieces.

**Why.** Scripted and procedural geometry scored 3 to 4. The 44 dropped clips are still inside the KayKit source GLBs
(the browser's `stage.mjs` KEEP list dropped them). Max's screen recording shows a chibi warrior that matches the
bible's layout but has no shield, no cleaver, red spiky hair and chrome PBR armour, so it is a starting mesh, not a
finished one.

**Replaces.** D-009 (three stand-in bodies, 32 clips). The browser's procedural three.js kits (CM-44/45/46) and the
CM-69 sculpt-script route for the Kaiju.

**Status.** Decided. Waiting on Max's `.blend` (not received as of the recon; only a screen recording exists).

## U-010 Parity gate

**Decision.** For every bible row there is a fixed list of framings (the bible's own pose and angle). One editor
script, run by `tools/unity-capture.sh`, renders each framing from the actual Unity build content on a Void Navy
ground, plus an ID-mask render of the same framing so only the asset's own pixels are measured. Measured checks per
framing: (1) colour: each named bible swatch for that asset, sampled inside the mask, within 10 on CIEDE2000; (2) shape:
silhouette overlap with the bible crop (intersection over union) at least 0.85; (3) required features: a per-row
checklist from the bible page, each confirmed by an Opus reviewer looking at the capture, with a one-line reason.
Measured checks only move a row from "not ready" to "ready for the team". A row PASSES only when every framing is
scored 9 or 10 by the team (Wilson's rule, unchanged). The board shows bible crop and Unity capture side by side, with
the commit and capture time, and marks a row stale when main has moved since its capture.

**Why.** The browser's CM-40 measure measured the background, not the Warrior: every capture's subject box was the
whole image and its PASS lines were false (post-mortem R18). Wilson's rule of 2026-11-09: measured numbers "never make
a row pass on their own". D-052: "looks similar" is not acceptance. The browser board went stale at build `a048531`.

**Replaces.** D-054's subject mask (corner-mean heuristic) and D-066's background-plate mask by an ID-mask render; the
bounding-box aspect silhouette check (within 15 %) by IoU at least 0.85; the "on screen at least 2 %" check is not one
of the three. D-055 baselines are browser-only.

**Status.** Decided. Spec in [PARITY.md](PARITY.md). Not built (`tools/unity-capture.sh` does not exist yet).

## U-011 Build loop

**Decision.** Every item carries its method, files, numbers, keys and pass check, with no "or" choices. Builders write
code only; one compile per batch of about 6; a red item is dropped from the batch on the first red and the rest land.
PRs go straight to main, no stacked PRs, docs ride in the item's own PR. Spend cap counts all tokens (input, output,
cache); no idle or dispatch agents. Shared limits (frame time, draw calls, shader variants) are measured by the gate and
reported per batch; builders are never asked to raise one. Harness scripts live in `loop/` in the repo and are
committed. Gates, all on this Mac: compile; EditMode tests (the sim oracle); a PlayMode smoke test (boot, 16v16 bots,
60 simulated seconds, zero exceptions); Mac build; parity capture for art items. Decision ids are `U-###`, allocated
only by the orchestrator.

**Why.** The browser loop post-mortem: stacked PRs merged into an integration branch instead of main (R1); docs PRs
conflicted with an open batch and stopped a run (R2); one bounce per 29-minute verify round (R3); the spend governor
counted output tokens only, 6 to 17 times under real use (R11); 489 idle and dispatch agents burned 59 M tokens (R12);
most bounces sat on shared budgets a builder could not measure (R16); decision ids collided (R17); the harness was
never in git (R20); throughput was about 1 item an hour (R21).

**Replaces.** The browser harness (`workflow-cm.js`, `cm-loop.mjs`, untracked) and its integrator "fix or bounce, at
most 4 attempts" rule.

**Status.** Decided. Rules in [loop/README.md](loop/README.md). No scripts yet.

## U-012 Order of work

**Decision.** Phase 0, foundation (F-01 to F-09): config, tick, grid, pathing, base rules, bots, controls, placeholder
view, soak runner. Exit: a full 16v16 bot match plays in grey-box and every ported test passes. Phase 1, mechanics:
Max's items, Travis's four must-haves first (dig and place ground, knock-down structures, jumping, diagonal bridge
crossing). Phase 2, look: `CM/Toon`, then bible rows in this order: p05 colour and type, p06 Warrior, p07 Farmer, p08
Miner, p09 animation set, p10 and p11 Kaiju, p12 and p13 map, p14 props, p15 HUD, p04 logo and title, p03 overall.
Phase 3, finish: effects, audio (the 42 CC0 sounds), menus, Windows build for Travis. Phases 1 and 2 run side by side
once Phase 0 exits.

**Why.** Every mechanic and every art row stands on the foundation. Travis's must-haves are CM-30, CM-31, CM-32 and
CM-33. In the browser the mechanics/look ownership split was decided mid-run, after mechanics items had been coded and
re-landed (R22); here it is decided up front.

**Replaces.** The browser loop's single mixed item list (`ITEMS-cm-loop.json`, 85 items).

**Status.** Decided. Phase 0 not started.

## U-013 Builds for Travis

**Decision.** Standalone app, no server. Mac build works today. Windows module gets installed before the first handoff
(his platform is not stated anywhere; "PC first" is the only hint). Steam first, consoles later (D-052).

**Why.** Max: "There wouldn't be a server or anything because that all costs money." Travis named Steam as the easiest
first store. The meeting summary: 16v16 was "a preferred demo setting, not proof of working 32-player online
multiplayer".

**Replaces.** The Vercel deploy of the browser demo (production on every push to main), D-025 (packaging), D-026
(release 1.0.0), D-051 (WebGL failure card).

**Status.** Decided. Mac build works (`tools/unity-build-mac.sh`). Windows module not installed (`Build.Windows` exits 3).

---

## Rulings on the doc writer's open questions (2026-11-11 Central)

## U-014 Spend cap measure

**Decision.** The cap counts the workflow's own reported total tokens (the measure that read 39.4M on Monday for about
14 landed items, i.e. about 2.8M per item), NOT the old output-only counter. Wilson sets the budget number.

**Why.** U-011 said "all tokens" while the decision file sized the budget at 2.8M per item; only the workflow total
fits both. The output-only counter read 6 to 17 times low (R11).

**Replaces.** The browser governor's `budget.spent()` (output tokens only).

**Status.** Decided. Budget number waiting on Wilson.

## U-015 Seats

**Decision.** At most 6 agents at once = 5 builders + 1 integrator; reviewers and fixers take a builder slot.

**Why.** The 6-agent limit and "6 builders plus 1-2 review/land seats" in the browser START text could not both hold.

**Replaces.** The browser START text's six coder lanes plus review and land seats.

**Status.** Decided.

## U-016 Bible reference silhouettes

**Decision.** One mask per bible crop, made once, stored in the repo under `loop/parity-board/ref-masks/`, reviewed by
eye before first use, never computed at gate time.

**Why.** IoU needs a silhouette on both sides, and a guessed mask is the defect that made the browser measure the
background (R18).

**Replaces.** D-054 and D-066 computed masks for the bible side.

**Status.** Decided. Masks not made yet.

## U-017 Parity capture

**Decision.** Output to `Logs/parity/<commit>/<row>/<framing>.png` plus `<framing>.mask.png` and a `results.json`;
capture camera is orthographic and framed to match the bible crop for that framing; one fixed key light defined in a
dedicated capture scene (`Assets/_Game/Scenes/ParityCapture.unity`), same for every row.

**Why.** The capture script's interface, camera and light were not decided in U-010, and colour checks need the same
light every time.

**Replaces.** The browser's Playwright framings.

**Status.** Decided. Not built.

## U-018 Team pass

**Decision.** A row passes when every rater who scored every framing gave 9 or 10, with at least two raters, one of
them Wilson.

**Why.** U-010's "scored 9 or 10 by the team" could be read as every rater or as the average.

**Replaces.** The browser board's "Team avg" column as a pass measure.

**Status.** Decided.

## U-019 p13 spatial grammar

**Decision.** Measured by Sim timing tests (base to hill 12–18 s, shrine detour 8–12 s at default pace) plus team
review.

**Why.** Bible p13 is text only; there is no picture for colour or IoU checks.

**Replaces.** The browser's unbuilt grammar tests.

**Status.** Decided. Not built.

## U-020 HUD bindings

**Decision.** The HUD shows the game's real bindings, not the bible's placeholder LB/RB/Y labels.

**Why.** The bible's labels do not match the U-006 controls table.

**Replaces.** Nothing built.

**Status.** Decided.

## U-021 Missing swatches

**Decision.** Missing swatches (Kaiju slate, moss, belly; HUD meter): re-sample from the bible pages, mark as "sampled,
pending Jeisil's confirmation".

**Why.** The bible names these colours but prints no hex, and the recon gave none.

**Replaces.** Nothing.

**Status.** Decided. Not sampled yet.

## U-022 Blender and glTFast

**Decision.** Blender pinned at 5.2.3 for all art source. glTFast is build-time/import only, for bringing in the CC0
KayKit GLB sources and their clips; nothing loads glTF at runtime.

**Why.** The Runtime Dependencies table had no pinned Blender and no job for `com.unity.cloud.gltfast`.

**Replaces.** The browser's GLB staging (`stage.mjs`, meshopt) and runtime GLB loading.

**Status.** Decided. Blender 5.2.3 LTS is installed on this Mac.

## U-023 Rule details

**Decision.** A ladder is a one-step piece joining two cell heights; a carried piece drops on death if the cell is
valid, else it is destroyed; every class buys its own upgrades at the station; command-line arguments are
`-teamSize`, `-speed`, `-map`; map choice uses the same arguments-plus-pause-menu pattern; the unused "switch
dig/place" binding row stays bound but unread; shared browser files that mix base rules with item code are ported by
the loop with the item code left out and marked, and the item owner adds it back.

**Why.** These were the open questions left in `MAX-ITEMS.md` after U-004 to U-007.

**Replaces.** The browser's `?size=`, `?speed=` and `?map=` URL options; the CM-39 branch's ally-targeted upgrade.

**Status.** Decided. Applied in `MAX-ITEMS.md`.

## U-024 Phase exits

**Decision.** Phase 1 ends when Travis's four must-haves pass a play test; Phase 2 when every bible row passes; Phase 3
when Travis has a working build for his platform.

**Why.** U-012 gave an exit only for Phase 0.

**Replaces.** Nothing.

**Status.** Decided.

## U-025 Board scoring per row

**Decision.** Board scoring is switched off per row until that row has a Unity capture (applied with the first Unity
capture publish, not before).

**Why.** The board still shows browser-demo captures; scores given now would rate the browser build, not Unity.

**Replaces.** Open scoring on every row of the browser board.

**Status.** Decided. Applied with the first Unity capture publish.

## Rulings on the harness builder's open questions (2026-11-12)

## U-026 Coupled batches

**Decision.** Batches come from `loop/items/_ORDER.md` (section 3 gives each batch's items; a batch is coupled when two
or more of its items sit on one dependency-graph line marked "(coupled"). Phase 0 batches 2, 3 and 4 are coupled. In a
coupled batch every builder finishes (in waves of at most 5), then ONE compile. On a red the drop rule does not apply:
one Opus fix seat gets the exact compiler/test output and the red item's spec and repairs only files inside that item's
`files_allowed`; at most 2 fix rounds per batch, then the loop halts for the architect with the evidence. A coupled
batch lands whole or not at all. Uncoupled batches keep the drop-on-first-red rule; a dropped item also drops the
items in the same batch that wait on it (batch 1 is a chain), without counting against them.

**Why.** The ported modules reference each other in cycles (combat and kaiju, step and every system, fsm and
director; `_OPEN.md` O-21, O-22), so their items cannot compile one by one.

**Replaces.** U-011's "a red item is dropped from the batch on the first red" for coupled batches only.

**Status.** Decided. Built: `loop/bin/integrate.py --coupled`, the workflow's fix seat, `cmloop.py batches`.

## U-027 Hierarchy in code, no hand-authored Unity files

**Decision.** The game hierarchy is built in code when Boot loads (F-08a `GameBootstrap`, `RuntimeInitializeOnLoadMethod`
plus `SceneManager.sceneLoaded`); `Assets/_Game/Scenes/Boot.unity` stays the only build scene and stays empty. Builders
never author `.unity`, `.prefab`, `.asset`, `.mat` or `.meta` files by hand and never choose a GUID or fileID: Unity
generates the `.meta` of every new file and folder on the integrator's import, and the assets Runtime needs (the
project-wide input actions assignment, the placeholder material) are made by `CriticalMass.Editor.ProjectSetup.Apply`
(F-07, run by U-029). The one hand-written Unity data file is `Assets/_Game/Settings/Controls.inputactions`, plain JSON
in the shape F-07 gives. Sim-to-Unity axes go through `CriticalMass.Runtime.SimSpace` only (O-13).

**Why.** Builders do not run Unity (loop/README.md) and cannot write scene or asset YAML, GUIDs or importer fileIDs that
are known to be right; code and editor API calls are checked by the compile gate.

**Replaces.** MAX-ITEMS' `Assets/_Game/Scenes/Arena.unity` and the drafted fixed-GUID `EditorBuildSettings` entry
(`_OPEN.md` O-16, O-17).

**Status.** Decided. Applied in `loop/items/` (conventions section 9, F-07, F-08a). The integrator must commit the
`.meta` files Unity generates with the batch.

## U-028 Soak lane

**Decision.** NUnit tests in category "Slow" are excluded from the per-batch EditMode gate (`tools/unity-test.sh`
passes `-testCategory "!Slow"`; Unity Test Framework 1.6 splits the value on `;` and turns a leading `!` into a NOT
filter). A separate `soak` gate runs `tools/unity-soak.sh` once after each batch lands on main and before any handoff
build. A soak red writes a BLOCKING entry (top-priority fix item needed) to `loop/review/<batch>.md`, which blocks the
next batch. Until `tools/unity-soak.sh` exists the gate reports skipped.

**Why.** The 14 slow outcome tests (F-09) would add their run time to every batch's gate.

**Replaces.** Running every EditMode test in the per-batch gate.

**Status.** Decided. Built: the filter in `tools/unity-test.sh`, `loop/gates/soak`. `tools/unity-soak.sh` not written.

## U-029 Project setup step

**Decision.** Before the compile gate the integrator runs `CriticalMass.Editor.ProjectSetup.Apply` with
`-executeMethod` (it is idempotent), recorded as the `setup` gate's result line; a non-zero exit fails the batch. Item
F-07 extends that method to assign the project-wide input actions through the Input System editor API instead of a
hand-written file id.

**Why.** Project settings applied by code are the same on every machine and every integration tree.

**Replaces.** Nothing.

**Status.** Decided. Built: `loop/gates/setup`, first gate in `loop/bin/integrate.py`.

## U-030 Generated Unity files and the setup order

**Decision.** Order: CriticalMass.Editor.ProjectSetup.Apply runs on the integration branch after every item branch of
the batch is merged and before the compile gate, on every batch (it is idempotent); nothing runs Unity on that branch
before it. After the compile, the harness (not an item) commits the files Unity generated for the batch: every `*.meta`
under `Assets/` (added or removed by the import), and the files ProjectSetup.Apply changed under
`Assets/_Game/Settings/`, `Assets/_Game/Resources/` and `ProjectSettings/`. Any other file left dirty fails the batch
(gate `generated`, no item named). Then the `clean-reimport` gate runs a second import with ProjectSetup.Apply and
fails unless `git status` is clean, which proves nothing generated was missed. The `scope` gate fails an item whose
own branch carries a hand-written `.meta`, `.unity`, `.prefab`, `.asset` or `.mat` (U-027), even when a
`files_allowed` glob such as `Foo.cs*` would admit it; `Controls.inputactions` and its meta are the exception.
ProjectSettings changes not produced by ProjectSetup.Apply still need `architect_approved: U-###`. Item-supplied gate
tools (`tools/unity-playtest.sh` from F-08c, `tools/unity-soak.sh` from F-09) are run through `bash`, so the
`playmode-smoke` and `soak` gates switch from skipped to real as soon as the file is in the tree under test.

**Why.** U-027 leaves the metas to Unity; without a harness commit they would never reach main, and every later
import would leave the tree dirty.

**Replaces.** Nothing; it implements U-027's "the integrator must commit the `.meta` files Unity generates".

**Status.** Decided. Built: `loop/gates/clean-reimport`, `commit_generated` and the gate-order check in
`loop/bin/integrate.py`, the U-027 rule in the scope gate, `SETUP_ORDER` in `loop/bin/cmloop.py` and the workflow.

### Further harness rulings (2026-11-12, no new id)

- **Review blockers (ruling 6).** Review findings marked BLOCKING are written to `loop/review/<batch>.md`. The next
  batch does not start while an unresolved BLOCKING entry exists (`halt-check` exits 6, the launch checklist fails,
  the workflow stops dispatching). The architect resolves each one with `- RESOLVED <key>: item <ID>` or waives it with
  `- WAIVED <key>: U-###`.
- **Quiet hours (ruling 7).** Quiet hours (23:00 to 07:30 Chicago) hold digests only; a HALT always texts.
- **Method (ruling 1).** An item's method is every section except "Out of scope"; no `## Method` heading is required.
- **Spend estimate (ruling 2).** The transcript-based estimate is kept until the workflow writes its own total.
- **Unattributed red (ruling 3).** A red that names no item's file returns the whole uncoupled batch uncounted and
  flags the architect; the integrator may name a culprit once.
- **PlayMode smoke and budgets (ruling 4).** The smoke test lives in `Assets/_Game/Runtime.PlayTests`, run by
  `tools/unity-playtest.sh` (F-08c creates both); the gate reports skipped until it exists. `perf.json`, draw calls and
  shader variants are report-only with no limits until the architect sets them after the first real measurement.
- **Memory (ruling 5).** The memory watchdog stays report-only.

## Still open (the architect specifies them in the shader and art item specs)

`CM/Toon` property names and team-mask storage; FBX export settings; the Kaiju rig; the prop script location.

---

## Open, waiting on people (from the decision file)

- Max's Unity project and Blender file: audit, then decide whether his project or this scaffold is the base. The
  decisions above hold either way.
- Travis's platform: ask before the first handoff.
- Token budget for the loop: Wilson sets it; size it at roughly 2.8M total tokens per landed item (Monday's real
  figure) until the new loop proves cheaper. The measure is fixed by U-014.

---

## Browser-era decisions (from the spec recon, section 7)

### Still apply

| Id | What it says | Note |
| --- | --- | --- |
| D-052 (2026-11-07) | Browser = demo only; production = Steam on a real engine; every Travis idea is demo scope; bible parity is a gate | the earlier D-052 (2026-10-28, immutable model URLs) is moot |
| D-067 | Base stations; corruption also from biomass (CM-85); 2x and 16v16 default; maps B/C; linear upgrades; combat choice open | free camera (item 3) is out of the first playable (U-005) |
| D-003 | Hill time is the only win; two feed stats; hatch at 240 of 480 s | |
| D-004 | Bible palette, fonts, Skybreak Basin | |
| D-007 | Layout and spatial grammar | heading only in the recon |
| D-020 | Hill ring: thin 8-connected circle, 4 cardinal gaps, 4 diagonal breakable panels | |
| D-009 | Three bodies on one 41-joint rig | the 32-clip limit and stand-in bodies are replaced by U-009 |
| D-012 | Collision tests the Float32 value it stores | port oracle tolerance (U-002) |
| D-013, D-015, D-016, D-018, D-021 | Combat/damage/respawn; abilities, barricades, wall-break; egg/feed/hatch/behaviour; bots; CM-19 bot tuning | headings only in the recon; the ported tests carry them |
| D-043b | CM-21 acceleration and turn-limited steering | |
| D-045 | Camera spring and look-ahead | |
| D-047 | Team rim as a mix toward team colour | |
| D-048 | 2 px outlines | the recon lists it as still applying; U-008 sets outline width per asset instead |
| D-054, D-055, D-066 | Parity measurement method and baselines | U-010 replaces the mask and silhouette method |
| D-056 | Corruption rate 9 | |
| D-057 | Logo as vector paths | |
| D-058 | Kaiju lifecycle states; hatchling at 0.45 scale | |
| D-059 | Dig/place rules | |
| D-062 | Jump: landing guard, bots never jump | the key moves from Q to Space (U-006) |
| D-064 | Palette equal to the printed hex, font roles, STATE_CUES | |

Superseded inside the browser log: D-002 (5v5 default) by D-067(6); D-053 (class switch) by D-067(5).

### Moot for Unity

| Id | What it was |
| --- | --- |
| D-001 | Next.js / React Three Fiber / Vercel |
| D-006 | Repo, hosting, deploy discipline |
| D-008 | Vercel project |
| D-010, D-016 (view part) | three.js node names |
| D-017 | No per-state emissive gate |
| D-019 | Landing page |
| D-022 | Laptop render budget, degrade ladder, WebKit |
| D-025 | Packaging |
| D-026 | Release 1.0.0 |
| D-028, D-030, D-031, D-032, D-050 | e2e and stepped specs, bloom settle, release walk |
| D-051 | WebGL failure card |
| D-052 (2026-10-28) | Immutable content-addressed model URLs |
| D-044 (history only) | The "no audio files" history is web-specific; the Kenney SFX bank itself is CC0 and portable |

## Panel revisions 2026-11-12

Senior Panel plan-audit before the first batch (seats: CTO, senior Unity/C# engineer, QA and harness, pre-mortem, demo
PM; synthesis seat applied these). Every entry below was checked against the files or a command; nothing above this
section is rewritten. Where an entry conflicts with an earlier decision, this entry wins.

- **P-01 Builders run on Opus.** The workflow sent every `tightly_defined` item (all 26) to Sonnet; the aborted run
  wf_a743c82d-56d's builder transcripts show `claude-sonnet-5-5`. That broke the binding "Opus in every seat" and would
  have measured the wrong cost. Now `model: 'opus'` for every seat; sim-workflow asserts it. `tightly_defined` stays in
  the front-matter as information only.
- **P-02 Batch membership is checked and a run can be limited to named batches.** `_ORDER.md` row 2 read
  `F-05a .. F-05j` and the parser kept only the two endpoints, leaving F-05b..F-05i in no batch. Now the row lists all ten
  ids, the parser expands letter ranges anyway, preflight FAILs when a loop item sits in no batch or in two, and
  `launch.sh --batches 1` (`cmloop.py plan --batches 1`) writes only batch 1 into the args file, so the cost run cannot
  roll into batch 2. A test pins the real membership (5, 10, 7, 4).
- **P-03 Float32 storage is emulated, not dropped** (replaces conventions 6.1's "every `Math.fround(v)` becomes `v`" and
  O-01's "keep all 53 bits"). Storage stays `double[]`; every store into a field the browser holds in a `Float32Array`,
  and every `Math.fround`, goes through `JsMath.F32(v) = (double)(float)v`. Measured reason: a port of
  computeFlowField + sampleFlowDir on 20 random 112x72 grids picked a different direction in 539 of 144,221 cells
  (0.374%) with f64 storage, because ties that collapse in f32 survive as one-ulp differences. U-002's "outcomes exact,
  positions within 1e-4" now has a mechanism; the 1e-4 tolerance is left for the last-bit transcendental case only.
  Edited: _CONVENTIONS 6.1, F-02a (adds `F32`), F-02b, F-04, F-05a, F-05b, F-05d, F-05j, F-06a.
- **P-04 `JsMath.Round` is JS `Math.round`.** `Math.Floor(v + 0.5)` gives 1 for 0.49999999999999994 (JS gives 0) and is
  wrong for odd integers from 2^52 up. F-02a now specifies floor-then-compare and adds one test,
  `JsMathTests.RoundMatchesJsMathRoundAtTheEdgesAndF32MatchesMathFround` (values taken from node on this Mac). Batch 1's
  EditMode count becomes 41 (37 ported + 3 added + SimVersionTests); Phase 0 exit default count becomes 195.
- **P-05 The oracle is the pinned commit, never the working tree.** `~/critical-mass` is checked out at 9a29d71, 149
  commits behind 09e0904, with local edits, and its src/ differs in game.config.ts, types.ts, map.ts and world.ts; a
  builder in the aborted run `cat`-ed world.ts from it. Conventions section 1 and the builder prompt now say
  `git -C ~/critical-mass show 09e0904:<path>` only.
- **P-06 Same-batch upstream contract.** Batch 1 is a chain built in parallel from origin/main, so downstream builders
  cannot see upstream code. The builder prompt and conventions section 1 now say: read each not-yet-landed `waits_on`
  item's spec and conventions section 11, call those signatures verbatim, never stub or guess, never read another lane;
  a missing upstream type is expected and is not an open question.
- **P-07 `tests_required` is enforced.** After a green EditMode gate, integrate.py checks that every `tests_required`
  name of each item is declared in a .cs file the item changed and shows `Passed` in the results file (not run is
  allowed only for `Explicit` or `Category("Slow")`; `[Ignore]` never). A shortfall is a counted red of that item at gate
  `tests`. `pass_check` stays as prose for humans; this check is what enforces it.
- **P-08 Art sources live where Unity does not import them.** `.blend` files go in `Assets/_Game/Art/*/_source~/`
  (Unity skips `~` folders, so no machine needs Blender to import the project and nothing imports twice); the FBX
  export sits one level up. `*.blend1`/`*.blend2` are ignored; `.mat`, `.anim`, `.controller` and the other Unity YAML
  types get `merge=unityyamlmerge`. Supersedes the path in U-009 (the rest of U-009 stands).
- **P-09 The follow camera is really orthographic.** Cinemachine 3.1.7 copies `Lens.ModeOverride` to the Unity camera
  only when `CinemachineBrain.LensModeOverride.Enabled` (CinemachineBrain.cs 755-773; default disabled, Perspective).
  F-08a now sets `camera.orthographic = true` and enables the brain override; F-08b casts the double view height to float.
- **P-10 The spend fallback errs high.** The transcript estimate read 0.886 of the reported total on the aborted run (it
  was documented as 1.2x). spend.py now divides the raw estimate by 0.85 before the thresholds and keeps `raw`.
- **P-11 Small corrections.** Batch 3's count is +31 (29 ported, 2 added), not +30. The parity gate's skip now needs exit
  78 like every other gate.

### Deferred by the panel

| What | Destination |
| --- | --- |
| The coupled flag is per batch, so F-07 (batch 3) and F-09 (batch 4) halt with their coupled siblings although `_ORDER.md` says they drop alone. Split the rows or carry per-item coupled sets. | Architect, before batch 3 |
| `public static class Bots` in namespace `CriticalMass.Sim.Bots` resolves to the namespace from `CriticalMass.Sim.*` code (CS0234), and Sim `Resources` collides with `UnityEngine.Resources` in Runtime files (CS0104). Rename to `BotBrain` and `SimResources` across conventions section 3, F-05g, F-05i, F-06a, F-06c, F-06d, F-06e, F-07, F-08a and MAX-ITEMS. | Architect, before batch 2 |
| Port V8's fdlibm `sin/cos/atan2/exp` into JsMath and ban the System.Math versions in the Sim (BoundaryTests); map.ts compares atan2 angles with `<=`. | Architect item before batch 2; sooner if a batch-1 map test goes red on an angle |
| A Unity-free `dotnet test` job for the Sim on ubuntu and windows (no licence needed; doubles as the cross-platform determinism check). | Phase 1, architect |
| A check that Runtime code never assigns to Sim state (scope rule or read-only facade). | Before batch 4 |
| Player-build (IL2CPP) determinism: per-tick state hash against EditMode. | Phase 1 |
| Steamworks wrapper, save and settings storage, Mac signing and notarisation stubs. | Decision stubs when Phase 1 starts; build in Phase 3 |
| F-01's "every tunable matches" test compares the spec to itself; commit a golden file generated from game.config.ts at 09e0904. | Architect, before batch 2 (F-01 lands as specified) |
| Per-seat-kind token totals and rounds/compiles per batch in the batch report, so batch 1's cost can be extrapolated per seat. | Harness, right after the batch-1 run |
| build-mac runs on every batch although `_ORDER.md` lists it only after batch 4. Accepted for batch 1 (it is part of the real cost); revisit after. | After batch 1 |
| A returned item's lane is cut from `origin/loop/<ID>`, which lacks upstream code that landed since; merge origin/main in. A consumer red naming an upstream type should not count against the consumer. | Architect, harness, before batch 2 |
| F-09's 14 outcome bands as a Phase 0 exit gate, CM-30 waiting on all of F-09, and the must-haves waiting on all 26 items (split into Sim and view parts). | Owner and architect, before batch 4 |
| Max's prototype against this scaffold (which base the view layer builds on). | Owner, before batch 3 |
| A named owner (Wilson) for the F-08a human play check, and one PlayMode test that drives human input. | Architect, before batch 4 |
| A CM/Toon shader item in parallel with Phase 0 (no Sim dependency). | Architect, slot into batch 4 |
| Windows build module and a build-windows gate. | After Travis's platform is known; batch 4 |
| Stale ownership parentheticals in `_ORDER.md` section 1 and MAX-ITEMS line 149. | Architect doc pass |
| SpatialHash infinity clamp (batch-1 review R20261112T1705-1-2): `CellOf` / `QueryCircle` clamp `FloorToInt`'s `int.MinValue` to cell 0 where the browser clamps +Infinity to the last cell; no current caller passes such values. | Architect, an F-04 fix item before Phase 1 bot work (deferred by U-047) |
| Mutable GameConfig held by World (review R20261112T1705-1-4): `CreateWorld` keeps the caller's instance; a Runtime write would change rules mid-match. Decide `opts.Config.Clone()` against a Runtime write ban. | Architect, before batch 4 (with the Runtime-never-writes check above) |
| IL2CPP floating-point contraction can break the fdlibm port's bit-exactness in player builds. | Phase 1 per-tick state hash (above) |

## Architect rulings for batches 2 to 5 (2026-11-12)

Applied on branch `architect/batch2-prep` while batch 1 ran and after it landed (PR 1, 579a53f). Each entry was checked
against the files or a command; nothing above this section is rewritten. Where an entry conflicts with an earlier
decision, this entry wins.

## U-031 Sim class names that never collide

**Decision.** The Sim class of `bots/index.ts` is `BotBrain` (file `Assets/_Game/Sim/Bots/BotBrain.cs`) and the Sim class
of `resources.ts` is `SimResources` (file `Assets/_Game/Sim/SimResources.cs`). The namespaces and the `GameConfig`
sections `Bots` and `Resources` (config properties, F-01) keep their names.
**Why.** `public static class Bots` inside namespace `CriticalMass.Sim.Bots` is shadowed by the namespace from other
`CriticalMass.Sim.*` code (CS0234); `Resources` is ambiguous with `UnityEngine.Resources` in Runtime files (CS0104).
**Replaces.** Conventions section 3's `index.ts -> Bots` and `Resources`. Applied in `_CONVENTIONS.md`, F-05g, F-05i,
F-06c, F-06e, `_ORDER.md`, MAX-ITEMS (F-07 and F-08a now write `UnityEngine.Resources.Load`).
**Status.** Decided. A grep for the old class names in `loop/items`, MAX-ITEMS, ARCHITECTURE and `loop/README.md` finds none.

## U-032 Transcendentals are fdlibm, bit-exact with node

**Decision.** Item F-02c (front of batch 2, coupled) ports V8's fdlibm `sin`, `cos`, `atan2`, `exp` into `JsMath`
(V8 tag 13.6.233.10, the V8 of node v24.11.1), checked bit for bit against `tools/golden/jsmath-fdlibm.json` (8,186
vectors, generator `tools/golden/gen-jsmath.mjs`), and `BoundaryTests` bans `System.Math`'s versions in the Sim. The four
batch-1 call sites (SkybreakBasinMap.cs:175, World.cs:120, 121, 307 on 579a53f) are converted by F-02c; every F-05
item waits on it.
**Why.** node builds V8 without `V8_USE_LIBM_TRIG_FUNCTIONS` (checked in node's `tools/v8_gypfiles` on 2026-11-12), so
the oracle's four functions are fdlibm; `System.Math` uses the platform libm. V8 main has since moved to LLVM libc, so
the port is pinned to the oracle's node.
**Replaces.** Conventions 6.15 ("System.Math equivalents; 1e-4 covers it").
**Status.** Decided. Item written; golden file committed.

## U-033 Coupling is per item

**Decision.** Each `_ORDER.md` batch row carries a coupled set; `cmloop.py` parses it (`coupled_set`, waves),
`integrate.py --coupled-set` drops an uncoupled item alone and sends a coupled red to the fix seat, and the workflow
retries dropped uncoupled items after the set lands. A coupled item never waits on an uncoupled item of its batch
(preflight). An uncoupled item that ends parked no longer stops the next batch unless that batch waits on it.
**Why.** F-07 and F-09 (and now F-01b, V-01) would have halted with their coupled siblings although `_ORDER.md` said
they drop alone (panel deferred list).
**Replaces.** U-026's per-batch coupled flag.
**Status.** Built and tested (`loop/tests/test_batch2_prep.py`, `sim-workflow.mjs` mixed-batch scenarios).

## U-034 Balance bands are a report

**Decision.** F-09's pass check is the headless match runner and `OutcomeRunnerTests.SameSeedTwiceGivesTheSameOutcomeInEveryField`.
The 14 outcome bands run in the soak lane, which REPORTS: a red writes an ADVISORY entry for the architect and a flag,
never a BLOCKING entry, never a halt. CM-30 no longer waits on F-09. The Phase 0 exit no longer includes the 14 bands.
**Replaces.** U-028's "a soak red writes a BLOCKING entry"; the U-012 exit's soak clause; CM-30's wait on F-09.
**Status.** Built (`run_soak`), tested; F-09 and MAX-ITEMS edited.

## U-035 Travis's must-haves split into rules and view parts

**Decision.** CM-30, CM-31, CM-32 and CM-33 each have a rules part that waits on batch 2 and a view part that waits on
the placeholder views (MAX-ITEMS section 2 and each item; `_ORDER.md` section 6). Checked against the oracle's imports:
jump.ts, topple.ts, bridges.test.ts and 7 of the 11 terraform cases need batch 2 only; 4 terraform cases, the terraform
soak case and the moved human-jump case call the bots, so they wait on batch 3. CM-31's rules part also waits on CM-30's
(route guard). All four stay Max's; the loop owns none of them, so no loop item specs were written.
**Status.** Decided; applied in MAX-ITEMS and `_ORDER.md`.

## U-036 Tunables against a golden file

**Decision.** `tools/golden/game-config-09e0904.json` (171 leaves of `GAME_CONFIG` at 09e0904, generator
`tools/golden/gen-game-config.mjs`) is the oracle for every tunable. Batch 1 had landed, so F-01 is not edited; the
follow-up item F-01b (front of batch 2, uncoupled) compares `CreateBrowser5v5()` to it, and also carries the batch-1
review's shared-scratch proof (U-047).
**Status.** Item written; golden file and generator committed.

## U-037 Builder limit is a launch argument, ceiling 12

**Decision.** The owner raised the machine-wide agent limit for this loop to 12. `launch.sh --builders N` (1..12, default 5)
passes through `cmloop.py plan --builders` into the workflow's `maxBuilders` (clamped to 12). Batches run their
independent items in one wave when the limit allows: batch 2's 12 items run in one wave of 12. The integrator, fix
seat and reviewer run alone after the builders, so the peak agent count equals the largest wave: batch 2 = 12, batch 3
= 7, batch 4 = 5, batch 5 = 4. One compile per batch. Integrations take at most 12 items.
**Replaces.** U-015's 6 agents (5 builders + 1 integrator); the hard clamp to 5.
**Status.** Built and tested.

## U-038 Returned items see landed upstream code

**Decision.** `cmloop.py lanes create <ID> --base origin/loop/<ID>` merges origin/main into the lane before the fixer
starts (a conflict fails the lane with the files named). In an uncoupled drop, a compile red whose every line is a
missing-name error (CS0246, CS0234, CS0103, CS0117) naming a type declared in the spec of an item the red item waits on
is recorded at gate `upstream` and does not count against its return limit.
**Status.** Built and tested.

## U-039 One review per landed batch

**Decision.** One Opus reviewer per landed batch, as the workflow already does. Confirmed: the workflow has no second
review stage; `loop/README.md`'s "one review pass (two Opus lenses)" line and the Haiku "lander and mechanical seats"
line described seats the workflow never had and are rewritten.
**Status.** Confirmed; docs corrected.

## U-040 Cost report per batch

**Decision.** At the end of each landed batch the reviewer runs `spend.py batch-report`, writing
`loop/logs/batch-report-<run>-<N>.{json,md}` (tokens per seat kind from the agent transcripts, integrator runs, compile
rounds from `loop/logs/integrations.jsonl`, fix rounds) and one live-log sentence; shutdown writes any missing report.
**First measured data point (batch 1, run wf_403d1691-a59, the workflow runtime's report):** 7 agents, 808,397
subagent tokens, 162 tool uses, 17.6 minutes wall clock, 5 items landed: about 162k tokens per landed item.
**Status.** Built and tested; the baseline is in `spend.py` (`BASELINE_BATCH_1`) and every report.

## U-041 CM/Toon shader item, human-input PlayMode test, the human play check

**Decision.** Item V-01 (no Sim dependency) builds `CM/Toon` in batch 4's wave and lands with batch 4 as an uncoupled
item; it fixes ARCHITECTURE section 5's OPEN contract (HLSL, vertex-colour red team mask, Void Navy outline, threshold
0, shade 0.62, rim 0.25 / 0.6). F-08c adds `HumanInputPlayTests` (two PlayMode tests that drive F-08a's
`HumanInputSource` through a virtual keyboard), and `integrate.py` now checks PlayMode `tests_required` against the
PlayMode results (before, F-08c's smoke test would have been a counted red: it never runs in EditMode). Wilson runs the
F-08a human play check.
**Status.** Items written; scoped check built and tested.

## U-042 Every piece of work leaves one checkable record

**Decision.** `loop/logs/ledger.jsonl` (git-ignored, never wiped) gets one row per item state change (dropped, parked,
landed) written once per run, item, state and batch, with one live-log sentence each; at shutdown every loop item the
run was given gets exactly one final row: landed (with its `loop-landed:` commit), dropped (with its failure file) or
parked (with the reason, including "not reached in run ...: <stop reason>"). `cmloop.py ledger audit` checks the
record. Already covered before this ruling: `loop-landed:` lines proven on origin/main, failure files per drop, parked
files, the resume note, the live log of drops, parks and batch landings, `launch-ids.txt`.
**Status.** Built and tested.

## U-043 All art is made by us in Blender, by script

**Decision.** Every model (Warrior, Farmer, Miner and the planned classes, the Kaiju, props, environment) is built by
the loop in Blender by a committed script; no art waits on Max. The loop owns every bible row's model. The Warrior is
on branch `art/warrior-blender` (another agent); items A-02 Farmer, A-03 Miner, A-04 Kaiju and A-05 environment kit
(batch 5) follow its rig, palette and layout and launch only after it is on main.
**Replaces.** U-009's "sculpted by a person" and "Max's Blender model is the starting mesh"; MAX-ITEMS section 5's
request is now a reference only.
**Status.** Decided; items written; ARCHITECTURE and MAX-ITEMS edited.

## U-044 Nothing goes to Travis until the demo is done

**Decision.** No build is sent to Travis until the demo is completely done (Phase 3 exit). Internal grey-box builds for
the team's own play checks are fine. U-013's "before the first handoff" and U-024's Phase 1 play test mean the
team's own play, not a drop to Travis; any earlier grey-box drop to Travis is rejected.
**Status.** Decided.

## U-045 Max keeps his items

**Decision.** Max keeps the items MAX-ITEMS.md gives him. The loop never touches a Max-owned item unless Max leaves it
undone and the owner says to take it. Nothing is reassigned now.
**Status.** Decided; MAX-ITEMS and `loop/README.md` state it.

## U-046 All code building is Opus

**Decision.** No builder, fixer or integrator seat ever runs on Sonnet or Haiku, whatever `tightly_defined` says (it is
information only). The workflow routes every seat through `OPUS` and `seat()` halts on any other model; sim-workflow
asserts every call is Opus and that the script names no other model.
**Status.** Confirmed and tested.

## U-047 Batch-1 review follow-up

**Decision.** From `loop/review/20261112T1705-1.md` (six ADVISORY, no BLOCKING): the boundary guard learns unqualified
`new Random(`, `Random.Shared`, `DateTimeOffset`, `DateTime.Today`, `GetHashCode(` and raw casts between team enums,
and `SimEnumMap` maps `TeamId` to `MatchResult`, `HillState`, `KaijuState` (F-02c, because those guards must hold over
the F-05 code). No ported test or serialised value depends on those enums' integers (the browser has strings there), so
the values stay; `Cell` and `ClassId`, whose numbers do matter, already match. The process-wide static scratch stays and
F-01b proves two worlds interleaved give the same results as each alone; the Sim is stepped from exactly one thread.
The SpatialHash infinity clamp and the mutable GameConfig reference go on the deferred list. The golden-fixture
suggestion (R-3) is covered for the transcendentals by F-02c and for tunables by F-01b; per-cell map checksums stay a
recommendation.
**Status.** Items written.

## U-048 A refused text never kills a landed batch

**Decision.** After a merge, a refused or failed notification is logged (`events.jsonl`) and flagged; `integrate.py`
still prints its JSON line and runs the soak gate. The card accepts ids that are part of an item's own title
(F-03's "with the CM-31 decoration data"). Found by batch 1's real run (2026-11-12T17:15:05Z).
**Status.** Built and tested.

## Architect rulings after batch 2's halt (2026-11-12)

Batch 2's coupled set halted on F-02c (run wf_a1267a8f-56b, `loop/logs/coupled-20261112T1739-1-round1.md`): 24
last-bit mismatches against `tools/golden/jsmath-fdlibm.json`. Nothing above this section is rewritten.

## U-049 The fdlibm oracle is node v24.11.1 arm64, FMA-contracted

**Decision.** The oracle for `JsMath.Sin`, `Cos`, `Atan2`, `Exp` is "node v24.11.1 arm64, FMA-contracted fdlibm":
every ported browser expectation was produced by that binary on this machine, so the C# port reproduces it bit for
bit. Node compiles V8's `ieee754.cc` with clang's default `-ffp-contract=on`, which turns every in-expression
`a*b + c` into one `fmadd` / `fmsub` / `fnmsub`. F-02c's spec is amended: a fused multiply-add is REQUIRED at exactly
the 49 sites node's binary fuses (tagged `FMA-1` .. `FMA-49` in `Fdlibm.cs`, `tools/golden/fdlibm_replay.py` and the
table in `loop/items/F-02c.md`) and forbidden everywhere else in the Sim. `System.Math.FusedMultiplyAdd` does not
exist at this project's API level (.NET Standard 2.1: Unity's `NetStandard/ref/2.1.0/netstandard.dll` and Mono's
`unityjit` mscorlib have no such member; compiling a call gives CS0117), so the fused sites call `Fdlibm.Fma`, a
correctly rounded software fma (musl's integer algorithm, `src/math/fma.c`, with a zero `z` returning `x * y` and the
min-normal case returning `+-DBL_MIN` directly): only 64-bit integer arithmetic, the exact `long` to `double`
conversion and `Scalbn`, so results are identical on every player CPU. The boundary ban on System.Math stays;
`BoundaryTests` now also bans `FusedMultiplyAdd` in the Sim and `Fma(` outside `Core/Fdlibm.cs`. F-02c's pass check
uses the bounded grep `(^|[^A-Za-z0-9_])Math\.(Sin|Cos|Atan2|Exp)\(` (its question 1: the unbounded one matches `JsMath.`).
**Derivation.** `otool -arch arm64 -tV "$(which node)"` over `v8::base::ieee754::{sin, cos, atan, atan2, exp}` and
`(anonymous)::__ieee754_rem_pio2` (which inlines `__kernel_rem_pio2`; `__kernel_sin` and `__kernel_cos` are inlined
into sin and cos), each `fmadd` / `fmsub` / `fnmsub` read against the C source order. Fused instruction counts: sin 38,
cos 40, rem_pio2 18 (kernel loops unrolled), atan 13, atan2 1, exp 7; every one maps to a site of the list, and the
list predicts exactly those counts (clang's rule: within one expression the left product fuses first, so in
`z*r - x*y` it is `z*r`; `half*y - v*r` fuses `half*y`). The vectorised `fmul.2d` path of `__kernel_rem_pio2`'s
inner loop needs 8 or more terms and the loop has at most 3 (nx <= 3), so only its fused scalar path runs.
**Fused sites.** RemPio2: FMA-1 `t*invpio2 + half`, FMA-2 `t - fn*pio2_1`, FMA-3 `fn*pio2_2t - ((t-r)-w)`, FMA-4
`fn*pio2_3t - ((t-r)-w)`. KernelRemPio2: FMA-5 and FMA-8 `fw += x[j]*f[jx+i-j]`, FMA-6 and FMA-9 `z - two24*fw`, FMA-7
`z -= 8.0*floor(z*0.125)`, FMA-10 `fw += PIo2[k]*q[i+k]`. KernelCos: FMA-11..15 the inner Horner adds of `r` (the outer
`z * (...)` is a plain product), FMA-16 `0.5*z - (...)`, FMA-17 and FMA-19 `z*r - x*y` (product `z*r`), FMA-18
`0.5*z - qx`. KernelSin: FMA-20..23 the Horner adds of `r`, FMA-24 `x + v*(...)`, FMA-25 `S1 + z*r`, FMA-26
`(...) - v*S1`, FMA-27 `z*(...) - y`, FMA-28 `half*y - v*r` (product `half*y`). Atan: FMA-29 `2.0*x - one`, FMA-30
`one + 1.5*x`, FMA-31..35 the Horner adds of `s1`, FMA-36..39 those of `s2`, FMA-40 `x - x*(s1+s2)`, FMA-41
`x*(s1+s2) - atanlo[id]`. Atan2: FMA-42 `pi_o_2 + 0.5*pi_lo` (V8's `pi_lo` is volatile). Exp: FMA-43
`invln2*x + halF[xsb]`, FMA-44 `x - t*ln2HI[0]`, FMA-45 `x - t*(...)`, FMA-46..49 the Horner adds of `P1..P5`.
**Proof (2026-11-12).** (a) `python3 tools/golden/fdlibm_replay.py` (math.fma at exactly those sites, the C#
expression order): fused 0 mismatches of 8,186 golden vectors for all four functions; plain order 12 sin, 8 cos, 2
exp, 2 atan2, exactly the Unity run's 24. (b) `tools/golden/gen-jsmath-large.mjs` (seed 0x5eed2026; tiny and
subnormal, the kernel range, near multiples of pi/2, every npio2_hw word, medium and big reduction paths, huge, NaN
payloads and infinities, exp overflow, underflow and subnormal-result edges, every atan band, atan2 |y/x| near 2^60
and 2^-60, x == 1): 271,649 sin and cos, 250,037 exp, 210,144 atan2 inputs, 1,003,479 vectors; `--full` (44 MB, not
committed) replayed fused: 0 mismatches (plain: 1,922). Committed form: `tools/golden/jsmath-fdlibm-large.json`
(seed, counts, SHA-256 of inputs and outputs). (c) `bash tools/golden/fdlibm-proof/run.sh <lane>`: the lane's
`Fdlibm.cs`, `JsMath.cs`, `JsMathFdlibmTests.cs` and `BoundaryTests.cs` compile with the editor's own Roslyn against
Unity's netstandard 2.1 reference (exit 0) and every test of both fixtures passes under Unity's Mono and Unity's
CoreCLR 6.0.21, including the new `LargeSeededSetMatchesNodeHashes` (the C# generator reproduces node's input hashes,
then the output hashes); the software fma equals Python's `math.fma` on 1,304,096 triples (random bits, exact and
near cancellation, integer ties, 1 + 2^-53 ties, subnormal and min-normal results, overflow, signed-zero products,
every special-value combination): 0 mismatches. A mutant with FMA-24 unfused fails both tests (3 sin vectors, the
large hash). Not yet run: the Unity EditMode gate itself (the integrator runs it on relaunch).
**Replaces.** F-02c's "never ... use `Math.FusedMultiplyAdd`" line and its unbounded grep. U-032 otherwise stands.
**Status.** Decided; built on `loop/F-02c` (77bb50e); spec, golden set, replay and proof script on main. A different
node, an x86_64 node (no contraction without `-mfma`) and IL2CPP contraction are out of scope (panel deferred list).

## U-050 A relaunch reuses pushed lanes

**Decision.** A fresh launch no longer rebuilds an item whose lane is already pushed. `cmloop.py plan` marks an item
`prebuilt` (its head sha) when `origin/loop/<ID>` exists, carries commits beyond origin/main, the item was never
returned (`returns.json` 0; a returned item's builder must read its failure file, U-038) and its head descends from
origin/main or merges with it cleanly (`git merge-tree --write-tree`, nothing written). The workflow puts every
prebuilt item in `reusable`, so `buildAll` gives it no builder and the integrator merges `origin/loop/<ID>` as
before; a counted drop takes it out of `reusable` and it rebuilds as a returned item on its own branch.
`record-launch` writes one `reused` ledger row per prebuilt item of the run (once each, U-042) and one live-log
sentence; `launch.sh --rebuild` (`plan --rebuild`) turns reuse off. **Found by:** reading the workflow before this
ruling: `reusable` started empty, so a fresh launch sent every batch-2 item to a builder, whose `lanes create`
(`worktree add -B` from origin/main) restarted the branch from scratch and whose push could never fast-forward.
**Effect on the batch-2 relaunch:** F-02c and F-05a .. F-05j are reused (11 lanes); F-01b (dropped once at setup:
CS0104 `Match` ambiguous with F-05i's `CriticalMass.Sim.Core.Match`) gets one returned builder on `origin/loop/F-01b`
that fixes exactly that, then joins the same integration.
**Status.** Built and tested (`test_batch2_prep.py`: prebuilt_lane on a real throwaway repo, plan, ledger rows;
`sim-workflow.mjs`: two relaunch scenarios).

## U-051 The demo ships as a Windows build

**Decision.** Travis plays on a Windows PC, so the demo ships as a Windows 64-bit build; the Mac build is for our own
testing. Gate `build-windows` (`loop/gates/build-windows`, `tools/unity-build-windows.sh` ->
`CriticalMass.Editor.Build.Windows` -> `Builds/Windows/CriticalMass.exe`) has build-mac's shape: one JSON line, the
real exit code, under the compile lock, and skipped = 78 when the editor has no
`PlaybackEngines/WindowsStandaloneSupport` (installed in 6000.3.28f1 on 2026-11-12). `integrate.py` runs it on the new
main after a landed batch, after the soak lane, once every item of _ORDER.md batch 4's coupled set (F-08a, F-08b,
F-08c) is on main, and after every landed batch from then on. A red writes a BLOCKING review entry (the batch has
landed; the next batch waits for the architect); skipped is recorded as skipped, never as a pass.
**Status.** Built and tested (`test_batch2_prep.py`: real 78 skip through the gate script, due rule against the real
_ORDER.md, red = BLOCKING, skipped recorded, not due = not run). Not yet run against a real Unity build.
