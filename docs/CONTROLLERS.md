# Controllers and keyboard: how to connect, what works, known limits

Written for controls-1 (2026-11-13). The in-game version of the connection steps is the CONTROLLER page of the
Controls screen (Pause, CONTROLS, or Settings, Controls, VIEW CONTROLS, then TAB / LB / RB to the CONTROLLER page).

## The three ways to play

Every action in a match and in every menu works in each of these, and the game switches live to the last one you
touched. There is no setting: move the mouse and the mouse aims; press a keyboard attack key with the mouse resting
for half a second and the keys aim again; touch a controller and the controller takes over. On-screen prompts change
with it (a keyboard player is never told to click).

The keyboard layouts follow the genre (sources in the controls-1 audit): the arrows with Z X C V on the other hand, as
in Brawlhalla's Keyboard A, Touhou and Celeste; W A S D with J K L H, as in Brawlhalla's Keyboard B; W A S D with the
mouse, as in Hades and Nuclear Throne. No keyboard-only layout puts a verb on the movement hand. The Controls screen
has four named layouts that apply in one press: ARROWS + Z X C, WASD + J K L, WASD + MOUSE and LEFT-HANDED (mouse in
the left hand; R SHIFT jump, / interact, . dig, , switch, ; ping beside the arrows). Single keys still rebind in
Settings, Controls, with a conflict warning and RESET ALL.

| Action | Keys, left hand on W A S D | Keys, right hand on the arrows | Keys and mouse | Controller |
| --- | --- | --- | --- | --- |
| Move | W A S D | Arrow keys | W A S D or arrows | Left stick |
| Aim | Faces where you move | Faces where you move | Mouse pointer | Right stick (at rest: faces where you move) |
| Lock facing (hold, strafe) | L | L CTRL | not needed | Right stick |
| Attack (hold to repeat) | J | Z | Left click (J and Z still work) | RT / R2 |
| Class ability | K | X | Right click (K and X still work) | LT / L2 |
| Jump | SPACE | C | SPACE | A / cross |
| Dodge | L SHIFT | R CTRL | L SHIFT | RB / R1 |
| Guard (hold) / parry (tap at the last moment), class moves | ; | R SHIFT | Q | B / circle |
| Cancel attack (feint) | P | \\ | Mouse forward side button | LB / L1 inside your own swing window (else LB is Next tool) |
| Interact / carry / use station | H | V | E | X / square |
| Dig / place | U | F | F | Y / triangle |
| Next tool / switch dig and place | I (1-7 pick a Builder tool) | R | Mouse wheel | LB / L1 outside your swing window |
| Ping | G | G | G or middle click | D-pad left |
| Look around (hold) | ` | ` | ` | Hold R3 |
| Show usable terrain (hold; terrain classes) | O | O | O | R3 (outside the base ring; inside it R3 opens the station) |
| Scoreboard (hold) | TAB | TAB | TAB | View / Create |
| Full map | M | M | M | D-pad up |
| Base station | B | B | B | Click the right stick (R3) inside your base ring |
| Station: buy | ENTER | ENTER | ENTER or click | A / cross in the panel |
| Emote wheel | T | T | T | Hold the left stick (L3) 0.3 s |
| HUD cursor | L ALT | L ALT | L ALT | D-pad down |
| Minimap zoom | N | N | N | D-pad right |
| Camera view: high / third person | F5 | F5 | F5 | Settings, CAMERA (no button) |
| Pause | ESC | ESC | ESC | Menu / Options |
| Menus: move focus | W A S D or arrows | Arrows | Arrows or hover | D-pad or left stick |
| Menus: confirm / back | ENTER or SPACE / ESC | ENTER or SPACE / ESC | ENTER or click / ESC | A / B (cross / circle) |

Keyboard aim: the fighter faces the way it moves and keeps that facing when you stop. Hold lock facing to keep the
facing while you strafe.

Aim help (Settings, ACCESS: AIM HELP for MOUSE, CONTROLLER and KEYS ONLY; OFF / LOW / HIGH; mouse LOW, controller and
keys HIGH by default): from the attack press through the wind-up, and 0.4 s after, the swing turns toward the best enemy
in front of you (HIGH: within 50 degrees and your reach + 2.5 m; LOW: 25 degrees and reach + 1.5 m), aimed where it will
be 0.13 s later. With the keys or one stick in the third-person view the cone is measured around the way you move. On
HIGH an enemy touching you is hit even when nothing is ahead. Outside attacks HIGH leans the facing a little (not in
third person). It never turns the camera. Fogged enemies are never picked; a clear line is preferred. TNT throws lead
by up to 0.5 s; the Healer's press turns to the hurt ally nearest your aim when no enemy is inside her lantern reach.

Cast distance: the right stick's push sets how far roots, the healing pulse and TNT go (1 m to the ability's range);
keyboard only, a Farmer or Healer holds the ability key to reach farther (full range after 0.5 s) and casts on
release; a quick tap casts 3 m ahead, or at the enemy the aim help picks. The mouse casts at the pointer.

Hard-coded keys in a match (not rebindable): 1-7 pick a Builder tool; F12 photo; F6 auto-director (spectating). H no
longer hides the HUD (it is the J K L layout's Interact): CLEAN HUD is a setting on the ACCESS tab only.

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

## Ground view (feat/ground-camera, 2026-11-13)

A low camera about 3 m behind and 1.6 m above a fighter, over his shoulder, with the horizon and the sky on screen.

Watching (spectating, or knocked out and waiting to respawn): CAMERA VIEW (F5, double-tap L3) switches between the
high view and ground view. Q / E or LB / RB pick the previous or next fighter (while spectating, the spectator's own
keys do it and ground view follows), right mouse drag or the right stick orbits (the camera swings back behind the
fighter 2.5 s after you let go), F6 turns the auto-director on or off (it cuts between duels, crowds, the hill,
carriers and the Kaiju, at least 4 s per shot). The minimap and Kaiju panels hide; the clock and score stay.

Playing (experimental): Settings, Game, CAMERA: GROUND, or CAMERA VIEW during a match. The mouse turns the camera (the
cursor is locked while you play; ESC frees it), or the right stick on a controller. W A S D, the arrows and the left
stick move relative to the camera; attacks and abilities go toward the reticle at the screen centre. Not supported
from this camera: keyboard-only play (nothing turns the camera), walk to pointer, and precise dig / place targeting
(the target is the ground point ahead of you at the reticle's distance).


## b16 finish: camera view off the View button (2026-11-22)

The Editor review found the View / Create button doing two things: hold for the scoreboard, double-tap for the camera
view. Every controller button already had a job, so the camera view moved to a double tap of the left stick (L3), and
the emote wheel on L3 now opens after a 0.3 s hold on a controller (the quick-chat wheel's rule), so a tap or a double
tap never flashes it. Keyboard T still opens the emote wheel at once. The controls screen and the rebinding rows read
the binding itself, so they show "DOUBLE-TAP L3" without a text change.


## Lane I: one job per button (2026-11-22)

Middle mouse is Ping only (it used to also pan the camera while held). Looking around with the mouse moved to the `
key (rebindable, LOOK AROUND). On a controller R3 looks only while held for 0.25 s or more, so a click on R3 opens the
base station without panning; L3 no longer changes the camera view (a stick click while sprinting did), so L3 is the
emote wheel only and the view is picked in Settings, CAMERA. LB stays Cancel inside your own swing window and Next tool
everywhere else; the HUD reads InputContext.PadLbCancels to name which. Interact taps shorter than one frame now always
reach the game.

## Class moves (2026-11-22)

Every class-moves verb has its own button or is a state of one button that the HUD names, so no button does two jobs at
once. GUARD / PARRY is new: Q (keys and mouse), ; (W A S D with J K L), R SHIFT (arrows with Z X C), B / circle on a
controller. Q stopped being "show usable terrain" (O and R3 keep it) and B stopped being a second class-ability button
(LT keeps it). LEFT-HANDED moves guard to ' (and to [ and ], so ; stays ping and R SHIFT stays jump). The others use
existing buttons: hold Attack to charge a heavy (a tap is still the light swing), press Attack again after a landed hit
for the next combo step, Dodge while TRAVEL is lit is a SLIDE (travel only happens with no enemy within 8 units), and the
Healer glides by holding Jump after the top of the jump. They are off in the normal build; the play build (scripting
define CM_MOVES_ON) turns them all on.
