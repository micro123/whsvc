#define MyAppName "Wallhaven 壁纸服务"
#define MyAppExeName "WallhavenService.exe"

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#ifndef LanguageFile
  #define LanguageFile "compiler:Default.isl"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{D8B3E5A0-3F92-4F08-B1E5-9E49B5C9A8F2}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher=Wallhaven Service
DefaultDirName={localappdata}\Programs\WallhavenService
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=WallhavenService-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#SourceDir}\Assets\App.ico
UninstallDisplayIcon={app}\Assets\App.ico
Uninstallable=yes
; Closing the main window normally hides it to the tray. Force Restart Manager
; shutdown during upgrades so the running background process releases its files.
CloseApplications=force
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#LanguageFile}"

[Tasks]
Name: "startup"; Description: "开机时自动启动 Wallhaven 壁纸服务"; GroupDescription: "附加选项:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: postinstall nowait skipifsilent unchecked

[Code]
function InstalledApplicationProcesses(Services: Variant): Variant;
begin
  Result := Services.ExecQuery(
    'SELECT * FROM Win32_Process WHERE Name = ''{#MyAppExeName}''');
end;

function IsInstalledApplication(Process: Variant): Boolean;
begin
  Result := False;
  if not VarIsNull(Process.ExecutablePath) then
    Result := CompareText(Process.ExecutablePath,
      ExpandConstant('{app}\{#MyAppExeName}')) = 0;
end;

function StopInstalledApplication: String;
var
  Locator, Services, Processes, Process: Variant;
  I, Attempt, ExitResult: Integer;
  StillRunning: Boolean;
begin
  Result := '';
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    Processes := InstalledApplicationProcesses(Services);
    for I := 0 to Processes.Count - 1 do
    begin
      Process := Processes.ItemIndex(I);
      if IsInstalledApplication(Process) then
      begin
        Log(Format('Stopping installed application PID %d: %s', [
          Integer(Process.ProcessId), String(Process.ExecutablePath)]));
        ExitResult := Process.Terminate(0);
        if ExitResult <> 0 then
          RaiseException(Format('无法结束旧程序（错误码 %d）。请从托盘退出程序后重试。', [ExitResult]));
      end;
    end;

    { Restart Manager can be unavailable when it also detects System or an
      application in another session. Verify our own process independently,
      before replacing any files, including when reinstalling the same version. }
    for Attempt := 0 to 50 do
    begin
      StillRunning := False;
      Processes := InstalledApplicationProcesses(Services);
      for I := 0 to Processes.Count - 1 do
        if IsInstalledApplication(Processes.ItemIndex(I)) then
          StillRunning := True;
      if not StillRunning then
      begin
        Log('Installed application stopped; safe to replace application files.');
        Exit;
      end;
      Sleep(200);
    end;
    Result := '旧程序仍在运行，尚未替换文件。请从托盘退出程序后重试。';
  except
    Result := '准备安装时无法关闭旧程序：' + GetExceptionMessage;
  end;
  if Result <> '' then
    Log(Result);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := StopInstalledApplication;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Error: String;
begin
  if CurStep = ssInstall then
  begin
    { Check again if the user relaunched the app on the preparation page. }
    Error := StopInstalledApplication;
    if Error <> '' then
      RaiseException(Error);
  end;
end;
