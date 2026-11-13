# Sanity capture checklist (every integration)

Written for test/gameplay-sanity, 2026-11-12. Why: the owner played a build in which every character and the Kaiju
walked BACKWARDS. Nobody caught it, because every check we ran was either a still screenshot judged for looks or a sim
test with no view. A still frame cannot show which way something is moving. This list is the moving-picture check a
reviewer does on every integration, next to the automated suite (EditMode `GameplaySanityTests`, PlayMode
`FacingSanityPlayTests`, `MotionSanityPlayTests`, `GameplaySanityPlayTests`).

The automated suite comes first: if it is red, fix the game before anyone looks at frames. The frames catch what the
suite does not measure yet: feel, timing, readability.

## What the reviewer looks at (about 2 minutes per integration)

Use the strips from the batch capture below (or a 10-second screen recording of the same script). For each row, write
PASS or what is wrong in the integration notes. Do not judge looks here; judge whether motion makes sense.

| # | Clip | Look for | Wrong looks like |
|---|------|----------|------------------|
| 1 | Player runs a square (right, up, left, down, 1.5 s each) | Chest, face and toes lead in every leg; legs cycle at the speed the body moves; the turn at each corner is instant or quick | Back or heels leading (walking backwards); legs still while the body glides; feet skating; the body pointing sideways |
| 2 | Player attacks in 4 directions toward the cursor (right, up, left, down) | The swing and its trail go toward the cursor; the body turns to the cursor first | Swing behind the body; trail on the wrong side; the body not turning |
| 3 | A bot picks up and delivers biomass or corruption | The cargo is visible in its hands or on its back the whole way, and disappears at the delivery point | Nothing carried; cargo floating at the map origin or hanging in the air behind it |
| 4 | The Kaiju walks (forced hatch, 8 frames over 4 s) | Head leads, tail trails, feet on the ground, steps match the speed | Tail leading; feet in the air or under the ground; sliding without steps |
| 5 | The same frames, HUD | Clock counts down; score, hill word and Kaiju word change with what happens on screen; the minimap's own marker points where the player faces | HUD frozen, lagging, or saying a team holds the hill while nobody is on it |
| 6 | The same frames, rings | The player's ring stays under the player; ally and enemy rings under the right units | A ring left behind, or on the wrong unit |

Anything wrong in rows 1 to 4 is a blocking defect even when every automated check is green: add the missing check to
the suite in the same fix.

## Batch capture script (spec for the integrator)

Run by the integrator only, once per integration, through the machine lock (`~/bin/cm-heavy`), never by a builder.
One Unity run, batchmode with a GPU (no `-nographics`), about 60 s.

**Entry point to add (Editor assembly):** `CriticalMass.Editor.SanityCapture.Run`, driven by
`tools/unity-sanity-capture.sh OUT_DIR`, modelled on `tools/unity-capture.sh` and `CriticalMass.Editor.CaptureTool.Run`
(same `_unity-env.sh`, same log handling, same exit codes).

**Setup:** load Boot with `SimDriverBehaviour.CaptureMode = true` (clock stopped), then set `HumanAutopilot = false` on
the driver so actor 0 is driven by the script, seed 1337 (GameConfig default), Azure Warrior, 16v16. Disable the
HumanInputSource component and write its `State` directly (the same harness as `SanityWorld` in
Runtime.PlayTests): `SetMove(right, up)`, `SetAim(x, z)`, `ButtonDown(Attack)` / `ButtonUp(Attack)`. Advance only with
`StepTicks`, call `ActorViews.Sync` after each tick, and render with the gameplay camera at its normal follow.

**Fixed input script (sim ticks at 30 Hz):**

| Segment | Ticks | Input | Frames saved |
|---------|-------|-------|--------------|
| settle | 90 | none | none |
| square | 4 x 45 | move right, up, left, down (screen axes) | 8: ticks 22 and 44 of each leg |
| attacks | 4 x 30 | stop; aim 3 u right, up, left, down; press Attack for 1 tick | 8: ticks 3 and 8 after each press |
| delivery | up to 1800 | autopilot on for actor 0 too; follow the first bot that picks up cargo (camera override on it) until it delivers | 8: evenly spaced from pick-up to delivery |
| kaiju | 120 | `World.ForceHatch = true`, camera override on the Kaiju once it walks | 8: every 15 ticks |

**Output in OUT_DIR:** one 8-frame strip per segment (`strip-square.png`, `strip-attacks.png`, `strip-delivery.png`,
`strip-kaiju.png`; each 8 frames of 480 x 270 side by side, frame number and sim time stamped in a corner), plus
`sanity-capture.json` with, per saved frame: tick, actor 0 sim position and facing, the measured model front (the
`SanityRig.TryFront` reading) and the angle between them, and for the delivery strip the carrier id and its `Carry`.
The script exits non-zero when any saved frame's front is more than 30 degrees from the sim facing, so a backwards model
fails the capture even before a person looks.

**Integrator run order:** compile, EditMode gate (includes `GameplaySanityTests`), PlayMode gate (includes the three
sanity fixtures), then `~/bin/cm-heavy tools/unity-sanity-capture.sh <integration>/sanity`, then the reviewer table
above on the four strips. Loops delete the strips at loop end (only the JSON is kept with the integration notes).
