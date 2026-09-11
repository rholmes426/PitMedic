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
UsePreviousAppDir=yes
DisableDirPage=auto
DirExistsWarning=no
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
Source: "..\Source\PitMedic\Assets\PitMedic.ico"; DestDir: "{app}\Assets"; DestName: "PitMedic-brand-v2.ico"; Flags: ignoreversion

[Dirs]
Name: "{commonappdata}\PitMedic"; Permissions: admins-full users-readexec; Flags: uninsneveruninstall
Name: "{commonappdata}\PitMedic\RepairBackups"; Permissions: admins-full users-readexec; Flags: uninsneveruninstall

[Icons]
Name: "{autoprograms}\PitMedic"; Filename: "{app}\PitMedic.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\PitMedic-brand-v2.ico"
Name: "{autodesktop}\PitMedic"; Filename: "{app}\PitMedic.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\PitMedic-brand-v2.ico"; Check: ShouldCreateDesktopShortcut

[Run]
Filename: "{app}\PitMedic.exe"; Description: "Launch PitMedic"; WorkingDir: "{app}"; Flags: nowait skipifsilent runasoriginaluser
Filename: "{app}\PitMedic.exe"; WorkingDir: "{app}"; Flags: nowait skipifnotsilent runasoriginaluser; Check: RestartPitMedicRequested

[UninstallDelete]
Type: files; Name: "{commonappdata}\PitMedic\sensor.json"
Type: files; Name: "{commonappdata}\PitMedic\sensor-*.tmp"
Type: files; Name: "{localappdata}\PitMedic\anonymous-usage.key"
Type: files; Name: "{localappdata}\PitMedic\anonymous-usage-state.json"
Type: files; Name: "{localappdata}\PitMedic\update-check-state.json"

[Code]
const
  PawnIOUrl = 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe';
  PawnIOHash = '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032';
  SensorServiceName = 'PitMedicSensor';
  PitMedicMutexName = 'PitMedic-E805E797-5FEF-4D91-8B72-0E20C53D2E09';

function PawnIOInstalled(): Boolean;
var
  Version: String;
begin
  Result := (RegQueryStringValue(HKLM64,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO',
    'DisplayVersion', Version) or RegQueryStringValue(HKLM32,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO',
    'DisplayVersion', Version)) and
    RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

function EnsurePawnIO(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if PawnIOInstalled() then
  begin
    Log('PawnIO prerequisite already registered; preserving the existing installation.');
    Exit;
  end;
  WizardForm.StatusLabel.Caption := 'Setting up the CPU sensor driver...';
  try
    DownloadTemporaryFile(PawnIOUrl, 'PawnIO_setup.exe', PawnIOHash, nil);
    if not Exec(ExpandConstant('{tmp}\PawnIO_setup.exe'), '-install -silent',
      '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException('Could not start the CPU sensor driver installer.');
    Log(Format('PawnIO setup exit code: %d', [ExitCode]));
    if ExitCode = 3010 then
      NeedsRestart := True
    else if ExitCode <> 0 then
      RaiseException(Format('CPU sensor driver setup failed (code %d).', [ExitCode]));
    if not PawnIOInstalled() then
      RaiseException('CPU sensor driver registration was not found after setup.');
  except
    Result := GetExceptionMessage + #13#10 +
      'PitMedic setup cannot finish its CPU monitoring prerequisites. Check your internet connection and retry, or install the signed driver from https://pawnio.eu/ and rerun setup.';
    Log(Result);
  end;
end;

function ShouldCreateDesktopShortcut(): Boolean;
begin
  { Refresh an existing installer shortcut even if the task is not selected. }
  Result := WizardIsTaskSelected('desktopicon') or
    FileExists(ExpandConstant('{autodesktop}\PitMedic.lnk'));
end;

function RestartPitMedicRequested(): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
  begin
    if CompareText(ParamStr(Index), '/RESTARTPITMEDIC=1') = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

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
    ' start= delayed-auto DisplayName= "PitMedic Sensor Service"';
  Exec(ExpandConstant('{sys}\sc.exe'), Parameters,
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);

  Parameters := 'config ' + SensorServiceName +
    ' binPath= "\"' + SensorExecutable + '\""' +
    ' start= delayed-auto DisplayName= "PitMedic Sensor Service"';
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
    'failure ' + SensorServiceName + ' reset= 86400 actions= restart/5000/restart/30000/restart/60000',
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
  Result := EnsurePawnIO(NeedsRestart);
  if Result <> '' then Exit;
  StopSensorService();
  RemoveLegacyStartupTasks();
end;

function VariantText(Value: Variant): String;
begin
  Result := Value;
end;

procedure CheckDeviceDrivers();
var
  Wmi, Devices, Device: Variant;
  Index: Integer;
  Warnings: String;
begin
  { Read-only detection: hardware-specific packages remain with Windows/OEMs. }
  try
    Wmi := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Wmi.ConnectServer('', 'root\CIMV2');
    Devices := Wmi.ExecQuery('SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0');
    Warnings := '';
    for Index := 0 to Devices.Count - 1 do
    begin
      Device := Devices.ItemIndex(Index);
      Warnings := Warnings + VariantText(Device.Name) + ' (Windows code ' +
        VariantText(Device.ConfigManagerErrorCode) + ')' + #13#10;
    end;
    Devices := Wmi.ExecQuery('SELECT Name FROM Win32_VideoController');
    for Index := 0 to Devices.Count - 1 do
    begin
      Device := Devices.ItemIndex(Index);
      if Pos('Microsoft Basic Display', VariantText(Device.Name)) > 0 then
        Warnings := Warnings + 'Generic display driver detected.' + #13#10;
    end;
    if Warnings <> '' then
    begin
      Log('Device driver attention required: ' + Warnings);
      SaveStringToFile(ExpandConstant('{commonappdata}\PitMedic\driver-setup.txt'),
        Warnings + 'Check Windows Update or your PC manufacturer for the matching drivers.', False);
      if not WizardSilent() then
        MsgBox('PitMedic is installed. Windows reports devices that need attention:' + #13#10 +
          Warnings + #13#10 +
          'Open Settings in PitMedic for driver guidance and Windows Update.', mbInformation, MB_OK);
    end;
  except
    Log('Device driver checks could not finish: ' + GetExceptionMessage);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    InstallSensorService();
    CheckDeviceDrivers();
  end;
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
