# Hit feel: weapon reach and the side camera (handoff, 2026-11-14)

Written Sat 14 Nov 2026, 17:47 CST, by the hit-feel builder.
Source: the capture log of candidate 7 (232f4bd), `~/cm-b10/runs/c7-hitfeel/Player.log` (the `[CM.HitFeel]` lines), and the sheet `~/cm-b10/sheets/sheet-c7-hitfeel-232f4bd.jpg`.
Nothing was run for this note. No code changed tonight.

## Words used here

- **Forward distance**: how far a point is in front of the attacker, along the line to the target.
  - In the swing tables it is measured from the attacker's drawn root.
  - In the tip-gap lines it is measured from the attacker's sim position.
- **Striking end**: the end of the weapon the game treats as the hitting end. "a" is the point farthest from the hand. "b (other end)" is the far point on the other side of the hand.
- **C**: the contact time from the fact table.
- **V**: the view contact. This is the clip time hit-feel-7 now shows on the damage tick: the moment in a window around C where the striking end is furthest forward.
- **Surface**: the target's body surface, as a forward distance.
- **Tip gap**: surface minus the tip's forward distance at the contact frame. 0 or less is a touch.
- **Cover**: the farthest body surface a strike can reach. It is the measured tip reach, plus the 0.25 u view offset cap, plus the gain from an 18 degree upper-body lean.

## Swing tables (candidate 7)

Each table shows the striking end's forward distance through the whole attack clip, in 25 samples. The clip is posed full-body. C and V mark the samples nearest each contact time.

| Class | Clip | End | C (s) | V (s) | Furthest forward | At (s) | Forward at C | Forward at V |
|---|---|---|---|---|---|---|---|---|
| Warrior | AttackSlice | a | 0.242 | 0.192 | 1.61 u | 0.260 | 1.61 | about 1.25 |
| Farmer | Attack2HSlice | b | 0.367 | 0.342 | 0.73 u | 0.361 | 0.73 | 0.73 |
| Miner | Attack2HChop | b | 0.700 | 0.750 | 1.03 u | 0.745 | 0.56 | 1.03 |
| Builder | Attack | b | 0.567 | 0.417 | 0.83 u | 0.347 | 0.76 | 0.81 |
| Demolitions | Attack | a | 0.567 | 0.702 | 0.36 u | 0.694 | -0.02 | 0.36 |
| Healer | Cast | a (right hand, no weapon) | 0.067 | 0.067 | 0.61 u | 0.267 | 0.20 | 0.20 |

The full rows, as logged (time = forward distance in u):

- **Warrior:** 0.00=0.67 0.04=0.88 0.09=1.28 0.13=1.40 0.17V=1.25 0.22=1.40 0.26C=1.61 0.30=0.97 0.35=0.41 0.39=0.13 ... 1.04=0.65
- **Farmer:** 0.00=0.52 ... 0.32=0.57 0.36CV=0.73 0.41=0.50 0.45=0.14 0.50=-0.02 ... 0.72=-0.29 ... 1.08=0.52
- **Miner:** 0.00=0.86 0.07=0.85 ... 0.54=0.23 0.61=0.25 0.68C=0.56 0.74V=1.03 0.81=0.94 0.88=0.35 ... 1.63=0.86
- **Builder:** 0.00=0.20 ... 0.30=0.82 0.35=0.83 0.39=0.82 0.43V=0.81 ... 0.56C=0.76 0.61=0.49 ... 1.04=0.20
- **Demolitions:** 0.00=0.16 ... 0.30=-0.09 ... 0.56C=-0.02 0.61=0.22 0.65=0.34 0.69V=0.36 0.74=0.36 ... 1.04=0.17
- **Healer (Cast):** 0.00=0.10 0.04=0.13 0.08CV=0.20 0.11=0.31 ... 0.27=0.61 ... 0.92=0.10

## The live contact frame disagrees with the swing table

These are the tip gaps the capture measured on the drawn model at the damage-tick frame:

| Class | Surface | Tip forward (live, at the contact frame) | Tip gap | Swing table at V (same clip time) | Disagreement |
|---|---|---|---|---|---|
| Warrior | 0.75 | 1.18 | -0.43 (touch) | about 1.25 | small |
| Farmer | 0.60 | -0.74 | 1.34 | 0.73 | 1.47 u |
| Miner | 0.68 | -0.21 | 0.88 | 1.03 | 1.24 u |
| Builder | 0.68 | -0.25 | 0.92 | 0.81 | 1.06 u |
| Demolitions | 0.53 | -0.02 | 0.54 | 0.36 | 0.38 u |
| Healer | 0.47 | 0.55 | -0.08 (touch) | 0.20 | hand only |

Both numbers in each row are taken at the same clip time V, but they measure different poses. The table poses the clip full-body. The live frame is the game's real pose on the damage-tick frame. For Farmer, Miner and Builder the live tip is BEHIND the body while the table has it more than 0.7 u in front. Weapon length cannot explain a disagreement that size. The live frame is not showing the pose the table measures.

There are two likely reasons, and neither is proven yet.

1. **The contact frame is drawn one tick early.** The wind-up craft holds the drawn-back pose and makes the whole strike inside the LAST tick before the damage tick. That was hit-feel-3's fix for the hand-peak test.
   - The capture stops the clock (time scale 0) and renders right after a tick. If the view draws that frame at the start of the tick, it shows the previous tick's state. That state is the drawn-back extreme.
   - A drawn-back extreme puts the tip behind the body, which is exactly what Farmer, Miner and Builder show.
   - In a real match the view interpolates, so the strike shows for about one or two frames at 60 fps and the hit lands within 0 to 33 ms. The capture may never photograph it.
2. **The table poses the clip on the full body; the game plays it on the upper-body layer.** In the game the hips and legs come from locomotion.
   - The two-hand slice and chop turn the hips and spine a lot. With the hips masked out, the tool points somewhere else.
   - MeasureReach picks the striking end and V from the same full-body pose. So "b (other end)" for the Farmer, Miner and Builder may be right for a full-body swing and wrong for the masked swing the game shows. Farmer: "striking with the other end".

So the measure is still wrong for those three. The retiming (V) was computed on the full-body pose, and the live frame looks at a different pose. That is why the retiming did not close the gaps. Builder (V moved earlier) and Demolitions (V moved later) got worse.

Class notes:

- **Healer:** the lantern is in her LEFT hand. The tip measure only looks at the right hand, so the Healer is measured from the bare hand ("no weapon measured").
  - Her row also logs a view contact of 0.607 s (fact 0.567 s). Those are the "Attack" clip's numbers, while the table uses "Cast" (contact 0.067 s). The clip the animator measured and the clip the table samples are not the same.
- **Demolitions:** the pry bar is the STAND-IN. Even its furthest-forward point over the whole swing is 0.36 u, short of the 0.53 u surface at the staged distance. Its numbers will change with the real weapon.

## Is each weapon physically long enough for the sim range?

The sim lands a hit when the target's body surface is within the attack range of the attacker's centre. So a strike must reach a surface `range` u away.

The best case per class is the furthest-forward point over the whole swing, from the full-body table above. That overstates what the masked game pose reaches. To it I add the 0.25 u view offset cap and the 18 degree lean gain (lever from the log; a lever under 0.25 counts as 0.25).

| Class | Best forward (table) | + 0.25 cap | + lean gain | Best cover | Sim range | Result |
|---|---|---|---|---|---|---|
| Warrior | 1.61 | 1.86 | 0.11 | 1.97 | 1.70 | covered |
| Miner | 1.03 | 1.28 | 0.10 | 1.38 | 1.50 | short 0.12 |
| Healer | 0.61 (hand) | 0.86 | 0.08 | 0.94 | 1.20 | short 0.26 |
| Builder | 0.83 | 1.08 | 0.15 | 1.23 | 1.50 | short 0.27 |
| Farmer | 0.73 | 0.98 | 0.08 | 1.06 | 1.40 | short 0.34 |
| Demolitions | 0.36 (stand-in) | 0.61 | 0.12 | 0.73 | 1.30 | short 0.57 |

The capture's own "covers / SHORT by" lines use the reach at the contact pose rather than the best point: Miner 0.09, Farmer 0.26, Healer 0.30, Builder 0.42, Demolitions 0.70. The two lists agree on the main point. **Even in the best case, only the Warrior's weapon reaches a target at the edge of the sim range.** The other five are short by 0.1 to 0.6 u, however the measure is fixed.

At the capture's staged distance (dummy 0.9 to 1.2 u away, surface 0.47 to 0.75 u), the full-body best points do pass the surface for Farmer, Miner, Builder and Healer. So at close range the swing CAN visibly touch. At the far end of the sim range it cannot.

### Options for the classes that are short

1. **Shorten the sim attack range to what the art reaches.** Miner about 1.40, Builder and Farmer about 1.10 to 1.25, Healer about 0.95. Use the corrected measure, not these estimates.
   - Cost: a Sim rule change. The fixed-seed checksum changes.
   - The bots' attack decisions use the range, so it needs a balance check (bot match win rates, time to kill).
   - It needs the owner's approval as a rules change.
   - Benefit: every hit can be shown as a real touch, at no per-frame cost.
2. **Lengthen the weapons (art).** Each class weapon is re-modelled 0.1 to 0.35 u longer in Blender (scripts run on GitHub), with outlines, LODs and import redone.
   - Cost: an art pass per class. Long two-hand tools start to look comic at 0.3 u more.
   - The Healer has no right-hand weapon, so this does not help her unless she strikes with the lantern.
   - Demolitions needs her real weapon regardless.
3. **Step the attacker in during the strike (view only).** This is what the 0.25 u cap already does.
   - Raising the cap breaks the placement test (0.35 u) and the ring test (0.3 u), and the body would visibly slide off its ring and back.
   - It cannot honestly cover 0.3 to 0.6 u.

My pick:
- **Demolitions:** the real weapon (option 2), then option 1 against whatever it reaches.
- **Miner, Builder, Farmer and Healer:** option 1, shorten each range to the corrected measured cover. It is the only option that makes every landed hit a visible touch without breaking the view tests or the art.
- **Miner:** short by about 0.1, so a 0.1 u range trim or a slightly longer pick head would close it.

Do this only after the measure below is fixed: the shortfalls should be re-read from the corrected numbers.

### What to fix in the measure first (code, next session)

- **Measure the game's own pose.** Pose the clip as the game does (upper-body layer over idle legs, envelope weight 1) in MeasureReach, ExtremeNow and the swing table. Pick the striking end and V from that pose.
- **Photograph the strike.** In the capture, read the tip on the frame where the strike is actually drawn. Either render the damage tick at full interpolation, or log the tip on the damage-tick frame and the frame after it. Log the render alpha too, to confirm or rule out reason 1.
- **Healer:** measure the hand that holds the lantern (hand.l), and use the clip the animator actually plays.

## The side camera: exact reason

The logged lines for all six classes:

```
warrior:     side cam pos (58.10, 3.05, -9.00) fwd (0.00, -0.06, 1.00) mid (58.10, 2.70, -3.50) ... pair at viewport 0.50, 0.50: inside
farmer:      side cam pos (56.03, 3.05, -9.00) fwd (0.00, -0.06, 1.00) mid (56.03, 2.70, -3.50) ... pair at viewport 0.50, 0.50: inside
miner:       side cam pos (56.06, 3.05, -9.00) ...  mid (56.06, 2.70, -3.50)
demolitions: side cam pos (55.99, 3.05, -9.00) ...  mid (55.99, 2.70, -3.50)
builder:     side cam pos (56.06, 3.05, -9.00) ...  mid (56.06, 2.70, -3.50)
healer:      side cam pos (55.94, 3.05, -9.00) ...  mid (55.94, 2.70, -3.50)
```

Every class reports drawn ground 1.50 u at the pair and 1.50 u at the camera, with a sim ground offset of 0.00.

1. **The camera is placed and aimed correctly.** The pair is drawn on its staged spot (0.00 u off for both fighters, both active). The camera looks straight at the pair's midpoint from 5.5 u away at chest height, and the midpoint projects to the exact centre of the frame (0.50, 0.50). Placement and aim are not the problem.
2. **Something stands between the camera and the pair.**
   - Every pair is staged at world z -3.50, which is sim z 3.5: about 3.5 u from the map's low-z edge.
   - The open-side pick then puts every camera 5.5 u into the map, at world z -9.00 (sim z 9.0).
   - The Warrior's pair stands at x 58.1; the other five stand about 2 u west, at x 56.0.
   - From x 56 the line of sight from sim z 9 to sim z 3.5 passes through a wall or rock piece. The strips show a wall face and grass right in front of the lens. From x 58 the line is clear.
   - The five strips are near-identical because the five pairs stand within 0.1 u of each other.
3. **Why the loud check did not fire:** it only tests that the pair's midpoint projects inside the central 60% of the frame. Nothing between the camera and the pair is checked: no occlusion or line-of-sight test.
4. **Why the open-side pick did not avoid it:** PickSide only counts floor cells (and their sim height) along the line, at the pair's own x, in 1 u steps. It cannot see:
   - wall or rock art that has no sim cell (world dressing);
   - a wall cell just beside the line;
   - anything that rises above the camera's 3 u height between the cells it samples.

### Fix (capture only, next session)

- **Line-of-sight test:** test the segment from the camera to the pair's chest against the bounds of every world renderer except the two fighters (Bounds.IntersectRay, capture-only cost). Try the 8 compass directions and two arm lengths (5.5 and 3.5 u), with a 20 to 30 degree downward angle. Use the first clear one; if none is clear, fail the class loudly.
- **Simpler alternative:** stage every class on the Warrior's spot (x 58.1, z 3.5), which is known to frame well. Keep the line-of-sight test as the loud check.

## Owner direction 2026-11-14

The owner's words: "it should be based off how long the weapon is ... real logic and physics".

So each class's attack range follows what its weapon really reaches. The art lead is being asked to confirm before any rule changes.

Order of work for next session:

1. **Fix the measure.**
   - Read the tip on the frame where the strike is actually drawn (the capture frame), not one tick early.
   - Pose the clip on the upper-body layer, as the game plays it, when picking the striking end and the view contact.
   - Measure the Healer's lantern hand (hand.l), with the clip the animator actually plays.
   - Give Demolitions her real weapon in place of the stand-in pry bar.
2. **Re-read each class's reach** from the fixed capture: the tip gap at contact, the swing table and the furthest-forward point.
3. **Set each class's range from its measured reach, in one data table.** The range IS the reach, never a hand-typed number, so a longer weapon automatically reaches further.
4. **Re-pin the checksums** (the fixed-seed sim checksum changes with the ranges) and run a bot balance check.
5. **Art lead sign-off.**

## Batch 11 item C (2026-11-16): what was built

- **One table.** `DepthConfig.WeaponReach.Reach` (indexed by class) is every class's swing reach in the Full profile.
  Combat reads it through `Combat.ReachOf` (the swing, its event points, barricades, decorations, the Kaiju), the bots
  through `Combat.BotReachOf` (BotSteer, ShiverWatch; the Healer keeps her 4.5 u mend stand-off), and the view through
  `LightWeaponView.Range`. The light weapons' `Range` is the same table's starting row. Classic and PhysicsOnly are
  untouched (depth lane off). Starting rows = the "best cover" column above: Warrior 1.70, Farmer 1.06, Miner 1.38,
  Demolitions 0.73 (stand-in pry bar), Builder 1.23, Healer 0.94.
- **Measure fixes.** The striking end, the view contact and the swing table are now taken on the pose the game draws
  (`AttackTiming.PoseDrawn`: idle legs, the clip on the upper-body layer, envelope 1); the wind-up extreme too. The
  Healer is measured at her lantern hand (left; `WeaponTip.Left*` measured at import, importer version 20) with the clip
  the animator plays for the lantern swing (`AttackTiming.SwingClip`: Attack, not Cast). The capture reads the striking
  end the animator measured (not always end "a"), logs the tip on the damage-tick frame and the frame after with each
  render alpha, and judges the gap on the frame that draws the strike.
- **Re-pin loop.** The EditMode test `WeaponReachTests` prints `[CM.WeaponReach] <class>: measured reach X u, table Y u`
  (measured = tip reach at the drawn view contact + 0.25 u step-in cap + 18 degree lean gain). The rows are replaced by
  those numbers in the fix-up round, then the Full golden is re-pinned from the "rules-combined golden" Full line.
