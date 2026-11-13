; DeskPal 安装向导：让用户选择安装目录，再调用内嵌的 Velopack Setup.exe（--installto）完成安装。
; 卸载项、快捷方式和自动更新都由 Velopack 负责，所以这里不注册 Inno 自己的卸载项。
; Velopack 安装时会清空目标目录，所以拒绝已有其他文件的目录，覆盖安装时先保留 logs\。
; 编译：ISCC /DAppVersion=<版本> /DVelopackSetup=<vpk pack 生成的 Setup.exe> /O<输出目录> installer\DeskPal.iss

#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif
#ifndef VelopackSetup
  #error 需要 /DVelopackSetup=<vpk pack 生成的 Setup.exe>
#endif

[Setup]
AppId=DeskPal
AppName=DeskPal
AppVersion={#AppVersion}
AppVerName=DeskPal {#AppVersion}
AppPublisher=DeskPal
DefaultDirName={code:DefaultInstallDir}
UsePreviousAppDir=no
DirExistsWarning=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
Uninstallable=no
CreateUninstallRegKey=no
CloseApplications=no
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern
ShowLanguageDialog=no
OutputDir=Output
OutputBaseFilename=DeskPal-{#AppVersion}-Setup

; 按 Windows 显示语言自动选择：中文系统用简体中文，其他语言用第一项英文。
[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "chs"; MessagesFile: "ChineseSimplified.isl"

[CustomMessages]
en.LaunchApp=Launch DeskPal
chs.LaunchApp=启动 DeskPal
en.RootDir=DeskPal cannot be installed in the root of a drive. Choose a folder, such as %1.
chs.RootDir=不能直接安装到磁盘根目录，请选择一个文件夹，例如 %1。
en.DirNotEmpty=The selected folder contains other files. Setup empties the installation folder, so choose an empty or new folder.
chs.DirNotEmpty=所选文件夹里已有其他文件。安装时会清空安装文件夹，请选择一个空文件夹或新文件夹。
en.DirNotWritable=Cannot write to the selected folder. Choose another location. DeskPal is installed for the current user only and cannot be installed in folders that require administrator rights, such as Program Files.
chs.DirNotWritable=无法写入所选文件夹，请选择其他位置。DeskPal 只为当前用户安装，不能装到 Program Files 等需要管理员权限的文件夹。
en.MoveInstall=DeskPal is already installed in:%n%1%n%nThe old folder is not deleted when you install to a new folder. You can delete it yourself after setup finishes. Your sign-in and settings are kept.%nIf DeskPal is running, exit it from the tray menu first.%n%nContinue?
chs.MoveInstall=DeskPal 已经安装在：%n%1%n%n安装到新文件夹后，原来的文件夹不会自动删除，可以在安装完成后手动删除。登录状态和设置会保留。%n如果 DeskPal 正在运行，请先从托盘菜单退出。%n%n是否继续？
en.KeepLogsFailed=Cannot keep the old log files. Exit DeskPal from the tray menu and run Setup again.
chs.KeepLogsFailed=无法保留旧的日志文件。请从托盘菜单退出 DeskPal 后重新安装。
en.Installing=Installing DeskPal…
chs.Installing=正在安装 DeskPal，请稍候…
en.StartFailed=Cannot start the installer: %1
chs.StartFailed=无法启动安装程序：%1
en.InstallFailed=Installation failed (exit code %1). Setup log: %2
chs.InstallFailed=安装失败（退出码 %1）。安装日志：%2

[Files]
Source: "{#VelopackSetup}"; DestName: "VelopackSetup.exe"; Flags: dontcopy nocompression

[Run]
Filename: "{app}\DeskPal.exe"; Description: "{cm:LaunchApp}"; Flags: postinstall nowait skipifsilent

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskPal';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function PreviousInstallDir(): String;
begin
  if not RegQueryStringValue(HKCU, UninstallKey, 'InstallLocation', Result) then
    Result := '';
  Result := RemoveBackslashUnlessRoot(Result);
end;

{ Velopack 会清空它认定的安装目录，所以只认 DeskPal 自己的目录结构。 }
function IsDeskPalInstall(Dir: String): Boolean;
begin
  Result := FileExists(AddBackslash(Dir) + 'DeskPal.exe') and FileExists(AddBackslash(Dir) + 'Update.exe')
    and FileExists(AddBackslash(Dir) + 'current\DeskPal.exe');
end;

function DefaultInstallDir(Param: String): String;
begin
  Result := PreviousInstallDir();
  if (Result = '') or not IsDeskPalInstall(Result) then
    Result := ExpandConstant('{localappdata}\Programs\DeskPal');
end;

function IsEmptyDir(Dir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := True;
  if FindFirst(AddBackslash(Dir) + '*', FindRec) then
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        Result := False;
        Break;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

function NearestExistingDir(Dir: String): String;
begin
  Result := Dir;
  while not DirExists(Result) do
  begin
    if ExtractFileDir(Result) = Result then
    begin
      Result := '';
      Exit;
    end;
    Result := ExtractFileDir(Result);
  end;
end;

function CanWrite(Dir: String): Boolean;
var
  Probe: String;
begin
  Probe := AddBackslash(Dir) + '.deskpal-write-test';
  Result := SaveStringToFile(Probe, '', False);
  if Result then
    DeleteFile(Probe);
end;

{ 静默安装不经过目录页，所以 PrepareToInstall 里再检查一次。 }
function InstallDirError(Dir: String): String;
var
  Existing: String;
begin
  Result := '';
  if ExtractFileDir(Dir) = Dir then
    Result := FmtMessage(CustomMessage('RootDir'), [AddBackslash(Dir) + 'DeskPal'])
  else if DirExists(Dir) and not IsDeskPalInstall(Dir) and not IsEmptyDir(Dir) then
    Result := CustomMessage('DirNotEmpty')
  else
  begin
    Existing := NearestExistingDir(Dir);
    if (Existing = '') or not CanWrite(Existing) then
      Result := CustomMessage('DirNotWritable');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := InstallDirError(RemoveBackslashUnlessRoot(WizardDirValue()));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir, Error, Previous: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then
    Exit;

  Dir := RemoveBackslashUnlessRoot(WizardDirValue());
  Error := InstallDirError(Dir);
  if Error <> '' then
  begin
    MsgBox(Error, mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Previous := PreviousInstallDir();
  if (Previous <> '') and IsDeskPalInstall(Previous) and (CompareText(Previous, Dir) <> 0) then
    Result := MsgBox(FmtMessage(CustomMessage('MoveInstall'), [Previous]), mbConfirmation, MB_YESNO) = IDYES;
end;

{ DeskPal 每写一行日志就打开一次文件，复制时可能碰上一瞬间的共享冲突。 }
function CopyWithRetry(FromFile, ToFile: String): Boolean;
var
  Attempt: Integer;
begin
  for Attempt := 1 to 5 do
  begin
    Result := CopyFile(FromFile, ToFile, False);
    if Result then
      Exit;
    Sleep(200);
  end;
end;

function CopyFilesIn(FromDir, ToDir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := True;
  if not FindFirst(AddBackslash(FromDir) + '*', FindRec) then
    Exit;
  try
    ForceDirectories(ToDir);
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY = 0)
        and not CopyWithRetry(AddBackslash(FromDir) + FindRec.Name, AddBackslash(ToDir) + FindRec.Name) then
        Result := False;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

{ 开机自启记录的是旧目录下的 current\DeskPal.exe，换目录后改成新目录。 }
procedure MoveAutoStart(Previous, Dir: String);
var
  Value: String;
begin
  if (Previous = '') or (CompareText(Previous, Dir) = 0) then
    Exit;
  if RegQueryStringValue(HKCU, RunKey, 'DeskPal', Value) and (Pos(Lowercase(AddBackslash(Previous)), Lowercase(Value)) > 0) then
    RegWriteStringValue(HKCU, RunKey, 'DeskPal', '"' + AddBackslash(Dir) + 'current\DeskPal.exe" --minimized');
end;

{ 在 ssInstall 里调用：这里抛出异常，Inno 按安装失败处理，静默安装返回非 0。 }
procedure CurStepChanged(CurStep: TSetupStep);
var
  Dir, Previous, Backup, Setup, Log: String;
  Code: Integer;
begin
  if CurStep <> ssInstall then
    Exit;

  Dir := RemoveBackslashUnlessRoot(WizardDirValue());
  Previous := PreviousInstallDir();
  Backup := ExpandConstant('{tmp}\logs-backup');
  if IsDeskPalInstall(Dir) and not CopyFilesIn(AddBackslash(Dir) + 'logs', Backup) then
    RaiseException(CustomMessage('KeepLogsFailed'));

  ExtractTemporaryFile('VelopackSetup.exe');
  Setup := ExpandConstant('{tmp}\VelopackSetup.exe');
  Log := AddBackslash(GetTempDir()) + 'DeskPal-setup.log';
  DeleteFile(Log);

  WizardForm.StatusLabel.Caption := CustomMessage('Installing');
  WizardForm.ProgressGauge.Style := npbstMarquee;
  try
    if not Exec(Setup, '--silent --installto "' + Dir + '" --log "' + Log + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    begin
      CopyFilesIn(Backup, AddBackslash(Dir) + 'logs');
      RaiseException(FmtMessage(CustomMessage('StartFailed'), [SysErrorMessage(Code)]));
    end;
    if Code <> 0 then
    begin
      CopyFilesIn(Backup, AddBackslash(Dir) + 'logs');
      RaiseException(FmtMessage(CustomMessage('InstallFailed'), [IntToStr(Code), Log]));
    end;
  finally
    WizardForm.ProgressGauge.Style := npbstNormal;
  end;

  ForceDirectories(AddBackslash(Dir) + 'logs');
  CopyFilesIn(Backup, AddBackslash(Dir) + 'logs');
  CopyWithRetry(Log, AddBackslash(Dir) + 'logs\setup.log');
  MoveAutoStart(Previous, Dir);
end;