#!/bin/bash
# Extracts the LOD build inputs (LFS objects) from the art clones at their pushed heads into <scratch>/src.
set -e
S="$1/src"; mkdir -p "$S/kaijuface"
ex(){ (cd ~/"$1" && git show "$2:$3" | git lfs smudge > "$S/$4"); }
ex critical-mass-unity-art b8216e6 Assets/_Game/Art/Characters/Warrior.fbx Warrior.fbx
ex critical-mass-unity-art b8216e6 Assets/_Game/Art/Characters/Warrior_Face.png Warrior_Face.png
ex critical-mass-unity-art b8216e6 'Assets/_Game/Art/Characters/_source~/kaykit/Knight.glb' Knight.glb
ex critical-mass-unity-art-farmer cacc440 Assets/_Game/Art/Farmer/Farmer.fbx Farmer.fbx
ex critical-mass-unity-art-farmer cacc440 Assets/_Game/Art/Farmer/Farmer_Face.png Farmer_Face.png
ex critical-mass-unity-art-farmer cacc440 'Assets/_Game/Art/Farmer/_source~/farmer.blend' farmer.blend
ex critical-mass-unity-art-miner 691e62a Assets/_Game/Art/Miner/Miner.fbx Miner.fbx
ex critical-mass-unity-art-miner 691e62a Assets/_Game/Art/Miner/MinerFace.png MinerFace.png
ex critical-mass-unity-art-kaiju d908740 Assets/_Game/Art/Kaiju/Kaiju_Adult.fbx Kaiju_Adult.fbx
ex critical-mass-unity-art-kaiju d908740 Assets/_Game/Art/Kaiju/Kaiju_Hatchling.fbx Kaiju_Hatchling.fbx
for f in Kaiju_Face Kaiju_Face_Angry Kaiju_Face_Berserk Kaiju_Face_Blink Kaiju_Face_Happy Kaiju_Face_Hurt Kaiju_Face_Sleepy; do
  ex critical-mass-unity-art-kaiju d908740 Assets/_Game/Art/Kaiju/$f.png kaijuface/$f.png; done
cp "$S/kaijuface/Kaiju_Face.png" "$S/"
for p in Demolitions:09e7ac0 Builder:fff2850 Healer:de9d0bb; do
  c=${p%%:*}; r=${p##*:}; l=$(echo $c | tr A-Z a-z)
  ex critical-mass-unity-art-$l "$r" "Assets/_Game/Art/$c/$c.fbx" "$c.fbx"
  ex critical-mass-unity-art-$l "$r" "Assets/_Game/Art/$c/${c}Face.png" "${c}Face.png"
done
echo "EXTRACTED $(ls "$S" | wc -l) files"
