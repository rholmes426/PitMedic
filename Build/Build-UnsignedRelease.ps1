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
[xml]$projectXml = Get-Content -Raw $projectPath
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) { throw "The project version could not be read from $projectPath." }

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

$executablePath = Join-Path $publishPath "PitMedic.exe"
if (-not (Test-Path $executablePath)) { throw "The expected release executable was not produced: $executablePath" }

$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
if ($fileVersion.FileVersion -ne $version -or $fileVersion.ProductVersion -notlike "$version*") {
    throw "PitMedic.exe metadata does not match release version $version."
}

$manifestPath = Join-Path $OutputRoot "unsigned-build.json"
$manifest = [ordered]@{
    product = "PitMedic"
    version = $version
    runtime = $Runtime
    commit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { "local-unverified" }
    createdUtc = [DateTime]::UtcNow.ToString("o")
    executableSha256 = (Get-FileHash -Algorithm SHA256 $executablePath).Hash.ToLowerInvariant()
    signed = $false
}
$manifest | ConvertTo-Json | Set-Content -Encoding UTF8 $manifestPath

Write-Host "Unsigned release input created at $publishPath"
Write-Host "This build is not an official public release until SignPath signs it."
