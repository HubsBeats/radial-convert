# Removes Radial Convert. Your settings stay in %APPDATA%\RadialConvert unless you pass -All.
param([switch]$All)
$dest = Join-Path $env:LOCALAPPDATA 'RadialConvert'
$exe = Join-Path $dest 'RadialConvert.exe'
Get-Process -Name RadialConvert -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -eq $exe } | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Milliseconds 300
Remove-Item (Join-Path ([Environment]::GetFolderPath('Startup')) 'Radial Convert.lnk') -ErrorAction SilentlyContinue
Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
if ($All) { Remove-Item (Join-Path $env:APPDATA 'RadialConvert') -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host 'Radial Convert removed.'
