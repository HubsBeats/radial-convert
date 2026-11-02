# Windows end-to-end test: converts generated samples through the installed (or built) exe.
param([string]$Exe = (Join-Path $env:LOCALAPPDATA 'RadialConvert\RadialConvert.exe'))
$ErrorActionPreference = 'Stop'
$ff = Join-Path $env:LOCALAPPDATA 'RadialConvert\tools\ffmpeg.exe'
if (-not (Test-Path $ff)) {
  $item = Get-Item (Get-Command ffmpeg.exe).Source
  $ff = if ($item.LinkType) { @($item.Target)[0] } else { $item.FullName }
}
$work = Join-Path $env:TEMP ('radialconvert-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
$env:RADIALCONVERT_HOME = Join-Path $work 'home'
Push-Location $work
function FF { & $ff -hide_banner -loglevel error -y @args }
FF -f lavfi -i testsrc2=size=320x240 '-frames:v' 1 photo.jpg
FF -f lavfi -i sine=frequency=440:duration=1 tone.wav
FF -f lavfi -i testsrc2=size=320x240:rate=24 -f lavfi -i sine=duration=1 -t 1 -shortest clip.mov
$fail = 0
$cases = @(@('photo.jpg','png'),@('photo.jpg','pdf'),@('photo.jpg','gif'),@('photo.jpg','webp'),
           @('tone.wav','mp3'),@('tone.wav','flac'),@('tone.wav','m4a'),
           @('clip.mov','mp4'),@('clip.mov','webm'),@('clip.mov','gif'),@('clip.mov','mp3'))
foreach ($c in $cases) {
  $p = Start-Process $Exe -ArgumentList @('convert','--to',$c[1],$c[0]) -Wait -PassThru -WindowStyle Hidden
  $out = [IO.Path]::GetFileNameWithoutExtension($c[0]) + '.' + $c[1]
  if ($p.ExitCode -eq 0 -and (Test-Path $out) -and (Get-Item $out).Length -gt 0) { "ok   $($c[0]) -> $($c[1])" }
  else { "FAIL $($c[0]) -> $($c[1])"; $fail++ }
}
Pop-Location
Remove-Item $work -Recurse -Force
if ($fail) { "$fail FAILED"; exit 1 } else { 'ALL PASSED' }
