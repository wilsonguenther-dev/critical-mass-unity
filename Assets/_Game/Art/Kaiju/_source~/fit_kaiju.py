"""Kaiju v4 measuring and sheets, plain Python (no Blender): the bible-fit tool on every bible framing, plus the
expression sheet, the swipe pose strip and the face close-up for the report.

  python3 fit_kaiju.py <render_dir> <guides_dir> <bible_fit_tools_dir> <out_dir> [--label v4] [--sheets]

Guides come from bible_fit/bible_guide.py (prefix 11: front, three-quarter, profile, rear; prefix 10: azure-adult,
hatchling, egg). Renders are render_kaiju.py's orthographic bible framings (transparent film = the mask). Writes
<out_dir>/fit-<label>-<view>.jpg/.json (fit_report.py) and fit-<label>.json (IoU, past / short share, the head and
shoulder landmarks, the top to-do items per view). With --sheets also writes expression-sheet.jpg, pose-swipe.jpg,
pose-others.jpg and face-closeup.jpg from the expr-*, pose-* and face-* renders.
"""
import json
import os
import subprocess
import sys

from PIL import Image, ImageDraw

REN, GUIDES, TOOLS, OUT = sys.argv[1:5]
LABEL = sys.argv[sys.argv.index("--label") + 1] if "--label" in sys.argv else "v4"
VIEWS = ["11-front", "11-three-quarter", "11-profile", "11-rear", "10-azure-adult", "10-hatchling", "10-egg"]
CREAM = (254, 247, 237)
os.makedirs(OUT, exist_ok=True)


def save_jpeg(im, path, cap_kb=175, max_w=1400):
    if im.width > max_w:
        im = im.resize((max_w, round(im.height * max_w / im.width)), Image.LANCZOS)
    for q in (88, 82, 76, 70, 62, 54, 46, 38):
        im.save(path, "JPEG", quality=q, optimize=True)
        if os.path.getsize(path) <= cap_kb * 1024:
            break
    return path


summary = {}
for v in VIEWS:
    r = os.path.join(REN, v + ".png")
    g = os.path.join(GUIDES, "guide-%s.json" % v)
    if not (os.path.exists(r) and os.path.exists(g)):
        continue
    out = os.path.join(OUT, "fit-%s-%s" % (LABEL, v))
    p = subprocess.run([sys.executable, os.path.join(TOOLS, "fit_report.py"), g, r, out, "--name", "%s %s" % (LABEL, v)],
                       capture_output=True, text=True)
    if p.returncode != 0:
        print("FIT FAILED", v, p.stderr[-400:])
        continue
    j = json.load(open(out + ".json"))
    summary[v] = {"iou": round(j["iou"], 3), "past": round(j["difference"]["past_share"], 3),
                  "short": round(j["difference"]["short_share"], 3),
                  "todo": [(t["kind"], round(t["score"], 3), t["text"]) for t in j["todo"][:6]]}
    print("FIT %-18s IoU %.3f  past %.1f%%  short %.1f%%" % (v, j["iou"], 100 * j["difference"]["past_share"],
                                                            100 * j["difference"]["short_share"]))
    for t in j["todo"][:5]:
        print("     [%s %.3f] %s" % (t["kind"], t["score"], t["text"][:170]))
json.dump(summary, open(os.path.join(OUT, "fit-%s.json" % LABEL), "w"), indent=1)

if "--sheets" in sys.argv:
    def tile(path, size, label=None, bg=CREAM):
        im = Image.open(path).convert("RGBA").resize(size, Image.LANCZOS)
        t = Image.new("RGB", size, bg)
        t.paste(im, (0, 0), im)
        if label:
            d = ImageDraw.Draw(t)
            d.rectangle((0, 0, size[0], 15), fill=(255, 255, 255))
            d.text((4, 2), label, fill=(0, 0, 0))
        return t

    names = [("neutral", "neutral-goofy (DEFAULT)"), ("happy", "happy-fed (allied)"), ("angry", "angry-roar (attack)"),
             ("berserk", "berserk (corrupted)"), ("hurt", "hurt"), ("sleepy", "sleepy"), ("blink", "blink")]
    hat = [("neutral", "hatchling default"), ("sleepy", "hatchling sleepy (egg-fresh)"), ("happy", "hatchling happy")]
    S = 340
    sheet = Image.new("RGB", (4 * S, 3 * S), CREAM)
    k = 0
    for st, lab in names:
        p = os.path.join(REN, "expr-adult-%s.png" % st)
        if os.path.exists(p):
            sheet.paste(tile(p, (S, S), lab), ((k % 4) * S, (k // 4) * S))
        k += 1
    k = 8
    for st, lab in hat:
        p = os.path.join(REN, "expr-hatchling-%s.png" % st)
        if os.path.exists(p):
            sheet.paste(tile(p, (S, S), lab), ((k % 4) * S, (k // 4) * S))
        k += 1
    save_jpeg(sheet, os.path.join(OUT, "expression-sheet.jpg"))

    def strip(items, name, S=280):
        ims = [(os.path.join(REN, f), lab) for f, lab in items if os.path.exists(os.path.join(REN, f))]
        if not ims:
            return
        st = Image.new("RGB", (len(ims) * S, S), CREAM)
        for i, (p, lab) in enumerate(ims):
            st.paste(tile(p, (S, S), lab), (i * S, 0))
        save_jpeg(st, os.path.join(OUT, name))
    strip([("pose-swipe-000.png", "Swipe f0 rest"), ("pose-swipe-027.png", "f8 anticipation"),
           ("pose-swipe-040.png", "f12 HIT"), ("pose-swipe-050.png", "f15 follow-through"),
           ("pose-swipe-075.png", "f22 recover")], "pose-swipe.jpg")
    strip([("pose-idle-030.png", "Idle"), ("pose-walk-025.png", "Walk f8"), ("pose-roar-014.png", "Roar f5 inhale"),
           ("pose-roar-045.png", "Roar f16"), ("pose-slam-057.png", "Slam f8 HIT"), ("pose-slam-080.png", "Slam f11 squash")],
          "pose-others.jpg", S=233)
    p = os.path.join(REN, "face-adult-15.png")
    if os.path.exists(p):
        save_jpeg(tile(p, (900, 900)), os.path.join(OUT, "face-closeup.jpg"), max_w=900)
print("FIT DONE", LABEL)
