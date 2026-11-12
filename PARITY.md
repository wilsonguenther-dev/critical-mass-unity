# Critical Mass (Unity): the parity gate

Written 2026-11-12T03:08:23Z (date -u). This is the spec for decision **U-010** ([DECISIONS.md](DECISIONS.md)). It says
how the game is measured against Jeisil's Visual Asset Bible and when a bible row passes. Items marked **OPEN** are
not decided and must not be chosen by a builder; items marked **ESTIMATE** are the spec recon's own readings, not
bible text.

Sources:
- The bible: `/Users/wilsonguenther/Library/Mobile Documents/com~apple~CloudDocs/Critical-Mass-Visual-Asset-Bible.pdf`
  (16 pages; PDF page N = printed footer N).
- The bible reference crops (150 dpi, 105 named boxes, pp 03-15), cut by the browser repo's `scripts/parity/crops.json`:
  `/Users/wilsonguenther/critical-mass/loop/parity/ref/`. Below, `ref/` means that folder. Nothing binary is copied
  into this repo by this document.
- Spec recon `spec-recon-2026-11-11.md`, sections 1 and 2.

---

## 1. Terms

- **Bible row**: one bible page from p03 to p15, one row on the board.
- **Framing**: one fixed view of one asset, matching one of the bible's own poses and angles (one reference crop).
- **ID mask**: a second render of the same framing in which the asset's own pixels are white and everything else is
  black, so the checks only look at the asset.
- **CIEDE2000 (ΔE2000)**: a standard number for how different two colours look to a person; under about 2 is hard to
  see, 10 is a clear but close match.
- **IoU (intersection over union)**: overlap of two silhouettes = pixels in both ÷ pixels in either. 1.0 is identical.

## 2. The capture script: `tools/unity-capture.sh`

**Decided (U-010).**
- One editor script in `CriticalMass.Editor`, run by `tools/unity-capture.sh`, renders every framing of a row from the
  actual content of the commit being tested (the same prefabs, materials and `CM/Toon` the Mac build ships), not from a
  test-only copy.
- Each framing is rendered twice: the **colour capture** on a flat **Void Navy #18233B** ground, and the **ID mask** of
  the same framing.
- Each capture records the commit hash and the capture time (from the clock, UTC), so the board can show both and mark
  a row stale when `main` has moved past that commit.
- It is a gate for art items (U-011): it runs on this Mac.

**Decided (U-017): output.**

```
Logs/parity/<commit>/<row>/<framing>.png         colour capture on Void Navy
Logs/parity/<commit>/<row>/<framing>.mask.png    ID mask of the same framing
Logs/parity/<commit>/results.json                every row and framing with its check results
```

`Logs/` is git-ignored. **OPEN:** the script's command-line arguments (for example selecting one row or framing) and
the capture resolution are not ruled.

**Decided (U-017): camera and light.** The capture camera is orthographic and framed to match the bible crop for that
framing. One fixed key light, defined in a dedicated capture scene `Assets/_Game/Scenes/ParityCapture.unity`, is the
same for every row.

Note for the builder: the existing `tools/*.sh` scripts run Unity with `-nographics`, which gives no GPU device;
a capture script that renders cameras cannot use that flag.

## 3. The ID-mask method

**Decided.** The mask is a render of the same framing (same camera, same pose, same frame), not a guess from the
colour image. Only pixels inside the mask are measured. This replaces the browser's corner-mean mask (D-054) and
background-plate mask (D-066); the corner-mean mask measured the background, not the Warrior (harness post-mortem R18).

**OPEN:** whether the outline hull counts as asset pixels (the bible's figures include their painted contour, so it
matters for IoU), and how team-swap framings are rendered (one capture per team, or Azure only plus the bible's
accent-swap crop for Crimson).

**Decided (U-016): bible-side silhouettes.** One mask per bible crop, made once, stored in the repo under
`loop/parity-board/ref-masks/`, reviewed by eye before first use, never computed at gate time. (IoU needs a silhouette
on both sides, and a guessed mask is exactly the defect R18.)

**OPEN:** how the capture and crop silhouettes are aligned before IoU (crop and capture differ in size). Recommended:
scale both masks to the same bounding-box height and centre them on their bounding boxes.

## 4. The three measured checks (per framing)

| # | Check | Pass when |
| --- | --- | --- |
| 1 | **Colour** | Every named bible swatch for that asset, sampled inside the mask, is within **ΔE2000 10**. Sampling rule carried from D-054 (the recon lists D-054 as still applying): the swatch is compared to the closest colour cluster holding at least 0.5 % of the masked pixels. |
| 2 | **Shape** | Silhouette IoU with the bible crop is at least **0.85**. |
| 3 | **Required features** | Every item on the row's checklist (section 6) is confirmed present by an **Opus reviewer** looking at the colour capture, each with a one-line reason. One missing item fails the framing. |

All three passing on every framing of a row moves the row from **not ready** to **ready for the team**. Measured
checks never make a row pass (Wilson's rule, 2026-11-09).

## 5. The pass rule and the board

- A row **PASSES** only when **every framing** of the row is scored **9 or 10** by the team. No measured number
  replaces this. **Decided (U-018):** that means every rater who scored every framing gave 9 or 10, with at least two
  raters, one of them Wilson.
- The board shows, per framing, the bible crop and the Unity capture side by side, with the commit and the capture
  time, and marks the row **stale** when `main` has moved since its capture.
- Team raters on the browser board were Wilson, Jeisil, Aidan and Max, each scoring 1-10 plus Redo plus a one-line
  note. The board holds 4 team ratings, all on the browser demo: Max p04 = 1; Jeisil p04 = 4, p05 = 3, p10 = 3.
- **Decided (U-025):** board scoring is switched off per row until that row has a Unity capture (applied with the first
  Unity capture publish, not before).

The board was republished as v4 (version `1794453554-f2c8`, Unity plan on top, rows marked not started in Unity;
source in `loop/parity-board/`). **OPEN:** who publishes it after each capture run.

## 6. The rows

**OPEN: the framing list per row.** The lists below are the doc writer's reading of "the bible's own pose and angle":
every reference crop that shows the asset in a pose or from an angle is a framing; `*-callouts.png`, `*-details.png`
and `*-swatches.png` crops are reference only (detail sheets and colour sources). The architect confirms or edits this
list, including whether the p09 direction sheets are one framing per facing.

**Capture camera (U-017):** orthographic, framed to match each framing's bible crop, one fixed key light in
`ParityCapture.unity`. **OPEN:** how p04 (title screen), p12 (whole map) and p15 (HUD overlay) use the Void Navy
ground.

**Swatches.** The core palette (p05) is exact: D-064 confirmed the swatch pixels equal the printed hex. The per-sheet
swatches on pp 06-08 are **ESTIMATE**: the recon sampled them with a 12-colour quantise; the bible prints no hex on those
pages. Rows that have no swatch sheet of their own list the core palette colours their description names; that
mapping is the doc writer's reading. **Decided (U-021):** the missing swatches (Kaiju slate, moss, belly; HUD meter)
are re-sampled from the bible pages and marked "sampled, pending Jeisil's confirmation". **OPEN:** confirmation of each row's swatch list, and skin and hair tones.

Core palette: Void Navy **#18233B** · Azure Team **#16C7D9** · Crimson Team **#FF5A5F** · Warm Cream **#F7F0E5** ·
Biomass **#B7F238** · Corruption **#8E3FD1** · Stone **#6F7282** · Tool Brass **#C79245**.

### p03 Visual DNA (CM-41)

- **Framings:** `ref/03-moodboard.png` (arena hero), `ref/03-props-strip.png`, `ref/03-silhouettes.png`.
  Reference only: `ref/03-swatches.png`.
- **Required features:**
  - [ ] chunky, rounded, toy-like forms with strong negative space
  - [ ] cel finish with restrained highlights; no photorealism, no dense detail that disappears with 32 characters on screen
  - [ ] oversized tools, hands, shields, resources and Kaiju features
  - [ ] floating-ruin materials
  - [ ] team and objective states legible at gameplay zoom, reinforced with icons, glow patterns and silhouette changes, not colour alone
  - [ ] prop strip: biomass plant, blue crystals, corruption plant, stone/lava/biomass/corruption orbs, crate, stone block, brass orb, log
  - [ ] blue and red class silhouettes; Kaiju silhouette progression
- **Swatches:** the core palette. The six swatch bars on `03-swatches.png` have no hex in the recon (**OPEN**).

### p04 Logo (CM-42)

- **Framings:** `ref/04-primary-logo.png` (title screen, upper-left lockup), `ref/04-compact-badge.png` (favicon,
  loading tile, launcher icon). Reference only: `ref/04-single-color.png`, `ref/04-alt-lockup-a.png`,
  `ref/04-alt-lockup-b.png` (other directions on the sheet).
- **Required features (primary):**
  - [ ] exact spelling CRITICAL MASS, rebuilt as clean vector paths (D-057)
  - [ ] chunky two-line wordmark: "CRITICAL" cyan/azure with a darker teal bevel; "MASS" below, larger, coral to orange-red
  - [ ] thick dark navy outline on both words
  - [ ] dark faceted stone egg behind, glowing seams teal on the left and red on the right
  - [ ] floating grey rock chunks with green moss on top; blue crystals at left; magenta/red corruption plume at right; small waterfall under the base
- **Required features (compact badge):**
  - [ ] egg over a stacked wordmark with a cyan diamond gem, on a stone base
- **Swatches:** Void Navy #18233B (outline and ground; the browser failed this at ΔE 12.459). Azure #16C7D9 and Crimson
  #FF5A5F for the two words are the doc writer's reading (the bible describes "cyan/azure" and "coral-to-orange-red").

### p05 Colour and type (CM-43)

- **Framings:** `ref/05-*.png` (the palette crops; exact file names are not listed in the recon) and
  `ref/05-type-stack.png`. In the browser this row was measured in game: the Void Navy ground, Stone on the neutral
  hill, Crimson on the HUD, Tool Brass on the crown. **OPEN:** which in-game framings stand for this row in Unity.
- **Required features:**
  - [ ] all eight core colours used at their exact hex
  - [ ] BUNGEE for display: logo support, title screens, event cards, large heads, mostly uppercase
  - [ ] Nunito Sans for UI: semibold controls, bold labels, regular rules and help
  - [ ] JetBrains Mono for timers, cooldowns, counts, scores, debug
  - [ ] team, corruption and objective state never shown by colour alone
- **Swatches:** all eight core palette colours.

### p06 Warrior, CM-CHR-001 (CM-44)

- **Framings:** `ref/06-front.png` (306x326 px), `ref/06-three-quarter.png`, `ref/06-profile.png`, `ref/06-back.png`,
  `ref/06-pose-cleave.png`, `ref/06-pose-shield-charge.png`, `ref/06-front-shield.png`, `ref/06-accent-swap.png`
  (Crimson). Reference only: `ref/06-callouts.png` (face, pauldron, blade edge, shield core), `ref/06-swatches.png`.
- **Required features** (the first six are named in U-010; the rest are from the bible page):
  - [ ] round shield on the left arm (viewer's right in front view), torso to knee; the class identifier, never removed
  - [ ] stone cleaver: broad, chunky, chipped grey stone, glowing cyan edge, wrapped brown grip, held low in the right hand
  - [ ] dark-brown spiky hair pulled into a high topknot, with spiky bangs
  - [ ] brass ring tying the topknot
  - [ ] teal scarf wrapped at the neck
  - [ ] grey stone-slab armour: rounded chunky pauldrons with cyan crack inlays and brass ring rivets
  - [ ] teal tabard strip from the belt, front and back, zigzag hem
  - [ ] navy padded doublet, cream quilted undershirt, baggy cream trousers
  - [ ] brown leather belt with a large round brass buckle disc
  - [ ] brown leather bracers and gloves, grey stone knee guards, brown boots with grey toe caps
  - [ ] shield face: round brown wood planks, segmented grey stone rim, glowing cyan core in brass/navy rings
  - [ ] face: thick dark eyebrows, large brown eyes with white highlights, confident closed-mouth smirk, warm tan skin
  - [ ] silhouette: stocky, wide, bottom-heavy, slight crouch, very broad shoulder line
  - [ ] shield height 0.626 and width 0.359 of body height (measured, browser `ref-ratios.json`)
  - [ ] about 3 heads tall counting hair (**ESTIMATE**), big hands, thick short legs, oversized boots
  - [ ] Crimson swap: scarf/tabard, shield core and blade edge go coral/red; the rest stays
  - [ ] flat toon shading, no glossy or metallic response
- **Swatches (ESTIMATE, recon sampling):** Azure row navy #2A374D, teal #2A95AE, cream #F2E5D5, rust brown #AB6844,
  brass/tan #BB9358. Crimson row maroon #652D2F, coral #E46753, cream #F2E5D5, brown #7B5238, brass #C99C64.

### p07 Farmer, CM-CHR-002 (CM-45)

- **Framings:** `ref/07-front.png` (248x355), `ref/07-three-quarter.png`, `ref/07-profile.png`, `ref/07-back.png`,
  `ref/07-pose-harvest.png`, `ref/07-pose-root-seed.png`, `ref/07-staff-views.png`, `ref/07-backpack-tank.png`,
  `ref/07-front-staff.png`. Reference only: `ref/07-callouts.png`, `ref/07-swatches.png`.
- **Required features:**
  - [ ] glowing round backpack tank behind the right shoulder: round glass globe of acid-lime biomass, leaves sprouting from the top, brass ring frame, teal side canisters, dark hose to the staff
  - [ ] hooked staff rising above the head (staff height 0.972 of body, measured): dark-teal shaft with brass bands; brass hook with a cream/bone crescent blade; lime glowing bulb; a hose loop below
  - [ ] reads as a harvesting tool first and a seed launcher second
  - [ ] big wavy dark-brown hair in a high messy bun tied with a teal band
  - [ ] brass goggles with teal lenses pushed up on the forehead
  - [ ] big brown eyes, smile, rosy cheeks
  - [ ] cream short-sleeve work jacket with rolled sleeves over navy baggy overalls with grey knee patches
  - [ ] brown belt with pouches and a brass buckle
  - [ ] large brown work gloves with teal cuffs; brown boots with cream cuffs and teal trim
  - [ ] root-seed: a round wooden seed pod with lime leaves; impact is a vine burst
  - [ ] harvest pose: the staff hooks a lime biomass bush
  - [ ] narrower and taller silhouette than the Warrior; about 3.5 heads with hair (**ESTIMATE**)
  - [ ] biomass glow always acid lime; team ownership on teal or coral trim only
- **Swatches (ESTIMATE):** navy #2F3449, teal #4999A5, cream #F3E3C8, dark brown #70503D, brass #BB814A, lime #D0DE6A,
  coral #E9705F.

### p08 Miner, CM-CHR-003 (CM-46)

- **Framings:** `ref/08-front.png` (295x300), `ref/08-three-quarter.png`, `ref/08-profile.png`, `ref/08-back.png`,
  `ref/08-pose-wall-break.png`, `ref/08-pose-barricade.png`, `ref/08-pick-hammer.png`, `ref/08-front-pick.png`,
  `ref/08-barricade-folded.png`, `ref/08-barricade-deployed.png`, `ref/08-barricade-damaged.png`,
  `ref/08-accent-swap.png` (Crimson). Reference only: `ref/08-swatches.png`.
- **Required features:**
  - [ ] rounded tan/stone hard-hat helmet with brass trim and a large glowing blue crystal lamp on top-front (a tall crystal spike gives the top a point)
  - [ ] square wooden crate backpack: brass corners, X brace, stone side panels
  - [ ] widest, most compact body: barrel torso on short legs; about 3 heads (**ESTIMATE**)
  - [ ] thick dark-brown beard, heavy eyebrows, big round red nose, determined grin
  - [ ] navy shirt with a blue neckerchief
  - [ ] cream/stone chest plate in a brass frame with a blue hex crystal at centre
  - [ ] brass/brown shoulder straps with blue gem studs
  - [ ] brown belt with pouches, navy trousers, brass/brown knee guards, heavy brown boots with brass toe caps, huge dark-brown gloves
  - [ ] pick-hammer (height 0.633 of body, measured): curved two-point blue crystal pick head on a brass/brown banded shaft, which rotates and folds into a hammer with a stone block head set with a blue crystal
  - [ ] barricade: wooden plank fence between two grey stone posts with a blue crystal at centre; folded (compact upright crate), deployed (wide fence), damaged
  - [ ] Crimson swap: blue crystals and trim go red/orange
- **Swatches (ESTIMATE):** navy #152953 / #1B2D52, cream #E7DCD0, stone grey #827671, blue #2A8ECB, brown/brass #AF713A,
  red #EF674E, orange #FA743D.

### p09 Eight-direction read and animation set (CM-47)

- **Framings:** `ref/09-warrior-directions.png`, `ref/09-warrior-actions.png`, `ref/09-farmer-directions.png`,
  `ref/09-farmer-actions.png`, `ref/09-miner-directions.png`, `ref/09-miner-actions.png`. Each directions sheet shows 8
  facings; each actions sheet shows 3 action frames and an Azure/Crimson pair.
- Directions are **screen** directions (carried from D-054/D-066): S faces the camera and is the front view; world
  facing = -yaw + k × 45°.
- **Required features:**
  - [ ] all of N, NE, E, SE, S, SW, W, NW for each class
  - [ ] consistent feet, scale and shadows across facings; the small diamond ground-shadow marker under each figure
  - [ ] states built: idle, run, attack, ability, carry or interact, hit, knockout
  - [ ] backpacks, shields, tools and top surfaces readable under the high camera
  - [ ] each class keeps its p06/p07/p08 identifiers in every facing
- **Swatches:** the p06, p07 and p08 lists for each class.

### p10 Kaiju lifecycle, CM-KAI-001 (CM-48)

- **Framings:** `ref/10-egg.png`, `ref/10-hatchling.png`, `ref/10-azure-adult.png`, `ref/10-crimson-adult.png`,
  `ref/10-berserk-adult.png`, `ref/10-roar.png`, `ref/10-ground-slam.png`, `ref/10-telegraph.png`.
- **Required features:**
  - [ ] one recognizable anatomy across every outcome; allegiance changes only glow and crystal colour
  - [ ] egg: upright ovoid of dark slate faceted stone plates, glowing lime seams, soft lime ground glow; about 3x a character's height (**ESTIMATE**)
  - [ ] hatchling: small lizard/turtle-like quadruped, dark slate rock plates, moss tufts, lime crystal spikes on the back, cute small face with green-yellow eyes and an open smile, short tail; height 0.447 of the adult (D-058, measured)
  - [ ] Azure adult: huge knuckle-walking gorilla-weight quadruped; dark blue-grey slate boulder plates with glowing cyan cracks; lime-green moss on up-facing plates; tan segmented belly/chest plate; cyan crystal crown/spine; small head low between massive shoulders; yellow-green eyes, blunt snout
  - [ ] Crimson adult: same anatomy, orange-red crystals and glowing cracks
  - [ ] berserk adult: same anatomy, magenta/violet crystals, violet-pink fractured glow over darker plates, floating crystal shards, rearing angry pose with roaring open mouth
  - [ ] roar: head up, mouth open, cyan shock rings
  - [ ] ground slam: fists smash down, rock debris, cyan shock
  - [ ] telegraph: red circular ground marker with concentric rings, red crystal spikes around the rim, cracked red tiles
  - [ ] adult roughly 8-9x the character figures on the sheet (**ESTIMATE**; the browser used 2.5x, kaijuHeight 5.0 vs characterHeight 2.0)
- **Swatches:** Biomass #B7F238 (egg seams, hatchling spikes), Azure #16C7D9 (Azure cracks and crown), Crimson #FF5A5F
  (Crimson crystals), Corruption #8E3FD1 (berserk). Slate body, moss and tan belly: to be re-sampled from the bible
  page, "sampled, pending Jeisil's confirmation" (U-021; the browser's slate check failed at ΔE 16.9-18.8).

### p11 Adult Kaiju turnaround, CM-KAI-002 (CM-49)

- **Framings:** `ref/11-front.png`, `ref/11-three-quarter.png`, `ref/11-profile.png`, `ref/11-rear.png`,
  `ref/11-idle.png`, `ref/11-walk.png`, `ref/11-roar.png`, `ref/11-slam-windup.png`. Reference only: `ref/11-details.png`.
- **Required features:**
  - [ ] heavy quadruped with gorilla-like weight transfer, not a fast dinosaur
  - [ ] shoulders far wider than hips (the browser's CM-69 reading: about 1.5x); forelimbs longer than the hind legs
  - [ ] long forelimbs ending in boulder fists planted on the ground
  - [ ] short hind legs with stubby rock toes and moss
  - [ ] head low and forward between the shoulders
  - [ ] rear: cyan crystal ridge down the spine onto a short tail with a crystal tip
  - [ ] small face: yellow eyes with black pupils, flat snout, smug mouth; powerful, readable, slightly comedic, never horrific
  - [ ] rock shell plates with moss and cyan seams; a cyan-to-lime crystal cluster
- **Swatches:** Azure #16C7D9, Biomass #B7F238; slate, moss: re-sampled as p10 (U-021).

### p12 Skybreak Basin map (CM-50)

- **Framings:** `ref/12-map-a-skybreak.png`. `ref/12-map-b-ember.png` and `ref/12-map-c-tidal.png` become framings when
  CM-68 (maps B and C) lands.
- **Required features** (the recon marks this whole description as its own visual read):
  - [ ] a portrait floating-island cluster in a cloud sea
  - [ ] north: the Kaiju shrine, an egg between two curved stone horns with a teal glow
  - [ ] centre: a circular hill plaza with a crown emblem, its ring split blue/red
  - [ ] west: the Azure pad with blue banners and blue crystals; east: the Crimson pad with red banners and red crystals
  - [ ] south: a blue-crystal plaza with blue banners (**OPEN:** the recon could not tell whether this is a base or a respawn shrine)
  - [ ] stone-paved roads; wooden rope bridges between islands, several diagonal; waterfalls off island edges
  - [ ] round green trees; blue/red crystal clusters along routes
- **Swatches:** Azure #16C7D9, Crimson #FF5A5F, Stone #6F7282 (doc writer's reading).

### p13 Spatial grammar (CM-50)

- **Framings:** `ref/13-spatial-grammar.png` is text only; there is no picture to overlap. **Decided (U-019):** this row
  is measured by Sim timing tests (base to hill 12–18 s, shrine detour 8–12 s at default pace) plus team review. Colour
  and IoU do not apply.
- **Required features (bible p13, verbatim list and blockout targets):**
  - [ ] opposing protected bases at clear ends
  - [ ] one central circular hill with at least 3 approach routes
  - [ ] Kaiju shrine in a distant district or corner
  - [ ] biomass off the shortest hill route
  - [ ] breakable Miner shortcuts never the only path
  - [ ] safe readable boundaries, no accidental fall deaths
  - [ ] strong oblique landmarks
  - [ ] a Kaiju path wide enough for telegraphs
  - [ ] 12-18 s base-to-hill run; shrine detour 8-12 s
  - [ ] free N/S/E/W and diagonal traversal on one connected plane
  - [ ] biomass pulls 3-5 bots, not half the team
  - [ ] fixed elevated three-quarter camera, no normal-play rotation
  - [ ] depth sort around trees, walls, buildings and props
- **Swatches:** none.

### p14 Props and interactables (CM-51)

- **Framings (22):** `ref/14-biomass-plants.png`, `ref/14-loose-orb.png`, `ref/14-carried-bundle.png`,
  `ref/14-corruption-growth.png`, `ref/14-shrine-azure.png`, `ref/14-shrine-crimson.png`, `ref/14-hill-neutral.png`,
  `ref/14-hill-azure.png`, `ref/14-hill-crimson.png`, `ref/14-wall-intact.png`, `ref/14-wall-cracked.png`,
  `ref/14-wall-destroyed.png`, `ref/14-barricade-folded.png`, `ref/14-barricade-deployed.png`,
  `ref/14-barricade-damaged.png`, `ref/14-banner-azure.png`, `ref/14-banner-crimson.png`, `ref/14-respawn-pad.png`,
  `ref/14-bridge.png`, `ref/14-ramp.png`, `ref/14-markers.png`, `ref/14-tiles.png`.
- **Required features:**
  - [ ] biomass plants, small/medium/large: bulbous lime blobs on dark-teal stalks
  - [ ] loose orb: a lime glass sphere; carried bundle: a lime orb in a wooden/brass frame with straps
  - [ ] corruption growth: magenta/violet flame-shaped crystal plants on dark rock
  - [ ] shrine: a faceted egg between two stone horn arches on a stone dais with a waterfall; Azure variant cyan seams and beam, Crimson variant red
  - [ ] hills: a stone ring dais between two curved stone horns; neutral grey, Azure with cyan rim, beam and crystals, Crimson with red
  - [ ] walls: mossy ashlar stone, intact, cracked and destroyed (rubble)
  - [ ] barricade: wood planks and stone posts with blue cloth, folded, deployed and damaged
  - [ ] banners: blue or red pennant with a white shield/sword emblem on a wooden T-post with a stone base
  - [ ] respawn pad: a stone ring with a cyan cross glyph and blue crystals
  - [ ] bridge: wooden plank rope bridge between mossy stone piers; ramp: a stone slab ramp
  - [ ] markers: cyan, grey and red diamond objective pointers over rings
  - [ ] tiles: stone flagstone, mossy stone, dirt/grass, corrupted violet-cracked tile
  - [ ] every damage state is its own authored model, no procedural destruction (bible: "Avoid procedural destruction")
- **Swatches:** Biomass #B7F238, Corruption #8E3FD1, Stone #6F7282 (the browser's neutral hill failed at ΔE 18.7),
  Azure #16C7D9, Crimson #FF5A5F, Tool Brass #C79245 (doc writer's reading).

### p15 HUD (CM-52)

- **Framings:** `ref/15-hud-full.png` (934x530), `ref/15-hud-match.png`, `ref/15-hud-player.png`,
  `ref/15-hud-minimap.png`.
- **Required features:**
  - [ ] priority: win condition first, Kaiju state second, personal combat state third
  - [ ] 01 MATCH: top-centre navy pill clock (mm:ss); Azure panel on its left (shield emblem, blue pip row, score); Crimson panel on its right (score, red pips, crimson shield); under the clock a hill-control bar, half blue half red, with a crown medallion
  - [ ] 02 KAIJU: top-right egg portrait in a dark frame, a segmented orange meter, small triangle pips (allegiance, instability, hatch countdown)
  - [ ] 03 PLAYER: bottom-left portrait in a frame, 4 cyan hearts above a cyan health bar, three rounded navy ability buttons with cyan icons and their button labels
  - [ ] 04 WORLD: objective arrows, contested state, class VFX, a crown marker over the hill, team banners
  - [ ] minimap bottom-right in a navy frame: egg at top, blue shield left, red shield right, crown at centre, dashed blue/red routes
  - [ ] fonts per p05 (JetBrains Mono for the clock, scores and counts)
- **Decided (U-020):** the HUD shows the game's real bindings (the U-006 controls table), not the bible's placeholder
  "LB", "RB" and "Y" labels. The feature check is "button labels match the controls table".
- **Swatches:** Void Navy #18233B, Warm Cream #F7F0E5, Azure #16C7D9, Crimson #FF5A5F (also the contested-hill stripe),
  Tool Brass #C79245. The orange meter: re-sampled from the bible page, "sampled, pending Jeisil's confirmation" (U-021).

## 7. Order

Rows are built and gated in the U-012 order: p05, p06, p07, p08, p09, p10 and p11, p12 and p13, p14, p15, p04, p03.
