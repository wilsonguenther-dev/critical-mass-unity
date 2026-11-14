# rig-method-2 (2026-11-14): read-only inventory of the image-to-3D GLB outputs (no Blender).
# Usage: python3 -I glb_inventory.py OUT.json FILE.glb...
# Per file: triangles, vertices, welded connected parts, size, skins/joints/morphs, textures or vertex colour,
# and a hand probe: horizontal slices from each hand's lowest point upward, counting separate cross-section loops
# inside the hand region (5 loops = five separate fingers, 1-2 = mitt or fist).
import json, struct, sys, os
import numpy as np

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}


def load(path):
    data = open(path, 'rb').read()
    n = struct.unpack('<I', data[12:16])[0]
    j = json.loads(data[20:20 + n])
    off = 20 + n
    binlen = struct.unpack('<I', data[off:off + 4])[0]
    bin_ = data[off + 8:off + 8 + binlen]

    def acc(i):
        a = j['accessors'][i]; bv = j['bufferViews'][a['bufferView']]
        o = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
        dt = np.dtype(CT[a['componentType']]); k = NC[a['type']]
        stride = bv.get('byteStride', 0)
        if stride and stride != dt.itemsize * k:
            raw = np.frombuffer(bin_, np.uint8, count=stride * a['count'], offset=o).reshape(a['count'], stride)
            return raw[:, :dt.itemsize * k].copy().view(dt).reshape(a['count'], k)
        return np.frombuffer(bin_, dt, count=a['count'] * k, offset=o).reshape(a['count'], k)
    return j, acc


def weld(P, tri, q=1e-5):
    key = np.round(P / q).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True)
    return inv.ravel()[tri]


def components(nv, tri):
    try:
        from scipy.sparse import coo_matrix
        from scipy.sparse.csgraph import connected_components
        e = np.vstack([tri[:, [0, 1]], tri[:, [1, 2]]])
        g = coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(nv, nv))
        _, lab = connected_components(g, directed=False)
        used = np.unique(tri)
        _, c = np.unique(lab[used], return_counts=True)
        return lab, sorted(c.tolist(), reverse=True)
    except ImportError:
        pass
    parent = np.arange(nv)
    def find(x):
        r = x
        while parent[r] != r: r = parent[r]
        while parent[x] != r: parent[x], x = r, parent[x]
        return r
    e = np.vstack([tri[:, [0, 1]], tri[:, [1, 2]]])
    for a, b in e:
        ra, rb = find(a), find(b)
        if ra != rb: parent[ra] = rb
    roots = np.array([find(i) for i in range(nv)])
    used = np.unique(tri)
    r = roots[used]
    u, c = np.unique(r, return_counts=True)
    return roots, sorted(c.tolist(), reverse=True)


def slice_loops(P, tri, y, mask_v):
    """Count connected loops of the plane y=const restricted to triangles whose verts are all in mask_v."""
    t = tri[mask_v[tri].all(1)]
    s = P[t, 1] - y
    cross = (np.sign(s[:, 0]) != np.sign(s[:, 1])) | (np.sign(s[:, 1]) != np.sign(s[:, 2]))
    t = t[cross]; s = s[cross]
    if len(t) == 0: return 0, []
    keys = {}
    segs = []
    for tr, ss in zip(t, s):
        ek = []
        for a, b, sa, sb in ((tr[0], tr[1], ss[0], ss[1]), (tr[1], tr[2], ss[1], ss[2]), (tr[2], tr[0], ss[2], ss[0])):
            if (sa < 0) != (sb < 0):
                ek.append((min(a, b), max(a, b)))
        if len(ek) == 2:
            segs.append([keys.setdefault(ek[0], len(keys)), keys.setdefault(ek[1], len(keys))])
    if not segs: return 0, []
    par = list(range(len(keys)))
    def f(x):
        while par[x] != x:
            par[x] = par[par[x]]; x = par[x]
        return x
    for a, b in segs:
        ra, rb = f(a), f(b)
        if ra != rb: par[ra] = rb
    from collections import Counter
    c = Counter(f(a) for a, _ in segs)
    sizes = sorted(c.values(), reverse=True)
    big = [v for v in sizes if v >= 4]   # ignore 1-3 segment slivers
    return len(big), sizes[:8]


def hand_probe(P, tri, H, ymin, side):
    x = P[:, 0] * side
    band = (P[:, 1] > ymin + 0.28 * H) & (P[:, 1] < ymin + 0.50 * H)
    if not band.any(): return None
    i = np.argmax(np.where(band, x, -1e9))
    c = P[i]
    R = 0.10 * H
    reg = (np.linalg.norm(P - c, axis=1) < R) & (x > c[0] * side - 0.10 * H)
    tip = P[reg][np.argmin(P[reg][:, 1])]
    prof = []
    for k in range(1, 26):
        y = tip[1] + k * 0.003 * H
        n, sz = slice_loops(P, tri, y, reg)
        prof.append(n)
    p = np.array(prof)
    return {'hand_centre': np.round(c, 3).tolist(), 'tip': np.round(tip, 3).tolist(),
            'loops_profile_from_tip_up_0.3pctH_steps': prof,
            'max_loops': int(p.max()), 'loops_mode_upper_half': int(np.bincount(p[5:]).argmax())}


def main():
    out = {}
    for f in sys.argv[2:]:
        j, acc = load(f)
        P_all, T_all, colour = [], [], set()
        base = 0
        for m in j['meshes']:
            for pr in m['primitives']:
                P = acc(pr['attributes']['POSITION']).astype(np.float64)
                T = acc(pr['indices']).astype(np.int64).reshape(-1, 3) if 'indices' in pr else np.arange(len(P)).reshape(-1, 3)
                if 'COLOR_0' in pr['attributes']: colour.add('vertex colour')
                mat = j.get('materials', [{}])[pr['material']] if 'material' in pr else {}
                if mat.get('pbrMetallicRoughness', {}).get('baseColorTexture'): colour.add('baseColor texture')
                P_all.append(P); T_all.append(T + base); base += len(P)
        P = np.vstack(P_all); T = np.vstack(T_all)
        lo, hi = P.min(0), P.max(0)
        zup = (hi - lo)[2] > (hi - lo)[1] and 'kaiju' not in f.lower()
        if zup:  # the cloud outputs store Z as up, against the glTF Y-up rule
            P = np.c_[P[:, 0], P[:, 2], -P[:, 1]]; lo, hi = P.min(0), P.max(0)
        H = hi[1] - lo[1]
        Tw = weld(P, T)
        nvw = int(Tw.max()) + 1
        # positions per welded index
        Pw = np.zeros((nvw, 3)); Pw[Tw.ravel()] = P[T.ravel()]
        roots, comp = components(nvw, Tw)
        rec = {'triangles': int(len(T)), 'vertices': int(len(P)), 'welded_vertices': nvw,
               'parts_total': len(comp), 'parts_over_1pct_tris': sum(1 for c in comp if c > 0.01 * nvw),
               'largest_parts_vertices': comp[:6], 'size_xyz_gltf_yup': np.round(hi - lo, 3).tolist(),
               'skins': len(j.get('skins', [])), 'joints': sum(len(s['joints']) for s in j.get('skins', [])),
               'morph_targets': sum(len(p.get('targets', [])) for m in j['meshes'] for p in m['primitives']),
               'animations': len(j.get('animations', [])), 'stored_z_up': bool(zup), 'colour': sorted(colour) or ['none']}
        if 'kaiju' not in f.lower() and 'body' in f.lower():
            rec['hand_L_plusX'] = hand_probe(Pw, Tw, H, lo[1], +1)
            rec['hand_R_minusX'] = hand_probe(Pw, Tw, H, lo[1], -1)
        out[os.path.relpath(f, os.path.expanduser('~/Desktop/CM-3D-outputs'))] = rec
        print(os.path.basename(f), json.dumps({k: rec[k] for k in ('triangles', 'parts_total', 'parts_over_1pct_tris', 'size_xyz_gltf_yup', 'colour')}),
              *[(k, rec[k]['max_loops'], rec[k]['loops_profile_from_tip_up_0.3pctH_steps']) for k in ('hand_L_plusX', 'hand_R_minusX') if rec.get(k)], flush=True)
    json.dump(out, open(sys.argv[1], 'w'), indent=1)


if __name__ == '__main__':
    main()
