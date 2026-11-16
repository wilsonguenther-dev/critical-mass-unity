# b12-i (2026-11-16): the Builder after the Air look of build 36a581c (Medium, gameplay camera) failed:
#   (1) team-colour lines and patches all over the INSIDE of the body: CM/Toon's hard rim (1 - N.V > 0.75, team colour)
#       lights every steep facet of the flat-shaded LOD0/LOD1 (rig-builder-4 exported one normal per face), and the Hero's
#       file normals point against the winding (3,518 corners, 1,402 by more than 60 deg). The outline hull is Void Navy and
#       already uses smoothed per-position directions (WorldMeshBake.SmoothOutlineTangents): it is not the cause.
#   (2) a stretched teal sheet from the raised right glove to the hip on the attack: the octree shell fused surfaces that
#       touch in the bind pose (glove at the hip, inner arm at the torso side, thighs under the apron); when the limbs move
#       apart, the bridge faces between vertices of different limbs (fused(): forearm/glove to belly or hip, inner arm to
#       hips, thigh to apron, thigh to thigh) stretch into a sheet.
# What this does, per level (Hero, LOD0, LOD1, Shadow), measured over every frame of Attack, Run and Walk (Idle every 4th):
#   stretch: edges whose posed length > SEAM_RATIO x rest and > SEAM_MIN_M metres, between vertices whose dominant bones are
#   on different limbs away from that limb's own joint (fused(): a bridge, not a joint) -> the faces on them are cut, each new hole is capped from its
#   own rim (weights, colours and UVs from the neighbouring corners), twice; then normals are rebuilt from the winding,
#   smooth inside SMOOTH_DEG and split at sharper edges (no corner normal can point against its face; thin slabs keep a
#   side each). Exports <out>/<Cls>.fbx with face_builder.py's settings, then renders BEFORE (the file as given) and AFTER
#   with a CM/Toon look-alike (vertex colour, code-8 team swap, two light bands, the hard team rim, back faces culled, the
#   inverted hull in Void Navy) from the gameplay camera (55 deg pitch) and a low side camera (10 deg), idle, run
#   mid-stride, attack raise, attack contact, both teams. Then face_rule_check.py and cull_check.py on the new file.
#   blender -b -P builder_seams.py -- <out> <model.fbx> <Cls> <clips.fbx>
import bpy, bmesh, sys, os, json, math, runpy, traceback
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index('--') + 1:]
OUT, MODEL, CLS, CLIPS = argv[0], argv[1], argv[2], argv[3]
os.makedirs(OUT, exist_ok=True)
RATIO = float(os.environ.get('SEAM_RATIO', '2.2'))
MINLEN = float(os.environ.get('SEAM_MIN_M', '0.05'))
SMOOTH_DEG = float(os.environ.get('SMOOTH_DEG', '50'))
JOINT_RATIO = float(os.environ.get('SEAM_JOINT_RATIO', '3.5'))
# b12-i run 3 (GitHub run 43946784954): cutting and capping the bridges left a dark hole at the raised right arm on the
# Hero and LOD0 (open edges 86 -> 144 / 104): a see-through regression, so the cut is OFF by default. ON by default: the
# limb weight clean-up (not yet run), the team-speck clean-up and the normals (both proven better in the run-3 renders).
CUT = os.environ.get('SEAM_CUT', '0') == '1'
# b12-i run 4 (43947773193): the weight clean-up alone made the welded bridges stretch further (LOD0 worst 15.3x -> 17.9x):
# the sheet is geometry welded across limbs, not only weights. SPLIT (default on): every face on a welded edge goes wholly
# to one limb; the vertices it shares with another limb are duplicated with that limb's weights, so no face spans two
# limbs; each new rim is one limb's loop and is capped (closed: no see-through). A level that would break its triangle
# contract or keep an uncapped rim is restored unsplit and reported.
SPLIT = os.environ.get('SEAM_SPLIT', '1') == '1'
WEIGHTS = os.environ.get('SEAM_WEIGHTS', '1') == '1'   # armpit webbing on the shoulder / hip pairs
SHADOW_MAX, LOD1_MAX, LOD0_MAX = 800, 1500, 4500
LEVELS = ('Hero', 'LOD0', 'LOD1', 'Shadow')
REP = {'params': dict(ratio=RATIO, joint_ratio=JOINT_RATIO, min_m=MINLEN, smooth_deg=SMOOTH_DEG, cut=CUT, weights=WEIGHTS, split=SPLIT)}


def save():
    json.dump(REP, open(os.path.join(OUT, f'seams_{CLS}.json'), 'w'), indent=1, default=str)


def imp(path, anim):
    bo, ba = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=anim, use_custom_normals=True, ignore_leaf_bones=False,
                             automatic_bone_orientation=False, primary_bone_axis='Y', secondary_bone_axis='X')
    return [o for o in bpy.data.objects if o not in bo], [a for a in bpy.data.actions if a not in ba]


def assign(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    if act is not None:
        try:
            if ad.action_slot is None and len(act.slots):
                ad.action_slot = act.slots[0]
        except Exception:
            pass


bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
mob, _ = imp(MODEL, False)
rig = next(o for o in mob if o.type == 'ARMATURE')
LV = {lv: next(o for o in mob if o.type == 'MESH' and o.name.startswith(f'{CLS}_{lv}')) for lv in LEVELS}
cob, cact = imp(CLIPS, True)
ca = next(o for o in cob if o.type == 'ARMATURE')
for o in cob:
    if o is not ca:
        bpy.data.objects.remove(o, do_unlink=True)
acts = {a.name.split('|')[-1]: a for a in cact}
REP['clips'] = sorted(acts)
for o in LV.values():
    if not any(m.type == 'ARMATURE' for m in o.modifiers):
        m = o.modifiers.new('Armature', 'ARMATURE'); m.object = rig
ntri = lambda o: sum(len(p.vertices) - 2 for p in o.data.polygons)
REP['tris_before'] = {lv: ntri(o) for lv, o in LV.items()}
REP['bones'] = [b.name for b in rig.data.bones]
FPS = sc.render.fps / max(1e-6, sc.render.fps_base)


def rest():
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix()
    bpy.context.view_layer.update()


def pose(act, f):
    assign(ca, act); sc.frame_set(int(f)); bpy.context.view_layer.update()
    for pb in rig.pose.bones:
        c = ca.pose.bones.get(pb.name); pb.matrix_basis = c.matrix_basis.copy() if c is not None else Matrix()
    bpy.context.view_layer.update()


def world_pos(o):
    oe = o.evaluated_get(bpy.context.evaluated_depsgraph_get())
    n = len(oe.data.vertices); b = np.empty(n * 3, np.float32); oe.data.vertices.foreach_get('co', b)
    M = np.array(o.matrix_world); return b.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]


def frames():
    out = []
    for c in ('Attack', 'Run', 'Walk', 'Idle'):
        a = acts.get(c)
        if a is None:
            continue
        f0, f1 = int(a.frame_range[0]), int(a.frame_range[1])
        out += [(c, a, f) for f in range(f0, f1 + 1, 4 if c == 'Idle' else 1)]
    return out


FR = frames()
BONES = rig.data.bones


def anc(n):
    out = []; b = BONES.get(n)
    while b is not None:
        out.append(b.name); b = b.parent
    return out


ANC = {b.name: anc(b.name) for b in BONES}


def tdist(a, b):
    if a == b:
        return 0
    A, B = ANC.get(a), ANC.get(b)
    if not A or not B:
        return 9
    for i, x in enumerate(A):
        if x in B:
            return i + B.index(x)
    return 9


ARM = ('upperarm', 'lowerarm', 'wrist', 'hand', 'handslot')
LEG = ('upperleg', 'lowerleg', 'foot', 'toes')


def limb(n):
    base, _, side = n.partition('.')
    if base in ARM:
        return 'arm.' + side
    if base in LEG:
        return 'leg.' + side
    return 'core'


def fused(a, b):
    # A fused bridge joins two different limbs, or a limb and the body away from that limb's own joint: the forearm or the
    # glove glued to the belly or the hip, the inner arm to the hips, a thigh to the apron (chest), thigh to thigh. Not
    # fused: same limb (elbow, knee, wrist folds), body to body (chest / hips across the spine: torso skin), the shoulder
    # (chest / upperarm) and the hip joint (hips or spine / upperleg).
    if not a or not b:
        return False
    la, lb = limb(a), limb(b)
    if la == lb:
        return False
    if 'core' in (la, lb):
        c, l = (a, b) if la == 'core' else (b, a)
        lbase = l.partition('.')[0]
        if c == 'chest' and lbase == 'upperarm':
            return False
        if c in ('hips', 'spine') and lbase == 'upperleg':
            return False
    return True


def joint_pair(a, b):
    # the shoulder (chest / upperarm) and the hip joint (hips or spine / upperleg): real skin stretches there, so only an
    # edge past JOINT_RATIO counts as a fused bridge (the inner arm glued to the torso side, the webbing under a raised arm)
    if not a or not b:
        return False
    x = {a.partition('.')[0], b.partition('.')[0]}
    return x == {'chest', 'upperarm'} or x == {'hips', 'upperleg'} or x == {'spine', 'upperleg'}


def dominant(o):
    names = {g.index: g.name for g in o.vertex_groups}
    dom = []
    for v in o.data.vertices:
        best, bw = '', 0.0
        for g in v.groups:
            nm = names.get(g.group)
            if g.weight > bw and nm in ANC:
                best, bw = nm, g.weight
        dom.append(best)
    return dom


def edges_of(o):
    me = o.data; e = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', e); return e.reshape(-1, 2)


def stretch(objs):
    rest()
    E = {o.name: edges_of(o) for o in objs}
    R = {o.name: world_pos(o) for o in objs}
    L0 = {k: np.linalg.norm(R[k][E[k][:, 0]] - R[k][E[k][:, 1]], axis=1) for k in R}
    best = {k: np.zeros(len(E[k])) for k in R}; where = {k: np.full(len(E[k]), -1) for k in R}
    for i, (c, a, f) in enumerate(FR):
        pose(a, f)
        for o in objs:
            k = o.name; P = world_pos(o)
            L = np.linalg.norm(P[E[k][:, 0]] - P[E[k][:, 1]], axis=1)
            r = np.where(L > MINLEN, L / np.maximum(L0[k], 1e-5), 0.0)
            m = r > best[k]; best[k][m] = r[m]; where[k][m] = i
    rest()
    out = {}
    for o in objs:
        k = o.name; dom = dominant(o); e = E[k]
        far = np.array([fused(dom[a], dom[b]) for a, b in e], bool) if len(e) else np.zeros(0, bool)
        jpair = np.array([joint_pair(dom[a], dom[b]) for a, b in e], bool) if len(e) else np.zeros(0, bool)
        flag = ((best[k] > RATIO) & far) | ((best[k] > JOINT_RATIO) & jpair)
        pairs, clips = {}, {}
        for i in np.nonzero(flag)[0]:
            a, b = e[i]; pk = ' / '.join(sorted((dom[a] or '-', dom[b] or '-'))); pairs[pk] = pairs.get(pk, 0) + 1
            cl = FR[where[k][i]][0]; clips[cl] = clips.get(cl, 0) + 1
        joint = (best[k] > RATIO) & ~flag
        jp = {}
        for i in np.nonzero(joint)[0]:
            a, b = e[i]; pk = ' / '.join(sorted((dom[a] or '-', dom[b] or '-'))); jp[pk] = jp.get(pk, 0) + 1
        out[k] = dict(flag=np.nonzero(flag)[0], summary=dict(
            fused_edges_over=int(flag.sum()), worst_ratio=round(float(best[k][flag].max()) if flag.any() else 0.0, 2),
            joint_edges_over=int(joint.sum()), worst_joint_ratio=round(float(best[k][joint].max()) if joint.any() else 0.0, 2),
            by_clip=clips, bone_pairs=dict(sorted(pairs.items(), key=lambda t: -t[1])[:12]),
            joint_pairs=dict(sorted(jp.items(), key=lambda t: -t[1])[:8])))
    return out


def clear_custom(o):
    me = o.data
    try:
        if 'custom_normal' in me.attributes:
            me.attributes.remove(me.attributes['custom_normal'])
    except Exception as ex:
        REP.setdefault('custom_clear_errors', []).append(str(ex))
    if getattr(me, 'has_custom_normals', False):
        try:
            with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o], selected_editable_objects=[o]):
                bpy.ops.mesh.customdata_custom_splitnormals_clear()
        except Exception as ex:
            REP.setdefault('custom_clear_errors', []).append(str(ex))


def cut_and_cap(o, flag_idx, cap=True):
    me = o.data; bm = bmesh.new(); bm.from_mesh(me)
    bm.edges.ensure_lookup_table()
    old_bnd = {frozenset(e.verts) for e in bm.edges if len(e.link_faces) == 1}
    faces = {f for i in flag_idx for f in bm.edges[int(i)].link_faces}
    nf = len(faces)
    if faces:
        bmesh.ops.delete(bm, geom=list(faces), context='FACES_ONLY')
        wire = [e for e in bm.edges if not e.link_faces]
        if wire:
            bmesh.ops.delete(bm, geom=wire, context='EDGES')
        loose = [v for v in bm.verts if not v.link_edges]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context='VERTS')
    newb = [e for e in bm.edges if len(e.link_faces) == 1 and frozenset(e.verts) not in old_bnd]
    capped = 0
    if cap and newb:
        res = bmesh.ops.holes_fill(bm, edges=newb, sides=0)
        new = [f for f in res.get('faces', []) if f.is_valid]
        newset = set(new)
        for f in new:   # wind each cap like the face across its rim edge
            for e in f.edges:
                g = next((x for x in e.link_faces if x not in newset), None)
                if g is None:
                    continue
                d = lambda face: next(((l.vert, l.link_loop_next.vert) for l in face.loops if l.edge == e), None)
                if d(f) == d(g):
                    f.normal_flip()
                break
        if new:
            new = bmesh.ops.triangulate(bm, faces=new, quad_method='BEAUTY', ngon_method='BEAUTY')['faces']
        newset = set(new)
        lays = list(bm.loops.layers.color) + list(bm.loops.layers.float_color)
        uvs = list(bm.loops.layers.uv)
        for f in new:
            src = None
            for l in f.loops:
                l2 = next((x for x in l.vert.link_loops if x.face not in newset), None)
                if l2 is None:
                    continue
                src = src or l2.face
                for lay in lays:
                    l[lay] = l2[lay]
                for uv in uvs:
                    l[uv].uv = l2[uv].uv
            if src is not None:
                f.material_index = src.material_index
        capped = len(new)
    bm.to_mesh(me); me.update(); bm.free()
    return dict(faces_cut=nf, cap_tris=capped, new_rim_edges=len(newb))


def smooth_by_angle(o, deg):
    clear_custom(o)
    me = o.data; bm = bmesh.new(); bm.from_mesh(me); thr = math.radians(deg)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        e.smooth = len(e.link_faces) == 2 and e.calc_face_angle(math.pi) < thr
    bm.to_mesh(me); me.update(); bm.free()


def normal_stats(o):
    me = o.data; nl = len(me.loops); cn = np.empty(nl * 3, np.float32)
    try:
        me.corner_normals.foreach_get('vector', cn)
    except Exception:
        me.calc_normals_split(); me.loops.foreach_get('normal', cn)
    cn = cn.reshape(-1, 3); npoly = len(me.polygons)
    pn = np.empty(npoly * 3, np.float32); me.polygons.foreach_get('normal', pn); pn = pn.reshape(-1, 3)
    ls = np.empty(npoly, np.int64); me.polygons.foreach_get('loop_start', ls)
    lt = np.empty(npoly, np.int64); me.polygons.foreach_get('loop_total', lt)
    owner = np.empty(nl, np.int64)
    for i in range(npoly):
        owner[ls[i]:ls[i] + lt[i]] = i
    d = (cn * pn[owner]).sum(1)
    return dict(corners=nl, against_face=int((d < 0).sum()), over60=int((d < 0.5).sum()), dot_mean=round(float(d.mean()), 4),
                custom=bool(getattr(me, 'has_custom_normals', False)))


def copy_obj(o, name):
    c = o.copy(); c.data = o.data.copy(); c.name = name; sc.collection.objects.link(c); return c


# ---- BEFORE: copies of the file as given (for the renders), stats
BEFORE = {lv: copy_obj(LV[lv], f'{LV[lv].name}_before') for lv in ('Hero', 'LOD0', 'LOD1')}
REP['normals_before'] = {lv: normal_stats(o) for lv, o in LV.items()}
st = stretch(list(LV.values()))
REP['stretch_before'] = {lv: st[o.name]['summary'] for lv, o in LV.items()}
save()
print('[seams] before', json.dumps(REP['stretch_before']), flush=True)

# ---- FIX 1: limb-exclusive weights. Run 2/3 measured stretched edges between vertices of the SAME dominant bone
# (LOD0: hand.r / hand.r 7, chest / chest 10): partial weights bled across limbs by the voxel proxy (a glove vertex partly on
# the chest or hips stays behind when the hand rises; a belly vertex partly on the forearm is dragged up with it). Each
# vertex keeps only the bones of its dominant limb, plus the joint it sits at: an arm vertex keeps the chest only when its
# top arm bone is upperarm (the shoulder), a leg vertex keeps hips/spine only at upperleg (the hip), a body vertex keeps
# upperarm and upperleg (shoulder and hip) and drops forearm, wrist, hand, shin, foot and toes. Then renormalised.
def limb_weights(o):
    names = {g.index: g.name for g in o.vertex_groups}
    changed = 0
    for v in o.data.vertices:
        ws = [(names.get(g.group), g.weight, g.group) for g in v.groups if names.get(g.group) in ANC and g.weight > 0]
        if len(ws) < 2:
            continue
        tot = {}
        for n, w, _ in ws:
            tot[limb(n)] = tot.get(limb(n), 0.0) + w
        L = max(tot, key=tot.get)
        own = [x for x in ws if limb(x[0]) == L]
        top = max(own, key=lambda x: x[1])[0].partition('.')[0]
        keep = []
        for n, w, gi in ws:
            base = n.partition('.')[0]
            ok = limb(n) == L
            if not ok and L == 'core':
                ok = base in ('upperarm', 'upperleg')
            elif not ok and L.startswith('arm'):
                ok = n == 'chest' and top == 'upperarm'
            elif not ok and L.startswith('leg'):
                ok = n in ('hips', 'spine') and top == 'upperleg'
            keep.append(ok)
        if all(keep):
            continue
        tw = sum(w for (n, w, gi), k in zip(ws, keep) if k)
        for (n, w, gi), k in zip(ws, keep):
            if k:
                o.vertex_groups[gi].add([v.index], w / tw, 'REPLACE')
            else:
                o.vertex_groups[gi].remove([v.index])
        changed += 1
    return changed


if WEIGHTS:
    REP['weights_cleaned_vertices'] = {lv: limb_weights(o) for lv, o in LV.items()}
    st = stretch(list(LV.values()))
    REP['stretch_after_weights'] = {lv: st[o.name]['summary'] for lv, o in LV.items()}
    REP['stretch_after'] = REP['stretch_after_weights']
    save()

# ---- FIX 2: split the welded faces by limb and cap each side
def vertex_limbs(o):
    names = {g.index: g.name for g in o.vertex_groups}
    out = []
    for v in o.data.vertices:
        tot = {}
        for g in v.groups:
            n = names.get(g.group)
            if n in ANC:
                tot[limb(n)] = tot.get(limb(n), 0.0) + g.weight
        out.append(max(tot, key=tot.get) if tot else 'core')
    return out


def static_fused_edges(o):
    dom = dominant(o)
    return [i for i, (a, b) in enumerate(edges_of(o)) if fused(dom[a], dom[b])], dom


def split_by_limb(o, extra):
    me = o.data
    fe, dom = static_fused_edges(o)
    vl = vertex_limbs(o)
    bridge = set(fe) | {int(i) for i in extra}
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table()
    dl = bm.verts.layers.deform.active
    vidx = {v: v.index for v in bm.verts}
    old_bnd = {frozenset(e.verts) for e in bm.edges if len(e.link_faces) == 1}
    faces = {f for i in bridge for f in bm.edges[i].link_faces}
    rep_ = dict(welded_edges=len(fe), bridge_edges=len(bridge), faces_split=len(faces))
    if not faces:
        bm.free(); return rep_
    lays = list(bm.loops.layers.color) + list(bm.loops.layers.float_color); uvs = list(bm.loops.layers.uv)
    recs = []
    for f in faces:
        cnt = {}
        for l in f.loops:
            k = vl[vidx[l.vert]]; cnt[k] = cnt.get(k, 0) + 1
        S = max(sorted(cnt), key=lambda k: (cnt[k], k == 'core'))
        recs.append((S, [(l.vert, [tuple(l[x]) for x in lays], [tuple(l[u].uv) for u in uvs]) for l in f.loops],
                     f.material_index, f.smooth))
    bmesh.ops.delete(bm, geom=list(faces), context='FACES_ONLY')
    dup, lim, domb = {}, {}, {}
    for v, i in vidx.items():
        lim[v] = vl[i]; domb[v] = dom[i]

    def vert_for(v, S, fv):
        if lim[v] == S:
            return v
        key = (vidx[v], S)
        if key not in dup:
            src = min((u for u in fv if lim[u] == S), key=lambda u: (u.co - v.co).length_squared)
            nv = bm.verts.new(v.co)
            for kk, w in src[dl].items():
                nv[dl][kk] = w
            dup[key] = nv; lim[nv] = S; domb[nv] = domb[src]
        return dup[key]

    made = 0; rebuilt_side = {}
    for S, data, mi, sm in recs:
        fv = [d[0] for d in data]
        nvs = [vert_for(v, S, fv) for v in fv]
        if len(set(nvs)) < 3:
            continue
        try:
            nf = bm.faces.new(nvs)
        except ValueError:
            continue
        nf.material_index = mi; nf.smooth = sm; made += 1; rebuilt_side[nf] = S
        for l, (v, cols, uvv) in zip(nf.loops, data):
            for x, c in zip(lays, cols):
                l[x] = c
            for u, t in zip(uvs, uvv):
                l[u].uv = t
    wire = [e for e in bm.edges if not e.link_faces]
    if wire:
        bmesh.ops.delete(bm, geom=wire, context='EDGES')
    loose = [v for v in bm.verts if not v.link_edges]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    newb = [e for e in bm.edges if len(e.link_faces) == 1 and frozenset(e.verts) not in old_bnd]
    # caps: run 5 showed the rims are not closed loops (they end where the weld meets the shoulder or hip skin, which
    # stays joined), so the generic hole fill closed almost nothing. Each limb's rim edges are walked into chains; a closed
    # chain is fan-filled, an open chain is fan-filled together with its closing edge (its two ends), which the other
    # side's cap shares, so the surface stays closed.
    def face_side(f):
        if f in rebuilt_side:
            return rebuilt_side[f]
        cnt = {}
        for l in f.loops:
            k = lim.get(l.vert, 'core'); cnt[k] = cnt.get(k, 0) + 1
        return max(sorted(cnt), key=lambda k: (cnt[k], k == 'core'))

    bys = {}
    for e in newb:
        bys.setdefault(face_side(e.link_faces[0]), []).append(e)
    caps, open_chains, mixed = [], 0, 0
    for S, es in sorted(bys.items()):
        adj = {}
        for e in es:
            v0, v1 = e.verts
            adj.setdefault(v0, []).append((v1, e)); adj.setdefault(v1, []).append((v0, e))
        used = set()
        starts = [v for v, n in adj.items() if len(n) % 2 == 1] + list(adj)
        for st0 in starts:
            while any(e not in used for _, e in adj[st0]):
                chain, cur = [st0], st0
                while True:
                    step = next(((w, e) for w, e in adj[cur] if e not in used), None)
                    if step is None:
                        break
                    w, e = step; used.add(e); chain.append(w); cur = w
                    if w is st0:
                        break
                if chain[-1] is chain[0]:
                    chain = chain[:-1]
                else:
                    open_chains += 1
                if len(chain) < 3:
                    continue
                p0 = chain[0]
                for i in range(1, len(chain) - 1):
                    tri = (p0, chain[i], chain[i + 1])
                    if len(set(tri)) < 3:
                        continue
                    try:
                        caps.append(bm.faces.new(tri))
                    except ValueError:
                        pass
    # second chance for whatever rim the chains left open (ends that did not meet the other side's closing edge)
    rem = [e for e in bm.edges if e.is_valid and len(e.link_faces) == 1 and frozenset(e.verts) not in old_bnd]
    if rem:
        try:
            extra = [f for f in bmesh.ops.holes_fill(bm, edges=rem, sides=0).get('faces', []) if f.is_valid]
            if extra:
                extra = bmesh.ops.triangulate(bm, faces=extra, quad_method='BEAUTY', ngon_method='BEAUTY')['faces']
            caps += extra; rep_['second_fill_tris'] = len(extra)
        except Exception as ex:
            rep_['second_fill_error'] = str(ex)
    cs = set(caps)
    for f in caps:   # wind each cap like the face across its rim edge
        for e in f.edges:
            g = next((x for x in e.link_faces if x not in cs), None)
            if g is None:
                continue
            d = lambda face: next(((l.vert, l.link_loop_next.vert) for l in face.loops if l.edge == e), None)
            if d(f) == d(g):
                f.normal_flip()
            break
    for f in caps:
        vs = [l.vert for l in f.loops]
        if any(fused(domb.get(x, ''), domb.get(y, '')) for x in vs for y in vs if x is not y):
            mixed += 1
        src = None
        for l in f.loops:
            l2 = next((x for x in l.vert.link_loops if x.face not in cs), None)
            if l2 is None:
                continue
            src = src or l2.face
            for x in lays:
                l[x] = l2[x]
            for u in uvs:
                l[u].uv = l2[u].uv
        if src is not None:
            f.material_index = src.material_index; f.smooth = src.smooth
    rep_['cap_tris_across_limbs'] = mixed
    rep_['open_rim_chains'] = open_chains
    rep_.update(faces_rebuilt=made, vertices_duplicated=len(dup), new_rim_edges=len(newb), cap_tris=len(caps))
    rep_['open_edges_after'] = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    rep_['open_edges_before'] = len(old_bnd)
    bm.to_mesh(me); me.update(); bm.free()
    return rep_


if SPLIT:
    REP['split'] = {}
    LIMIT = {'LOD0': LOD0_MAX, 'LOD1': LOD1_MAX, 'Shadow': SHADOW_MAX, 'Hero': 10 ** 9}
    for lv, o in LV.items():
        keep = o.data.copy()
        r_ = split_by_limb(o, st[o.name]['flag'])
        r_['tris'] = ntri(o)
        if r_.get('open_edges_after', 0) > r_.get('open_edges_before', 0) or r_['tris'] > LIMIT[lv]:
            old = o.data; o.data = keep; bpy.data.meshes.remove(old); r_['restored_unsplit'] = True
        else:
            bpy.data.meshes.remove(keep)
        r_['welded_edges_left'] = len(static_fused_edges(o)[0])
        REP['split'][lv] = r_
    save()
    print('[seams] split', json.dumps(REP['split']), flush=True)
    st = stretch(list(LV.values()))
    REP['stretch_after_split'] = {lv: st[o.name]['summary'] for lv, o in LV.items()}
    REP['stretch_after'] = REP['stretch_after_split']
    save()

# ---- FIX 3 (SEAM_CUT=1 only): cut the fused bridges and cap, three rounds
REP['cut'] = {lv: [] for lv in LEVELS}
for rnd in range(3 if CUT else 0):
    for lv, o in LV.items():
        fl = st[o.name]['flag']
        if len(fl) == 0:
            continue
        cap = True
        REP['cut'][lv].append(cut_and_cap(o, fl, cap))
        if lv == 'Shadow' and ntri(o) > SHADOW_MAX:
            REP['cut'][lv].append('over the Shadow contract after capping')
    st = stretch(list(LV.values()))
    REP[f'stretch_after_{rnd + 1}'] = {lv: st[o.name]['summary'] for lv, o in LV.items()}
    save()
if CUT:
    REP['stretch_after'] = REP['stretch_after_3']


def despeckle(o, passes=2):
    # team-cloth specks: a code-8 face whose neighbours are at least two thirds fixed colour (code 0) takes the colour of
    # its largest fixed-colour neighbour (the one-colour-per-face LOD transfer dropped shirt triangles onto the apron,
    # the ladder, the boots and the thighs; at the gameplay camera they read as team lines inside the body)
    me = o.data; bm = bmesh.new(); bm.from_mesh(me)
    lay = bm.loops.layers.float_color.get(COLN) or bm.loops.layers.color.get(COLN)
    if lay is None:
        bm.free(); return 'no corner colour layer ' + COLN
    changed = 0
    for _ in range(passes):
        mean = {f: [sum(l[lay][i] for l in f.loops) / len(f.loops) for i in range(4)] for f in bm.faces}
        code = {f: int(round(mean[f][3] * 8)) for f in bm.faces}
        todo = []
        for f in bm.faces:
            if code[f] != 8:
                continue
            nb = [g for e in f.edges for g in e.link_faces if g is not f]
            other = [g for g in nb if code[g] == 0]
            if len(nb) >= 2 and len(other) * 3 >= len(nb) * 2:
                todo.append((f, max(other, key=lambda x: x.calc_area())))
        for f, g in todo:
            for l in f.loops:
                l[lay] = mean[g]
        changed += len(todo)
        if not todo:
            break
    bm.to_mesh(me); me.update(); bm.free()
    return changed


COLN = (LV['LOD0'].data.color_attributes.active_color or LV['LOD0'].data.color_attributes[0]).name
REP['colour_domain'] = LV['LOD0'].data.color_attributes[COLN].domain
REP['despeckled_faces'] = {lv: despeckle(o) for lv, o in LV.items()}
for lv, o in LV.items():
    smooth_by_angle(o, SMOOTH_DEG)
REP['normals_after'] = {lv: normal_stats(o) for lv, o in LV.items()}
REP['tris_after'] = {lv: ntri(o) for lv, o in LV.items()}
REP['contracts'] = dict(LOD0=REP['tris_after']['LOD0'] <= LOD0_MAX, LOD1=REP['tris_after']['LOD1'] <= LOD1_MAX,
                        Shadow=REP['tris_after']['Shadow'] <= SHADOW_MAX)
save()
print('[seams] after', json.dumps({k: REP.get(k) for k in ('stretch_after', 'weights_cleaned_vertices', 'despeckled_faces', 'normals_after', 'tris_after', 'contracts')}), flush=True)

# ---- export (rest pose, model only)
rest(); assign(ca, None)
for ob in sc.objects:
    ob.select_set(ob is rig or ob in LV.values())
bpy.context.view_layer.objects.active = rig
path = os.path.join(OUT, f'{CLS}.fbx')
r = bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'}, global_scale=1.0,
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type='FACE',
                             use_tspace=False, colors_type='SRGB', prioritize_active_color=True, add_leaf_bones=False,
                             primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
                             armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
                             use_custom_props=False)
REP['export'] = dict(path=path, result=list(r), bytes=os.path.getsize(path))
save()

# ---- renders: CM/Toon look-alike, before vs after
def lin(h):
    h = h.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]


AUTH = lin('#2A9DBA')
TEAMS = {'azure': lin('#2A9DBA'), 'crimson': lin('#E46753')}
NAVY = lin('#18233B')
COL = (LV['LOD0'].data.color_attributes.active_color or LV['LOD0'].data.color_attributes[0]).name
L = Vector((0.35, -0.5, 0.8)).normalized()


def sock(coll, ident):
    return next(s for s in coll if s.identifier == ident)


def toon_mat(name, team, rim=0.6):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; N = nt.nodes; K = nt.links.new; N.clear()
    out = N.new('ShaderNodeOutputMaterial'); vc = N.new('ShaderNodeVertexColor'); vc.layer_name = COL
    sep = N.new('ShaderNodeSeparateColor'); K(vc.outputs['Color'], sep.inputs[0])
    m1 = N.new('ShaderNodeMath'); m1.operation = 'MAXIMUM'; K(sep.outputs[0], m1.inputs[0]); K(sep.outputs[1], m1.inputs[1])
    m2 = N.new('ShaderNodeMath'); m2.operation = 'MAXIMUM'; K(m1.outputs[0], m2.inputs[0]); K(sep.outputs[2], m2.inputs[1])
    k = N.new('ShaderNodeMath'); k.operation = 'DIVIDE'; K(m2.outputs[0], k.inputs[0]); k.inputs[1].default_value = max(AUTH)
    sw = N.new('ShaderNodeVectorMath'); sw.operation = 'SCALE'; sw.inputs[0].default_value = TEAMS[team]; K(k.outputs[0], sock(sw.inputs, 'Scale'))
    is8 = N.new('ShaderNodeMath'); is8.operation = 'GREATER_THAN'; K(vc.outputs['Alpha'], is8.inputs[0]); is8.inputs[1].default_value = 0.94
    mx = N.new('ShaderNodeMix'); mx.data_type = 'RGBA'
    K(is8.outputs[0], sock(mx.inputs, 'Factor_Float')); K(vc.outputs['Color'], sock(mx.inputs, 'A_Color')); K(sw.outputs[0], sock(mx.inputs, 'B_Color'))
    geo = N.new('ShaderNodeNewGeometry')
    dt = N.new('ShaderNodeVectorMath'); dt.operation = 'DOT_PRODUCT'; K(geo.outputs['Normal'], dt.inputs[0]); dt.inputs[1].default_value = L
    bd = N.new('ShaderNodeMath'); bd.operation = 'GREATER_THAN'; K(sock(dt.outputs, 'Value'), bd.inputs[0]); bd.inputs[1].default_value = 0.0
    bv = N.new('ShaderNodeMath'); bv.operation = 'MULTIPLY_ADD'; K(bd.outputs[0], bv.inputs[0]); bv.inputs[1].default_value = 0.38; bv.inputs[2].default_value = 0.62
    lit = N.new('ShaderNodeVectorMath'); lit.operation = 'SCALE'; K(sock(mx.outputs, 'Result_Color'), lit.inputs[0]); K(bv.outputs[0], sock(lit.inputs, 'Scale'))
    lw = N.new('ShaderNodeLayerWeight'); lw.inputs['Blend'].default_value = 0.5
    rm = N.new('ShaderNodeMath'); rm.operation = 'GREATER_THAN'; K(lw.outputs['Facing'], rm.inputs[0]); rm.inputs[1].default_value = 0.75
    rs = N.new('ShaderNodeMath'); rs.operation = 'MULTIPLY'; K(rm.outputs[0], rs.inputs[0]); rs.inputs[1].default_value = rim
    m3 = N.new('ShaderNodeMix'); m3.data_type = 'RGBA'
    K(rs.outputs[0], sock(m3.inputs, 'Factor_Float')); K(lit.outputs[0], sock(m3.inputs, 'A_Color')); sock(m3.inputs, 'B_Color').default_value = (*TEAMS[team], 1.0)
    em = N.new('ShaderNodeEmission'); K(sock(m3.outputs, 'Result_Color'), em.inputs['Color'])
    tr = N.new('ShaderNodeBsdfTransparent'); ms = N.new('ShaderNodeMixShader')
    K(geo.outputs['Backfacing'], ms.inputs[0]); K(em.outputs[0], ms.inputs[1]); K(tr.outputs[0], ms.inputs[2]); K(ms.outputs[0], out.inputs['Surface'])
    return m


def hull_mat():
    m = bpy.data.materials.new('hull'); m.use_nodes = True; nt = m.node_tree; N = nt.nodes; K = nt.links.new; N.clear()
    out = N.new('ShaderNodeOutputMaterial'); geo = N.new('ShaderNodeNewGeometry'); em = N.new('ShaderNodeEmission')
    em.inputs['Color'].default_value = (*NAVY, 1.0); tr = N.new('ShaderNodeBsdfTransparent'); ms = N.new('ShaderNodeMixShader')
    K(geo.outputs['Backfacing'], ms.inputs[0]); K(em.outputs[0], ms.inputs[1]); K(tr.outputs[0], ms.inputs[2]); K(ms.outputs[0], out.inputs['Surface'])
    return m


try:
    MATS = {t: toon_mat('toon_' + t, t) for t in TEAMS}; HULL = hull_mat()
    MATS['crimson_norim'] = toon_mat('toon_crimson_norim', 'crimson', 0.0)
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 8; sc.cycles.use_denoising = False
    sc.cycles.max_bounces = 2; sc.cycles.transparent_max_bounces = 32; sc.cycles.diffuse_bounces = 0; sc.cycles.glossy_bounces = 0
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new('w'); sc.world = w
    try:
        w.use_nodes = True
        bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND'); bg.inputs[0].default_value = (0.55, 0.6, 0.65, 1.0)
    except Exception:
        w.color = (0.55, 0.6, 0.65)
    sc.render.resolution_x, sc.render.resolution_y = 260, 330; sc.render.resolution_percentage = 100
    cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = 3.3
    cam = bpy.data.objects.new('cam', cd); sc.collection.objects.link(cam); sc.camera = cam
    VIEWS = {'game55': (30.0, 55.0), 'front10': (-20.0, 10.0), 'side10': (90.0, 10.0)}   # (azimuth from the front, pitch); faces -Y
    LEVEL_VIEWS = {'LOD0': ('game55', 'front10', 'side10'), 'LOD1': ('game55',), 'Hero': ('game55', 'front10', 'side10')}
    AFTER = {lv: copy_obj(LV[lv], f'{LV[lv].name}_after') for lv in ('Hero', 'LOD0', 'LOD1')}
    RO = {}
    for tag, objs in (('before', BEFORE), ('after', AFTER)):
        for lv, o in objs.items():
            # the inverted hull: the same skinned surface, winding reversed, pushed 2 cm out along the averaged vertex
            # normals (the game's _OutlineSource 3 direction); its near side is back-facing (transparent), the far side
            # shows as the Void Navy line round the body
            h = copy_obj(o, o.name + '_hull'); hb_ = bmesh.new(); hb_.from_mesh(h.data)
            bmesh.ops.reverse_faces(hb_, faces=hb_.faces[:]); hb_.to_mesh(h.data); hb_.free()
            h.data.materials.clear(); h.data.materials.append(HULL)
            dm = h.modifiers.new('Hull', 'DISPLACE'); dm.direction = 'NORMAL'; dm.mid_level = 0.0; dm.strength = -0.02
            RO[(tag, lv)] = (o, h)
    for o in LV.values():
        o.hide_render = True
    # poses: idle, run mid-stride (feet furthest apart), attack raise (right hand highest), attack contact (0.567 s)
    POSES = [('idle', acts.get('Idle'), int(acts['Idle'].frame_range[0]) if 'Idle' in acts else 0)]
    if 'Run' in acts:
        a = acts['Run']; bestf, bd_ = int(a.frame_range[0]), -1.0
        for f in range(int(a.frame_range[0]), int(a.frame_range[1]) + 1):
            pose(a, f); pl, pr = rig.pose.bones.get('foot.l'), rig.pose.bones.get('foot.r')
            if pl and pr:
                d = ((rig.matrix_world @ pl.head) - (rig.matrix_world @ pr.head)).length
                if d > bd_:
                    bd_, bestf = d, f
        POSES.append(('run_mid', a, bestf))
    if 'Attack' in acts:
        a = acts['Attack']; f0, f1 = int(a.frame_range[0]), int(a.frame_range[1]); bestf, bz = f0, -9.0
        for f in range(f0, f1 + 1):
            pose(a, f); hb = rig.pose.bones.get('hand.r')
            if hb and (rig.matrix_world @ hb.head).z > bz:
                bz, bestf = (rig.matrix_world @ hb.head).z, f
        POSES.append(('attack_raise', a, bestf))
        POSES.append(('attack_contact', a, min(f1, f0 + int(round(0.567 * FPS)))))
    REP['render_poses'] = [(n, f) for n, a, f in POSES]
    save()

    def shot(p):
        sc.render.filepath = p; bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(p); px = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)
        bpy.data.images.remove(img); os.remove(p); return px

    tgt = Vector((0, 0, 1.05)); REP['renders'] = []
    ROWS = [('azure', 'before'), ('azure', 'after'), ('crimson', 'before'), ('crimson', 'after'), ('crimson_norim', 'after')]
    for lv in ('LOD0', 'LOD1', 'Hero'):
        for vname in LEVEL_VIEWS[lv]:
            az, pitch = VIEWS[vname]
            a_, p_ = math.radians(az), math.radians(pitch)
            loc = tgt + 8.0 * Vector((math.sin(a_) * math.cos(p_), -math.cos(a_) * math.cos(p_), math.sin(p_)))
            cam.location = loc; cam.rotation_euler = (tgt - loc).to_track_quat('-Z', 'Y').to_euler()
            rows = []
            for team, tag in ROWS:
                if True:
                    o, h = RO[(tag, lv)]
                    o.data.materials.clear(); o.data.materials.append(MATS[team])
                    for (t2, l2), (o2, h2) in RO.items():
                        vis = (t2, l2) == (tag, lv); o2.hide_render = not vis; h2.hide_render = not vis
                    row = []
                    for pn_, act, f in POSES:
                        if act is None:
                            rest()
                        else:
                            pose(act, f)
                        row.append(shot(os.path.join(OUT, f'_t_{lv}_{vname}.png')))
                    rows.append(np.concatenate(row, axis=1))
            g = np.concatenate(rows[::-1], axis=0)
            im = bpy.data.images.new('g', g.shape[1], g.shape[0]); im.pixels = g.ravel()
            p = os.path.join(OUT, f'{CLS}_{lv}_{vname}.png'); im.filepath_raw = p; im.file_format = 'PNG'; im.save()
            REP['renders'].append(p); save()
    REP['render_layout'] = 'rows top to bottom: azure before, azure after, crimson before, crimson after, crimson after with the rim off; columns: ' + ', '.join(n for n, a, f in POSES)
except Exception:
    REP['render_error'] = traceback.format_exc()
save()
print('[seams] renders', json.dumps({k: REP.get(k) for k in ('renders', 'render_layout', 'render_error')}), flush=True)

# ---- the existing checks on the new file
try:
    HERE = os.path.dirname(os.path.abspath(__file__))
except NameError:
    HERE = os.path.join(os.environ.get('GITHUB_WORKSPACE', '.'), 'Assets/_Game/Art/Characters/_source~/characters_hy/rig')
for script, args in (('face_rule_check.py', [os.path.join(OUT, 'rule'), path, f'{CLS}_Hero']),
                     ('cull_check.py', [os.path.join(OUT, 'cull'), path, CLS, CLIPS])):
    try:
        sys.argv = ['blender', '--'] + args
        runpy.run_path(os.path.join(HERE, script), run_name='__main__')
    except BaseException:
        print('[seams] check failed', script, traceback.format_exc()[-800:], flush=True)
print('[seams] done', flush=True)
