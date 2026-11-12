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
