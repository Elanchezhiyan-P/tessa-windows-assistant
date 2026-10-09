<#
.SYNOPSIS
  Publishes a self-contained build (no .NET needed on the target PC) and wraps it in an installer.

.EXAMPLE
  .\build\build-release.ps1                 # publish + installer, unsigned
  .\build\build-release.ps1 -Sign           # also sign (needs WINCOMPANION_PFX and WINCOMPANION_PFX_PASSWORD; see sign.ps1)

Output: publish\ (the app) and installer\Output\WinCompanion-Setup-<version>.exe
Needs: the .NET 8 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
#>
param(
    [string]$Version = '1.0.0',
    [switch]$Sign
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'publish'

# Where Inno Setup lives for a per-user or all-users install.
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup' }

Write-Host "Publishing WinCompanion $Version (self-contained, win-x64)..." -ForegroundColor Cyan
if ((Test-Path $publish) -and $publish.EndsWith('publish')) { Remove-Item -LiteralPath $publish -Recurse -Force }
dotnet publish (Join-Path $root 'WinCompanion.csproj') -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

if ($Sign) { & (Join-Path $PSScriptRoot 'sign.ps1') -Files (Join-Path $publish 'WinCompanion.exe') }

Write-Host 'Building the installer...' -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" "/DSourceDir=$publish" (Join-Path $root 'installer\WinCompanion.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }

$setup = Join-Path $root "installer\Output\WinCompanion-Setup-$Version.exe"
if ($Sign) { & (Join-Path $PSScriptRoot 'sign.ps1') -Files $setup }

$size = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "Done: $setup ($size MB)" -ForegroundColor Green
