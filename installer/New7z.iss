#ifndef AppVersion
 #define AppVersion "0.1.18"
#endif
#ifndef BuildId
 #error BuildId is required
#endif
#ifndef PayloadDir
 #error PayloadDir is required
#endif
#ifndef ReleaseDir
 #error ReleaseDir is required
#endif
#define RuntimeDir "Versions\" + AppVersion + "-" + BuildId
#ifndef InstallerAppId
 #define InstallerAppId "{{4F8A4D03-F13A-40C1-91EE-6D9957C98406}"
#endif
#ifndef SetupBaseName
 #define SetupBaseName "New7z-" + AppVersion + "-x64-Setup"
#endif

[Setup]
AppId={#InstallerAppId}
AppName=New7z
AppVersion={#AppVersion}
AppPublisher=王国梁
AppPublisherURL=https://github.com/outsideoftime/new-7z
AppVerName=New7z {#AppVersion}
VersionInfoVersion={#AppVersion}.0
DefaultDirName={localappdata}\Programs\New7z
DefaultGroupName=New7z
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern dynamic windows11
SetupIconFile={#PayloadDir}\Assets\New7z.ico
UninstallDisplayIcon={app}\{#RuntimeDir}\New7z.App.exe
OutputDir={#ReleaseDir}
OutputBaseFilename={#SetupBaseName}
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
AllowNoIcons=yes
ChangesAssociations=yes
InfoBeforeFile=InstallNotes.txt
UninstallFilesDir={app}\Uninstall
SetupLogging=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "shellmenu"; Description: "添加 New7z 资源管理器右键菜单"; GroupDescription: "系统集成："
Name: "fileassoc"; Description: "将 New7z 注册到默认应用和压缩包打开方式列表"; GroupDescription: "系统集成："
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}\{#RuntimeDir}"; Excludes: "*.pdb"; Flags: recursesubdirs createallsubdirs
Source: "Integrate-Shell.ps1"; DestDir: "{app}\Installer"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\New7z"; Filename: "{app}\{#RuntimeDir}\New7z.App.exe"; WorkingDir: "{app}\{#RuntimeDir}"
Name: "{userdesktop}\New7z"; Filename: "{app}\{#RuntimeDir}\New7z.App.exe"; WorkingDir: "{app}\{#RuntimeDir}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#RuntimeDir}\New7z.App.exe"; Description: "启动 New7z"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  MOVEFILE_DELAY_UNTIL_REBOOT = 4;

function MoveFileExW(lpExistingFileName, lpNewFileName: String; dwFlags: Cardinal): Boolean;
  external 'MoveFileExW@kernel32.dll stdcall';

procedure ScheduleDelete(const FileName: String);
begin
  if not DeleteFile(FileName) then begin
    if MoveFileExW(FileName, '', MOVEFILE_DELAY_UNTIL_REBOOT) then
      Log('Scheduled for deletion after reboot: ' + FileName)
    else
      Log('Could not delete or schedule: ' + FileName);
  end;
end;

procedure DeleteTree(const Dir: String);
var
  FindRec: TFindRec;
  Item: String;
begin
  if FindFirst(Dir + '\*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then begin
        Item := Dir + '\' + FindRec.Name;
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          DeleteTree(Item)
        else
          ScheduleDelete(Item);
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
  RemoveDir(Dir);
end;

procedure CleanOldRuntimes;
var
  VersionsDir, TargetName, Dir: String;
  FindRec: TFindRec;
begin
  VersionsDir := ExpandConstant('{app}\Versions');
  TargetName := '{#AppVersion}-{#BuildId}';
  if not FindFirst(VersionsDir + '\*', FindRec) then exit;
  try
    repeat
      if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0)
        and (FindRec.Name <> '.') and (FindRec.Name <> '..')
        and (FindRec.Name <> TargetName) then begin
        Dir := VersionsDir + '\' + FindRec.Name;
        Log('Removing previous runtime: ' + Dir);
        DeleteTree(Dir);
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

procedure CleanCrashDumps;
var
  DumpDir: String;
  FindRec: TFindRec;
begin
  DumpDir := ExpandConstant('{localappdata}\CrashDumps');
  if not FindFirst(DumpDir + '\New7z.App.exe.*.dmp', FindRec) then exit;
  try
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then begin
        Log('Removing New7z crash dump: ' + FindRec.Name);
        ScheduleDelete(DumpDir + '\' + FindRec.Name);
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

function IntegrationParameters(Action: String; IncludeRuntime: Boolean): String;
var Enabled, Associated, ClassicOnly: String;
begin
  Enabled := '0';
  if WizardIsTaskSelected('shellmenu') then Enabled := '1';
  Associated := '0';
  if WizardIsTaskSelected('fileassoc') then Associated := '1';
  ClassicOnly := '';
  if ExpandConstant('{param:CLASSICMENU|0}') = '1' then ClassicOnly := ' -ClassicOnly';
  Result := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{app}\Installer\Integrate-Shell.ps1') + '" -Action ' + Action +
    ' -InstallRoot "' + ExpandConstant('{app}') + '"';
  if IncludeRuntime then Result := Result + ' -RuntimeDirectory "' +
    ExpandConstant('{app}\{#RuntimeDir}') + '" -EnableMenu ' + Enabled + ' -EnableAssociations ' + Associated + ClassicOnly;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var ResultCode: Integer;
begin
  if CurStep = ssInstall then begin
    CleanOldRuntimes;
    CleanCrashDumps;
  end
  else if CurStep = ssPostInstall then begin
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      IntegrationParameters('Install', True), '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
      or (ResultCode <> 0) then begin
      Log('Shell integration failed. See integration-error.txt in the installation folder.');
      SuppressibleMsgBox('程序文件已安装，但右键菜单注册未完成。详细原因见安装目录中的 integration-error.txt。', mbError, MB_OK, IDOK);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var ResultCode: Integer; Parameters: String;
begin
  if CurUninstallStep = usUninstall then begin
    Parameters := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{app}\Installer\Integrate-Shell.ps1') + '" -Action Remove -InstallRoot "' + ExpandConstant('{app}') + '"';
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parameters,
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      RaiseException('无法移除右键菜单，请关闭 New7z 后重试卸载。');
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Versions"
Type: files; Name: "{app}\installation.json"
Type: files; Name: "{app}\integration-error.txt"
