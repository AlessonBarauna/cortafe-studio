$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ApiRoot = Join-Path $ProjectRoot 'src\CortaFeStudio.Api'
$ToolsRoot = Join-Path $ApiRoot 'tools'
New-Item -ItemType Directory -Force -Path $ToolsRoot | Out-Null

$LocalPython = Join-Path $ToolsRoot 'python\python.exe'
if (-not (Test-Path $LocalPython)) {
  Write-Host 'Python não encontrado. Instalando Python 3.12 isolado no projeto...'
  $PythonInstaller = Join-Path $ProjectRoot 'python-3.12.10-amd64.exe'
  Invoke-WebRequest 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-amd64.exe' -OutFile $PythonInstaller
  $PythonTarget = Join-Path $ToolsRoot 'python'
  $PythonInstall = Start-Process -FilePath $PythonInstaller -ArgumentList '/quiet','InstallAllUsers=0',"TargetDir=$PythonTarget",'Include_pip=1','Include_launcher=0','Include_test=0','AssociateFiles=0','Shortcuts=0','PrependPath=0' -WindowStyle Hidden -PassThru -Wait
  if ($PythonInstall.ExitCode -ne 0) { throw "A instalação do Python falhou com o código $($PythonInstall.ExitCode)." }
}

Write-Host 'Instalando Faster-Whisper no Python...'
& $LocalPython -m pip install --upgrade faster-whisper 'opencv-python-headless==4.10.0.84'

if (-not (Get-Command yt-dlp -ErrorAction SilentlyContinue)) {
  Write-Host 'Baixando yt-dlp...'
  Invoke-WebRequest 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile (Join-Path $ToolsRoot 'yt-dlp.exe')
}

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue) -and -not (Test-Path (Join-Path $ToolsRoot 'ffmpeg.exe'))) {
  Write-Warning 'FFmpeg não foi encontrado. Instale o FFmpeg e copie ffmpeg.exe e ffprobe.exe para src\CortaFeStudio.Api\tools.'
}

Write-Host 'Instalação concluída. Ollama é opcional para títulos mais criativos.' -ForegroundColor Green
