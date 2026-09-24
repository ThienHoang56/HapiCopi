; HapiCopi — Inno Setup
; Build: .\installer\build.ps1
; Output: artifacts\installer\HapiCopi-Setup.exe

#define MyAppName "HapiCopi"
; build.ps1 truyền phiên bản qua /DMyAppVersion; giá trị dưới chỉ dùng khi chạy ISCC bằng tay.
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "HapiCopi"
#define MyAppURL "https://github.com/ThienHoang56/HapiCopi"
#define MyAppExeName "HapiCopi.exe"
#define PublishDir "..\artifacts\publish"

[Setup]
AppId={{A8F3C2E1-9B47-4D6A-8E21-7C5B91F0A3D2}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
; Tên có phiên bản để nguồn cập nhật giữ được nhiều bản cạnh nhau (phục vụ quay lui).
OutputBaseFilename=HapiCopi-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\hapicopi.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a Desktop shortcut"; GroupDescription: "Additional options:"; Flags: unchecked
Name: "autostart"; Description: "Start HapiCopi with Windows"; GroupDescription: "Additional options:"; Flags: unchecked

[Files]
; Toàn bộ output publish (single-file exe + WebViewResources + DLL phụ nếu có)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\HapiCopi"
Type: filesandordirs; Name: "{localappdata}\HapiCopiWebViewCache"
