#!/usr/bin/env bash
# Art sync (2026-11-12): copy the Blender FBX files out of the art clones into this project, rebuild the KayKit clip
# file, and re-import them in Unity under the import contract (Editor/Art/ArtImportPostprocessor.cs), then rebuild the
# ArtCatalog. Re-importing a newer art version is this one command; the clones are only read, never edited.
#
# Usage: tools/art-sync.sh [--no-unity] [--no-blender]
#   ART_KAIJU   (default ~/critical-mass-unity-art-kaiju)  Assets/_Game/Art/Kaiju/*.fbx
#   ART_MINER   (default ~/critical-mass-unity-art-miner)  Assets/_Game/Art/Miner/{Miner,MinerBarricade}.fbx
#   ART_WARRIOR (default ~/critical-mass-unity-art)        Assets/_Game/Art/Characters/Warrior.fbx (+ kaykit/Knight.glb)
#   ART_PROPS   (default ~/critical-mass-unity-art-props)  Assets/_Game/Art/Props/*.fbx
#   ART_SYNC_TIMEOUT seconds for the Unity re-import (default 900); BLENDER (default: blender on PATH)
set -euo pipefail
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
ART="$ROOT/Assets/_Game/Art"

# A Git LFS pointer is ~130 bytes of text; a real FBX starts with "Kaydara FBX Binary".
copy_fbx() {
  local src="$1" dst_dir="$2"
  [[ -f "$src" ]] || { echo "art-sync: MISSING $src" >&2; return 1; }
  if [[ "$(head -c 18 "$src")" != "Kaydara FBX Binary" ]]; then
    echo "art-sync: $src is not a binary FBX (LFS pointer? run git lfs pull in its clone)" >&2; return 1
  fi
  mkdir -p "$dst_dir"
  local clone; clone="$(cd "$(dirname "$src")" && git rev-parse --show-toplevel 2>/dev/null || echo "?")"
  printf '%s\t%s\t%s\t%s\n' "$(basename "$src")" "$(shasum -a 256 "$src" | cut -c1-16)" \
    "$(git -C "$clone" rev-parse --short HEAD 2>/dev/null || echo "?")$(git -C "$clone" diff --quiet HEAD -- "$src" 2>/dev/null || echo "+dirty")" "$src" >> "$SOURCES.tmp"
  if ! cmp -s "$src" "$dst_dir/$(basename "$src")"; then
    cp "$src" "$dst_dir/"
    echo "art-sync: updated $(basename "$src") <- $src"
  fi
}

# tools/art/art-sources.tsv records what was copied: file, sha256 prefix, clone commit (+dirty = uncommitted export), path.
SOURCES="$ROOT/tools/art/art-sources.tsv"
: > "$SOURCES.tmp"
n=0
for f in "$KAIJU"/Assets/_Game/Art/Kaiju/*.fbx; do copy_fbx "$f" "$ART/Kaiju"; n=$((n+1)); done
for f in Miner MinerBarricade; do copy_fbx "$MINER/Assets/_Game/Art/Miner/$f.fbx" "$ART/Miner"; n=$((n+1)); done
copy_fbx "$WARRIOR/Assets/_Game/Art/Characters/Warrior.fbx" "$ART/Characters"; n=$((n+1))
for f in "$PROPS"/Assets/_Game/Art/Props/*.fbx; do copy_fbx "$f" "$ART/Props"; n=$((n+1)); done
mv "$SOURCES.tmp" "$SOURCES"
echo "art-sync: $n FBX file(s) checked (sources: tools/art/art-sources.tsv)"

# Warrior.fbx and Miner.fbx carry the KayKit rig but no clips: build the shared clip FBX from the CC0 Knight.glb.
KNIGHT="$WARRIOR/Assets/_Game/Art/Characters/_source~/kaykit/Knight.glb"
if [[ $RUN_BLENDER -eq 1 ]]; then
  BL="${BLENDER:-$(command -v blender || true)}"
  if [[ -n "$BL" && -f "$KNIGHT" ]]; then
    tmp="$(mktemp -d)"
    perl -e 'alarm 300; exec @ARGV' "$BL" -b --factory-startup --python "$ROOT/tools/art/build_kaykit_clips.py" -- "$KNIGHT" "$tmp/KayKit_Clips.fbx" > "$tmp/blender.log" 2>&1 \
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
perl -e 'alarm shift; exec @ARGV' "${ART_SYNC_TIMEOUT:-900}" "$UNITY" -batchmode -nographics -projectPath "$ROOT" \
  -executeMethod CriticalMass.Editor.Art.ArtSync.Reimport -logFile "$LOG"
code=$?
set -e
grep -E "^\[ArtCatalog\]|^\[ArtSync\]|^\[ArtImport\]|error CS" "$LOG" || true
echo "art-sync: Unity exit=$code (log: $LOG)"
exit "$code"
