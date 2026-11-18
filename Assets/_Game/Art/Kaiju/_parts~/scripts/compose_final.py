# python3 -I compose_final.py : out/00-kaiju-five-types.png (<= 2000 px wide) + renders/lod_sheet.png + renders/turn_sheet.png
import os
from PIL import Image, ImageDraw
R = os.path.expanduser("~/cm-art-trial/parts-kaiju/renders/final"); O = os.path.expanduser("~/cm-art-trial/parts-kaiju/out")
T = ["Elder", "Grove", "Brute", "Timber", "Mire"]; S = 220; LBL = 22
def tile(p, s=S): return Image.open(f"{R}/{p}").convert("RGB").resize((s, s), Image.LANCZOS)
def sil(p, s=S):
    im = Image.open(f"{R}/{p}").convert("RGBA"); a = im.split()[3]
    w = Image.new("RGB", im.size, "white"); w.paste((0, 0, 0), mask=a.point(lambda v: 255 if v > 127 else 0)); return w.resize((s, s), Image.LANCZOS)
cols = [("night 45", lambda t: tile(f"game_{t}_night_45.png")), ("night 300", lambda t: tile(f"game_{t}_night_300.png")),
        ("day 45", lambda t: tile(f"game_{t}_day_45.png")), ("owned azure", lambda t: tile(f"game_{t}_azure.png")),
        ("owned crimson", lambda t: tile(f"game_{t}_crimson.png")), ("sil 45", lambda t: sil(f"sil_{t}_45.png")),
        ("sil 135", lambda t: sil(f"sil_{t}_135.png")), ("sil 225", lambda t: sil(f"sil_{t}_225.png")), ("sil 300", lambda t: sil(f"sil_{t}_300.png"))]
LW = 90; W = LW + S * len(cols); TOP = 300 + LBL * 2
out = Image.new("RGB", (W, TOP + (S + 4) * len(T)), (236, 236, 240)); d = ImageDraw.Draw(out)
# top band: OLD vs NEW (Elder) at the gameplay camera, and size vs a player
d.text((8, 6), "OLD (file replaced)            NEW Elder (same camera, night)        size vs player (azure Warrior, 2.0 m) - Elder / Brute", fill=(0, 0, 0))
x = 8
for p in ("old_night_45.png", "old_night_300.png", "game_Elder_night_45.png", "game_Elder_night_300.png"):
    out.paste(tile(p, 300), (x, LBL)); x += 304
for p in ("size_Elder.png", "size_Brute.png"):
    out.paste(tile(p, 300), (x, LBL)); x += 304
y0 = TOP - LBL
for i, (n, _) in enumerate(cols): d.text((LW + i * S + 6, y0 + 4), n, fill=(0, 0, 0))
for r, t in enumerate(T):
    y = TOP + r * (S + 4); d.text((8, y + S // 2 - 6), t.upper(), fill=(0, 0, 0))
    for i, (_, f) in enumerate(cols): out.paste(f(t), (LW + i * S, y))
if out.width > 2000: out = out.resize((2000, round(out.height * 2000 / out.width)), Image.LANCZOS)
out.save(f"{O}/00-kaiju-five-types.png"); print("sheet", out.size)
# LOD sheet
L = ["Hero", "LOD0", "LOD1", "Shadow"]; lo = Image.new("RGB", (S * 4 + LW, (S + 4) * 5 + LBL), "white"); d = ImageDraw.Draw(lo)
for i, l in enumerate(L): d.text((LW + i * S + 6, 4), l, fill=(0, 0, 0))
for r, t in enumerate(T):
    d.text((8, LBL + r * (S + 4) + S // 2), t, fill=(0, 0, 0))
    for i, l in enumerate(L): lo.paste(tile(f"lod_{t}_{l}.png"), (LW + i * S, LBL + r * (S + 4)))
lo.save(R + "/../lod_sheet.png"); print("lod", lo.size)
tu = Image.new("RGB", (S * 6, S * 5), "white")
for r, t in enumerate(T):
    for k in range(6): tu.paste(tile(f"turn_{t}_{k}.png"), (k * S, r * S))
tu.save(R + "/../turn_sheet.png"); print("turn", tu.size)
