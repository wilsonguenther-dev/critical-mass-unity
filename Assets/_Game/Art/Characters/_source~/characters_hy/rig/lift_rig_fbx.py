"""Lift a whole rigged class FBX (mesh, skeleton and bind pose) by D metres along +Y, in place, no Blender
(art/rig-miner-1, 2026-11-14). Why: the HY Miner's rest-pose (T) mesh reaches 0.112 m below the pivot because its A-pose
legs are longer once straightened, which fails ArtImportTests.IsTheBibleHeightWithItsFeetOnTheGround (0.10 limit).
Moving mesh and skeleton together keeps the skin binding exact; the per-class clip bake (bake_clips.py) then measures
the new rest soles and grounds every clip from there. Edits: every mesh's Vertices y += D (mesh nodes are at the root
with identity transforms, checked), the root armature node's Lcl Translation y += D, every Cluster TransformLink
translation y += D with Transform = inverse(TransformLink) (checked equal before), every bind-pose PoseNode matrix of a
bone or the armature node translated by D. Nodes, names, normals, UVs, colours and weights stay as they were.
Usage: python3 lift_rig_fbx.py <in.fbx> <out.fbx> <D>
"""
import sys, os, struct, json, collections
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from patch_farmer_fbx import parse, serialize, val, enc_array, child, Node


def main(src, dst, d):
    d = float(d)
    data = open(src, "rb").read()
    head, top, foot_id, magic, ver = parse(data)
    assert serialize(head, top, foot_id, magic, ver) == data, "round trip differs"
    objs = next(n for n in top if n.name == "Objects")
    conns = next(n for n in top if n.name == "Connections")
    byid = {val(k.props[0]): k for k in objs.kids}
    parent = {}
    for c in conns.kids:
        if val(c.props[0]) == "OO": parent[val(c.props[1])] = val(c.props[2])
    rep = collections.Counter()
    moved_nodes = set()
    for k in objs.kids:
        if k.name == "Model":
            typ, mid = val(k.props[2]), val(k.props[0])
            if typ == "Mesh":
                assert parent.get(mid) == 0, "mesh not at the root"
            if typ in ("LimbNode", "Null"):
                moved_nodes.add(mid)
            if typ == "Null" and parent.get(mid) == 0:
                p70 = child(k, "Properties70")
                lt = next((q for q in p70.kids if val(q.props[0]) == "Lcl Translation"), None)
                if lt is None:   # Blender writes no entry for a zero translation: add one (FBX 7.x Properties70 P record)
                    S = lambda t: b"S" + struct.pack("<I", len(t)) + t.encode()
                    lt = Node("P", [S("Lcl Translation"), S("Lcl Translation"), S(""), S("A"),
                                    b"D" + struct.pack("<d", 0.0), b"D" + struct.pack("<d", 0.0), b"D" + struct.pack("<d", 0.0)], [], False)
                    p70.kids.append(lt); rep["armature_lcl_added"] += 1
                y = val(lt.props[5]); lt.props[5] = b"D" + struct.pack("<d", y + d); rep["armature_lcl"] += 1
        elif k.name == "Geometry" and val(k.props[2]) == "Mesh":
            vn = child(k, "Vertices"); V = val(vn.props[0]).reshape(-1, 3); V[:, 1] += d
            vn.props[0] = enc_array("d", V.reshape(-1)); rep["meshes"] += 1
        elif k.name == "Deformer" and val(k.props[2]) == "Cluster" and child(k, "TransformLink") is not None:
            ln, tn = child(k, "TransformLink"), child(k, "Transform")
            L = val(ln.props[0]).reshape(4, 4).copy(); T = val(tn.props[0]).reshape(4, 4)
            assert np.allclose(T @ L, np.eye(4), atol=1e-4), "Transform is not inverse(TransformLink)"
            L[3, 1] += d
            ln.props[0] = enc_array("d", L.reshape(-1)); tn.props[0] = enc_array("d", np.linalg.inv(L).reshape(-1)); rep["clusters"] += 1
        elif k.name == "Pose":
            for pn in k.kids:
                if pn.name != "PoseNode": continue
                if val(child(pn, "Node").props[0]) in moved_nodes:
                    mn = child(pn, "Matrix"); M = val(mn.props[0]).reshape(4, 4).copy(); M[3, 1] += d
                    mn.props[0] = enc_array("d", M.reshape(-1)); rep["pose_nodes"] += 1
    open(dst, "wb").write(serialize(head, top, foot_id, magic, ver))
    print(json.dumps(dict(rep, lift=d)))


if __name__ == "__main__":
    main(*sys.argv[1:4])
