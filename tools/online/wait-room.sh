#!/usr/bin/env bash
# b12-w: the joiner's side of a two-runner room on GitHub Actions. The HOST job makes the room itself (POST /rooms right
# before its game starts, so it connects well inside the relay's 2-minute host window) and uploads the code as a workflow
# artifact; this script, on the JOINER runner, waits for that artifact (one check every 10 s; artifacts are listed by the
# REST API as soon as they are uploaded, while the run is still going), downloads it and prints its key=value lines.
# Why: on 2026-11-14 (run 43627866241) a separate job made the room and both runners raced; the macOS joiner connected 9 s
# before the Windows host, heard "no room with that code", gave up at once (exit 5), and the host then waited 15 minutes
# for nobody (exit 4). Waiting for the host's own artifact orders the two without any clock.
# The host side uses it the other way round too: it waits for joiner-ready-<mode> before it makes the room, so the host never
# sits alone in a room (a lone Windows player host lost its connection about 30 s after ROOM READY in run 43955919705).
#   tools/online/wait-room.sh ARTIFACT_NAME OUT_DIR [MAX_SECONDS] [POLL_SECONDS]   (needs GH_TOKEN, GITHUB_REPOSITORY, GITHUB_RUN_ID;
#   the artifact holds one file, room.txt, of key=value lines)
set -euo pipefail
NAME="${1:?artifact name}"; OUT="${2:?out dir}"; MAX="${3:-1500}"; POLL="${4:-10}"
mkdir -p "$OUT"
start=$(date +%s); id=""
while :; do
  id=$(gh api "repos/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID/artifacts?name=$NAME" --jq '.artifacts[0].id // empty' 2>/dev/null || true)
  [ -n "$id" ] && break
  now=$(date +%s)
  if (( now - start > MAX )); then echo "wait-room: no '$NAME' artifact after $MAX s (the host job never made its room)" >&2; exit 4; fi
  sleep "$POLL"
done
echo "wait-room: '$NAME' artifact $id after $(( $(date +%s) - start )) s"
if ! gh run download "$GITHUB_RUN_ID" -R "$GITHUB_REPOSITORY" -n "$NAME" -D "$OUT"; then   # unzips itself
  # Fallback: the artifact zip through the REST API, unpacked by Python (Windows runners have no unzip).
  if [ "${OS:-}" = Windows_NT ]; then PY=python; else PY=python3; fi
  gh api "repos/$GITHUB_REPOSITORY/actions/artifacts/$id/zip" > "$OUT/room.zip"
  "$PY" -m zipfile -e "$OUT/room.zip" "$OUT" && rm -f "$OUT/room.zip"
fi
tr -d '\r' < "$OUT/room.txt" > "$OUT/room.clean" && mv "$OUT/room.clean" "$OUT/room.txt"
cat "$OUT/room.txt"
