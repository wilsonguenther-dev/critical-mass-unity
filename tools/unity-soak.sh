#!/usr/bin/env bash
# Soak lane (U-028): only the NUnit "Slow" tests. Results: Logs/editmode-results.xml.
set -euo pipefail
exec "$(dirname "${BASH_SOURCE[0]}")/unity-test.sh" --category Slow
