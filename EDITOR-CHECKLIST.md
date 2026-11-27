# Batch 15 (waves one and two): one in-Editor checklist for the reviewer

Branch integration/batch-15-cand1 (wave two head: see the last integrator commit). Fourteen pieces: rejoin #218, zero-cost
online #224, match summary #216, profile/mastery #217, terrain height #211, audio #223, world look #220, island geometry
#221, safe defaults #222, Kaiju parts #225, balance tool #226 (tooling only, nothing to look at), combat feel #215, bot brain
#227, comeback + hut choices #219. Run under Full rules unless a line says otherwise. Positions are sim x:z. Each line is a
thing to LOOK at and what you should see. Tick a line only after you saw it in Play mode or a player build.

## 0. Open visual items carried from wave one (not fixed blind)
1. DIG one cell short: aim at 15:40 with the Builder's DIG. Expected: the dug cell is the one under the marker you read. Today
   it digs 14:40. First find which marker the player reads (the tile disc on `Terraform.TargetCell`, or the mouse reticle)
   and whether input sends a different aim than the view draws. #222's routed `AffordanceCore.DigPlace` patch is NOT applied
   (it would draw a block glyph for a dig).
2. 0.28 u float at a ladder foot: stop a fighter 0.1 u inside a ladder foot. Expected: feet on the ground. Today
   `LadderClimb.Profile` lifts it about 0.28 u (slope about 2.8 u per u from `FootDist`). Fix options: a 0.15 u dead zone or
   a smoothstep (`LadderClimbTests` pins the midpoint at top/2; a smoothstep keeps it).
3. Heal-grove FX shapes: heal with a Healer at a grove. Expected: mint (#220), not Biomass lime; the "lime voxel cubes with +"
   shapes from the audit are not rebuilt yet: note what you see.
4. Kaiju brightness (changed in wave two, read this one carefully): the Kaiju now gets ONE lift, the world look's character
   band and gain (`CMToon` charW, rim above 0). `KaijuAnimator` no longer sets `_MidRatio/_ShadowRatio/_LitMultiplier` lifts
   for a parts model (and keeps only the type tint for a model without parts). Run CM > World Look > Value probe with a
   hatched Kaiju of each type at Day, Dusk and Night. Expected: body minus ground at least +0.12 (Day, Dusk) and +0.15
   (Night), no clipping in the lit band. If it reads flat, put back `KaijuTypeLook.Parts*Lift` (1.10/1.19/1.27) and say so.
5. Black square at match start: start a match, a rematch and a replay. Expected: no black square on the first frames.
6. ReactionStamp pass-index error (`ForceField.cs`, `GroundPaint.cs`): watch the Console through a match with force fields
   and ground paint. Expected: no "pass index" error; if it rolls, copy the first one with its stack.
7. Load hold: in a player, the match does not tick until the world view started its sliced build (`WorldView.AwaitsWorld`).
   Expected: match start, rematch and replay all begin, with no wait you can see.

## 1. World look (#220) and island geometry (#221)
1. CM > World Look > Value probe: body minus ground at least +0.12 at Day and Dusk and +0.15 at Night, at the hill, both
   pads and the player.
2. Frame probe: SetPass p50 under 150 (about 90 to 120 at High), 0 frames over 33 ms after the hatch, 0 B per frame.
3. Feet probe: planted share at least 30% per class. Then `ClassStrides.UseInAnimator = true` and rerun (held off: the
   Warrior's back-left diagonal slid 0.41 u/u, limit 0.2).
4. The moon's parallax; particles on Medium and High (none on Very Low); bloom on glows only; scorch 0.6 to 1.2 u at 30%.
5. Cliffs at 91:07, 29:50, 93:33: a continuous skirt, no slabs, corners rounded; the omni camera sees a closed underside.
6. Pits near the hill trench at 46:40 (a 1-cell and a 3x3): a round rim, banded walls, a smooth floor, no inner walls; a
   2-deep pit is drawn at its true depth.
7. Hill 56:40: 8 gate posts at radius about 10.8, the marker diamond, a round plate. Shrine 56:12: gate posts at
   54.5:12.5 and 57.5:12.5, braziers along the yard wall. Bases 13:36 and 99:36: thin props, no fences on walkable cells.
8. DG-09 pillars topple with flat arrows; the sky has varied rocks; signposts at the side-route bridge mouths.

## 2. Characters and the Kaiju (#225, world look)
1. Hatch each type (Elder, Grove, Brute, Timber, Mire) for each team: ownership reads from the gameplay camera (Elder's
   team share is only about 6.5% from the front).
2. Elder and Grove silhouettes read apart (IoU 0.79, limit 0.80).
3. Mire's tail fluke (boxy slabs?), Brute's and Timber's clubs (0.1 to 0.16 m under the ground at rest?).
4. Mire (5.45 m, the largest footprint) on the hill and on bridges: no clipping into rails or posts.
5. A neutral Kaiju: the team plates take the type's crystal colour.
6. Class select: hover one card while the keyboard selects another: one card lit (Builder and Healer side by side).

## 3. Combat (#215 combat feel, wired with #222 and #223 in wave two)
1. Wind-up wedge: every swing draws its real hit shape on the ground for the wind-up (class reach and arc, the fill grows
   to the edge). An enemy wedge that will reach YOU is brighter with an orange edge.
2. Wind-up warning sound (new in wave two): stand in an enemy Warrior's or Miner's reach as it winds up. Expected: one low
   swing cue per wind-up at the swinger (Builder/Healer a light cue, others mid), never a second cue on the release.
3. Stagger and knockback: a hit staggers at most 0.5 s in a row (no stun-lock); a heavy hit throws the target back; a
   Warrior in his armoured wind-up shrugs off a light hit unless a Miner SUNDERED him in the last seconds.
4. Hit colour (wave two: one source, `ImpactPalette`): at GORE Off and Reduced, hit sparks and hit bursts are cream/white,
   never yellow-orange or team red; at FULL gore the blood and pools are violet-brown, never red. Compare a hit on an Azure
   and a Crimson fighter: the hit colour is the same.
5. Your hit target stays solid inside the see-through dither for 0.35 s after you strike it.
6. Ladders: two teammates on one ladder pass each other; an enemy hit knocks a climber off.
7. TNT: 46 damage, one throw every 6 s; standing in your own blast hurts you.
8. Healer MEND reaches the combat-feel range, and with the WIDE MEND hut pick it reaches further (both multiply now).

## 4. Bots (#227 bot brain, ON by default since wave two; terrain verbs wired in wave two)
1. A hostile Kaiju draws a kite squad of 2 (3 in big matches: Farmers first, never Healers or Builders) striking it from
   the side away from the hill; the rest keep hill duty.
2. Group-up: when the enemy holds the disc by 2+, your bots wait at about 15 u from the hill and go in together (at least
   3, or after 12 s), not one by one.
3. Targets: bots go for a Healer in fight range first, then the weakest enemy; no flicker between targets.
4. KAIJU HOLDS NOTHING escort: when your own Kaiju stands on the hill and no enemy is on the disc, two guards stay and the
   rest walk toward the enemy pad; they come back when the Kaiju leaves the ring or an enemy steps on.
5. Terrain jumps (new in wave two): build a 2-high step on a bot route (or dig a 2-deep pit beside one). Expected: a bot
   walking at the 2-level ledge JUMPS up it instead of pushing into the wall for half a second. 3+ high: it goes round.
   Bots do not yet STACK or DIG on purpose (follow-up).
6. Around pits and stacks no bot sticks, and a teammate in a pit shows a team pip over the rim.

## 5. Rules and HUD (#219 comeback + hut choices, #211 terrain height)
1. Hill word under the hill bar: "NO SCORE: YOUR KAIJU" / "NO SCORE: THEIR KAIJU" while a sided Kaiju stands on the hill,
   in brass; else AZURE HOLDS / CRIMSON HOLDS / CONTESTED / NEUTRAL. It never meets the RALLY tag or the vote cards.
2. One banner a match the first time it happens ("YOUR KAIJU IS ON THE HILL ..." / "THEIR KAIJU IS ON THE HILL ...").
3. RALLY tag for the trailing team ("RALLY xN  S s left"), the rally banner once, the rally point line on respawn; the
   respawn wait grows from 2 to 5 s through the match.
4. Hut vote: two cards per level, keyboard, pad and mouse all pick; D-pad zoom and ping are blocked while the vote is open;
   the scoreboard shows each team's picks.
5. Terrain height: STACK prompts STACK, x2, x3 then "too high - 3 max"; a 1-deep pit gives a short slow; climb out of a
   2-deep pit by walking into the wall about 0.5 s (or jump); a height LADDER on a pit wall or stack side is never refused
   "no shortcut here"; standing on a tower being dug drops one level per dig; a Kaiju smashes stacks down to +1; TNT on a
   3-stack takes 2 levels; rope bridge decks still carry fighters.

## 6. Menus, profile and results (#216, #217, #222)
1. Results STORY tab: the lead graph, the turning-point sentence, support awards, the MVP line. Profile > history shows the
   same sentence on the newest row.
2. First match per class: the class goal line at match start and on respawn until done once.
3. Profile v2: an old profile loads, mastery tracks fill, looks unlock only at the real levels; the scoreboard shows a
   human's mastery title and frame ring.
4. Feedback card after a match in a player (keyboard, pad, mouse; SAVE, COPY, OPEN work); none after a rematch.
5. Builder with BRIDGE next to a pillar: the strip names the tool; check the wrong bottom hint seen on 2026-11-17.

## 7. Audio (#223, wired with #219 in wave two)
1. Skybreak score: calm, contested, egg, final minute and berserk change on the bar line; hatch stingers per type.
2. Rally music (new in wave two, `MusicDirector.RallyProbe` reads `Comeback.Earning`): while YOUR team earns rally credit
   the score goes to its rally state; when the other team rallies it does not.
3. Announcer an-23 (rally) and an-24 ("A Kaiju on the hill scores nothing for its team."), each once a match, at the
   ANNOUNCER slider's volume.
4. Dodge, wall, ore, ladder, pit and behind-you sounds. The owner listens before this counts as final.

## 8. Online (#218 rejoin, #224 zero-cost)
1. Direct/LAN room and the relay room both start; the "matches left today" line shows.
2. Drop a player mid-match: "NAME left: a bot took over."; rejoin: "NAME is back." within a few seconds, nobody waits.
3. Two players see each other's cosmetic looks (look ids 40/41).
4. A replay from schema 3 (wave one) is refused in plain words on this build (schema 4).

## 9. Safety and first run (#222)
1. Fresh profile: the notice, then the film; any key skips after 1 s; neither on the second launch.
2. Access tab: INTENSE FLASHING OFF; big moments give a soft pulse with no invert; GORE Off by default and hits look
   neutral (see 3.4).
