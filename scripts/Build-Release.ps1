#requires -Version 5.1
[CmdletBinding()]
param([string]$Version='0.1.0')
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid release version.' }
$root=Split-Path $PSScriptRoot -Parent
$release=Join-Path $root 'release'
$stage=Join-Path $release ('work\'+$Version+'-'+[Guid]::NewGuid().ToString('N'))
$package=Join-Path $stage ('dictee-locale-'+$Version)
New-Item -ItemType Directory -Force -Path $package | Out-Null
& dotnet publish (Join-Path $root 'src\WindowsApp\WindowsApp.csproj') -c Release -r win-x64 --self-contained true -p:Version=$Version -o (Join-Path $package 'app')
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
New-Item -ItemType Directory -Force -Path (Join-Path $package 'backend') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'backend\server.py'),(Join-Path $root 'backend\download_models.py') -Destination (Join-Path $package 'backend')
foreach ($name in @('setup.ps1','Install.cmd','Start.ps1','Start.vbs','requirements.txt','README.md','LICENSE','THIRD-PARTY.md','CONTRIBUTING.md','SECURITY.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $package
}
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $package -Recurse
# README icon needs the same relative path; this is the only source asset shipped here.
$iconDir=Join-Path $package 'src\WindowsApp\Assets'
New-Item -ItemType Directory -Force -Path $iconDir | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src\WindowsApp\Assets\dictee.png') -Destination $iconDir
$zip=Join-Path $release ('dictee-locale-'+$Version+'-windows-x64.zip')
Compress-Archive -LiteralPath $package -DestinationPath $zip -Force
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$checksums=Join-Path $release 'SHA256SUMS.txt'
[IO.File]::WriteAllText($checksums,$hash+'  '+[IO.Path]::GetFileName($zip)+"`n",[Text.Encoding]::ASCII)
Write-Host ('Release ZIP: '+$zip)
Write-Host ('SHA-256: '+$hash)
