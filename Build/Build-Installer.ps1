[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PayloadPath,
    [string]$OutputRoot,
    [string]$InnoCompilerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "Source/PitMedic/PitMedic.csproj"
[xml]$projectXml = Get-Content -Raw $projectPath
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) { throw "The project version could not be read from $projectPath." }

$resolvedPayload = (Resolve-Path $PayloadPath).Path
$appExecutable = if (Test-Path $resolvedPayload -PathType Container) {
    Get-ChildItem -Path $resolvedPayload -Filter "PitMedic.exe" -Recurse -File | Select-Object -First 1
} else {
    $null
}
if (-not $appExecutable) { throw "PitMedic.exe was not found under $resolvedPayload." }

$payloadDirectory = $appExecutable.Directory.FullName
$helperExecutable = Join-Path $payloadDirectory "PitMedic.RepairHelper.exe"
if (-not (Test-Path $helperExecutable -PathType Leaf)) {
    throw "PitMedic.RepairHelper.exe must be beside PitMedic.exe before the installer can be built."
}
$sensorHelperExecutable = Join-Path $payloadDirectory "PitMedic.SensorHelper.exe"
if (-not (Test-Path $sensorHelperExecutable -PathType Leaf)) {
    throw "PitMedic.SensorHelper.exe must be beside PitMedic.exe before the installer can be built."
}

foreach ($releaseExecutable in @($appExecutable.FullName, $helperExecutable, $sensorHelperExecutable)) {
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($releaseExecutable)
    if ($fileVersion.FileVersion -ne $version -or $fileVersion.ProductVersion -notlike "$version*") {
        throw "$(Split-Path -Leaf $releaseExecutable) metadata does not match release version $version."
    }
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot "Artifacts/installer"
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$resolvedOutput = (Resolve-Path $OutputRoot).Path

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) { $InnoCompilerPath = $command.Source }
}
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ -and (Test-Path $_ -PathType Leaf) }
    $InnoCompilerPath = $candidates | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or -not (Test-Path $InnoCompilerPath -PathType Leaf)) {
    throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6 or pass -InnoCompilerPath."
}

$installerScript = Join-Path $repositoryRoot "Installer/PitMedic.iss"
$setupPath = Join-Path $resolvedOutput "PitMedic-Setup-x64.exe"
if (Test-Path $setupPath) { Remove-Item -Force $setupPath }

& $InnoCompilerPath `
    "/DAppVersion=$version" `
    "/DPayloadDir=$payloadDirectory" `
    "/DOutputDir=$resolvedOutput" `
    $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }
if (-not (Test-Path $setupPath -PathType Leaf)) { throw "The expected installer was not produced: $setupPath" }

$installerVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($setupPath)
$actualFileVersion = [string]$installerVersion.FileVersion
$actualProductVersion = [string]$installerVersion.ProductVersion
Write-Host "Installer metadata: FileVersion='$actualFileVersion'; ProductVersion='$actualProductVersion'"
if ($actualFileVersion -notlike "$version*" -or $actualProductVersion -notlike "$version*") {
    throw "Installer metadata does not match release version $version. FileVersion='$actualFileVersion'; ProductVersion='$actualProductVersion'."
}

Write-Host "Installer created at $setupPath"
Write-Output $setupPath
