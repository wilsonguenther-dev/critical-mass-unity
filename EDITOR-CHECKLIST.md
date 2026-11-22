# Batch 16 combined: in-Editor checklist (only eyes can judge these)

Branch integration/batch-16-cand1 (head: see RESUME.md). Four pieces: physics #231, duels + CANCEL #230, mechanics #232, chat #234.
Full rules unless a line says otherwise. Tick a line only after you saw it in Play mode or a player build.

## Look first
1. First person hidden (owner decision): F5 in a match and Settings > CAMERA go TOP-DOWN <-> THIRD PERSON only; no FIRST PERSON
   anywhere; a profile that had first person saved opens in third person.
2. Hut vote keys while typing (chat): open the chat input during a hut vote and type `[` and `]`: no vote is cast; the same with the
   chat panel open and the quick-chat wheel held. Pad D-pad left/right still vote when chat is closed.
3. CANCEL feint flick (#230): wind up and press CANCEL (mouse side button, P, backslash, LB): a ring snaps inward on the fighter, a
   placeholder light swing sound plays (placeholder, the owner listens); no hit lands.
4. "7/10 ON HILL" count and its tint (#232): the team's weighted count with the cap; a Warrior adds 2; "THE HILL IS FULL" when capped.
5. Own Kaiju waiting post (#232): an owned Kaiju with no target waits on the hill edge OUTSIDE the ring, facing in; "YOUR KAIJU IS
   ON THE CIRCLE" only when it is on the ring.

## Physics (#231)
6. X-ray rings: the Kaiju's dashed 1.3 u footprint ring and fighters' rings show under the canopy and under the Kaiju.
7. Facing smoothing: a fighter's drawn turn settles in 60-90 ms, no wobble; the swing snaps to its direction on contact.
8. Refused jump: at the island edge a refused jump gives a 0.3 s stumble, a soft "refused" sound (own player only) and a 0.6 s edge glow.
9. Ladder foot: a fighter standing 0.1 u inside a ladder foot has its feet on the ground (no 0.28 u float).
10. Speed dust: footstep dust grows with speed (none standing, more at a run).
11. Death fling: a fighter killed with no clear hit direction still falls away along a sensible direction (no fling into the camera).

## Duels and combat (#230)
12. Attack telegraphs with the longer wind-ups (Miner 0.35 s, Farmer 0.31 s): the wedge fill reads in time; the Warrior is a touch slower.
13. Tips (TrialText) and the controls screen row for CANCEL read right; the first-run CANCEL hint shows once.
14. Miner text: the Miner's card / tips text matches the slower swing (mining unchanged).

## Mechanics (#232)
15. "NO SCORE" pill under the hill bar while a sided Kaiju stands on the hill; never overlaps the RALLY tag or vote cards.
16. Hut vote timing: with a human on the team, bots vote in the last 3 s or after the humans; the window is 10 s (7 s people only).
17. "A BOT IS PLAYING FOR YOU" panel after 15 s with no input; it goes the first input after.
18. "BEHIND: YOU COME BACK CLOSER" / "RALLY: YOU ARE BEHIND" and "WHY YOU WENT DOWN" read right and do not stack.
19. Builder keep-out ring (7 u from the hill) on the wall ghost.

## Chat (#234)
20. Chat panel on the pause screen: scroll-back shows; team-coloured names.
21. Esc: closes the chat input first (one press), a second Esc opens the pause menu; no stray game input on the closing frame.
22. Quick-chat wheel: hold D-pad right / Y; the callout marker shows for the team; D-pad zoom only when the wheel does not own it.
