# Builds jobs.json for gen.py (hud-art-1). Prompts are original; references are our own turnarounds and 3D renders.
import json, os
T = os.path.expanduser("~/critical-mass-unity-art-turnarounds/Assets/_Game/Art/Characters/_source~")
TA = T + "/turnarounds_v1/turnaround"
STYLE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "refs/style-3d.jpg")
WAR = [T + "/warrior_v8/turnaround/warrior-v8-front-2k.jpg",
       "/Users/wilsonguenther/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/warrior-v8/props.jpg"]
def cls_refs(c, prop):
    return [f"{TA}/{c}/{c}-three-quarter-2k.jpg", f"{TA}/{c}/{c}-front-2k.jpg", f"{TA}/{c}/{c}-prop-{prop}-2k.jpg"]
CLASSES = {
    "warrior": (WAR, "raising the stone-blue cleaver high for a downward chop, round wooden shield braced forward, mouth open in a battle shout"),
    "farmer": (cls_refs("farmer", "staff"), "leaping forward mid-attack, both hands on her staff (exactly the simple wooden staff of the prop reference image, no blade, no hose) sweeping it down diagonally across her body in a powerful hooking strike, hair and scarf flying back, knees bent, fierce determined grin; a dynamic, off-balance action pose, not standing still"),
    "miner": (cls_refs("miner", "pick-hammer"), "lifting the pick-hammer high over the shoulder about to smash it down, knees bent, gritted teeth"),
    "demolitions": (cls_refs("demolitions", "dynamite"), "holding a lit dynamite bundle up in one raised hand ready to throw, other fist clenched, wild cheeky grin, barrel on her back"),
    "builder": (cls_refs("builder", "mallet"), "swinging the big wooden mallet up over the shoulder mid-strike, wide stance, confident shout"),
    "healer": (cls_refs("healer", "lantern"), "holding the lantern high in one hand and stretching the other hand forward, leaf wings spread wide, calm focused expression"),
}
TEAM = {"azure": "Keep the team colour exactly as the turnaround shows it: teal-cyan (#16C7D9) on the scarf, sash, gems and trims.",
        "crimson": "Team change: every teal-cyan team accent of the turnaround (scarf, sash, gems, trims, glow) becomes crimson red (#FF5A5F). Everything else (skin, hair, leather, metal, cloth) stays identical to the turnaround."}
COMMON = ("The result must look like a real-time 3D render from our stylised 3D game, NOT a flat 2D cartoon drawing: solid 3D volumes "
          "with hand-painted texture detail (wood grain, worn metal, cloth folds, stone chips), soft toon shading in three value steps with "
          "gentle painted gradients inside each step, a soft ambient-occlusion darkening in creases, a thin warm rim light, light from the "
          "upper left, and a crisp dark navy (#18233B) outline of even width around the whole silhouette (like an inverted-hull outline in a "
          "game engine), chunky toy-like proportions exactly like the references. Match the design of the turnaround exactly (face, hair, costume, "
          "colours, proportions, prop); match the rendering style of the 3D render reference. Full body, three-quarter view, body turned toward "
          "the right side of the image, the whole figure inside the frame with a clear margin on every side (head, hands, prop and feet never "
          "touch or leave the edge), figure about 86% of the image height. Background: perfectly flat pure white (#FFFFFF), no floor, no cast "
          "shadow, no gradient, no glow, no particles, no speed lines, no text, no border, no frame.")
jobs = []
for c, (refs, pose) in CLASSES.items():
    for t in ("azure", "crimson"):
        jobs.append({"name": f"cut-{c}-{t}", "model": "gemini-3-pro-image", "aspect": "1:1", "size": "2K", "refs": refs + [STYLE],
                     "prompt": f"Reference images: the first images are the approved design turnaround of our {c} character and its hand-held prop; "
                               f"the last image shows our 3D game render style. Create a dynamic action pose for a fighting-game style special-move "
                               f"cut-in: the {c} is {pose}. {TEAM[t]} {COMMON}"})
KA = [f"{TA}/kaiju_adult/kaiju_adult-three-quarter-2k.jpg", f"{TA}/kaiju_adult/kaiju_adult-front-2k.jpg"]
KSTATE = {
    "neutral": ("its crystals and the cracks between its rock plates glow lime green (#B8E833)", "lunging toward the viewer with both arms spread wide and claws open, head thrust forward, jaws wide open in a mighty roar; it fills the frame"),
    "azure": ("its crystals and the cracks between its rock plates now glow bright cyan (#13F1F4), the colour of the team it has sided with", "leaning forward on one fist, head turned toward the viewer, roaring a loyal battle roar"),
    "crimson": ("its crystals and the cracks between its rock plates now glow warm crimson red (#F2563F), the colour of the team it has sided with", "leaning forward on one fist, head turned toward the viewer, roaring a loyal battle roar"),
    "berserk": ("corrupted: its crystals and the cracks between its rock plates glow hot magenta (#EE2CC0), its eyes burn magenta, a few rock plates are cracked", "the exact same creature and the same body proportions as the image before the style image (same olive skin, tan belly, grey rock plates, crystal crest), now hunched lower and angrier, arms spread wide with claws open, jaws wide open in a furious berserk roar"),
}
for k, (glow, pose) in KSTATE.items():
    extra = [os.path.join(os.path.dirname(os.path.abspath(__file__)), "refs/kaiju-base.png")]
    jobs.append({"name": f"kaiju-{k}", "model": "gemini-3-pro-image", "aspect": "1:1", "size": "2K", "refs": KA + extra + [STYLE],
                 "prompt": "Reference images: the first two are the approved design turnaround of our Kaiju (a stocky rock-plated creature with "
                           "crystal spikes along its back and tail); the last shows our 3D game render style (use it for the rendering only, not the "
                           "character)" + ("; the image before the style image is the same Kaiju already rendered in our style: keep that exact creature, rendering and proportions" if extra else "") + f". Create the Kaiju for a full-screen dramatic cut-in: it is {pose}; {glow}. Keep its design exactly: body "
                           "shape, plate pattern, tan belly, crystal spikes, tail. " + COMMON.replace("body turned toward the right side", "body turned toward the right side")})
json.dump(jobs, open("jobs.json", "w"), indent=1)
print(len(jobs), "jobs")
