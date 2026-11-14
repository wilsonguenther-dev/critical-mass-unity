# hud-art-1 round 2: applies match_ingame.py to the shipped cut-ins and icons (see that file for why).
# usage: python3 apply_round2.py ICON_TMP [icons]   (ICON_TMP = the icons.py work folder holding busts-N.png and kaiju-N.png)
import os, sys
import numpy as np
from PIL import Image
here = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, here)
from match_ingame import apply
import recolor as rc
import icons as ic
BM = os.path.normpath(os.path.join(here, "../..")); ICONS = os.path.normpath(os.path.join(BM, "../Icons")); tmp = sys.argv[1]
def fix(src, dst, mode, f=0.32):
    out, share = apply(np.asarray(Image.open(src).convert("RGBA")), mode, f); Image.fromarray(out, "RGBA").save(dst, optimize=True)
    print(os.path.basename(dst), mode, round(share, 4))
ONLY_ICONS = len(sys.argv) > 2 and sys.argv[2] == "icons"
for c, mode, f in (() if ONLY_ICONS else (("farmer", "farmer-hair", 0.30), ("healer", "healer-skin", 0.32))):
    az = os.path.join(BM, f"bm-portrait-{c}-azure.png"); fix(az, az, mode, f)
    rc.recolor(az, os.path.join(BM, f"bm-portrait-{c}-crimson.png"), "crimson")
for st in (() if ONLY_ICONS else ("neutral", "azure", "crimson", "berserk")):
    p = os.path.join(BM, f"bm-kaiju-{st}.png"); fix(p, p, "kaiju-navy")
# HUD icons: the healer portrait (bust from the icon sheet) and the four adult Kaiju badges
# sources are the cut-outs icons.py saved (busts-N.png / kaiju-N.png are the raw slices)
b = os.path.join(tmp, "bust-healer-a.png"); hb = os.path.join(tmp, "r2-bust-healer.png"); fix(b, hb, "healer-skin")
hb = Image.open(hb).convert("RGBA")
ic.write(ic.tile_portrait(hb, "azure"), "portrait-healer-azure", ICONS, 106)
ic.write(ic.tile_portrait(ic.team_variant(hb, "crimson", tmp, "r2-bust-healer"), "crimson"), "portrait-healer-crimson", ICONS, 106)
k = os.path.join(tmp, "adult-azure-a.png"); kn = os.path.join(tmp, "r2-adult.png"); fix(k, kn, "kaiju-navy")
adult = Image.open(kn).convert("RGBA")
ic.write(ic.plain(adult, 288), "kaiju-adult-neutral", ICONS, 72)
for st in ("azure", "crimson", "berserk"):
    ic.write(ic.plain(ic.team_variant(adult, "kaiju-" + st, tmp, "r2-adult-" + st), 288), f"kaiju-adult-{st}", ICONS, 72)
print("done")
