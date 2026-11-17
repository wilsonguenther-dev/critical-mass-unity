"""Robust skin weight transfer, reusable for any character on the shared 41-bone skeleton.

Method: Abdrashitov, Raichstat, Monsen, Hill, "Robust Skin Weights Transfer via Weight Inpainting", SIGGRAPH Asia 2023,
re-implemented from the paper and its MIT reference code (github.com/rin-23/RobustSkinWeightsTransferCode, utils.py):
  1. match:   every target vertex takes the source weights interpolated at its closest source surface point, ACCEPTED
              only if that point is within dist_frac x the target's bounding-box diagonal and the interpolated source
              normal is within angle_deg of the target normal;
  2. inpaint: the rejected vertices are solved by minimising the biharmonic-type energy tr(W^T Q W), Q = L + L M^-1 L
              (L = robust Laplacian of the target, Sharp & Crane 2020, positive semidefinite; M = lumped mass), with the
              accepted vertices fixed (the paper's Q = -L + L M^-1 L with libigl's negative cotangent matrix);
  3. smooth:  the rejected vertices and every vertex reachable over edges within the distance threshold of one get
              iters x (1 - alpha) w + alpha mean(neighbours);
  4. at most 4 influences, normalised.
Plus the template fit the paper assumes (both bodies overlapping): fit_template() moves every source bone onto its
target counterpart (head on head, direction on direction, length on length; an anatomical frame per bone so the two
skeletons' bone rolls never matter) and scales across the bone by the ratio of the two bodies' own extents, per axis and
per side (the belly is deeper than the back), then deforms the source mesh by linear blend skinning with its OWN weights.

Pure numpy / scipy / robust_laplacian + mathutils (no bpy object access), so the caller decides what source, target
and thresholds are. Dependencies outside Blender: scipy, robust-laplacian (see README: installed with Blender's own
Python into a local folder, never system-wide).
"""
import numpy as np
from collections import deque
from mathutils import Vector
from mathutils.bvhtree import BVHTree


# ------------------------------------------------------------------------------------------------ geometry helpers
def vertex_normals(V, F):
    fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]])            # area weighted
    N = np.zeros_like(V)
    for k in range(3): np.add.at(N, F[:, k], fn)
    return N / np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)


def edges_of(F):
    E = np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]]); E.sort(1)
    return np.unique(E, axis=0)


def components(n, E):
    lab = np.arange(n)
    while True:
        a, b = lab[E[:, 0]], lab[E[:, 1]]; m = np.minimum(a, b); new = lab.copy()
        np.minimum.at(new, a, m); np.minimum.at(new, b, m)
        while True:
            n2 = new[new]
            if (n2 == new).all(): break
            new = n2
        if (new == lab).all(): return lab
        lab = new


def orient_outward(V, F):
    """Per connected piece: faces reversed when the piece's area-weighted normals point inward on average (an
    inside-out import). Returns (F, pieces flipped, pieces)."""
    F = F.copy(); lab = components(len(V), edges_of(F)); fl = lab[F[:, 0]]; flipped = 0
    fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]]); fc = V[F].mean(1)
    for u in np.unique(fl):
        m = fl == u; c = fc[m].mean(0)
        if (fn[m] * (fc[m] - c)).sum() < 0: F[m] = F[m][:, ::-1]; flipped += 1
    return F, flipped, int(len(np.unique(fl)))


def submesh(V, F, keep):
    """Vertices `keep` (bool) and the faces entirely inside them, re-indexed. Returns (V', F', index map)."""
    idx = np.nonzero(keep)[0]; remap = -np.ones(len(V), int); remap[idx] = np.arange(len(idx))
    Fk = F[keep[F].all(1)]
    return V[idx], remap[Fk], idx


def closest_on_mesh(Vs, Fs, P):
    """Closest point on the triangle mesh (Vs, Fs) for each P: (face index, barycentric (n,3), distance)."""
    bvh = BVHTree.FromPolygons([tuple(map(float, v)) for v in Vs], [tuple(map(int, f)) for f in Fs], all_triangles=True)
    fi = np.zeros(len(P), int); loc = np.zeros((len(P), 3)); d = np.full(len(P), np.inf)
    for i, p in enumerate(P):
        hit = bvh.find_nearest(Vector(tuple(map(float, p))))
        if hit[2] is None: continue
        loc[i] = hit[0]; fi[i] = hit[2]; d[i] = hit[3]
    A, B, Cc = Vs[Fs[fi, 0]], Vs[Fs[fi, 1]], Vs[Fs[fi, 2]]
    v0, v1, v2 = B - A, Cc - A, loc - A
    d00 = (v0 * v0).sum(1); d01 = (v0 * v1).sum(1); d11 = (v1 * v1).sum(1); d20 = (v2 * v0).sum(1); d21 = (v2 * v1).sum(1)
    den = np.maximum(d00 * d11 - d01 * d01, 1e-20)
    b1 = (d11 * d20 - d01 * d21) / den; b2 = (d00 * d21 - d01 * d20) / den
    bc = np.clip(np.stack([1 - b1 - b2, b1, b2], 1), 0, 1); bc /= np.maximum(bc.sum(1, keepdims=True), 1e-12)
    return fi, bc, d


# ------------------------------------------------------------------------------------------------ 1. match
def match(Vs, Fs, Ws, Vt, Ft, dist_thr, angle_deg, allow_flip=False, Ns=None, Nt=None):
    """Closest-point weights + the paper's acceptance test. Returns (W (nt, bones), matched bool, dist, angle_deg)."""
    Ns = vertex_normals(Vs, Fs) if Ns is None else Ns
    Nt = vertex_normals(Vt, Ft) if Nt is None else Nt
    fi, bc, d = closest_on_mesh(Vs, Fs, Vt)
    tri = Fs[fi]
    W = np.einsum("ij,ijk->ik", bc, Ws[tri])
    Ni = np.einsum("ij,ijk->ik", bc, Ns[tri]); Ni /= np.maximum(np.linalg.norm(Ni, axis=1, keepdims=True), 1e-12)
    c = (Ni * Nt).sum(1)
    if allow_flip: c = np.abs(c)
    ang = np.degrees(np.arccos(np.clip(c, -1, 1)))
    ok = (d <= dist_thr) & (ang <= angle_deg)
    return W, ok, d, ang


# ------------------------------------------------------------------------------------------------ 2. inpaint
def inpaint(V, F, W, known):
    """Fill W at ~known by minimising tr(W^T (L + L M^-1 L) W) with W[known] fixed. Every connected piece needs at
    least one known vertex; pieces without one take their closest-point weights as known (counted in the result)."""
    import scipy.sparse as sp
    import scipy.sparse.linalg as sla
    import robust_laplacian
    n = len(V); known = known.copy()
    lab = components(n, edges_of(F)) if len(F) else np.arange(n)
    orphan = 0
    for u in np.unique(lab):
        m = lab == u
        if not known[m].any(): known[m] = True; orphan += int(m.sum())
    isolated = np.ones(n, bool); isolated[F.ravel()] = False
    known |= isolated
    unk = ~known
    if not unk.any(): return W.copy(), dict(orphan_piece_verts=orphan, solved=0)
    L, M = robust_laplacian.mesh_laplacian(np.ascontiguousarray(V, float), np.ascontiguousarray(F, np.int64))
    md = M.diagonal(); Minv = sp.diags(1.0 / np.maximum(md, 1e-12 * max(md.max(), 1e-30)))
    Q = (L + L @ Minv @ L).tocsr()
    ui, ki = np.nonzero(unk)[0], np.nonzero(known)[0]
    Quu = Q[ui][:, ui].tocsc(); Quk = Q[ui][:, ki]
    lu = sla.splu(Quu)
    Wn = W.copy(); Wn[ui] = lu.solve(-(Quk @ W[ki]))
    neg = float(Wn[ui].min())
    Wn = np.clip(Wn, 0, None); s = Wn.sum(1, keepdims=True)
    Wn = np.where(s > 1e-9, Wn / np.maximum(s, 1e-12), W)
    return Wn, dict(orphan_piece_verts=orphan, solved=int(len(ui)), min_raw_weight=round(neg, 4))


# ------------------------------------------------------------------------------------------------ 3. smooth, 4. limit
def smooth(V, F, W, matched, dist_thr, iters=10, alpha=0.2, frozen=None):
    """The reference smoothing: the unmatched vertices plus every vertex reachable over edges whose distance to an
    unmatched seed stays under dist_thr; Jacobi iterations of (1-alpha) w + alpha * mean(neighbours)."""
    n = len(V); E = edges_of(F); adj = [[] for _ in range(n)]
    for a, b in E: adj[a].append(b); adj[b].append(a)
    sel = ~matched.copy()
    for s in np.nonzero(~matched)[0]:
        q = deque([s]); seen = {s}
        while q:
            u = q.popleft()
            for w in adj[u]:
                if w not in seen and np.linalg.norm(V[w] - V[s]) < dist_thr:
                    seen.add(w); sel[w] = True; q.append(w)
    if frozen is not None: sel &= ~frozen
    deg = np.zeros(n); np.add.at(deg, E[:, 0], 1); np.add.at(deg, E[:, 1], 1)
    W = W.copy()
    for _ in range(iters):
        acc = np.zeros_like(W); np.add.at(acc, E[:, 0], W[E[:, 1]]); np.add.at(acc, E[:, 1], W[E[:, 0]])
        avg = acc / np.maximum(deg, 1)[:, None]
        W[sel & (deg > 0)] = ((1 - alpha) * W + alpha * avg)[sel & (deg > 0)]
    return W, sel


def smooth_all(F, W, iters, alpha=0.2, frozen=None):
    """Light smoothing over a whole mesh (e.g. a decimated LOD): iters x (1-alpha) w + alpha mean(neighbours)."""
    E = edges_of(F); n = len(W); deg = np.zeros(n); np.add.at(deg, E[:, 0], 1); np.add.at(deg, E[:, 1], 1)
    sel = deg > 0
    if frozen is not None: sel &= ~frozen
    W = W.copy()
    for _ in range(iters):
        acc = np.zeros_like(W); np.add.at(acc, E[:, 0], W[E[:, 1]]); np.add.at(acc, E[:, 1], W[E[:, 0]])
        W[sel] = ((1 - alpha) * W + alpha * acc / np.maximum(deg, 1)[:, None])[sel]
    return W


def limit(W, k=4, eps=1e-3):
    W = W.copy(); o = np.argsort(-W, 1)[:, k:]; np.put_along_axis(W, o, 0, 1)
    W[W < eps] = 0
    return W / np.maximum(W.sum(1, keepdims=True), 1e-12)


def smooth_normals(N, F, iters):
    """Neighbour-averaged vertex normals (coarse, faceted low-poly bodies): iters x mean over the 1-ring."""
    if iters <= 0: return N
    E = edges_of(F)
    for _ in range(iters):
        acc = N.copy(); np.add.at(acc, E[:, 0], N[E[:, 1]]); np.add.at(acc, E[:, 1], N[E[:, 0]])
        N = acc / np.maximum(np.linalg.norm(acc, axis=1, keepdims=True), 1e-12)
    return N


def transfer(Vs, Fs, Ws, Vt, Ft, cfg, frozen=None, mask=None):
    """match -> inpaint -> smooth -> (mask) -> limit on one target mesh. cfg: dist_frac, angle_deg, allow_flip,
    normal_smooth_iters (0 = the paper), smooth_iters, smooth_alpha, max_influences. mask (nt, bones) bool, optional:
    bones a vertex may carry at all (region limits); applied to the matched weights before the inpaint (a matched
    vertex whose weight lies entirely outside its mask counts as unmatched) and to the result. Returns (W, stats, ok)."""
    diag = float(np.linalg.norm(Vt.max(0) - Vt.min(0)))
    dthr = cfg.get("dist_frac", 0.05) * diag
    ns = cfg.get("normal_smooth_iters", 0)
    Ns = smooth_normals(vertex_normals(Vs, Fs), Fs, ns); Nt = smooth_normals(vertex_normals(Vt, Ft), Ft, ns)
    W0, ok, d, ang = match(Vs, Fs, Ws, Vt, Ft, dthr, cfg.get("angle_deg", 30.0), cfg.get("allow_flip", False), Ns, Nt)
    share_at = {str(a): round(float(((d <= dthr) & (ang <= a)).mean()), 3) for a in (30, 45, 60, 90)}
    masked_out = 0
    if mask is not None:
        Wm = W0 * mask; sm = Wm.sum(1); bad = ok & (sm < 0.5)
        masked_out = int(bad.sum()); ok = ok & ~bad
        W0 = np.where(sm[:, None] > 1e-9, Wm / np.maximum(sm, 1e-12)[:, None], W0)
    Wi, ist = inpaint(Vt, Ft, W0, ok)
    Wsm, ssel = smooth(Vt, Ft, Wi, ok, dthr, cfg.get("smooth_iters", 10), cfg.get("smooth_alpha", 0.2), frozen)
    if mask is not None and cfg.get("mask_result", False):        # off: a hard mask on the result leaves weight cliffs
        Wm = Wsm * mask; sm = Wm.sum(1)
        Wsm = np.where(sm[:, None] > 1e-9, Wm / np.maximum(sm, 1e-12)[:, None], Wsm)
    Wf = limit(Wsm, cfg.get("max_influences", 4))
    st = dict(verts=int(len(Vt)), bbox_diag_m=round(diag, 4), dist_thr_m=round(dthr, 4), angle_deg=cfg.get("angle_deg", 30.0),
              matched=int(ok.sum()), matched_share=round(float(ok.mean()), 4),
              rejected_by_distance=int((d > dthr).sum()), rejected_by_angle_only=int(((d <= dthr) & ~ok).sum()),
              dist_m=dict(median=round(float(np.median(d)), 4), p90=round(float(np.percentile(d, 90)), 4), max=round(float(d.max()), 4)),
              inpaint=ist, smoothed_verts=int(ssel.sum()), matched_share_at_angle=share_at, matched_rejected_by_mask=masked_out,
              normal_smooth_iters=ns, masked=mask is not None)
    return Wf, st, ok


# ------------------------------------------------------------------------------------------------ template fit
def anatomical_frame(head, tail, front=(0.0, -1.0, 0.0), up=(0.0, 0.0, 1.0)):
    """Columns x, y, z; y along the bone, x = y x front (or y x up when the bone points to the front: feet, toes)."""
    y = np.asarray(tail, float) - np.asarray(head, float); y /= max(np.linalg.norm(y), 1e-12)
    ref = np.asarray(front, float) if abs(y @ np.asarray(front, float)) < 0.7 else np.asarray(up, float)
    x = np.cross(y, ref); x /= max(np.linalg.norm(x), 1e-12); z = np.cross(x, y)
    return np.stack([x, y, z], 1)


def min_rotation(a, b):
    """3x3 rotation taking direction a to direction b by the shortest arc."""
    a = np.asarray(a, float) / max(np.linalg.norm(a), 1e-12); b = np.asarray(b, float) / max(np.linalg.norm(b), 1e-12)
    v = np.cross(a, b); c = float(a @ b); s2 = float(v @ v)
    if s2 < 1e-16:
        if c > 0: return np.eye(3)
        p = np.cross(a, [1.0, 0, 0]) if abs(a[0]) < 0.9 else np.cross(a, [0, 1.0, 0]); p /= np.linalg.norm(p)
        return 2 * np.outer(p, p) - np.eye(3)
    K = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + K + K @ K * ((1 - c) / s2)


def fit_template(Vs, Ws, Js, Vt, own_t, Jt, bones, terminal, clamp=(0.5, 3.0), min_verts=12, pct=95.0):
    """Deform the source (template) mesh Vs (weights Ws, columns = bones; joints Js[b] = (head, tail)) so that its
    skeleton coincides with the target joints Jt, scaled across each bone to the target body's extent.
    own_t: (nt,) index of the bone each target vertex belongs to (or -1), used only to measure the target's extents.
    terminal: bones whose length is not a joint distance (head, hands, toes): their along-bone scale is measured too.
    Returns (deformed Vs, per-bone scale report)."""
    own_s = Ws.argmax(1)
    out = np.zeros_like(Vs); rep = {}
    for bi, b in enumerate(bones):
        hs, ts = map(np.asarray, Js[b]); ht, tt = map(np.asarray, Jt[b])
        # one frame per bone, chosen on the TARGET bone; the source frame is that frame carried back by the minimal
        # rotation between the two bone directions (no roll convention, no reference-axis switch between skeletons)
        Rt = anatomical_frame(ht, tt); Rs = min_rotation(tt - ht, ts - hs) @ Rt
        ls, lt = np.linalg.norm(ts - hs), np.linalg.norm(tt - ht)
        qs = (Vs[own_s == bi] - hs) @ Rs; qt = (Vt[own_t == bi] - ht) @ Rt
        S = np.ones((3, 2))                   # axis x, y, z; column 0 = positive side, 1 = negative side
        S[1, 0] = lt / max(ls, 1e-9)          # along the bone: joint on joint; behind the bone's head (y < 0) the
        S[1, 1] = 1.0                         # parent's region, not scaled by this bone's length
        for ax in range(3):
            if ax == 0:                       # across, sideways: one scale for both sides (bodies are near symmetric)
                a_, b_ = np.abs(qs[:, 0]), np.abs(qt[:, 0])
                if len(a_) >= min_verts and len(b_) >= min_verts:
                    S[0, :] = float(np.clip(np.percentile(b_, pct) / max(np.percentile(a_, pct), 1e-6), *clamp))
                continue
            for si, sg in ((0, 1.0), (1, -1.0)):    # along (terminal bones, beyond the head) and front/back: per side
                if ax == 1 and not (b in terminal and si == 0): continue
                a_, b_ = qs[sg * qs[:, ax] > 0, ax] * sg, qt[sg * qt[:, ax] > 0, ax] * sg
                if len(a_) >= min_verts and len(b_) >= min_verts:
                    S[ax, si] = float(np.clip(np.percentile(b_, pct) / max(np.percentile(a_, pct), 1e-6), *clamp))
        rep[b] = dict(src_verts=int(len(qs)), tgt_verts=int(len(qt)), len_src=round(float(ls), 3), len_tgt=round(float(lt), 3),
                      scale_pos_neg_xyz=np.round(S, 3).tolist())
        q = (Vs - hs) @ Rs
        qq = np.where(q >= 0, q * S[:, 0], q * S[:, 1])
        out += Ws[:, bi:bi + 1] * (ht + qq @ Rt.T)
        rep[b]["_S"] = S
    for b in rep: rep[b].pop("_S")
    return out, rep
