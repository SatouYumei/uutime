; uutime 安装包。由 build.ps1 调用，版本号从 uutime.csproj 传进来。
; 程序装在 %LOCALAPPDATA%\Programs\uutime，数据在 %LOCALAPPDATA%\uutime，卸载时保留数据。

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "uutime"
#define AppExe "uutime.exe"
#define AppMutexName "Local\uutime.single"

[Setup]
AppId={{5E2B9C61-3F4A-4D8E-A1B7-7C0D2E9F4A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={localappdata}\Programs\uutime
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=uutime-Setup-{#AppVersion}
SetupIconFile=..\uutime.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; 安装/卸载前提示关闭正在运行的 uutime（程序里的单实例互斥量）
AppMutex={#AppMutexName}
CloseApplications=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"; Flags: checkedonce
Name: "autostart"; Description: "开机自动启动（在托盘里记录）"; GroupDescription: "其他:"

[Files]
Source: "..\build\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; 和程序里“开机自启”开关写的是同一个值，卸载时一并删除
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "uutime"; ValueData: """{app}\{#AppExe}"" --tray"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "启动 uutime"; Flags: nowait postinstall skipifsilent
; 程序内更新是静默安装，装完重新启动并打开一次主窗口
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait; Check: IsAutoUpdate

[Code]
function DesktopRuntimeInstalled: Boolean;
var
  Rec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), Rec);
  if Result then
    FindClose(Rec);
end;

function IsAutoUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function InitializeSetup: Boolean;
var
  ErrorCode, Waited: Integer;
begin
  Result := True;
  // 自动更新时 uutime 刚开始退出，等它真正退出（最多 20 秒）
  if IsAutoUpdate then
  begin
    Waited := 0;
    while CheckForMutexes('{#AppMutexName}') and (Waited < 20000) do
    begin
      Sleep(250);
      Waited := Waited + 250;
    end;
  end;
  if DesktopRuntimeInstalled then
    Exit;
  MsgBox('uutime 需要 .NET 10 桌面运行时（Windows Desktop Runtime x64）。' + #13#10 + #13#10 +
    '请先到微软官方页面下载安装，然后再运行这个安装包。' + #13#10 +
    'https://dotnet.microsoft.com/download/dotnet/10.0',
    mbInformation, MB_OK);
  ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  Result := False;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // 用户在程序里打开过“开机自启”的话，安装时没写这个值，这里也要删掉
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'uutime');
end;
