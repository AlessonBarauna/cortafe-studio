$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot

Write-Host 'Compilando backend...'
dotnet build (Join-Path $ProjectRoot 'CortaFeStudio.sln') -c Release --no-restore

Write-Host 'Validando JavaScript...'
Get-ChildItem (Join-Path $ProjectRoot 'src\CortaFeStudio.Api\wwwroot') -Filter '*.js' | ForEach-Object {
  node --check $_.FullName
}

$healthUrl = 'http://localhost:5088/api/health'
try {
  $health = Invoke-RestMethod $healthUrl -TimeoutSec 5
  if ($health.status -ne 'ok') { throw 'A API respondeu com estado inesperado.' }
  Invoke-RestMethod 'http://localhost:5088/api/diagnostics' -TimeoutSec 30 | Out-Null
  Write-Host 'API e diagnóstico responderam corretamente.' -ForegroundColor Green
} catch {
  Write-Warning 'O servidor não está aberto; verificações HTTP foram ignoradas.'
}

Write-Host 'Verificação concluída.' -ForegroundColor Green
