#!/usr/bin/env bash
# U-049 standalone C# proof of the F-02c port, no Unity project needed: compiles Fdlibm.cs, JsMath.cs,
# JsMathFdlibmTests.cs and BoundaryTests.cs from a checkout of loop/F-02c (arg 1) with the Roslyn inside the Unity
# editor against Unity's netstandard 2.1 reference (the project's API level), then runs every test of both fixtures
# plus >= 1.25M software-fma triples against Python's math.fma, under Unity's Mono and under Unity's CoreCLR.
# Run from the main checkout root (it holds tools/golden/*.json):  bash tools/golden/fdlibm-proof/run.sh <lane dir>
# The .cs files here end in .txt so Unity never imports them into the project.
set -uo pipefail
LANE="$(cd "${1:?lane checkout of loop/F-02c}" && pwd)"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S=/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/Resources/Scripting
OUT="$(mktemp -d)"; trap 'rm -rf "$OUT"' EXIT
cp "$HERE/NUnitShim.cs.txt" "$OUT/NUnitShim.cs"; cp "$HERE/Program.cs.txt" "$OUT/Program.cs"
python3 "$HERE/fma_triples.py" "$OUT/fma-triples.bin" || exit 1
"$S/NetCoreRuntime/dotnet" "$S/DotNetSdkRoslyn/csc.dll" -nologo -nostdlib -noconfig \
  -r:"$S/NetStandard/ref/2.1.0/netstandard.dll" -target:exe -out:"$OUT/fdlibm-proof.exe" \
  "$LANE/Assets/_Game/Sim/Core/Fdlibm.cs" "$LANE/Assets/_Game/Sim/Core/JsMath.cs" \
  "$LANE/Assets/_Game/Sim.Tests/JsMathFdlibmTests.cs" "$LANE/Assets/_Game/Sim.Tests/BoundaryTests.cs" \
  "$OUT/NUnitShim.cs" "$OUT/Program.cs"
c=$?; echo "csc exit $c"; [ $c -eq 0 ] || exit $c
printf '{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.21"}}}' > "$OUT/fdlibm-proof.runtimeconfig.json"
# BoundaryTests scans Assets/_Game/Sim of the working directory, JsMathFdlibmTests reads tools/golden: run in a copy
# of the lane with the main checkout's golden files beside it.
W="$OUT/w"; mkdir -p "$W/tools"; cp -R "$LANE/Assets" "$W/Assets"; cp -R tools/golden "$W/tools/golden"
(cd "$W" && "$S/MonoBleedingEdge/bin/mono" "$OUT/fdlibm-proof.exe" "$OUT/fma-triples.bin"); m=$?; echo "mono exit $m"
(cd "$W" && "$S/NetCoreRuntime/dotnet" "$OUT/fdlibm-proof.exe" "$OUT/fma-triples.bin"); n=$?; echo "coreclr exit $n"
[ $m -eq 0 ] && [ $n -eq 0 ]
