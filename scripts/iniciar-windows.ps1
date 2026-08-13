$ProjectRoot = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $ProjectRoot 'src\CortaFeStudio.Api') --urls http://localhost:5088
