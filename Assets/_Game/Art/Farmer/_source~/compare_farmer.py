"""Side-by-sides (bible LEFT, ours RIGHT), silhouette sheet and the measured checks, from a render_farmer.py output.

  python3 compare_farmer.py <render_dir> <out_dir> [warrior_sil_dir] [bible_ref_dir]

Needs Pillow (system python3). bible_ref_dir defaults to ~/critical-mass/loop/parity/ref (read-only).
Writes <out_dir>/sbs-*.jpg (each < 180 KB, <= 1400 px wide), silhouettes-64px.jpg, game-*-2x.jpg, game_*.jpg and
checks.json. IoU: both masks cropped to their bounding box, scaled to the same height and centred (PARITY.md
alignment); the bible mask is a "differs from paper" threshold, not a reviewed U-016 mask, so IoU is indicative.
"""
import io
import json
import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFont

RD, OUT = sys.argv[1], sys.argv[2]
WSIL = sys.argv[3] if len(sys.argv) > 3 else None
REF = sys.argv[4] if len(sys.argv) > 4 else os.path.expanduser("~/critical-mass/loop/parity/ref")
os.makedirs(OUT, exist_ok=True)
PAPER = (254, 248, 238)
NAVY = (0x18, 0x23, 0x3B)
SS, OUTER_PX, INNER_PX = 2, 3.0, 1.5      # must match render_farmer.py


def dilate(a, r):
    out = a.copy()
    for dx in range(-r, r + 1):
        for dy in range(-r, r + 1):
            if (dx or dy) and dx * dx + dy * dy <= r * r + 0.5:
                out = ImageChops.lighter(out, ImageChops.offset(a, dx, dy))
    return out


def finish(src):
    """Raw 2x renders -> final 1x: add (OUTER - INNER) px of Void Navy outside the character silhouette, then halve.
    game_* frames take the silhouette from the matching character-only pass gchar_<team>.png."""
    dst = os.path.join(src, "final")
    os.makedirs(dst, exist_ok=True)
    r = round((OUTER_PX - INNER_PX) * SS)
    for fn in sorted(os.listdir(src)):
        if not fn.endswith(".png") or fn.startswith("gchar_"):
            continue
        im = Image.open(os.path.join(src, fn)).convert("RGBA")
        if fn.startswith("game_"):
            ch = Image.open(os.path.join(src, "gchar_" + fn.rsplit("_", 1)[1])).convert("RGBA")
            ring = Image.new("RGBA", im.size, NAVY + (0,))
            ring.putalpha(dilate(ch.split()[3], r))
            im.alpha_composite(ring)
            im.alpha_composite(ch)
        else:
            ring = Image.new("RGBA", im.size, NAVY + (0,))
            ring.putalpha(dilate(im.split()[3], r))
            ring.alpha_composite(im)
            im = ring
        size = (im.width // SS, im.height // SS)
        im = im.resize(size, Image.NEAREST if fn.startswith("mask_") else Image.LANCZOS)
        im.save(os.path.join(dst, fn))
    for fn in ("measure.json",):
        if os.path.exists(os.path.join(src, fn)):
            with open(os.path.join(src, fn)) as a, open(os.path.join(dst, fn), "w") as b:
                b.write(a.read())
    return dst


RD = finish(RD)
if WSIL:
    WSIL = finish(WSIL)
CREAM = (0xF7, 0xF0, 0xE5)
FACINGS = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
# bible 09 directions sheet order, left to right (back view first, front view fifth; read off the sheet by eye)
BIBLE_DIR_ORDER = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]


def font(sz):
    for p in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        if os.path.exists(p):
            return ImageFont.truetype(p, sz)
    return ImageFont.load_default()


def ref(name, box=None):
    im = Image.open(os.path.join(REF, name)).convert("RGB")
    return im.crop(box) if box else im


def ours(name):
    return Image.open(os.path.join(RD, name)).convert("RGBA")


def on_paper(im):
    bg = Image.new("RGBA", im.size, PAPER + (255,))
    bg.alpha_composite(im)
    return bg.convert("RGB")


def alpha_mask(im):
    return im.split()[3].point(lambda a: 255 if a > 127 else 0)


def bible_mask(im):
    diff = ImageChops.difference(im.convert("RGB"), Image.new("RGB", im.size, im.getpixel((1, 1)))).convert("L")
    return diff.point(lambda v: 255 if v > 22 else 0)


def hrow(ims, gap=12, bg=(0, 0, 0, 0)):
    h = max(i.height for i in ims)
    w = sum(i.width for i in ims) + gap * (len(ims) - 1)
    out = Image.new("RGBA", (w, h), bg)
    x = 0
    for i in ims:
        out.paste(i, (x, h - i.height), i if i.mode == "RGBA" else None)
        x += i.width + gap
    return out


def fit_h(im, h):
    return im.resize((max(1, round(im.width * h / im.height)), h), Image.LANCZOS)


def norm_mask(m, h=256):
    bb = m.getbbox()
    m = m.crop(bb)
    return m.resize((max(1, round(m.width * h / m.height)), h), Image.NEAREST)


def iou(a, b):
    a, b = norm_mask(a), norm_mask(b)
    w = max(a.width, b.width)
    A = Image.new("L", (w, 256))
    Bm = Image.new("L", (w, 256))
    A.paste(a, ((w - a.width) // 2, 0))
    Bm.paste(b, ((w - b.width) // 2, 0))
    inter = ImageChops.multiply(A, Bm).histogram()[255]
    union = ImageChops.lighter(A, Bm).histogram()[255]
    return inter / max(union, 1)


def save_jpg(im, path, max_kb=178, max_w=1400):
    if im.width > max_w:
        im = im.resize((max_w, round(im.height * max_w / im.width)), Image.LANCZOS)
    for q in (88, 82, 76, 70, 64, 58, 52, 46, 40):
        buf = io.BytesIO()
        im.save(buf, "JPEG", quality=q, optimize=True)
        if buf.tell() < max_kb * 1024:
            break
    with open(path, "wb") as f:
        f.write(buf.getvalue())
    return round(buf.tell() / 1024, 1)


def sbs(name, left, right, H=560):
    L = fit_h(left.convert("RGB"), H)
    Rr = fit_h(on_paper(right) if right.mode == "RGBA" else right, H)
    W = L.width + Rr.width + 60
    sheet = Image.new("RGB", (W, H + 44), PAPER)
    sheet.paste(L, (20, 34))
    sheet.paste(Rr, (L.width + 40, 34))
    d = ImageDraw.Draw(sheet)
    d.text((20, 6), f"BIBLE p07 / p09  ({name})", fill=NAVY, font=font(20))
    d.text((L.width + 40, 6), "OURS (Blender build, CM/Toon preview)", fill=NAVY, font=font(20))
    return save_jpg(sheet, os.path.join(OUT, f"sbs-{name}.jpg"))


checks = {"iou": {}, "sbs_kb": {}}
# ------------------------------------------------------------------------------------------------ framings
PAIRS = [
    ("front", "07-front.png", "front_azure.png"),
    ("three-quarter", "07-three-quarter.png", "three-quarter_azure.png"),
    ("profile", "07-profile.png", "profile_azure.png"),
    ("back", "07-back.png", "back_azure.png"),
    ("backpack-tank", "07-backpack-tank.png", "backpack-tank_azure.png"),
    ("front-staff", "07-front-staff.png", "staff-front_azure.png"),
    ("pose-harvest", "07-pose-harvest.png", "pose-harvest_azure.png"),
    ("pose-root-seed", "07-pose-root-seed.png", "pose-root-seed_azure.png"),
]
for name, bfile, ofile in PAIRS:
    if not os.path.exists(os.path.join(RD, ofile)):
        continue
    b, o = ref(bfile), ours(ofile)
    checks["iou"][name] = round(iou(bible_mask(b), alpha_mask(o)), 3)
    checks["sbs_kb"][name] = sbs(name, b, o)
if os.path.exists(os.path.join(RD, "staff-front_azure.png")):
    row = hrow([fit_h(ours(f"staff-{v}_azure.png"), 600) for v in ("front", "side", "back")], 30)
    checks["iou"]["staff-views"] = round(iou(bible_mask(ref("07-staff-views.png")), alpha_mask(row)), 3)
    checks["sbs_kb"]["staff-views"] = sbs("staff-views", ref("07-staff-views.png"), row)
if os.path.exists(os.path.join(RD, "front_crimson.png")):
    pair = hrow([fit_h(ours("front_azure.png"), 500), fit_h(ours("front_crimson.png"), 500)], 30)
    checks["sbs_kb"]["team-pair"] = sbs("team-pair", ref("09-farmer-actions.png"), pair)

# ------------------------------------------------------------------------------------------------ facings
if os.path.exists(os.path.join(RD, "facing_S_azure.png")):
    tiles = [fit_h(ours(f"facing_{f}_azure.png"), 300) for f in BIBLE_DIR_ORDER]
    row = hrow(tiles, 16)
    checks["sbs_kb"]["directions"] = sbs("directions", ref("09-farmer-directions.png"), row, H=300)
    team, lime_same, lime_dc = {}, True, 0
    for f in FACINGS:
        res = {}
        for t in ("azure", "crimson"):
            m = ours(f"mask_{f}_{t}.png")
            px = m.load()
            n = tm = gl = ol = 0
            for y in range(m.height):
                for x in range(m.width):
                    r, g, b_, a = px[x, y]
                    if a < 128:
                        continue
                    n += 1
                    if r > 170 and g < 90 and b_ > 170:
                        tm += 1
                    elif g > 170 and r < 90:
                        gl += 1
                    elif r < 90 and g < 90:
                        ol += 1
            res[t] = {"team_pct_all": round(100 * tm / n, 1), "team_pct_fill": round(100 * tm / max(n - ol, 1), 1),
                      "lime_px": gl, "outline_px": ol, "px": n}
        # lime invariance: same pixels, same colour on both teams
        ma, mc = ours(f"mask_{f}_azure.png").load(), ours(f"mask_{f}_crimson.png").load()
        ca, cc = ours(f"facing_{f}_azure.png").load(), ours(f"facing_{f}_crimson.png").load()
        im = ours(f"mask_{f}_azure.png")
        for y in range(im.height):
            for x in range(im.width):
                r, g, b_, a = ma[x, y]
                if a > 200 and g > 200 and r < 50 and 0 < x < im.width - 1 and 0 < y < im.height - 1 and all(
                        ma[x + dx, y + dy][1] > 200 and ma[x + dx, y + dy][0] < 50
                        for dx in (-1, 0, 1) for dy in (-1, 0, 1)):        # interior lime pixels (no AA blend)
                    if not (mc[x, y][1] > 200 and mc[x, y][0] < 50):
                        lime_same = False
                    lime_dc = max(lime_dc, max(abs(ca[x, y][i] - cc[x, y][i]) for i in range(3)))
        team[f] = res
    checks["team_colour"] = team
    checks["team_pct_min_all"] = min(team[f][t]["team_pct_all"] for f in FACINGS for t in ("azure", "crimson"))
    checks["lime_same_pixels_both_teams"] = lime_same
    checks["lime_max_channel_diff_between_teams"] = lime_dc

    # outline width: first navy run from the left edge, rows through the middle 70 % of each facing
    runs = []
    for f in FACINGS:
        im = ours(f"facing_{f}_azure.png")
        px = im.load()
        y0, y1 = int(im.height * 0.15), int(im.height * 0.85)
        for y in range(y0, y1, 2):
            x = 0
            while x < im.width and px[x, y][3] < 128:
                x += 1
            k = 0
            while x + k < im.width and px[x + k, y][3] >= 128 and \
                    sum(abs(px[x + k, y][i] - NAVY[i]) for i in range(3)) < 70:
                k += 1
            if 0 < k < 12:
                runs.append(k)
    runs.sort()
    checks["outline_px_median"] = runs[len(runs) // 2] if runs else None
    checks["outline_px_p25_p75"] = [runs[len(runs) // 4], runs[3 * len(runs) // 4]] if runs else None

    # ------------------------------------------------------------------------------------ 64 px silhouette sheet
    def sil_row(paths_or_ims, label):
        tiles = []
        for p in paths_or_ims:
            m = alpha_mask(Image.open(p).convert("RGBA")) if isinstance(p, str) else p
            m = m.crop(m.getbbox())
            m = m.resize((max(1, round(m.width * 64 / m.height)), 64), Image.LANCZOS)
            t = Image.new("RGB", (max(m.width, 44) + 12, 72), CREAM)
            t.paste(Image.new("RGB", m.size, NAVY), ((t.width - m.width) // 2, 4), m)
            tiles.append(t)
        row = Image.new("RGB", (150 + sum(t.width for t in tiles), 72), CREAM)
        ImageDraw.Draw(row).text((6, 26), label, fill=NAVY, font=font(15))
        x = 150
        for t in tiles:
            row.paste(t, (x, 0))
            x += t.width
        return row

    rows = [sil_row([os.path.join(RD, f"facing_{f}_azure.png") for f in BIBLE_DIR_ORDER], "Farmer (ours)")]
    if WSIL and os.path.exists(os.path.join(WSIL, "sil_S.png")):
        rows.append(sil_row([os.path.join(WSIL, f"sil_{f}.png") for f in BIBLE_DIR_ORDER], "Warrior (v1 branch)"))

    def bible_cells(fname):
        """Figures of a p09 directions sheet: threshold, close small holes, split at empty columns."""
        from PIL import ImageFilter
        b = ref(fname)
        m = ImageChops.difference(b, Image.new("RGB", b.size, b.getpixel((1, 1)))).convert("L")
        m = m.point(lambda v: 255 if v > 40 else 0).filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(5))
        m = m.crop((0, 0, m.width, int(m.height * 0.88)))
        px = m.load()
        cols = [sum(1 for y in range(m.height) if px[x, y]) for x in range(m.width)]
        runs, x = [], 0
        while x < m.width:
            if cols[x] > 2:
                s0 = x
                while x < m.width and cols[x] > 2:
                    x += 1
                runs.append([s0, x])
            x += 1
        merged = []
        for r in runs:
            if merged and r[0] - merged[-1][1] < 6:
                merged[-1][1] = r[1]
            else:
                merged.append(r)
        merged = sorted(sorted(merged, key=lambda r: r[0] - r[1])[:8])
        return [m.crop((a, 0, b_, m.height)) for a, b_ in merged]

    rows.append(sil_row(bible_cells("09-miner-directions.png"), "Miner (bible p09*)"))
    rows.append(sil_row(bible_cells("09-farmer-directions.png"), "Farmer (bible p09*)"))
    W = max(r.width for r in rows)
    sheet = Image.new("RGB", (W, 72 * len(rows) + 30), CREAM)
    for i, r in enumerate(rows):
        sheet.paste(r, (0, i * 72))
    ImageDraw.Draw(sheet).text((6, 72 * len(rows) + 6), "64 px Void Navy fills, facings N NE E SE S SW W NW. "
                               "*bible rows: threshold masks of the p09 sheets (no Miner build exists yet)",
                               fill=NAVY, font=font(13))
    checks["silhouette_sheet_kb"] = save_jpg(sheet, os.path.join(OUT, "silhouettes-64px.jpg"))
    # narrower and taller than the Warrior: height / width of the S-facing silhouettes at the same camera
    if WSIL and os.path.exists(os.path.join(WSIL, "sil_S.png")):
        def hw(p):
            bb = alpha_mask(Image.open(p).convert("RGBA")).getbbox()
            return (bb[3] - bb[1]), (bb[2] - bb[0])
        ratios = {}
        for f in FACINGS:
            fh, fw = hw(os.path.join(RD, f"facing_{f}_azure.png"))
            wh, ww = hw(os.path.join(WSIL, f"sil_{f}.png"))
            ratios[f] = {"farmer_h_w": [fh, fw, round(fh / fw, 2)], "warrior_h_w": [wh, ww, round(wh / ww, 2)]}
        checks["vs_warrior_bbox_px"] = ratios

# ------------------------------------------------------------------------------------------------ gameplay frames
for t in ("azure", "crimson"):
    p = os.path.join(RD, f"game_grass_{t}.png")
    if os.path.exists(p):
        im = Image.open(p).convert("RGB")
        checks["sbs_kb"][f"game_grass_{t}"] = save_jpg(im, os.path.join(OUT, f"game_grass_{t}.jpg"), max_w=1920)
        crop = im.crop((960 - 240, 540 - 200, 960 + 240, 540 + 70)).resize((960, 540), Image.NEAREST)
        checks["sbs_kb"][f"game_{t}_2x"] = save_jpg(crop, os.path.join(OUT, f"game-{t}-2x.jpg"))
p = os.path.join(RD, "game_grey_azure.png")
if os.path.exists(p):
    checks["sbs_kb"]["game_grey_azure"] = save_jpg(Image.open(p).convert("RGB"),
                                                   os.path.join(OUT, "game_grey_azure.jpg"), max_w=1920)


# ------------------------------------------------------------------------------------------------ swatches (dE2000)
def lab(rgb):
    def lin(c):
        c /= 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    r, g, b = (lin(c) for c in rgb)
    X = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047
    Y = 0.2126 * r + 0.7152 * g + 0.0722 * b
    Z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883
    f = lambda t: t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116  # noqa: E731
    return 116 * f(Y) - 16, 500 * (f(X) - f(Y)), 200 * (f(Y) - f(Z))


def de2000(c1, c2):
    L1, a1, b1 = lab(c1)
    L2, a2, b2 = lab(c2)
    C1, C2 = math.hypot(a1, b1), math.hypot(a2, b2)
    Cm = (C1 + C2) / 2
    G = 0.5 * (1 - math.sqrt(Cm ** 7 / (Cm ** 7 + 25 ** 7)))
    a1p, a2p = a1 * (1 + G), a2 * (1 + G)
    C1p, C2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1p = math.degrees(math.atan2(b1, a1p)) % 360
    h2p = math.degrees(math.atan2(b2, a2p)) % 360
    dLp, dCp = L2 - L1, C2p - C1p
    dh = h2p - h1p
    if C1p * C2p == 0:
        dh = 0
    elif dh > 180:
        dh -= 360
    elif dh < -180:
        dh += 360
    dHp = 2 * math.sqrt(C1p * C2p) * math.sin(math.radians(dh / 2))
    Lm, Cmp = (L1 + L2) / 2, (C1p + C2p) / 2
    hm = h1p + h2p
    if C1p * C2p != 0:
        hm = (h1p + h2p + 360) / 2 if abs(h1p - h2p) > 180 else (h1p + h2p) / 2
    T = (1 - 0.17 * math.cos(math.radians(hm - 30)) + 0.24 * math.cos(math.radians(2 * hm)) +
         0.32 * math.cos(math.radians(3 * hm + 6)) - 0.20 * math.cos(math.radians(4 * hm - 63)))
    dth = 30 * math.exp(-((hm - 275) / 25) ** 2)
    Rc = 2 * math.sqrt(Cmp ** 7 / (Cmp ** 7 + 25 ** 7))
    Sl = 1 + 0.015 * (Lm - 50) ** 2 / math.sqrt(20 + (Lm - 50) ** 2)
    Sc, Sh = 1 + 0.045 * Cmp, 1 + 0.015 * Cmp * T
    Rt = -math.sin(math.radians(2 * dth)) * Rc
    return math.sqrt((dLp / Sl) ** 2 + (dCp / Sc) ** 2 + (dHp / Sh) ** 2 + Rt * (dCp / Sc) * (dHp / Sh))


def hx(h):
    return tuple(int(h.lstrip("#")[i:i + 2], 16) for i in (0, 2, 4))


SW = {"navy": "#2F3349", "teal": "#4999A5", "cream": "#F3E3C8", "dark brown": "#70503D", "brass": "#BB814A",
      "lime": "#D0DE6A", "coral": "#E9705F"}
OURS = {"navy": "#2F3349", "teal": "#4999A5", "cream": "#F3E1C7", "dark brown": "#70513D", "brass": "#BB8148",
        "lime": "#D0E26A", "coral": "#E9705F"}
checks["swatch_de2000_palette"] = {k: round(de2000(hx(SW[k]), hx(OURS[k])), 2) for k in SW}
checks["swatch_de2000_biomass_core_vs_p07_lime"] = round(de2000(hx("#B7F238"), hx(SW["lime"])), 2)
# rendered: share of the front render's fill pixels within dE 10 of each swatch (lit band)
if os.path.exists(os.path.join(RD, "front_azure.png")):
    im = ours("front_azure.png").resize((200, round(200 * 720 / 400)))
    pix = [p[:3] for p in im.get_flattened_data() if p[3] > 200 and sum(abs(p[i] - NAVY[i]) for i in range(3)) > 60]
    rend = {}
    for k, h in SW.items():
        if k == "coral":
            continue
        c = hx(h)
        near = sum(1 for p in pix[::3] if de2000(p, c) < 10)
        rend[k] = round(100 * near / max(len(pix[::3]), 1), 1)
    checks["swatch_render_pct_within_de10_front_azure"] = rend
    if os.path.exists(os.path.join(RD, "front_crimson.png")):
        imc = ours("front_crimson.png").resize(im.size)
        pc = [p[:3] for p in imc.get_flattened_data() if p[3] > 200]
        checks["swatch_render_pct_within_de10_front_crimson"] = {
            "coral": round(100 * sum(1 for p in pc[::3] if de2000(p, hx(SW["coral"])) < 10) / max(len(pc[::3]), 1), 1)}

with open(os.path.join(OUT, "checks.json"), "w") as f:
    json.dump(checks, f, indent=1)
print(json.dumps({k: v for k, v in checks.items() if k not in ("team_colour", "vs_warrior_bbox_px")}))
