# The player-seat gate

The question this gate asks: when a skilled person presses a button, does the press land, does it matter, and can they
see that it did? Bot-versus-bot statistics cannot answer that. A build can balance perfectly between bots while a human's
swing whiffs, a press does nothing, or a hit shows no number. This gate drives the game the way a human does and fails
when the human seat is let down. The bots' win rates do not come into it.

A build is not "ready" while `player-seat` is red, and it is not ready while it is incomplete either.

## Run it

```
loop/gates/player-seat                 # tier S on this Mac (no Unity editor), tier F on GitHub
loop/gates/player-seat --remote        # tier S on the Air (falls back to GitHub), tier F on GitHub
loop/gates/player-seat --skip-f        # tier S only: exits 78 (incomplete), never 0
```

The gate prints one JSON line and appends it to `loop/logs/gates.jsonl`. It returns one of these exit codes:

| Exit | Meaning |
| --- | --- |
| 0 | Both tiers passed. |
| 1 | At least one tier failed. |
| 78 | Nothing failed, but a tier did not run: tier F skipped, no Unity licence on GitHub, or the runner timed out. |
| 64 | Bad command-line usage. |

The tiers can also be run on their own:

```
tools/ci/sim-tests-mac.sh --library ~/critical-mass-unity/Library PlayerSeat         # tier S, about 30 s
loop/gates/remote playmode --category PlayerSeat                                    # tier F (GitHub, Unity licence)
loop/gates/remote playmode --category PlayerSeat --graphics software                # + F19, the pixel test
CM_PX_SOAK_SEEDS=24 tools/ci/sim-tests-mac.sh --slow PlayerSeatImpactSoak           # the soak (Slow, not in the gate)
```

Set `CM_PX_DATA_DIR=<dir>` to collect raw data. With it set, every `[px]` line is also appended to
`<dir>/tier-s-measures.csv`, and the soak writes one csv row per seed.

## Tier S: the sim, with a human model (`Assets/_Game/Sim.Tests/PlayerSeat/`)

Tier S is plain C# and needs no Unity. Every press is a human `InputFrame` passed through `MatchTick.AdvanceHumans`, the
game's only input seam.

The human model (`HumanModel` in `PlayerSeatRig.cs`) works like this:

- It aims at where the target was 6 ticks (0.2 s) ago.
- It walks in to 0.8 x reach.
- It presses when the target is in reach and the swing is ready.
- It can optionally miss every other swing by 90 degrees (the "50% player").
- It can optionally strafe.

Two rig checks keep the model honest. `Control_PassiveTarget_Lands` must stay green: if it goes red, the rig is broken,
not the game. `Seat.Step()` also throws if the idle autopilot ever drives the human seat, so a test can never measure a
bot by mistake.

| File | What it covers |
| --- | --- |
| `PlayerSeatSimTests.cs` | Presses in isolation: S1/S2/S4/S7 primary and ability per class, S8 blocked hits reported (spawn, dodge, ward, wall), S16/S18/S25/S26 class oddities, S30/S47 autopilot, S33 every button x every scene answered within 2 ticks, S34, S46 the input buffer. |
| `PlayerSeatDuelTests.cs` | Duels: S3 strafer land rate, S5/S5b lag-6 human vs Normal bots, S6 kill through a Healer, S11 chase, S35 human vs human, S41 fountain, S42a / S42b bot fairness (see below), S43 the 50% Warrior, S44 Healer rules, S45 the middle classes. |
| `PlayerSeatWorldTests.cs` | Class verbs and the world: S9-S31 and S32, S36-S40 (structures, Kaiju, dig, ladder/bridge, bank). |
| `PlayerSeatImpactSoak.cs` | (`Slow`, `PlayerSeatSoak`) 5v5 and 16v16 over 24 seeds, all-Normal against a strong seat 0, with uplift and seat-0 shares. |
| `PlayerSeatTargets.cs` | Every threshold. |

Each measured case prints one line: `[px] id | case | measured | target | PASS/FAIL`.

## Tier F: real devices, what the player is shown (`Assets/_Game/Runtime.PlayTests/PlayerSeat/`)

`PlayerSeatFixture` derives from the Input System's `InputTestFixture`. The setup follows the Input System 1.20 manual,
"Set up test fixtures" and "Set up test assemblies":

- `Packages/manifest.json` lists `"testables": ["com.unity.inputsystem"]`.
- The play-test asmdef references `Unity.InputSystem.TestFramework` and `UnityEditor.TestRunner`.

The fixture adds a real Keyboard, Mouse and Gamepad and presses them with `Press`, `Release` and `Set`. The shipped
`HumanInputSource` of the Boot scene reads them. The sim clock is stopped, and the test steps it one tick per rendered
frame. How each kind of cue is checked:

- **Cues that exist today** are read in their real state: GameHud's damage-number slots, `CameraShake.Applied`, playing
  `AudioSource`s and `ParticleSystem`s, and on-screen text.
- **Cues the rebuild adds** are read from the `PlayerFeedback` ledger (`Runtime/Game/PlayerFeedback.cs`). The entry's
  `Source` must be an active, enabled object, so a cue cannot be reported while its object is hidden.

Camera views and saved settings:

- Core tests run in both views: TopDown (a fresh install, `cm.camera.ground` false) and ThirdPerson (how the owner plays).
  The core tests are F1-F4, F20-F23, F25, F26, F30, F31 and F33.
- The fixture saves and restores the camera, clean-HUD and reduce-motion prefs it changes.
- Every test asserts that the autopilot was not driving.
- The wall-clock idle guard (`HumanControlGuard.IdleAutopilotEnabled`) is off during tests and restored afterwards. The
  sim's own autopilot rule is the one under test (F34).

Thresholds live in `PlayerSeatFeedbackTargets.cs`, for example camera kick >= 0.03 u on my hit, >= 0.06 u on a hit on me,
and 0 with reduce motion.

Categories: `PlayerSeat`, plus `PlayerSeat.Graphics` for F19. F19 ignores itself under `-nographics`.

Side effect of `testables`: the Input System's own `Unity.InputSystem.IntegrationTests` (category `Integration`, a few
tests) now compile into the project, and they join the unfiltered PlayMode and EditMode runs.

## Rules for fixers

Fix the game, never a test. A number in a Targets file changes only with the lead's sign-off, and the reason goes in the
comment beside it. Sim changes that move S1/S4 numbers re-pin them in the same PR as the golden hashes.

## S42 bot fairness: two tests (px S4, lead's call 13)

The first S42 asserted one band (a Normal bot lands 0.40 to 0.75 on a strafing human) against one strafer that circled at
0.8 x its own range, deep inside the bot's reach, so the hit arc covered any aim error (bots landed 0.80 to 0.95). Moving that
strafer to a player who really dodges (the edge of the bot's reach, sees the wind-up 6 ticks late, steps out until it has
resolved, comes back in to punish: `HumanModel.DodgeSpacing`) dropped the bots to 0.05 to 0.50. The one band mixed two
different players, so S42 is now two honest tests:

- **S42a DodgingPays**: against the dodging strafer a Normal bot lands **at most 0.60**, every class
  (`PlayerSeatTargets.BotLandDodgingMax`). Dodging must pay.
- **S42b StandingInReachCosts**: against the old close strafer that never steps out a Normal bot lands **at least 0.40**, every
  class (`PlayerSeatTargets.BotLandStandingMin`); no upper bound: a player who does not dodge gets hit.

Both print the bot's and the human's land counts and the number of step-outs in the assert message.
