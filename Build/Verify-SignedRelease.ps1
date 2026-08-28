[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [ValidateSet("Binaries", "Installer")]
    [string]$ArtifactKind = "Binaries",
    [string]$ExpectedPublisher = "SignPath Foundation",
    [string]$ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedPath = (Resolve-Path $Path).Path
$searchRoot = if (Test-Path $resolvedPath -PathType Container) { $resolvedPath } else { Split-Path -Parent $resolvedPath }
$expectedNames = if ($ArtifactKind -eq "Binaries") {
    @("PitMedic.exe", "PitMedic.RepairHelper.exe")
} else {
    @("PitMedic-Setup-x64.exe")
}

$verifiedFiles = @()
foreach ($name in $expectedNames) {
    $candidate = if ((Test-Path $resolvedPath -PathType Leaf) -and (Split-Path -Leaf $resolvedPath) -eq $name) {
        Get-Item $resolvedPath
    } else {
        Get-ChildItem -Path $searchRoot -Filter $name -Recurse -File | Select-Object -First 1
    }
    if (-not $candidate) { throw "$name was not found under $searchRoot." }

    $signature = Get-AuthenticodeSignature -FilePath $candidate.FullName
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "$name does not have a valid Authenticode signature. Status: $($signature.Status)."
    }
    if (-not $signature.SignerCertificate) { throw "$name has no signer certificate." }
    if ($ExpectedPublisher -and $signature.SignerCertificate.Subject -notlike "*$ExpectedPublisher*") {
        throw "Unexpected publisher for $name`: $($signature.SignerCertificate.Subject)"
    }
    if (-not $signature.TimeStamperCertificate) { throw "$name has a valid signature but no timestamp certificate." }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($candidate.FullName)
    if ($ExpectedVersion -and ($versionInfo.FileVersion -ne $ExpectedVersion -or $versionInfo.ProductVersion -notlike "$ExpectedVersion*")) {
        throw "$name version does not match expected release version $ExpectedVersion."
    }

    $verifiedFiles += [ordered]@{
        name = $name
        version = $versionInfo.ProductVersion
        fileVersion = $versionInfo.FileVersion
        publisher = $signature.SignerCertificate.Subject
        signerThumbprint = $signature.SignerCertificate.Thumbprint
        timestampSubject = $signature.TimeStamperCertificate.Subject
        sha256 = (Get-FileHash -Algorithm SHA256 $candidate.FullName).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    product = "PitMedic"
    artifactKind = $ArtifactKind
    verifiedUtc = [DateTime]::UtcNow.ToString("o")
    files = $verifiedFiles
}
$manifestName = if ($ArtifactKind -eq "Binaries") { "PitMedic-binaries-manifest.json" } else { "PitMedic-installer-manifest.json" }
$manifestPath = Join-Path $searchRoot $manifestName
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $manifestPath

Write-Host "Verified signed PitMedic $($ArtifactKind.ToLowerInvariant()) under $searchRoot"
foreach ($file in $verifiedFiles) {
    Write-Host "$($file.name): $($file.publisher); SHA-256 $($file.sha256)"
}
