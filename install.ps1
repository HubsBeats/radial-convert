<#
  Radial Convert installer (no admin needed).
    powershell -ExecutionPolicy Bypass -File install.ps1
  1. builds RadialConvert.exe from source with Windows' own compiler (falls back to prebuilt\RadialConvert.exe)
  2. installs it to %LOCALAPPDATA%\RadialConvert
  3. downloads FFmpeg into %LOCALAPPDATA%\RadialConvert\tools (or uses one already installed)
  4. adds it to Startup and starts it
  Flags: -NoStartup  -SkipFfmpeg
#>
param([switch]$NoStartup, [switch]$SkipFfmpeg)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$src = $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'RadialConvert'
$exe = Join-Path $dest 'RadialConvert.exe'
New-Item -ItemType Directory -Force -Path (Join-Path $dest 'tools') | Out-Null

function Invoke-Exe([string[]]$a) {
  $p = Start-Process -FilePath $exe -ArgumentList $a -Wait -PassThru -WindowStyle Hidden
  return $p.ExitCode
}

Write-Host '== Stopping a running copy'
Get-Process -Name RadialConvert -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -eq $exe } | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Milliseconds 300

Write-Host '== Building'
$built = Join-Path $src 'dist\RadialConvert.exe'
try { & (Join-Path $src 'scripts\build.ps1') }
catch { Write-Warning "Build from source failed ($_). Using the prebuilt exe." }
if (-not (Test-Path $built)) { $built = Join-Path $src 'prebuilt\RadialConvert.exe' }
if (-not (Test-Path $built)) { throw "No RadialConvert.exe to install." }
Copy-Item $built $exe -Force
Unblock-File $exe

if (-not $SkipFfmpeg) {
  Write-Host '== FFmpeg'
  $ff = Join-Path $dest 'tools\ffmpeg.exe'
  $existing = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
  # skip a dead link (e.g. a removed winget package): only accept a file that opens
  if ($existing) { try { [IO.File]::OpenRead($existing.Source).Close() } catch { $existing = $null } }
  if (Test-Path $ff) { Write-Host "already at $ff" }
  elseif ($existing) {
    # Copy the real exe next to the app (WinGet's Links entry is a symlink).
    $item = Get-Item $existing.Source
    $real = if ($item.LinkType) { @($item.Target)[0] } else { $item.FullName }
    if ($real -and -not [IO.Path]::IsPathRooted($real)) { $real = Join-Path $item.DirectoryName $real }
    Copy-Item $real $ff -Force
    Write-Host "copied $real to $ff"
  }
  else {
    try {
      $zip = Join-Path $env:TEMP 'radialconvert-ffmpeg.zip'
      $tmp = Join-Path $env:TEMP ('radialconvert-ffmpeg-' + [guid]::NewGuid().ToString('N'))
      Write-Host 'downloading FFmpeg essentials build (about 100 MB)...'
      Invoke-WebRequest 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $zip -UseBasicParsing
      Expand-Archive $zip $tmp -Force
      $bin = Get-ChildItem $tmp -Recurse -Filter ffmpeg.exe | Select-Object -First 1
      if (-not $bin) { throw 'ffmpeg.exe not in the download' }
      Copy-Item $bin.FullName $ff -Force
      Remove-Item $zip, $tmp -Recurse -Force -ErrorAction SilentlyContinue
      Write-Host "installed $ff"
    } catch {
      Write-Warning "Direct download failed ($_). Trying winget."
      winget install --id Gyan.FFmpeg -e --silent --accept-package-agreements --accept-source-agreements
    }
  }
}

Write-Host '== Checking'
if ((Invoke-Exe @('selftest')) -ne 0) { Write-Warning 'selftest failed, see above' }
if ((Invoke-Exe @('tools')) -ne 0) { Write-Warning 'FFmpeg not found: audio, video, GIF and WebP need it.' }

if (-not $NoStartup) { Invoke-Exe @('startup', 'on') | Out-Null; Write-Host 'starts with Windows: yes' }

Start-Process -FilePath $exe
Write-Host ''
Write-Host "Radial Convert is running (tray icon). Settings: $env:APPDATA\RadialConvert\settings.json"
Write-Host 'Select files in Explorer or on the desktop, hold Shift, right-click and flick toward a format.'
