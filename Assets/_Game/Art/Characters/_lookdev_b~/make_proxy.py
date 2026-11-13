"""Pipeline PROXY input (used only while the hosted generator's free quota is spent): the v7 Warrior in its Idle
display pose, all objects fused into one vertex-coloured GLB in glTF axes, i.e. the condition a generated mesh
arrives in (posed, held items touching the body, no rig). It tests clean-up, re-UV, repaint and rigging, NOT the
generator's shape quality.
  <venv>/bin/python -I make_proxy.py <v7pose.npz> <out.glb>
"""
import sys
import numpy as np
import trimesh

d = np.load(sys.argv[1])
ms = []
for n in [str(x) for x in d["meshes"]]:
    V = d["V_" + n].astype(np.float64)
    C = d["C_" + n] if ("C_" + n) in d.files else np.full((len(V), 4), 0.5)
    Vg = np.stack([V[:, 0], V[:, 2], -V[:, 1]], 1)            # Blender -> glTF axes
    ms.append(trimesh.Trimesh(Vg, d["F_" + n], vertex_colors=(np.c_[C[:, :3], np.ones(len(V))] * 255).astype(np.uint8),
                              process=False))
m = trimesh.util.concatenate(ms)
m.export(sys.argv[2])
print("proxy", len(m.vertices), len(m.faces))
