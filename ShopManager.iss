#define AppName "ShopManager"
#define AppPublisher "Jerlsy"
#define AppExeName "ShopManager.exe"

[Setup]
AppId={{A3F2E1D0-4B5C-4A6D-8E7F-9C0B1A2D3E4F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename=ShopManager-Setup-{#AppVersion}
OutputDir=installer-out
SetupIconFile=Resources\app.ico
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
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
