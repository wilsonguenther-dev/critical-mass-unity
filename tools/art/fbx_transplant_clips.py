#!/usr/bin/env python3
"""Copy the animation curves of one FBX into another with the same rig (fix/kaiju-alive-1, 2026-11-13).

Why: the fighter-LOD pass (critical-mass-unity-art-lods 3b15ccf, build_lods.py) set the Kaiju rig to
`pose_position = "REST"` before its export and only back to "POSE" after it, so every one of the 8 Kaiju clips in
Kaiju_Adult.fbx and Kaiju_Hatchling.fbx was baked as the rest pose (2 keys per curve, constant): the Kaiju glided.
The pre-LOD files (critical-mass-unity-art-kaiju d908740) carry the real clips on the same 20-bone rig (identical
bind poses, stack names and ranges). This tool replaces the key data of every AnimationCurve in DEST by the curve of
SRC that drives the same (take, model, property, axis), keeping DEST's object ids, meshes, skins, materials and
everything else byte-identical, then re-serialises the file (FBX 7.x binary, 32-bit offsets).

    python3 -I tools/art/fbx_transplant_clips.py SRC.fbx DEST.fbx OUT.fbx [--check]

No Blender, no third-party modules. --check prints, per take, the largest rotation range per bone of OUT.
Refuses (exit 2) when a DEST curve has no SRC match or the bind poses differ by more than 1e-3.
"""
import array
import struct
import sys
import zlib


def read_node(buf, pos):
    end, nprops, plen = struct.unpack_from('<III', buf, pos)
    nl = buf[pos + 12]
    if end == 0:
        return None, pos + 13
    name = buf[pos + 13:pos + 13 + nl].decode('ascii')
    p = pos + 13 + nl
    props = []  # (type byte, raw bytes including the type byte)
    for _ in range(nprops):
        t = buf[p:p + 1]
        q = p + 1
        if t in (b'S', b'R'):
            n = struct.unpack_from('<I', buf, q)[0]
            q += 4 + n
        elif t in b'YCIFDL':
            q += {b'Y': 2, b'C': 1, b'I': 4, b'F': 4, b'D': 8, b'L': 8}[t]
        else:
            n, enc, cl = struct.unpack_from('<III', buf, q)
            q += 12 + cl
        props.append(bytes(buf[p:q]))
        p = q
    kids = []
    has_null = False
    while p < end:
        k, p2 = read_node(buf, p)
        if k is None:
            has_null = True
            p = p2
            break
        kids.append(k)
        p = p2
    return [name, props, kids, has_null], end


def write_node(out, node):
    name, props, kids, has_null = node
    start = len(out)
    out += b'\0' * 12
    out.append(len(name))
    out += name.encode('ascii')
    plen = 0
    for r in props:
        out += r
        plen += len(r)
    for k in kids:
        write_node(out, k)
    if kids or has_null:
        out += b'\0' * 13
    struct.pack_into('<III', out, start, len(out), len(props), plen)


def pval(raw):
    t = raw[0:1]
    if t in (b'S', b'R'):
        n = struct.unpack_from('<I', raw, 1)[0]
        v = raw[5:5 + n]
        return v.decode('utf8', 'replace') if t == b'S' else v
    if t in b'YCIFDL':
        fmt = {b'Y': '<h', b'C': '<?', b'I': '<i', b'F': '<f', b'D': '<d', b'L': '<q'}[t]
        return struct.unpack_from(fmt, raw, 1)[0]
    n, enc, cl = struct.unpack_from('<III', raw, 1)
    data = raw[13:13 + cl]
    if enc:
        data = zlib.decompress(data)
    a = array.array({b'f': 'f', b'd': 'd', b'l': 'q', b'i': 'i', b'b': 'b'}[t])
    a.frombytes(data)
    return list(a)


def load(path):
    buf = open(path, 'rb').read()
    ver = struct.unpack_from('<I', buf, 23)[0]
    if ver >= 7500:
        sys.exit('fbx_transplant_clips: FBX %d uses 64-bit offsets; only 7.x (< 7500) is supported' % ver)
    pos, top = 27, []
    while True:
        n, pos = read_node(buf, pos)
        if n is None:
            break
        top.append(n)
    return buf, ver, top, pos


def child(top, name):
    return next(n for n in top if n[0] == name)


def obj_name(o):
    return pval(o[1][1]).split('\x00')[0]


def index(top):
    objs = child(top, 'Objects')[2]
    byid = {pval(o[1][0]): o for o in objs}
    parents = {}
    for c in child(top, 'Connections')[2]:
        vals = [pval(r) for r in c[1]]
        parents.setdefault(vals[1], []).append((vals[2], vals[3] if len(vals) > 3 else None))
    curves = {}
    for cid, o in byid.items():
        if o[0] != 'AnimationCurve':
            continue
        for cn, axis in parents.get(cid, []):
            node = byid.get(cn)
            if node is None or node[0] != 'AnimationCurveNode':
                continue
            model = prop = layer = None
            for pid, pp in parents.get(cn, []):
                po = byid.get(pid)
                if po is None:
                    continue
                if po[0] == 'Model':
                    model, prop = obj_name(po), pp
                elif po[0] == 'AnimationLayer':
                    layer = obj_name(po)
            curves[(layer, model, prop, axis)] = o
    binds = {}
    for o in objs:
        if o[0] != 'Pose':
            continue
        for pn in o[2]:
            if pn[0] != 'PoseNode':
                continue
            nid = pval(next(k for k in pn[2] if k[0] == 'Node')[1][0])
            mat = pval(next(k for k in pn[2] if k[0] == 'Matrix')[1][0])
            if nid in byid:
                binds.setdefault(obj_name(byid[nid]), mat)
    return curves, binds


def ranges(curves):
    out = {}
    for (layer, model, prop, axis), o in curves.items():
        vals = pval(next(k for k in o[2] if k[0] == 'KeyValueFloat')[1][0])
        if prop and 'Rotation' in prop:
            r = max(vals) - min(vals) if vals else 0.0
            d = out.setdefault(layer, {})
            d[model] = max(d.get(model, 0.0), r)
    return out


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 64
    src_p, dst_p, out_p = argv[:3]
    _, _, src_top, _ = load(src_p)
    dbuf, ver, dst_top, tail = load(dst_p)
    s_curves, s_binds = index(src_top)
    d_curves, d_binds = index(dst_top)
    for bone, m in d_binds.items():
        if bone in s_binds and max(abs(a - b) for a, b in zip(m, s_binds[bone])) > 1e-3:
            print('fbx_transplant_clips: bind pose of %s differs; refusing' % bone)
            return 2
    missing = [k for k in d_curves if k not in s_curves]
    if missing:
        print('fbx_transplant_clips: %d DEST curves have no SRC match, e.g. %s' % (len(missing), missing[:3]))
        return 2
    for k, o in d_curves.items():
        o[2] = [list(c) for c in s_curves[k][2]]   # KeyTime, KeyValueFloat, KeyAttr*: the source's, same object id
    out = bytearray(dbuf[:27])
    for n in dst_top:
        write_node(out, n)
    out += b'\0' * 13
    # Footer as Blender writes it: id, 4 zeros, pad to 16 (16 when aligned), version, 120 zeros, magic.
    out += bytes.fromhex('fabcab09d0c8d466b176fb831cf7267e') + b'\0' * 4
    pad = ((len(out) + 15) & ~15) - len(out)
    out += b'\0' * (pad if pad else 16)
    out += struct.pack('<I', ver) + b'\0' * 120 + bytes.fromhex('f85a8c6adef5d97eece90ce3758f290b')
    open(out_p, 'wb').write(bytes(out))
    print('fbx_transplant_clips: %d curves replaced, %s -> %s (%d bytes)' % (len(d_curves), src_p, out_p, len(out)))
    if '--check' in argv:
        o_curves, o_binds = index(load(out_p)[2])
        assert o_binds == d_binds, 'bind poses changed'
        for layer, d in sorted(ranges(o_curves).items()):
            top3 = sorted(d.items(), key=lambda x: -x[1])[:4]
            print('  %-10s %s' % (layer, ', '.join('%s %.1f' % t for t in top3)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
