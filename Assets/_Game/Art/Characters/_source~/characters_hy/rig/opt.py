"""Fit LBS weights so every edge moves like the rotation-blended (dual-quaternion-like) deformation of the
initial weights, holding props, rigid parts and single-bone interiors fixed. Normal equations, per-frame accumulation."""
import numpy as np, scipy.sparse as sp, scipy.sparse.linalg as spl, riglib as rl, fix

def polar(M):
    U, _, Vt = np.linalg.svd(M)
    D = np.ones((len(M), 3)); D[:, 2] = np.sign(np.linalg.det(U @ Vt))
    return np.einsum("nij,nj,njk->nik", U, D, Vt)

def pick_frames(dd, clips, per=5):
    out = []
    for c in clips:
        S = dd["S_" + c]
        n = len(S)
        for f in np.unique(np.linspace(0, n - 1, min(per, n)).round().astype(int)):
            out.append(S[f].astype(float))
    return out

def optimise(W0, A, E, S_bind, frames, locked, iters=2, alpha=0.05, free_thr=0.97, ring=1, cand_smooth=2):
    n, nb = W0.shape
    Sbi = np.linalg.inv(S_bind)
    G = [np.einsum("bij,bjk->bik", S, Sbi) for S in frames]
    dom = np.argmax(W0, 1)
    free = (W0.max(1) < free_thr)
    diff = dom[E[:, 0]] != dom[E[:, 1]]
    edge_v = np.zeros(n, bool); edge_v[E[diff].ravel()] = True
    free |= edge_v
    for _ in range(ring):
        g = free.copy(); g[E[free[E[:, 0]], 1]] = True; g[E[free[E[:, 1]], 0]] = True; free = g
    free &= ~locked
    # candidate bones: top-4 of the init smoothed over the mesh
    Ws = W0.copy()
    for _ in range(cand_smooth):
        acc = Ws.copy(); np.add.at(acc, E[:, 0], Ws[E[:, 1]]); np.add.at(acc, E[:, 1], Ws[E[:, 0]])
        Ws = acc / acc.sum(1, keepdims=True)
    cand = np.argsort(-Ws, 1)[:, :4]
    candw = np.take_along_axis(Ws, cand, 1)
    col = -np.ones((n, 4), int)
    fv = np.nonzero(free)[0]
    m = candw[fv] > 2e-3
    m[:, 0] = True
    cnt = m.sum()
    col[fv[:, None].repeat(4, 1)[m], np.nonzero(m)[1]] = np.arange(cnt)
    nu = cnt
    sel = free[E[:, 0]] | free[E[:, 1]]
    Es = E[sel]; ne = len(Es)
    l0 = np.linalg.norm(A[Es[:, 0]] - A[Es[:, 1]], axis=1)
    sc = 1.0 / np.maximum(l0, 0.25 * np.median(l0))
    W = W0.copy()
    for it in range(iters):
        Wfix = W.copy(); Wfix[free] = 0
        M = sp.csr_matrix((nu, nu)); g = np.zeros(nu)
        wbar = 0.5 * (W[Es[:, 0]] + W[Es[:, 1]])
        for Gf in G:
            Re = polar(np.einsum("eb,bij->eij", wbar, Gf[:, :3, :3]))
            tgt = np.einsum("eij,ej->ei", Re, A[Es[:, 0]] - A[Es[:, 1]])
            Pfix = np.einsum("nb,bij,nj->ni", Wfix, Gf[:, :3, :3], A) + Wfix @ Gf[:, :3, 3]
            rhs = (tgt - (Pfix[Es[:, 0]] - Pfix[Es[:, 1]])) * sc[:, None]
            rows, cols, vals = [], [], []
            for side, sgn in ((0, 1.0), (1, -1.0)):
                v = Es[:, side]
                for k in range(4):
                    c = col[v, k]; ok = c >= 0
                    if not ok.any():
                        continue
                    b = cand[v[ok], k]
                    q = np.einsum("eij,ej->ei", Gf[b, :3, :3], A[v[ok]]) + Gf[b, :3, 3]
                    q = q * (sgn * sc[ok])[:, None]
                    e = np.nonzero(ok)[0]
                    for a in range(3):
                        rows.append(3 * e + a); cols.append(c[ok]); vals.append(q[:, a])
            J = sp.csr_matrix((np.concatenate(vals), (np.concatenate(rows), np.concatenate(cols))), shape=(3 * ne, nu))
            M = M + (J.T @ J); g += J.T @ rhs.ravel()
        dg = M.diagonal(); s0 = np.median(dg[dg > 0])
        lam = alpha * s0; rho = 10.0 * s0
        # normalisation rows
        rr = np.nonzero(col.ravel() >= 0)[0] // 4
        N = sp.csr_matrix((np.ones(nu), (rr[np.argsort(col.ravel()[col.ravel() >= 0])], np.arange(nu))), shape=(n, nu))
        N = N[np.unique(rr)]
        winit = np.zeros(nu)
        vv, kk = np.nonzero(col >= 0)
        winit[col[vv, kk]] = W0[vv, cand[vv, kk]]
        A_ = M + lam * sp.identity(nu) + rho * (N.T @ N)
        b_ = g + lam * winit + rho * (N.T @ np.ones(N.shape[0]))
        x = spl.spsolve(A_.tocsc(), b_)
        x = np.maximum(x, 0)
        Wn = W.copy(); Wn[free] = 0
        Wn[vv, cand[vv, kk]] = x[col[vv, kk]]
        s = Wn.sum(1); bad = s < 1e-6
        Wn[bad] = W0[bad]; Wn /= Wn.sum(1, keepdims=True)
        W = rl.limit4(Wn)
    return W, dict(free=int(free.sum()), unknowns=int(nu), edges=int(ne), frames=len(G))

def make_fn(clips_fn, per=5, **kw):
    def f(d, dd, W, A, S, tag, locked, bind):
        E = d[f"{tag}_E"]
        frames = pick_frames(dd, clips_fn(dd), per)
        Wn, info = optimise(W, A, E, S, frames, locked, **kw)
        print("OPT", tag, info, flush=True)
        return Wn
    return f
