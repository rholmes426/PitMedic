[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [string]$ExpectedPublisher = "SignPath Foundation",
    [string]$ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedPath = (Resolve-Path $Path).Path
$executablePath = if (Test-Path $resolvedPath -PathType Container) {
    $candidate = Get-ChildItem -Path $resolvedPath -Filter "PitMedic.exe" -Recurse -File | Select-Object -First 1
    if (-not $candidate) { throw "PitMedic.exe was not found under $resolvedPath." }
    $candidate.FullName
} else {
    $resolvedPath
}

$signature = Get-AuthenticodeSignature -FilePath $executablePath
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "PitMedic.exe does not have a valid Authenticode signature. Status: $($signature.Status)."
}
if (-not $signature.SignerCertificate) { throw "PitMedic.exe has no signer certificate." }
if ($ExpectedPublisher -and $signature.SignerCertificate.Subject -notlike "*$ExpectedPublisher*") {
    throw "Unexpected publisher: $($signature.SignerCertificate.Subject)"
}
if (-not $signature.TimeStamperCertificate) { throw "PitMedic.exe has a valid signature but no timestamp certificate." }

$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
if ($ExpectedVersion -and ($versionInfo.FileVersion -ne $ExpectedVersion -or $versionInfo.ProductVersion -notlike "$ExpectedVersion*")) {
    throw "PitMedic.exe version does not match expected release version $ExpectedVersion."
}
$manifest = [ordered]@{
    product = "PitMedic"
    version = $versionInfo.ProductVersion
    fileVersion = $versionInfo.FileVersion
    publisher = $signature.SignerCertificate.Subject
    signerThumbprint = $signature.SignerCertificate.Thumbprint
    timestampSubject = $signature.TimeStamperCertificate.Subject
    sha256 = (Get-FileHash -Algorithm SHA256 $executablePath).Hash.ToLowerInvariant()
    verifiedUtc = [DateTime]::UtcNow.ToString("o")
}

$manifestPath = Join-Path (Split-Path -Parent $executablePath) "PitMedic-release-manifest.json"
$manifest | ConvertTo-Json | Set-Content -Encoding UTF8 $manifestPath
Write-Host "Verified signed PitMedic release: $executablePath"
Write-Host "Publisher: $($signature.SignerCertificate.Subject)"
Write-Host "SHA-256: $($manifest.sha256)"
