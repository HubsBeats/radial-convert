#!/usr/bin/env bash
# Rebuilds assets/demo.gif from the real menu graphics. Needs mono, ffmpeg, python3 + Pillow.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
"$here/scripts/build.sh" >/dev/null
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
export RADIALCONVERT_HOME="$tmp/home"
for spec in "img:png,webp,pdf,gif:JPG" "aud:mp3,ogg,flac,m4a:WAV" "vid:mp4,webm,gif,mp3:MOV"; do
  IFS=: read -r n l c <<<"$spec"
  for s in -1 0 1 2 3; do mono "$here/dist/RadialConvert.exe" preview "$tmp/$n$s.png" "$s" "$l" "$c" >/dev/null; done
done
python3 "$here/scripts/demo-gif.py" "$tmp" "$here/assets/demo.gif"
