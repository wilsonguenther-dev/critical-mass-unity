#!/usr/bin/env bash
# Run any command on the second Mac (the MacBook Air worker) inside its queue lock, the same lock the play-tests use.
#   tools/remote-run.sh [--host HOST] [--put LOCAL_PATH]... [--get REMOTE_REL_PATH LOCAL_DIR]... [--lock-wait S]
#                       [--timeout S] -- COMMAND...
# COMMAND runs with bash in ~/cm-remote/work on the box, with ~/cm-remote/env.sh sourced (PATH gets the user-space
# ffmpeg, the Python venv with Pillow and numpy, and dotnet). --put copies files or folders into ~/cm-remote/work first;
# --get copies a path (relative to ~/cm-remote/work) back afterwards. Prints the command's output; exits with its code
# (75 queue wait ran out, 124 timeout, 69 box unreachable). Nothing outside ~/cm-remote is touched by this tool.
# Example: tools/remote-run.sh --put art/intro.mov --get out.mp4 Logs/ -- ffmpeg -y -i intro.mov -vf scale=1280:-2 out.mp4
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/tools/remote/_remote-lib.sh"
PUTS=(); GETS=(); LOCK_WAIT=3600; TIMEOUT=3600
while [[ $# -gt 0 ]]; do
  case "$1" in
    --host) REMOTE_HOST="${2:?}"; shift 2 ;;
    --put) PUTS+=("${2:?}"); shift 2 ;;
    --get) GETS+=("${2:?}" "${3:?}"); shift 3 ;;
    --lock-wait) LOCK_WAIT="${2:?}"; shift 2 ;;
    --timeout) TIMEOUT="${2:?}"; shift 2 ;;
    --) shift; break ;;
    *) break ;;
  esac
done
[[ $# -gt 0 ]] || { sed -n '3,4p' "${BASH_SOURCE[0]}" | sed 's/^# *//' >&2; exit 64; }
# One argument = a shell command line (pipes, ;, $VARS work); several = one command with those exact arguments.
if [[ $# -eq 1 ]]; then CMD="$1"; else CMD="$(printf '%q ' "$@")"; fi
remote_preflight
for p in ${PUTS[@]+"${PUTS[@]}"}; do rsync_to "$p" "$REMOTE_USER@$REMOTE_HOST:cm-remote/work/"; done
ID="$(date -u +%Y%m%dT%H%M%SZ)-run-$$"
remote_start "$ID" "$((LOCK_WAIT + TIMEOUT))" --exec "$CMD" --lock-wait "$LOCK_WAIT"
code="$REMOTE_CODE"
rssh "cat ~/cm-remote/runs/$ID/output.txt 2>/dev/null; rm -rf ~/cm-remote/runs/$ID" || true
i=0
while (( i < ${#GETS[@]} )); do
  mkdir -p "${GETS[i+1]}"; rsync_to "$REMOTE_USER@$REMOTE_HOST:cm-remote/work/${GETS[i]}" "${GETS[i+1]}/" || code=1; i=$((i + 2))
done
exit "$code"
