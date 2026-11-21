# Critical Mass physics audit (2026-11-21)

Build: released 1.0.1356 (integration/batch-15-cand1 aa2ecfef), Unity 6000.3.28f1, run in ~/cm-editor with the Full rules
(GameConfig.CreateDefault). Every number below comes from a script in branch audit/physics-2026-11-21, tools/physics-audit/
(PA.cs headless sim harness, PV.cs Play-mode staging, trap-pose.cs). Raw output is in data/ and poses/. Code paths are under
~/cm-editor/Assets/_Game/Sim/ unless another folder is named. 1 u = 1 cell, 30 ticks a second.

Contact sheet (the six most telling pictures): contact-sheet.png

## Ranked top 15

| # | What is wrong | Number | Who notices | Sev | Fix | Size | View/Sim |
|---|---|---|---|---|---|---|---|
| 1 | A Kaiju that pins a fighter in a dead end teleports the fighter through its own body | 2.71 u jump in one tick (x 57.55 to 54.84, tick 32 to 33), 2-cell corridor | everyone | High | Depenetrate only along the free side (wall-aware ring search), never across the Kaiju centre; if no free spot, push the Kaiju back instead | M | Sim, re-pin |
| 2 | TNT does not move anyone | 0.00 to 0.20 u shove and no stun at 0.4 to 2.4 u; 0 blast knocks, 40 to 54 blast kills per 8-min match | new players (explosions feel weightless) | High | Give the blast its own flag (BlastKnock) separate from Physics.Knockback, tune with the existing BlastKnockCentre 1.8 | S | Sim, re-pin |
| 3 | Bots shiver in fights | about 123 shiver flags per match (739 over 6 matches), median swing 1.09 u, max 5.96 u; 642 of 739 within 13 u of the hill; 2 of 17 MovementShiverTests red | everyone watching a fight | High | Commit time on bot strafe and target choice (hysteresis), damp Fight Dither and Reversals | M | Sim (bot AI), re-pin |
| 4 | Grazing a block corner stops you dead | stop at 0.15, 0.3 and 0.4 u onto a pillar side; 0.05 u passes but 37 ticks against 24 | new players | High | Raise CornerNudgeMax from 0.1 to about 0.35 and scale the nudge to the overlap | S | Sim, re-pin |
| 5 | A 1-cell gap jams a crowd | 2 of 8 bodies through in 20 s (6 stuck, overlap up to 0.22 u); 2-cell gap passes all 8 in 2.6 s | everyone at built walls | Med-High | Let a body wedged at a 1-cell gap shrink its footprint for the pass, or yield by arrival order; cheaper option: builders cannot leave 1-cell gaps | M (S as a build rule) | Sim, re-pin |
| 6 | Teammates fuse into each other in crowds | worst overlap 0.867 u; 4.3% of contact pair-ticks over 0.05 u, mostly ally pairs | everyone (two models in one) | Med | Floor the ally push (AllyPushShare 0.5 to 0.8 above 0.3 u overlap), keep AllyPassOpposing only for retreaters | S | Sim, re-pin |
| 7 | The Kaiju lurches when a crowd shoves it | 63 ticks over 3.2 u/s, one at 16 u/s (0.53 u in one tick) near the hill | everyone | Med | Cap ShoveKaiju per tick (about 0.12 u) and carry the rest over | S | Sim, re-pin |
| 8 | The Kaiju's sim facing flips instantly | up to 179 degrees in one tick; 73 turns over 45 and 28 over 90 degrees in one tick (seed 7); the view hides it but swings aim from the sim facing | skilled players | Med | Turn-rate cap on the sim facing (for example 360 deg/s) | S | Sim, re-pin |
| 9 | Kaiju body is much smaller than its drawing | collision radius 1.3 u; canopy drawn about 3 u wide; fighters at exact contact stand under it and are hidden | everyone | Med | Dither or fade the canopy over fighters (view), or grow the body to 1.8 u (sim) | S view / M sim | View (or Sim) |
| 10 | Fighter facing pops on direction changes | facing snaps in 1 tick and the view applies it raw | everyone, mildly | Med-Low | Spring the fighter yaw in the view like KaijuAnimator (about 25 ms) | S | View |
| 11 | Jump at the island edge is silently eaten | air time 0, no sound or puff; a standing jump onto a void landing is refused | new players | Low-Med | Play a refused-jump cue (small dust and a tiny hop pose) | S | View |
| 12 | Sliding along a wall loses speed | wall slide speed = cos of the input angle (45 degrees: 71%) | skilled players | Low-Med | Keep at least 85% of speed along a wall when input is within 60 degrees of it | S | Sim, re-pin |
| 13 | Falling costs nothing | fall: 4 ticks, 2.0 u teleport to safe floor, 0.77 s stand-up, 0 HP; edge knocks and dodges are held, so no ring-outs | skilled players | Low (design) | Owner decision: small fall damage (5 to 10%) or a longer stand-up; keep no ring-outs | S | Sim, re-pin |
| 14 | Ladder foot float | 0.07 u above the deck at the ladder foot (after the b15 smoothstep fix, 0.28 to 0.07); not re-measured today | nobody at the game camera | Low | Clamp the last 0.1 u of the climb profile to the deck | S | View |
| 15 | LowWall and Barricade health is not tracked after TNT | WallHp reads 0 for those cells while the blast still carves them | nobody (tests and tools) | Low | Track or document; tests that read WallHp should skip those types | S | Sim bookkeeping |

Not counted as defects after checking: the camera "0.5 u jumps" seen in early samples were my sampling (read before
LateUpdate after a hitch frame). Aside, not physics: dark slabs under some staged fighters in p1-ally-overlap-*.png, cause not
traced.

## Three best-feeling things

1. Movement answers at once: full speed in 4 ticks (0.13 s), stop in 3 ticks within 0.1 u, reverse in 6 ticks, 0 ticks from
   input to motion in the sim, the drawn body only 0.063 u behind on average, steady drawn speed (median 3.8, p5 3.69 u/s).
2. Walls are solid and paths are clean: no tunnelling (dodge and a 60 u/s knock stop at the wall), 0 bridge-mouth snags in
   420 player walks over 16 spans, no ring-outs.
3. The sim is deterministic and cheap: same seed twice gives the same hash (4BF26EE2792815B2); Windows host and Mac join agree on
   63 fingerprints over ticks 0 to 1830; movement plus separation 230 us a tick with 0 B allocated.

## Missing features: add now or not

| Feature | Case | Add now? | View/Sim |
|---|---|---|---|
| Blast shove (item 2) | TNT is the loudest event and moves nobody | Yes | Sim, re-pin |
| Corner rounding at grazes (item 4) | stops the most common snag | Yes | Sim, re-pin |
| Kaiju turn-rate cap and shove cap (items 7, 8) | removes lurch and aim flips in one small change | Yes | Sim, re-pin |
| Fighter yaw smoothing (item 10) | cheap polish, no risk | Yes | View |
| Refused-jump cue (item 11) | tells a new player why nothing happened | Yes | View |
| Doorway flow assist (item 5) | built walls make 1-cell gaps all match | Yes (or the build rule) | Sim, re-pin |
| Push priority by mass (SeparationByMass=false) | Warriors would hold doors; may make jams worse | Later, after item 5 | Sim, re-pin |
| Death fling or ragdoll (only a death pop now) | makes kills read in a crowd | Later | View |
| Knock-into-wall bonus | skill reward, but a new rule to teach | No | Sim |
| Air control | lockstep jumps read better as commitments | No | Sim |
| Faster fall than rise in the sim | the view already reshapes the arc | No | View already |
| Carry slowdown | no carry slowdown measured; adds a rule nobody asked for | No | Sim |
| Fall damage or ring-outs | owner decision (item 13) | Ask the owner | Sim |
| Camera shake | off by owner decision OD-6 | No | View |

## 1. Movement (data/1-movement.txt)

- Top speed: Warrior 3.8, Farmer 4.2, Miner, Demolitions and Builder 4.0, Healer 4.1 u/s.
- 4 ticks to top speed; stop in 3 ticks within 0.1 u; reverse to -90% in 6 ticks; a 90 degree turn in 5 ticks.
- Diagonal speed is normalised; stick input scales speed. Mass does not change acceleration (accel 0.12 s, brake 0.08 s for all).
- Facing snaps in 1 tick (Movement.cs:131-132) and the view draws it raw (Rendering/ActorViews.cs:380-396). Item 10.
- Input lag: the first tick already moves 0.035 u. The view draws Lerp(prev, now, alpha): mean 0.063 u, max 0.127 u behind
  (data/view-lag.csv). Camera: critically damped spring, omega 9, look-ahead 0.25 s.

## 2. Collision (data/2-collision.txt, data/crowd-seed7.txt, data/shiver-*.txt)

- Two overlapping bodies separate in 2 ticks. Head-on bodies slide past (ally overlap 0.036 u).
- Doorway with 8 bodies: 1 cell: 2 of 8 through in 20 s, max overlap 0.22 u (item 5, p2-doorway-jam.png). 2 cells: all 8 in
  2.6 s. 3 cells: all 8 in 2.3 s.
- Wall slide: pure projection, speed along the wall = cos(angle) (Collide.cs:341 SweepActor). Item 12.
- Convex wall end: clean. Pillar graze: 0.05 u passes slowly (37 against 24 ticks); 0.15, 0.3, 0.4 u stop dead. CornerNudge
  (Collide.cs:310) is capped by CornerNudgeMax 0.1 (Sim/Config/PhysicsConfig.cs:201). Item 4, p3-pillar-snag.png.
- Tunnelling: none. A dodge and a 60 u/s knock both stop at the wall (substeps at most 0.4 u). A jump clears a LowWall only.
- Bridge mouths, player walking: 0 snags in 420 walks over 16 spans.
- 16v16 crowd, seed 7, 3600 ticks: up to 20 bodies in the hill disc; worst overlap 0.867 u; 4.3% of contact pair-ticks over
  0.05 u. Overlap counts per 0.1 u band above 0.05 u: 449, 179, 60, 33, 14, 19, 11, 2, 2. Deep pairs are mostly allies
  (AllyPushShare 0.5 at Collide.cs:701, AllyPassOpposing at Collide.cs:703; config PhysicsConfig.cs:188 and 194). Item 6,
  p1-ally-overlap-mid.png (Farmer and Builder 0.71 u inside one another at tick 1696). Kaiju against fighter overlap: 0.
- Jitter (second difference of position): hill crowd median 0.0017, p95 0.057, p99 0.18 u/tick^2; open walkers p95 0.028,
  p99 0.05. Velocity reversals in the crowd: 1.88% of ticks.
- Shiver (MovementShiverTests, 2 of 17 red): NoShiverInTwoMinutes 2 hard flags (for example a Farmer walked 1.69 u for a net
  1.20 u). Whole bot matches: 739 flags in 6 matches; 642 near the hill, 421 next to a non-floor cell, 27 on the Kaiju. By state:
  Fight Dither 175, Fight Reversals 329, Unstick 69. Swing median 1.09 u, p90 3.28, max 5.96. This is bot fight steering. Item 3.
  No picture: a still cannot show a shiver; data/shiver-soak.txt has every flag.

## 3. Vertical (data/3-vertical.txt)

- Jump: 18 air ticks, apex 0.8, symmetric in the sim (the view reshapes it), no air control; coyote 3 ticks and buffer 3 ticks,
  both work. A blocked landing shortens the jump to as little as 6 ticks; otherwise it is refused (Jump.cs:94-96).
- Distance at full speed 2.28 to 2.52 u; standing jump hops in place; landing exactly where planned.
- Running jump at a tall wall: lands against it, no clip.
- Fall: after 4 unsupported ticks, a 2.0 u teleport to the nearest floor, 0.77 s stand-up, 0 HP lost (Terraform.cs:669
  FallIfUnsupported). Item 13.
- Height levels: +1 walks up with no delay; +2 needs a 15-tick climb when walking (29 against 14 ticks) or is instant with a
  jump; +3 is blocked even with a jump. Pit depth 1 exits in 10 ticks; depth 2 in 25 walking or 10 jumping.
- Island edge: a walker is held at the edge; a jump there is silently refused (air 0, item 11); outward knocks and dodges are
  held, so no ring-outs.
- Ladders: 0.07 u float at the foot from the b15 review (item 14), not re-measured here.

## 4. Hits (data/4-hits.txt)

- Melee knock = Strike.Knock / victim mass, decaying 0.82 per tick (Knock.cs:61-109). Smallest: Miner on Warrior 0.17 u over 12
  ticks; largest: Builder on Farmer 1.61 u over 23 ticks. Real swings match the table.
- Stun: 3 ticks; 8 ticks for heavy hits (Warrior or Builder on Farmer). Press to hit: Miner 7, Healer 8, Farmer 9, Builder 11,
  Warrior 13, Demolitions 28 ticks.
- Walls: a 2.4 u knock stops at a wall and at a LowWall. It carries the victim down into a -1 or -2 pit and up onto a +1 block
  (same rule as walking); +2 stops it.
- Hit-stop is view only (Rendering/CombatFx.cs:602, fx.Holding); camera shake is off (OD-6).
- TNT: damage 43, 36, 30, 24, 16 at 0 to 2 u, nothing beyond; allies untouched. Knock at most 0.2 u and no stun, because blast
  knock is gated by Physics.Knockback = false (Demolitions.cs:328, PhysicsConfig.cs:40). Over 8-minute matches: 0 blast knocks,
  40 to 54 blast kills. Item 2, p5-tnt-a-before.png and p5-tnt-c-after.png (5 enemies, 16 to 40 damage each, no one moved).

## 5. Kaiju against fighters (data/5-kaiju-match-seed7.txt, data/5-kaiju-match-seed4242.txt, data/5b-kaiju-trap.txt)

- Facing: up to 179 degrees in one tick (Kaiju.Ai.cs:525, 592, 607, 614 set Facing directly); 73 turns over 45 and 28 over 90
  degrees in one tick in an 8-minute match. The view springs the yaw. Item 8.
- Speed: 63 ticks over 3.2 u/s, one at 16 u/s, from crowd shoves near the hill (Kaiju.ShoveKaiju from Collide.cs:535). Item 7.
- Shoves of fighters: 3112 to 4643 shove ticks per match; largest single shove 0.66 to 0.72 u in one tick.
- Kaiju knocks: mean 0.87 to 0.94 u, max 1.25 u (scaled 0.5 by KaijuKnockScale, Damage.cs:77); stun at most 9 ticks.
- Trap tests: open ground, the fighter is pinned 10 ticks then freed; at a wall or a corner the Kaiju is pushed back 0.9 u and
  the fighter is not trapped. In a 2-cell dead-end corridor the fighter is pinned, then at tick 33 placed 2.71 u away on the far
  side of the Kaiju (KaijuDepenetrate, ring radii 0.02, 0.35, 0.8, 1.4 at Collide.cs:551-563). Item 1,
  p4-kaiju-corridor-t33-hidden.png and p4-kaiju-corridor-t34-hidden.png (Kaiju model hidden so the jump shows), and the
  unhidden pair p4-kaiju-corridor-t33.png / -t34.png.
- Kaiju on the hill with 20 bodies: Kaiju against fighter overlap 0 through the whole crowd run.
- Body size: p6-kaiju-footprint.png, fighters at exact contact (1.75 u from centre) stand under the canopy. Item 9.

## 6. World (data/6-world.txt)

- Bridge sway: a view-only shader (UV5); the sim deck does not move.
- Blocks: per-cell integer lift, so floating blocks cannot exist. TNT at the edge of a +2 stack carved 2 cells to 0; an enemy on
  top dropped from +2 to 0 and lived. WallHp is not used for LowWall and Barricade (reads 0). Item 15.
- Carried items: a Warrior carrying one of each showed no slowdown (design note, not a defect).
- Pits: see section 3.

## 7. Determinism and cost

- Replica check: my per-system timed copy of the tick gives the same StateHash as MatchTick.Advance every tick.
- Same seed (31337) twice: identical, final hash 4BF26EE2792815B2.
- Cross-play: online-smoke run 44887058125 on batch-15-cand1 a15d48a6 (view-only difference to aa2ecfef), Windows x64 host and
  macOS arm64 join: 63 fingerprints agree, ticks 0 to 1830; config hash F1010A3673191F0B the same on both.
- Cost per tick (Editor, Mono, 16v16 seed 7): bots 307 us (47%), movement plus separation 230 us (35%), spatial hash 35 us (5%),
  Kaiju 8 us; total 654 us. Worst tick 86.5 ms (an Editor hitch, not repeatable). MovementSystem allocates 0 B.
- Every sim fix in the top 15 changes the state hash: re-pin the sim checksum once per batch, not per fix.

## 8. Pictures (all opened and checked)

p1-ally-overlap-mid.png (also -close, -gameplay), p2-doorway-jam.png, p3-pillar-snag.png, p4-kaiju-corridor-t33/-t34 (with and
without -hidden), p5-tnt-a-before.png, p5-tnt-b-blast.png, p5-tnt-c-after.png, p6-kaiju-footprint.png, contact-sheet.png
(1984 x 1869).
