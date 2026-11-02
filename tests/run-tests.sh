#!/usr/bin/env bash
# End-to-end test: builds the exe, makes sample media with ffmpeg, converts every
# category to every radial format through the CLI, and checks the outputs.
# Needs mono, ffmpeg and ffprobe (Linux/macOS); on Windows run tests/run-tests.ps1.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
"$here/scripts/build.sh" >/dev/null || exit 1
exe="$here/dist/RadialConvert.exe"
work="$(mktemp -d)"; export RADIALCONVERT_HOME="$work/home"
trap 'rm -rf "$work"' EXIT
fail=0
ok()  { echo "  ok   $1"; }
bad() { echo "  FAIL $1"; fail=1; }

mono "$exe" selftest || fail=1

cd "$work"
ff() { ffmpeg -hide_banner -loglevel error -y "$@"; }
ff -f lavfi -i testsrc2=size=320x240 -frames:v 1 photo.jpg
ff -f lavfi -i "color=c=red@0.0:size=64x64,format=rgba" -frames:v 1 clear.png
ff -f lavfi -i testsrc2=size=160x120:rate=10 -t 1 anim.gif
ff -f lavfi -i testsrc2=size=160x120 -frames:v 1 still.webp
ff -f lavfi -i sine=frequency=440:duration=1 tone.wav
ff -f lavfi -i sine=frequency=440:duration=1 -c:a flac tone24.flac
ff -f lavfi -i testsrc2=size=321x241:rate=24 -f lavfi -i sine=duration=1 -t 1 -shortest -c:v mpeg4 -c:a aac clip.mov

check() { # file target
  local in="$1" to="$2" base="${1%.*}"
  out=$(mono "$exe" convert --to "$to" "$in" 2>&1)
  local made="$base.$to"
  if [ -s "$made" ] && ffprobe -v error "$made" >/dev/null 2>&1; then ok "$in -> $to"
  elif [ "$to" = pdf ] && head -c 5 "$made" 2>/dev/null | grep -q '%PDF'; then ok "$in -> $to"
  else bad "$in -> $to: $out"; fi
  rm -f "$made"
}

for t in png pdf gif webp bmp tiff; do check photo.jpg $t; done
check clear.png jpg; check clear.png pdf; check anim.gif webp; check anim.gif png; check still.webp png; check still.webp pdf
for t in mp3 flac m4a ogg opus aiff; do check tone.wav $t; done
check tone24.flac wav
for t in mp4 webm gif mp3 mkv; do check clip.mov $t; done

# Never overwrites: second conversion gets " (1)".
mono "$exe" convert --to png photo.jpg >/dev/null; mono "$exe" convert --to png photo.jpg >/dev/null
[ -f "photo (1).png" ] && ok "no overwrite" || bad "no overwrite"
# Same-format conversion is skipped, not failed.
mono "$exe" convert --to jpeg photo.jpg | grep -q skipped && ok "skip same format" || bad "skip same format"
# Transparent png -> jpg is flattened onto white.
mono "$exe" convert --to jpg clear.png >/dev/null
px=$(ffmpeg -v error -i clear.jpg -frames:v 1 -f rawvideo -pix_fmt rgb24 - | head -c 3 | od -An -tu1 | tr -s ' ')
[[ "$px" =~ 25[0-5]\ 25[0-5]\ 25[0-5] ]] && ok "transparent -> white ($px)" || bad "transparent -> white ($px)"
# Radial menu swaps the source's own format.
mono "$exe" menu photo.jpeg | grep -q "right=webp" && ok "menu swaps jpg on a jpeg" || bad "menu swap"
# No temp files left behind.
ls | grep -q converting && bad "temp files left" || ok "no temp files left"

[ $fail = 0 ] && echo "ALL PASSED" || echo "SOME FAILED"
exit $fail
