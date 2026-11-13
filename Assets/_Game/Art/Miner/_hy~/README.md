# Miner HY candidate (staged after the rigging pass)

Miner.fbx here is the HY Miner with the rigging-pass weights (2026-11-13, art/characters-hy-rig). At the 55 degree
gameplay camera the worst frame of every match clip shows no visible tear, and p99 edge stretch went 2.81 -> 2.03.
It is still staged because CI (run 43500072619) fails ArtImportTests.IsTheBibleHeightWithItsFeetOnTheGround(Miner):
the rest-pose (T) mesh reaches 0.112 m below the pivot (tolerance 0.10). The A-pose legs are spread wide, so they
are longer once straightened in the rest pose. Fix: lift the leg joints or export the A-pose as the bind pose, then
swap. To swap: copy over ../Miner.fbx (keep the .meta), set the Miner ModelSpec to VertexColourFace = true with no
FaceTexture or FaceSlot, and add Miner/Miner.fbx to HY_KEEP in tools/art-sync.sh. Old model: ../_fallback~/Miner.fbx.
