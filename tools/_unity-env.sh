# Sourced by the tools/*.sh scripts. Resolves the repo root and the Unity editor binary.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Build loop: builder lanes never run Unity (loop/README.md). Only the integration tree _int may.
LANES_ROOT="${CM_LOOP_LANES:-$HOME/critical-mass-unity-lanes}"
case "$ROOT/" in
  "$LANES_ROOT/_int/"*) ;;
  "$LANES_ROOT/"*) echo "refused: $ROOT is a builder lane; builders never run Unity" >&2; exit 126 ;;
esac
UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt")"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity}"
if [[ ! -x "$UNITY" ]]; then
  echo "Unity $UNITY_VERSION not found at $UNITY (set UNITY_PATH to override)" >&2
  exit 127
fi
mkdir -p "$ROOT/Logs"
