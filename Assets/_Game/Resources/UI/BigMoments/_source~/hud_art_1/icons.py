# hud-art-1 HUD icons from the generated sheets: slice -> matte (matte.py) -> team variants (recolor.py) -> one icon system:
#   portrait-<class>-<team>   106 px @1x: bust on a rounded tile (team colour over Void Navy, navy rim), clipped to the tile
#   attack-/ability-<class>-<team>  54 px @1x: object on a Void Navy disc with a team ring
#   kaiju-egg, kaiju-egg-cracking, kaiju-hatchling, kaiju-adult-<state>  72 px @1x: the cut-out alone (it sits in the egg frame)
#   res-coin, res-wood, res-water, res-food  32 px @1x: the cut-out alone
# Every icon is written at @1x, @2x and @4x (UiIcons tiers), drawn at 4x and downsampled in premultiplied space.
# usage: python3 icons.py SHEETS_DIR OUT_DIR
import sys, os, json
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from matte import matte
import recolor as rc
NAVY = (24, 35, 59); AZURE = (22, 199, 217); CRIMSON = (255, 90, 95)
CLASSES = ["warrior", "farmer", "miner", "demolitions", "builder", "healer"]

def slice_sheet(path, count):
    im = np.asarray(Image.open(path).convert("RGB")).astype(np.int16)
    ink = (255 - im.min(axis=2)) > 30
    ink = ndi.binary_dilation(ink, iterations=12)
    lab, n = ndi.label(ink)
    objs = ndi.find_objects(lab)
    sizes = [(int((lab[o] == i + 1).sum()), o) for i, o in enumerate(objs)]
    sizes.sort(key=lambda t: -t[0]); keep = [o for _, o in sizes[:count]]
    cys = sorted((o[0].start + o[0].stop) / 2 for o in keep); mid = (cys[len(cys) // 2 - 1] + cys[len(cys) // 2]) / 2
    keep.sort(key=lambda o: (0 if (o[0].start + o[0].stop) / 2 < mid else 1, (o[1].start + o[1].stop) / 2))
    return keep

def cutout(path, sl, tmp):
    im = Image.open(path).convert("RGB")
    y0, y1, x0, x1 = sl[0].start, sl[0].stop, sl[1].start, sl[1].stop
    side = int(max(y1 - y0, x1 - x0) * 1.08)
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    c = Image.new("RGB", (side, side), (255, 255, 255))
    c.paste(im.crop((x0, y0, x1, y1)), (x0 - (cx - side // 2), y0 - (cy - side // 2)))
    c.save(tmp)
    out, rep = matte(tmp, 1024)
    return Image.fromarray(out, "RGBA"), rep

def fit(img, box, align="center"):
    a = img.split()[3]; bb = a.getbbox(); img = img.crop(bb)
    s = min(box / img.width, box / img.height); img = img.resize((max(1, int(img.width * s)), max(1, int(img.height * s))), Image.LANCZOS)
    return img

def tile_portrait(bust, team):
    S = 424; R = 76; rim = 10
    base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    mask = Image.new("L", (S, S), 0); ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), R, fill=255)
    tc = AZURE if team == "azure" else CRIMSON
    fill = tuple(int(NAVY[k] * 0.55 + tc[k] * 0.45) for k in range(3))
    tile = Image.new("RGBA", (S, S), fill + (255,))
    # one quiet value step: the lower third a touch darker (a floor for the bust), no hue gradient
    d = ImageDraw.Draw(tile); d.rectangle((0, int(S * 0.70), S, S), fill=tuple(int(v * 0.86) for v in fill) + (255,))
    b = fit(bust, int(S * 0.94))
    tile.alpha_composite(b, ((S - b.width) // 2, S - b.height + 6 if S - b.height + 6 < S * 0.12 else int(S * 0.06)))
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0)); out.paste(tile, (0, 0), mask)
    ring = Image.new("L", (S, S), 0); dr = ImageDraw.Draw(ring)
    dr.rounded_rectangle((0, 0, S - 1, S - 1), R, fill=255); dr.rounded_rectangle((rim, rim, S - 1 - rim, S - 1 - rim), R - rim, fill=0)
    out.paste(Image.new("RGBA", (S, S), NAVY + (255,)), (0, 0), ring)
    return out

def disc_icon(obj, team):
    S = 216; ring = 12
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(out)
    tc = AZURE if team == "azure" else CRIMSON
    d.ellipse((0, 0, S - 1, S - 1), fill=NAVY + (255,))
    d.ellipse((ring // 2, ring // 2, S - 1 - ring // 2, S - 1 - ring // 2), outline=tc + (255,), width=ring)
    o = fit(obj, int(S * 0.70)); out.alpha_composite(o, ((S - o.width) // 2, (S - o.height) // 2))
    return out

def plain(obj, S):
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0)); o = fit(obj, int(S * 0.96)); out.alpha_composite(o, ((S - o.width) // 2, (S - o.height) // 2))
    return out

def write(img4, name, outdir, base1x):
    # img4 is the 4x drawing; downsample premultiplied to 4x/2x/1x sizes
    a = np.asarray(img4.convert("RGBA")).astype(np.float32) / 255
    pm = np.dstack([a[..., :3] * a[..., 3:], a[..., 3:]])
    for t in (4, 2, 1):
        s = base1x * t
        ch = [Image.fromarray(pm[..., k], "F").resize((s, s), Image.LANCZOS) for k in range(4)]
        p = np.dstack([np.asarray(c) for c in ch]); A = np.clip(p[..., 3], 0, 1)
        rgb = np.where(A[..., None] > 1e-3, p[..., :3] / np.maximum(A[..., None], 1e-3), 0)
        vis = A > 0.02
        if vis.any() and (~vis).any():
            _, (by, bx) = ndi.distance_transform_edt(~vis, return_indices=True); rgb = np.where(vis[..., None], rgb, rgb[by, bx])
        Image.fromarray((np.dstack([np.clip(rgb, 0, 1), A]) * 255).astype(np.uint8), "RGBA").save(os.path.join(outdir, f"{name}@{t}x.png"), optimize=True)

def team_variant(img, mode, tmpdir, tag):
    a = os.path.join(tmpdir, tag + "-a.png"); b = os.path.join(tmpdir, tag + "-b.png")
    img.save(a); rc.recolor(a, b, mode); return Image.open(b).convert("RGBA")

if __name__ == "__main__":
    sheets, outdir = sys.argv[1], sys.argv[2]; tmp = "/tmp/hudart-icons-tmp"; os.makedirs(tmp, exist_ok=True); os.makedirs(outdir, exist_ok=True)
    report = []
    def cuts(name, n):
        p = os.path.join(sheets, f"sheet-{name}.png"); res = []
        for i, sl in enumerate(slice_sheet(p, n)):
            img, rep = cutout(p, sl, os.path.join(tmp, f"{name}-{i}.png")); rep["icon"] = f"{name}[{i}]"; report.append(rep); res.append(img)
        return res
    busts = cuts("busts", 6); attacks = cuts("attacks", 6); abilities = cuts("abilities", 6); res = cuts("resources", 4); kj = cuts("kaiju", 4)
    for c, b, at, ab in zip(CLASSES, busts, attacks, abilities):
        for team in ("azure", "crimson"):
            bb = b if team == "azure" else team_variant(b, "crimson", tmp, f"bust-{c}")
            abb = ab if team == "azure" else team_variant(ab, "crimson", tmp, f"ab-{c}")
            att = at if team == "azure" else team_variant(at, "crimson", tmp, f"at-{c}")
            write(tile_portrait(bb, team), f"portrait-{c}-{team}", outdir, 106)
            write(disc_icon(att, team), f"attack-{c}-{team}", outdir, 54)
            write(disc_icon(abb, team), f"ability-{c}-{team}", outdir, 54)
    for n, img in zip(["res-coin", "res-wood", "res-water", "res-food"], res): write(plain(img, 128), n, outdir, 32)
    egg, crack, hatch, adult = kj
    write(plain(egg, 288), "kaiju-egg", outdir, 72); write(plain(crack, 288), "kaiju-egg-cracking", outdir, 72)
    write(plain(hatch, 288), "kaiju-hatchling", outdir, 72); write(plain(adult, 288), "kaiju-adult-neutral", outdir, 72)
    for st in ("azure", "crimson", "berserk"):
        write(plain(team_variant(adult, "kaiju-" + st, tmp, "adult-" + st), 288), f"kaiju-adult-{st}", outdir, 72)
    json.dump(report, open(os.path.join(outdir, "_icons-report.json"), "w"), indent=0)
    print("icons written:", len([f for f in os.listdir(outdir) if f.endswith("@1x.png")]), "names;", "max corner alpha", max(r["corner_alpha_max"] for r in report),
          "max fringe", max(r["fringe_ratio"] for r in report), "touching edge", [r["icon"] for r in report if r["touches_edge"]])
