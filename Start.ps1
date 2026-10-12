$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'app\DicteeLocale.exe'
if (!(Test-Path -LiteralPath $executable)) { throw "Application non compilée : lance setup.ps1." }
Start-Process -FilePath $executable -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
