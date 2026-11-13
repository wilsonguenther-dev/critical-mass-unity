"""project_paint: camera-project a bible view onto the POSED mesh, inside Blender (numpy only, Blender 5.x).

    import sys; sys.path.insert(0, "<repo>/Assets/_Game/Art/_tools~/bible_fit")   # or: from cm_art import project_paint
    import project_paint as pp
    res = pp.project_view(mesh_objects, "guide-06-front.json", azimuth_deg=8, elevation_deg=6, outline_m=0.0135)
    print(res["report"]["summary"])          # bloat / missing in plain words
    pp.save_result(res, "/tmp/x/project-front")  # .json report + .npz (masks, per-vertex samples) for paint_preview.py
    pp.write_guide_attributes(res)           # per-vertex GUIDE attributes (never a colour attribute, never exported)
    pp.apply_guide_paint(obj, res, regions=["team teal"], min_conf=0.7)   # optional: starting paint, RGB ONLY
    pp.clear_guide_attributes(mesh_objects)  # before export

What it does
  1. Evaluates the posed meshes (armature applied, outline-hull Solidify modifiers switched off so vertex indices match
     the mesh), projects them orthographically from (azimuth, elevation) exactly like render_warrior.py's bible
     framings (azimuth 0 = camera at -Y in front of a character facing -Y; negative = camera toward his right).
  2. Rasterises a depth buffer at the bible crop's scale (character height in pixels = the guide's), widened by the
     outline thickness, puts the feet on the bible's feet row and slides sideways to the best overlap (the same
     alignment as fit_report.py).
  3. Per vertex that faces the camera and is not hidden: the bible pixel under it -> region label (outline ink is
     skipped, the 5x5 neighbourhood decides), the region's palette colour (linear RGB) and a confidence 0..1
     (= label agreement in the neighbourhood x how squarely the vertex faces the camera).
  4. Reports BLOAT (mesh outside the art's silhouette) per height band, per side, per object and per deform bone,
     and MISSING mass (art with no mesh) per height band and side.

Team-mask contract: the 'Col' colour attribute's ALPHA is the team-mask code (cm_art/palette.py MASK_*). This module
never writes alpha: the guide lives in separate generic attributes (_bible_guide_rgb FLOAT_VECTOR, _bible_guide_conf
FLOAT, _bible_guide_label INT; the FBX exporter only exports colour attributes, so they never reach Unity), and
apply_guide_paint() writes RGB only and checks alpha is bit-identical afterwards.
"""
import json
import math
import os

import numpy as np

GUIDE_RGB_ATTR = "_bible_guide_rgb"
GUIDE_CONF_ATTR = "_bible_guide_conf"
GUIDE_LABEL_ATTR = "_bible_guide_label"
HEAD_BAND = (0.75, 0.95)


# ------------------------------------------------------------------ camera
def view_basis(azimuth_deg, elevation_deg):
    """d = unit vector from the target toward the camera; right/up = screen axes (render_warrior.view_dir)."""
    a, e = math.radians(azimuth_deg), math.radians(elevation_deg)
    d = np.array([math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)])
    fwd = -d
    right = np.cross(fwd, [0.0, 0.0, 1.0])
    right /= np.linalg.norm(right)
    up = np.cross(right, fwd)
    return d, right, up


def setup_guide_camera(scene, objects, azimuth_deg, elevation_deg, height_px=720, margin=0.03,
                       name="BibleGuideCam"):
    """Orthographic camera that frames the posed objects from (azimuth, elevation), resolution set so the character
    fills the height. Render it with a transparent film (and an all-white ID material for a mask) and give the PNG to
    fit_report.py. Returns the camera object (also made the scene camera)."""
    import bpy
    from mathutils import Vector
    pts = np.concatenate([_eval(ob)[0] for ob in objects])
    d, right, up = view_basis(azimuth_deg, elevation_deg)
    u, v = pts @ right, pts @ up
    w, h = u.max() - u.min(), v.max() - v.min()
    cu, cv = (u.max() + u.min()) / 2, (v.max() + v.min()) / 2
    centre = pts.mean(axis=0)
    centre = centre + right * (cu - centre @ right) + up * (cv - centre @ up)
    cam_data = bpy.data.cameras.get(name) or bpy.data.cameras.new(name)
    cam = bpy.data.objects.get(name) or bpy.data.objects.new(name, cam_data)
    if cam.name not in scene.collection.objects:
        scene.collection.objects.link(cam)
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = float(max(w, h) * (1 + 2 * margin))
    cam_data.clip_start, cam_data.clip_end = 0.1, 100.0
    cam.location = Vector(centre + d * 20.0)
    cam.rotation_euler = Vector(-d).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.resolution_y = int(height_px)
    scene.render.resolution_x = max(16, int(round(height_px * w / h)))
    scene.render.resolution_percentage = 100
    return cam


# ------------------------------------------------------------------ mesh access
def _eval(ob):
    """World-space vertices, triangles and vertex normals of the evaluated (posed) mesh with outline hulls off."""
    import bpy
    saved = []
    for md in ob.modifiers:
        if md.type == "SOLIDIFY" or "outline" in md.name.lower() or "hull" in md.name.lower():
            saved.append((md, md.show_viewport, md.show_render))
            md.show_viewport = md.show_render = False
    bpy.context.view_layer.update()
    try:
        dg = bpy.context.evaluated_depsgraph_get()
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        n = len(me.vertices)
        co = np.empty(n * 3, np.float64)
        me.vertices.foreach_get("co", co)
        me.calc_loop_triangles()
        tri = np.empty(len(me.loop_triangles) * 3, np.int64)
        me.loop_triangles.foreach_get("vertices", tri)
        M = np.array(ev.matrix_world)
        ev.to_mesh_clear()
    finally:
        for md, sv, sr in saved:
            md.show_viewport, md.show_render = sv, sr
        bpy.context.view_layer.update()
    co = co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]
    tri = tri.reshape(-1, 3)
    if n != len(ob.data.vertices):
        raise RuntimeError(f"{ob.name}: evaluated mesh has {n} vertices, mesh has {len(ob.data.vertices)}; a "
                           "topology-changing modifier is on (only Armature and outline Solidify are supported)")
    fn = np.cross(co[tri[:, 1]] - co[tri[:, 0]], co[tri[:, 2]] - co[tri[:, 0]])   # area-weighted face normals
    vn = np.zeros_like(co)
    for k in range(3):
        np.add.at(vn, tri[:, k], fn)
    vn /= np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12)
    return co, tri, vn


def _dominant_groups(ob):
    """Per vertex: (dominant deform bone, dominant non-bone vertex group such as a 'feat_*' part group)."""
    bones = set()
    for md in ob.modifiers:
        if md.type == "ARMATURE" and md.object is not None:
            bones |= {b.name for b in md.object.data.bones}
    names = {g.index: g.name for g in ob.vertex_groups}
    out_b, out_p = [], []
    for v in ob.data.vertices:
        bb, bw, pb, pw = None, 0.0, None, 0.0
        for g in v.groups:
            nm = names.get(g.group)
            if nm is None:
                continue
            if nm in bones or not bones:
                if g.weight > bw:
                    bb, bw = nm, g.weight
            elif g.weight > pw:
                pb, pw = nm, g.weight
        out_b.append(bb or "-")
        out_p.append(pb or ob.name)
    return out_b, out_p


# ------------------------------------------------------------------ raster + mask helpers (numpy only)
def _raster(P, z, tris, H, W, zbuf, owner, base):
    """Depth raster: zbuf keeps the NEAREST surface (largest z = toward the camera); owner = global vertex index."""
    for t in tris:
        p0, p1, p2 = P[t[0]], P[t[1]], P[t[2]]
        x0 = int(max(0, math.floor(min(p0[0], p1[0], p2[0]))))
        x1 = int(min(W - 1, math.ceil(max(p0[0], p1[0], p2[0]))))
        y0 = int(max(0, math.floor(min(p0[1], p1[1], p2[1]))))
        y1 = int(min(H - 1, math.ceil(max(p0[1], p1[1], p2[1]))))
        if x1 < x0 or y1 < y0:
            continue
        area = (p1[0] - p0[0]) * (p2[1] - p0[1]) - (p1[1] - p0[1]) * (p2[0] - p0[0])
        if abs(area) < 1e-9:
            continue
        xs = np.arange(x0, x1 + 1) + 0.5
        ys = np.arange(y0, y1 + 1)[:, None] + 0.5
        w0 = ((p1[0] - xs) * (p2[1] - ys) - (p1[1] - ys) * (p2[0] - xs)) / area
        w1 = ((p2[0] - xs) * (p0[1] - ys) - (p2[1] - ys) * (p0[0] - xs)) / area
        w2 = 1.0 - w0 - w1
        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not inside.any():
            continue
        zz = w0 * z[t[0]] + w1 * z[t[1]] + w2 * z[t[2]]
        sub = zbuf[y0:y1 + 1, x0:x1 + 1]
        upd = inside & (zz > sub)
        sub[upd] = zz[upd]
        own = owner[y0:y1 + 1, x0:x1 + 1]
        own[upd] = base + t[0]


def _dilate(m, r):
    if r <= 0:
        return m
    out = m.copy()
    H, W = m.shape
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy > r * r:
                continue
            ys, yd = (slice(0, H - dy), slice(dy, H)) if dy >= 0 else (slice(-dy, H), slice(0, H + dy))
            xs, xd = (slice(0, W - dx), slice(dx, W)) if dx >= 0 else (slice(-dx, W), slice(0, W + dx))
            out[yd, xd] |= m[ys, xs]
    return out


def _frame(m, anchor=None):
    ys, xs = np.where(m)
    top, bot = int(ys.min()), int(ys.max())
    H = float(bot - top + 1)
    if anchor is None:
        y = (bot + 1 - (ys + 0.5)) / H
        sel = (y >= HEAD_BAND[0]) & (y <= HEAD_BAND[1])
        anchor = float(xs[sel].mean() + 0.5) if sel.any() else float(xs.mean() + 0.5)
    return top, bot, H, anchor


def _place(mb, mm, dy, dx, pad):
    Hc, Wc = mb.shape[0] + 2 * pad, mb.shape[1] + 2 * pad
    A = np.zeros((Hc, Wc), bool)
    A[pad:pad + mb.shape[0], pad:pad + mb.shape[1]] = mb
    B = np.zeros((Hc, Wc), bool)
    oy, ox = pad + dy, pad + dx
    h, w = mm.shape
    ya, yb, xa, xb = max(0, oy), min(Hc, oy + h), max(0, ox), min(Wc, ox + w)
    if yb > ya and xb > xa:
        B[ya:yb, xa:xb] = mm[ya - oy:yb - oy, xa - ox:xb - ox]
    return A, B


# ------------------------------------------------------------------ main entry
def project_view(objects, guide_json, azimuth_deg, elevation_deg, outline_m=0.0135, eps_frac=0.02, window=2):
    with open(guide_json) as fh:
        guide = json.load(fh)
    z = np.load(os.path.splitext(guide_json)[0] + ".npz")
    mb, labels = z["mask"].astype(bool), z["labels"].astype(np.int32)
    tb, bb, Hb, axb = _frame(mb, guide["frame"]["anchor_x_px"])
    regions = {r["id"]: r for r in guide["regions"] if r.get("hex")}
    d, right, up = view_basis(azimuth_deg, elevation_deg)

    data = []
    for ob in objects:
        co, tri, vn = _eval(ob)
        data.append((ob, co, tri, vn))
    allp = np.concatenate([c for _, c, _, _ in data])
    u_all, v_all = allp @ right, allp @ up
    umin, umax, vmin, vmax = u_all.min(), u_all.max(), v_all.min(), v_all.max()
    height_m = (vmax - vmin) + 2 * outline_m
    s = Hb / height_m                                      # pixels per metre, bible scale
    pad = int(0.1 * Hb) + 4
    Hm = int(math.ceil(height_m * s)) + 2 * pad
    Wm = int(math.ceil((umax - umin + 2 * outline_m) * s)) + 2 * pad
    zbuf = np.full((Hm, Wm), -1e9)
    owner = np.full((Hm, Wm), -1, np.int64)
    base, bases, proj = 0, [], []
    for ob, co, tri, vn in data:
        P = np.stack([(co @ right - umin + outline_m) * s + pad, (vmax + outline_m - co @ up) * s + pad], axis=1)
        depth = co @ d
        _raster(P, depth, tri, Hm, Wm, zbuf, owner, base)
        bases.append(base)
        proj.append((P, depth))
        base += len(co)
    core = zbuf > -1e8
    mm = _dilate(core, int(round(outline_m * s)))
    tm, bm_, Hm_, axm = _frame(mm)
    dy = bb - bm_
    base_dx = int(round(axb - axm))
    padc = int(0.6 * Hb)
    best = (-1.0, base_dx)
    for dx in range(base_dx - int(0.25 * Hb), base_dx + int(0.25 * Hb) + 1):
        A, B = _place(mb, mm, dy, dx, padc)
        u_ = (A | B).sum()
        iou = (A & B).sum() / u_ if u_ else 0.0
        if iou > best[0]:
            best = (float(iou), dx)
    iou, dx = best
    A, B = _place(mb, mm, dy, dx, padc)
    O = np.full(A.shape, -1, np.int64)
    _, Ob = _place(mb, np.ones_like(mm), dy, dx, padc)
    oy, ox = padc + dy, padc + dx
    ya, yb, xa, xb = max(0, oy), min(O.shape[0], oy + Hm), max(0, ox), min(O.shape[1], ox + Wm)
    O[ya:yb, xa:xb] = owner[ya - oy:yb - oy, xa - ox:xb - ox]

    # per-vertex samples
    per_object = {}
    eps = eps_frac * height_m
    bone_of, part_of = [], []
    for (ob, co, tri, vn), (P, depth), b0 in zip(data, proj, bases):
        n = len(co)
        xi = np.clip(P[:, 0].astype(int), 0, Wm - 1)
        yi = np.clip(P[:, 1].astype(int), 0, Hm - 1)
        visible = depth >= zbuf[yi, xi] - eps
        facing = vn @ d
        rb, cb = yi + dy, xi + dx                            # bible crop pixel
        lab = np.full(n, -1, np.int32)
        conf = np.zeros(n)
        Hc, Wc = labels.shape
        for i in np.where(visible & (facing > 0.05))[0]:
            r, c = rb[i], cb[i]
            if not (0 <= r < Hc and 0 <= c < Wc) or not mb[r, c]:
                continue
            win = labels[max(0, r - window):r + window + 1, max(0, c - window):c + window + 1]
            good = win[win >= 0]
            if len(good) == 0:
                continue
            centre = labels[r, c]
            pick = centre if centre >= 0 else int(np.bincount(good).argmax())
            lab[i] = pick
            conf[i] = (good == pick).sum() / float(win.size) * min(1.0, max(0.0, (facing[i] - 0.15) / 0.45))
        rgb = np.zeros((n, 3))
        for rid, r in regions.items():
            h = r["hex"].lstrip("#")
            srgb = np.array([int(h[k:k + 2], 16) / 255.0 for k in (0, 2, 4)])
            lin = np.where(srgb <= 0.04045, srgb / 12.92, ((srgb + 0.055) / 1.055) ** 2.4)
            rgb[lab == rid] = lin
        per_object[ob.name] = {"label": lab, "conf": conf, "rgb_lin": rgb, "visible": visible & (facing > 0.05),
                               "bible_px": np.stack([cb, rb], axis=1)}
        b_, p_ = _dominant_groups(ob)
        bone_of.extend(b_)
        part_of.extend(p_)
    names_by_base = [(b0, len(co), ob.name) for (ob, co, _, _), b0 in zip(data, bases)]

    # bloat / missing reports
    past, short = B & ~A, A & ~B
    rows = np.arange(A.shape[0])[:, None]
    yb_n = (padc + bb + 1 - (rows + 0.5)) / Hb
    cols = np.arange(A.shape[1])[None, :]
    right_side = (cols + 0.5) >= (padc + axb)
    bands = []
    for k in range(10):
        sel = (yb_n >= k / 10) & (yb_n < (k + 1) / 10)
        bands.append({"band": f"{k * 10}-{(k + 1) * 10}%",
                      "bloat_left": round(float((past & sel & ~right_side).sum()) / A.sum(), 4),
                      "bloat_right": round(float((past & sel & right_side).sum()) / A.sum(), 4),
                      "missing_left": round(float((short & sel & ~right_side).sum()) / A.sum(), 4),
                      "missing_right": round(float((short & sel & right_side).sum()) / A.sum(), 4)})
    ob_stats, bone_stats = {}, {}
    own_past = O[past & (O >= 0)]
    own_all = O[B & (O >= 0)]
    for b0, n, nm in names_by_base:
        tot = int(((own_all >= b0) & (own_all < b0 + n)).sum())
        bl = int(((own_past >= b0) & (own_past < b0 + n)).sum())
        if tot:
            ob_stats[nm] = {"visible_px": tot, "bloat_px": bl, "bloat_share_of_part": round(bl / tot, 3)}
    bc_all = np.bincount(own_all, minlength=len(bone_of)) if len(own_all) else np.zeros(len(bone_of))
    bc_past = np.bincount(own_past, minlength=len(bone_of)) if len(own_past) else np.zeros(len(bone_of))
    part_stats = {}
    for vi in np.where(bc_all > 0)[0]:
        for key, st in ((bone_of[vi], bone_stats), (part_of[vi], part_stats)):
            e = st.setdefault(key, [0, 0])
            e[0] += int(bc_all[vi])
            e[1] += int(bc_past[vi])

    def top(st, label):
        return sorted(({label: k, "visible_px": v[0], "bloat_px": v[1], "bloat_share_of_part": round(v[1] / v[0], 3)}
                       for k, v in st.items() if v[1] > 0), key=lambda e: -e["bloat_px"])[:10]
    bones, parts = top(bone_stats, "bone"), top(part_stats, "part")
    ring_px = int((past & (O < 0)).sum())
    lines = [f"IoU {iou:.3f}; mesh outside the art (bloat) {past.sum() / A.sum() * 100:.1f}% of the bible silhouette, "
             f"art with no mesh (missing) {short.sum() / A.sum() * 100:.1f}%"]
    worst = sorted(((b[k], b["band"], k) for b in bands for k in ("bloat_left", "bloat_right", "missing_left",
                                                                    "missing_right")), reverse=True)[:4]
    for val, band, k in worst:
        if val >= 0.005:
            what, side = k.split("_")
            lines.append(f"{'bloat' if what == 'bloat' else 'missing mass'} {val * 100:.1f}% at {band} height, "
                         f"screen-{side}")
    for e in parts[:3]:
        lines.append(f"part {e['part']}: {e['bloat_px']} px outside the art ({e['bloat_share_of_part'] * 100:.0f}% "
                     "of its visible area)")
    for e in bones[:3]:
        lines.append(f"bone {e['bone']}: {e['bloat_px']} px outside the art ({e['bloat_share_of_part'] * 100:.0f}% "
                     "of its visible area)")
    painted = sum(int((po["conf"] > 0.5).sum()) for po in per_object.values())
    visible_n = sum(int(po["visible"].sum()) for po in per_object.values())
    lines.append(f"paint guide: {painted} of {visible_n} camera-facing vertices sampled with confidence > 0.5")
    report = {"view": guide.get("view"), "guide": os.path.abspath(guide_json), "azimuth_deg": azimuth_deg,
              "elevation_deg": elevation_deg, "iou": round(iou, 4), "px_per_m": round(s, 2),
              "align": {"dy_px": int(dy), "dx_px": int(dx), "slide_from_anchor_px": int(dx - base_dx)},
              "bloat_share": round(float(past.sum()) / A.sum(), 4),
              "missing_share": round(float(short.sum()) / A.sum(), 4), "outline_ring_bloat_px": ring_px,
              "bands": bands, "objects": ob_stats, "parts": parts, "bones": bones, "summary": lines}
    return {"report": report, "per_object": per_object, "masks": {"bible": A, "ours": B, "owner": O},
            "pad": padc, "regions": regions}


# ------------------------------------------------------------------ outputs
def save_result(res, out_prefix):
    os.makedirs(os.path.dirname(os.path.abspath(out_prefix)), exist_ok=True)
    with open(out_prefix + ".json", "w") as fh:
        json.dump(res["report"], fh, indent=1)
    arrays = {"bible": res["masks"]["bible"], "ours": res["masks"]["ours"], "pad": np.array(res["pad"])}
    pts, cols, conf = [], [], []
    for nm, po in res["per_object"].items():
        sel = po["label"] >= 0
        pts.append(po["bible_px"][sel])
        cols.append(po["rgb_lin"][sel])
        conf.append(po["conf"][sel])
    if pts:
        arrays.update(px=np.concatenate(pts), rgb_lin=np.concatenate(cols), conf=np.concatenate(conf))
    np.savez_compressed(out_prefix + ".npz", **arrays)


def write_guide_attributes(res):
    import bpy
    for nm, po in res["per_object"].items():
        me = bpy.data.objects[nm].data
        for a in (GUIDE_RGB_ATTR, GUIDE_CONF_ATTR, GUIDE_LABEL_ATTR):
            if a in me.attributes:
                me.attributes.remove(me.attributes[a])
        me.attributes.new(GUIDE_RGB_ATTR, "FLOAT_VECTOR", "POINT").data.foreach_set(
            "vector", po["rgb_lin"].astype(np.float32).ravel())
        me.attributes.new(GUIDE_CONF_ATTR, "FLOAT", "POINT").data.foreach_set("value", po["conf"].astype(np.float32))
        me.attributes.new(GUIDE_LABEL_ATTR, "INT", "POINT").data.foreach_set("value", po["label"].astype(np.int32))


def clear_guide_attributes(objects):
    for ob in objects:
        me = ob.data
        for a in (GUIDE_RGB_ATTR, GUIDE_CONF_ATTR, GUIDE_LABEL_ATTR):
            if a in me.attributes:
                me.attributes.remove(me.attributes[a])


def apply_guide_paint(ob, res, regions=None, min_conf=0.6, attr="Col", mix=1.0):
    """Starting paint from the guide: for vertices with confidence >= min_conf (and, if given, in the named regions
    or region ids) blend the colour attribute's RGB toward the guide colour. ALPHA (team mask) is never written and
    is verified unchanged. Returns the number of vertices painted."""
    po = res["per_object"][ob.name]
    ids = None
    if regions is not None:
        ids = {r if isinstance(r, int) else None for r in regions} - {None}
        names = {r for r in regions if isinstance(r, str)}
        ids |= {rid for rid, r in res["regions"].items() if r.get("name") in names}
    sel = (po["conf"] >= min_conf) & (po["label"] >= 0)
    if ids is not None:
        sel &= np.isin(po["label"], list(ids))
    ca = ob.data.color_attributes[attr]
    if ca.domain != "POINT":
        raise RuntimeError(f"{ob.name}.{attr} is {ca.domain}; apply_guide_paint supports POINT colour attributes")
    n = len(ob.data.vertices)
    c = np.empty(n * 4, np.float32)
    ca.data.foreach_get("color", c)
    c = c.reshape(-1, 4)
    alpha_before = c[:, 3].copy()
    c[sel, :3] = (1 - mix) * c[sel, :3] + mix * po["rgb_lin"][sel]
    c[:, 3] = alpha_before
    ca.data.foreach_set("color", c.ravel())
    chk = np.empty(n * 4, np.float32)
    ca.data.foreach_get("color", chk)
    if not np.array_equal(chk.reshape(-1, 4)[:, 3], alpha_before):
        raise RuntimeError("team-mask alpha changed: refusing")
    return int(sel.sum())


def depth_profile(objects, azimuth_deg, elevation_deg, chin=0.62):
    """Front-to-back numbers of the posed mesh in the same units as depth_hints.py (character heights, the body
    column = |x - head anchor| < 0.10 H), to compare with a guide's depth_hints. Measured level (elevation 0, like
    depth_hints) whatever elevation_deg says: a tilted view mixes height into depth near the top of the head."""
    d, right, up = view_basis(azimuth_deg, 0.0)
    pts = np.concatenate([_eval(ob)[0] for ob in objects])
    v, u, z = pts @ up, pts @ right, pts @ d
    H = v.max() - v.min()
    yn, xn, zn = (v - v.min()) / H, u / H, z / H
    head = (yn >= 0.75) & (yn <= 0.95)
    xn = xn - xn[head].mean()
    core = np.abs(xn) < 0.10
    rows = []
    for i in range(101):
        sl = (np.abs(yn - i / 100) <= 0.006) & core
        rows.append((np.percentile(zn[sl], 99), np.percentile(zn[sl], 1)) if sl.sum() >= 3 else (np.nan, np.nan))
    f, b = np.array(rows).T
    y = np.arange(101) / 100
    torso, hb = (y >= 0.3) & (y <= 0.6), y >= chin
    return {"head_depth": round(float(np.nanmax((f - b)[hb])), 4),
            "torso_depth": round(float(np.nanmedian((f - b)[torso])), 4),
            "head_back_behind_torso_back": round(float(np.nanmedian(b[torso]) - np.nanmin(b[hb])), 4),
            "head_front_ahead_of_torso_front": round(float(np.nanmax(f[hb]) - np.nanmedian(f[torso])), 4),
            "core_depth": [None if np.isnan(a) else round(float(a), 4) for a in (f - b)]}
