#!/usr/bin/env python3
"""Contact sheet of every prepared story layer, and the storyboard: one composed still per shot, rendered from the SAME
layers and the SAME shot table the game plays (Assets/_Game/Resources/Story/motion-comic.json), using the same
transform rules as Runtime/Menus/Story/MotionComic.cs (camera zoom/pan with per-layer depth parallax, eased keys,
y up, origin at the stage centre, 1920 x 1080 stage units).

Usage: python3 make_sheets.py OUTDIR [--at 0.5]   (writes contact-sheet.png, storyboard.png, shot-NN-<id>.png)
"""
import json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", ".."))
RES = os.path.join(REPO, "Assets", "_Game", "Resources")
STORY = os.path.join(RES, "Story")
FONTS = os.path.join(RES, "UI", "Fonts")
W, H = 1920, 1080

def font(name, size):
    for f in [os.path.join(FONTS, name)] + [os.path.join(dp, name) for dp, _, fs in os.walk(os.path.join(REPO, "Assets")) if name in fs]:
        if os.path.exists(f): return ImageFont.truetype(f, size)
    return ImageFont.load_default()

# ---- easing (mirrors MotionComic.Ease)
def ease(name, t):
    t = min(1.0, max(0.0, t))
    if name == "linear": return t
    if name == "inOutSine": return -(math.cos(math.pi * t) - 1) / 2
    if name == "outSine": return math.sin(t * math.pi / 2)
    if name == "inSine": return 1 - math.cos(t * math.pi / 2)
    if name == "outCubic": return 1 - (1 - t) ** 3
    if name == "inCubic": return t ** 3
    if name == "inOutCubic": return 4 * t ** 3 if t < 0.5 else 1 - (-2 * t + 2) ** 3 / 2
    if name == "outExpo": return 1.0 if t >= 1 else 1 - 2 ** (-10 * t)
    if name == "inExpo": return 0.0 if t <= 0 else 2 ** (10 * t - 10)
    if name == "outBack":
        c1 = 1.70158; c3 = c1 + 1; return 1 + c3 * (t - 1) ** 3 + c1 * (t - 1) ** 2
    if name == "inBack":
        c1 = 1.70158; c3 = c1 + 1; return c3 * t ** 3 - c1 * t ** 2
    if name == "step": return 1.0 if t >= 1 else 0.0
    return -(math.cos(math.pi * t) - 1) / 2

def sample(keys, t, fields):
    """keys sorted by t (seconds in the shot); each key eases from the previous one with its own ease."""
    if not keys: return None
    if t <= keys[0]["t"]: return {f: keys[0][f] for f in fields}
    for i in range(1, len(keys)):
        a, b = keys[i - 1], keys[i]
        if t <= b["t"]:
            u = ease(b.get("ease", "inOutSine"), (t - a["t"]) / max(1e-6, b["t"] - a["t"]))
            return {f: a[f] + (b[f] - a[f]) * u for f in fields}
    return {f: keys[-1][f] for f in fields}

_cache = {}
def tex(name):
    if name not in _cache:
        p = os.path.join(STORY, name + ".png")
        if not os.path.exists(p): p = os.path.join(RES, name + ".png")
        _cache[name] = Image.open(p).convert("RGBA") if os.path.exists(p) else None
    return _cache[name]

def hexc(s, a=1.0):
    s = s.lstrip("#"); return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4)) + (int(255 * a),)

def paste(canvas, img, cx, cy, w, h, rot, alpha, tint, flip):
    if img is None or w < 1 or h < 1 or alpha <= 0.003: return
    im = img.resize((max(1, int(abs(w))), max(1, int(abs(h)))), Image.LANCZOS)
    if flip: im = im.transpose(Image.FLIP_LEFT_RIGHT)
    if tint and tint.upper() != "#FFFFFF":
        t = np.array(hexc(tint)[:3], np.float32) / 255
        a = np.asarray(im).astype(np.float32); a[..., :3] *= t; im = Image.fromarray(a.clip(0, 255).astype(np.uint8), "RGBA")
    if alpha < 1:
        a = np.asarray(im).copy(); a[..., 3] = (a[..., 3] * alpha).astype(np.uint8); im = Image.fromarray(a, "RGBA")
    if abs(rot) > 0.01: im = im.rotate(rot, resample=Image.BICUBIC, expand=True)
    x = int(W / 2 + cx - im.width / 2); y = int(H / 2 - cy - im.height / 2)
    canvas.alpha_composite(im, (x, y)) if 0 <= x and 0 <= y and x + im.width <= W and y + im.height <= H else _clip_paste(canvas, im, x, y)

def _clip_paste(canvas, im, x, y):
    x0, y0 = max(0, x), max(0, y); x1, y1 = min(W, x + im.width), min(H, y + im.height)
    if x1 <= x0 or y1 <= y0: return
    canvas.alpha_composite(im.crop((x0 - x, y0 - y, x1 - x, y1 - y)), (x0, y0))

def h01(i, j, sysi):
    v = math.sin(i * 12.9898 + j * 78.233 + sysi * 37.719) * 43758.5453
    return v - math.floor(v)

def to_screen(cam, d, x, y):
    zd = 1 + (cam["dolly"] - 1) * d
    px, py = (x - cam["x"] * d) * zd, (y - cam["y"] * d) * zd
    return (px - cam["zx"]) * cam["zoom"] + cam["zx"], (py - cam["zy"]) * cam["zoom"] + cam["zy"], zd * cam["zoom"]

def compose(data, shot, t):
    canvas = Image.new("RGBA", (W, H), hexc("#18233B"))
    cam = sample(shot["cam"], t, ["zoom", "zx", "zy", "dolly", "x", "y", "rot"])
    for L in shot["layers"]:
        k = sample(L["keys"], t, ["x", "y", "s", "a", "r", "b"])
        if k is None or t < L.get("from", 0) or t > L.get("to", 1e9): continue
        d = L["depth"]
        px, py, z = to_screen(cam, d, k["x"], k["y"])
        name = L["tex"]
        if L.get("frames"):
            f = int(max(0, t - L.get("frameAt", 0)) * L.get("fps", 12)); fr = L["frames"]; name = fr[min(f, len(fr) - 1)]
        img = Image.new("RGBA", (8, 8), (255, 255, 255, 255)) if name == "@solid" else tex(name)
        if img is None: continue
        w = L["w"] * k["s"] * z
        h = (L["h"] if L.get("h", 0) > 0 else L["w"] * img.height / img.width) * k["s"] * z
        a = k["a"]
        if L.get("pulseHz", 0) > 0:
            a *= L["pulseLo"] + (1 - L["pulseLo"]) * (0.5 + 0.5 * math.sin(2 * math.pi * L["pulseHz"] * t))
        rot = k["r"] + cam["rot"] * d
        b = k["b"]
        if L.get("blur") and b > 0:
            paste(canvas, tex(L["blur"]), px, py, w, h, rot, a, L.get("tint"), L.get("flip"))
            a *= (1 - b)
        paste(canvas, img, px, py, w, h, rot, a, L.get("tint"), L.get("flip"))
    for si, P in enumerate(shot.get("particles", [])):
        if not (P["t0"] <= t <= max(P["t1"], P["t0"] + P["life"])): continue
        img = tex(P["tex"])
        for i in range(P["count"]):
            hs = [h01(i, j, si) for j in range(5)]
            if P["loop"]:
                if t > P["t1"]: continue
                age = ((t - P["t0"]) - hs[0] * P["life"]) % P["life"]
            else:
                age = t - (P["t0"] + (P["t1"] - P["t0"]) * hs[0])
                if age < 0 or age > P["life"]: continue
            x = P["x"] + P["sx"] * (hs[1] - 0.5) + (P["vx"] + P["jx"] * (hs[3] - 0.5)) * age
            y = P["y"] + P["sy"] * (hs[2] - 0.5) + (P["vy"] + P["jy"] * (hs[4] - 0.5)) * age + 0.5 * P["g"] * age * age
            sx, sy, z = to_screen(cam, P["depth"], x, y)
            sz = P["size"] * (0.6 + 0.8 * hs[3]) * z
            paste(canvas, img, sx, sy, sz, sz, 0, math.sin(math.pi * age / P["life"]), P["color"], False)
    for fl in shot.get("flash", []):
        if fl["t"] <= t <= fl["t"] + fl["dur"]:
            u = (t - fl["t"]) / fl["dur"]; a = fl["peak"] * (1 - ease("outCubic", u))
            canvas.alpha_composite(Image.new("RGBA", (W, H), hexc(fl.get("color", "#FFFFFF"), a)))
    # titles
    dr = ImageDraw.Draw(canvas)
    for ti in shot.get("titles", []):
        if ti["at"] <= t <= ti["out"]:
            f = font("Bungee-Regular.ttf" if ti.get("font", "display") == "display" else "NunitoSans-ExtraBold.ttf", int(ti["size"]))
            u = min(1, (t - ti["at"]) / 0.5); track = ti["track0"] + (ti["track1"] - ti["track0"]) * ease("outCubic", u)
            txt = ti["text"]; widths = [dr.textlength(c, font=f) for c in txt]; total = sum(widths) + track * (len(txt) - 1)
            x = W / 2 + ti["x"] - total / 2; y = H / 2 - ti["y"] - ti["size"] * 0.6
            for c, cw in zip(txt, widths):
                dr.text((x, y), c, font=f, fill=hexc(ti["color"]), stroke_width=max(3, int(ti["size"] / 16)), stroke_fill=hexc("#18233B"))
                x += cw + track
    bars = sample(shot.get("bars", [{"t": 0, "v": 1, "ease": "linear"}]), t, ["v"])["v"]
    bh = int(data["letterbox"] * H * bars)
    if bh > 0:
        dr.rectangle([0, 0, W, bh], fill=hexc("#18233B")); dr.rectangle([0, H - bh, W, H], fill=hexc("#18233B"))
    if shot.get("sub"):
        s = shot["sub"]
        if s["at"] <= t:
            f = font("NunitoSans-ExtraBold.ttf", 34)
            tw = dr.textlength(s["text"], font=f)
            if s["text"] and s["at"] <= t <= s["out"]: dr.text((W / 2 - tw / 2, H - max(bh, 120) / 2 - 22), s["text"], font=f, fill=hexc("#F7F0E5"), stroke_width=2, stroke_fill=hexc("#18233B"))
    return canvas.convert("RGB")

def contact_sheet(outdir):
    names = sorted(f[:-4] for f in os.listdir(STORY) if f.endswith(".png"))
    cols, tw, th = 8, 300, 230
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * tw, rows * th), (24, 35, 59))
    f = font("NunitoSans-ExtraBold.ttf", 15)
    chk = Image.new("RGB", (tw - 16, th - 46), (70, 78, 98))
    d0 = ImageDraw.Draw(chk)
    for yy in range(0, th, 16):
        for xx in range(0, tw, 16):
            if (xx // 16 + yy // 16) % 2: d0.rectangle([xx, yy, xx + 15, yy + 15], fill=(98, 106, 126))
    for i, n in enumerate(names):
        im = Image.open(os.path.join(STORY, n + ".png")).convert("RGBA")
        x, y = (i % cols) * tw, (i // cols) * th
        sheet.paste(chk, (x + 8, y + 8))
        t = im.copy(); t.thumbnail((tw - 16, th - 46), Image.LANCZOS)
        sheet.paste(t, (x + 8 + (tw - 16 - t.width) // 2, y + 8 + (th - 46 - t.height) // 2), t)
        ImageDraw.Draw(sheet).text((x + 10, y + th - 34), "%s  %dx%d" % (n, im.width, im.height), font=f, fill=(247, 240, 229))
    sheet.save(os.path.join(outdir, "contact-sheet.png"), optimize=True)
    return sheet

def storyboard(outdir, at):
    data = json.load(open(os.path.join(STORY, "motion-comic.json")))
    shots = data["shots"]
    stills = []
    f = font("Bungee-Regular.ttf", 26); f2 = font("NunitoSans-ExtraBold.ttf", 20)
    t0 = 0.0
    for i, s in enumerate(shots):
        t = s.get("boardAt", s["seconds"] * at)
        im = compose(data, s, t)
        im.save(os.path.join(outdir, "shot-%02d-%s.jpg" % (i + 1, s["id"])), quality=88)
        stills.append((s, im, t0)); t0 += s["seconds"]
    cols, sw, sh = 3, 640, 360
    rows = (len(stills) + cols - 1) // cols
    board = Image.new("RGB", (cols * (sw + 20) + 20, rows * (sh + 92) + 20), (24, 35, 59))
    d = ImageDraw.Draw(board)
    for i, (s, im, ts) in enumerate(stills):
        x, y = 20 + (i % cols) * (sw + 20), 20 + (i // cols) * (sh + 92)
        board.paste(im.resize((sw, sh), Image.LANCZOS), (x, y))
        d.text((x, y + sh + 8), "%02d  %s" % (i + 1, s["id"].upper()), font=f, fill=(199, 146, 69))
        d.text((x, y + sh + 44), "%.1f-%.1f s  %s" % (ts, ts + s["seconds"], s.get("beat", ""))[:70], font=f2, fill=(247, 240, 229))
    board.save(os.path.join(outdir, "storyboard.png"), optimize=True)
    return t0

if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    at = float(sys.argv[sys.argv.index("--at") + 1]) if "--at" in sys.argv else 0.6
    if "--board-only" not in sys.argv: contact_sheet(out)
    if os.path.exists(os.path.join(STORY, "motion-comic.json")): print("total seconds", storyboard(out, at))
