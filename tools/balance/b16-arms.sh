#!/usr/bin/env bash
# PROBE ONLY (ci/balance/b16-mech-*): run "tag|args" lines from stdin. A tag starting "cmp-" is a compare against BASE_REF.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
while IFS='|' read -r tag args; do
  [[ -z "${tag// }" ]] && continue
  out="Temp/balance/arms/$tag"
  mkdir -p "$out"
  echo "=== arm $tag: $args"
  if [[ "$tag" == cmp-* ]]; then
    # shellcheck disable=SC2086
    tools/balance/balance-report.sh compare --base "${BASE_REF:?}" --head HEAD $args --out "$out" > "$out/run.log" 2>&1
    rc=$?
    for s in base head; do [[ -d "Temp/balance/$s-report" ]] && { rm -rf "$out/$s"; mv "Temp/balance/$s-report" "$out/$s"; }; done
  else
    # shellcheck disable=SC2086
    tools/balance/balance-report.sh run $args --label "$tag" --out "$out" > "$out/run.log" 2>&1
    rc=$?
  fi
  echo "rc=$rc $(tail -1 "$out/run.log")"
done
