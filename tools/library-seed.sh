#!/usr/bin/env bash
# Seed a new worktree's Library/ from an existing one with APFS copy-on-write clones (ci/offload, 2026-11-13).
# A fresh worktree otherwise re-imports every asset (10 to 20 minutes of one Unity slot). A clone (`cp -c`, clonefile(2))
# shares the source's disk blocks until either side changes a file, so a 3.6 GB Library costs almost no disk at first
# and grows only by what the new worktree re-imports. Unity's import cache (Artifacts, ArtifactDB, SourceAssetDB) is keyed
# by GUID and content hash, so it carries over; the folders that hold absolute paths of the old worktree are deleted
# (Bee, BuildPlayerData, ScriptAssemblies: the batch-4 setup failure of 2026-11-13 07:58), as are lock and pid files.
#   tools/library-seed.sh [--from LIBRARY_DIR] [--force] [DEST_WORKTREE]   (default DEST: this checkout)
# Default source: the most recently used Library of ~/critical-mass-unity and ~/cm-wt-*, not DEST's own.
# Refuses: DEST already has a Library (unless --force: it is moved aside, then deleted after the clone succeeds); the source
# project is open in a running Unity (Temp/UnityLockfile held); the volume is not APFS (no silent full copy); under 15 GB free.
set -euo pipefail
FROM=''; FORCE=0; DEST=''
while [[ $# -gt 0 ]]; do
  case "$1" in
    --from) FROM="${2:?}"; shift 2 ;;
    --force) FORCE=1; shift ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    *) DEST="$1"; shift ;;
  esac
done
DEST="$(cd "${DEST:-$(dirname "${BASH_SOURCE[0]}")/..}" && pwd)"
[[ -f "$DEST/ProjectSettings/ProjectVersion.txt" ]] || { echo "library-seed: $DEST is not a Unity project checkout" >&2; exit 64; }
if [[ -z "$FROM" ]]; then
  best=''; bestm=0
  for l in "$HOME"/critical-mass-unity/Library "$HOME"/cm-wt-*/Library; do
    [[ -d "$l/Artifacts" && -f "$l/ArtifactDB" && "$l" != "$DEST/Library" ]] || continue
    m=$(stat -f %m "$l/ArtifactDB"); (( m > bestm )) && { best="$l"; bestm=$m; }
  done
  FROM="$best"
fi
[[ -n "$FROM" && -d "$FROM/Artifacts" ]] || { echo "library-seed: no source Library found (pass --from DIR)" >&2; exit 78; }
FROM="$(cd "$FROM" && pwd)"; SRCPROJ="$(dirname "$FROM")"
if [[ -e "$SRCPROJ/Temp/UnityLockfile" ]] && lsof "$SRCPROJ/Temp/UnityLockfile" >/dev/null 2>&1; then
  echo "library-seed: $SRCPROJ is open in a running Unity; cloning a Library mid-import is unsafe. Try again after it exits." >&2; exit 75
fi
[[ "$(diskutil info "$(df "$DEST" | awk 'NR==2{print $NF}')" 2>/dev/null | awk -F: '/Type \(Bundle\)/{gsub(/ /,"",$2);print $2}')" == apfs ]] || { echo "library-seed: $DEST is not on APFS; refusing a full copy" >&2; exit 78; }
free_before=$(df -g "$DEST" | awk 'NR==2{print $4}')
(( free_before >= 15 )) || { echo "library-seed: only ${free_before} GB free (floor 15)" >&2; exit 75; }
if [[ -e "$DEST/Library" ]]; then
  [[ $FORCE -eq 1 ]] || { echo "library-seed: $DEST/Library exists (use --force to replace it)" >&2; exit 64; }
  mv "$DEST/Library" "$DEST/Library.old.$$"
fi
t0=$(date +%s)
cp -c -R "$FROM" "$DEST/Library"
rm -rf "$DEST/Library/Bee" "$DEST/Library/BuildPlayerData" "$DEST/Library/ScriptAssemblies" \
       "$DEST/Library/ilpp.pid" "$DEST/Library/ArtifactDB-lock" "$DEST/Library/SourceAssetDB-lock"
[[ -e "$DEST/Library.old.$$" ]] && rm -rf "$DEST/Library.old.$$"
free_after=$(df -g "$DEST" | awk 'NR==2{print $4}')
printf '{"tool":"library-seed","ts":"%s","from":"%s","to":"%s","seconds":%d,"free_gb_before":%d,"free_gb_after":%d}\n' \
  "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$FROM" "$DEST/Library" "$(( $(date +%s) - t0 ))" "$free_before" "$free_after"
