# Skybreak Basin environment kit, v2

A modular kit that tiles on the game's 1 m cell grid (F-03), built by script in headless Blender 5.2.3 for the
perspective gameplay camera (pitch 55, vertical FOV 35, 23.4 u, yaw 45; ART-DIRECTION-2026-11-12 sections 2 and 5,
brief A-05). Scope: ground, ledges, cliffs, water, paths, bridges, trees, rocks, crystals, small field dressing, sky
rocks and clouds, ruins. NOT bases, walls, towers, hill or shrine structures (bases-and-structures agent): their v1
FBX files are left untouched and only stand in for those structures in the renders.

## Rebuild (from the repo root; exit 0, about 20 s in total)

```
B=/Applications/Blender.app/Contents/MacOS/Blender
D="Assets/_Game/Art/Environment/_source~"
$B -b --factory-startup --python-exit-code 1 -P "$D/build_env_kit.py"                  # ../env_*.fbx, env_kit.blend, manifest
$B -b --factory-startup --python-exit-code 1 -P "$D/render_v2.py" -- <out> [--scale 2]  # assembly, checks.json, renders/*.png
python3 "$D/compose_v2.py" <out>/renders <report_dir>                                   # 1080p + 2x-crop JPEGs, sbs-*.jpg
```

`render_v2.py ... --no-render` runs the assembly and checks only; `--only name1,name2` renders a subset.

| File | What |
| --- | --- |
| `env_art/kit2.py` | every v2 piece and the registry (`PIECES`: kind, pivot, outline px, FBX material slots). `RETIRED` lists v1 FBX files the build deletes |
| `env_art/layout2.py` | placement rules from the Sim grid (rim fills, rim/ledge edges, runs, skirts, corners, waterfalls, decor, bridges) and the ground field bake; pure Python (numpy only in `fields`) so it ports to C# |
| `env_art/geo.py` | Unity-space mesh builder. v2: only downward faces (normal y < -0.5) are dropped, so every piece may be yawed; material index and UV per face |
| `env_art/pieces.py`, `env_art/layout.py` | v1 (structures, ruins, clouds reused; `layout.py` is still the 1:1 port of `src/sim/map.ts`) |
| `env_art/palette.py` | environment palette (v2 entries added: grass tones, seam, ink, band, foam, water, crystal colours); outline is now Void Navy #18233B |
| `render_v2.py`, `compose_v2.py` | Blender preview materials (3-band CM/Toon, CM_Ground), checks, renders, side-by-sides |

Export recipe unchanged (FBX, `axis_forward=-Z`, `axis_up=Y`, `bake_space_transform`, sRGB vertex colours in `Col`,
alpha = mask code; 0.25 = fixed-colour self-lit: ink, face band, foam, water, Biomass/Corruption crystal facets).
Material SLOTS now carry meaning: `CM_Toon` (vertex colour, 3-band), `CM_Ground` (world-space ground shader),
`CM_GlowDecal` (crystal ground glow, UV radial, additive/alpha), `CM_ShadowDecal` (ledge shadow, alpha = 1 - UV.v).

## How the Unity side places v2 pieces (`layout2.place_map` is the reference)

Positions are SIM coordinates; Unity = `SimSpace.ToWorld(x, y, z) = (x, y, -z)`; `yaw` is Unity Euler Y.

1. **Ground.** `env_ground_cell` on every island cell at its height (no rotation). Pieces carry height and material only.
2. **Rim fills.** A VOID cell whose land neighbours sit on exactly one corner (two adjacent sides and the diagonal, equal
   heights) gets `env_ground_fill_tri` at its centre, yaw = atan2(dz, dx) - 45 toward that corner. This turns a
   45-degree staircase into a straight diagonal rim. Fills are never on walkable cells; trees, boulders and crystals
   live on them.
3. **Edges.** Every exposed side of the land + fill outline is a unit edge (1 u axis or 1.414 u diagonal), oriented so
   the outward normal is (-dz, dx). Each gets a lip (`env_lip_straight_a/b/c`, `env_lip_diag_a/b`, hash pick) at its
   start point, yaw = atan2(dz, dx). A side facing LOWER land is a ledge: lip + `env_ledge_band` per extra 0.75 step +
   `env_ledge_shadow`. Convex 90-degree corners get `env_lip_corner`.
4. **Cliffs.** Chain rim edges into loops and straight runs; cut each run into `env_cliff_s3..s6` (remainders s1, s2) or
   `env_cliff_d2..d4` (d1) by hash, each at its first edge's start point and yaw. Convex corners get
   `env_cliff_corner`, 45-degree bends `env_cliff_bend`. Re-run for the neighbourhood of a dug cell.
5. **Waterfalls.** On straight rim edges facing south or east (the camera side), not at bridge mouths, by anchor list
   plus a 6% hash: `env_waterfall` at the edge midpoint, yaw of the edge. No decor on that cell.
6. **Bridges.** Per span (v1 `all_spans`), tile `env_bridge_span` (2.04 u: 6 planks, posts and ropes) n = round(len /
   2.04) times, stretched along the deck; `env_bridge_post` pair at the far end; `env_bridge_pier` beside each mouth on
   a non-walkable cell. Stair-step cells not under a span get `env_bridge_cell`.
7. **Decor** (deterministic `h32`): edge trees on rim cells (11%) and fills (32%); boulders and rock clusters on fills
   only; crystals on fills near routes (team by side) or near Biomass/Corruption sites; small props (pebbles, tufts,
   flowers, stumps, low bush; all under 0.3 u) on rim cells, path sides and grass. Keep-outs: hill 6 u (props allowed
   6.6 to 9.8 u inside the ring), shrine yard, pads.
8. **Ground field texture** (`layout2.fields`, 4 texels/u, RGBA half): R = path distance, G = distance to rims and hub
   discs, B = dirt mask (0.4 u soft edge), A = ledge shadow. Rebake when the grid changes (dig/place).

### CM_Ground (the shader to write in URP; `render_v2.ground_mat` is the Blender preview of it)

World-space xz, domain-warped by 2D noise (scale 0.55, amplitude +-0.14 u). Grass: Voronoi F1 at scale 0.4 (mean cell
2.5 u), cell hash -> #ACCB56 / #A3C150 / #96B249, distance-to-edge < 0.008 u -> seam #7E9A3F (use fwidth for a constant
1 px in Unity). Value x (1 + 0.04 sin(2 pi G / 8 + 6 noise(0.07))). Dirt mix by B, ledge shadow mix 55% A toward
#6F9A3E. Paving: path half-width w = 1.6 + 0.6 noise(0.18); flagstone Voronoi scale 1.0 is paved when the field R at
the CELL'S feature point < w - 0.5 (and a 0.33-scale noise < 0.6, so grass breaks through), tones #E6C597 / #D8B78C /
#EBD2A6, 5% of cells dirt, grout #A08A68; kerb Voronoi scale 2.0 where w - 0.5 < R(feature) < w. Lighting: flat, cast
shadows only (shadow = colour x 0.66 mixed 12% toward Void Navy).

## Checks (`render_v2.py`, results in `checks.json`)

Tiling: 5 down-rays per land cell into the ground (holes, height mismatch > 0.12), bridge cells onto decks, every rim
unit edge has a lip and exactly one skirt over it. Pixel measures at 1080p for each gameplay framing: character height,
ink width of central lips, foam band width at every waterfall in frame, props and trees/big rocks in frame. Nothing
over 0.3 u on walkable cells except edge trees. Grass-cell statistics on a reference jittered Voronoi. Boulder facets.

## Not done / known gaps

- Unity side: no CM_Ground or 3-band CM/Toon shader changes, no constant-pixel outline, no placement code, no `.meta`
  files; nothing imported into Unity (FBX verified only by the shared export recipe).
- Ledges inside an island are not smoothed (axis staircase); only rims get fill triangles.
- 55% of rim length is in 1-2 u skirt pieces (ellipse staircases make short runs); facets join flush, but bends add
  columns.
- Bridge rails and end posts (0.8 u) stand on walkable bridge-edge cells (22 posts): a declared exception.
- 4 of 578 bridge cells miss the deck within 0.2 u (deck edge at chipped planks on diagonal spans).
- Fog, foam drift and waterfall scroll are shader work (UVs are in the meshes; v = metres along the fall).
