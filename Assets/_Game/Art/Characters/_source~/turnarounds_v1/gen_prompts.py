"""Writes every turnarounds-v1 prompt into prompts/. usage: python3 gen_prompts.py"""
import os
H = os.path.dirname(os.path.abspath(__file__)); P = os.path.join(H, "prompts"); os.makedirs(P, exist_ok=True)

STYLE = ("Rendering and proportions exactly as on the art bible page: the same painted stylised-3D game concept style, "
         "with BOLD, THICK dark outlines as heavy as on the bible page (do not thin them), chunky readable shapes and soft painted volume. "
         "Keep the BIG chibi head exactly as large relative to the body as the bible draws it (do NOT shrink the head), and the hair "
         "(or hat) as tall and voluminous as the bible draws it (do NOT tidy or shrink it). Broad shoulders, short sturdy legs.")
POSE = ("Neutral relaxed A-POSE: both arms held straight down and out to the sides at about 35 degrees from the body, clearly separated "
        "from the torso and hips with background visible between arm and body; elbows nearly straight; both hands open and relaxed, "
        "fingers together, palms facing the thighs. Legs straight, feet slightly apart (about shoulder width), toes pointing forward. "
        "Standing upright, looking straight ahead, calm slight smile. NOTHING held in the hands: no tool, no weapon, no effects, no glow trails, no debris.")
LIGHT = ("Flat, even, shadowless studio lighting; plain flat light grey background (#D9D9D9); no ground shadow, no text, no labels, no border. "
         "Orthographic feel: no perspective distortion, camera at chest height. Full body from the top of the hair or hat to the soles of the "
         "boots, centred, with a small margin all round.")
SKEL = ("Image {n} is the accepted front modelling reference of ANOTHER class of the same game (the Warrior). Use it ONLY for the body "
        "skeleton and the pose: match its overall figure height in the frame, shoulder height and width, arm length and angle, hand position, "
        "crotch height, leg length and foot spacing, so that one shared skeleton fits both characters. Do NOT copy its outfit, face, hair or colours. "
        "The head must be as big as the bible draws it, a little bigger than the Warrior reference's head.")
TURN = {
 "side": ("exact 90-degree SIDE PROFILE, like a 3D model turned on a turntable by 90 degrees: the whole body (feet, hips, chest, shoulders and "
          "head) faces the LEFT edge of the image. Only the character's right side is visible. Because the arms are raised out to the sides in "
          "the frontal plane, in profile the near arm hangs nearly vertically beside the torso, slightly in front of the hip, and the far arm is "
          "hidden behind the body. Both boots point to the left. The chest is seen edge-on; this must NOT show the chest from the front."),
 "back": ("BACK VIEW: the character seen directly from behind, facing away from the viewer: the back of the head and hair, everything worn on "
          "the back fully visible, the backs of the boots. Arms held straight down and out to the sides at about 35 degrees, hands open and empty."),
}
SAME = ("Paint the SAME CHARACTER in EXACTLY the same neutral A-pose, outfit, proportions, head size, hair volume, colours, line weight and "
        "rendering as the accepted front view, the figure the same height in frame, hands open and empty, nothing held, no effects.")

C = {
 "farmer": dict(title="Farmer", page="Farmer", desc=(
   "the young woman Farmer with big curly brown hair in a high puffy ponytail and a teal hair band, aviator goggles pushed up on her head, "
   "her face and freckles, cream short-sleeved shirt, navy dungarees with teal trim and brass buckles, brown leather straps and belt with pouches, "
   "brown work gloves, rolled cuffs, heavy brown boots with cream cuffs, and the ROUND GLOWING ACID-LIME BIOMASS BACKPACK TANK in its brass and "
   "leather frame on her back (worn, keep it). Same colours as the sheet."),
   prop="the Farmer's HOOKED HARVESTING STAFF (crook-topped staff with the curved hook blade, the glowing lime biomass bulb at the top, the brass "
        "fittings, the leather wrap and the hose/cable), exactly as on the sheet"),
 "miner": dict(title="Miner", page="Miner", desc=(
   "the stocky bearded Miner with his full brown beard and moustache, the stone-and-leather helmet with the glowing BLUE CRYSTAL LAMP on the front, "
   "navy shirt with cream sleeves, the blue crystal chest plate in brass, brown leather harness, belt and pouches with brass buckles, stone shoulder "
   "and knee pads, brown gloves, heavy brown boots, and the SQUARE WOODEN-AND-BRASS BACKPACK FRAME on his back (worn, keep it). Same colours as the sheet."),
   prop="the Miner's PICK-HAMMER in its striking state (curved grey stone pick head with the glowing blue crystal point, the square hammer face, "
        "brass fittings, the navy-wrapped haft and brass pommel), exactly as on the sheet"),
}
DESIGNED = {
 "builder": dict(title="Builder", desc=(
   "the Builder: a sturdy cheerful man with a big round face, an Ivy-style flat cap with a teal band and a pencil behind his right ear, thick copper "
   "eyebrows and a big copper walrus moustache with curled tips, an open grin; navy shirt with sleeves rolled above the elbow and teal cuffs, bare "
   "chunky forearms, big leather work gloves; a canvas carpenter's apron with teal trim border and stitched pockets holding a brass carpenter's square, "
   "a folding rule and a chisel; a leather tool belt with brass buckle and nail pouch; a coil of rope on his left hip; a wooden A-frame pack with a "
   "FOLDED WOODEN LADDER lashed diagonally across his back on teal straps (worn, keep it); grey stone knee pads, cream trousers with rolled cuffs, heavy "
   "brown boots with brass toe caps."),
   prop="the Builder's OVERSIZED WOODEN MALLET: a big banded barrel-shaped wooden head (about 1.7 times as long as it is thick) with dark end grain and "
        "brass bands, a glowing acid-lime biomass gem in a brass bezel on the face of the head, a wooden handle with a leather grip, a teal wrap and a brass pommel"),
 "healer": dict(title="Healer", desc=(
   "the Healer: a young woman with silver hair in two round buns with teal ribbons, a short braid and a brass circlet with a lime brow gem; big eyes, "
   "blush, gentle smile; LIVING-LEAF WINGS on her back: three broad chunky leaf blades per side in deep green with glowing acid-lime veins, fanning up "
   "and out from a brass hinge boss between her shoulder blades (worn, keep them, rising beside her head in the front view); a fitted grey stone-plate "
   "bodice with brass trim and a big lime heart-gem; small round stone pauldrons with brass rims; a teal collar stole; a linen tabard with an embroidered "
   "leaf emblem over a navy skirt; a belt with three lime healing vials and pouches; bandage arm wraps; on her right forearm a brass BIOMASS GAUNTLET with "
   "a glowing lime orb held by four brass claws (worn, keep it); armoured boots with stone toe caps and brass cuffs."),
   prop="the Healer's ornate CLEANSING LANTERN: a brass lantern with a tall brass spire and top ring for holding, a crown of six brass leaves, claw feet, "
        "glass panes and a warm cream-gold light inside"),
 "demolitions": dict(title="Demolitions", desc=(
   "the Demolitions expert: a lean young woman with twin big puffed pigtails tied with teal hair ties (slightly singed tips), a short stitched leather cap "
   "with a rolled rim and big round brass goggles with teal lenses pushed up on it; green eyes, arched brows, a wide gap-tooth grin, soot smudges on her "
   "cheeks; a teal scarf; a padded grey quilted vest with brass-buckled straps; a bandolier across the chest holding three small 3-stick dynamite bundles "
   "with teal wrapper bands; rolled cream sleeves, a teal armband, bare forearms, fingerless leather gloves; navy cargo trousers with pouches; heavy boots; "
   "a ROUND WOODEN POWDER KEG on her back with brass hoops, a teal middle band and a curled fuse rising beside her head (worn, keep it)."),
   prop="the Demolitions expert's THROWABLE DYNAMITE BUNDLE: three thick red-brown dynamite sticks tied together with a teal wrapper band and twine, a short "
        "curled unlit fuse on top"),
}

def w(name, text):
    open(os.path.join(P, name + ".txt"), "w").write(text.strip() + "\n")

for k, c in C.items():
    w(f"{k}_front", f"""The first image is the character model sheet for the "{c['page']}" class from our game's art bible (painted stylised 3D-game concept art). The second image is a close crop of the same {c['page']}'s front view from that sheet. {SKEL.format(n='3')}

Paint THIS EXACT SAME {c['page'].upper()}, by the same painter, as a clean modelling reference for a 3D artist: FRONT VIEW, facing the viewer straight on.
- {POSE}
- Identical design to the sheet: {c['desc']}
- {STYLE}
- {LIGHT}""")
    for v in ("side", "back"):
        w(f"{k}_{v}", f"""The first image is the character model sheet for the "{c['page']}" class from our game's art bible. The second image is an accepted FRONT VIEW modelling reference of this same {c['page']} in a neutral A-pose with empty hands. The third image is the bible's own {'profile' if v=='side' else 'back'} view of the {c['page']}.

{SAME} Seen from a different angle: {TURN[v]}
- Every design detail must match the second image and the bible sheet: {c['desc']}
- {STYLE}
- {LIGHT}""")
    w(f"{k}_tq", f"""Image 1 is a FRONT view and image 2 is a SIDE PROFILE view of the same game character (the {c['page']}) in a neutral A-pose (same character, same painter). Image 3 is the art bible's three-quarter drawing of this character (holding a tool, ignore it).

Paint the in-between angle: the SAME character in the SAME neutral A-pose, rotated exactly halfway between image 1 and image 2, a 45-degree THREE-QUARTER FRONT VIEW, facing toward the left side of the image like image 2 does, but turned halfway back toward the viewer: the chest seen at an angle, the character's right shoulder nearer the viewer, the left shoulder partly behind the chest, the face in three-quarter with the nose pointing left, the boots pointing down-left.
Same outfit, colours, head size, hair volume, line weight and proportions as images 1 and 2; arms out about 35 degrees from the body, hands open and empty; no tool, no effects. Full body, centred, the same height in frame as image 1. Flat even shadowless lighting, plain flat light grey background (#D9D9D9), no ground shadow, no text. Orthographic feel.""")
    w(f"{k}_prop", f"""Image 1 is the art bible sheet for our game's {c['page']}; image 2 is a close-up of the tool on that sheet. Paint ONLY {c['prop']}, as a clean prop modelling reference, by the same painter, identical design, with the same bold dark outlines as the sheet. Show it alone, nobody holding it, upright in the picture plane (handle at the bottom), seen from the side with a slight 20-degree turn so its thickness is visible, the whole object in frame, centred, filling about 85 % of the image height. Flat even shadowless lighting, plain flat light grey background (#D9D9D9), no shadow, no glow trail, no text. Orthographic feel.""")

for k, c in DESIGNED.items():
    w(f"{k}_front", f"""Image 1 is the art bible sheet for our game's Warrior class and image 2 the bible sheet for the Farmer class: they define the painting style. Image 3 is a rough 3D blockout turnaround of the {c['title']} class, which has no bible page yet: use it ONLY for the design (outfit parts, gear and colours), NOT for its plastic 3D look or its proportions. {SKEL.format(n='4')}

Paint the {c['title'].upper()} as a new character sheet entry by the SAME painter as images 1 and 2, as a clean modelling reference for a 3D artist: FRONT VIEW, facing the viewer straight on.
- {POSE}
- Design: {c['desc']} Bible palette: warm cream, navy, brown leather, brass, grey carved stone, with teal team colour only on trim, bands and small accents, and acid-lime only for glowing biomass.
- {STYLE}
- {LIGHT}""")
    for v in ("side", "back"):
        w(f"{k}_{v}", f"""Image 1 is an accepted FRONT VIEW modelling reference of our game's {c['title']} in a neutral A-pose with empty hands. Image 2 is the accepted turnaround {'side profile' if v=='side' else 'back view'} of another class (the Warrior) in the same pose, showing the exact camera angle wanted. Image 3 is a rough 3D blockout of the {c['title']}'s design (for the parts worn on the back and sides only; ignore its plastic look).

{SAME} Seen from a different angle: {TURN[v]}
- Every design detail must match image 1: {c['desc']}
- {STYLE}
- {LIGHT}""")
    w(f"{k}_tq", f"""Image 1 is a FRONT view and image 2 is a SIDE PROFILE view of the same game character (the {c['title']}) in a neutral A-pose (same character, same painter). Image 3 is another class (the Warrior) at the exact three-quarter angle wanted.

Paint the in-between angle: the SAME {c['title']} in the SAME neutral A-pose, rotated exactly halfway between image 1 and image 2, a 45-degree THREE-QUARTER FRONT VIEW, facing toward the left side of the image like image 2 does, but turned halfway back toward the viewer, exactly the body orientation of image 3: the chest seen at an angle, the character's right shoulder nearer the viewer, the left shoulder partly behind the chest, the face in three-quarter with the nose pointing left, the boots pointing down-left.
Same outfit, colours, head size, hair volume, line weight and proportions as images 1 and 2 (do not copy anything from image 3 except the angle); arms out about 35 degrees from the body, hands open and empty; no tool, no effects. Full body, centred, the same height in frame as image 1. Flat even shadowless lighting, plain flat light grey background (#D9D9D9), no ground shadow, no text. Orthographic feel.""")
    w(f"{k}_prop", f"""Image 1 is the accepted front modelling reference of our game's {c['title']} (painted in our art bible's style). Image 2 is a rough 3D blockout of the {c['title']}'s props (design only, ignore its plastic look). Paint ONLY {c['prop']}, as a clean prop modelling reference, in the same painted style with the same bold dark outlines as image 1. Show it alone, nobody holding it, upright in the picture plane (handle or ring at the top or bottom as natural), seen from the side with a slight 20-degree turn so its thickness is visible, the whole object in frame, centred, filling about 85 % of the image height. Flat even shadowless lighting, plain flat light grey background (#D9D9D9), no shadow, no glow trail, no text. Orthographic feel.""")

KLIGHT = ("Flat, even, shadowless studio lighting; plain flat light grey background (#D9D9D9); no ground shadow, no text, no labels, no border, no other "
          "characters, no effects, no debris. Orthographic feel, no perspective distortion, camera at mid-body height. The whole creature in frame "
          "including the full tail, centred, with a small margin all round.")
KPOSE = ("a NEUTRAL STANDING POSE for rigging: standing squarely on all four limbs, the four legs straight and spread apart so background shows "
         "between each limb and the body, weight even, the tail extended out behind and clearly visible, head level, MOUTH CLOSED with a small smug "
         "expression, calm.")
KNEUTRAL = ("NEUTRAL colouring (not aligned to either team): every crystal, crystal crown, spine ridge and glowing crack is ACID LIME / biomass green "
            "(like the egg's lime seams and the neutral hatchling), never cyan, teal, blue, red or violet.")
KADULT = ("the adult Kaiju: a heavy knuckle-walking quadruped with gorilla-like weight, shoulders about 1.5 times as wide as the hips, long thick "
          "forelimbs with huge boulder fists, short hind legs, the small head carried low between the shoulders, dark slate rock plates with glowing "
          "cracks, lime moss on the upward-facing plates, a tan segmented belly, a tall crystal crown and spine ridge running down to a crystal-tipped "
          "tail, yellow eyes, a small expressive face.")
KHATCH = ("the neutral Kaiju hatchling: a small cute lizard/turtle quadruped, dark slate rock plates, lime moss tufts, acid-lime crystal back spikes, "
          "a big round cute face with big yellow-green eyes, a tan belly, stubby legs, a short tail.")
for k, (src, desc) in {"kaiju_adult": ("the art bible's Adult Kaiju turnaround sheet", KADULT),
                       "kaiju_hatchling": ("the art bible's Kaiju lifecycle sheet", KHATCH)}.items():
    crop = "its front view" if k == "kaiju_adult" else "the neutral hatchling on it"
    w(f"{k}_front", f"""Image 1 is {src} (painted stylised 3D-game concept art). Image 2 is a close crop of {crop}.

Paint THIS EXACT SAME CREATURE by the same painter, as a clean modelling reference for a 3D artist: FRONT VIEW, facing the viewer straight on, in {KPOSE}
- Identical anatomy and design to the sheet: {desc}
- {KNEUTRAL}
- Same painted style with BOLD, THICK dark outlines as heavy as on the sheet; keep the sheet's proportions exactly (do not slim it or enlarge the head).
- {KLIGHT}""")
    for v, word in (("side", "exact 90-degree SIDE PROFILE, like a 3D model turned on a turntable by 90 degrees: the head and the whole body face the LEFT edge of the image, the near legs in front of the far legs, the full tail extending to the right"),
                    ("back", "REAR VIEW: seen directly from behind, the tail toward the viewer and fully visible, the back plates, spine ridge and crystal crown from behind")):
        w(f"{k}_{v}", f"""Image 1 is {src}. Image 2 is an accepted FRONT VIEW modelling reference of this same creature in a neutral standing pose. Image 3 is the bible's own drawing to show the angle and the anatomy from that side.

Paint the SAME CREATURE in EXACTLY the same neutral standing pose, proportions, colours and rendering as image 2, seen from a different angle: {word}. Legs straight and apart, mouth closed, calm. The creature the same size in frame as in image 2.
- Same design: {desc}
- {KNEUTRAL}
- Same bold dark outlines as the sheet.
- {KLIGHT}""")
    w(f"{k}_tq", f"""Image 1 is a FRONT view and image 2 is a SIDE PROFILE view of the same game creature in a neutral standing pose (same creature, same painter). Image 3 is the bible's drawing of this creature (ignore its pose and colours, use it for anatomy only).

Paint the in-between angle: the SAME creature in the SAME neutral standing pose, rotated exactly halfway between image 1 and image 2, a 45-degree THREE-QUARTER FRONT VIEW, facing toward the left side of the image like image 2 does, but turned halfway back toward the viewer: the face in three-quarter pointing left, the right shoulder nearer the viewer, the tail visible behind on the right. Legs straight and apart, mouth closed.
Same design, NEUTRAL acid-lime crystals and cracks, proportions and bold outlines as images 1 and 2. The same size in frame as image 1. {KLIGHT}""")
print(len(os.listdir(P)), "prompts")
