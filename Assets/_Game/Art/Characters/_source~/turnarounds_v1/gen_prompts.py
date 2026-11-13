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
    w(f"{k}_side", f"""Image 1 is an accepted FRONT VIEW modelling reference of our game's {c['title']} in a neutral A-pose with empty hands. Image 2 shows the exact camera angle wanted: another class (the Warrior) seen in TRUE SIDE PROFILE. Image 3 is a rough 3D blockout of the {c['title']}'s design (for the parts worn on the back and sides only; ignore its plastic look).

Paint the {c['title']} from image 1 turned on a turntable by a full 90 degrees, so that the WHOLE BODY is in profile exactly like image 2, not only the head: the feet, knees, hips, chest, shoulders and head all face the LEFT edge of the image. This is a narrow side silhouette: the figure is only about half as wide as in image 1. The chest and stomach are seen edge-on as the left outline of the body; the back is the right outline, with everything worn on the back (seen edge-on, sticking out to the RIGHT behind the shoulders). Both boots are seen from the side with the toes pointing LEFT, one boot partly in front of the other. Only ONE arm is visible: the near arm hangs nearly vertically beside the torso, slightly in front of the hip, hand open; the far arm is hidden behind the body. Only one eye is visible. Do NOT paint a front-facing body with a turned head.
- {SAME}
- Every design detail must match image 1: {c['desc']}
- {STYLE}
- {LIGHT}""")
    for v in ("back",):
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

# ---- second-round prompts (after looking at round one) ----
# Side views of the designed classes came back as front bodies with a turned head, even with turntable wording.
# Fix: give an accepted, same-pipeline SIDE view of a bible class as a pose-and-angle template (image 1).
for k, c in DESIGNED.items():
    w(f"{k}_side3", f"""Image 1 is a POSE AND CAMERA TEMPLATE: an accepted modelling reference of a different character of the same game, in TRUE SIDE PROFILE facing left, neutral A-pose. Image 2 is the accepted FRONT VIEW of our {c['title']}, the character to paint. Image 3 is a rough 3D blockout of the {c['title']}'s design (only for the parts worn on the back and sides; ignore its plastic look).

Paint the {c['title']} from image 2 in EXACTLY the body orientation, pose and camera angle of image 1: the whole body in true 90-degree side profile facing the LEFT edge of the image, feet, knees, hips, chest, shoulders and head all in profile, one arm visible hanging beside the body like image 1, the far arm hidden, both boots seen from the side with toes pointing left, a narrow side silhouette, only one eye visible. Everything worn on the back sticks out to the RIGHT behind the shoulders, seen edge-on. Do not copy anything from image 1 except the pose and angle; the outfit, face, hair, colours, head size and height in frame come from image 2.
- Design (must match image 2): {c['desc']}
- {STYLE}
- {LIGHT}""")
    w(f"{k}_prop2", f"""Image 1 is a rough 3D blockout of props from our game (design reference only; ignore its plastic look). Paint ONLY ONE OBJECT: {c['prop']}. A clean prop modelling reference in a painted stylised-3D game concept style with BOLD thick dark outlines. The object ALONE: absolutely NO person, no character, no hands, no body parts, no faint or ghosted figure behind it, nothing else in the picture. Upright in the picture plane, seen from the side with a slight 20-degree turn so its thickness is visible, the whole object in frame, centred, filling about 85 % of the image height. Flat even shadowless lighting, plain flat light grey background (#D9D9D9), no shadow, no glow trail, no text. Orthographic feel.""")
w("demolitions_back2", f"""Image 1 is an accepted FRONT VIEW modelling reference of our game's Demolitions expert in a neutral A-pose with empty hands. Image 2 is the accepted back view of another class (the Warrior), showing the exact camera angle wanted.

Paint the SAME Demolitions expert from image 1 seen directly from BEHIND, facing away from the viewer, same neutral A-pose, arms out about 35 degrees, hands open and empty, the same height in frame. On her back she wears ONE SINGLE round wooden powder keg lying HORIZONTALLY across her shoulder blades (exactly the one keg whose two round ends show beside her shoulders in image 1): a single barrel seen from its side, wooden staves, two brass hoops near its ends, ONE teal band around its middle, held by brown leather shoulder straps, a curled fuse rising from it beside her head. NOT two barrels. Show the back of the leather cap with the goggles strap, the twin puffed pigtails with teal ties, the teal scarf knot, the back of the quilted vest, the cream rolled sleeves, the back of the navy cargo trousers and boots.
- {STYLE}
- {LIGHT}""")
# Three-quarter: the accepted Miner three-quarter (mirrored so it faces left) becomes the pose-and-angle template.
for k, nm in (("farmer", "Farmer"), ("builder", "Builder"), ("healer", "Healer"), ("demolitions", "Demolitions")):
    w(f"{k}_tq3", f"""Image 1 is a POSE AND CAMERA TEMPLATE: an accepted modelling reference of a different character of the same game in a 45-degree THREE-QUARTER FRONT VIEW, facing toward the left, neutral A-pose. Image 2 is the accepted FRONT VIEW and image 3 the accepted SIDE PROFILE of our {nm}, the character to paint.

Paint the {nm} in EXACTLY the body orientation, pose and camera angle of image 1: the whole body (feet, hips, chest, shoulders and head) rotated 45 degrees, halfway between image 2 and image 3; the chest seen at an angle, the character's right shoulder nearer the viewer, the left shoulder partly behind the chest, the face in three-quarter with the nose pointing left, both boots pointing down-left. Do NOT paint a front-facing body with a turned head. Do not copy anything from image 1 except pose and angle; outfit, face, hair, colours, head size and height in frame come from images 2 and 3. Hands open and empty, nothing held, no effects.
- {STYLE}
- {LIGHT}""")
# Kaiju fronts came back at a slight three-quarter angle (the bible draws them that way). Force a symmetric front.
for k, src, crop in (("kaiju_adult", "the art bible's Adult Kaiju turnaround sheet", "its front view"),
                     ("kaiju_hatchling", "the art bible's Kaiju lifecycle sheet", "the neutral hatchling")):
    desc = KADULT if k == "kaiju_adult" else KHATCH
    w(f"{k}_front2", f"""Image 1 is {src}. Image 2 is a close crop of {crop}. Image 3 is our accepted design and NEUTRAL colouring of this creature, but drawn at a slight angle.

Paint the SAME creature as image 3 (same design, colours, crystals, proportions and painted style with bold dark outlines), but as a STRICT, SYMMETRICAL FRONT VIEW for a 3D modeller: the creature faces the viewer dead-on, its spine pointing straight away from the camera, the left and right halves mirror images of each other, both forelimbs equally visible at the same size, both hind legs equally visible, the head centred and facing straight at the viewer, only the tip of the tail peeking out to one side behind a leg. {KPOSE}
- Design: {desc}
- {KNEUTRAL}
- {KLIGHT}""")
# tq3 drifted off-model on the Farmer (new hair, denim, new backpack): the template's identity leaked and the design text was missing.
# Rewrite tq3 with the full design text and a hard identity clause (same image order: template, front, side).
ALL = {k: dict(title=v["page"], desc=v["desc"]) for k, v in C.items()}; ALL.update({k: dict(title=v["title"], desc=v["desc"]) for k, v in DESIGNED.items()})
for k, c in ALL.items():
    nm = c["title"]
    w(f"{k}_tq3", f"""Three images. Image 2 (FRONT VIEW) and image 3 (SIDE PROFILE) are accepted modelling references of our {nm}: THIS is the character to paint, and it must stay IDENTICAL to them: the same face, the same hair shape, colour and volume, the same outfit parts, materials and colours, the same gear worn on the back, the same proportions, head size, line weight and painted rendering. Change NOTHING about the design. Image 1 is only a CAMERA-ANGLE TEMPLATE showing a different character at the wanted angle; take nothing from it except the angle and pose.

Paint the {nm} from images 2 and 3 seen from the in-between angle: a 45-degree THREE-QUARTER FRONT VIEW facing toward the left, exactly the body orientation of image 1: the whole body (feet, hips, chest, shoulders and head) rotated 45 degrees, halfway between image 2 and image 3; the chest seen at an angle, the character's right shoulder nearer the viewer, the left shoulder partly behind the chest, the face in three-quarter with the nose pointing left, both boots pointing down-left. Not a front-facing body with a turned head. Neutral A-pose, arms out about 35 degrees, hands open and empty, nothing held, no effects. The figure the same height in frame as image 2.
- The design, exactly as in images 2 and 3: {c['desc']}
- {STYLE}
- {LIGHT}""")
# Round three. The Kaiju side views and two class three-quarters came back as front views: the accepted FRONT reference anchors the angle.
# Fix: the angle template goes FIRST, the character's front and back/side follow as design-only references.
for k, desc in (("kaiju_adult", KADULT), ("kaiju_hatchling", KHATCH)):
    for v, word in (("side2", "a TRUE 90-degree SIDE PROFILE: the creature walks toward the left edge of the image; we see only its right flank; the head in profile at the left with one eye visible; the near legs in front of the far legs; the tail stretching out to the RIGHT; the belly seen edge-on as the bottom outline"),
                    ("tq2", "a 45-degree THREE-QUARTER FRONT VIEW: body turned halfway between a front view and a side profile, the face in three-quarter, one shoulder nearer the viewer, the tail visible behind on the far side")):
        w(f"{k}_{v}", f"""Image 1 is an ANGLE TEMPLATE only: a drawing of a creature of the same species at the wanted camera angle (ignore its colours, crystals' colour and exact pose). Image 2 is the accepted FRONT VIEW and image 3 the accepted REAR VIEW of our creature: they fix its design, NEUTRAL acid-lime colouring, proportions and painted style. Do NOT paint another front view.

Paint our creature (images 2 and 3) seen from {word}. If image 1 faces right, still paint it facing LEFT. Neutral standing pose for rigging: all four legs straight and apart, weight even, the tail extended and fully visible, head level, mouth closed, calm. The creature the same size in frame as image 2.
- Design: {desc}
- {KNEUTRAL}
- Same bold dark outlines as images 2 and 3.
- {KLIGHT}""")
for k, c in ALL.items():
    nm = c["title"]
    w(f"{k}_tq5", f"""Image 1 is the accepted SIDE PROFILE and image 2 the accepted FRONT VIEW of our {nm}, the character to paint: keep the design IDENTICAL (face, hair, outfit, colours, gear on the back, proportions, head size, line weight). Image 3 is a different character of the same game at EXACTLY the wanted camera angle; take only the angle from it.

Start from image 1 and turn the {nm} 45 degrees back toward the viewer, so the result is a true 45-degree THREE-QUARTER FRONT VIEW facing left like image 3. Concrete checks: the torso is clearly narrower than in image 2; the character's right shoulder and right arm are nearer the viewer and larger, the left arm is partly hidden behind the body; the face is in three-quarter with the nose pointing left and the far cheek partly hidden; the near (right) boot points down-left and the far boot is partly behind it; part of the gear worn on the back shows behind the right side of the body. It must NOT look like image 2 with a turned head. Neutral A-pose, hands open and empty, nothing held, no effects; the same height in frame as image 2.
- The design, exactly as in images 1 and 2: {c['desc']}
- {STYLE}
- {LIGHT}""")
# Round four: two angle templates (the accepted Warrior and Miner three-quarters, both facing left) outvote the front reference.
for k, c in ALL.items():
    nm = c["title"]
    w(f"{k}_tq6", f"""Images 1 and 2 are ANGLE TEMPLATES: two DIFFERENT characters of our game, both drawn at exactly the wanted camera angle, a 45-degree THREE-QUARTER FRONT VIEW facing left, neutral A-pose. Take ONLY the angle, pose and framing from them. Image 3 (FRONT VIEW) and image 4 (SIDE PROFILE) are the accepted references of our {nm}: the character to paint, with an IDENTICAL design (face, hair shape, colour and volume, outfit parts and colours, gear worn on the back, proportions, head size, line weight).

Paint the {nm} of images 3 and 4 at the same angle as images 1 and 2: the whole body (boots, hips, chest, shoulders, head) turned 45 degrees to face the lower left, the torso narrower than in image 3, the right shoulder and arm nearer the viewer, the left arm partly behind the body, the face in three-quarter with the nose pointing left, the back gear showing behind the right side of the body. Standing still in the neutral A-pose like images 1 and 2 (NOT walking), arms out about 35 degrees, hands open and empty, nothing held, no effects; the same height in frame as image 3.
- The design, exactly as in images 3 and 4: {c['desc']}
- {STYLE}
- {LIGHT}""")
# Round five: the hatchling side/three-quarter came back as a slender lizard without the boulder forefists of our accepted front and rear.
HANAT = ("Keep the EXACT anatomy of images 2 and 3: a compact, broad, chunky body; two HUGE grey boulder-plated forearms and fists planted wide "
         "apart (the biggest shapes of the silhouette, as tall as the body); short sturdy hind legs; a big round head with a tan muzzle and big "
         "yellow-green eyes; a tan segmented belly; dark slate plates with lime moss and lime crystal back spikes; a short tail. NOT a slender "
         "long-bodied lizard, NOT a stegosaurus.")
for v, word in (("side4", "a TRUE 90-degree SIDE PROFILE facing the LEFT edge of the image: we see its right flank, the near boulder forefist in front, the far one partly hidden behind it, the head in profile at the left with one eye, the short tail to the right"),
                ("tq4", "a 45-degree THREE-QUARTER FRONT VIEW facing left: body turned halfway between the front view and a side profile, the near (right) boulder forefist larger and nearer the viewer, the far one partly behind the body, the face in three-quarter, the tail visible behind on the right")):
    w(f"kaiju_hatchling_{v}", f"""Image 1 is an ANGLE TEMPLATE only: the adult of the same species at the wanted camera angle (ignore its size, proportions and pose). Image 2 is the accepted FRONT VIEW and image 3 the accepted REAR VIEW of our Kaiju HATCHLING: they fix its design, anatomy, NEUTRAL acid-lime colouring and painted style.

Paint our hatchling (images 2 and 3) seen from {word}. {HANAT} Neutral standing pose for rigging, limbs straight and apart, mouth closed, calm. The same size in frame as image 2.
- {KNEUTRAL}
- Same bold dark outlines as images 2 and 3.
- {KLIGHT}""")
