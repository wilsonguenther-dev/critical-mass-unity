Pre-switch Kaiju adult (navy plates, cyan crystals, face decal with seven expression atlases), kept as the fallback for the
green-crystal adult swapped in on 2026-11-14 (branch art/rig-kaiju-2, owner: "Switch, I love it"). Unity ignores this folder.

To go back: copy this FBX over Assets/_Game/Art/Kaiju/Kaiju_Adult.fbx (its .meta stays, so the GUID and import settings do
not change) and restore the Kaiju_Adult ModelSpec line in ArtMapping.cs as its comment says: Bones = 22,
Authored = KaijuDefault(GlowAzure), FaceTexture = KaijuFace, Expressions = KaijuExpressionTextures, drop VertexColourFace.
