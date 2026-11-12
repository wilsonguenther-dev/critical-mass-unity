#!/usr/bin/env bash
# Loop shutdown (decision 8): stop every watcher by its pidfile (never by name pattern) and prove each pid is gone,
# remove lanes after confirming each lane branch is pushed or bundled, write a resume note to loop/logs/.
#   loop/bin/shutdown.sh [--dry-run]
# --dry-run still stops the watchers (they are processes this run started and must be proven gone); it only prints
# the lane removals and leaves run state alone.
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DRY=""
case "${1:-}" in --dry-run) DRY="--dry-run" ;; "") ;; *) echo "shutdown: unknown argument $1"; exit 2 ;; esac
echo "shutdown ${DRY:-(real)} at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
python3 "$HERE/cmloop.py" shutdown $DRY
RC=$?
echo "shutdown: exit $RC"
exit $RC
