"""Scale a whole rigged class FBX (mesh, skeleton and bind pose) uniformly by S about the origin (the feet), in place,
no Blender (art/rig-miner-3, 2026-11-14). Why: the HY Miner's body (soles to helmet top, crest and pick left out) stood
2.034 m against the 2.0 bible height. One uniform factor keeps the pick, the crest and every proportion, and keeps the
skin binding exact; the per-class clip bake (bake_clips.py) then measures the new hips and soles from the file.
Edits (same file layout lift_rig_fbx.py checks): every mesh's Vertices *= S (mesh nodes at the root, identity), every
bone's (LimbNode) Lcl Translation *= S, every Cluster TransformLink translation *= S with Transform = inverse(link),
every bind-pose PoseNode matrix of a bone: translation *= S. Rotations, names, normals, UVs, colours, weights unchanged.
Usage: python3 scale_rig_fbx.py <in.fbx> <out.fbx> <S>
"""
import sys, os, json, collections
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from patch_farmer_fbx import parse, serialize, val, enc_array, child


def main(src, dst, s):
    s = float(s)
    data = open(src, "rb").read()
    head, top, foot_id, magic, ver = parse(data)
    assert serialize(head, top, foot_id, magic, ver) == data, "round trip differs"
    objs = next(n for n in top if n.name == "Objects")
    conns = next(n for n in top if n.name == "Connections")
    byid = {val(k.props[0]): k for k in objs.kids}
    parent = {}
    for c in conns.kids:
        if val(c.props[0]) == "OO" and (val(c.props[2]) == 0 or (val(c.props[2]) in byid and byid[val(c.props[2])].name == "Model")):
            parent[val(c.props[1])] = val(c.props[2])
    rep = collections.Counter()
    bones = set()
    for k in objs.kids:
        if k.name != "Model":
            continue
        typ, mid = val(k.props[2]), val(k.props[0])
        p70 = child(k, "Properties70")
        lt = next((q for q in p70.kids if val(q.props[0]) == "Lcl Translation"), None) if p70 is not None else None
        if typ == "Mesh":
            assert parent.get(mid) == 0, "mesh not at the root"
            assert lt is None or max(abs(val(lt.props[i])) for i in (4, 5, 6)) < 1e-6, "mesh node translated"
        elif typ == "Null":
            assert lt is None or max(abs(val(lt.props[i])) for i in (4, 5, 6)) < 1e-6, "armature node translated"
        elif typ == "LimbNode":
            bones.add(mid)
            if lt is not None:
                for i in (4, 5, 6):
                    lt.props[i] = b"D" + np.float64(val(lt.props[i]) * s).tobytes()
                rep["bone_lcl"] += 1
    for k in objs.kids:
        if k.name == "Geometry" and val(k.props[2]) == "Mesh":
            vn = child(k, "Vertices"); V = val(vn.props[0]) * s
            vn.props[0] = enc_array("d", V.reshape(-1)); rep["meshes"] += 1
        elif k.name == "Deformer" and val(k.props[2]) == "Cluster" and child(k, "TransformLink") is not None:
            ln, tn = child(k, "TransformLink"), child(k, "Transform")
            L = val(ln.props[0]).reshape(4, 4).copy(); T = val(tn.props[0]).reshape(4, 4)
            assert np.allclose(T @ L, np.eye(4), atol=1e-4), "Transform is not inverse(TransformLink)"
            L[3, :3] *= s
            ln.props[0] = enc_array("d", L.reshape(-1)); tn.props[0] = enc_array("d", np.linalg.inv(L).reshape(-1))
            rep["clusters"] += 1
        elif k.name == "Pose":
            for pn in k.kids:
                if pn.name == "PoseNode" and val(child(pn, "Node").props[0]) in bones:
                    mn = child(pn, "Matrix"); M = val(mn.props[0]).reshape(4, 4).copy(); M[3, :3] *= s
                    mn.props[0] = enc_array("d", M.reshape(-1)); rep["pose_nodes"] += 1
    open(dst, "wb").write(serialize(head, top, foot_id, magic, ver))
    print(json.dumps(dict(rep, scale=s)))


if __name__ == "__main__":
    main(*sys.argv[1:4])
