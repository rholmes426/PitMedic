[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "Source/PitMedic/PitMedic.csproj"
$helperProjectPath = Join-Path $repositoryRoot "Source/PitMedic.RepairHelper/PitMedic.RepairHelper.csproj"
$sensorHelperProjectPath = Join-Path $repositoryRoot "Source/PitMedic.SensorHelper/PitMedic.SensorHelper.csproj"
[xml]$projectXml = Get-Content -Raw $projectPath
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) { throw "The project version could not be read from $projectPath." }

[xml]$helperProjectXml = Get-Content -Raw $helperProjectPath
$helperVersion = [string]($helperProjectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ($helperVersion -ne $version) {
    throw "PitMedic.exe and PitMedic.RepairHelper.exe must use the same version. App: $version; helper: $helperVersion."
}

[xml]$sensorHelperProjectXml = Get-Content -Raw $sensorHelperProjectPath
$sensorHelperVersion = [string]($sensorHelperProjectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ($sensorHelperVersion -ne $version) {
    throw "PitMedic.exe and PitMedic.SensorHelper.exe must use the same version. App: $version; sensor service: $sensorHelperVersion."
}

$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^10\.') {
    throw "PitMedic releases require the .NET 10 SDK. Detected: $sdkVersion"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot "Artifacts/unsigned"
}

$publishPath = Join-Path $OutputRoot "PitMedic-$version-$Runtime"
if (Test-Path $publishPath) { Remove-Item -Recurse -Force $publishPath }
New-Item -ItemType Directory -Force -Path $publishPath | Out-Null

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

& dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishPath `
    -p:PublishReadyToRun=false `
    -p:ContinuousIntegrationBuild=true `
    -p:DebugSymbols=false `
    -p:DebugType=None

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

& dotnet publish $helperProjectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishPath `
    -p:PublishReadyToRun=false `
    -p:ContinuousIntegrationBuild=true `
    -p:DebugSymbols=false `
    -p:DebugType=None

if ($LASTEXITCODE -ne 0) { throw "Repair-helper publish failed with exit code $LASTEXITCODE." }

& dotnet publish $sensorHelperProjectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishPath `
    -p:PublishReadyToRun=false `
    -p:ContinuousIntegrationBuild=true `
    -p:DebugSymbols=false `
    -p:DebugType=None

if ($LASTEXITCODE -ne 0) { throw "Sensor-helper publish failed with exit code $LASTEXITCODE." }

$executablePath = Join-Path $publishPath "PitMedic.exe"
$helperExecutablePath = Join-Path $publishPath "PitMedic.RepairHelper.exe"
$sensorHelperExecutablePath = Join-Path $publishPath "PitMedic.SensorHelper.exe"
if (-not (Test-Path $executablePath)) { throw "The expected release executable was not produced: $executablePath" }
if (-not (Test-Path $helperExecutablePath)) { throw "The expected repair helper was not produced: $helperExecutablePath" }
if (-not (Test-Path $sensorHelperExecutablePath)) { throw "The expected sensor service executable was not produced: $sensorHelperExecutablePath" }

foreach ($releaseExecutable in @($executablePath, $helperExecutablePath, $sensorHelperExecutablePath)) {
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($releaseExecutable)
    if ($fileVersion.FileVersion -ne $version -or $fileVersion.ProductVersion -notlike "$version*") {
        throw "$(Split-Path -Leaf $releaseExecutable) metadata does not match release version $version."
    }
}

$manifestPath = Join-Path $OutputRoot "unsigned-build.json"
$manifest = [ordered]@{
    product = "PitMedic"
    version = $version
    runtime = $Runtime
    commit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { "local-unverified" }
    createdUtc = [DateTime]::UtcNow.ToString("o")
    executables = @(
        [ordered]@{
            name = "PitMedic.exe"
            sha256 = (Get-FileHash -Algorithm SHA256 $executablePath).Hash.ToLowerInvariant()
        },
        [ordered]@{
            name = "PitMedic.RepairHelper.exe"
            sha256 = (Get-FileHash -Algorithm SHA256 $helperExecutablePath).Hash.ToLowerInvariant()
        },
        [ordered]@{
            name = "PitMedic.SensorHelper.exe"
            sha256 = (Get-FileHash -Algorithm SHA256 $sensorHelperExecutablePath).Hash.ToLowerInvariant()
        }
    )
    signed = $false
}
$manifest | ConvertTo-Json | Set-Content -Encoding UTF8 $manifestPath

Write-Host "Unsigned release input created at $publishPath"
Write-Host "This build is not an official public release until SignPath signs all three executables and the final installer."
