"""Offline rig / weight / deformation-metric library (numpy + scipy, no Blender). Data comes from dump_rig.py."""
import json
import numpy as np
import scipy.sparse as sp
import scipy.sparse.linalg as spl
from scipy.sparse.csgraph import connected_components

DUMP = "/Users/wilsonguenther/cm-rig-scratch/dump"
TAGS = ("Hero", "LOD0", "LOD1")


def load(cls):
    z = np.load(f"{DUMP}/{cls}.npz", allow_pickle=False)
    d = {k: z[k] for k in z.files}
    d["bones"] = [str(b) for b in d["bones"]]
    d["clip_names"] = [str(c) for c in d["clip_names"]]
    d["cls"] = cls
    return d


def top4(W):
    idx = np.argsort(-W, 1)[:, :4]
    wt = np.take_along_axis(W, idx, 1)
    wt = wt / np.maximum(wt.sum(1, keepdims=True), 1e-12)
    return idx, wt


def blend(idx, wt, S):
    """Per-vertex blended 4x4 matrices from a per-bone stack S (B,4,4)."""
    return np.einsum("nk,nkij->nij", wt, S[idx])


def apply(M, V):
    return np.einsum("nij,nj->ni", M[:, :3, :3], V) + M[:, :3, 3]


def unskin(A, idx, wt, S_P):
    """Rest positions whose blended A-pose transform lands on A."""
    M = blend(idx, wt, S_P)
    Mi = np.linalg.inv(M)
    return apply(Mi, A)


def tri_normals(P, T):
    a, b, c = P[T[:, 0]], P[T[:, 1]], P[T[:, 2]]
    n = np.cross(b - a, c - a)
    ar = np.linalg.norm(n, axis=1)
    return n / np.maximum(ar[:, None], 1e-15), 0.5 * ar


def procrustes_resid(X, Y):
    """Max residual of the best rigid map X -> Y."""
    cx, cy = X.mean(0), Y.mean(0)
    H = (X - cx).T @ (Y - cy)
    U, _, Vt = np.linalg.svd(H)
    D = np.eye(3)
    D[2, 2] = np.sign(np.linalg.det(Vt.T @ U.T))
    R = Vt.T @ D @ U.T
    return float(np.linalg.norm((X - cx) @ R.T + cy - Y, axis=1).max())


class Mesh:
    def __init__(self, V_bind, T, E, idx, wt, S_bind):
        """V_bind: the reference shape (generated, A-pose for fighters); idx/wt: weights; S_bind: skinning of the bind
        pose relative to the rig rest (S_P for fighters, identity for the Kaiju)."""
        self.A, self.T, self.E, self.idx, self.wt = V_bind, T, E, idx, wt
        self.Mb_inv = np.linalg.inv(blend(idx, wt, S_bind))
        self.R = apply(self.Mb_inv, V_bind)          # rig-rest positions
        self.l0 = np.linalg.norm(V_bind[E[:, 0]] - V_bind[E[:, 1]], axis=1)
        self.ok = self.l0 > 0.25 * np.median(self.l0)
        self.n0, self.a0 = tri_normals(V_bind, T)
        self.okT = self.a0 > 0.05 * np.median(self.a0)
        self.dom = idx[:, 0]
        # dominant bone per triangle (majority of the three vertices' dominant bones; first vertex on ties)
        dv = self.dom[T]
        self.tdom = np.where(dv[:, 1] == dv[:, 2], dv[:, 1], dv[:, 0])

    def pose(self, S):
        return apply(blend(self.idx, self.wt, S), self.R)

    def frame_metrics(self, S, S_bind):
        P = self.pose(S)
        l1 = np.linalg.norm(P[self.E[:, 0]] - P[self.E[:, 1]], axis=1)
        r = l1[self.ok] / self.l0[self.ok]
        r = np.maximum(r, 1.0 / np.maximum(r, 1e-6))
        n1, a1 = tri_normals(P, self.T)
        # the triangle's own frame-relative rotation (dominant bone, bind -> frame)
        Rrel = np.einsum("bij,bjk->bik", S[:, :3, :3], np.linalg.inv(S_bind[:, :3, :3]))[self.tdom]
        nref = np.einsum("nij,nj->ni", Rrel, self.n0)
        dot = np.einsum("ni,ni->n", n1, nref)
        inv = (dot < 0) & self.okT & (a1 > 0.05 * self.a0)
        col = self.okT & (a1 < 0.05 * self.a0)
        return P, r, int(inv.sum()), int(col.sum())


def joint_heads(d, S):
    """World positions of bone heads under skinning stack S (B,4,4)."""
    H = d["rest_w"][:, :3, 3]
    return np.einsum("bij,bj->bi", S[:, :3, :3], H) + S[:, :3, 3]


def seg_dist(P, a, b):
    ab = b - a
    t = np.clip(((P - a) @ ab) / max(ab @ ab, 1e-12), 0, 1)
    return np.linalg.norm(P - (a + t[:, None] * ab), axis=1), t


def adjacency(n, E, wlen=None):
    w = np.ones(len(E)) if wlen is None else wlen
    A = sp.coo_matrix((np.r_[w, w], (np.r_[E[:, 0], E[:, 1]], np.r_[E[:, 1], E[:, 0]])), shape=(n, n)).tocsr()
    return A


def harmonic(n, E, V, fixed_mask, fixed_W):
    """Weights on free vertices solving the (inverse edge length weighted) graph Laplace equation with fixed values."""
    l = np.linalg.norm(V[E[:, 0]] - V[E[:, 1]], axis=1)
    A = adjacency(n, E, 1.0 / np.maximum(l, 1e-5))
    deg = np.asarray(A.sum(1)).ravel()
    L = sp.diags(deg) - A
    free = np.nonzero(~fixed_mask)[0]
    fix = np.nonzero(fixed_mask)[0]
    W = fixed_W.copy()
    if len(free) == 0:
        return W
    Lff = L[free][:, free].tocsc()
    Lfc = L[free][:, fix]
    rhs = -(Lfc @ fixed_W[fix])
    # isolated free vertices (no path to a fixed one) would make Lff singular: regularise slightly
    Lff = Lff + sp.identity(len(free)) * 1e-9
    solve = spl.factorized(Lff)
    for b in range(W.shape[1]):
        if np.abs(rhs[:, b]).max() > 0:
            W[free, b] = solve(rhs[:, b])
        else:
            W[free, b] = 0.0
    W = np.clip(W, 0, None)
    return W / np.maximum(W.sum(1, keepdims=True), 1e-12)


def limit4(W, floor=0.01):
    idx = np.argsort(-W, 1)[:, 4:]
    W = W.copy()
    np.put_along_axis(W, idx, 0.0, 1)
    W[W < floor] = 0.0
    return W / np.maximum(W.sum(1, keepdims=True), 1e-12)


def components(n, E, mask):
    """Connected components of the vertices in mask over edges inside mask; returns labels (-1 outside)."""
    m = mask[E[:, 0]] & mask[E[:, 1]]
    A = adjacency(n, E[m])
    nc, lab = connected_components(A, directed=False)
    lab = np.where(mask, lab, -1)
    return lab
