#define AppName "ShopManager"
#define AppPublisher "Jerlsy"
#define AppExeName "ShopManager.exe"

[Setup]
AppId={{A3F2E1D0-4B5C-4A6D-8E7F-9C0B1A2D3E4F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename=ShopManager-Setup-{#AppVersion}
OutputDir=installer-out
SetupIconFile=Resources\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷徑"; GroupDescription: "附加工作:"

[Files]
Source: "publish-out\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "立即執行 {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;
  NeedDotNet: Boolean;
  NeedVCRedist: Boolean;

function IsDotNetDesktopRuntimeInstalled(): Boolean;
var
  SubkeyNames: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', SubkeyNames) then
    for I := 0 to GetArrayLength(SubkeyNames) - 1 do
      if Pos('10.', SubkeyNames[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
end;

function IsVCRedistInstalled(): Boolean;
var
  Installed: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed)
            and (Installed = 1);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    '正在安裝必要元件',
    '請稍候，正在下載必要的系統元件...',
    nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  if CurPageID = wpReady then
  begin
    NeedDotNet   := not IsDotNetDesktopRuntimeInstalled();
    NeedVCRedist := not IsVCRedistInstalled();

    if NeedDotNet or NeedVCRedist then
    begin
      DownloadPage.Clear;
      if NeedDotNet then
        DownloadPage.Add(
          'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe',
          'dotnet-runtime.exe', '');
      if NeedVCRedist then
        DownloadPage.Add(
          'https://aka.ms/vs/17/release/vc_redist.x64.exe',
          'vc_redist.x64.exe', '');

      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
        except
          MsgBox('下載必要元件失敗，請確認網路連線後重試。'#13#10 + GetExceptionMessage, mbError, MB_OK);
          Result := False;
          Exit;
        end;
      finally
        DownloadPage.Hide;
      end;

      if NeedDotNet then
      begin
        Exec(ExpandConstant('{tmp}\dotnet-runtime.exe'),
             '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
        if ResultCode <> 0 then
        begin
          MsgBox('.NET 10 Desktop Runtime 安裝失敗（代碼 ' + IntToStr(ResultCode) + '）', mbError, MB_OK);
          Result := False;
          Exit;
        end;
      end;

      if NeedVCRedist then
      begin
        Exec(ExpandConstant('{tmp}\vc_redist.x64.exe'),
             '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
        if ResultCode <> 0 then
        begin
          MsgBox('Visual C++ Redistributable 安裝失敗（代碼 ' + IntToStr(ResultCode) + '）', mbError, MB_OK);
          Result := False;
          Exit;
        end;
      end;
    end;
  end;
end;
