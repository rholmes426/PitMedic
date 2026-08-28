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
Filename: "{app}\PitMedic.exe"; Description: "Launch PitMedic"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runasoriginaluser
