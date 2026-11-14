"""Move one joint (and everything under it) along its parent bone's own axis, in a rigged class FBX, in place, no
Blender (art/rig-warrior-1, 2026-11-14). The mesh, its weights and its colours do not change; the bone's rest
translation, the bind links of the bone and its subtree, their cluster Transforms (= inverse link) and bind-pose
matrices move together, so the rest pose shows the same mesh and only the pivot moves.
Why: the HY Warrior is skinned to the KayKit skeleton, but his boots reach 0.061 m below KayKit's sole. Grounding his
soles therefore lifts his ankle and toe joints 0.06 m inside the boots, and the game's feet test (lowest of the
toes.l/r and foot.l/r joints, limit 0.35 u) failed in Run and the strafes. Lengthening each shin by D puts the ankle and
toe joints back at KayKit's height above his own sole.
Usage: python3 move_joint_fbx.py <in.fbx> <out.fbx> <bone>=<metres along the parent's +Y> [more]
"""
import sys, os, json, collections
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from patch_farmer_fbx import parse, serialize, val, enc_array, child
import struct


def main(src, dst, *moves):
    data = open(src, "rb").read()
    head, top, foot_id, magic, ver = parse(data)
    assert serialize(head, top, foot_id, magic, ver) == data
    objs = next(n for n in top if n.name == "Objects")
    conns = next(n for n in top if n.name == "Connections")
    byid = {val(k.props[0]): k for k in objs.kids}
    nm = lambda k: val(k.props[1]).split("\x00")[0]
    models = {val(k.props[0]): k for k in objs.kids if k.name == "Model"}
    byname = {nm(k): i for i, k in models.items()}
    parent, kids = {}, collections.defaultdict(list)
    bone_clusters = collections.defaultdict(list)
    for c in conns.kids:
        if val(c.props[0]) != "OO": continue
        a, b = val(c.props[1]), val(c.props[2])
        if a in models and (b == 0 or b in models):
            parent[a] = b; kids[b].append(a)
        if a in models and b in byid and byid[b].name == "Deformer":
            bone_clusters[a].append(byid[b])
    poses = [pn for k in objs.kids if k.name == "Pose" for pn in k.kids if pn.name == "PoseNode"]
    rep = {}
    for mv in moves:
        bone, d = mv.split("="); d = float(d)
        bid = byname[bone]; pid = parent[bid]
        # parent's global Y axis at bind (its link, row-vector convention: row 1 = local Y)
        plink = val(child(bone_clusters[pid][0], "TransformLink").props[0]).reshape(4, 4)
        axis = plink[1, :3] / np.linalg.norm(plink[1, :3])
        delta = axis * d
        # rest translation of the bone in its parent's frame
        p70 = child(models[bid], "Properties70")
        lt = next(q for q in p70.kids if val(q.props[0]) == "Lcl Translation")
        y = val(lt.props[5]); lt.props[5] = b"D" + struct.pack("<d", y + d)
        sub, stack = [], [bid]
        while stack:
            n = stack.pop(); sub.append(n); stack.extend(kids[n])
        nl = 0
        for n in sub:
            for cl in bone_clusters[n]:
                ln, tn = child(cl, "TransformLink"), child(cl, "Transform")
                L = val(ln.props[0]).reshape(4, 4).copy(); L[3, :3] += delta
                ln.props[0] = enc_array("d", L.reshape(-1)); tn.props[0] = enc_array("d", np.linalg.inv(L).reshape(-1)); nl += 1
        npn = 0
        for pn in poses:
            if val(child(pn, "Node").props[0]) in sub:
                mn = child(pn, "Matrix"); M = val(mn.props[0]).reshape(4, 4).copy(); M[3, :3] += delta
                mn.props[0] = enc_array("d", M.reshape(-1)); npn += 1
        rep[bone] = dict(local_y_before=round(y, 4), local_y_after=round(y + d, 4), global_move=[round(float(x), 4) for x in delta],
                         subtree=[nm(models[n]) for n in sub], links=nl, pose_nodes=npn)
    open(dst, "wb").write(serialize(head, top, foot_id, magic, ver))
    print(json.dumps(rep))


if __name__ == "__main__":
    main(*sys.argv[1:])
