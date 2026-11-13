# Sourced by air-run.sh --exec on the worker Mac (copied to ~/cm-remote/env.sh by tools/remote/_remote-lib.sh).
# User-space tools only, all under ~/cm-remote; nothing here needs sudo.
CMR="$HOME/cm-remote"
[ -d "$CMR/tools/bin" ] && PATH="$CMR/tools/bin:$PATH"             # static ffmpeg, ffprobe
[ -d "$CMR/venv/bin" ] && PATH="$CMR/venv/bin:$PATH"               # python3 with Pillow and numpy
[ -x "$CMR/dotnet/dotnet" ] && { PATH="$CMR/dotnet:$PATH"; export DOTNET_ROOT="$CMR/dotnet"; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 NUGET_PACKAGES="$CMR/nuget"
export PATH
