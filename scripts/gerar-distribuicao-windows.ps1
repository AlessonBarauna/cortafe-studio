$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$OutputRoot = Join-Path $ProjectRoot 'dist'
$AppRoot = Join-Path $OutputRoot 'CortaFeStudio'
$ZipPath = Join-Path $OutputRoot 'CortaFeStudio-Windows-x64.zip'

if (Test-Path $AppRoot) { Remove-Item -LiteralPath $AppRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $AppRoot | Out-Null
dotnet publish (Join-Path $ProjectRoot 'src\CortaFeStudio.Api\CortaFeStudio.Api.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $AppRoot
Copy-Item (Join-Path $ProjectRoot 'scripts\instalar-windows.ps1') $AppRoot
@'
@echo off
start "CortaFe Studio" /min CortaFeStudio.Api.exe --urls http://localhost:5088
timeout /t 2 /nobreak >nul
start http://localhost:5088
'@ | Set-Content (Join-Path $AppRoot 'Abrir CortaFe Studio.bat') -Encoding ASCII
if (Test-Path $ZipPath) { Remove-Item -LiteralPath $ZipPath }
Compress-Archive -Path (Join-Path $AppRoot '*') -DestinationPath $ZipPath -CompressionLevel Optimal
Write-Host "Distribuição criada em $ZipPath" -ForegroundColor Green
