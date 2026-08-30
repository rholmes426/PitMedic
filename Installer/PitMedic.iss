#ifndef AppVersion
  #error AppVersion must be supplied by Build-Installer.ps1
#endif
#ifndef PayloadDir
  #error PayloadDir must be supplied by Build-Installer.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by Build-Installer.ps1
#endif

[Setup]
AppId={{E805E797-5FEF-4D91-8B72-0E20C53D2E09}
AppName=PitMedic
AppVersion={#AppVersion}
AppVerName=PitMedic {#AppVersion}
AppPublisher=PitMedic Project
AppPublisherURL=https://pitmedic.com/
AppSupportURL=https://github.com/rholmes426/PitMedic/issues
AppUpdatesURL=https://pitmedic.com/
DefaultDirName={autopf}\PitMedic
DefaultGroupName=PitMedic
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\PitMedic.exe
LicenseFile=..\LICENSE
SetupIconFile=..\Source\PitMedic\Assets\PitMedic.ico
OutputDir={#OutputDir}
OutputBaseFilename=PitMedic-Setup-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=PitMedic.exe,PitMedic.RepairHelper.exe
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoCompany=PitMedic Project
VersionInfoDescription=PitMedic installer
VersionInfoProductName=PitMedic
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (c) 2026 PitMedic contributors

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupAppRunningError=PitMedic is still running. Right-click the PitMedic tray icon, choose Exit, and then click Retry.
UninstallAppRunningError=PitMedic is still running. Right-click the PitMedic tray icon, choose Exit, and then click Retry.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\PitMedic"; Permissions: admins-full users-readexec; Flags: uninsneveruninstall
Name: "{commonappdata}\PitMedic\RepairBackups"; Permissions: admins-full users-readexec; Flags: uninsneveruninstall

[Icons]
Name: "{autoprograms}\PitMedic"; Filename: "{app}\PitMedic.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\PitMedic"; Filename: "{app}\PitMedic.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\PitMedic.exe"; Description: "Launch PitMedic"; WorkingDir: "{app}"; Flags: nowait skipifsilent runasoriginaluser

[UninstallDelete]
Type: files; Name: "{commonappdata}\PitMedic\sensor.json"
Type: files; Name: "{commonappdata}\PitMedic\sensor-*.tmp"
Type: files; Name: "{localappdata}\PitMedic\anonymous-usage.key"
Type: files; Name: "{localappdata}\PitMedic\anonymous-usage-state.json"
Type: files; Name: "{localappdata}\PitMedic\update-check-state.json"

[Code]
const
  SensorServiceName = 'PitMedicSensor';
  PitMedicMutexName = 'PitMedic-E805E797-5FEF-4D91-8B72-0E20C53D2E09';

function ExistingPitMedicPath(): String;
var
  InstallLocation: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM64,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{E805E797-5FEF-4D91-8B72-0E20C53D2E09}_is1',
      'InstallLocation', InstallLocation) then
    Result := AddBackslash(InstallLocation) + 'PitMedic.exe'
  else if FileExists(ExpandConstant('{autopf}\PitMedic\PitMedic.exe')) then
    Result := ExpandConstant('{autopf}\PitMedic\PitMedic.exe');
end;

function InitializeSetup(): Boolean;
var
  ExistingExe: String;
  ExitCode: Integer;
  WaitedMs: Integer;
begin
  Result := True;
  if not CheckForMutexes(PitMedicMutexName) then
    exit;

  ExistingExe := ExistingPitMedicPath();
  if (ExistingExe <> '') and FileExists(ExistingExe) then
    Exec(ExistingExe, '--shutdown-for-maintenance', ExtractFileDir(ExistingExe),
      SW_HIDE, ewWaitUntilTerminated, ExitCode);

  WaitedMs := 0;
  while CheckForMutexes(PitMedicMutexName) and (WaitedMs < 15000) do
  begin
    Sleep(250);
    WaitedMs := WaitedMs + 250;
  end;

  if CheckForMutexes(PitMedicMutexName) then
  begin
    MsgBox('PitMedic could not close automatically. A repair may still be active. Use Exit from the PitMedic tray icon, then run this installer again.',
      mbError, MB_OK);
    Result := False;
  end;
end;

procedure StopSensorService();
var
  ExitCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop ' + SensorServiceName,
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Sleep(1500);
end;

procedure RemoveLegacyStartupTasks();
var
  ExitCode: Integer;
begin
  { v0.5 and earlier used Task Scheduler. The app now uses the lightweight
    per-user Run key, so retire both historic task names once during upgrade. }
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "PitMedic" /F',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "SimWatch" /F',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;

procedure InstallSensorService();
var
  ExitCode: Integer;
  SensorExecutable: String;
  Parameters: String;
begin
  SensorExecutable := ExpandConstant('{app}\PitMedic.SensorHelper.exe');

  Parameters := 'create ' + SensorServiceName +
    ' binPath= "\"' + SensorExecutable + '\""' +
    ' start= auto DisplayName= "PitMedic Sensor Service"';
  Exec(ExpandConstant('{sys}\sc.exe'), Parameters,
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);

  Parameters := 'config ' + SensorServiceName +
    ' binPath= "\"' + SensorExecutable + '\""' +
    ' start= auto DisplayName= "PitMedic Sensor Service"';
  if not Exec(ExpandConstant('{sys}\sc.exe'), Parameters,
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
  begin
    MsgBox('PitMedic was installed, but its read-only CPU sensor service could not be configured. The app will still work, although CPU temperature may be unavailable.',
      mbError, MB_OK);
    Exit;
  end;

  Exec(ExpandConstant('{sys}\sc.exe'),
    'description ' + SensorServiceName + ' "Provides read-only CPU telemetry to the locally installed PitMedic app."',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Exec(ExpandConstant('{sys}\sc.exe'),
    'failure ' + SensorServiceName + ' reset= 86400 actions= restart/5000',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);

  if not Exec(ExpandConstant('{sys}\sc.exe'), 'start ' + SensorServiceName,
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
  begin
    MsgBox('PitMedic was installed, but its read-only CPU sensor service did not start. Restart Windows or reinstall PitMedic if CPU temperature remains unavailable.',
      mbError, MB_OK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopSensorService();
  RemoveLegacyStartupTasks();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallSensorService();
end;

function InitializeUninstall(): Boolean;
var
  ExitCode: Integer;
begin
  Result := True;
  if FileExists(ExpandConstant('{app}\PitMedic.exe')) then
    Exec(ExpandConstant('{app}\PitMedic.exe'), '--shutdown-for-maintenance',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode);
  StopSensorService();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExitCode: Integer;
begin
  if CurUninstallStep = usUninstall then
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete ' + SensorServiceName,
      '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;
