#requires -Version 5.1
[CmdletBinding()]
param([switch]$Cpu, [switch]$SkipBuild, [switch]$SkipModels, [switch]$BootstrapOnly)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
    throw 'This release requires Windows x64.'
}
$running = Get-Process DicteeLocale -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -eq (Join-Path $PSScriptRoot 'app\DicteeLocale.exe')
}
if ($running) { throw 'Quit Local Dictation from its tray icon before installing or updating.' }
if (-not $SkipBuild -and -not $BootstrapOnly) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET SDK 8 or later, or use the Windows release ZIP and Install.cmd.' }
    $sdks = & dotnet --list-sdks
    if (-not ($sdks | Where-Object { $_ -match '^(8|9|[1-9][0-9])\.' })) { throw '.NET SDK 8 or later is required to build from source.' }
}
if ($SkipBuild -and -not $BootstrapOnly -and -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'app\DicteeLocale.exe'))) {
    throw 'The prebuilt application is missing. Extract the release ZIP, or run setup.ps1 without -SkipBuild.'
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$env:UV_CACHE_DIR = Join-Path $PSScriptRoot 'runtime\uv-cache'
$env:UV_PYTHON_INSTALL_DIR = Join-Path $PSScriptRoot 'runtime\python'
$env:UV_PYTHON_BIN_DIR = Join-Path $PSScriptRoot 'runtime\bin'
$env:UV_NO_MODIFY_PATH = '1'
$env:PYTHONUTF8 = '1'
$uv = Join-Path $PSScriptRoot 'runtime\uv.exe'
New-Item -ItemType Directory -Path (Split-Path $uv) -Force | Out-Null
if (-not (Test-Path -LiteralPath $uv)) {
    Write-Host 'Downloading uv 0.13.0 (verified SHA-256)...'
    $zip = Join-Path $PSScriptRoot 'runtime\uv-bootstrap.zip'
    $unpack = Join-Path $PSScriptRoot 'runtime\uv-bootstrap'
    Invoke-WebRequest -UseBasicParsing 'https://github.com/astral-sh/uv/releases/download/0.13.0/uv-x86_64-pc-windows-msvc.zip' -OutFile $zip
    $expected = '088962f9e7b7bd9ea740c04c650b2a21c8928c345bd99ac24350dc924dba656c'
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'uv checksum mismatch. Download was not installed.' }
    Expand-Archive -LiteralPath $zip -DestinationPath $unpack -Force
    $binary = Get-ChildItem -LiteralPath $unpack -Filter uv.exe -Recurse | Select-Object -First 1
    if (-not $binary) { throw 'uv.exe was missing from the verified download.' }
    Copy-Item -LiteralPath $binary.FullName -Destination $uv
}
Write-Host 'Preparing private Python 3.12.11...'
& $uv python install 3.12.11 --no-registry
if ($LASTEXITCODE -ne 0) { throw 'Python installation failed. Check your network connection and retry.' }
if ($BootstrapOnly) { Write-Host 'Bootstrap ready.'; return }
$python = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    & $uv venv --python 3.12.11 --python-preference only-managed (Join-Path $PSScriptRoot '.venv')
    if ($LASTEXITCODE -ne 0) { throw 'Environment creation failed.' }
}
$index = if ($Cpu) { 'https://download.pytorch.org/whl/cpu' } else { 'https://download.pytorch.org/whl/cu128' }
Write-Host 'Installing local inference dependencies...'
& $uv pip install --python $python torch==2.11.0 --index-url $index
if ($LASTEXITCODE -ne 0) { throw 'PyTorch installation failed.' }
& $uv pip install --python $python -r (Join-Path $PSScriptRoot 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
if (-not $SkipModels) {
    & $python (Join-Path $PSScriptRoot 'backend\download_models.py')
    if ($LASTEXITCODE -ne 0) { throw 'Model download failed. Run this installer again to resume.' }
}
if (-not $SkipBuild) {
    & dotnet publish (Join-Path $PSScriptRoot 'src\WindowsApp\WindowsApp.csproj') -c Release -o (Join-Path $PSScriptRoot 'app') --self-contained false
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
}
Write-Host 'Ready. Double-click Start.vbs. The microphone icon appears in the notification area.'
