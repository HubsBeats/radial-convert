# Builds RadialConvert.exe on Windows with the C# compiler that ships with .NET Framework 4.x.
# Nothing to install. Output: dist\RadialConvert.exe
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '.NET Framework 4 compiler (csc.exe) not found' }
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$out = Join-Path $dist 'RadialConvert.exe'
$srcFiles = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$argsList = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', "/out:$out",
  '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
  '/r:System.Web.Extensions.dll', '/r:Microsoft.CSharp.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll')
$ico = Join-Path $root 'assets\radial.ico'
if (Test-Path $ico) { $argsList += "/win32icon:$ico" }
& $csc @argsList @srcFiles
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
Write-Host "built $out"
