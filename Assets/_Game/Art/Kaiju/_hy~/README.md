# Kaiju adult HY candidate (not swapped)

Kaiju_Adult.fbx here is the Tencent HY build (2026-11-13, art/characters-hy): own 20-bone rig (the native
Kaiju.blend Adult_Rig) and its 8 clips, Hero 38,000 / LOD0 8,900 / LOD1 4,400 / shadow 1,386 triangles, glowing
seams as mask code 6 (vertex alpha 0.75) recoloured GlowAzure #13F1F4. It is NOT wired in because the HY face is
painted into the vertex colour: there is no face-decal UV, so the expression atlases and the ModelFacing face check
would break. Needs a face-decal UV fit (or a VertexColourFace path for Kaiju expressions) first. To swap: copy over
../Kaiju_Adult.fbx (keep the .meta). Old model: ../_fallback~/Kaiju_Adult.fbx.
