# rig-kaiju-2 probe: import the static green-crystal Kaiju, report its shape, render gridded views for joint placement.
#   args: {out}
import bpy, sys, os, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kr2_common as C

OUT = sys.argv[sys.argv.index('--') + 1]
os.makedirs(OUT, exist_ok=True)
C.clear()
obs = C.import_static()
rep = {'objects': {}}
for lv, o in obs.items():
    V = C.verts(o)
    me = o.data
    rep['objects'][lv] = {'name': o.name, 'verts': len(V), 'tris': int(len(C.tris(o))),
                          'min': V.min(0).round(3).tolist(), 'max': V.max(0).round(3).tolist(),
                          'colors': [(a.name, a.domain, a.data_type) for a in me.color_attributes],
                          'uvs': [u.name for u in me.uv_layers], 'materials': [m.name if m else None for m in me.materials]}
hero = obs['Hero']
V = C.verts(hero); col = C.vcol(hero)
code = np.round(col[:, 3] * 8).astype(int)
# slices
sl = {}
for z in np.arange(0, 5.01, 0.25):
    m = (V[:, 2] >= z) & (V[:, 2] < z + 0.25)
    if m.sum():
        sl[round(float(z), 2)] = {'n': int(m.sum()), 'x': V[m, 0].min().round(2).tolist() if False else [round(float(V[m, 0].min()), 2), round(float(V[m, 0].max()), 2)],
                                  'y': [round(float(V[m, 1].min()), 2), round(float(V[m, 1].max()), 2)]}
rep['z_slices'] = sl
ys = {}
for y in np.arange(np.floor(V[:, 1].min() * 4) / 4, V[:, 1].max(), 0.25):
    m = (V[:, 1] >= y) & (V[:, 1] < y + 0.25)
    if m.sum():
        ys[round(float(y), 2)] = {'n': int(m.sum()), 'x': [round(float(V[m, 0].min()), 2), round(float(V[m, 0].max()), 2)],
                                  'z': [round(float(V[m, 2].min()), 2), round(float(V[m, 2].max()), 2)],
                                  'glow': int((code[m] == 6).sum())}
rep['y_slices'] = ys
# components (loose parts) of the hero
import bmesh
bm = bmesh.new(); bm.from_mesh(hero.data)
bm.verts.ensure_lookup_table()
seen = np.zeros(len(bm.verts), bool); comps = []
for i in range(len(bm.verts)):
    if seen[i]:
        continue
    stack = [i]; seen[i] = True; cnt = []
    while stack:
        k = stack.pop(); cnt.append(k)
        for e in bm.verts[k].link_edges:
            o = e.other_vert(bm.verts[k]).index
            if not seen[o]:
                seen[o] = True; stack.append(o)
    comps.append(cnt)
rep['hero_components'] = sorted([len(c) for c in comps], reverse=True)[:10]
nonman = sum(1 for e in bm.edges if not e.is_manifold)
rep['hero_nonmanifold_edges'] = nonman
bm.free()
# frontmost region (head guess)
fy = V[:, 1].min()
hm = V[:, 1] < fy + 0.8
rep['front_region'] = {'y': [round(float(fy), 3), round(float(fy + 0.8), 3)], 'x': [round(float(V[hm, 0].min()), 2), round(float(V[hm, 0].max()), 2)],
                       'z': [round(float(V[hm, 2].min()), 2), round(float(V[hm, 2].max()), 2)]}
# dark colours (eyes/mouth paint) near the front
dark = (col[:, :3].sum(1) < 0.25) & (V[:, 1] < fy + 1.2) & (V[:, 2] > 1.0)
rep['dark_front'] = {'n': int(dark.sum())}
if dark.sum():
    rep['dark_front'].update({'x': [round(float(V[dark, 0].min()), 2), round(float(V[dark, 0].max()), 2)],
                              'y': [round(float(V[dark, 1].min()), 2), round(float(V[dark, 1].max()), 2)],
                              'z': [round(float(V[dark, 2].min()), 2), round(float(V[dark, 2].max()), 2)]})
for lv, o in obs.items():
    o.hide_render = lv != 'Hero'
sc, cam = C.scene_setup(900)
cen = (0, (V[:, 1].min() + V[:, 1].max()) / 2, 2.5)
for v in ('front', 'side', 'top', 'back'):
    p = os.path.join(OUT, f'probe_{v}.png')
    C.shoot(sc, cam, p, v, cen, 6.4)
    C.grid_overlay(p, v, cen, 6.4)
# head close-ups
hc = (0, fy + 0.6, float(np.median(V[hm, 2])))
for v in ('front', 'side'):
    p = os.path.join(OUT, f'probe_head_{v}.png')
    C.shoot(sc, cam, p, v, hc, 2.4)
    C.grid_overlay(p, v, hc, 2.4, step=0.1)
p = os.path.join(OUT, 'probe_game.png'); C.shoot(sc, cam, p, 'game', cen, 7.0)
rep['head_centre'] = [round(float(x), 3) for x in hc]
json.dump(rep, open(os.path.join(OUT, 'probe.json'), 'w'), indent=1)
print(json.dumps(rep)[:3000])
