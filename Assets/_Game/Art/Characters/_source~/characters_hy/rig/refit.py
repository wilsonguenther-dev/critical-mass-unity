"""Joint refit: put the arm chains on the mesh's own arm axis (A-pose), rebuild the A-pose stack and every clip's
skinning stack by forward kinematics with the clip's local transforms unchanged (bone orientations kept)."""
import numpy as np

import riglib as rl


def depth_order(parents):
    order, seen = [], set()

    def visit(i):
        if i in seen:
            return
        if parents[i] >= 0:
            visit(parents[i])
        seen.add(i)
        order.append(i)
    for i in range(len(parents)):
        visit(i)
    return order


def fk_restack(S, rest, rest_new, parents, order):
    """S: (F,B,4,4) skinning stacks for rig `rest`; returns stacks for rig `rest_new` with the same local transforms."""
    M = np.einsum("fbij,bjk->fbik", S, rest)              # pose world matrices (old)
    Mn = np.empty_like(M)
    for b in order:
        p = parents[b]
        if p < 0:
            Mn[:, b] = M[:, b] @ np.linalg.inv(rest[b]) @ rest_new[b]
            continue
        rel_old = np.linalg.inv(rest[p]) @ rest[b]
        rel_new = np.linalg.inv(rest_new[p]) @ rest_new[b]
        L = np.linalg.inv(rel_old) @ np.linalg.inv(M[:, p]) @ M[:, b]
        Mn[:, b] = Mn[:, p] @ rel_new @ L
    return np.einsum("fbij,bjk->fbik", Mn, np.linalg.inv(rest_new))


def rot_between(a, b):
    a = a / np.linalg.norm(a)
    b = b / np.linalg.norm(b)
    v = np.cross(a, b)
    c = float(a @ b)
    if np.linalg.norm(v) < 1e-9:
        return np.eye(3)
    K = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + K + K @ K * (1 / (1 + c))


def arm_refit(d, A, dom, side, shoulder_in=0.35):
    """New A-pose heads for the arm chain of one side, on the axis of the forearm + hand vertices (PCA), keeping the
    chain's own length ratios. The shoulder pivot sits on that axis where it passes closest to the old pivot, moved
    `shoulder_in` x the arm radius back toward the body along the axis."""
    B = d["bones"]
    bi = {b: i for i, b in enumerate(B)}
    HA = rl.joint_heads(d, d["S_P"])
    sel = np.isin(dom, [bi[f"lowerarm.{side}"], bi[f"wrist.{side}"], bi[f"hand.{side}"]])
    P = A[sel]
    c = P.mean(0)
    _, _, Vt = np.linalg.svd(P - c)
    u = Vt[0]
    if (HA[bi[f"hand.{side}"]] - HA[bi[f"upperarm.{side}"]]) @ u < 0:
        u = -u
    S0 = HA[bi[f"upperarm.{side}"]]
    t = (S0 - c) @ u
    S = c + t * u
    perp = np.linalg.norm((P - c) - np.outer((P - c) @ u, u), axis=1)
    rad = float(np.median(perp))
    S = S - u * shoulder_in * rad
    chain = [f"upperarm.{side}", f"lowerarm.{side}", f"wrist.{side}", f"hand.{side}"]
    # chain proportions from the old A-pose heads, total length to the old hand head projected on the axis
    old = np.array([HA[bi[n]] for n in chain])
    seg = np.linalg.norm(np.diff(old, axis=0), axis=1)
    L_new = float((HA[bi[f"hand.{side}"]] - S) @ u)
    acc = np.r_[0, np.cumsum(seg)] / seg.sum() * L_new
    new = {n: S + acc[k] * u for k, n in enumerate(chain)}
    new[f"handslot.{side}"] = new[f"hand.{side}"] + (HA[bi[f"handslot.{side}"]] - HA[bi[f"hand.{side}"]])
    return new, u, dict(radius=round(rad, 3), shoulder_move=round(float(np.linalg.norm(S - S0)), 3),
                        axis=[round(float(x), 3) for x in u], length=round(L_new, 3))


def compute(d, A, dom, sides=("l", "r"), shoulder_in=0.35):
    """Returns rest heads (B,3) and the new A-pose stack S_P_new."""
    B = d["bones"]
    bi = {b: i for i, b in enumerate(B)}
    rest = d["rest_w"]
    HR = rest[:, :3, 3].copy()
    S_P = d["S_P"].copy()
    info = {}
    for s in sides:
        newA, u, inf = arm_refit(d, A, dom, s, shoulder_in)
        info[s] = inf
        sh = newA[f"upperarm.{s}"]
        # A-pose rotation: rest arm direction (shoulder -> hand, rest heads) onto the mesh axis
        d_rest = rest[bi[f"hand.{s}"], :3, 3] - rest[bi[f"upperarm.{s}"], :3, 3]
        R = rot_between(d_rest, u)
        T = np.eye(4)
        T[:3, :3] = R
        T[:3, 3] = sh - R @ sh
        names = [n for n in B if n.endswith("." + s) and any(n.startswith(k) for k in ("upperarm", "lowerarm", "wrist", "hand"))]
        # every bone below upperarm in the hierarchy rotates with the arm
        sub = set()
        for i, n in enumerate(B):
            j = i
            while j >= 0:
                if B[j] == f"upperarm.{s}":
                    sub.add(i)
                    break
                j = d["parents"][j]
        for i in sub:
            S_P[i] = T
        Ri = R.T
        for n, p in newA.items():
            HR[bi[n]] = sh + Ri @ (p - sh)
        # other descendants (fingers etc.) follow the hand head's move
        dh = HR[bi[f"hand.{s}"]] - rest[bi[f"hand.{s}"], :3, 3]
        for i in sub:
            if B[i] not in newA:
                HR[i] = rest[i, :3, 3] + dh
    return HR, S_P, info


def apply(d, HR, S_P_new=None):
    rest_new = d["rest_w"].copy()
    rest_new[:, :3, 3] = HR
    order = depth_order(list(d["parents"]))
    fk = dict(d)
    fk["rest_w"] = rest_new
    for c in d["clip_names"]:
        fk[f"S_{c}"] = fk_restack(d[f"S_{c}"].astype(np.float64), d["rest_w"], rest_new, list(d["parents"]), order)
    if S_P_new is not None:
        fk["S_P"] = S_P_new
    d["fk"] = fk
    d["S_P_new"] = S_P_new
    return fk
