Pre-HY fighter FBX (the art-landing LOD build), kept as the fallback for the HY swap of 2026-11-13 (branch art/characters-hy).
Unity ignores this folder. To go back: copy the FBX over the one in the parent folder (its .meta stays, so the GUID and
import settings do not change) and restore the class's ModelSpec line in ArtMapping.cs (FaceTexture, FaceSlot, OutlineWeights
as noted in the comment on that line; drop VertexColourFace).
