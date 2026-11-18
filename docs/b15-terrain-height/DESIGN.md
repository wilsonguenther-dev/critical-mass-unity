# b15 terrain height: design (feat/b15-terrain-height)

Owner ruling: "do it" (add HEIGHT to terrain edits). Goals: (1) blocks STACK; (2) characters walk DOWN INTO a dug pit and
back out, instead of the pit pushing them away like a wall. One switch: `DepthConfig.TerrainHeight.Enabled`
(`Sim/Config/TerrainHeightConfig.cs`, every number of the rule lives there). Full rules only (`DepthRules.On`), so Classic and
PhysicsOnly never allocate the layer and their checksum rows cannot move.

## How it works today (read before designing)
- Cells are one byte type (`Cell`). Walkable = Floor, BreakableDestroyed, Bridge. A dug cell and a TNT crater both become
  `Cell.Void`: non-walkable, so the box collider pushes everyone out of a pit. A Builder BLOCK on ground is a `Cell.LowWall`
  barricade slot (60 HP, crumbles after 45 s, jump clears it); a BLOCK aimed at Void FILLS it to Floor (`Terraform.CommitFill`).
- Dig / place (`Terraform.cs`, F, owned by the Builder in Full): refused on keep-outs, occupied cells, the route guard
  (`CellsKeepRoutes`: both pads reach the hill, no new shrine cut, b14-c Kaiju route), and the terrain rate cap.
- Ladders (`BuiltPieces`) turn Wall cells into Bridge cells; bridges span Void / Water. Jump: 18 air ticks, 0.8 u apex, airborne
  bodies pass over in-map Void and LowWall. Dodge sweeps along the ground (`Collide.SweepActor`, not airborne).
- The island's elevation (hill, cliffs, ramps) is `MapDef.Height01`, VIEW ONLY. The sim is flat.
- Movement: `Collide.SweepActor` axis-separated box push-out against "blocked" cells. Bots: Dijkstra fields
  (`FlowField`, `BotFields`), sampled cell to cell. Kaiju: its own clearance grid (`nav.Clear`, a separate MapDef) and field.
- TNT (`Demolitions.BlowBomb`): breakables destroyed, Floor/Bridge cells in CarveRadius go Void through the dig rules.
- View: `GridChunkMeshView` draws cell boxes at Height01; `TerrainEditMesh` draws edited cells (pits 0.9/1.35/1.8 u by ring,
  placed/filled earth blocks, seamed short-wall stones); `TerrainEdits` tracks which cells were touched (the dresser drops
  its ground art there). Actors stand at `GroundHeight.At` = Height01.

## The model (smallest deterministic one)
- `MapDef.Lift` (sbyte per cell, null unless the rule is on): an integer height offset on top of the island elevation.
  Only Floor and BreakableDestroyed cells carry a lift; every other cell keeps 0. Range -2 (MaxDepth) to +3 (MaxStack).
  Integer maths only; one step is 0.45 u in the view (`StepU`, the existing PitDepthStep). No floats, no new RNG.
- An actor's LEVEL = the highest lift among the walkable cells its square touches (`TerrainHeight.LevelAt`). So nobody is
  ever inside a cell that is "too high" for it (the push-out never meets a blocking cell it already overlaps).
- Grounded walking: a cell blocks when its lift is 2 or more above the walker's level. Down any amount is allowed.
  1 step either way = a short hop with a small slow (6 ticks at 0.7x). Drop of 2+ = 12 ticks at 0.5x. No fall damage.
- 2 steps up: JUMP (airborne may rise 2), or CLIMB: keep walking into a 2-step ledge for 15 ticks (0.5 s) and you climb it
  (10 ticks at 0.6x after), or a ladder, or a dug ramp (a -1 cell). 3+ steps up is a wall for everyone but the Kaiju.
- Bridge cells (ladder rungs, bridge decks) are ramps: never blocked by height.
- So: max stack 3 (a wall), max pit depth 2 (a trap you climb out of in 0.5 s, never a prison).

## Rules
- STACK (who: the Builder, F with BLOCK costs BuildConfig.CostBlock wood; PLACE spends a carried dirt block as today):
  on a Floor / BreakableDestroyed cell with lift -2..+2 the lift goes up by one (a pit fills one level first). Refused:
  "Too high - 3 max" at +3; "Someone is standing there" (BuilderWalls.CellRefusal clearance); keep-outs (pads + spawn,
  hill inner 3 u, shrine, nodes and veins, the station/hut zone); bridges, walls, void ("No ground"); the route guard when
  the new step would cut a route; the terrain rate cap ("Wait"). A BLOCK aimed at real Void still fills it (unchanged).
- DIG (F with DIG): a raised cell loses its top level (allowed with someone on it: they drop one level), the digger carries
  one block; ground deepens 0 -> -1 -> -2 ("Too deep - 2 max"), refused when occupied, on keep-outs, by the route guard and
  the rate cap. Bridge cells keep today's rule (the deck breaks to Void).
- Removing a tower: dig the top block first, level by level. TNT: every raised cell in CarveRadius loses 2 levels (people
  on it drop); cells at 0 or below keep today's crater rule (Void, through the dig rules); a pit that is cratered resets to 0.
- WALL pieces stand only on level-0 ground (keeps the barricade slots and lifts apart; "Level ground only").
- HEIGHT LADDERS (coordinator addition 2026-11-17, owner-approved): a Builder whose LADDER faces a lift cell 2+ steps above
  his own cell lays a 1-cell ladder ON it: (a) a pit wall (the ledge above a 2-deep pit), (b) the side of a 2-3 high stack
  or tower. Same tool wheel slot, same cost and limits; the ghost and the model stand flat against the face. The cell
  becomes a Bridge (a ramp) and keeps its lift, so climbing it puts you on the ledge / the stack top. The ladder comes down
  with the block it leans on: DIG on it (ladder down, one level off), TNT (ladder down, 2 levels off), the Kaiju (ladder
  down, smashed to +1). When it crumbles the ground comes back with its lift. Path edge onto a ladder: +PathCostLadder (1.0);
  bots use them through their fields. Rock-wall ladders and bridges are unchanged.
- (c) NATURAL CLIFFS: the island's elevation is view-only in the sim (Height01), and on Skybreak Basin every island is at
  0 / 0.75 / 1.5 u, separated by void and bridges, so no two walkable cells differ by 2 steps; a ladder on a natural face
  would change nothing in the sim. The census test prints the count; making natural cliffs sim height is a separate call.
- Prompt for a ladder with nowhere to go: "face a wall, a ledge or a stack" (PR #210's "face a rock wall" is rewritten to
  this when it lands).
- Kaiju: ignores height (it is 3 m tall); any cell at +2 or more under its body is smashed to +1 at once (one Terraform
  event). Its clearance grid and route guard are unchanged; digging cannot trap it any more because pits are floor.
- Route guard: one tentative lift write, then the existing proofs (pads reach the hill, no new shrine cut) on fields that
  respect height. Only a change that creates a 3-step edge can cut a route, so other edits skip the proof.

## Bots
- Fields (FlowField, BotFields) and their samplers: an edge costs +0.5 for 1 step, +3 for 2 steps (a climb), and is closed
  for 3+; no diagonal across any height change. Bots climb by walking into the ledge (the climb rule), so a bot that
  falls into a 2-deep pit always gets out; they prefer flat routes. Bot Builders keep their three rules (rule 3 still fills
  Void craters). Kaiju: unchanged nav plus the smash.

## Hits, sight, view
- Projectiles, throws, melee and line of sight ignore height in v1 (stated limitation; a +3 tower is not cover yet).
  Fog is 2D and unchanged.
- Actors stand at Height01 + level x 0.45 (`GroundHeight.Body`). Pits draw at true depth (lift x 0.45: 0.45 / 0.9 u),
  stacks as seamed stone courses with a cap, one course per level. An actor below ground level shows a team-coloured head
  pip above the pit rim (readable over pit walls). The BLOCK ghost stands at the resulting height; the prompt reads
  "STACK x2" / "x3" and the refusal reasons above.

## State, netcode, replays
- `MapDef.Lift` plus per-actor level / slow / climb counters (`TerrainHeightState` on DepthState) are sim state; reset with the
  world. StateHash and the tests' MatchHash mix the lift layer only when it exists (Classic and PhysicsOnly bit-identical).
- ConfigHash picks the new config up by reflection (a b15 build cannot share a room with an older one: expected).
- `SimVersion.Schema` 1 -> 2: older replays show "from a different game version" (the file format itself is unchanged).

## Cost
- 0 B per tick (fixed arrays at world creation). Per actor per tick: one 2x2 lift read for the level. Fields: one extra
  array read per edge when the layer exists. Route proofs only on a 3-step edge. View: lifted cells reuse TerrainEditMesh.
