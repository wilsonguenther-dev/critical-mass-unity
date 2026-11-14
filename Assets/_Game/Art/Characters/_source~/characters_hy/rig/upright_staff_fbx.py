"""Stand the Farmer's staff upright in her hand at idle (art/rig-farmer-3, 2026-11-14), FBX bytes in place, no Blender.
The staff is the island bound to handslot.r. It was authored upright for the T-pose bind, so once the clips dropped her
arms (bake_clips.py full local rotations) it tipped diagonally across her body and swung out level in the walk. This
reads the right hand's idle rotation from her baked clip pack the way the game does (absolute local rotations, forward
kinematics of wrist.r, hand.r, handslot.r; best-fit rotation against the bind) and turns the staff's bind vertices and
normals about the grip so that rotation brings it upright, hook end up. In the walk it then swings with the arm.
Usage: python3 upright_staff_fbx.py <model.fbx> <Farmer_Clips.fbx> <out.fbx> [take=Idle]
"""
import sys, os, json, math, collections
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from patch_farmer_fbx import parse, serialize, val, enc_array, child

SLOT = "handslot.r"
HAND = ("wrist.r", "hand.r", "handslot.r")
CLEAR = 0.03   # metres between the staff foot and the ground at idle


def graph(top):
    objs = next(n for n in top if n.name == "Objects"); conns = next(n for n in top if n.name == "Connections")
    byid = {val(k.props[0]): k for k in objs.kids}
    up, down = collections.defaultdict(list), collections.defaultdict(list)
    for c in conns.kids:
        a, b = val(c.props[1]), val(c.props[2]); p = val(c.props[3]) if len(c.props) > 3 else None
        up[a].append((b, p)); down[b].append((a, p))
    return objs, byid, up, down


def euler(e):
    x, y, z = np.radians(e)
    Rx = np.array([[1, 0, 0], [0, math.cos(x), -math.sin(x)], [0, math.sin(x), math.cos(x)]])
    Ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    Rz = np.array([[math.cos(z), -math.sin(z), 0], [math.sin(z), math.cos(z), 0], [0, 0, 1]])
    return Rz @ Ry @ Rx


def pack_points(path, take, frame):
    """World positions of HAND in the clip pack: at rest (frame None) or at a frame of a take."""
    objs, byid, up, down = graph(parse(open(path, "rb").read())[1])
    nm = lambda k: val(k.props[1]).split("\x00")[0]
    models = {val(k.props[0]): k for k in objs.kids if k.name == "Model"}
    byname = {nm(k): i for i, k in models.items()}
    parent = {i: next((b for b, _ in up[i] if b in models), 0) for i in models}
    anim = collections.defaultdict(dict)
    if frame is not None:
        sid = next(val(k.props[0]) for k in objs.kids if k.name == "AnimationStack" and nm(k).split("|")[-1] == take)
        for L, _ in down[sid]:
            for cn, _ in down[L]:
                k = byid.get(cn)
                if k is None or k.name != "AnimationCurveNode": continue
                t = [(b, p) for b, p in up[cn] if b in models]
                if not t: continue
                v = np.zeros(3)
                for cv, ax in down[cn]:
                    kv = val(child(byid[cv], "KeyValueFloat").props[0]); v[{"d|X": 0, "d|Y": 1, "d|Z": 2}[ax]] = kv[min(frame, len(kv) - 1)]
                anim[t[0][0]][t[0][1]] = v

    def p70(i):
        d = {"Lcl Translation": np.zeros(3), "Lcl Rotation": np.zeros(3), "PreRotation": np.zeros(3)}
        for q in child(models[i], "Properties70").kids:
            n = val(q.props[0])
            if n in d: d[n] = np.array([val(x) for x in q.props[4:7]], float)
        d.update(anim.get(i, {}))
        return d

    def G(i):
        if i not in models: return np.eye(4)
        d = p70(i); M = np.eye(4); M[:3, :3] = euler(d["PreRotation"]) @ euler(d["Lcl Rotation"]); M[:3, 3] = d["Lcl Translation"]
        return G(parent[i]) @ M
    return np.array([G(byname[n])[:3, 3] for n in HAND])


def kabsch(A, B):
    a, b = A - A.mean(0), B - B.mean(0)
    U, _, Vt = np.linalg.svd(a.T @ b)
    d = np.sign(np.linalg.det(Vt.T @ U.T))
    return Vt.T @ np.diag([1, 1, d]) @ U.T   # B ~ R @ A


def rot_between(u, v):
    u, v = u / np.linalg.norm(u), v / np.linalg.norm(v)
    c = float(u @ v); w = np.cross(u, v); s = np.linalg.norm(w)
    if s < 1e-9: return np.eye(3)
    K = np.array([[0, -w[2], w[1]], [w[2], 0, -w[0]], [-w[1], w[0], 0]]) / s
    a = math.atan2(s, c)
    return np.eye(3) + math.sin(a) * K + (1 - math.cos(a)) * K @ K


def main(src, clips, dst, take="Idle"):
    rest = pack_points(clips, None, None)
    frames = [pack_points(clips, take, f) for f in (0, 10, 20)]
    Q = kabsch(rest, np.mean(frames, axis=0))
    grip_idle_y = float(np.mean(frames, axis=0)[2][1])   # handslot.r height above the soles (pack ground is 0)
    data = open(src, "rb").read()
    head, top, foot_id, magic, ver = parse(data)
    objs, byid, up, down = graph(top)
    nm = lambda k: val(k.props[1]).split("\x00")[0]
    rep = {"hand_idle_rotation_deg": round(math.degrees(math.acos(max(-1, min(1, (np.trace(Q) - 1) / 2)))), 1), "meshes": {}}
    for g in [k for k in objs.kids if k.name == "Geometry" and val(k.props[2]) == "Mesh"]:
        gid = val(g.props[0])
        model = next(byid[p] for p, _ in up[gid] if p in byid and byid[p].name == "Model")
        V = val(child(g, "Vertices").props[0]).reshape(-1, 3).copy()
        PVI = val(child(g, "PolygonVertexIndex").props[0]); idx = np.where(PVI < 0, ~PVI, PVI)
        w = np.zeros(len(V)); link = None
        for sk, _ in down[gid]:
            if sk not in byid or byid[sk].name != "Deformer": continue
            for cl, _ in down[sk]:
                if cl not in byid: continue
                bones = [nm(byid[b]) for b, _ in down[cl] if b in byid and byid[b].name == "Model"]
                if bones and bones[0] == SLOT and child(byid[cl], "Indexes") is not None:
                    w[val(child(byid[cl], "Indexes").props[0])] = val(child(byid[cl], "Weights").props[0])
                    link = val(child(byid[cl], "TransformLink").props[0]).reshape(4, 4)
        staff = w > 0.5
        if not staff.any() or link is None: continue
        grip = link[3, :3]
        P = V[staff]; mu = P.mean(0)
        ax = np.linalg.svd(P - mu)[2][0]
        if (Q @ ax)[1] < 0: ax = -ax                      # the end that is up at idle now (the hook) stays up
        M = rot_between(ax, Q.T @ np.array([0.0, 1.0, 0.0]))
        V[staff] = grip + (P - grip) @ M.T
        # The grip was centred on the staff (patch_farmer_fbx.py), so upright its foot would reach the ground: slide it up
        # its own (now idle-vertical) axis until the foot clears the ground by CLEAR at idle.
        t = Q.T @ np.array([0.0, 1.0, 0.0])
        tmin = float(((V[staff] - grip) @ t).min())
        shift = max(0.0, CLEAR - (grip_idle_y + tmin))
        V[staff] = V[staff] + shift * t
        child(g, "Vertices").props[0] = enc_array("d", V.reshape(-1))
        nrot = 0
        for le in ("LayerElementNormal", "LayerElementTangent", "LayerElementBinormal"):
            for L in [k for k in g.kids if k.name == le]:
                arr = {"LayerElementNormal": "Normals", "LayerElementTangent": "Tangents", "LayerElementBinormal": "Binormals"}[le]
                an = child(L, arr)
                if an is None: continue
                mode = val(child(L, "MappingInformationType").props[0]); ref = val(child(L, "ReferenceInformationType").props[0])
                N = val(an.props[0]).reshape(-1, 3).copy()
                if mode == "ByPolygonVertex":
                    sel = staff[idx]
                elif mode in ("ByVertice", "ByVertex"):
                    sel = staff
                else:
                    raise SystemExit(f"{nm(model)} {le}: {mode} not handled")
                if ref == "Direct":
                    N[sel] = N[sel] @ M.T
                elif ref == "IndexToDirect":
                    xn = child(L, arr[:-1] + "Index") or child(L, arr + "Index")
                    I = val(xn.props[0]).copy()
                    # the table may be shared with the body: the staff entries get their own turned copies
                    new = N[I[sel]] @ M.T
                    I[sel] = len(N) + np.arange(int(sel.sum()))
                    N = np.vstack([N, new])
                    xn.props[0] = enc_array("i", I)
                else:
                    raise SystemExit(f"{nm(model)} {le}: {ref} not handled")
                nrot += int(sel.sum())
                an.props[0] = enc_array("d", N.reshape(-1))
        tilt_before = math.degrees(math.acos(abs((Q @ ax)[1])))
        rep["meshes"][nm(model)] = dict(staff_verts=int(staff.sum()), idle_tilt_before_deg=round(tilt_before, 1),
                                        foot_above_ground_idle=round(grip_idle_y + tmin + shift, 3), slid_up=round(shift, 3), turned_deg=round(math.degrees(math.acos(max(-1, min(1, (np.trace(M) - 1) / 2)))), 1), vectors_rotated=nrot)
    open(dst, "wb").write(serialize(head, top, foot_id, magic, ver))
    print(json.dumps(rep))


if __name__ == "__main__":
    main(*sys.argv[1:])
