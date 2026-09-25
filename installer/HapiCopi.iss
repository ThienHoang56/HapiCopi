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
; App chạy ẩn ở khay hệ thống, không có cửa sổ để Restart Manager gửi WM_CLOSE tới.
; Việc đóng app do mục [Code] bên dưới lo, qua kênh lệnh --quit của chính app.
CloseApplications=no

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
; Cache WebView2, log, bộ cài đã tải về — rác của app, xoá thẳng.
Type: filesandordirs; Name: "{localappdata}\HapiCopi"
Type: filesandordirs; Name: "{localappdata}\HapiCopiWebViewCache"
; App có thể sinh file ngay cạnh exe lúc chạy; không quét cả thư mục thì nó sẽ nằm lại.
Type: filesandordirs; Name: "{app}"

[Code]
const
  AppExeName = 'HapiCopi.exe';

function AppIsRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;

  if Exec(ExpandConstant('{cmd}'),
          '/C tasklist /FI "IMAGENAME eq ' + AppExeName + '" /NH | find /I "' + AppExeName + '" > nul',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := (ResultCode = 0);
  end;
end;

/// Đóng app đang chạy. Trả về False nếu tới cuối vẫn còn tiến trình.
function StopRunningApp(): Boolean;
var
  ExePath: String;
  ResultCode, I: Integer;
begin
  if not AppIsRunning() then
  begin
    Result := True;
    Exit;
  end;

  // Đường êm: app tự đóng qua kênh lệnh của chính nó, kịp gỡ hook bàn phím,
  // ẩn icon khay và thả khoá thư mục cache WebView2.
  ExePath := ExpandConstant('{app}\' + AppExeName);
  if FileExists(ExePath) then
  begin
    Exec(ExePath, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  // Không tin mã thoát: bản cũ chưa biết --quit cũng trả về 0 rồi thoát ngay,
  // trong khi app vẫn đang chạy. Chỉ danh sách tiến trình mới nói thật.
  for I := 1 to 20 do
  begin
    if not AppIsRunning() then
    begin
      Result := True;
      Exit;
    end;
    Sleep(500);
  end;

  // App treo hoặc quá cũ: buộc phải tắt, nếu không file exe còn bị khoá và
  // thư mục cài đặt sẽ nằm lại sau khi gỡ.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ' + AppExeName, '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(2000);
  Result := not AppIsRunning();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  StopRunningApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  PersonalDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  // Lịch sử clipboard và cấu hình nằm ở %AppData% — dữ liệu riêng của người dùng, phải hỏi.
  PersonalDir := ExpandConstant('{userappdata}\HapiCopi');

  if not DirExists(PersonalDir) then
    Exit;

  // Gỡ im lặng thì không tự ý xoá dữ liệu khi chưa ai đồng ý.
  if UninstallSilent then
    Exit;

  if MsgBox('Xoá luôn dữ liệu cá nhân của HapiCopi?' + #13#10#13#10 +
            'Bao gồm lịch sử clipboard, cấu hình và danh sách loại trừ đang lưu tại:' + #13#10 +
            PersonalDir + #13#10#13#10 +
            'Chọn Không nếu bạn định cài lại và muốn giữ nguyên dữ liệu.',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES then
  begin
    DelTree(PersonalDir, True, True, True);
  end;
end;
