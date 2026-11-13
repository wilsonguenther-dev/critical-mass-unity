#!/usr/bin/env bash
# Soak lane (U-028): only the NUnit "Slow" tests. Results: Logs/editmode-results.xml.
set -euo pipefail
# Unity runs inside unity-test.sh, through the machine lock (cm-heavy unity, DG-44).
exec "$(dirname "${BASH_SOURCE[0]}")/unity-test.sh" --category Slow
