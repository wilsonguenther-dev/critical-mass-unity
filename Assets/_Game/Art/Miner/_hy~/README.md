# Miner HY candidate (not swapped)

Miner.fbx here is the Tencent HY build (2026-11-13, art/characters-hy). It is NOT wired in: the generated
body has a much deeper torso and shorter, closer legs than the 41-bone rig (bone fit: torso x1.37, leg spacing
x0.53, arms moved 0.26 m), and the KayKit clips tear it 2-5x more than the swapped Warrior (LOD0 run 97 visible
stretched edges, chop 167). To swap after a weight/fit pass: copy it over ../Miner.fbx (keep the .meta) and set
the Miner ModelSpec to VertexColourFace = true, FaceSlot = -1, no FaceTexture. Old model: ../_fallback~/Miner.fbx.
