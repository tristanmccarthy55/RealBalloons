#!/usr/bin/env bash
# Build RealBalloons.dll with Mono's mcs (Linux / macOS). On Windows, use RealBalloons.csproj with the .NET SDK.
# Usage: Source/build.sh "<path to KSP>/KSP_x64_Data/Managed"
set -e
M="${1:?usage: build.sh <path to KSP_x64_Data/Managed>}"
HERE="$(cd "$(dirname "$0")" && pwd)"
REFS=()
for f in "$M"/UnityEngine*.dll "$M"/Assembly-CSharp.dll "$M"/Assembly-CSharp-firstpass.dll "$M"/mscorlib.dll "$M"/System.dll "$M"/System.Core.dll; do
  [ -f "$f" ] && REFS+=("-r:$f")
done
mkdir -p "$HERE/../GameData/RealBalloons/Plugins"
mcs -target:library -optimize+ -nostdlib -noconfig -langversion:6 -out:"$HERE/../GameData/RealBalloons/Plugins/RealBalloons.dll" "${REFS[@]}" "$HERE"/*.cs
echo "Built GameData/RealBalloons/Plugins/RealBalloons.dll"
