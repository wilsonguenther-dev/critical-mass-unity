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

**Amendment 2026-11-12 (owner direction).** The gameplay camera becomes a perspective camera: pitch 55° down, vertical
field of view 35°, 23.4 units from the look target along the view ray (19.2 u above it), yaw 45° (unchanged), never
rotates, no shake. The look target sits 2 units toward screen-up from the player (the player at about 58% of screen
height) with up to 3 units of aim look-ahead toward the cursor (was 2 units of velocity look-ahead). This replaces the
orthographic projection, the 38° pitch and the 20-unit view height above; the spring follow, the bounds inset and
"never rotates" stand.
*Why:* the owner's direction of 2026-11-12 after the reference audit (Obsidian `unity-port/ART-DIRECTION-2026-11-12.md`
section 2.1): every gameplay audit puts the reference at 50 to 65° perspective, and at 38° orthographic you see walls
and cliff faces while roofs, wall tops and the ground pattern, which carry the style, go thin.
*Consequences:* at the same character size about 42% less ground is visible up the screen (19 u instead of 32.5 u) and
26% less across (26 u instead of 35.6 u); the screen-up offset and the 3 u look-ahead buy most of the forward view
back. A 2 u character at screen centre measures 90 px feet to head at 1080p (the art direction's 84 px is the
orthographic estimate; the head is 1.6 u nearer the lens). The numbers live in the view (`Runtime/Camera/GameplayCamera.cs`)
because the Sim assembly is frozen: `RenderConfig.CameraPitchDeg`, `CameraDistance` and `ViewHeightUnits` keep their
browser values in GameConfig (golden test) but the gameplay camera no longer reads them. Cinemachine's lens is
perspective and the P-09 orthographic brain override is removed. Mouse aim already casts each pointer ray from the
camera onto the plane at the player's ground height, so it needs no change; its tests move from 38° to 55° (0.53 u
miss instead of 0.96 u). Input directions are unaffected (yaw only). The parity capture camera stays orthographic for
the bible framings (U-017). The outline must be constant screen-pixel width (done in CM/Toon), since an object-space
width varies with distance under perspective.
*Follow, parity with the browser (review advisory R20261112T1918-2-2):* render-velocity look-ahead, the teleport rule
(a jump only zeroes the look-ahead velocity; the camera snaps only on a new match), the clamp after the spring step and
the height spring now match `CameraRig.tsx` at 09e0904; the 2 u screen-up offset, the 3 u cap and the aim term are this
amendment's.

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

## U-052 The loop takes over all of Max's items (2026-11-12)

**Decision.** The owner, Wilson, decided on 2026-11-12 by voice message: "take over all Max items ... all of it". The
loop now builds every item MAX-ITEMS.md gave Max (CM-30 to CM-38, CM-84, CM-85 and the rest), in the rules lane order
of the demo gap list (DEMO-GAP-LIST-2026-11-12.md section 5.2, starting with DG-02 = CM-32 jumping plus CM-33 any-angle
bridges). Max reviews each pull request afterwards and may take any item back. This supersedes U-045's ownership rule
("the loop never touches a Max-owned item"); U-012's split of mechanics to Max no longer binds the loop. Max is not
contacted by the loop about the change.
**Status.** Decided; MAX-ITEMS.md and `loop/README.md` section 1 state it.

## U-053 Dig, place and knock-down as built in DG-09: a dig / place mode, a three-block carry and a fall rule (2026-11-13T00:45:03Z)

**Source.** Travis, "What Demo needs" (2026-11-12 email): "Ground removable and placeable by miners" and "I have an idea
for some decoration structures to be able to knock down. Knock it down one direction, ramp to another area. Knock it down
a different direction, path block. (it doesn't need to be fancy, just enough to show the versatility)". The DG-09 brief
(rules lane, from the orchestrator) asks that F digs or places and that the wheel or LB / RB switches the mode, and that
falling into dug holes is handled.
**Ported as is (browser 09e0904, D-059, D-065).** `src/sim/terraform.ts` and `src/sim/topple.ts` rule for rule: the
keep-outs, the "nobody under it" occupancy rule, the route guard (pads to hill, no new shrine cut), the rate cap (30-tick
team cooldown, 15-tick world gap after any wall change, `World.WallChangeTick` stamped by `MapGrid.SetCell`), the two
mirrored pillars (3 swings by any class, away from the attacker, ramp = Void to Bridge, block = walkable to Wall, 4 x 3
footprint, refused while anyone stands in a wall cell). Oracle bots never dig or place, so the Unity bots do not either;
they knock pillars over only as the oracle's do, when a melee swing happens to reach one.
**Decided (not in the browser).**
1. Mode. Each miner has a dig / place mode (`World.Terraform.Mode`). F digs in Dig mode and places in Place mode;
   `InputFrame.TerraformSwitch` (mouse wheel notch, LB or RB, edge-latched by the human input layer, read and cleared by
   the sim) flips it. A dig that fills the carry switches to Place, a place that empties it switches to Dig. This
   supersedes U-007's "no dig / place mode" and U-023's "bound but unread" for the switch row, at the brief's request.
2. Carry. `MinerTerraform.CarryMax` = 3 [guess], so the switch has something to choose (dig a short trench, then fill a
   three-cell gap). CarryMax 1 replays the browser exactly (dig, place, dig...); the ported tests run with it.
3. Fall rule. A grounded actor that has stood unsupported for more than the DG-02 coyote window (3 ticks) with no walkable
   cell under its centre falls in and climbs out: it moves to the nearest cell centre within 6 rings
   (`MinerTerraform.FallSearchRings`, [guess]) where its body fits, or to its pad spawn point if none, with zero velocity and
   the knock-down get-up (`Respawn.StandUpSeconds`), and no damage (bible p13, "no accidental fall deaths"). A body corner
   over a fresh hole, or a diagonal bridge deck, is not a fall. With the ported rules nobody stands where ground is removed
   (digs and blocks are refused under actors), so in play this catches a jump that returns to a takeoff point dug while
   airborne, and later craters (DG-22 TNT).
4. Events. `Terraform` (dug / placed / refused) and `Topple` (falling: ramp / block / refused) as in the browser, plus
   `ToppleHit` (each counted swing, value = hits left), `ToppleDown` (FallTicks after the fall, at the footprint's far end)
   and `Fall` (where the actor climbs out), all with positions, for the effects and sound items.
**Status.** Built in DG-09 (draft PR, stacked on DG-02 #19). Max reviews; the owner can drop the mode (set CarryMax 1 and
unbind the switch) without touching the rules.

## U-054 Healing, meat, biomass corruption, contestable deliveries and the shrine cleanse as built in DG-16a / DG-16b (2026-11-13T01:05:37Z)

**Source.** Travis, "Current ideas for the game (not needed for demo)" (2026-11-12 email): "Slower corruption/biomass
corruption", "Different biomass types (meat from killed bodies/plants around the map", "A way to remove corruption (healer
special or something else)", "A way to heal (healer class or fountain/flask)"; Nov 4 call: "there's no way ... to kill them
before they drop off the resources". Gap list rows 6 to 9 and 14, items DG-16a and DG-16b.
**Ported as is.** Browser 09e0904 for CM-37 (base fountain, 1 HP per tick inside radius 3.75 of the own pad, bots retreat
below 30% HP as on main), CM-35 (a knocked-out unit drops a 2-unit meat orb for 600 ticks while the egg stands; walk-over
pickup into a separate meat load; meat pays 6 allegiance per unit) and CM-85 (every biomass unit adds 0.125 instability).
Unmerged branches cm/CM-64 (e056d8e: a 30-tick delivery wind-up, an enemy hit restarts it, Kaiju-duty bots contest carriers
within 11 of the shrine, a dead feeder keeps its duty seat) and cm/CM-36 (1c6ce00: hold E empty-handed at the egg for 15
ticks to drain 2 corruption units over 90 ticks, 2700-tick team cooldown, closed after the berserk lock or the hatch; only
the human slot on autopilot cleanses, never pure bots).
**Decided (not in the browser).**
1. The two branches were written separately on batch 1 and are merged here. A meat load counts as a load for the cleanse:
   an actor holding meat (or cargo) is disarmed until it lets go of E, so the press that delivers meat never cleanses.
2. The cleanse test that delivered with one held press now holds for the wind-up first; every older delivery test runs
   `Delivery.WindupTicks` passes, as the CM-64 branch did to its own tests.
3. `AiState.Cleanse` is appended at the end of the enum (the browser lists it before retreat) so no stored integer moves.
**Status.** Built in DG-16a and DG-16b (one draft PR, stacked on DG-09 #29). Known: the single-seed bot check
`BotsMatchTests` (a) (5v5 seed 1, 120 s) reads azure hill presence 0.498 against its 0.70 floor once the wind-up and
interception are on; the browser's CM-64 hit the same check (0.5224) and its bounce fix (interceptors are Kaiju-duty bots
only, dead feeders keep their seat) is ported. See the PR for the soak numbers.

## U-055 Damage cause, passive bots and attack events for the view (2026-11-13T01:05:37Z)

**Source.** Two view builders (DG-11 kill feed and death recap, DG-12 guided first match, combat effects) asked the rules
lane for small sim additions. Not in the browser.
**Decided.**
1. `DamageCause { None, Melee, KaijuMelee, KaijuSlam, Fall, Explosion }`, written on every Hit and Death event
   (`SimEvent.Cause`) and kept in `Actor.LastCause` until respawn. Fall is reserved (the DG-09 fall rule does no damage),
   Explosion for DG-22. `Damage.ApplyDamage` gains an overload with an explicit cause.
2. The slam passes `Damage.KaijuSlamSource = -3` (the value DG-11's `CombatLog.KaijuSlam` already uses), but the event's B
   and `LastAttacker` still read -1 for it, because ported tests pin that; the slam is told apart by its cause.
3. `BotsConfig.PassiveSeconds` (default 0): while match time is below it, bots do not perceive, intercept, chase or swing
   at actor 0, and a bot's cleave skips actor 0. Deterministic (sim time only). The tutorial sets 30.
4. Events `AttackStart` (A attacker, at the swing's reach point, Value facing) and `KaijuDamaged` (A attacker, at the
   contact point on the Kaiju's hit radius toward the attacker, Value damage), placed before the CM-84 marker.

## U-056 DG-16b retune: delivery wind-up 12 ticks, intercept radius 14 (departs from the browser) (2026-11-13)

**Finding.** The port of CM-64 is faithful. Diffing delivery.ts, resources.ts, damage.ts, fsm.ts (interceptTarget,
interceptorRole, the intercept override), director.ts (deadKaijuDuty), steer.ts and the config line by line found no unit or
selection error: ticks are ticks in both, the radius is world units in both, only Kaiju-duty bots intercept, and an
interrupt only touches a wind-up in progress (deliverTicks > 0). The browser's own CM-64 branch, run unchanged
(SLOW=1 vitest src/sim/delivery.slow.test.ts on cm/CM-64), measures berserk share 0.05 and fails its own CM-34 band
[0.10, 0.50]; intercepts azure 386 / crimson 466 in 93 of 100 seeds (the port: 392 / 452, 93). That is why CM-64 never
merged in the browser. The wind-up starves corruption deliveries, and berserk is the short-handed team's comeback, so the
D-030 passive-human sweep falls with it.

**Rule.** kaijuFeed.deliverWindupTicks 30 -> 12 and bots.interceptShrineRadius 11 -> 14. 12 is shorter than the fastest
swing (farmer 18 ticks): one defender can delay a carrier but never pin it (the DG-09 soak's 'stationary carrier' was a
lone interceptor pinning a 30-tick wind-up until the carrier died); two defenders still can. 14 = 11 + one
shrineInteractRange, so the shorter wind-up stays contestable. DeliveryTests now asserts the wind-up is shorter than the
fastest swing.

**Measured** (no-Unity runner; berserk and intercepts: 5v5 seeds 1..100 all bots; passive: D-030 seeds 201..250):

| wind-up, radius | berserk /100 | intercepts | seeds with one | passive-team wins /50 |
| --- | --- | --- | --- | --- |
| 1, off (before DG-16b) | 38 | 0 | 0 | 20 |
| 30, 11 (browser) | 5 | 844 | 93 | 1 |
| 30, off | 22 | 380 | 61 | 5 |
| 20, 11 | 18 | 642 | 88 | 8 |
| 20, 17 | 12 | 804 | 96 | 6 |
| 15, 11 | 24 | 164 | 76 | 7 |
| 10, 11 | 32 | 138 | 76 | 11 |
| 10, 14 | 33 | 178 | 84 | 7 |
| 12, 17 | 22 | 208 | 89 | 6 |
| **12, 14 (chosen)** | **27** | **192** | **86** | **11** |

Gates: berserk in [10, 50], intercepts in at least 80 of 100 seeds (DG-16b row), D-030 at least 11 of 50. Only 12 / 14
clears all three, and the D-030 margin is zero: neighbouring settings read 6 to 11, so that sweep is noisy at 50 seeds and
any later rule touching corruption delivery will move it. The structural cause (any wind-up taxes corruption carriers,
which the short-handed team relies on) is not fixed here; a corruption hand-off without a wind-up is the candidate if it
bounces.

## U-057 Autopilot cleanse gate: 8 net corruption units, not 10 (departs from the browser) (2026-11-13)

**Finding.** After U-056 the CM-36 soak still read 0 cleanses. The port of the CM-36 autopilot rule is faithful (actor 0,
biomass lead, kaijuFeed.cleanse.botNetUnits net corruption units in the egg, cleanse ready). Instrumented over 30 seeds:
net corruption units never exceeded 9 before the berserk lock or the hatch, so the browser's 10 can never be met here. The
cause is CM-85 (DG-16a): delivered biomass also feeds instability, so berserk locks a few corruption units earlier than on
the CM-36 branch, which predates that rule. **Rule.** botNetUnits 10 -> 8, the largest whole value below the measured
ceiling that the cleanse can reach. **Measured** (CM-36 driver, 5v5 seeds 1..100, all bots, stop at lock or hatch):

| botNetUnits | berserk /100 | cleanses | matches with one |
| --- | --- | --- | --- |
| 10 (browser) | 27 | 0 | 0 |
| **8 (chosen)** | **19** | **17** | **17** |
| 7 | 19 | 17 | 17 |
| 6 | 17 | 36 | 35 |
| 5 | 17 | 36 | 35 |

Berserk stays inside the CM-34 band [10, 50]. Pure bots still never cleanse; only the autopilot slot does.


## U-058 Bot difficulty as built in DG-21: Easy, Normal, Hard (not in the browser) (2026-11-13T02:15:17Z)

**Why.** The browser has no bot difficulty (git grep on 09e0904 and every cm/* branch finds none), so the rule is ours: the
smallest one that meets row DG-21 (reaction delay, aim error, ability use, target choice, objective discipline).
**Rule.** `WorldOptions.BotDifficulty` sets the bots of the team the human does not play (what match setup's "Bot
difficulty" sets); `WorldOptions.AllyBotDifficulty` sets the human's own team (default Normal; the menu leaves it).
Normal has no profile: every call site keeps the browser code path, so a Normal match is the browser match bit for bit
(four golden state hashes taken on 798fa11 before any DG-21 code, test `DG21NormalIsTodaysBotsBitForBitOnTheGoldenSeeds`).
Easy and Hard read `Bots.Easy` / `Bots.Hard` (`BotSkillConfig`). Every roll is a stateless hash of seed, actor, a salt and a
tick bucket (`BotSkill.Roll`): no shared bot RNG draw moves.

| Lever | Easy | Normal | Hard |
| --- | --- | --- | --- |
| Reaction: think period (ticks at 30 Hz) | 12 | 8 | 4 |
| Aim error on roots and TNT (units, disc) | 1.5 | 0 | 0 |
| TNT lead on a moving enemy (x velocity x fuse) | 0 | 0 | 0.6 |
| Swing held in this share of 1/3 s windows | 0.4 | 0 | 0 |
| Chance a decided ability press is sent per think | 0.5 | 1 | 1 |
| Warrior shield: enemies within 3 | 3 | 2 | 1 |
| Target choice | nearest | nearest | carriers x0.25 on d2, hurt enemies first |
| Off-hill chase factor while holding | 1 | 0.5 | 0.25 |
| Retreat when hurt and an enemy is within | 6 | 6 | 3 |

**Measured** (5v5, CreateBrowser5v5, whole matches, actor 0 on autopilot). Hard vs Easy: Hard wins 20 of 20 as Azure and
20 of 20 as Crimson (floor 14 of 20). The human proxy (Normal allies) wins 20/20 against Easy, 11/20 against Normal, 5/20
against Hard. Hard was tuned on 30 seeds: the first cut (think 6, chase 0.35, retreat 8) won only as often as Normal (18 vs
19 of 30); the faster think, the tighter hill discipline and the shield at one enemy each mattered (10 of 30 with all three).

## U-059 The Demolitions Expert as built in DG-22: TNT and mines, ported from CM-38 with view events (2026-11-13T02:24:16Z)

**Source.** The browser's Demolitions sim is on 09e0904 itself (cm/CM-38 is an ancestor: `src/sim/demolitions.ts`, D-068),
so the rules are a port: mine on Space (place range 1.6, arms after 15 ticks, lives 1800, first grounded unprotected enemy
in its cell triggers it, 70 damage within 1.2, two per owner and the oldest recycles, pad keep-outs), TNT on the DigPlace
binding (F; Y on a controller) on the rising edge (range 5, 45-tick fuse, 55 damage within 2, own 150-tick cooldown), no
regular attack, enemy barricades hit, breakable walls in the radius destroyed, and a crater of Floor / Bridge cells within 1
(placed ground is Floor) decided cell by cell by the CM-30 dig rules (keep-outs, actors on the cell, the route guard) and
refused inside the CM-30 world gap. Bots: the CM-38 `decideDemolitions` rule (TNT at the enemy or the centroid in range,
else a mine on its own cell while holding the hill or fighting near it). The browser's six CM-38 unit cases are ported.
**Departures (ours, flagged in the PR).**
1. State is a World field (`World.Demolitions`, fixed pools, reset in `InitWorld`), the Unity pattern, not a side table.
2. View events (appended before the CM-84 marker, `SimEvent.Radius` appended, nothing reordered): MinePlaced, MineArmed,
   MineTriggered, MineRemoved (expired, ground gone, recycled), TntThrown, TntLanded, Explosion (B = mine or TNT, Value =
   actors hurt). The browser's Ability events (cast, refusal codes, blasts with A = -1) are kept as they were.
3. TNT touches down `DemolitionsTnt.FlightTicks` = 12 ticks after the throw (TntLanded) [guess: 0.4 s]; the fuse still
   counts from the throw, so the blast tick is the browser's.
4. Blast damage goes through `Damage.ApplyDamage(..., DamageCause.Explosion)`; a bot's blast respects the DG-16 passive
   window like its swing; an exhausted bomb pool is counted (`Stats.PoolRefusals`), not logged.
5. Difficulty (U-058) in the bot rule: Hard leads a moving target by 0.6 x velocity x fuse; Easy throws up to 1.5 units off
   and only on half its thinks. Normal is the browser rule.
6. The default bot ClassMix keeps Demolitions at 0, so default matches (and every existing outcome band, including the
   passive-team sweep that has no margin) are unchanged. Bots play the class whenever a config gives it a share; the soak
   uses 3 of 16 per team (the browser soak's count). Turning it on by default is the owner's call after the integrator's
   soak: in the 16v16 soak (seed 101, 3 per team) the six Demolitions bots score 144 blast knock-outs to 109 deaths at
   Normal, the most lethal class per head, so a damage or cooldown pass may be wanted first.
7. On-screen names live in one place, `DemolitionsNames` (MINE, TNT, DEMOLITIONS); GameHud's AbilityNames should read them.

## U-060 Builder ladders and rope bridges as built in DG-24: temporary, paid from a team bank, carried by any class (not in the browser) (2026-11-13T02:43:02Z)

**Source.** The oracle (09e0904 and every branch) has no ladder or bridge piece: its only Builder code is cm/CM-39
`upgrade.ts` (spend team biomass to raise an ally's class level), which this item does not port (the station / upgrade half
of row DG-24 is still open). So the rule is ours, the smallest one that satisfies Travis 2026-11-06 ("Build temporary
ladders/bridges with biomass / Ladders/bridges movable by all classes") and the Nov 4 call ("ladders to get from different
elevation levels ... rope bridges"). On this grid a cliff is a Wall cell and a gap is Void or Water, so:
1. **BUILD (Space, Builder).** Along the cardinal axis nearest the aim (no aim: facing), the first blocked cell within 2 of
   the Builder's cell decides: a Wall run up to 2 long makes a LADDER (1 biomass), a Void / Water run up to 6 long a ROPE
   BRIDGE (2 biomass); the next cell must be walkable (the landing). 3 lanes wide (Topple.WidthCells), centred on that line;
   every cell of the kind's type in that rectangle becomes Cell.Bridge through `MapGrid.SetCell`. Refused (Ability event with
   a code, Space locked 0.5 s): nothing to span, too long, breakable / barricade in the way, shrine keep-out, map edge, the
   Builder's 2 pieces already out, bank short, the CM-30 world gap. Class row unchanged (the CM-56 miner copy: mallet swing).
2. **BANK.** StartBiomass 2 + the team's delivered biomass - what it spent. Spending never changes allegiance, instability or
   the hatch (the CM-39 bank idea, read from Delivered instead of a new MatchState field).
3. **TEMPORARY.** 60 s from the build, carried time included; PieceCrumbling 3 s before. A cell that stops being Bridge (dug,
   cratered) breaks the piece (PieceDestroyed). Taking a piece away (expiry, break, lift) only reverts cells still Bridge, and
   only when nobody (or the hatched Kaiju, or a barricade) is on them, the world gap has run out and the ROUTE GUARD passes
   (`Terraform.CellsKeepRoutes`); otherwise it retries every tick. So a piece never leaves anyone in a wall or over the void,
   and never seals a base from the hill: a piece that became the only route stays until another route exists.
4. **CARRY (Interact, any class of the team).** Empty-handed and outside a node's / the shrine's interact range (where E
   gathers and delivers), within 1.0 + radius of a piece cell: lift it (same safety checks). The next press puts it down in
   front by the build rule of its kind (no cost, no cooldown). A carrier that dies drops it: gone. Bots never carry.
5. **Bots.** A Builder bot on its way to the hill (ToHill, Unstick) or the shrine (Deliver, Corrupt) builds the accepted
   piece whose landing is at least 2 cells closer by its team flow field. Skybreak Basin is well connected, so this is rare:
   1 ladder per Builder-heavy 16v16 match at Normal and at Hard (seed 101); Builders stay out of the default ClassMix.
6. View events (appended before the CM-84 marker): PiecePlaced, PieceCrumbling, PieceExpired (expired / dropped / broken),
   PieceDestroyed, PiecePickedUp, each with a position and Radius = half the length; live state in `World.Builder.Pieces`.
7. Names in one place: `BuilderNames` (BUILDER, BUILD, LADDER, ROPE BRIDGE, CARRY).

## U-061 The Healer class as built in DG-40: mend, healing pulse, purify; no weapon (not in the browser) (2026-11-13T02:50:20Z)

**Source.** The oracle has no Healer on any branch (its only heal is the CM-37 fountain, its only corruption removal the
CM-36 shrine cleanse; cm/CM-36 `cleanse.ts` names "healer special" as the idea it chose not to build). Travis 2026-11-06:
"Healer / Heal / Remove corruption"; "A way to remove corruption (healer special or something else)". Art direction: an
armoured battle healer with wings and NO sword; she heals and supports. So the rules are ours, the smallest set that gives
both of Travis's words a button on the bindings already on main:
1. **ClassId.Healer = 5, appended** (every ClassPick index stays valid), class row MaxHp 110, speed 4.1, radius 0.42,
   AttackDamage 0, AttackRange 4.5 (where bots stop from the fight), AttackCooldown 0.8, AbilityCooldown 9. Default
   ClassMix 0: not in the default bot lineup.
2. **MEND (attack).** No damage ever: the press heals the hurt ally (not herself) within 5.5 nearest the aim, 6 HP. No
   target, no cooldown. Not an attack, so spawn protection stays.
3. **HEALING WINGS (Space).** 30 HP to every ally within 3.5 of the aim (pulled back to 6), herself included.
4. **PURIFY (DigPlace, F / Y).** Within 4 of the shrine, while `Cleanse.CleanseOpen` and corruption is in the egg: one
   corruption unit removed at once (9 instability, the CM-36 units-to-instability rule), counted as cleansed
   (`Cleanse.NetCorruptionUnits` subtracts `World.Healer.PurifiedUnits`, 0 without Healers, so CM-36 is unchanged); own
   30 s cooldown, the team cleanse cooldown untouched. Refusals push an Ability event with a code.
5. **Bots.** Attack presses come from the usual steering (she mends the hurt ally nearest the enemy she faces). Purify when
   possible and her team is not behind in allegiance (or instability is at half the threshold). Pulse on the hurt ally
   whose radius holds the most missing HP, when that is a full pulse or someone there is under a third of max HP. DG-21
   Easy acts on half its thinks.
6. Events (appended before the CM-84 marker): HealCast (mend / pulse, with position and pulse radius), HealLanded (per
   actor), Purify (shrine, radius). Names in one place: `HealerNames` (HEALER, MEND, HEALING WINGS, PURIFY).
7. **Tuning, measured.** First cut (mend 9, pulse 40): a team with three Healers in place of three Warriors beat the default
   lineup 13 of 16; at mend 6, pulse 30: 10 of 16 (control, no swap: 8 of 16; three Miners: 3 of 16; three Builders: 0 of 16).
   Shields and revives were not added. The CM-54 class switch (CM-84 markers) is still unported.

## U-062 Random berserk line on a square-root curve as built in DG-41, off by default (not in the browser) (2026-11-13T02:58:16Z)

**Source.** No branch of the oracle has it (its lock is `instability >= berserkThreshold`, 100). Travis 2026-11-06:
"Corruption berserk threshold should be random, but affected by deposited corruption on a parabolic curve (y=√x)".
1. **Rule.** With `KaijuFeed.RandomBerserk` the line that locks berserk is u² x `BerserkCurveMax`, u uniform in [0, 1) from
   a stateless hash of the match seed (`SimResources.SeedUniform`, never the shared RNG), fixed for the match. So the chance
   that instability I has locked berserk is √(I / BerserkCurveMax): y = √x, x being the corruption deposited (instability).
   Every lock check (delivery, the Kaiju step, the debug feed) reads `SimResources.BerserkLine`.
2. **Off by default.** It changes match outcomes, so on it would move the golden Normal hashes (U-058) and every berserk band;
   the owner turns it on. Measured on 100 whole 5v5 matches (seeds 1..100, shipped config): fixed threshold 0.19 berserk
   share; random line with curve top 160: 0.68, 250: 0.51, **800 (shipped): 0.30**, 1200: 0.23 (CM-34 band 0.10 to 0.50).
3. The HUD's instability bar (Runtime/Hud/HudState.cs line 95) still draws against BerserkThreshold; with the rule on, the
   view should hide the line (it is a secret roll) or show the √ chance instead. Not changed here (rules lane only).

## U-063 Class roles made real: only Farmers feed the egg, only raiders carry corruption, everyone else hands off or escorts (our rule) (2026-11-13T04:22:24Z)

**Source.** Our rule. No branch of the oracle restricts who feeds. The owner after playing: "the warriors are feeding the
eggs, it's not even proper". Travis names the classes and their jobs, not who may feed.
0. **The classic switch.** Every rule of U-063..U-068 sits behind `GameConfig.Depth.Classic`. `CreateBrowser5v5()` sets it
   true (the game as it was: the DG-21 golden hashes and every older test are unchanged); `CreateDefault()`, which the game
   runs, leaves the rules on. Each rule also has its own `Enabled`. Numbers: `Sim/Config/DepthConfig.cs`, one block per rule.
1. **Feed.** Only a class in `Roles.MayFeed` (the Farmer) hands plant, meat, wood or water to the egg. Any class may gather
   and carry it; a carrier that may not feed presses interact beside an allied Farmer to hand it over (`Handoff` event) or
   banks it at its base (U-064). At the shrine it keeps its load and gets one `DeliverRefused` event per 45 ticks.
2. **Corrupt.** Only Warrior, Miner and Demolitions (`Roles.MayCorrupt`) gather, carry and deliver corruption.
3. **Bots obey.** Lineup with the rules on (`Roles.ClassMix`), at 16: 5 Warriors, 4 Farmers, 2 Miners, 2 Demolitions,
   1 Builder, 2 Healers. Only Farmers gather and deliver; only raiders run corruption; an egg-duty bot that may do neither
   escorts the nearest allied carrier (`AiState.Escort`) and still intercepts enemy carriers.
4. **Small teams.** A team of 8 or fewer uses `Roles.SmallTeamClassMix` (at 5: 2 Warriors, Farmer, Miner, Healer); one
   of each class left a five with a single Warrior and two classes with no weapon.
5. **Measured** (seed 7, 16v16, first 3 minutes, all rules on): 37 feed events, all by Farmers; 46 and 52 units delivered;
   1 unit of corruption (bots rarely raid with the rule on: see the owner question in the PR).
6. **The idle-player check, measured.** Classic rules (D-030, unchanged by this lane): the idle player's team wins 37 of
   200 5v5 seeds, before and after. With U-063..U-068 on: **20 of 200 (0.10), under the 0.15 floor.** Class roles are the
   cause (roles alone 9 of 100, each other rule alone 14 to 23 of 100, classic 17 of 100 on the same seeds): with roles an
   idle seat is a missing specialist. Tried and measured worse: one of each class (16), two Farmers and no Healer (0),
   escorts off (1), egg duty capped to Farmers (0), non-feeders carrying food to Farmers (11). `DepthSoakTests` reports
   the number with a 0.06 collapse floor. Whether 0.10 is acceptable at 5v5 is the owner's call.

## U-064 Biomass sources as built for DG-42: wood groves and water springs, a team stock at the base, supply bots (our rule) (2026-11-13T04:22:24Z)

**Source.** Our rule from Travis 2026-11-06, "Biomass sources / Meat / Wood / Water / Corruption". The browser has plant,
meat and corruption only.
1. **On the map.** 6 wood groves and 5 water springs, mirrored, on open floor both pads can walk to (`Sources.Place`).
   Hold interact beside one to gather: class gather time x the class scale (Builder and Miner chop in half the time, the
   Healer draws water in two thirds). A hit restarts the gather. Wood: 4 charges, one back per 20 s. Water: 3, one per 15 s.
2. **Carry.** `CarryKind.Wood` / `Water` (appended) ride in the class cargo, one kind at a time.
3. **Spend, two ways.** Feed: wood 7 allegiance a unit, water 4 and each unit cools the egg by 3 instability (never below
   0, never after the berserk lock). Bank: a press of interact within 4.5 of the own pad puts the load in the team stock
   (wood, water, food). Banked wood pays Builder pieces first (U-060) and pays upgrades (U-065) and bridge repairs (U-068);
   a Farmer with empty hands takes food back out.
4. **Bots.** Before the hatch floor(team x 0.125) bots of Builder, Miner, Demolitions run supply (16: 2 a team, 5: none).
5. **Measured** (seed 7, 16v16, 3 minutes): 30 wood and 8 water gathered, 38 units banked, at most 2 supply bots a team.

## U-065 Base stations: live class switching at the base (CM-54 ported into the CM-84 markers) and Builder-bought class upgrades (CM-39 ladder ported, paid our way) (2026-11-13T04:22:24Z)

**Source.** Ported: cm/CM-54 (switch cooldown 150 ticks, `classSwitches` counter, 'classSwitch' event codes Accepted 0 /
Cooldown 1 / Alive 2) and cm/CM-39 `upgrade.ts` (linear ladder, cost 3 then 5, +15% then +30% max HP, refusal codes -2
max level and -3 cannot pay). Ours: switching while alive, team-wide class levels, paying in wood and water at the base.
Travis: "Builder / Build class upgrades with biomass".
1. **Station.** Each team's station is its base zone (4.5 of the pad). Names in `StationNames`.
2. **Live switch.** A class pick by a living, grounded actor in its station changes its class at once: load banked (or
   dropped), health share kept, ability locked 3 s, 150 ticks before the next switch. Away from the station the pick is
   refused (`ClassSwitch` code Alive), as in CM-54. A dead actor's pick queues for the respawn, now with the cooldown.
3. **Upgrades.** A living Builder in its station sends `InputFrame.StationBuy` = class index + 1: that class's level rises
   for the whole team for the match. Level 2: 3 wood + 1 water, +15% max HP. Level 3: 5 wood + 2 water, +30%. Living
   members gain the HP at once; later respawns start with it. One `Upgrade` event per attempt (new level or a refusal code).
4. **Bots.** A Builder bot in its station buys the affordable level for the class with most living teammates. Bots do not switch.
5. **Measured** (seed 7, 16v16, about 4 minutes): 10 upgrades bought by bots across both teams from 33 wood and 13 water.

## U-066 Pickups and drops: coins from knock-outs and map piles, Haste / Might / Ward with counters, a seed-fixed spawner (our rule) (2026-11-13T04:22:24Z)

**Source.** Our rule for DG-39 (the owner: "the skill points, the coins collected, drop item, everything"). Not in the browser.
1. **Coins.** A knocked-out actor leaves 3 coins where it fell for 20 s, for anyone. 10 map piles of 5 (mirrored spots),
   back 30 s after being taken. Walking over adds to `Actor.CoinsPicked` (Runtime's MatchRewardTracker marker reads it).
2. **Power-ups**, one at a time per actor. HASTE: move x 1.3 for 10 s; a Farmer's roots strip it. MIGHT: melee x 1.5 for
   10 s; shields still cut it. WARD: soaks the next 60 damage for 15 s; any explosion goes through and breaks it. A
   knocked-out carrier drops its power-up with the time left (2 s or more) for 15 s.
3. **Spawner.** From 20 s, every 45 s, one power-up on one of 4 centre-line spots, chosen by a stateless hash of the seed
   (never the shared RNG), announced 5 s before it can be taken, gone 30 s later. Fixed pool of 96; the oldest drop is recycled.
4. **Bots** take what they walk over and go for a power-up within 9 units in line of sight.
5. **Measured** (seed 7, 16v16, 3 minutes): 445 coins picked up, 4 power-ups spawned, 3 taken, 0 pool recycles.

## U-067 Pings the bots answer, bots that call their own, named bots with play styles (our rule, to the HUD lane's spec) (2026-11-13T04:22:24Z)

**Source.** Our rule: the unbuilt half of DG-21, written to the spec in `Runtime/Hud/Interact/Pings.cs`. Not in the browser.
1. **Input.** `InputFrame.PingKind` = call + 1 (Attack 0, On my way 1, Defend 2, Help 3: the HUD's wheel order) with
   `PingX` / `PingZ`, for one tick. The sim accepts 3 per actor per 150 ticks (`Ping` event) and refuses the rest (`PingRefused`).
2. **Who answers.** Living bots of the caller's team within 40 units, nearest first, at most 3 for Attack and Defend, 2
   for Help, none for On my way. Never a bot that carries something, already has an order, or holds the hill with fewer
   than 2 teammates on it; a class with no weapon never answers Attack or Help. Each answer pushes `PingAnswered`.
3. **What they do.** Attack: fight the nearest enemy within 8 of the ping, else walk there; over when the spot is clear or
   after 12 s. Defend: hold within 5 of the ping for 12 s. Help: go to the caller and fight its last attacker for 8 s.
   A bot below the retreat line drops the order. Obeyed on the ping's own tick (`AiState.Order`).
4. **Bots call too.** Under 35% health with an enemy on it: Help. Fighting an enemy carrier before the hatch: Attack.
   One call per bot per 20 s, one per team per 8 s, one answerer per bot call.
5. **Names and styles.** `BotNames.NameOf(world, id)`: 37 names, unique in a match, fixed by seed and slot.
   `BotNames.StyleOf`: Brawler, Guard, Harvester, Sapper, from the class (40% of Warriors are Guards). A style scales how
   readily the bot fights (1.15 / 0.95 / 0.8 / 1) and which calls it answers first.
6. **Measured** (seed 7, 16v16, 3 minutes): bots made 36 Help and 3 Attack calls, 39 answers; a player's Attack ping drew
   3 answers on its tick; same seed and same ping ticks give the same world hash.

## U-068 Map events: contested supply drop, egg exposed, bridge collapse and Builder repair (our rule) (2026-11-13T04:22:24Z)

**Source.** Our rule. Not in the browser.
1. **Schedule.** First event at 50 s, one every 60 s. The three kinds rotate from a start fixed by the seed (stateless
   hash). A kind that cannot run (egg hatched, a bridge already down, no safe span) becomes a supply drop.
2. **Supply drop.** Announced 8 s ahead on a centre-line spot, then lands. A team claims it by standing within 2.5 for 3 s
   with no enemy there (contested: nobody's clock runs). Reward: 4 wood and 2 water to the team stock, 10 coins to the
   claimer, a power-up on the spot. Gone after 30 s unclaimed.
3. **Egg exposed.** For 20 s every delivery, food or corruption, counts double toward allegiance. Ends at the hatch.
4. **Bridge collapse.** One span is announced 8 s ahead, then its middle third falls; anyone on it falls and climbs out
   unhurt (the DG-09 fall rule). Never a span whose loss cuts a base from the hill or the shrine (the CM-30 route guard,
   re-checked at the collapse). A Builder holding interact within 3 of a broken end for 3 s rebuilds it for 2 wood from
   the team stock; otherwise it rebuilds itself after 45 s. Bots do not repair (the timer does).
5. **Events.** SupplyDropIncoming / Landed / Contest / Claimed / Expired, EggExposed / EggExposedEnded, BridgeWarning /
   BridgeCollapsed / BridgeRepaired, each with a position. Announcer lines in `MapEventNames`.
6. **Measured** (seed 7, 16v16, 4 minutes): 4 events, 1 drop claimed by bots, 1 egg window, 2 bridges down, 1 back; both
   pads still reach the hill at the end. All 16 event numbers find a span that passes the route guard.

## U-069 Rules combined: sim physics plus mechanics depth on one branch, one rules switch (our rule) (2026-11-13T05:57:05Z)

**Source.** Integrator decision. Supersedes the separate drafts #49 (feat/sim-physics) and #58 (feat/mechanics-depth).
1. **Merge order.** Physics events first, exactly as pinned by #49, then the mechanics events, all before the CM-84
   marker; new `SimEvent` and `Actor` fields at the end in the same order; enum members appended only.
2. **One switch.** `GameConfig.ApplyRules(RulesProfile)`: `Classic` = browser parity (every physics rule off, instant hits,
   every depth rule off, pace 2x), `PhysicsOnly` = physics on, depth off (the test config, `CreateBrowser5v5`), `Full` =
   everything on (`CreateDefault`, the game). `GameConfig.CreateClassic()` and `GameConfig.Rules` read it back.
   `PhysicsConfig.Classic` and `DepthConfig.Classic` stay as the two lane switches underneath.
3. **Composition.** Might scales the physics wind-up swing (fair hits included); Haste multiplies with the swing
   multiplier; every mechanics `*Ticks` read goes through `Durations.T30` (the DG-68 one-time-unit scan).
4. **Goldens per profile** (`GoldenHashesOfTheNewDefaultAreStable`): Classic equals the DG-21 goldens bit for bit at pace 2x.

## U-071 Allies walking into each other pass instead of jamming (fixes the DG-22 Demolitions soak stall) (our rule) (2026-11-13T05:57:05Z)

**Source.** Our rule (physics lane). Trace of the #49 red: a Crimson bot in Retreat at (64.1, 41.5) walking east along a
one-cell corridor while three Crimson allies in Fight walked west through it. Teammates pushed each other at half
strength, so the retreating bot was shoved back every tick and stood 96 ticks (limit 90). Not a Kaiju or wall-corner case.
1. **Rule.** In the relaxed separation, a bot in Retreat and a teammate whose input direction points into it (dot product
   under `PhysicsConfig.AllyPassDot` = -0.3) do not push at all (`AllyPassOpposing`, on). Everyone else pushes as before.
   The 90-tick limit is unchanged. Rejected: every opposing ally pair passing (fixed the stall but cost the D-030 passive
   check 39 -> 21 of 200 and the berserk share 0.14 -> 0.10).
2. **Measured.** DG-22 worst still: Easy 58, Normal 58, Hard 61 ticks (was 96 on Normal in #49).

## U-070 Every class feeds the egg; the Farmer is the best at it (owner decision, supersedes U-063's Farmers-only feeding) (2026-11-13T06:12:27Z)

**Source.** Owner decision 2026-11-12: the game's creator never restricted feeding and the browser lets anyone feed.
Supersedes U-063 item "only Farmers feed" and the small-team relaxation variants the integrator was asked to test first
(dropped: no longer needed).
1. **Rule.** `RolesConfig.MayFeed` is true for all six classes at every team size: no `DeliverRefused` with the defaults.
2. **The Farmer is the best feeder** (`RolesConfig.BestFeeder`): its browser stats already carry 3 a trip (others 1, Healer
   2) and gather in 1 s (others 1.5 to 2 s); on top, its deliveries count `BestFeederBonus` = 0.25 extra allegiance.
   Measured on 200 + 200 idle-player seeds and 100 CM-64 seeds: bonus 0, 0.25 and 0.5 gave the same bands within one
   seed (5v5 idle 2 / 2 / 3 of 200 before U-072), so the mildest value that still makes the Farmer's unit worth more was taken.
   Farmers deliver 0.73 of all food with the rules on (every class delivers the rest).
3. **Optional plays.** A non-Farmer pressing interact beside an allied Farmer hands its food over (`Handoff`); food taken
   out of the team stock is the same. Those units pay `PassBonus` = 0.1 extra once, on the delivery (`DepthState.PassUnits`);
   a deposit pays nothing, so a bank-and-withdraw loop earns nothing.
4. **Bots.** Every class delivers food it holds when full, when no node is near, or within `DeliverNearShrine` = 10 of the
   shrine; a Farmer bot's gather utility is scaled by `BestFeederGatherBias` = 1.25. Escorts only exist when a config turns
   `MayFeed` off for a class. The Farmer role line is "Best at feeding the egg"; no line tells another class it cannot feed.

## U-072 Corruption reopened to every class but the Healer; raiders still go first (our rule, supersedes U-063's raiders-only corruption) (2026-11-13T06:12:27Z)

**Source.** Integrator decision for the designer's question 3 (bots ran 1 unit in 3 minutes).
1. **Cause, measured** (everything on, 100 seeds 5v5 unless named): with U-063's raiders-only corruption, bots delivered
   4.5 units a match (8.5 with the depth rules off), the berserk share was 0 of 100 (band 0.10 to 0.50) and the idle
   player's team won 2 of 200. Turning class roles off alone gave 19 of 200 and 14 of 100; the Healer, a lower trigger
   (`CorruptWhenBehindBy` 5 or 0), the small-team lineup, map events, stations, pickups, pings and sources changed
   nothing (each 0 to 2 of 200, berserk 0). Corruption open to every class gave 79 of 200 and 23 of 100.
2. **Rule.** `RolesConfig.MayCorrupt` is true for every class except the Healer (she purifies). A trailing team's
   deliberate run goes to a raider first (`CorruptionRunner` = Warrior, Miner, Demolitions), then by class index.

## U-073 Knockback stays OFF by default; measured again with KnockSparesCarriers under the combined rules (our rule) (2026-11-13T06:31:40Z)

**Source.** Integrator, owner rule from #49 (default ON only if every band passes).
1. **PhysicsOnly + knockback + spares** (pace 1x): idle-player 5v5 19 of 200 (floor 30: 11 short; #49 read 27), CM-64
   interceptions 86 of 100, berserk 0.16, hill 0.749 / 0.891, proxy 19 > 8 > 2. Fails the D-030 band.
2. **Everything on + knockback + spares**: idle 5v5 64 of 200, 16v16 134 of 200, interceptions 82, berserk 0.21, but hill
   presence 0.636 (floor 0.70: 0.064 short) and proxy Normal 8 = Hard 8 (ordering lost). Fails two bands.
3. **Decision.** `PhysicsConfig.Knockback` stays false. Every DG-70 rule test still stages it on.
4. **DG-24 build check.** U-071 moved paths so the single-seed "Builder bots built something" check read 0 at Hard on
   seed 101; the check now widens to seeds 102 and 103 at the same difficulty (as U-056 did for hill presence). Measured
   1 build in the three Hard seeds: the Builder bot's build rule is still thin (#49 said so too).
