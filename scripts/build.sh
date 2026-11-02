#!/usr/bin/env bash
# Builds RadialConvert.exe on Linux/macOS with Mono's compiler, against the .NET Framework 4.8
# reference assemblies, held to C# 5 so the same source also builds with Windows' own csc.exe.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
api=/usr/lib/mono/4.8-api
out="$here/dist/RadialConvert.exe"
mkdir -p "$here/dist"
icon=()
[ -f "$here/assets/radial.ico" ] && icon=(-win32icon:"$here/assets/radial.ico")
mcs -langversion:5 -target:winexe -platform:anycpu -optimize+ -nostdlib -noconfig \
  -lib:"$api" -lib:"$api/Facades" \
  -r:mscorlib.dll -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll \
  -r:System.Web.Extensions.dll -r:Microsoft.CSharp.dll \
  "${icon[@]}" -out:"$out" "$here"/src/*.cs
echo "built $out"
