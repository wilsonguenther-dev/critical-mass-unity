"""Farmer look fix, applied to the class FBX's own bytes (art/rig-farmer-2, 2026-11-14). Pure Python + numpy, no Blender.

Why not a Blender re-export: importing Farmer.fbx and exporting it again with the class recipe wrote every mesh node
with Lcl Rotation 90 X and its geometry turned to match (GitHub Blender runs 43611159728 and the next), which changes
the meshes' local bounds that the game's import tests measure. Editing the arrays in place keeps every node, bone,
bind pose, normal, UV and the file layout as they were; only vertex positions of the staff and colour values change.

Edits (same rules as fix_farmer_look.py, which renders and measures them in Blender):
  1. Team colour: every non-staff code-2 corner (alpha 2/8, the lime outfit pieces: backpack, shoulder pads) becomes
     code 8 (cloth) with the authored Azure cloth hue (ArtMapping.FarmerAzure #4999A5) at the same brightness, so the
     shader's team swap reaches it. The staff's own code-2 and code-8 corners are kept.
  2. Staff: the island bound to handslot.r is moved along its own long axis so the grip (handslot.r bind position)
     sits at its middle (shape and length kept), and its dark slate code-0 shaft becomes light wood at the same relative
     values.
  3. Face: none added; her eyes, brows and mouth are painted in the vertex colour already.
Usage: python3 patch_farmer_fbx.py <in.fbx> <out.fbx> [report.json]
"""
import struct, zlib, sys, json, collections
import numpy as np

AUTHORED_CLOTH = np.array([0x49, 0x99, 0xA5]) / 255.0
WOOD = np.array([0xA8, 0x76, 0x45]) / 255.0
SLOT = "handslot.r"


class Node:
    __slots__ = ("name", "props", "kids", "sentinel")

    def __init__(self, name, props, kids, sentinel):
        self.name, self.props, self.kids, self.sentinel = name, props, kids, sentinel


ARR = {"f": "<f4", "d": "<f8", "l": "<i8", "i": "<i4", "b": "u1"}


def parse(data):
    ver = struct.unpack_from("<I", data, 23)[0]
    assert ver < 7500, "64-bit node headers not handled"

    def prop(o):
        t = chr(data[o]); s = o; o += 1
        if t in "YCIFDL":
            o += {"Y": 2, "C": 1, "I": 4, "F": 4, "D": 8, "L": 8}[t]
        elif t in ARR:
            n, enc, cl = struct.unpack_from("<III", data, o); o += 12 + cl
        elif t in "SR":
            n = struct.unpack_from("<I", data, o)[0]; o += 4 + n
        else:
            raise ValueError("prop type " + t)
        return data[s:o], o

    def node(o):
        end, nprops, plen = struct.unpack_from("<III", data, o); o += 12
        nl = data[o]; o += 1
        if end == 0:
            return None, o
        name = data[o:o + nl].decode("latin1"); o += nl
        props = []
        for _ in range(nprops):
            raw, o = prop(o)
            props.append(raw)
        kids = []
        sentinel = False
        while o < end:
            if end - o == 13 and data[o:o + 13] == b"\0" * 13:
                sentinel = True; o += 13; break
            k, o = node(o)
            kids.append(k)
        return Node(name, props, kids, sentinel), end

    o = 27
    top = []
    while True:
        n, o2 = node(o)
        if n is None:
            o = o2; break
        top.append(n); o = o2
    return data[:27], top, data[o:o + 16], data[-16:], ver


def val(raw):
    t = chr(raw[0])
    if t in "YCIFDL":
        return struct.unpack_from({"Y": "<h", "C": "<?", "I": "<i", "F": "<f", "D": "<d", "L": "<q"}[t], raw, 1)[0]
    if t in ARR:
        n, enc, cl = struct.unpack_from("<III", raw, 1)
        b = raw[13:13 + cl]
        if enc == 1: b = zlib.decompress(b)
        return np.frombuffer(b, ARR[t], n).copy()
    n = struct.unpack_from("<I", raw, 1)[0]
    b = raw[5:5 + n]
    return b if t == "R" else b.decode("latin1")


def enc_array(t, arr):
    a = np.ascontiguousarray(arr, dtype=ARR[t])
    z = zlib.compress(a.tobytes(), 9)
    return t.encode() + struct.pack("<III", len(a), 1, len(z)) + z


def serialize(head, top, foot_id, magic, ver):
    out = bytearray(head)

    def w(n):
        start = len(out)
        out.extend(b"\0" * 12)
        nm = n.name.encode("latin1")
        out.append(len(nm)); out.extend(nm)
        p0 = len(out)
        for p in n.props: out.extend(p)
        plen = len(out) - p0
        for k in n.kids: w(k)
        if n.sentinel or n.kids: out.extend(b"\0" * 13)
        struct.pack_into("<III", out, start, len(out), len(n.props), plen)

    for n in top: w(n)
    out.extend(b"\0" * 13)
    out.extend(foot_id); out.extend(b"\0" * 4)
    pad = ((len(out) + 15) & ~15) - len(out)
    if pad == 0: pad = 16
    out.extend(b"\0" * pad)
    out.extend(struct.pack("<I", ver)); out.extend(b"\0" * 120); out.extend(magic)
    return bytes(out)


def child(n, name):
    for k in n.kids:
        if k.name == name: return k


def main(src, dst, rep_path=None):
    data = open(src, "rb").read()
    head, top, foot_id, magic, ver = parse(data)
    # round-trip guard: an unmodified tree must serialize to the same node bytes
    again = serialize(head, top, foot_id, magic, ver)
    rep = {"roundtrip_identical": again == data, "src_bytes": len(data)}
    objs = next(n for n in top if n.name == "Objects")
    conns = next(n for n in top if n.name == "Connections")
    byid = {val(k.props[0]): k for k in objs.kids}
    par, kids = collections.defaultdict(list), collections.defaultdict(list)
    for c in conns.kids:
        if val(c.props[0]) == "OO":
            a, b = val(c.props[1]), val(c.props[2])
            par[a].append(b); kids[b].append(a)
    nm = lambda k: val(k.props[1]).split("\x00")[0]
    rep["meshes"] = {}
    for g in [k for k in objs.kids if k.name == "Geometry" and val(k.props[2]) == "Mesh"]:
        gid = val(g.props[0])
        model = next(byid[p] for p in par[gid] if p in byid and byid[p].name == "Model")
        mname = nm(model)
        vn = child(g, "Vertices")
        V = val(vn.props[0]).reshape(-1, 3)
        PVI = val(child(g, "PolygonVertexIndex").props[0])
        idx = np.where(PVI < 0, ~PVI, PVI)
        # staff weights and the slot's bind position
        w = np.zeros(len(V)); link = None
        for sk in [byid[k] for k in kids[gid] if k in byid and byid[k].name == "Deformer"]:
            for cl in [byid[k] for k in kids[val(sk.props[0])] if k in byid]:
                bones = [nm(byid[k]) for k in kids[val(cl.props[0])] if k in byid and byid[k].name == "Model"]
                if bones and bones[0] == SLOT and child(cl, "Indexes") is not None:
                    w[val(child(cl, "Indexes").props[0])] = val(child(cl, "Weights").props[0])
                    link = val(child(cl, "TransformLink").props[0]).reshape(4, 4)
        staff = w > 0.5
        s = {"verts": int(len(V)), "staff_verts": int(staff.sum())}
        if staff.any() and link is not None:
            P = V[staff]; mu = P.mean(0)
            ax = np.linalg.svd(P - mu, full_matrices=False)[2][0]
            grip = link[3, :3]
            t = (P - grip) @ ax
            shift = -0.5 * (t.min() + t.max())
            V2 = V.copy(); V2[staff] = P + shift * ax
            vn.props[0] = enc_array("d", V2.reshape(-1))
            s["staff"] = dict(length=round(float(t.max() - t.min()), 3), from_grip_before=[round(float(t.min()), 3), round(float(t.max()), 3)],
                              shift_m=round(float(shift), 3), axis=[round(float(x), 3) for x in ax])
        lc = child(g, "LayerElementColor")
        if lc is not None and not mname.endswith("_Shadow"):
            cn, ci = child(lc, "Colors"), child(lc, "ColorIndex")
            assert val(child(lc, "MappingInformationType").props[0]) == "ByPolygonVertex"
            assert val(child(lc, "ReferenceInformationType").props[0]) == "IndexToDirect"
            C = val(cn.props[0]).reshape(-1, 4)
            CI = val(ci.props[0])
            pc = C[CI].copy()
            codes = np.round(pc[:, 3] * 8).astype(int)
            sc = staff[idx]
            s["codes_before"] = {int(k): int(v) for k, v in zip(*np.unique(codes, return_counts=True))}
            cloth = (codes == 2) & ~sc
            k = pc[cloth, :3].max(1) / AUTHORED_CLOTH.max()
            pc[cloth, :3] = np.clip(np.outer(k, AUTHORED_CLOTH), 0, 1)
            pc[cloth, 3] = 1.0
            wood = sc & (codes == 0)
            if wood.any():
                v = pc[wood, :3].max(1)
                rel = np.clip(v / max(1e-4, float(np.median(v))), 0.6, 1.4)
                pc[wood, :3] = np.clip(np.outer(rel, WOOD), 0, 1)
            uniq, inv = np.unique(np.round(pc, 6), axis=0, return_inverse=True)
            cn.props[0] = enc_array("d", uniq.reshape(-1))
            ci.props[0] = enc_array("i", inv.reshape(-1).astype(np.int32))
            codes2 = np.round(uniq[inv.reshape(-1), 3] * 8).astype(int)
            s["codes_after"] = {int(k): int(v) for k, v in zip(*np.unique(codes2, return_counts=True))}
            s["cloth_changed"], s["wood_changed"] = int(cloth.sum()), int(wood.sum())
        rep["meshes"][mname] = s
    out = serialize(head, top, foot_id, magic, ver)
    open(dst, "wb").write(out)
    rep["dst_bytes"] = len(out)
    print(json.dumps(rep, indent=1))
    if rep_path: json.dump(rep, open(rep_path, "w"), indent=1)


if __name__ == "__main__":
    main(*sys.argv[1:4])
