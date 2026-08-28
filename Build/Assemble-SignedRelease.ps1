[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SignedBinariesPath,
    [Parameter(Mandatory)]
    [string]$SignedInstallerPath,
    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "Source/PitMedic/PitMedic.csproj"
[xml]$projectXml = Get-Content -Raw $projectPath
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) { throw "The project version could not be read from $projectPath." }

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot "Artifacts/release"
}
if (Test-Path $OutputRoot) { Remove-Item -Recurse -Force $OutputRoot }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$resolvedOutput = (Resolve-Path $OutputRoot).Path

$binaryRoot = (Resolve-Path $SignedBinariesPath).Path
$appExecutable = Get-ChildItem -Path $binaryRoot -Filter "PitMedic.exe" -Recurse -File | Select-Object -First 1
if (-not $appExecutable) { throw "Signed PitMedic.exe was not found under $binaryRoot." }
$payloadDirectory = $appExecutable.Directory.FullName
if (-not (Test-Path (Join-Path $payloadDirectory "PitMedic.RepairHelper.exe") -PathType Leaf)) {
    throw "Signed PitMedic.RepairHelper.exe was not found beside PitMedic.exe."
}

$installerRoot = (Resolve-Path $SignedInstallerPath).Path
$installer = Get-ChildItem -Path $installerRoot -Filter "PitMedic-Setup-x64.exe" -Recurse -File | Select-Object -First 1
if (-not $installer) { throw "Signed PitMedic-Setup-x64.exe was not found under $installerRoot." }
Copy-Item $installer.FullName (Join-Path $resolvedOutput $installer.Name)

$portableDirectory = Join-Path $resolvedOutput "PitMedic-$version-win-x64"
New-Item -ItemType Directory -Force -Path $portableDirectory | Out-Null
Copy-Item (Join-Path $payloadDirectory "*") $portableDirectory -Recurse -Force
$portableArchive = Join-Path $resolvedOutput "PitMedic-$version-win-x64.zip"
Compress-Archive -Path $portableDirectory -DestinationPath $portableArchive -CompressionLevel Optimal
Remove-Item -Recurse -Force $portableDirectory

$binaryManifest = Get-ChildItem -Path $binaryRoot -Filter "PitMedic-binaries-manifest.json" -Recurse -File | Select-Object -First 1
$installerManifest = Get-ChildItem -Path $installerRoot -Filter "PitMedic-installer-manifest.json" -Recurse -File | Select-Object -First 1
if ($binaryManifest) { Copy-Item $binaryManifest.FullName (Join-Path $resolvedOutput $binaryManifest.Name) }
if ($installerManifest) { Copy-Item $installerManifest.FullName (Join-Path $resolvedOutput $installerManifest.Name) }

$publicFiles = @(
    (Join-Path $resolvedOutput "PitMedic-Setup-x64.exe"),
    $portableArchive
)
$checksumLines = foreach ($file in $publicFiles) {
    $hash = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $file)"
}
$checksumLines | Set-Content -Encoding ASCII (Join-Path $resolvedOutput "SHA256SUMS.txt")

Write-Host "Signed release package assembled at $resolvedOutput"
