"""DEPTH HINTS from a generated image-to-3D mesh (TRELLIS.2 GLB), measured without graphics (trimesh + numpy).

  <venv>/bin/python -I depth_hints.py <trellis.glb> <guide-front.json> [--up Y] [--profile-guide guide-profile.json]
                                      [--into guide-a.json,guide-b.json] [--preview out.jpg]

What a 2D drawing cannot give: front-to-back thickness. From the generated mesh (height normalised to 1, front
found from where the skin-coloured vertices sit) this measures, at every 1 % of height, the depth of the whole slice
(axes found by matching the mesh silhouette to the bible front mask) and of the central body column (|x - head anchor| < 0.10 H); head depth; and, per bible palette region (vertex
colours from the GLB texture matched to the guide's palette), how far it stands in front of or behind the body's
mid-plane (the shield, the hair, a backpack). The numbers are written as "depth_hints" into the guide JSONs.
The mesh is a guess by a generative model: treat these as hints with the reliability flags given, and NEVER use the
mesh as an asset.
"""
import json
import os
import sys

import numpy as np


def srgb_to_lab(rgb):
    c = np.asarray(rgb, np.float64) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    M = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = c @ M.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[:, 1] - 16, 500 * (f[:, 0] - f[:, 1]), 200 * (f[:, 1] - f[:, 2])], axis=1)


def load_coloured(path):
    import trimesh
    sc = trimesh.load(path)
    geoms = list(sc.geometry.values()) if hasattr(sc, "geometry") else [sc]
    V, F, C, off = [], [], [], 0
    for g in geoms:
        if hasattr(sc, "graph"):
            try:
                T = sc.graph.get(sc.graph.geometry_nodes[[k for k, v in sc.geometry.items() if v is g][0]][0])[0]
                g = g.copy()
                g.apply_transform(T)
            except Exception:
                pass
        col = np.full((len(g.vertices), 3), 128.0)
        vis = g.visual
        try:
            tex = vis.material.baseColorTexture
            if tex is not None and vis.uv is not None:
                a = np.asarray(tex.convert("RGB")).astype(np.float64)
                H, W = a.shape[:2]
                uv = vis.uv
                px = (uv[:, 0] % 1 * (W - 1)).astype(int)
                py = ((1 - uv[:, 1] % 1) * (H - 1)).astype(int)
                col = a[py, px]
        except AttributeError:
            try:
                col = np.asarray(vis.vertex_colors)[:, :3].astype(np.float64)
            except Exception:
                pass
        V.append(np.asarray(g.vertices))
        F.append(np.asarray(g.faces) + off)
        C.append(col)
        off += len(g.vertices)
    V, F, C = np.concatenate(V), np.concatenate(F), np.concatenate(C)
    area = np.zeros(len(V))
    fa = 0.5 * np.linalg.norm(np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]]), axis=1)
    for k in range(3):
        np.add.at(area, F[:, k], fa / 3)
    return V, C, area


def main():
    glb, gfront = sys.argv[1], sys.argv[2]
    opt = {a: sys.argv[i + 1] for i, a in enumerate(sys.argv) if a.startswith("--") and i + 1 < len(sys.argv)}
    with open(gfront) as fh:
        guide = json.load(fh)
    regs = [r for r in guide["regions"] if r.get("lab")]
    V, C, area = load_coloured(glb)
    # Axes. Up: the glTF convention (+Y) unless --up says otherwise (a long weapon can be the longest extent, so the
    # longest axis is not trusted). Width axis and its sign: whichever of the 4 choices makes the mesh's silhouette
    # best match the bible front mask (height-normalised, feet aligned, best sideways slide). Front = right x up.
    ua = "XYZ".index(opt.get("--up", "Y").upper())
    ext = V.max(axis=0) - V.min(axis=0)
    H = ext[ua]
    yn = (V[:, ua] - V[:, ua].min()) / H
    gz = np.load(os.path.splitext(gfront)[0] + ".npz")
    mb = gz["mask"].astype(bool)
    rows = np.where(mb.any(axis=1))[0]
    Hb = rows[-1] - rows[0] + 1
    best = None
    from scipy import ndimage
    for wa in [k for k in range(3) if k != ua]:
        for sg in (1.0, -1.0):
            xs = (V[:, wa] - np.median(V[:, wa])) * sg / H
            Wm = int(np.ceil(np.ptp(xs) * Hb)) + 4
            mm = np.zeros((Hb + 4, Wm), bool)
            ci = np.clip(((xs - xs.min()) * Hb + 2).astype(int), 0, Wm - 1)
            ri = np.clip(((1 - yn) * (Hb - 1) + 2).astype(int), 0, Hb + 3)
            mm[ri, ci] = True
            mm = ndimage.binary_fill_holes(ndimage.binary_closing(mm, iterations=2))
            A = mb[rows[0]:rows[-1] + 1]
            Bm = mm[2:Hb + 2]
            for dx in range(-Bm.shape[1], A.shape[1]):
                pa = np.zeros((Hb, A.shape[1] + 2 * Bm.shape[1]), bool)
                pb = pa.copy()
                pa[:, Bm.shape[1]:Bm.shape[1] + A.shape[1]] = A
                pb[:, Bm.shape[1] + dx:Bm.shape[1] + dx + Bm.shape[1]] = Bm
                u = (pa | pb).sum()
                iou = (pa & pb).sum() / u if u else 0
                if best is None or iou > best[0]:
                    best = (float(iou), wa, sg)
    _, wa, sg = best
    U = np.zeros(3)
    U[ua] = 1.0
    Rv = np.zeros(3)
    Rv[wa] = sg
    F = np.cross(Rv, U)
    c0 = np.median(V, axis=0)
    zn = (V - c0) @ F / H            # + = toward the viewer of the front crop
    xn = (V - c0) @ Rv / H           # + = screen right in the front crop
    fa = int(np.argmax(np.abs(F)))
    axis_note = (f"up = +{'XYZ'[ua]}, screen right = {'+' if sg > 0 else '-'}{'XYZ'[wa]}, front = "
                 f"{'+' if F[fa] > 0 else '-'}{'XYZ'[fa]}")
    how = f"silhouette match with the bible front mask (IoU {best[0]:.2f})"
    # colours: the generated texture is darker and flatter than the art; match Lab mean/std to the bible's pixels
    lab = srgb_to_lab(C)
    bl = srgb_to_lab(gz["rgb"][mb].astype(np.float64))
    lab = (lab - lab.mean(axis=0)) / np.maximum(lab.std(axis=0), 1e-6) * bl.std(axis=0) + bl.mean(axis=0)
    RL = np.array([r["lab"] for r in regs], np.float64)
    w = np.array([0.5, 1, 1])
    d = np.linalg.norm((lab[:, None, :] - RL[None]) * w, axis=2)
    lbl = d.argmin(axis=1)
    lbl[d.min(axis=1) > 30] = -1
    head = (yn >= 0.75) & (yn <= 0.95)
    ax = np.average(xn[head], weights=area[head]) if head.any() else 0.0
    xn = xn - ax
    core = np.abs(xn) < 0.10
    prof = {"y": [], "full_front": [], "full_back": [], "full_depth": [], "core_front": [], "core_back": [],
            "core_depth": []}
    for i in range(101):
        yy = i / 100
        sl = np.abs(yn - yy) <= 0.006
        prof["y"].append(round(yy, 2))
        for nm, sel in (("full", sl), ("core", sl & core)):
            if sel.sum() >= 3:
                f, b = np.percentile(zn[sel], 99), np.percentile(zn[sel], 1)
                prof[f"{nm}_front"].append(round(float(f), 4))
                prof[f"{nm}_back"].append(round(float(b), 4))
                prof[f"{nm}_depth"].append(round(float(f - b), 4))
            else:
                for k in ("front", "back", "depth"):
                    prof[f"{nm}_{k}"].append(None)

    def arr(k):
        return np.array([np.nan if v is None else v for v in prof[k]])
    cf, cb, ff, fb_ = arr("core_front"), arr("core_back"), arr("full_front"), arr("full_back")
    torso = (np.array(prof["y"]) >= 0.3) & (np.array(prof["y"]) <= 0.6)
    mid = float(np.nanmedian(((cf + cb) / 2)[torso]))
    chin = guide["landmarks"].get("chin", {}).get("y", 0.62)
    yv = np.array(prof["y"])
    hb = yv >= chin
    hint = {"source": os.path.basename(glb), "generator": "TRELLIS.2 (hosted Space, MIT), one generation from the "
            "front crop", "units": "character heights; z + = toward the front-crop viewer",
            "front_found_from": how, "axes": axis_note,
            "extent_h": {"width": round(float(np.ptp(xn)), 3), "depth": round(float(np.ptp(zn)), 3)},
            "body_mid_plane_z": round(mid, 4),
            "head_depth": round(float(np.nanmax(arr("core_depth")[hb])), 4),
            "head_depth_over_width": None,
            "torso_depth": round(float(np.nanmedian(arr("core_depth")[torso])), 4),
            "forward_standoff": None, "backward_standoff": None, "depth_profile": prof, "regions": []}
    hw = guide["landmarks"].get("head_width", {}).get("w")
    if hw:
        hint["head_depth_over_width"] = round(hint["head_depth"] / hw, 3)
    fwd = ff - cf
    bwd = cb - fb_
    if not np.all(np.isnan(fwd)):
        i = int(np.nanargmax(fwd))
        hint["forward_standoff"] = {"max": round(float(fwd[i]), 4), "at_y": prof["y"][i],
                                    "band": [prof["y"][j] for j in np.where(fwd > 0.5 * fwd[i])[0][[0, -1]]],
                                    "meaning": "how far anything (shield, weapon) stands in front of the body column"}
    if not np.all(np.isnan(bwd)):
        i = int(np.nanargmax(bwd))
        hint["backward_standoff"] = {"max": round(float(bwd[i]), 4), "at_y": prof["y"][i],
                                     "meaning": "how far anything (hair, cape, pack) stands behind the body column"}
    # side columns: props held at the sides (Warrior front crop: shield screen-right, cleaver screen-left)
    for side, selx in (("screen_left", xn < -0.15), ("screen_right", xn > 0.15)):
        fr_, rows_ = [], []
        for i, yy in enumerate(prof["y"]):
            sl = (np.abs(yn - yy) <= 0.006) & selx
            if sl.sum() >= 5 and not np.isnan(cf[i]):
                fr_.append(float(np.percentile(zn[sl], 95)) - cf[i])
                rows_.append(yy)
        if fr_:
            fr_ = np.array(fr_)
            k = int(np.argmax(fr_))
            hint[f"forward_standoff_{side}"] = {
                "max": round(float(fr_[k]), 4), "at_y": rows_[k],
                "band": [rows_[j] for j in np.where(fr_ > 0.5 * fr_[k])[0][[0, -1]]] if fr_[k] > 0 else None,
                "median": round(float(np.median(fr_)), 4),
                "meaning": "95th-percentile front of the side column minus the body column's front, per height"}
    tb = np.nanmedian(cb[torso])
    tf = np.nanmedian(cf[torso])
    hint["head_back_behind_torso_back"] = round(float(tb - np.nanmin(cb[hb])), 4)
    hint["head_front_ahead_of_torso_front"] = round(float(np.nanmax(cf[hb]) - tf), 4)
    hint["notes"] = ("the generator guessed the unseen depth; the cleaver came out pointing toward the viewer (about "
                     "1 H), so 'forward_standoff' and full-slice depth at 14-29 % height measure that blade, not the "
                     "body; use the body column, the head numbers and the side columns")
    tot = area.sum()
    for k, r in enumerate(regs):
        sel = lbl == k
        if area[sel].sum() < 0.01 * tot:
            continue
        z = zn[sel]
        hint["regions"].append({
            "id": r["id"], "name": r.get("name"), "hex": r["hex"], "area_share": round(float(area[sel].sum() / tot), 3),
            "front_z": round(float(np.percentile(z, 95)), 4), "back_z": round(float(np.percentile(z, 5)), 4),
            "mean_offset_from_mid_plane": round(float(np.average(z, weights=area[sel]) - mid), 4),
            "y_span": [round(float(np.percentile(yn[sel], 5)), 3), round(float(np.percentile(yn[sel], 95)), 3)],
            "x_centroid": round(float(np.average(xn[sel], weights=area[sel])), 3)})
    # reliability check against the bible PROFILE view: its width profile should match the generated depth
    if "--profile-guide" in opt:
        with open(opt["--profile-guide"]) as fh:
            pg = json.load(fh)
        pl = np.array([np.nan if v is None else v for v in pg["width_profile"]["left"]])
        pr = np.array([np.nan if v is None else v for v in pg["width_profile"]["right"]])
        dep_b = pr - pl
        dep_g = arr("full_depth")
        ok = ~np.isnan(dep_b) & ~np.isnan(dep_g)
        hint["check_vs_profile_view"] = {
            "mean_abs_diff_h": round(float(np.mean(np.abs(dep_b[ok] - dep_g[ok]))), 4),
            "correlation": round(float(np.corrcoef(dep_b[ok], dep_g[ok])[0, 1]), 3),
            "meaning": "bible profile-view silhouette width vs generated full depth, per 1 % of height (the profile "
                       "drawing holds the shield and cleaver differently, so expect differences at prop heights)"}
    out_guides = opt.get("--into", gfront).split(",")
    for gp in out_guides:
        with open(gp) as fh:
            gj = json.load(fh)
        gj["depth_hints"] = hint
        with open(gp, "w") as fh:
            json.dump(gj, fh, indent=1)
    if "--preview" in opt:
        from PIL import Image, ImageDraw
        S = 300
        im = Image.new("RGB", (2 * S + 20, S + 20), "white")
        dr = ImageDraw.Draw(im)
        idx = np.random.default_rng(0).choice(len(V), min(len(V), 40000), replace=False)
        for ox, xs in ((10, xn), (S + 20, zn)):
            for i in idx:
                dr.point((ox + S / 2 + xs[i] * S, 10 + (1 - yn[i]) * S), fill=tuple(int(c) for c in C[i]))
        for i, yy in enumerate(prof["y"]):
            if prof["core_front"][i] is not None:
                py = 10 + (1 - yy) * S
                dr.point((S + 20 + S / 2 + prof["core_front"][i] * S, py), fill=(255, 0, 0))
                dr.point((S + 20 + S / 2 + prof["core_back"][i] * S, py), fill=(255, 0, 0))
        dr.text((12, 2), "generated mesh: front (x,y)          side (z,y), front to the right; red = body column",
                fill=(0, 0, 0))
        im.save(opt["--preview"], quality=70)
    print(json.dumps({k: v for k, v in hint.items() if k not in ("depth_profile", "regions")}, indent=1))
    for r in hint["regions"]:
        print(f"  {r['name']:14s} {r['hex']} area {r['area_share']:.2f} offset {r['mean_offset_from_mid_plane']:+.3f} "
              f"front {r['front_z']:+.3f} back {r['back_z']:+.3f} y {r['y_span']}")


if __name__ == "__main__":
    main()
