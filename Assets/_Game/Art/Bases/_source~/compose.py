"""Turn the raw renders into the report set: JPEGs under 180 KB, bible-left / ours-right side-by-sides, previews.

  python3 compose.py <render_dir> <report_dir> [preview_dir]
"""
import os
import sys

from PIL import Image, ImageDraw

BIBLE = os.path.expanduser("~/critical-mass/loop/parity/ref")
SKY = (180, 218, 238)
CREAM = (251, 247, 238)
PAIRS = [
    ("shrine", "14-shrine-azure", "bf_shrine"),
    ("hill-neutral", "14-hill-neutral", "bf_hill_neutral"),
    ("hill-azure", "14-hill-azure", "bf_hill_azure"),
    ("hill-crimson", "14-hill-crimson", "bf_hill_crimson"),
    ("wall-intact", "14-wall-intact", "bf_wall_intact"),
    ("wall-cracked", "14-wall-cracked", "bf_wall_cracked"),
    ("wall-destroyed", "14-wall-destroyed", "bf_wall_destroyed"),
    ("banner", "14-banner-azure", "bf_banner"),
    ("respawn-pad", "14-respawn-pad", "bf_pad"),
    ("p12", "12-map-a-skybreak", "bf_p12"),
]


def flat(path, bg):
    im = Image.open(path).convert("RGBA")
    base = Image.new("RGBA", im.size, bg + (255,))
    base.alpha_composite(im)
    return base.convert("RGB")


def save_small(im, path, max_kb=178, max_w=None):
    if max_w and im.width > max_w:
        im = im.resize((max_w, round(im.height * max_w / im.width)), Image.LANCZOS)
    for q in (88, 82, 76, 70, 64, 58, 52, 46, 40):
        im.save(path, "JPEG", quality=q, optimize=True)
        if os.path.getsize(path) <= max_kb * 1024:
            return q
    im = im.resize((im.width * 4 // 5, im.height * 4 // 5), Image.LANCZOS)
    im.save(path, "JPEG", quality=60, optimize=True)
    return -1


def sbs(left, right, path):
    h = 640
    l = left.resize((round(left.width * h / left.height), h), Image.LANCZOS)
    r = right.resize((round(right.width * h / right.height), h), Image.LANCZOS)
    w = l.width + r.width + 12
    out = Image.new("RGB", (w, h + 30), (40, 44, 58))
    out.paste(l, (0, 30))
    out.paste(r, (l.width + 12, 30))
    d = ImageDraw.Draw(out)
    d.text((8, 8), "BIBLE", fill=(255, 255, 255))
    d.text((l.width + 20, 8), "OURS (Blender, v2)", fill=(255, 255, 255))
    save_small(out, path, 178, 1400)


def main():
    src, dst = sys.argv[1], sys.argv[2]
    prev = sys.argv[3] if len(sys.argv) > 3 else None
    os.makedirs(dst, exist_ok=True)
    if prev:
        os.makedirs(prev, exist_ok=True)
    for f in sorted(os.listdir(src)):
        if not f.endswith(".png"):
            continue
        name = f[:-4]
        im = flat(os.path.join(src, f), SKY if name.startswith(("gp_", "crop", "bf_p12")) else CREAM)
        q = save_small(im, os.path.join(dst, name + ".jpg"))
        if prev:
            save_small(im, os.path.join(prev, name + ".jpg"), 145, 960)
        print(name, q)
    for what, bib, ours in PAIRS:
        o = os.path.join(src, ours + ".png")
        b = os.path.join(BIBLE, bib + ".png")
        if os.path.exists(o) and os.path.exists(b):
            sbs(Image.open(b).convert("RGB"), flat(o, SKY if ours == "bf_p12" else CREAM),
                os.path.join(dst, f"sbs-{what}.jpg"))
            if prev:
                im = Image.open(os.path.join(dst, f"sbs-{what}.jpg"))
                save_small(im, os.path.join(prev, f"sbs-{what}.jpg"), 145, 960)


main()
