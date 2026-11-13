"""Look-dev B, plain Python (no graphics): generated GLB -> clean game mesh, re-UV, re-baked + repainted textures,
skin weights transferred from the v7 Warrior, un-posed to the KayKit rest pose.

  <venv>/bin/python -I process.py <raw.glb> <v7pose.npz> <guides_dir> <out_dir>

Steps (all CPU): inspect the raw mesh -> orient and fit it to the v7 Idle display pose (front silhouette against the
bible mask) -> split Body / Shield / Cleaver (nearest v7 part, front-weighted) -> voxel remesh (occupancy grid,
close, fill, light smoothing, marching cubes) -> Taubin smooth -> quadric decimate -> xatlas UVs -> bake colour
from the raw texture (normal-aware nearest samples, drawn-ink texels excluded) -> repaint toward the bible region
palette -> voxel ambient occlusion + light gradient -> team mask, emission mask -> weights from v7 by proximity ->
inverse linear-blend skinning to the rest pose.
Two variants: 'fused' (one remeshed piece, every vertex weighted by proximity, the way a generated mesh arrives)
and 'parts' (body weighted by proximity, shield and cleaver as rigid attachments on handslot.l / handslot.r).
"""
import json
import os
import sys

import numpy as np
import trimesh
from PIL import Image
from scipy import ndimage as ndi
from scipy.spatial import cKDTree

RAW, V7, GUIDES, OUT = sys.argv[1:5]
os.makedirs(OUT, exist_ok=True)
REPORT = {}
rng = np.random.default_rng(0)
TEX = 1024


# ---------------------------------------------------------------- colour helpers
def srgb_to_lin(c):
    c = np.asarray(c, np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def rgb2lab(rgb):  # rgb 0..1 sRGB, (...,3)
    l = srgb_to_lin(rgb)
    M = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = l @ M.T / np.array([0.9505, 1.0, 1.089])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def lab2rgb(lab):
    fy = (lab[..., 0] + 16) / 116
    fx, fz = fy + lab[..., 1] / 500, fy - lab[..., 2] / 200
    f = np.stack([fx, fy, fz], -1)
    xyz = np.where(f > 0.2069, f ** 3, (f - 16 / 116) / 7.787) * np.array([0.9505, 1.0, 1.089])
    Mi = np.linalg.inv(np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]]))
    return lin_to_srgb(xyz @ Mi.T)


def hexrgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)]) / 255.0


# ---------------------------------------------------------------- 1. load + inspect the raw GLB
sc = trimesh.load(RAW, force="scene")
parts_v, parts_f, parts_uv, parts_vc, off = [], [], [], [], 0
tex_img = None
for g in sc.dump():
    vc = None
    if getattr(g.visual, "kind", None) == "vertex":
        vc = np.asarray(g.visual.vertex_colors)[:, :3] / 255.0
    parts_vc.append(vc if vc is not None else np.zeros((len(g.vertices), 3)))
    v = np.asarray(g.vertices)
    parts_v.append(v)
    parts_f.append(np.asarray(g.faces) + off)
    off += len(v)
    uv = getattr(g.visual, "uv", None)
    parts_uv.append(uv if uv is not None else np.zeros((len(v), 2)))
    mat = getattr(g.visual, "material", None)
    if tex_img is None and mat is not None:
        tex_img = getattr(mat, "baseColorTexture", None)
Vg = np.vstack(parts_v)
Fg = np.vstack(parts_f)
UVg = np.vstack(parts_uv)
VCg = np.vstack(parts_vc)
HAS_TEX = tex_img is not None     # False for a vertex-coloured input (the pipeline proxy)
tex = np.asarray(tex_img.convert("RGB"), np.float32) / 255.0 if HAS_TEX else np.zeros((4, 4, 3), np.float32)
raw = trimesh.Trimesh(Vg, Fg, process=False)
merged = trimesh.Trimesh(Vg.copy(), Fg.copy(), process=True)   # merges coincident vertices (UV seams)
merged.merge_vertices(digits_vertex=5)
pieces = merged.split(only_watertight=False)
REPORT["raw"] = {"bytes": os.path.getsize(RAW), "triangles": int(len(Fg)), "vertices": int(len(Vg)),
                 "loose_parts_as_loaded": int(len(raw.split(only_watertight=False))),
                 "pieces_after_merge": int(len(pieces)), "watertight": bool(merged.is_watertight),
                 "texture_px": list(tex.shape[:2]), "texture_mean_rgb255": [round(x * 255) for x in tex.reshape(-1, 3).mean(0)],
                 "largest_piece_share": round(max(len(p.faces) for p in pieces) / len(merged.faces), 3)}
print("RAW", json.dumps(REPORT["raw"]))

# glTF (Y up, front +Z) -> Blender (Z up, front -Y)
Vb = np.stack([Vg[:, 0], -Vg[:, 2], Vg[:, 1]], 1)

# ---------------------------------------------------------------- 2. orient + fit to the v7 Idle display pose
d7 = np.load(V7, allow_pickle=False)
mesh_names = [str(x) for x in d7["meshes"]]
bones = [str(x) for x in d7["bones"]]
P7 = {n: d7["V_" + n].astype(np.float64) for n in mesh_names}
F7 = {n: d7["F_" + n] for n in mesh_names}
W7 = {n: d7["W_" + n] for n in mesh_names}
all7 = np.vstack(list(P7.values()))
z0_7, z1_7 = all7[:, 2].min(), all7[:, 2].max()
H7 = z1_7 - z0_7
gfront = np.load(os.path.join(GUIDES, "guide-06-front.npz"))
bmask = gfront["mask"].astype(bool)


def front_mask(V, F, size=200, lo=None, hi=None):
    """Rasterise the front (XZ) projection of triangles into a size x size mask over [lo, hi]."""
    from skimage.draw import polygon
    m = np.zeros((size, size), bool)
    xy = (V[:, [0, 2]] - lo) / (hi - lo) * (size - 1)
    for f in F[:: max(1, len(F) // 60000)]:
        p = xy[f]
        rr, cc = polygon(size - 1 - p[:, 1], p[:, 0], m.shape)
        m[rr, cc] = True
    return ndi.binary_closing(m, iterations=1)


def bible_on_grid(lo, hi, size, Vref):
    """Bible front mask scaled to the reference figure height, feet aligned, centred on the reference bbox."""
    ys, xs = np.nonzero(bmask)
    h = ys.max() - ys.min()
    s = (Vref[:, 2].max() - Vref[:, 2].min()) / h
    cx = (xs.min() + xs.max()) / 2
    gx = (np.arange(size) / (size - 1)) * (hi[0] - lo[0]) + lo[0]
    gz = (np.arange(size)[::-1] / (size - 1)) * (hi[1] - lo[1]) + lo[1]
    X, Z = np.meshgrid(gx, gz)
    cxr = (Vref[:, 0].max() + Vref[:, 0].min()) / 2
    px = ((X - cxr) / s + cx).round().astype(int)
    py = (ys.max() - (Z - Vref[:, 2].min()) / s).round().astype(int)
    ok = (px >= 0) & (px < bmask.shape[1]) & (py >= 0) & (py < bmask.shape[0])
    out = np.zeros_like(ok)
    out[ok] = bmask[py[ok], px[ok]]
    return out


def iou(a, b):
    return (a & b).sum() / max(1, (a | b).sum())


best = None
for flip in (1, -1):          # 1 = as generated, -1 = turned 180 degrees about the vertical
    V = Vb * np.array([flip, flip, 1.0])
    s = H7 / (V[:, 2].max() - V[:, 2].min())
    V = (V - [0, 0, V[:, 2].min()]) * s + [0, 0, z0_7]
    V[:, 0] += (all7[:, 0].max() + all7[:, 0].min()) / 2 - (V[:, 0].max() + V[:, 0].min()) / 2
    lo = np.array([min(V[:, 0].min(), all7[:, 0].min()) - 0.05, z0_7 - 0.05])
    hi = lo + max(np.ptp(V[:, 0]), np.ptp(all7[:, 0]), H7) + 0.1
    gm = front_mask(V, Fg, 200, lo, hi)
    bm = bible_on_grid(lo, hi, 200, V)
    # small x slide for the best overlap against the bible
    scores = [(iou(np.roll(gm, k, 1), bm), k) for k in range(-8, 9)]
    sc_, k = max(scores)
    V[:, 0] += k * (hi[0] - lo[0]) / 199
    if best is None or sc_ > best[0]:
        best = (sc_, flip, V, lo, hi)
iou_b, flip, Vfit, lo, hi = best
# depth: put the generated torso where the v7 torso is (median Y of the 45-70 % height band)
band = lambda V: (V[:, 2] > z0_7 + 0.45 * H7) & (V[:, 2] < z0_7 + 0.70 * H7)
Vfit[:, 1] += np.median(P7["Warrior_Body"][band(P7["Warrior_Body"]), 1]) - np.median(Vfit[band(Vfit), 1])
m7 = front_mask(all7, np.vstack([F7[n] + sum(len(P7[m]) for m in mesh_names[:i]) for i, n in enumerate(mesh_names)]),
                200, lo, hi)
gm = front_mask(Vfit, Fg, 200, lo, hi)
REPORT["fit"] = {"flip": flip, "front_iou_vs_bible": round(float(iou_b), 3),
                 "front_iou_vs_v7_idle": round(float(iou(gm, m7)), 3), "height_m": round(float(H7), 3),
                 "depth_y_span_m": round(float(np.ptp(Vfit[:, 1])), 3),
                 "v7_depth_y_span_m": round(float(np.ptp(all7[:, 1])), 3)}
print("FIT", REPORT["fit"])

# ---------------------------------------------------------------- 3. split into Body / Shield / Cleaver
part_names = ["Warrior_Body", "Warrior_Shield", "Warrior_Cleaver"]
wy = 0.35   # the generator guesses depth: weigh front-plane distance more than depth
dists = []
for n in part_names:
    t = cKDTree(P7[n] * [1, wy, 1])
    dists.append(t.query(Vfit * [1, wy, 1])[0])
lab_v = np.argmin(np.stack(dists, 1), 1)
# smooth the labels over the merged mesh connectivity (majority of neighbours, 4 passes)
mv = trimesh.Trimesh(Vfit, Fg, process=False)
mv.merge_vertices(digits_vertex=5)
inv = mv.vertices  # noqa
# map raw vertices -> merged vertices by rounding
key = np.round(Vfit, 5)
_, grp = np.unique(key, axis=0, return_inverse=True)
grp = grp.ravel()
lab_g = np.zeros((grp.max() + 1, 3))
np.add.at(lab_g, (grp, lab_v), 1)
gl = lab_g.argmax(1)
edges = np.vstack([grp[Fg[:, [0, 1]]], grp[Fg[:, [1, 2]]], grp[Fg[:, [2, 0]]]])
for _ in range(4):
    votes = np.zeros((len(gl), 3))
    np.add.at(votes, (edges[:, 0], gl[edges[:, 1]]), 1)
    np.add.at(votes, (edges[:, 1], gl[edges[:, 0]]), 1)
    np.add.at(votes, (np.arange(len(gl)), gl), 2)
    gl = votes.argmax(1)
lab_v = gl[grp]
l3 = lab_v[Fg]
face_lab = np.where((l3[:, 1] == l3[:, 2]) & (l3[:, 0] != l3[:, 1]), l3[:, 1], l3[:, 0])
REPORT["split_faces"] = {n: int((face_lab == i).sum()) for i, n in enumerate(part_names)}
print("SPLIT", REPORT["split_faces"])

# ---------------------------------------------------------------- 4. sample the raw surface colour (ink excluded)
NS = 1_500_000
area = trimesh.Trimesh(Vfit, Fg, process=False).area_faces
fi = rng.choice(len(Fg), NS, p=area / area.sum())
r1, r2 = rng.random(NS), rng.random(NS)
sq = np.sqrt(r1)
bc = np.stack([1 - sq, sq * (1 - r2), sq * r2], 1)
Sp = (Vfit[Fg[fi]] * bc[:, :, None]).sum(1)
Suv = (UVg[Fg[fi]] * bc[:, :, None]).sum(1)
fn = np.cross(Vfit[Fg[:, 1]] - Vfit[Fg[:, 0]], Vfit[Fg[:, 2]] - Vfit[Fg[:, 0]])
fn /= np.linalg.norm(fn, axis=1, keepdims=True) + 1e-12
Sn = fn[fi]
th, tw = tex.shape[:2]
tx = np.clip((Suv[:, 0] % 1.0) * (tw - 1), 0, tw - 1).round().astype(int)
ty = np.clip((1 - Suv[:, 1] % 1.0) * (th - 1), 0, th - 1).round().astype(int)
# drawn ink in the raw texture = dark thin strokes (dark areas that vanish under a 2 px opening)
tlum = tex @ np.array([0.299, 0.587, 0.114])
dark = tlum < 0.16
ink_tex = dark & ~ndi.binary_opening(dark, iterations=2)
Sc = tex[ty, tx] if HAS_TEX else (VCg[Fg[fi]] * bc[:, :, None]).sum(1)
Sink = ink_tex[ty, tx] if HAS_TEX else np.zeros(NS, bool)
REPORT["raw"]["ink_texel_share"] = round(float(ink_tex.mean()), 4)
REPORT["raw"]["ink_surface_share"] = round(float(Sink.mean()), 4)
Spart = face_lab[fi]

# ---------------------------------------------------------------- 5. palette from the bible region maps
pal = []
for view in ("front", "three-quarter", "profile", "back"):
    g = json.load(open(os.path.join(GUIDES, f"guide-06-{view}.json")))
    for r in g["regions"]:
        lab = np.array(r["lab"])
        if all(np.linalg.norm(lab - p[1]) > 6 for p in pal):
            pal.append((r["name"], lab, r["hex"]))
pal_lab = np.array([p[1] for p in pal])
pal_names = [p[0] for p in pal]
# global colour transfer (Lab mean/std) from the generated texture to the bible crop
bl = rgb2lab(gfront["rgb"][bmask & ~gfront["outline"].astype(bool)] / 255.0)
gl_lab = rgb2lab(Sc[~Sink])
mu_g, sd_g, mu_b, sd_b = gl_lab.mean(0), gl_lab.std(0), bl.mean(0), bl.std(0)
REPORT["colour"] = {"generated_mean_lab": mu_g.round(1).tolist(), "bible_mean_lab": mu_b.round(1).tolist()}


def transfer(lab):
    return (lab - mu_g) / (sd_g + 1e-6) * sd_b + mu_b


def nearest_pal(lab):
    d = ((lab[:, None, :] - pal_lab[None]) * np.array([0.5, 1, 1])) ** 2
    return d.sum(-1).argmin(1)


# colour fidelity before/after, measured on the raw samples: dE76 to the nearest palette entry
lab_raw = gl_lab[rng.choice(len(gl_lab), 50000)]
dE_raw = np.linalg.norm(lab_raw - pal_lab[nearest_pal(lab_raw)], axis=1)
lab_tr = transfer(lab_raw)
dE_tr = np.linalg.norm(lab_tr - pal_lab[nearest_pal(lab_tr)], axis=1)
REPORT["colour"]["dE_to_palette_raw_median"] = round(float(np.median(dE_raw)), 1)
REPORT["colour"]["dE_to_palette_after_transfer_median"] = round(float(np.median(dE_tr)), 1)

# ---------------------------------------------------------------- 6. voxel remesh helpers
VOX = H7 / 300.0     # finer than v1 (230): keeps edges a little crisper


def occupancy(points, pad=6):
    lo_ = points.min(0) - pad * VOX
    idx = np.floor((points - lo_) / VOX).astype(int)
    shape = idx.max(0) + pad + 1
    occ = np.zeros(shape, bool)
    occ[idx[:, 0], idx[:, 1], idx[:, 2]] = True
    occ = ndi.binary_dilation(occ, iterations=2)
    occ = ndi.binary_fill_holes(occ)
    occ = ndi.binary_erosion(occ, iterations=2, border_value=0)
    return occ, lo_


def remesh(points, target_tris, smooth_sigma=0.6):
    from skimage.measure import marching_cubes
    import fast_simplification
    occ, lo_ = occupancy(points)
    f = ndi.gaussian_filter(occ.astype(np.float32), smooth_sigma)
    v, fcs, _, _ = marching_cubes(f, 0.5)
    v = v * VOX + lo_
    m = trimesh.Trimesh(v, fcs, process=True)
    m.fix_normals()
    trimesh.smoothing.filter_taubin(m, lamb=0.5, nu=-0.53, iterations=3)
    pts, fcs2 = fast_simplification.simplify(m.vertices, m.faces, target_reduction=1 - target_tris / len(m.faces))
    m2 = trimesh.Trimesh(pts, fcs2, process=True)
    m2.fix_normals()
    # keep the largest pieces only (drop crumbs under 0.5 % of faces)
    comps = m2.split(only_watertight=False)
    comps = [c for c in comps if len(c.faces) > 0.005 * len(m2.faces)]
    m2 = trimesh.util.concatenate(comps)
    return m2, occ, lo_


# ---------------------------------------------------------------- 7. textures for one variant
def ao_per_vertex(V, N, occ, lo_, ndir=32, steps=26):
    dirs = rng.normal(size=(ndir, 3))
    dirs /= np.linalg.norm(dirs, axis=1, keepdims=True)
    vis = np.zeros(len(V))
    for d in dirs:
        cosn = N @ d
        hemi = np.where(cosn[:, None] >= 0, d, d - 2 * cosn[:, None] * N)  # reflect into the hemisphere
        free = np.ones(len(V), bool)
        for s_ in range(2, steps):
            p = V + N * 1.5 * VOX + hemi * s_ * VOX
            ii = np.floor((p - lo_) / VOX).astype(int)
            ok = np.all((ii >= 0) & (ii < occ.shape), 1)
            hit = np.zeros(len(V), bool)
            hit[ok] = occ[ii[ok, 0], ii[ok, 1], ii[ok, 2]]
            free &= ~hit
        vis += free * np.abs(cosn)
    return vis / (np.abs(dirs @ np.array([0, 0, 1.0])).sum() * 0 + ndir * 0.5)


def bake_variant(name, meshes, vpart, occ_all, lo_all):
    """meshes: list of trimesh (posed, Blender axes); labels: part index per mesh."""
    import xatlas
    V = np.vstack([m.vertices for m in meshes])
    F = np.vstack([m.faces + sum(len(x.vertices) for x in meshes[:i]) for i, m in enumerate(meshes)])
    full = trimesh.Trimesh(V, F, process=False)
    VN = full.vertex_normals
    atlas = xatlas.Atlas()
    atlas.add_mesh(V.astype(np.float32), F.astype(np.uint32))
    po = xatlas.PackOptions()
    po.resolution, po.padding, po.bilinear = TEX, 4, True
    atlas.generate(pack_options=po)
    vmap, Fn, UV = atlas[0]
    Vn, Nn, Pn = V[vmap], VN[vmap], vpart[vmap]
    # per-vertex AO (voxel rays through the full figure) and light gradient
    ao = ao_per_vertex(Vn, Nn, occ_all, lo_all)
    ao = np.clip(ao / np.percentile(ao, 95), 0, 1)
    # rasterise the atlas: every texel -> 3D point, normal, ao, part
    from skimage.draw import polygon
    P = np.zeros((TEX, TEX, 3)); N_ = np.zeros((TEX, TEX, 3)); A = np.zeros((TEX, TEX)); PT = np.full((TEX, TEX), -1)
    cov = np.zeros((TEX, TEX), bool)
    uvp = UV * [TEX, TEX]
    for f in Fn:
        a, b, c = uvp[f]
        rr, cc = polygon(TEX - np.array([a[1], b[1], c[1]]), np.array([a[0], b[0], c[0]]), (TEX, TEX))
        if len(rr) == 0:
            continue
        x, y = cc + 0.5, TEX - rr - 0.5
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        l1 = ((b[1] - c[1]) * (x - c[0]) + (c[0] - b[0]) * (y - c[1])) / den
        l2 = ((c[1] - a[1]) * (x - c[0]) + (a[0] - c[0]) * (y - c[1])) / den
        w = np.clip(np.stack([l1, l2, 1 - l1 - l2], 1), 0, 1)
        w /= w.sum(1, keepdims=True)
        P[rr, cc] = w @ Vn[f]
        N_[rr, cc] = w @ Nn[f]
        A[rr, cc] = w @ ao[f]
        PT[rr, cc] = Pn[f[0]]
        cov[rr, cc] = True
    q = np.nonzero(cov)
    Pq, Nq = P[q], N_[q]
    Nq /= np.linalg.norm(Nq, axis=1, keepdims=True) + 1e-9
    # colour: 12 nearest non-ink raw samples, weighted by distance and normal agreement
    keep = ~Sink
    tree = cKDTree(Sp[keep])
    dd, ii = tree.query(Pq, k=12)
    sn = Sn[keep][ii]
    agree = np.clip((sn * Nq[:, None, :]).sum(-1), 0, 1) ** 2 + 1e-3
    wgt = agree / (dd + VOX * 0.5)
    col = (Sc[keep][ii] * wgt[..., None]).sum(1) / wgt.sum(1, keepdims=True)
    raw_img = np.zeros((TEX, TEX, 3))
    raw_img[q] = col
    # repaint: transfer to the bible's colour statistics, snap toward the nearest region colour, keep some variation
    lab = transfer(rgb2lab(col))
    pi = nearest_pal(lab)
    target = pal_lab[pi]
    out_lab = target.copy()
    out_lab[:, 0] = target[:, 0] + 0.35 * np.clip(lab[:, 0] - target[:, 0], -12, 12)   # painted value variation
    out_lab[:, 1:] = target[:, 1:] + 0.15 * (lab[:, 1:] - target[:, 1:])
    rep = lab2rgb(out_lab)
    zf = (Pq[:, 2] - z0_7) / H7
    grad = 0.90 + 0.14 * zf                       # light gradient: feet a touch darker, head a touch lighter
    aoq = 0.62 + 0.38 * A[q]
    names = np.array(pal_names)[pi]
    glow = np.char.find(names, "glow") >= 0
    team = np.char.find(names, "team") >= 0
    lit = rep * (np.where(glow, 1.0, aoq) * np.where(glow, 1.0, grad))[:, None]
    img = np.zeros((TEX, TEX, 3)); img[q] = np.clip(lit, 0, 1)
    mask = np.zeros((TEX, TEX, 3))
    # R team mask (1.0 team cloth, 0.75 team glow), G emission (glow on the shield and cleaver only), B baked AO
    tm = np.where(team, 1.0, np.where(glow, 0.75, 0.0))
    em = glow & (PT[q] >= 1)
    mask[q] = np.stack([tm, em.astype(float), A[q]], 1)
    # dilate islands so bilinear sampling never pulls black from outside a chart
    idx = ndi.distance_transform_edt(~cov, return_distances=False, return_indices=True)
    for arr in (img, mask, raw_img):
        arr[:] = arr[idx[0], idx[1]]
    Image.fromarray((img * 255).round().astype(np.uint8)).save(os.path.join(OUT, f"{name}_albedo.png"))
    Image.fromarray((mask * 255).round().astype(np.uint8)).save(os.path.join(OUT, f"{name}_mask.png"))
    Image.fromarray((raw_img * 255).round().astype(np.uint8)).save(os.path.join(OUT, f"{name}_rebake_raw.png"))
    share = {n: round(float((pi == i).mean()), 3) for i, n in enumerate(pal_names) if (pi == i).mean() > 0.005}
    return Vn, Fn, UV, Pn, {"palette_share_texels": share, "team_texel_share": round(float(team.mean()), 3),
                            "glow_texel_share": round(float(glow.mean()), 3),
                            "atlas_utilisation": round(float(cov.mean()), 3)}


# ---------------------------------------------------------------- 8. weights + un-posing
rest = d7["rest"]
posem = d7["pose"]
S = np.array([posem[i] @ np.linalg.inv(rest[i]) for i in range(len(bones))])  # rest -> posed, armature space
AW = d7["arm_world"]
AWi = np.linalg.inv(AW)


def weights_from(Vn, sources, Fn=None, smooth_iters=6):
    """v2: dominant bone of the NEAREST v7 vertex (no Euclidean blending across gaps: a hand resting on a hip must
    not pull the hip), then blended only along the new mesh's own surface (uniform Laplacian over its edges), limit 4,
    normalise. A vertex whose blended skinning matrix collapses (det < 0.6) falls back to its dominant bone, so
    un-posing cannot fling it out."""
    Psrc = np.vstack([P7[n] for n in sources])
    Wsrc = np.vstack([W7[n] for n in sources])
    t = cKDTree(Psrc)
    _, ii = t.query(Vn, k=1)
    dom = Wsrc[ii].argmax(1)
    W = np.zeros((len(Vn), Wsrc.shape[1]))
    W[np.arange(len(Vn)), dom] = 1.0
    if Fn is not None and smooth_iters:
        E = np.vstack([Fn[:, [0, 1]], Fn[:, [1, 2]], Fn[:, [2, 0]]])
        E = np.vstack([E, E[:, ::-1]])
        deg = np.bincount(E[:, 0], minlength=len(Vn)).astype(float)
        for _ in range(smooth_iters):
            acc = np.zeros_like(W)
            np.add.at(acc, E[:, 0], W[E[:, 1]])
            W = 0.5 * W + 0.5 * acc / np.maximum(deg, 1)[:, None]
    order = np.argsort(-W, 1)
    W2 = np.zeros_like(W)
    rows = np.arange(len(W))[:, None]
    W2[rows, order[:, :4]] = W[rows, order[:, :4]]
    W2 /= W2.sum(1, keepdims=True) + 1e-12
    M = np.einsum("vb,bij->vij", W2, S)[:, :3, :3]
    det = np.linalg.det(M)
    bad = det < 0.6
    W2[bad] = 0
    W2[np.nonzero(bad)[0], dom[bad]] = 1.0
    REPORT.setdefault("weights", []).append({"vertices": int(len(Vn)), "collapsed_fallback": int(bad.sum()),
                                             "blended_vertices": int(((W2 > 0).sum(1) > 1).sum())})
    return W2.astype(np.float32)


def unpose(Vn, W):
    Va = (np.c_[Vn, np.ones(len(Vn))] @ AWi.T)
    M = np.einsum("vb,bij->vij", W, S)
    Mi = np.linalg.inv(M)
    return np.einsum("vij,vj->vi", Mi, Va)[:, :3]


def save_variant(name, Vn, Fn, UV, Pn, W, info):
    Vr = unpose(Vn, W)
    # check: re-pose the rest mesh and compare with the posed input
    Vp = np.einsum("vij,vj->vi", np.einsum("vb,bij->vij", W, S), np.c_[Vr, np.ones(len(Vr))])[:, :3]
    info["repose_error_max_m"] = float(np.abs(Vp - (np.c_[Vn, np.ones(len(Vn))] @ AWi.T)[:, :3]).max())
    info["triangles"] = int(len(Fn))
    info["triangles_by_part"] = {part_names[i]: int((Pn[Fn[:, 0]] == i).sum()) for i in range(3)}
    info["influences_max"] = int((W > 0).sum(1).max())
    np.savez_compressed(os.path.join(OUT, f"{name}.npz"), V_rest=Vr.astype(np.float32), V_posed=Vn.astype(np.float32),
                        F=Fn.astype(np.int32), UV=UV.astype(np.float32), part=Pn.astype(np.int8),
                        W=W.astype(np.float32), bones=np.array(bones), part_names=np.array(part_names))
    REPORT[name] = info
    print("VARIANT", name, json.dumps({k: v for k, v in info.items() if k != "palette_share_texels"}))


# fused: one remesh of the whole generated figure, proximity weights from every v7 object
m_f, occ_f, lo_f = remesh(Sp, 24000)
vp_f = Spart[cKDTree(Sp).query(m_f.vertices)[1]]     # nearest raw part: for the emission mask and reporting
Vn, Fn, UV, Pn, info = bake_variant("fused", [m_f], vp_f, occ_f, lo_f)
W = weights_from(Vn, part_names, Fn)
info["watertight"] = bool(m_f.is_watertight)
info["pieces"] = int(len(m_f.split(only_watertight=False)))
save_variant("fused", Vn, Fn, UV, Pn, W, info)

# parts: body / shield / cleaver remeshed separately; held items rigid on the hand slots
targets = [17500, 4500, 2500]
ms = []
for i in range(3):
    sel = Spart == i
    m, _, _ = remesh(Sp[sel], targets[i])
    ms.append(m)
Vn, Fn, UV, Pn, info = bake_variant("parts", ms, np.concatenate([np.full(len(m.vertices), i) for i, m in enumerate(ms)]), occ_f, lo_f)
W = np.zeros((len(Vn), len(bones)), np.float32)
body = Pn == 0
# body-only sub-mesh for surface smoothing
bidx = np.nonzero(body)[0]
remap = -np.ones(len(Vn), int)
remap[bidx] = np.arange(len(bidx))
Fb = remap[Fn[np.all(body[Fn], 1)]]
W[body] = weights_from(Vn[body], ["Warrior_Body"], Fb)
W[Pn == 1, bones.index("handslot.l")] = 1.0
W[Pn == 2, bones.index("handslot.r")] = 1.0
info["watertight_by_part"] = [bool(m.is_watertight) for m in ms]
save_variant("parts", Vn, Fn, UV, Pn, W, info)
REPORT["palette"] = [(n, h) for n, _, h in pal]
json.dump(REPORT, open(os.path.join(OUT, "process_report.json"), "w"), indent=1, default=float)
print("DONE")
