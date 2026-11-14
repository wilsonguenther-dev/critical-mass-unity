# rig-batch-b (2026-11-14): rig a cleaned generated body (holding FBX: <Cls>_Hero, _LOD0, _LOD1, _Shadow, static,
# origin at the feet, facing Blender -Y) on the shared clip-pack skeleton names, plus 15 finger bones per hand.
#   blender -b -P rig_body.py -- <out> <repo> <Cls> <holding.fbx> [fingers 1|0]
# Method (rig-method-2 + the rig agent's proven bake):
#   1. Joints found by geometry (landmarks.py); optional per-class override rigb_joints_<Cls>.json (any joint, metres).
#   2. Skeleton = the class's CURRENT game rig (KayKit bind, 41 bones, same names and hierarchy), every deform bone moved
#      onto the new body's joints, its roll carried over from the KayKit bind by the smallest turn (so a lowered arm keeps
#      KayKit's twist and the shared clips' full local rotations read the same). Hand slots keep KayKit's frame relative
#      to the hand (the game hangs the light weapon on handslot.r's +Y). Control and IK bones stay as they are.
#   3. Hero weights: bone heat on a watertight voxel proxy, transferred back; head top and a back pack held rigid;
#      fingers by distance to their own chains; the welded fingertips cut apart (faces that bridge two fingers past the
#      middle of the finger are removed); smoothed, 4 influences, normalised. LOD0, LOD1, Shadow take the Hero's weights.
#   4. Exports <out>/<Cls>.fbx (fighter recipe) and <out>/rig_<Cls>.json, plus a joint check render.
import bpy, bmesh, sys, os, json, math, traceback
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index('--') + 1:]
OUT, REPO, CLS, SRC = argv[0], argv[1], argv[2], argv[3]
FINGERS = (argv[4] != '0') if len(argv) > 4 else True
HERE = os.path.dirname(os.path.abspath(__file__)) if '__file__' in globals() else os.path.join(REPO, 'Assets/_Game/Art/Characters/_source~/characters_hy/rig')
sys.path.insert(0, HERE)
import importlib
import landmarks as LM
importlib.reload(LM)
OLD = {'Builder': 'Assets/_Game/Art/Builder/Builder.fbx', 'Healer': 'Assets/_Game/Art/Healer/Healer.fbx',
       'Demolitions': 'Assets/_Game/Art/Demolitions/Demolitions.fbx'}[CLS]
_fb = OLD.replace(f'/{CLS}.fbx', f'/_fallback~/{CLS}.fbx')
if os.path.exists(os.path.join(REPO, _fb)):
    OLD = _fb   # the class's KayKit-joint model once the new body has replaced it
os.makedirs(OUT, exist_ok=True)
REP = {'class': CLS, 'src': SRC, 'old_rig': OLD, 'fingers': FINGERS, 'log': []}


def log(*a):
    s = ' '.join(str(x) for x in a)
    print('[rigb]', s, flush=True)
    REP['log'].append(s)


def save():
    json.dump(REP, open(os.path.join(OUT, f'rig_{CLS}.json'), 'w'), indent=1,
              default=lambda o: o.tolist() if hasattr(o, 'tolist') else (list(o) if hasattr(o, '__iter__') else str(o)))


def imp(path):
    bo = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path if os.path.isabs(path) else os.path.join(REPO, path), use_anim=False,
                             use_custom_normals=False, ignore_leaf_bones=False, automatic_bone_orientation=False,
                             primary_bone_axis='Y', secondary_bone_axis='X')
    return [o for o in bpy.data.objects if o not in bo]


def upd():
    bpy.context.view_layer.update()


def verts(ob):
    a = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', a); return a.reshape(-1, 3)


def faces(ob):
    me = ob.data; me.calc_loop_triangles()
    a = np.empty(len(me.loop_triangles) * 3, dtype=np.int64); me.loop_triangles.foreach_get('vertices', a)
    return a.reshape(-1, 3)


def seg_dist(P, a, b):
    a = np.array(a); b = np.array(b); ab = b - a
    t = np.clip(((P - a) @ ab) / (ab @ ab + 1e-12), 0, 1)
    return np.linalg.norm(P - (a + t[:, None] * ab), axis=1), t


try:
    # ------------------------------------------------------------ 1. the cleaned body
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    objs = imp(SRC)
    LV = {}
    for o in objs:
        if o.type != 'MESH':
            continue
        for lv in ('Hero', 'LOD0', 'LOD1', 'Shadow'):
            if o.name.endswith('_' + lv) or o.name.split('.')[0].endswith('_' + lv):
                LV[lv] = o
    for o in objs:
        if o not in LV.values():
            bpy.data.objects.remove(o, do_unlink=True)
    for lv, o in LV.items():
        mw = o.matrix_world.copy(); o.parent = None
        o.data.transform(mw); o.matrix_world = Matrix.Identity(4)
        for m in list(o.modifiers):
            o.modifiers.remove(m)
        o.vertex_groups.clear()
        o.name = o.data.name = f'{CLS}_{lv}'
    hero = LV['Hero']

    def measure_sheet(tag):
        """Front and side orthographic renders of the Hero with a 0.1 m grid behind (red every 0.5 m) to read joints."""
        sc = bpy.context.scene
        sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'FLAT'
        sc.display.shading.color_type = 'VERTEX'; sc.render.resolution_x = sc.render.resolution_y = 1000
        sc.display.shading.background_type = 'VIEWPORT'; sc.display.shading.background_color = (1, 1, 1)
        made = []
        cam_d = bpy.data.cameras.new('mcam'); cam_d.type = 'ORTHO'; cam_d.ortho_scale = 2.6
        cam = bpy.data.objects.new('mcam', cam_d); sc.collection.objects.link(cam); sc.camera = cam; made.append(cam)
        for vn in ('front', 'side'):
            bm = bmesh.new()
            for k in range(-13, 14):
                c = 0.1 * k
                for horiz in (True, False):
                    if horiz and not (0 <= k + 13 <= 26):
                        continue
                    w = 0.006 if k % 5 else 0.012
                    if horiz:
                        z = 0.1 * (k + 13); sz = (2.6, 0.002, w); ctr = (0, 0, z)
                    else:
                        sz = (w, 0.002, 2.7); ctr = (c, 0, 1.3)
                    r = bmesh.ops.create_cube(bm, size=1.0)
                    for v in r['verts']:
                        v.co = Vector((v.co.x * sz[0] + ctr[0], v.co.y * sz[1] + ctr[1], v.co.z * sz[2] + ctr[2]))
            me = bpy.data.meshes.new('grid'); bm.to_mesh(me); bm.free()
            ca = me.color_attributes.new('Col', 'BYTE_COLOR', 'CORNER')
            for d in ca.data:
                d.color = (0.6, 0.6, 0.6, 1)
            g = bpy.data.objects.new('grid', me); sc.collection.objects.link(g); made.append(g)
            if vn == 'front':
                g.location = (0, 1.5, 0); cam.location = (0, -8, 1.25); cam.rotation_euler = (math.pi / 2, 0, 0)
            else:
                g.rotation_euler = (0, 0, math.pi / 2); g.location = (-1.5, 0, 0)
                cam.location = (8, 0, 1.25); cam.rotation_euler = (math.pi / 2, 0, math.pi / 2)
            for o in LV.values():
                o.hide_render = o is not hero
            sc.render.filepath = os.path.join(OUT, f'measure_{CLS}_{tag}_{vn}.png')
            bpy.ops.render.render(write_still=True)
            bpy.data.objects.remove(g, do_unlink=True); made.remove(g)
        # hand close-ups (positions from the joint override when there is one): are the fingers separate?
        jf_ = os.path.join(HERE, f'rigb_joints_{CLS}.json')
        if os.path.exists(jf_):
            ov_ = json.load(open(jf_))
            sc.render.resolution_x = sc.render.resolution_y = 600
            cam_d.ortho_scale = 0.42
            for s_ in ('l', 'r'):
                if f'hand.{s_}' not in ov_:
                    continue
                hx, hy, hz = ov_[f'hand.{s_}']
                sg = 1 if s_ == 'l' else -1
                for vn, loc, rot in (('front', (hx, hy - 3, hz - 0.05), (math.pi / 2, 0, 0)),
                                     ('out', (hx + sg * 3, hy, hz - 0.05), (math.pi / 2, 0, sg * math.pi / 2)),
                                     ('in', (hx - sg * 3, hy, hz - 0.05), (math.pi / 2, 0, -sg * math.pi / 2)),
                                     ('below', (hx, hy, hz - 3), (0, 0, 0))):
                    cam.location, cam.rotation_euler = loc, rot
                    cam_d.clip_start = 2.75 if vn != 'below' else 2.6
                    sc.render.filepath = os.path.join(OUT, f'measure_{CLS}_{tag}_hand_{s_}_{vn}.png')
                    bpy.ops.render.render(write_still=True)
            cam_d.clip_start = 0.1
        for o in made:
            bpy.data.objects.remove(o, do_unlink=True)

    try:
        measure_sheet('bind')
    except Exception as e:
        log('measure sheet failed', repr(e))
    V = verts(hero)
    log('levels', {k: (len(o.data.vertices), len(faces(o))) for k, o in LV.items()},
        'hero bbox', V.min(0).round(3).tolist(), V.max(0).round(3).tolist())
    F = faces(hero)
    Vw, Fw, inv_w = LM.weld(V, F)
    lmk = LM.find(Vw, Fw)
    jf = os.path.join(HERE, f'rigb_joints_{CLS}.json')
    if os.path.exists(jf):
        ov = json.load(open(jf))
        seed = {s: [ov[k] for k in (f'upperarm.{s}', f'lowerarm.{s}', f'wrist.{s}')] for s in ('l', 'r')
                if all(k in ov for k in (f'upperarm.{s}', f'lowerarm.{s}', f'wrist.{s}'))}
        if seed:
            lmk = LM.find(Vw, Fw, arm_seed=seed)
        for k, v in ov.items():
            if not k.startswith('_note') and not isinstance(v, dict):   # rig-healer-3: dict keys are options
                lmk['joints'][k] = v
        log('joint override', jf, sorted(ov))
    H = lmk['height']
    J = {k: Vector(v) for k, v in lmk['joints'].items()}
    for n in lmk['notes']:
        log('landmark:', n)
    # rig-healer-2: the cloth strip hanging behind her legs is MODELLED down to the floor, so no weighting can keep it off
    # the ground (healer-5 put it on the hips and it still lay between and behind her feet). Joint-file "_flap_hem"
    # [half width x, -, hem z]: every vertex of the strip (behind "_flap_y", within the half width, below the hem) is
    # folded up to end just under the hem, on every level. A short closed fold, no hole.
    JS = json.load(open(jf)) if os.path.exists(jf) else {}   # rig-healer-3: option dicts
    if '_flap_y' in J and '_flap_hem' in J and '_strip_del' not in JS:
        hx, hy, hz = J['_flap_hem'].x, max(J['_flap_hem'].y, J['_flap_y'].y), J['_flap_hem'].z
        fold = {}
        legsegs = []
        for sd in ('l', 'r'):
            ch = [J[f'lowerleg.{sd}'], J[f'foot.{sd}'], J[f'toes.{sd}'], J[f'_toetip.{sd}']]
            legsegs += [(ch[i], ch[i + 1]) for i in range(3)]
        for lv, o in LV.items():
            A = verts(o)
            # healer-2 run 2: picked by distance, not a box: below the hem, behind the ankles and farther than the
            # half width from every lower-leg, foot and toe bone is not a leg or a boot (the heel is 0.14 from its ankle)
            dleg = np.min([seg_dist(A, a, b)[0] for a, b in legsegs], axis=0)
            sel = (A[:, 2] < hz) & (A[:, 1] > min(J['foot.l'].y, J['foot.r'].y)) & (dleg > hx)
            A[sel, 2] = hz - (hz - A[sel, 2]) * 0.08
            o.data.vertices.foreach_set('co', A.reshape(-1)); o.data.update()
            fold[lv] = int(sel.sum())
        V = verts(hero)
        REP['flap_folded_to_hem'] = {'hem_z': round(hz, 3), 'half_width': round(hx, 3), 'behind_y': round(hy, 3), 'vertices': fold,
                                     'lowest_z_behind': round(float(V[V[:, 1] > J['_flap_y'].y][:, 2].min()), 3)}
        log('flap folded', REP['flap_folded_to_hem'])

    # ------------------------------------------------------------ rig-healer-3: colour helpers, painted face, strip removed
    def face_rgba(o):
        """Per-face sRGB colour + code alpha of the 'Col' attribute (faces are flat-coloured by clean_export)."""
        me = o.data
        a = me.color_attributes.get('Col') or (me.color_attributes[0] if len(me.color_attributes) else None)
        if a is None:
            return None, None
        n = len(a.data); c = np.empty(n * 4, np.float32)
        try:
            a.data.foreach_get('color_srgb', c)
        except Exception:
            a.data.foreach_get('color', c)
        c = c.reshape(-1, 4)
        if a.domain == 'CORNER':
            ls = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_start', ls)
            return c[ls], a
        out = np.array([c[list(pg.vertices)].mean(0) for pg in me.polygons], np.float32)
        return out, a

    def set_face_rgba(o, a, fids, rgba):
        me = o.data
        n = len(a.data); c = np.empty(n * 4, np.float32)
        key = 'color_srgb'
        try:
            a.data.foreach_get(key, c)
        except Exception:
            key = 'color'; a.data.foreach_get(key, c)
        c = c.reshape(-1, 4)
        for f in fids:
            pg = me.polygons[int(f)]
            idx = range(pg.loop_start, pg.loop_start + pg.loop_total) if a.domain == 'CORNER' else list(pg.vertices)
            for li in idx:
                c[li] = rgba
        a.data.foreach_set(key, c.ravel()); me.update()

    def hex_rgb(h):
        h = h.lstrip('#'); return [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]

    # Painted face (ArtImportTests.HyFacesArePaintedInVertexColourAndNeverTeamSwapped). Same rule as rig-demolitions-1's
    # clean_export face_features, applied to the holding model's colours: inside the face box a face that is DARK
    # (luma under rel x the skin) or EYE-COLOURED (her eyes are painted teal: green and blue well above red; skin is warm,
    # hair grey) is painted the dark feature colour, code 0 (never team-swapped). Dark teal keeps her teal eyes.
    ffo = JS.get('_face_feat')
    if ffo:
        x0, x1, y0, y1, z0, z1 = ffo['box']
        REP['face_features'] = {}
        for lv, o in LV.items():
            if lv == 'Shadow':
                continue
            C, a = face_rgba(o)
            if C is None:
                REP['face_features'][lv] = 'no colour attribute'; continue
            ctr = np.empty(len(o.data.polygons) * 3, np.float32); o.data.polygons.foreach_get('center', ctr); ctr = ctr.reshape(-1, 3)
            inb = (ctr[:, 0] > x0) & (ctr[:, 0] < x1) & (ctr[:, 1] > y0) & (ctr[:, 1] < y1) & (ctr[:, 2] > z0) & (ctr[:, 2] < z1)
            r, g, b = C[:, 0], C[:, 1], C[:, 2]
            luma = C[:, :3].mean(1)
            skin = inb & (r > 0.45) & (r > g) & (g > b) & (r - b > 0.12)
            ref = float(np.percentile(luma[skin], 75)) if skin.any() else 0.7
            teal = (g - r > ffo.get('teal_g', 0.06)) & (b - r > ffo.get('teal_b', 0.04))
            dark = inb & ((luma < ffo.get('rel', 0.65) * ref) | teal)
            fids = np.nonzero(dark)[0]
            if len(fids):
                set_face_rgba(o, a, fids, hex_rgb(ffo.get('colour', '#1C3A3D')) + [0.0])
            REP['face_features'][lv] = {'faces_in_box': int(inb.sum()), 'skin_faces': int(skin.sum()), 'skin_ref': round(ref, 3),
                                        'teal': int((inb & teal).sum()), 'painted': int(len(fids))}
        log('face features', REP['face_features'])

    # The strip behind her back (neck to floor, a pale plank once folded) is DELETED on every level: faces near its
    # centre line in the side view (yz, "_strip_del" top/bot [y, z], thickness, half width x), plus anything below the
    # hem behind the ankles and away from the lower-leg/foot/toe bones; never the dress (blue) or a wing (green). The
    # hole is capped and the cap takes the dress colour (copied from the dress beside it, code included).
    sdo = JS.get('_strip_del')
    if sdo:
        (yt, zt), (yb, zb) = sdo['top'], sdo['bot']
        legsegs = []
        for sd in ('l', 'r'):
            ch = [J[f'lowerleg.{sd}'], J[f'foot.{sd}'], J[f'toes.{sd}'], J[f'_toetip.{sd}']]
            legsegs += [(ch[i], ch[i + 1]) for i in range(3)]
        footy = min(J['foot.l'].y, J['foot.r'].y)
        REP['strip_deleted'] = {}
        for lv, o in LV.items():
            C, a = face_rgba(o)
            me = o.data
            ctr = np.empty(len(me.polygons) * 3, np.float32); me.polygons.foreach_get('center', ctr); ctr = ctr.reshape(-1, 3)
            # distance in the side view (y, z) to the strip's centre line
            A2 = np.array([yt, zt]); B2 = np.array([yb, zb]); Q = ctr[:, 1:3]
            t = np.clip(((Q - A2) @ (B2 - A2)) / float((B2 - A2) @ (B2 - A2)), 0, 1)
            dl = np.linalg.norm(Q - (A2 + t[:, None] * (B2 - A2)), axis=1)
            dleg = np.min([seg_dist(ctr, aa, bb)[0] for aa, bb in legsegs], axis=0)
            low_ok = (ctr[:, 2] >= sdo['hem']) | (dleg > sdo['leg_r'])
            band = (np.abs(ctr[:, 0]) < sdo['half_x']) & (dl < sdo['thick']) & (ctr[:, 2] < zt + 0.02) & low_ok
            low = (ctr[:, 2] < sdo['hem']) & (ctr[:, 1] > footy) & (dleg > sdo['leg_r'])
            if C is not None:
                r, g, b = C[:, 0], C[:, 1], C[:, 2]
                dress = (b - r > sdo.get('dress_b', 0.05))
                wing = (g > np.maximum(r, b) + 0.05)
            else:
                dress = wing = np.zeros(len(ctr), bool)
            sel = (band | low) & ~dress & ~wing
            # the dress colour for the cap: the most common blue face behind her between hem and chest
            behind = dress & (ctr[:, 1] > 0) & (ctr[:, 2] > sdo['hem']) & (ctr[:, 2] < 1.0)
            ref_face = None
            if behind.any():
                key = np.round(C[behind, :4] * 50).astype(int)
                u, inv, cnt = np.unique(key, axis=0, return_inverse=True, return_counts=True)
                ref_face = int(np.nonzero(behind)[0][np.nonzero(inv.ravel() == cnt.argmax())[0][0]])
            bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
            lay = None   # run 1: the FBX layer is not always named 'Col'; the cap stayed uncoloured (pale grey)
            for _ls in (bm.loops.layers.color, bm.loops.layers.float_color):
                if lay is None and len(_ls):
                    lay = _ls.get(a.name) if a is not None and _ls.get(a.name) is not None else _ls.active or _ls.values()[0]
            ref_col = None
            if lay is not None and ref_face is not None:
                ref_col = bm.faces[ref_face].loops[0][lay].copy() if hasattr(bm.faces[ref_face].loops[0][lay], 'copy') else tuple(bm.faces[ref_face].loops[0][lay])
            # run 2: grow from the band through connected faces that are strip-like (not dress, not wing, behind the
            # strip's front line less "grow_margin", below "grow_z", inside the half width): run 1 left its far edge hanging.
            gm = sdo.get('grow_margin', 0.03); gz = sdo.get('grow_z', 1.05)
            yline = yt + (yb - yt) * np.clip((zt - ctr[:, 2]) / (zt - zb), 0, 1)
            cand = ~dress & ~wing & (np.abs(ctr[:, 0]) < sdo['half_x']) & (ctr[:, 2] < gz) & (ctr[:, 1] > yline - gm) & low_ok
            grown = sel.copy(); front = list(np.nonzero(sel)[0])
            while front:
                nxt = []
                for fi in front:
                    for e in bm.faces[int(fi)].edges:
                        for nf in e.link_faces:
                            j = nf.index
                            if not grown[j] and cand[j]:
                                grown[j] = True; nxt.append(j)
                front = nxt
            REP.setdefault('strip_grown', {})[lv] = int(grown.sum() - sel.sum())
            sel = grown
            kill = [bm.faces[i] for i in np.nonzero(sel)[0]]
            edges = {e for f in kill for e in f.edges}
            bmesh.ops.delete(bm, geom=kill, context='FACES')
            bnd = [e for e in edges if e.is_valid and len(e.link_faces) == 1]
            capped = 0
            if bnd:
                res = bmesh.ops.holes_fill(bm, edges=bnd, sides=0)
                nf = res.get('faces', [])
                if nf:
                    tri = bmesh.ops.triangulate(bm, faces=nf)
                    nf = tri.get('faces', nf)
                    capped = len(nf)
                    if lay is not None and ref_col is not None:
                        for f in nf:
                            for l in f.loops:
                                l[lay] = ref_col
            loose = [v for v in bm.verts if not v.link_faces]
            if loose:
                bmesh.ops.delete(bm, geom=loose, context='VERTS')
            bm.to_mesh(me); bm.free(); me.update()
            open_edges = 0
            bm2 = bmesh.new(); bm2.from_mesh(me); open_edges = sum(1 for e in bm2.edges if len(e.link_faces) == 1); bm2.free()
            REP['strip_deleted'][lv] = {'faces_deleted': len(kill), 'band': int((band & ~dress & ~wing).sum()),
                                        'below_hem': int((low & ~dress & ~wing).sum()), 'cap_tris': capped,
                                        'open_edges_after': open_edges, 'cap_colour': None if ref_face is None else
                                        [round(float(x), 3) for x in C[ref_face]], 'faces_after': len(me.polygons)}
        log('strip deleted', REP['strip_deleted'])
    V = verts(hero)
    REP['joints'] = {k: [round(x, 4) for x in v] for k, v in J.items()}
    REP['hands'] ={s: {k: v for k, v in h.items() if k != 'finger_vids'} for s, h in lmk['hands'].items()}
    save()

    # ------------------------------------------------------------ 2. skeleton from the class's current rig
    oobjs = imp(OLD)
    old = next(o for o in oobjs if o.type == 'ARMATURE')
    for o in oobjs:
        if o is not old:
            bpy.data.objects.remove(o, do_unlink=True)
    REP['old_rig_world'] = [list(r) for r in old.matrix_world]
    if any(abs(old.matrix_world[i][j] - (1 if i == j else 0)) > 1e-4 for i in range(4) for j in range(4)):
        log('old rig world matrix is not identity: applying it', REP['old_rig_world'])
        for o in scene.objects:
            o.select_set(o is old)
        bpy.context.view_layer.objects.active = old
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    KB = {b.name: (b.head_local.copy(), b.tail_local.copy(), b.matrix_local.to_3x3().copy()) for b in old.data.bones}
    REP['old_bones'] = len(KB)
    rig = old
    rig.name = 'Rig'; rig.data.name = 'Rig'
    for o in scene.objects:
        o.select_set(o is rig)
    bpy.context.view_layer.objects.active = rig
    SIDES = ('l', 'r')
    CHILD = {'hips': 'spine', 'spine': 'chest', 'chest': 'head', 'head': '_headtop'}
    for s in SIDES:
        CHILD.update({f'upperleg.{s}': f'lowerleg.{s}', f'lowerleg.{s}': f'foot.{s}', f'foot.{s}': f'toes.{s}',
                      f'toes.{s}': f'_toetip.{s}', f'upperarm.{s}': f'lowerarm.{s}', f'lowerarm.{s}': f'wrist.{s}',
                      f'wrist.{s}': f'hand.{s}'})
    hands = lmk['hands']
    handtip = {}
    for s in SIDES:
        h = hands.get(s, {})
        tips = [Vector(h[k][1]) for k in ('index', 'middle', 'ring', 'pinky', 'mitten') if k in h]
        if tips:
            handtip[s] = sum(tips, Vector()) / len(tips)
        else:
            handtip[s] = J[f'hand.{s}'] + (J[f'hand.{s}'] - J[f'wrist.{s}']).normalized() * 0.06 * H
    bpy.ops.object.mode_set(mode='EDIT')
    eb = rig.data.edit_bones
    for b in eb:
        b.use_connect = False
    moved = {}

    def place(name, head, tail):
        b = eb[name]
        hk, tk, Rk = KB[name]
        dk = (tk - hk).normalized()
        dn = (tail - head).normalized()
        q = dk.rotation_difference(dn)
        zn = q @ Rk.col[2]
        b.head = head; b.tail = tail
        b.align_roll(zn)
        moved[name] = round(math.degrees(dk.angle(dn)), 1)

    for name, ch in CHILD.items():
        if name in eb and name in J and ch in J:
            place(name, J[name], J[ch])
    for s in SIDES:
        hn = f'hand.{s}'
        if hn in eb and hn in J:
            d = handtip[s] - J[hn]
            place(hn, J[hn], J[hn] + d * 0.5)
        sn = f'handslot.{s}'
        if sn in eb and hn in eb:
            # KayKit's slot frame relative to KayKit's hand, carried onto the new hand; position from the grip centre
            hk, tk, Rhk = KB[hn]; shk, stk, Rsk = KB[sn]
            Rhn = eb[hn].matrix.to_3x3()
            Rsn = Rhn @ Rhk.inverted() @ Rsk
            p = J.get(sn, eb[hn].head.lerp(eb[hn].tail, 1.0))
            ln = (stk - shk).length
            b = eb[sn]
            b.head = p; b.tail = p + Rsn.col[1] * ln
            b.align_roll(Rsn.col[2])
            moved[sn] = 'slot'
    # finger bones (15 per hand), as rig-method-2
    FING = ('thumb', 'index', 'middle', 'ring', 'pinky')
    palm = {}
    if FINGERS:
        for s in SIDES:
            h = hands.get(s, {})
            wr, hd = J[f'wrist.{s}'], J[f'hand.{s}']
            hdir = (handtip[s] - wr).normalized()
            hv = Vw[(Vw - np.array(wr)) @ np.array(hdir) > 0]
            hv = hv[np.linalg.norm(hv - np.array(hd), axis=1) < 0.12 * H]
            inward = Vector((0, J['hips'].y, J['hips'].z)) - Vector(hd)
            n = inward.normalized()
            if len(hv) > 20:
                w_, ev = np.linalg.eigh(np.cov((hv - hv.mean(0)).T))
                n = Vector(ev[:, 0])
                if n.dot(inward) < 0:
                    n = -n
            n = (n - hdir * n.dot(hdir)).normalized()
            side_dir = hdir.cross(n).normalized()
            ch = {k: (Vector(h[k][0]), Vector(h[k][1])) for k in ('thumb', 'index', 'middle', 'ring', 'pinky', 'mitten') if k in h}
            if 'mitten' in ch and 'middle' not in ch:
                ch['middle'] = ch['mitten']
            if 'middle' not in ch:
                ch['middle'] = (hd + hdir * 0.03 * H, handtip[s])
            if 'index' in ch and (ch['middle'][0] - ch['index'][0]).dot(side_dir) < 0:
                side_dir = -side_dir
            off = 0.012 * H
            if 'index' not in ch:
                ch['index'] = (ch['middle'][0] - side_dir * off, ch['middle'][1] - side_dir * off)
            if 'ring' not in ch:
                ch['ring'] = (ch['middle'][0] + side_dir * off, ch['middle'][1] + side_dir * off)
            if 'pinky' not in ch:
                ch['pinky'] = (ch['ring'][0] + side_dir * off, ch['ring'][1] + side_dir * off)
            if 'thumb' not in ch:
                ch['thumb'] = (hd + n * 0.02 * H - side_dir * 0.02 * H, hd + n * 0.03 * H - side_dir * 0.03 * H + hdir * 0.03 * H)
            palm[s] = (n, side_dir, hdir, ch)
            for f in FING:
                base, tip = ch[f]
                if f == 'thumb':
                    pts = [hd.lerp(base, 0.5), base, base.lerp(tip, 0.55), tip]
                    roll = Vector(J.get(f'handslot.{s}', hd)) - base
                    roll = (roll - (tip - base).normalized() * roll.dot((tip - base).normalized())).normalized()
                else:
                    pts = [base, base.lerp(tip, 0.45), base.lerp(tip, 0.75), tip]
                    roll = n
                par = f'hand.{s}'
                for i in range(3):
                    nm = f'{f}_0{i + 1}.{s}'
                    b = eb.new(nm)
                    b.head, b.tail = pts[i], pts[i + 1]
                    if (b.tail - b.head).length < 1e-4:
                        b.tail = b.head + hdir * 0.01
                    b.parent = eb[par]
                    b.align_roll(roll)
                    par = nm
            REP.setdefault('finger_mode', {})[s] = h.get('mode')
    bpy.ops.object.mode_set(mode='OBJECT')
    REP['bone_turn_from_kaykit_deg'] = moved
    DEFORM = {'hips', 'spine', 'chest', 'head'}
    for s in SIDES:
        DEFORM |= {f'{n}.{s}' for n in ('upperleg', 'lowerleg', 'foot', 'toes', 'upperarm', 'lowerarm', 'wrist', 'hand')}
        DEFORM |= {f'{f}_0{i}.{s}' for f in FING for i in (1, 2, 3)} if FINGERS else set()
    for b in rig.data.bones:
        b.use_deform = b.name in DEFORM
    names = [b.name for b in rig.data.bones]
    REP['skeleton'] = {'bones': len(names), 'deform': sum(b.use_deform for b in rig.data.bones), 'names': names}
    log('skeleton', len(names), 'bones,', REP['skeleton']['deform'], 'deform; turn from KayKit (deg)', moved)
    save()

    # ------------------------------------------------------------ 3. Hero weights
    DEF = [b.name for b in rig.data.bones if b.use_deform]
    FINGER_B = [n for n in DEF if n.split('_0')[0] in FING]
    bi = {n: i for i, n in enumerate(DEF)}
    proxy = hero.copy(); proxy.data = hero.data.copy(); proxy.name = 'Proxy'
    scene.collection.objects.link(proxy)
    proxy.data.remesh_voxel_size = H / 150
    with bpy.context.temp_override(active_object=proxy, object=proxy, selected_objects=[proxy], selected_editable_objects=[proxy]):
        bpy.ops.object.voxel_remesh()
    log('proxy', len(proxy.data.vertices), 'verts')
    for n in FINGER_B:
        rig.data.bones[n].use_deform = False
    for o in scene.objects:
        o.select_set(False)
    proxy.select_set(True); rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    for n in FINGER_B:
        rig.data.bones[n].use_deform = True
    log('proxy heat groups', len(proxy.vertex_groups))
    for n in DEF:
        hero.vertex_groups.new(name=n)
    m = hero.modifiers.new('DT', 'DATA_TRANSFER')
    m.object = proxy; m.use_vert_data = True; m.data_types_verts = {'VGROUP_WEIGHTS'}
    m.vert_mapping = 'POLYINTERP_NEAREST'; m.layers_vgroup_select_src = 'ALL'; m.layers_vgroup_select_dst = 'NAME'
    with bpy.context.temp_override(object=hero, active_object=hero, selected_objects=[hero]):
        bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.data.objects.remove(proxy, do_unlink=True)
    gi = {g.index: g.name for g in hero.vertex_groups}
    nv = len(hero.data.vertices)
    W = np.zeros((nv, len(DEF)))
    for v in hero.data.vertices:
        for g in v.groups:
            nm = gi[g.group]
            if nm in bi:
                W[v.index, bi[nm]] = g.weight
    log('transferred: vertices with no weight', int((W.sum(1) < 1e-6).sum()), 'of', nv)
    P = V
    dom = W.argmax(1)
    locked = np.zeros(nv, bool)
    # head top rigid
    # back pack rigid on the chest: well behind the chest joint, between the hips and the head top, not an arm
    # rig-builder-2: everything carried behind the back (ladder, pack, wings) is ONE rigid piece on the chest: any vertex
    # behind the back plane above the hips that is not on an arm (the ladder took shoulder weight and fanned out in Attack)
    back = (P[:, 1] > J['chest'].y + 0.05 * H) & (P[:, 2] > J['hips'].z)
    headc = np.array([0.0, J['head'].y, (J['head'].z + J['_headtop'].z) / 2])
    back &= np.linalg.norm(P - headc, axis=1) > 0.65 * (J['_headtop'].z - J['head'].z)   # hair buns stay on the head
    # rig-builder-2 (owner's look 2): NO arm exception, and the head-top rule runs after this and skips the pack: the
    # ladder's top rail above the head was taken by the head rule, so the ladder fanned between head and chest in Attack.
    W[back] = 0; W[back, bi['chest']] = 1; locked |= back
    rig_head = (P[:, 2] > J['head'].z + 0.06 * H) & np.isin(dom, [bi['head'], bi['chest'], bi['spine']]) & ~back
    W[rig_head] = 0; W[rig_head, bi['head']] = 1; locked |= rig_head
    # rig-healer-3: the hair buns are ONE rigid piece on the head (their outer halves fell in the back-pack rule and rode
    # the chest, so the buns smeared upward in the run). Above the head joint + dz, within r of the head axis, not green.
    hro = JS.get('_head_rigid')
    if hro:
        Cf, _a = face_rgba(hero)
        green = np.zeros(nv, bool)
        if Cf is not None:
            gf = Cf[:, 1] > np.maximum(Cf[:, 0], Cf[:, 2]) + 0.05
            for pg in hero.data.polygons:
                if gf[pg.index]:
                    green[list(pg.vertices)] = True
        buns = (P[:, 2] > J['head'].z + hro['dz']) & (np.hypot(P[:, 0], P[:, 1] - J['head'].y) < hro['r']) & ~green
        W[buns] = 0; W[buns, bi['head']] = 1; locked |= buns
        REP['buns_on_head'] = int(buns.sum())
    # rig-healer-1: a cloth panel hanging behind the legs to the floor rides the hips (joint file "_flap_y": metres behind)
    if '_flap_y' in J:
        flap = (P[:, 1] > J['_flap_y'].y) & (P[:, 2] < J['hips'].z) & ~locked
        W[flap] = 0; W[flap, bi['hips']] = 1; locked |= flap
        REP['flap_on_hips'] = int(flap.sum())
    # rig-healer-2: shins and boots hold their volume. Below the knee (and not the strip behind) a leg vertex is rigid
    # on its side's foot up to just above the ankle, then blends over 8 cm into rigid on the lower leg. Heat weights
    # had mixed hips, skirt and the other leg into her boots, which thinned and smeared them in the side walk.
    if J.get('_rigid_shins') is not None:
        flapz = (P[:, 1] > J['_flap_y'].y) if '_flap_y' in J else np.zeros(nv, bool)
        shin = {}
        for s in SIDES:
            fx = J[f'foot.{s}'].x
            sel = (np.sign(P[:, 0]) == np.sign(fx)) & (np.abs(P[:, 0]) > 0.03) & \
                  (P[:, 2] < J[f'lowerleg.{s}'].z - J['_rigid_shins'].z) & ~flapz & ~locked & \
                  ~((P[:, 2] > J[f'foot.{s}'].z + 0.08) & (P[:, 1] < J['_rigid_shins'].y))   # not the apron's front hem
            t = np.clip((P[sel, 2] - (J[f'foot.{s}'].z - 0.01)) / 0.08, 0, 1)
            W[sel] = 0
            W[sel, bi[f'foot.{s}']] = 1 - t
            W[sel, bi[f'lowerleg.{s}']] = t
            locked |= sel
            shin[s] = int(sel.sum())
        REP['rigid_shins_boots'] = shin
    # arm weights only near the arm: an A-pose hand hangs beside the belt (the Builder's rope coil, pouches), and the
    # proxy heat let the arm carry them. Arm-chain weight on a vertex farther than ARM_R from its arm's bone chain goes to
    # the torso and legs it already had (or the hips).
    ARM_R = 0.075 * H
    moved_arm = 0
    for s in SIDES:
        cols = [bi[n] for n in (f'upperarm.{s}', f'lowerarm.{s}', f'wrist.{s}', f'hand.{s}') if n in bi]
        cols += [bi[n] for n in DEF if n.endswith('.' + s) and n.split('_0')[0] in FING]
        pts = [J[f'upperarm.{s}'], J[f'lowerarm.{s}'], J[f'wrist.{s}'], J[f'hand.{s}'], handtip[s]]
        # rig-builder-2: the hand reaches only its glove (0.055 H): a rope coil beside the A-pose hand stretched up with it
        d = np.min([seg_dist(P, pts[i], pts[i + 1])[0] - (0.0 if i < 2 else 0.02 * H) for i in range(4)], axis=0)
        far = (d > ARM_R) & (W[:, cols].sum(1) > 0) & ~locked
        if far.any():
            W[np.ix_(far, cols)] = 0
            empty = W[far].sum(1) < 1e-6
            idx = np.nonzero(far)[0][empty]
            hi = P[idx, 2] > J['chest'].z
            W[idx[hi], bi['chest']] = 1      # wings, a pack edge: ride the chest
            W[idx[~hi], bi['hips']] = 1      # a rope coil, belt pouches: ride the hips
            moved_arm += int(far.sum())
    REP['rigid'] = {'head_top': int(rig_head.sum()), 'back_pack': int(back.sum()), 'off_arm_vertices_reweighted': moved_arm}
    label = np.full(nv, -1)
    cut_faces = 0
    if FINGERS:
        for s in SIDES:
            if s not in palm:
                continue
            n, side_dir, hdir, ch = palm[s]
            wr = np.array(J[f'wrist.{s}'])
            hand_m = np.isin(dom, [bi[f'hand.{s}'], bi[f'wrist.{s}']]) | (np.linalg.norm(P - np.array(J[f'hand.{s}']), axis=1) < 0.05 * H)
            hand_m &= (P - wr) @ np.array(hdir) > 0
            hand_m &= ~locked
            rows = np.nonzero(hand_m)[0]
            if len(rows) == 0:
                continue
            Pm = P[rows]
            D, T1, LEN = [], [], []
            for fi, f in enumerate(FING):
                segs = [rig.data.bones[f'{f}_0{i}.{s}'] for i in (1, 2, 3)]
                ds = [seg_dist(Pm, b.head_local, b.tail_local) for b in segs]
                D.append(np.min([d for d, _ in ds], axis=0))
                T1.append(ds[0][1])
            D = np.array(D).T          # (m, 5)
            near = D.argmin(1)
            dmin = D.min(1)
            # inside the finger region: past each finger's knuckle (first segment t > 0) and close to its chain
            fr = np.zeros(len(rows), bool)
            for fi, f in enumerate(FING):
                b1 = rig.data.bones[f'{f}_01.{s}']
                proj = (Pm - np.array(b1.head_local)) @ np.array((b1.tail_local - b1.head_local).normalized())
                sel = (near == fi) & (proj > -0.004 * H) & (dmin < 0.035 * H)
                fr |= sel
            for fi, f in enumerate(FING):
                sel = fr & (near == fi)
                if not sel.any():
                    continue
                r_ = rows[sel]
                segs = [f'{f}_0{i}.{s}' for i in (1, 2, 3)]
                Dd = np.array([seg_dist(P[r_], rig.data.bones[sn].head_local, rig.data.bones[sn].tail_local)[0] for sn in segs]).T
                w = 1.0 / (Dd ** 4 + 1e-12); w /= w.sum(1, keepdims=True)
                b1 = rig.data.bones[segs[0]]
                _, t = seg_dist(P[r_], b1.head_local, b1.tail_local)
                keep_hand = np.clip(0.5 - t, 0, 0.5) if f != 'thumb' else np.clip(0.3 - t, 0, 0.3)
                W[r_] = 0
                for j, sn in enumerate(segs):
                    W[r_, bi[sn]] = w[:, j] * (1 - keep_hand)
                W[r_, bi[f'hand.{s}']] += keep_hand
                locked[r_] = True
                # distal part of the finger (for the cut): past the middle of the second segment
                b2 = rig.data.bones[segs[1]]
                _, t2 = seg_dist(P[r_], b2.head_local, b2.tail_local)
                dist = (t2 > 0.3) | (Dd.argmin(1) == 2)
                label[r_[dist]] = fi + (0 if s == 'l' else 10)
            REP.setdefault('finger_vertices', {})[s] = {f: int((fr & (near == fi)).sum()) for fi, f in enumerate(FING)}
    # smooth (not the locked parts)
    me = hero.data
    E = np.empty(len(me.edges) * 2, dtype=np.int64); me.edges.foreach_get('vertices', E); E = E.reshape(-1, 2)
    deg = np.bincount(E.ravel(), minlength=nv).astype(float)
    for _ in range(2):
        acc = np.zeros_like(W)
        np.add.at(acc, E[:, 0], W[E[:, 1]]); np.add.at(acc, E[:, 1], W[E[:, 0]])
        Wn = 0.5 * W + 0.5 * acc / np.maximum(deg, 1)[:, None]
        W = np.where(locked[:, None], W, Wn)

    def limit4(W, fallback):
        nvv = W.shape[0]
        order = np.argsort(-W, axis=1)
        W4 = np.zeros_like(W); r = np.arange(nvv)[:, None]
        W4[r, order[:, :4]] = W[r, order[:, :4]]
        W4[W4 < 0.01] = 0
        sums = W4.sum(1); zero = sums < 1e-9
        W4[zero, fallback] = 1
        return W4 / W4.sum(1)[:, None], int(zero.sum())

    W, z0 = limit4(W, bi['hips'])

    def write(ob, W):
        ob.vertex_groups.clear()
        for n in DEF:
            ob.vertex_groups.new(name=n)
        for j, n in enumerate(DEF):
            g = ob.vertex_groups[n]
            col = W[:, j]
            for i in np.nonzero(col > 0)[0]:
                g.add([int(i)], float(col[i]), 'REPLACE')

    write(hero, W)
    # cut the welded fingertips: faces whose corners lie on two different fingers' distal parts
    if FINGERS:
        bm = bmesh.new(); bm.from_mesh(hero.data); bm.faces.ensure_lookup_table()
        kill = []
        for fc in bm.faces:
            ls = {label[v.index] for v in fc.verts}
            if len(ls) > 1 and -1 not in ls:
                kill.append(fc)
        cut_faces = len(kill)
        if kill:
            bmesh.ops.delete(bm, geom=kill, context='FACES_ONLY')
            loose = [v for v in bm.verts if not v.link_faces]
            bmesh.ops.delete(bm, geom=loose, context='VERTS')
        bm.to_mesh(hero.data); bm.free(); hero.data.update()
    REP['weights_hero'] = {'vertices': nv, 'unweighted_fixed': z0, 'max_influences': int((W > 0).sum(1).max()),
                           'sum_err': float(np.abs(W.sum(1) - 1).max()), 'fingertip_faces_cut': cut_faces}
    log('hero weights', REP['weights_hero'])
    # LODs take the Hero's weights
    for lv in ('LOD0', 'LOD1', 'Shadow'):
        o = LV.get(lv)
        if o is None:
            continue
        for n in DEF:
            o.vertex_groups.new(name=n)
        m = o.modifiers.new('DT', 'DATA_TRANSFER')
        m.object = hero; m.use_vert_data = True; m.data_types_verts = {'VGROUP_WEIGHTS'}
        m.vert_mapping = 'POLYINTERP_NEAREST'; m.layers_vgroup_select_src = 'ALL'; m.layers_vgroup_select_dst = 'NAME'
        with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o]):
            bpy.ops.object.modifier_apply(modifier=m.name)
        gi = {g.index: g.name for g in o.vertex_groups}
        Wl = np.zeros((len(o.data.vertices), len(DEF)))
        for v in o.data.vertices:
            for g in v.groups:
                if gi[g.group] in bi:
                    Wl[v.index, bi[gi[g.group]]] = g.weight
        Wl, zl = limit4(Wl, bi['hips'])
        write(o, Wl)
        REP.setdefault('weights_lods', {})[lv] = {'vertices': len(o.data.vertices), 'unweighted_fixed': zl,
                                                  'max_influences': int((Wl > 0).sum(1).max())}
    log('lod weights', REP.get('weights_lods'))
    for o in LV.values():
        am = o.modifiers.new('Armature', 'ARMATURE'); am.object = rig
        o.parent = rig
        o.matrix_parent_inverse = rig.matrix_world.inverted()
    save()

    # ------------------------------------------------------------ 4. export + joint check render
    for o in scene.objects:
        o.select_set(o is rig or o in LV.values())
    bpy.context.view_layer.objects.active = rig
    path = os.path.join(OUT, f'{CLS}.fbx')
    r = bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'}, global_scale=1.0,
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                                 bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type='FACE',
                                 use_tspace=False, colors_type='SRGB', prioritize_active_color=True, add_leaf_bones=False,
                                 primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=False,
                                 armature_nodetype='NULL', bake_anim=False, path_mode='STRIP', embed_textures=False,
                                 use_custom_props=False)
    REP['export'] = {'path': path, 'result': list(r), 'bytes': os.path.getsize(path),
                     'tris': {lv: len(faces(o)) for lv, o in LV.items()}}
    log('export', REP['export'])
    save()
    try:
        scene.render.engine = 'BLENDER_WORKBENCH'
        scene.display.shading.light = 'STUDIO'
        scene.display.shading.color_type = 'VERTEX'
        scene.display.shading.show_xray = True
        scene.display.shading.xray_alpha = 0.35
        scene.render.resolution_x, scene.render.resolution_y = 520, 600
        for o in LV.values():
            o.hide_render = o is not hero
        mk = []
        for b in rig.data.bones:
            if not b.use_deform and not b.name.startswith('handslot'):
                continue
            for p_, rad in ((b.head_local, 0.012), (b.head_local.lerp(b.tail_local, 0.5), 0.006)):
                bpy.ops.mesh.primitive_uv_sphere_add(radius=rad * H / 2, location=rig.matrix_world @ p_, segments=8, ring_count=6)
                s_ = bpy.context.active_object
                ca = s_.data.color_attributes.new('Col', 'BYTE_COLOR', 'CORNER')
                c = (1, 0.1, 0.1, 1) if not b.name.startswith('handslot') else (0.1, 1, 0.1, 1)
                for d in ca.data:
                    d.color = c
                mk.append(s_)
        cam_d = bpy.data.cameras.new('cam'); cam_d.type = 'ORTHO'; cam_d.ortho_scale = H * 1.15
        cam = bpy.data.objects.new('cam', cam_d); scene.collection.objects.link(cam); scene.camera = cam
        for vn, loc, rot in (('front', (0, -8, H / 2), (math.pi / 2, 0, 0)), ('side', (8, 0, H / 2), (math.pi / 2, 0, math.pi / 2))):
            cam.location, cam.rotation_euler = loc, rot
            scene.render.filepath = os.path.join(OUT, f'joints_{CLS}_{vn}.png')
            bpy.ops.render.render(write_still=True)
        for s_ in mk:
            bpy.data.objects.remove(s_, do_unlink=True)
        scene.display.shading.show_xray = False
    except Exception as e:
        log('joint render failed', repr(e))
except Exception:
    REP['fatal'] = traceback.format_exc()[-3000:]
    print(REP['fatal'], flush=True)
save()
