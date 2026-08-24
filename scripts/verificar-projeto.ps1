$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ArtifactsPath = Join-Path $ProjectRoot ".artifacts\verify-$PID"

Write-Host 'Compilando backend...'
dotnet build (Join-Path $ProjectRoot 'CortaFeStudio.sln') -c Release --artifacts-path $ArtifactsPath
if ($LASTEXITCODE -ne 0) { throw 'A compilação Release falhou.' }

Write-Host 'Executando testes automatizados...'
dotnet test (Join-Path $ProjectRoot 'CortaFeStudio.sln') -c Release --no-build --artifacts-path $ArtifactsPath
if ($LASTEXITCODE -ne 0) { throw 'A suíte automatizada falhou.' }

Write-Host 'Validando JavaScript...'
Get-ChildItem (Join-Path $ProjectRoot 'src\CortaFeStudio.Api\wwwroot') -Filter '*.js' | ForEach-Object {
  node --check $_.FullName
  if ($LASTEXITCODE -ne 0) { throw "JavaScript inválido: $($_.Name)" }
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
