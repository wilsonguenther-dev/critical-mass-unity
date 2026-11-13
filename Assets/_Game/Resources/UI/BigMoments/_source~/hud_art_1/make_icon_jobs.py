# hud-art-1 icon sheets: one generation per family so every icon of a family shares one drawing style.
import json, os
H = os.path.dirname(os.path.abspath(__file__))
T = os.path.expanduser("~/critical-mass-unity-art-turnarounds/Assets/_Game/Art/Characters/_source~")
STY = ("Style: our stylised 3D game look (hand-painted 3D render, soft toon shading in three value steps, light from the upper left, "
       "a bold dark navy (#18233B) outline of even width around every object), chunky readable shapes that stay clear when shown at 40 pixels. "
       "Background perfectly flat pure white (#FFFFFF), no shadows, no glow halos, no text, no labels, no numbers, no frames, no circles or tiles "
       "behind the objects. Each item sits centred in its own cell of an invisible grid with wide white space between items; items never touch.")
jobs = [
 {"name": "sheet-busts", "refs": [H + "/refs/classes-cutins.jpg"],
  "prompt": "The reference shows our six game characters (top row: warrior, farmer, miner; bottom row: demolitions, builder, healer). Draw a "
            "head-and-shoulders portrait of each one for HUD icons, in the same 3-by-2 arrangement and the same order, each facing three-quarter "
            "to the right with a confident expression, same faces, hair, headgear and team-teal scarves as the reference, cut off cleanly at "
            "mid-chest. " + STY},
 {"name": "sheet-attacks", "refs": [T + "/turnarounds_v1/turnaround/miner/miner-prop-pick-hammer-2k.jpg", T + "/turnarounds_v1/turnaround/builder/builder-prop-mallet-2k.jpg",
                                     "/Users/wilsonguenther/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/turnarounds-v1/props.jpg",
                                     "/Users/wilsonguenther/Obsidian/Wilson-Brain/Projects/Kaiju-Demo/unity-port/warrior-v8/props.jpg"],
  "prompt": "Six weapon icons for a game HUD, from our own prop designs in the references, in a 3-by-2 grid: top row: the warrior's stone-blue "
            "cleaver, the farmer's wooden staff with a hook, the miner's pick-hammer; bottom row: the demolitions expert's dynamite bundle with a "
            "lit fuse, the builder's big wooden mallet, the healer's brass lantern with a soft teal flame. Each object drawn diagonally (handle lower "
            "left, head upper right), big in its cell. " + STY},
 {"name": "sheet-abilities", "refs": [H + "/refs/classes-cutins.jpg"],
  "prompt": "Six ability icons for a game HUD in a 3-by-2 grid, each a single bold symbolic object: top row: a round wooden shield with a "
            "stone rim and a glowing teal (#16C7D9) gem, raised as a guard; a cluster of thick green roots bursting up and curling; a cracked "
            "stone wall block splitting apart with flying chips; bottom row: a round iron landmine with a blinking teal light; a wooden plank "
            "barricade with a hammer resting on it; a pair of leaf wings around a small glowing teal heart. Use teal (#16C7D9) only for the "
            "small glowing accents. " + STY},
 {"name": "sheet-resources", "refs": [],
  "prompt": "Four resource icons for a game HUD in a 2-by-2 grid: top left a thick gold coin with an embossed crystal mark; top right a bundle "
            "of three chopped wooden logs tied with rope; bottom left a wooden bucket full of bright blue water with a splash; bottom right a "
            "harvest basket with bread, carrots and an apple. " + STY},
 {"name": "sheet-kaiju", "refs": [H + "/refs/kaiju-base.png", T + "/turnarounds_v1/turnaround/kaiju_hatchling/kaiju_hatchling-front-2k.jpg",
                                   os.path.expanduser("~/cm-wt-hud-art-1/Assets/_Game/Resources/Story/egg.png")],
  "prompt": "Four badge icons for a game HUD in a 2-by-2 grid, from our own designs in the references (first: the adult Kaiju, second: the "
            "hatchling, third: the egg): top left the egg, calm and whole, speckled stone shell; top right the same egg cracking open, bright "
            "mint-teal (#4FDDC3) light shining out of zigzag cracks, a few shell chips lifting; bottom left the hatchling's head and shoulders, "
            "front view, curious; bottom right the adult Kaiju's head and shoulders, front three-quarter view, jaws slightly open, its crystal "
            "crest glowing lime (#B8E833). Keep each design exactly like its reference. " + STY},
]
for j in jobs: j.update({"model": "gemini-3-pro-image", "aspect": "1:1" if "2-by-2" in j["prompt"] else "3:2", "size": "2K"})
json.dump(jobs, open(os.path.join(H, "icon_jobs.json"), "w"), indent=1); print(len(jobs))
