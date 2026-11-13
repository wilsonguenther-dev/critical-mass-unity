# Controllers and keyboard: how to connect, what works, known limits

Written for controls-1 (2026-11-13). The in-game version of the connection steps is the CONTROLLER page of the
Controls screen (Pause, CONTROLS, or Settings, Controls, VIEW CONTROLS, then TAB / LB / RB to the CONTROLLER page).

## The three ways to play

Every action in a match and in every menu works in each of these, and the game switches live to the last one you
touched. There is no setting: move the mouse and the mouse aims; press a keyboard attack key with the mouse resting
for half a second and the keys aim again; touch a controller and the controller takes over. On-screen prompts change
with it (a keyboard player is never told to click).

| Action | Keys, left hand on W A S D | Keys, right hand on the arrows | Keys and mouse | Controller |
| --- | --- | --- | --- | --- |
| Move | W A S D | Arrow keys | W A S D or arrows | Left stick |
| Aim | Faces where you move | Faces where you move | Mouse pointer | Right stick (at rest: faces where you move) |
| Lock facing (hold, strafe) | L or L SHIFT | L SHIFT | not needed | Right stick |
| Attack (hold to repeat) | J | Z | Left click (J and Z still work) | RT / R2 |
| Class ability | K | X | Right click (K and X still work) | LT / L2, or B / circle |
| Jump / dodge | SPACE | C | SPACE | A / cross |
| Interact / carry / use station | E | V | E | X / square |
| Dig / place | F | F | F | Y / triangle |
| Switch dig / place | Q | / | Mouse wheel | LB or RB / L1 or R1 |
| Ping wheel | G | G | G or middle click | D-pad left |
| Scoreboard (hold) | TAB | TAB | TAB | View / Create |
| Full map | M | M | M | D-pad up |
| Base station | B | B | B | Press the right stick (R3) |
| Station: buy | ENTER | ENTER | ENTER or click | A / cross in the panel |
| Emote wheel | T | T | T | Press the left stick (L3) |
| HUD cursor | L ALT | L ALT | L ALT | D-pad down |
| Minimap zoom | N | N | N | D-pad right |
| Character panel | P | P | P or click the portrait | (HUD cursor) |
| Pause | ESC | ESC | ESC | Menu / Options |
| Menus: move focus | W A S D or arrows | Arrows | Arrows or hover | D-pad or left stick |
| Menus: confirm / back | ENTER or SPACE / ESC | ENTER or SPACE / ESC | ENTER or click / ESC | A / B (cross / circle) |

Keyboard aim: the fighter faces the way it moves, keeps that facing when you stop, and leans toward the nearest enemy
inside a 30 degree cone up to 7 metres ahead (target assist). Hold lock facing to keep the facing while you strafe;
lock facing turns the assist off.

Every key and button can be changed in Settings, Controls (five columns: W A S D hand, arrow hand, mouse, controller,
controller 2), saved between launches, with a warning when a key is already used and RESET TO DEFAULTS.

## Controller settings

On the CONTROLLER page: stick dead zone (small, medium, large), stick sensitivity (low, normal, high: how far you must
push for full speed), invert aim up/down, vibration on or off, CHANGE BUTTONS (opens the rebinding grid), RESET SETTINGS.

## Connecting a controller

The game reads controllers through Unity's Input System. Controllers with the A, B, X, Y buttons and controllers with
the cross, circle, square, triangle buttons are recognised, and the prompts draw that controller's own buttons.

### USB (both kinds, Mac and Windows)

Plug the controller in with a data cable (USB-C for the newer ones, micro-USB for older ones). It works at once; no
pairing. If nothing happens, try another cable: many charge-only cables carry no data.

### Bluetooth, controller with A B X Y buttons

1. Turn the controller on with the round centre button.
2. Hold the small pairing button on the top edge, next to the USB port, for about 3 seconds, until the centre light
   flashes quickly.
3. Mac: Apple menu, System Settings, Bluetooth. The controller appears under Nearby Devices; click Connect.
   Windows 10 / 11: Start, Settings, Bluetooth and devices, Add device, Bluetooth, then pick the controller.
4. The centre light stays on when connected. Start the game (or switch back to it).

### Bluetooth, controller with cross / circle / square / triangle buttons

1. With the controller off, hold the small Create button (Share on the older model, left of the touch pad) and the
   centre button together for about 3 seconds, until the light bar flashes quickly (the older model double-flashes).
2. Mac: System Settings, Bluetooth, click Connect next to the controller (it is listed as a wireless controller).
   Windows: Settings, Bluetooth and devices, Add device, Bluetooth, pick the wireless controller.
3. The light bar glows steadily when connected.

### Mid-match

Unplug the controller or let it switch off during a match and the game pauses with a "controller disconnected" notice.
Connect it again and the match carries on where it was; you keep control of your fighter. With two controllers
connected, the one used last plays.

## Known limits (check these before telling a player it "should work")

- macOS: the newest controllers of both kinds need macOS 11.3 or later over Bluetooth; earlier macOS versions only take
  the older cross / circle model (and any controller by USB with a driver). Older A B X Y controllers without
  Bluetooth (the first model of that generation, and the 360-era wired pads) are not supported on Mac without a
  third-party driver; on Windows they work by USB or with the wireless adapter.
- Not supported on the cross / circle controller: adaptive triggers, the advanced haptics (the game uses plain two-motor
  rumble), the touch pad as a pointer, the motion sensor, the speaker and the microphone. Rumble over Bluetooth can be
  missing on some Windows setups (USB works); this has not been measured on this project's builds.
- Other controllers (for example the Nintendo-style pro controller and generic pads): those the Input System knows play
  with A B X Y labels (positions may differ from the labels on the pad); pads it only sees as a "joystick" (many
  older DirectInput pads) are not used. Steam Input, when it runs for this game, can remap buttons: turn it off for the
  game if prompts and buttons disagree.
- The glyph shapes (cross, circle, square, triangle) are drawn with the font's geometric-shape characters; if a build's
  font lacks them the integrator's capture will show empty circles. Text cells say CROSS, CIRCLE, SQUARE, TRIANGLE.
- On screen the game never names a brand: it only draws the buttons of the controller it sees.

## For developers

- Bindings: `Assets/_Game/Settings/Controls.inputactions` (groups KeyboardMouse and Gamepad; keyboard keys carry the
  binding name `WASD` or `Arrows` for their hand position, shared keys none).
- Schemes, completeness and collisions: `Assets/_Game/Runtime/Input/ControlSchemes.cs`; tests
  `Runtime.Tests/ControlSchemesTests.cs`, `Runtime.PlayTests/KeyboardAndControllerPlayTests.cs`.
- Keyboard aim and target assist: `AimResolver.ResolveKeys` (view side; the sim only receives an aim point).
- Controller family, dead zone, sensitivity, invert, vibration: `Assets/_Game/Runtime/Input/ControllerSettings.cs`.
- Hot-plug pause: `Assets/_Game/Runtime/Game/SystemEvents.cs`.
