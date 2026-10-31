#define AppName "NEXUS PC"
#define AppVersion "0.1.0"
#define AppPublisher "NEXUS PC"
#define AppExeName "NexusPC.Desktop.exe"

[Setup]
AppId={{B544F62C-CFF4-4C24-9B52-5CA4A2F57D2D}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\NEXUS PC
DefaultGroupName=NEXUS PC
OutputDir=..\artifacts\installer
OutputBaseFilename=NexusPC-Setup-x64
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
PrivilegesRequired=admin
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
SetupLogging=yes
WizardStyle=modern
; Authenticode signing is intentionally omitted for local development builds.

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Ek kısayollar:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\NEXUS PC"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\NEXUS PC"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "NEXUS PC'yi başlat"; Flags: postinstall nowait skipifsilent

; No UninstallDelete entries: per-user settings and backups are preserved.
