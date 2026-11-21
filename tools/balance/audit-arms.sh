#!/usr/bin/env bash
# AUDIT ONLY (ci/balance/audit-mech-*): run a list of arms "tag|args" (one per line on stdin) through the balance tool.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
while IFS='|' read -r tag args; do
  [[ -z "${tag// }" ]] && continue
  out="Temp/balance/arms/$tag"
  mkdir -p "$out"
  echo "=== arm $tag: $args"
  # shellcheck disable=SC2086
  tools/balance/balance-report.sh run $args --label "$tag" --out "$out" > "$out/run.log" 2>&1
  echo "rc=$? $(tail -1 "$out/run.log")"
done
