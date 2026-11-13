#!/bin/bash
# Runs ON the worker Mac, idempotent, user space only (no sudo, nothing outside ~/cm-remote):
#   tools/remote-run.sh --put tools/remote/setup-worker.sh -- bash setup-worker.sh [ffmpeg] [venv] [dotnet] [repo]
# (no argument = all four). Installs: static arm64 ffmpeg + ffprobe in ~/cm-remote/tools/bin, a Python venv with Pillow
# and numpy in ~/cm-remote/venv (from the system python3), the .NET 8 SDK in ~/cm-remote/dotnet (Microsoft's
# dotnet-install.sh), and a shallow clone of the public repo in ~/cm-remote/repo (for tools/ci/sim-tests.sh).
set -u
C="$HOME/cm-remote"; mkdir -p "$C/tools/bin" "$C/tmp"
WANT="${*:-ffmpeg venv dotnet repo}"
has() { case " $WANT " in *" $1 "*) return 0 ;; esac; return 1; }
rc=0
if has ffmpeg; then
  if ! "$C/tools/bin/ffmpeg" -hide_banner -version >/dev/null 2>&1; then
    for tool in ffmpeg ffprobe; do
      ok=""
      for url in "https://ffmpeg.martin-riedl.de/redirect/latest/macos/arm64/release/$tool.zip" \
                 "https://www.osxexperts.net/${tool}71arm.zip"; do
        rm -rf "$C/tmp/$tool"; mkdir -p "$C/tmp/$tool"
        if curl -fsSL --max-time 300 -o "$C/tmp/$tool/x.zip" "$url" && (cd "$C/tmp/$tool" && unzip -oq x.zip); then
          f="$(find "$C/tmp/$tool" -type f -name "$tool" | head -1)"
          if [ -n "$f" ]; then mv "$f" "$C/tools/bin/$tool"; chmod +x "$C/tools/bin/$tool"; xattr -c "$C/tools/bin/$tool" 2>/dev/null
            "$C/tools/bin/$tool" -hide_banner -version >/dev/null 2>&1 && { ok=1; echo "ffmpeg: $tool from $url"; break; }; fi
        fi
      done
      rm -rf "$C/tmp/$tool"; [ -n "$ok" ] || { echo "ffmpeg: could not install $tool"; rc=1; }
    done
  fi
  "$C/tools/bin/ffmpeg" -hide_banner -version 2>/dev/null | head -1
fi
if has venv; then
  [ -x "$C/venv/bin/python3" ] || /usr/bin/python3 -m venv "$C/venv" || rc=1
  "$C/venv/bin/python3" -m pip install -q --disable-pip-version-check --upgrade pillow numpy >/dev/null 2>&1 || rc=1
  "$C/venv/bin/python3" -c 'import sys, PIL, numpy; print("venv: python", sys.version.split()[0], "Pillow", PIL.__version__, "numpy", numpy.__version__)' || rc=1
fi
if has dotnet; then
  if ! "$C/dotnet/dotnet" --list-sdks 2>/dev/null | grep -q '^8\.'; then
    curl -fsSL --max-time 120 -o "$C/tmp/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh \
      && bash "$C/tmp/dotnet-install.sh" --channel 8.0 --install-dir "$C/dotnet" --no-path >/dev/null 2>&1 || rc=1
    rm -f "$C/tmp/dotnet-install.sh"
  fi
  echo "dotnet: $("$C/dotnet/dotnet" --list-sdks 2>/dev/null | tr '\n' ' ')"
fi
if has repo; then
  if [ -d "$C/repo/.git" ]; then git -C "$C/repo" fetch -q --depth 1 origin main && git -C "$C/repo" reset -q --hard FETCH_HEAD || rc=1
  else  # sparse: only what the no-Unity Sim tests read (no art, no LFS)
    GIT_LFS_SKIP_SMUDGE=1 git clone -q --depth 1 --filter=blob:none --sparse https://github.com/wilsonguenther-dev/critical-mass-unity.git "$C/repo" \
      && git -C "$C/repo" sparse-checkout set Assets/_Game/Sim Assets/_Game/Sim.Tests tools/ci tools/golden ProjectSettings || rc=1; fi
  echo "repo: $(git -C "$C/repo" log -1 --format='%h %s' 2>/dev/null)"
fi
echo "disk: $(df -g "$HOME" | awk 'NR==2{print $4}') GB free; ~/cm-remote $(du -sh "$C" 2>/dev/null | cut -f1)"
exit $rc
