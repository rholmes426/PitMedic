[CmdletBinding()]
param([string]$InstallerPath = './Artifacts/installer/PitMedic-Setup-x64.exe')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path $InstallerPath).Path
$installRoot = Join-Path $env:ProgramFiles 'PitMedic'
$logRoot = Join-Path $PWD 'Artifacts/installer-tests'
New-Item -ItemType Directory -Force $logRoot | Out-Null
# Run only on disposable Windows CI runners: this installs a signed kernel driver.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This integration test is restricted to disposable GitHub Actions runners.' }
foreach ($attempt in 1..2) {
    $log = Join-Path $logRoot "setup-$attempt.log"
    $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$log`"") -Wait -PassThru
    if ($process.ExitCode -notin @(0, 3010)) { Get-Content $log -Tail 80; throw "Setup failed: $($process.ExitCode)" }
    $driver = Get-CimInstance Win32_SystemDriver -Filter "Name='PawnIO'"
    if (-not $driver) { throw 'PawnIO kernel driver registration missing.' }
    $version = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO').DisplayVersion
    if (-not $version) { throw 'PawnIO version registration missing.' }
    if ($attempt -eq 1) { $firstVersion = $version }
    elseif ($version -ne $firstVersion) { throw 'Reinstall changed the existing driver version.' }
    $service = Get-Service PitMedicSensor -ErrorAction Stop
    if ($service.Status -ne 'Running') { throw 'PitMedic sensor service did not start.' }
    $samplePath = Join-Path $env:ProgramData 'PitMedic/sensor.json'
    $fresh = $false
    for ($poll = 0; $poll -lt 20; $poll++) {
        if (Test-Path $samplePath) {
            try {
                $sample = Get-Content $samplePath -Raw | ConvertFrom-Json
                $fresh = ([DateTimeOffset]::UtcNow - [DateTimeOffset]$sample.Timestamp).TotalSeconds -lt 10
                if ($fresh) { break }
            } catch { }
        }
        Start-Sleep -Seconds 1
    }
    if (-not $fresh) { throw 'Sensor service did not publish a fresh sample.' }
    # Virtual CI hardware may not expose temperature; never require a fabricated reading.
}
$uninstaller = Join-Path $installRoot 'unins000.exe'
$process = Start-Process $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Uninstall failed.' }
if (-not (Get-CimInstance Win32_SystemDriver -Filter "Name='PawnIO'")) { throw 'PitMedic uninstall removed shared PawnIO driver.' }
Write-Host 'Fresh install, reinstall, fresh sensor publishing, and shared-driver preservation passed.'
