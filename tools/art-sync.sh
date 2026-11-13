#!/usr/bin/env bash
# Art sync (2026-11-12): copy the Blender FBX files out of the art clones into this project, rebuild the KayKit clip
# file, and re-import them in Unity under the import contract (Editor/Art/ArtImportPostprocessor.cs), then rebuild the
# ArtCatalog. Re-importing a newer art version is this one command; the clones are only read, never edited.
#
# Usage: tools/art-sync.sh [--no-unity] [--no-blender]
#   ART_KAIJU   (default ~/critical-mass-unity-art-kaiju)  Assets/_Game/Art/Kaiju/*.fbx
#   ART_MINER   (default ~/critical-mass-unity-art-miner)  Assets/_Game/Art/Miner/{Miner,MinerBarricade}.fbx
#   ART_WARRIOR (default ~/critical-mass-unity-art)        Assets/_Game/Art/Characters/Warrior.fbx, Warrior_Face.png (+ kaykit/Knight.glb)
#   ART_FARMER  (default ~/critical-mass-unity-art-farmer) Assets/_Game/Art/Farmer/Farmer.fbx (+ Farmer_OutlineW.json from farmer.blend)
#   ART_PROPS   (default ~/critical-mass-unity-art-props)  Assets/_Game/Art/Props/*.fbx            (pinned, see below)
#   ART_ENV     (default ~/critical-mass-unity-art-env)    Assets/_Game/Art/Environment/*.fbx      (pinned)
#   ART_BASES   (default ~/critical-mass-unity-art-bases)  Assets/_Game/Art/Bases/*.fbx + bases_placements.json (pinned)
# Every clone is pinned (art refresh 2): tools/art/art-sources.tsv starts with "#pin<TAB>name<TAB>commit" lines (kaiju,
# miner, warrior, farmer, props, environment, bases); each is read at that commit with `git archive`, never its working folder.
#   ART_SYNC_TIMEOUT seconds for the Unity re-import (default 900); BLENDER (default: blender on PATH)
#   Every Blender and Unity call goes through the machine lock, cm-heavy (DG-44): CM_HEAVY (default ~/bin/cm-heavy).
set -euo pipefail
CM_HEAVY="${CM_HEAVY:-$HOME/bin/cm-heavy}"
[[ -x "$CM_HEAVY" ]] || { echo "art-sync: cm-heavy not found at $CM_HEAVY (DG-44)" >&2; exit 127; }
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_UNITY=1; RUN_BLENDER=1
for a in "$@"; do
  case "$a" in
    --no-unity) RUN_UNITY=0 ;;
    --no-blender) RUN_BLENDER=0 ;;
    *) echo "usage: art-sync.sh [--no-unity] [--no-blender]" >&2; exit 64 ;;
  esac
done
KAIJU="${ART_KAIJU:-$HOME/critical-mass-unity-art-kaiju}"
MINER="${ART_MINER:-$HOME/critical-mass-unity-art-miner}"
WARRIOR="${ART_WARRIOR:-$HOME/critical-mass-unity-art}"
PROPS="${ART_PROPS:-$HOME/critical-mass-unity-art-props}"
ENV_CLONE="${ART_ENV:-$HOME/critical-mass-unity-art-env}"
BASES_CLONE="${ART_BASES:-$HOME/critical-mass-unity-art-bases}"
FARMER="${ART_FARMER:-$HOME/critical-mass-unity-art-farmer}"
ART="$ROOT/Assets/_Game/Art"

# A Git LFS pointer is ~130 bytes of text; a real FBX starts with "Kaydara FBX Binary", a real PNG with \x89PNG.
real_file() {
  case "$1" in
    *.fbx|*.FBX) [[ "$(head -c 18 "$1")" == "Kaydara FBX Binary" ]] ;;
    *.png) [[ "$(head -c 4 "$1" | tail -c 3)" == "PNG" ]] ;;
    *) [[ "$(head -c 40 "$1")" != "version https://git-lfs.github.com/spec/v1"* ]] ;;
  esac
}
copy_art() {
  local src="$1" dst_dir="$2" pinned="$3"
  [[ -f "$src" ]] || { echo "art-sync: MISSING $src" >&2; return 1; }
  real_file "$src" || { echo "art-sync: $src is not a real file (LFS pointer not smudged?)" >&2; return 1; }
  mkdir -p "$dst_dir"
  printf '%s\t%s\t%s\t%s\n' "$(basename "$src")" "$(shasum -a 256 "$src" | cut -c1-16)" "$pinned" "${src#"$PIN_TMP"/}" >> "$SOURCES.tmp"
  if ! cmp -s "$src" "$dst_dir/$(basename "$src")"; then
    cp "$src" "$dst_dir/"
    echo "art-sync: updated $(basename "$src") <- ${src#"$PIN_TMP"/}"
  fi
}
# prune_stale DST_DIR SRC_DIR: an FBX in a fully synced folder that the pinned source no longer has is removed (with
# its .meta), so a re-run at a new pin leaves exactly the pinned set.
prune_stale() {
  local dst="$1" src="$2" f
  for f in "$dst"/*.fbx; do
    [[ -f "$f" ]] || continue
    [[ -f "$src/$(basename "$f")" ]] || { rm -f "$f" "$f.meta"; echo "art-sync: removed stale $(basename "$f")"; }
  done
}

# tools/art/art-sources.tsv records what was copied: file, sha256 prefix, pinned commit, path inside the pinned tree.
# EVERY source is pinned (2026-11-12, art refresh 2): the "#pin<TAB>name<TAB>commit" lines at the top of the file say
# which commit of which clone is read, with `git archive` (never a clone's working folder, where another agent may be
# writing a half-finished export). Re-running this script at the same pins reproduces the same files byte for byte.
SOURCES="$ROOT/tools/art/art-sources.tsv"
pin_of() { awk -F'\t' -v n="$1" '$1 == "#pin" && $2 == n { print $3 }' "$SOURCES"; }
grep '^#' "$SOURCES" > "$SOURCES.tmp" || true
PIN_TMP="$(mktemp -d)"
trap 'rm -rf "$PIN_TMP" "$SOURCES.tmp"' EXIT
# extract_pinned NAME CLONE SUBDIR: the clone's SUBDIR at the pinned commit into $PIN_TMP/NAME (echoes the folder),
# LFS pointers smudged from the clone's LFS store.
extract_pinned() {
  local name="$1" clone="$2" sub="$3" rev; rev="$(pin_of "$name")"
  [[ -n "$rev" ]] || { echo "art-sync: no #pin line for $name in $SOURCES" >&2; return 1; }
  git -C "$clone" cat-file -e "$rev^{commit}" 2>/dev/null || { echo "art-sync: $clone has no commit $rev (fetch it)" >&2; return 1; }
  mkdir -p "$PIN_TMP/$name"
  git -C "$clone" archive "$rev" "$sub" | tar -x -C "$PIN_TMP/$name" \
    || { echo "art-sync: git archive $rev $sub failed in $clone" >&2; return 1; }
  local f
  while IFS= read -r f; do
    if [[ "$(head -c 40 "$f")" == "version https://git-lfs.github.com/spec/v1"* ]]; then
      git -C "$clone" lfs smudge < "$f" > "$f.tmp" && mv "$f.tmp" "$f"
    fi
  done < <(find "$PIN_TMP/$name" -type f -size -2k)
  echo "$PIN_TMP/$name/$sub"
}
n=0
K_DIR="$(extract_pinned kaiju "$KAIJU" Assets/_Game/Art/Kaiju)"
for f in "$K_DIR"/*.fbx "$K_DIR/Kaiju_Face.png"; do copy_art "$f" "$ART/Kaiju" "$(pin_of kaiju)"; n=$((n+1)); done
prune_stale "$ART/Kaiju" "$K_DIR"
M_DIR="$(extract_pinned miner "$MINER" Assets/_Game/Art/Miner)"
for f in Miner.fbx MinerBarricade.fbx MinerFace.png; do copy_art "$M_DIR/$f" "$ART/Miner" "$(pin_of miner)"; n=$((n+1)); done
W_DIR="$(extract_pinned warrior "$WARRIOR" Assets/_Game/Art/Characters)"
for f in Warrior.fbx Warrior_Face.png; do copy_art "$W_DIR/$f" "$ART/Characters" "$(pin_of warrior)"; n=$((n+1)); done
F_DIR="$(extract_pinned farmer "$FARMER" Assets/_Game/Art/Farmer)"
copy_art "$F_DIR/Farmer.fbx" "$ART/Farmer" "$(pin_of farmer)"; n=$((n+1))
P_DIR="$(extract_pinned props "$PROPS" Assets/_Game/Art/Props)"
for f in "$P_DIR"/*.fbx; do copy_art "$f" "$ART/Props" "$(pin_of props)"; n=$((n+1)); done
E_DIR="$(extract_pinned environment "$ENV_CLONE" Assets/_Game/Art/Environment)"
for f in "$E_DIR"/*.fbx; do copy_art "$f" "$ART/Environment" "$(pin_of environment)"; n=$((n+1)); done
prune_stale "$ART/Environment" "$E_DIR"
B_DIR="$(extract_pinned bases "$BASES_CLONE" Assets/_Game/Art/Bases)"
for f in "$B_DIR"/*.fbx; do copy_art "$f" "$ART/Bases" "$(pin_of bases)"; n=$((n+1)); done
prune_stale "$ART/Bases" "$B_DIR"
# The bases layouts (Unity positions and yaw per site) become a TextAsset the world dresser reads (JsonUtility form).
mkdir -p "$ROOT/Assets/_Game/Resources/World"
python3 -I "$ROOT/tools/art/bases_placements_to_unity.py" "$B_DIR/bases_placements.json" "$ROOT/Assets/_Game/Resources/World/bases_placements.json"
printf '%s\t%s\t%s\t%s\n' "bases_placements.json" "$(shasum -a 256 "$B_DIR/bases_placements.json" | cut -c1-16)" "$(pin_of bases)" \
  "Assets/_Game/Art/Bases/bases_placements.json" >> "$SOURCES.tmp"
# The Farmer's face carries no outline (its README: outline_w 0 on the face features). FBX drops that Blender vertex
# group, so it is read from the pinned farmer.blend into a sidecar the importer bakes into UV4 (outline weight).
FARMER_BLEND="$PIN_TMP/farmer/Assets/_Game/Art/Farmer/_source~/farmer.blend"
if [[ $RUN_BLENDER -eq 1 && -f "$FARMER_BLEND" ]]; then
  BL="${BLENDER:-$(command -v blender || echo /Applications/Blender.app/Contents/MacOS/Blender)}"
  CM_HEAVY_TIMEOUT=300 "$CM_HEAVY" blender "$BL" -b "$FARMER_BLEND" --factory-startup --python-exit-code 1 \
    --python "$ROOT/tools/art/outline_weights.py" -- "$ART/Farmer/Farmer_OutlineW.json" > "$PIN_TMP/ow.log" 2>&1 \
    || { echo "art-sync: outline weight export failed:"; tail -20 "$PIN_TMP/ow.log"; exit 1; } >&2
  grep OUTLINE_W_OK "$PIN_TMP/ow.log"
fi
cp "$SOURCES.tmp" "$SOURCES"
echo "art-sync: $n art file(s) checked (sources: tools/art/art-sources.tsv)"

# Warrior.fbx and Miner.fbx carry the KayKit rig but no clips: build the shared clip FBX from the CC0 Knight.glb.
KNIGHT="$W_DIR/_source~/kaykit/Knight.glb"
if [[ $RUN_BLENDER -eq 1 ]]; then
  BL="${BLENDER:-$(command -v blender || true)}"
  if [[ -n "$BL" && -f "$KNIGHT" ]]; then
    tmp="$(mktemp -d)"
    CM_HEAVY_TIMEOUT=300 "$CM_HEAVY" blender "$BL" -b --factory-startup --python "$ROOT/tools/art/build_kaykit_clips.py" -- "$KNIGHT" "$tmp/KayKit_Clips.fbx" > "$tmp/blender.log" 2>&1 \
      || { echo "art-sync: Blender clip build failed (log $tmp/blender.log)" >&2; exit 1; }
    grep -q KAYKIT_CLIPS_OK "$tmp/blender.log" || { echo "art-sync: clip build printed no OK line ($tmp/blender.log)" >&2; exit 1; }
    # The FBX header carries a timestamp, so compare the clip list only: keep the committed file unless clips changed.
    list="$(grep KAYKIT_CLIPS_OK "$tmp/blender.log" | sed 's/.*KayKit_Clips.fbx //')"
    if [[ ! -f "$ART/Characters/KayKit_Clips.fbx" || "$(cat "$ROOT/tools/art/kaykit_clips.list" 2>/dev/null)" != "$list" ]]; then
      cp "$tmp/KayKit_Clips.fbx" "$ART/Characters/KayKit_Clips.fbx"
      printf '%s\n' "$list" > "$ROOT/tools/art/kaykit_clips.list"
      echo "art-sync: rebuilt KayKit_Clips.fbx"
    fi
    rm -rf "$tmp"
  else
    echo "art-sync: no Blender or no $KNIGHT; keeping the committed KayKit_Clips.fbx"
  fi
fi

[[ $RUN_UNITY -eq 1 ]] || { echo "art-sync: copied; Unity re-import skipped (--no-unity)"; exit 0; }
source "$ROOT/tools/_unity-env.sh"
LOG="$ROOT/Logs/art-sync.log"
set +e
CM_HEAVY_TIMEOUT="${ART_SYNC_TIMEOUT:-900}" "$CM_HEAVY" unity "$UNITY" -batchmode -nographics -projectPath "$ROOT" \
  -executeMethod CriticalMass.Editor.Art.ArtSync.Reimport -logFile "$LOG"
code=$?
set -e
grep -E "^\[ArtCatalog\]|^\[ArtSync\]|^\[ArtImport\]|error CS" "$LOG" || true
echo "art-sync: Unity exit=$code (log: $LOG)"
exit "$code"
