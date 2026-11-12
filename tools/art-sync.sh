#!/usr/bin/env bash
# Art sync (2026-11-12): copy the Blender FBX files out of the art clones into this project, rebuild the KayKit clip
# file, and re-import them in Unity under the import contract (Editor/Art/ArtImportPostprocessor.cs), then rebuild the
# ArtCatalog. Re-importing a newer art version is this one command; the clones are only read, never edited.
#
# Usage: tools/art-sync.sh [--no-unity] [--no-blender]
#   ART_KAIJU   (default ~/critical-mass-unity-art-kaiju)  Assets/_Game/Art/Kaiju/*.fbx
#   ART_MINER   (default ~/critical-mass-unity-art-miner)  Assets/_Game/Art/Miner/{Miner,MinerBarricade}.fbx
#   ART_WARRIOR (default ~/critical-mass-unity-art)        Assets/_Game/Art/Characters/Warrior.fbx (+ kaykit/Knight.glb)
#   ART_PROPS   (default ~/critical-mass-unity-art-props)  Assets/_Game/Art/Props/*.fbx            (pinned, see below)
#   ART_ENV     (default ~/critical-mass-unity-art-env)    Assets/_Game/Art/Environment/*.fbx      (pinned)
#   ART_BASES   (default ~/critical-mass-unity-art-bases)  Assets/_Game/Art/Bases/*.fbx + bases_placements.json (pinned)
# Pinned clones (world art, 2026-11-12): tools/art/art-sources.tsv starts with "#pin<TAB>name<TAB>commit" lines; a pinned
# clone is read at that commit with `git archive` (never its working folder, which may hold a newer export in progress).
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
ENV_CLONE="${ART_ENV:-$HOME/critical-mass-unity-art-env}"
BASES_CLONE="${ART_BASES:-$HOME/critical-mass-unity-art-bases}"
ART="$ROOT/Assets/_Game/Art"

# A Git LFS pointer is ~130 bytes of text; a real FBX starts with "Kaydara FBX Binary".
copy_fbx() {
  local src="$1" dst_dir="$2" pinned="${3:-}"
  [[ -f "$src" ]] || { echo "art-sync: MISSING $src" >&2; return 1; }
  if [[ "$(head -c 18 "$src")" != "Kaydara FBX Binary" ]]; then
    echo "art-sync: $src is not a binary FBX (LFS pointer? run git lfs pull in its clone)" >&2; return 1
  fi
  mkdir -p "$dst_dir"
  local rev label="$src"
  if [[ -n "$pinned" ]]; then
    rev="$pinned"; label="${src#"$PIN_TMP"/}"
  else
    local clone; clone="$(cd "$(dirname "$src")" && git rev-parse --show-toplevel 2>/dev/null || echo "?")"
    rev="$(git -C "$clone" rev-parse --short HEAD 2>/dev/null || echo "?")$(git -C "$clone" diff --quiet HEAD -- "$src" 2>/dev/null || echo "+dirty")"
  fi
  printf '%s\t%s\t%s\t%s\n' "$(basename "$src")" "$(shasum -a 256 "$src" | cut -c1-16)" "$rev" "$label" >> "$SOURCES.tmp"
  if ! cmp -s "$src" "$dst_dir/$(basename "$src")"; then
    cp "$src" "$dst_dir/"
    echo "art-sync: updated $(basename "$src") <- $src"
  fi
}

# tools/art/art-sources.tsv records what was copied: file, sha256 prefix, clone commit (+dirty = uncommitted export), path.
SOURCES="$ROOT/tools/art/art-sources.tsv"
pin_of() { awk -F'\t' -v n="$1" '$1 == "#pin" && $2 == n { print $3 }' "$SOURCES"; }
grep '^#' "$SOURCES" > "$SOURCES.tmp" || true
PIN_TMP="$(mktemp -d)"
trap 'rm -rf "$PIN_TMP"' EXIT
# extract_pinned NAME CLONE SUBDIR: the clone's SUBDIR at the pinned commit into $PIN_TMP/NAME (echoes the folder).
extract_pinned() {
  local name="$1" clone="$2" sub="$3" rev; rev="$(pin_of "$name")"
  [[ -n "$rev" ]] || { echo "art-sync: no #pin line for $name in $SOURCES" >&2; return 1; }
  mkdir -p "$PIN_TMP/$name"
  git -C "$clone" archive "$rev" "$sub" | tar -x -C "$PIN_TMP/$name" \
    || { echo "art-sync: git archive $rev $sub failed in $clone" >&2; return 1; }
  if git -C "$clone" lfs ls-files "$rev" 2>/dev/null | grep -q .; then
    (cd "$PIN_TMP/$name" && for f in $(find . -name '*.fbx'); do
      if [[ "$(head -c 18 "$f")" != "Kaydara FBX Binary" ]]; then git -C "$clone" lfs smudge < "$f" > "$f.tmp" && mv "$f.tmp" "$f"; fi
    done)
  fi
  echo "$PIN_TMP/$name/$sub"
}
n=0
for f in "$KAIJU"/Assets/_Game/Art/Kaiju/*.fbx; do copy_fbx "$f" "$ART/Kaiju"; n=$((n+1)); done
for f in Miner MinerBarricade; do copy_fbx "$MINER/Assets/_Game/Art/Miner/$f.fbx" "$ART/Miner"; n=$((n+1)); done
copy_fbx "$WARRIOR/Assets/_Game/Art/Characters/Warrior.fbx" "$ART/Characters"; n=$((n+1))
P_DIR="$(extract_pinned props "$PROPS" Assets/_Game/Art/Props)"
for f in "$P_DIR"/*.fbx; do copy_fbx "$f" "$ART/Props" "$(pin_of props)"; n=$((n+1)); done
E_DIR="$(extract_pinned environment "$ENV_CLONE" Assets/_Game/Art/Environment)"
for f in "$E_DIR"/*.fbx; do copy_fbx "$f" "$ART/Environment" "$(pin_of environment)"; n=$((n+1)); done
B_DIR="$(extract_pinned bases "$BASES_CLONE" Assets/_Game/Art/Bases)"
for f in "$B_DIR"/*.fbx; do copy_fbx "$f" "$ART/Bases" "$(pin_of bases)"; n=$((n+1)); done
# The bases layouts (Unity positions and yaw per site) become a TextAsset the world dresser reads (JsonUtility form).
mkdir -p "$ROOT/Assets/_Game/Resources/World"
python3 -I "$ROOT/tools/art/bases_placements_to_unity.py" "$B_DIR/bases_placements.json" "$ROOT/Assets/_Game/Resources/World/bases_placements.json"
printf '%s\t%s\t%s\t%s\n' "bases_placements.json" "$(shasum -a 256 "$B_DIR/bases_placements.json" | cut -c1-16)" "$(pin_of bases)" \
  "Assets/_Game/Art/Bases/bases_placements.json" >> "$SOURCES.tmp"
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
