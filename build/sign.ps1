<#
.SYNOPSIS
  Signs files with an Authenticode code-signing certificate.

.DESCRIPTION
  A signed installer shows your name instead of "Unknown publisher". NOTE: Windows SmartScreen also weighs how many people have
  run a file ("reputation"). An ordinary certificate removes "Unknown publisher" but the SmartScreen warning can still
  appear until the file builds reputation; an EV certificate or Microsoft's Trusted Signing service avoids that sooner.
  This script just does the signing step; getting the certificate is up to you.

.EXAMPLE
  $env:WINCOMPANION_PFX = 'C:\keys\mycert.pfx'; $env:WINCOMPANION_PFX_PASSWORD = '...'
  .\build\sign.ps1 -Files publish\WinCompanion.exe, installer\Output\WinCompanion-Setup-1.0.0.exe

.EXAMPLE
  .\build\sign.ps1 -Files x.exe -SelfSignedForTesting     # proves the mechanism only; Windows will not trust it
#>
param(
    [Parameter(Mandatory)][string[]]$Files,
    [string]$PfxPath = $env:WINCOMPANION_PFX,
    [string]$PfxPassword = $env:WINCOMPANION_PFX_PASSWORD,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [switch]$SelfSignedForTesting
)

$ErrorActionPreference = 'Stop'

if ($SelfSignedForTesting) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=WinCompanion (test only)' `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddDays(7)
    Write-Warning "Using a throwaway self-signed certificate ($($cert.Thumbprint)). Windows will NOT trust this signature."
}
else {
    if (-not $PfxPath -or -not (Test-Path $PfxPath)) { throw 'Set WINCOMPANION_PFX to your .pfx file (and WINCOMPANION_PFX_PASSWORD), or pass -PfxPath.' }
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($PfxPath, $PfxPassword)
}

foreach ($file in $Files) {
    $params = @{ FilePath = $file; Certificate = $cert; HashAlgorithm = 'SHA256' }
    if (-not $SelfSignedForTesting) { $params.TimestampServer = $TimestampServer }   # a timestamp keeps the signature valid after the cert expires
    $result = Set-AuthenticodeSignature @params
    # "UnknownError" with a self-signed certificate just means the root isn't trusted; the signature itself was applied.
    $applied = $result.SignerCertificate -ne $null -and $result.Status -in 'Valid', 'UnknownError'
    if (-not $applied) { throw "Signing $file failed: $($result.Status) $($result.StatusMessage)" }
    Write-Host "Signed $file ($($result.Status))" -ForegroundColor Green
}
