#!/usr/bin/env bash
# b12-w: the joiner's side of a two-runner room on GitHub Actions. The HOST job makes the room itself (POST /rooms right
# before its game starts, so it connects well inside the relay's 2-minute host window) and uploads the code as a workflow
# artifact; this script, on the JOINER runner, waits for that artifact (one check every 10 s; artifacts are listed by the
# REST API as soon as they are uploaded, while the run is still going), downloads it and prints its key=value lines.
# Why: on 2026-11-14 (run 43627866241) a separate job made the room and both runners raced; the macOS joiner connected 9 s
# before the Windows host, heard "no room with that code", gave up at once (exit 5), and the host then waited 15 minutes
# for nobody (exit 4). Waiting for the host's own artifact orders the two without any clock.
#   tools/online/wait-room.sh ARTIFACT_NAME OUT_DIR [MAX_SECONDS]       (needs GH_TOKEN, GITHUB_REPOSITORY, GITHUB_RUN_ID)
set -euo pipefail
NAME="${1:?artifact name}"; OUT="${2:?out dir}"; MAX="${3:-1500}"
mkdir -p "$OUT"
start=$(date +%s); id=""
while :; do
  id=$(gh api "repos/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID/artifacts?name=$NAME" --jq '.artifacts[0].id // empty' 2>/dev/null || true)
  [ -n "$id" ] && break
  now=$(date +%s)
  if (( now - start > MAX )); then echo "wait-room: no '$NAME' artifact after $MAX s (the host job never made its room)" >&2; exit 4; fi
  sleep 10
done
echo "wait-room: '$NAME' artifact $id after $(( $(date +%s) - start )) s"
gh api "repos/$GITHUB_REPOSITORY/actions/artifacts/$id/zip" > "$OUT/room.zip"
(cd "$OUT" && unzip -o -q room.zip && rm -f room.zip)
tr -d '\r' < "$OUT/room.txt" > "$OUT/room.clean" && mv "$OUT/room.clean" "$OUT/room.txt"
cat "$OUT/room.txt"
