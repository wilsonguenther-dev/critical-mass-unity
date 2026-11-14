# Kaiju adult HY candidate (staged, not swapped)

Kaiju_Adult.fbx here is the Tencent HY build after the rigging pass (2026-11-13, art/characters-hy-rig): the native
Kaiju.blend Adult_Rig (20 bones) with its joints refit to the HY body (hips, spine, arm and leg columns, the tail
chain; the right limbs sit 0.16-0.25 m further out than the left on this mesh), bone-heat weights refit by
_source~/characters_hy/rig/opt.py, crystals rigid, and a face-decal UV fit on the snout (top half of Kaiju_Face.png)
so the expression atlases work. Hero 38,000 / LOD0 8,900 / LOD1 4,400 / shadow 1,450 triangles.

Measured against the previous staged build (all frames, Hero): 99th-percentile edge stretch 4.20 -> 2.17,
worst-frame inverted faces 2049 -> 505, visible-tear edge-frames 20845 -> 5199.

Still NOT swapped: at the 55 degree gameplay camera the right upper arm stretches visibly (armour plates smeared,
roughly twice its length) when it goes overhead in SlamWindup (frames 15-20) and Slam (frames 0-4); the welded
chin/shoulder bridge (an Arm_R/Jaw edge 4.75x in the Hero, Arm_R 3.06x in LOD0) and, in Swipe frame 16, a Neck/Arm_L
edge (7.2x, Hero) are the worst. Next step: raise and move inward the Arm_L/Arm_R pivots (prep_heat.py KAIJU_HEADS),
re-run heat.py and opt.py, then re-render. To swap: copy over ../Kaiju_Adult.fbx (keep the .meta) and add
Kaiju/Kaiju_Adult.fbx back to HY_KEEP in tools/art-sync.sh. Old model: ../_fallback~/Kaiju_Adult.fbx.
