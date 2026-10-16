#ifndef ProjectRoot
  #define ProjectRoot ".."
#endif
#define AppVersion "1.1.0"

[Setup]
AppId={{598F8048-7195-4C0C-A1F1-3BC37D03927D}
AppName=Sarab Desktop
AppVersion={#AppVersion}
AppPublisher=Kreeli and contributors; powered by Sarab / TheMRVX
AppPublisherURL=https://github.com/Kreeli/sarab-desktop
AppSupportURL=https://github.com/Kreeli/sarab-desktop/issues
AppUpdatesURL=https://github.com/Kreeli/sarab-desktop/releases
AppCopyright=GUI: Kreeli and contributors. Sarab core: TheMRVX.
DefaultDirName={autopf}\SarabDesktop
DefaultGroupName=Sarab Desktop
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
OutputDir={#ProjectRoot}\releases
OutputBaseFilename=SarabDesktop-{#AppVersion}-Setup-x64
SetupIconFile={#ProjectRoot}\assets\sarab.ico
UninstallDisplayIcon={app}\SarabDesktop.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=115
DisableWelcomePage=no
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=no
CloseApplications=yes
RestartApplications=no
AppMutex=SarabDesktop.App
LicenseFile={#ProjectRoot}\installer\LICENSE-BUNDLE.txt
Uninstallable=yes
UninstallDisplayName=Sarab Desktop

[Languages]
Name: "en_US"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "{#ProjectRoot}\installer\BEFORE.en.txt"; InfoAfterFile: "{#ProjectRoot}\installer\AFTER.en.txt"
Name: "zh_CN"; MessagesFile: "ChineseSimplified.isl"; InfoBeforeFile: "{#ProjectRoot}\installer\BEFORE.zh-CN.txt"; InfoAfterFile: "{#ProjectRoot}\installer\AFTER.zh-CN.txt"

[CustomMessages]
en_US.DesktopShortcut=Create a desktop shortcut
zh_CN.DesktopShortcut=创建桌面快捷方式
en_US.OpenSetup=Open Sarab Desktop and its driver setup guide
zh_CN.OpenSetup=打开 Sarab Desktop 与驱动安装引导
en_US.AppLanguage=en-US
zh_CN.AppLanguage=zh-CN
en_US.RuntimeStartError=Could not start the bundled Microsoft Visual C++ x64 Runtime installer.
zh_CN.RuntimeStartError=无法启动内置的 Microsoft Visual C++ x64 运行库安装程序。
en_US.RuntimeInstallError=Microsoft Visual C++ x64 Runtime installation failed. Exit code:
zh_CN.RuntimeInstallError=Microsoft Visual C++ x64 运行库安装失败。退出代码：
en_US.RuntimeRestart=Microsoft Visual C++ Runtime was installed. Please restart Windows manually before installing or using the GNSS driver.
zh_CN.RuntimeRestart=Microsoft Visual C++ 运行库已安装。请在安装或使用 GNSS 驱动前手动重启 Windows。

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Files]
Source: "{#ProjectRoot}\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ProjectRoot}\dist\runtime\VC_redist.x64.exe"; Flags: dontcopy

[Icons]
Name: "{group}\Sarab Desktop"; Filename: "{app}\SarabDesktop.exe"
Name: "{group}\Sarab driver setup"; Filename: "{app}\SarabDesktop.exe"; Parameters: "--setup"
Name: "{group}\Contributors and licenses"; Filename: "{app}\CREDITS.html"
Name: "{autodesktop}\Sarab Desktop"; Filename: "{app}\SarabDesktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SarabDesktop.exe"; Parameters: "--setup --language {cm:AppLanguage}"; Description: "{cm:OpenSetup}"; Flags: nowait postinstall skipifsilent; Check: not IsTestInstall

[Code]
function IsTestInstall: Boolean;
begin
  Result := ExpandConstant('{param:TESTINSTALL|0}') = '1';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if IsTestInstall then begin
    Log('Isolated test installation: system runtime installation skipped.');
    exit;
  end;
  ExtractTemporaryFile('VC_redist.x64.exe');
  if not Exec(ExpandConstant('{tmp}\VC_redist.x64.exe'), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    Result := CustomMessage('RuntimeStartError');
    exit;
  end;
  if (ExitCode <> 0) and (ExitCode <> 1638) and (ExitCode <> 3010) then begin
    Result := CustomMessage('RuntimeInstallError') + ' ' + IntToStr(ExitCode);
    exit;
  end;
  if ExitCode = 3010 then begin
    Log('Microsoft Visual C++ runtime requests a restart. No restart will be scheduled by this installer.');
    SuppressibleMsgBox(CustomMessage('RuntimeRestart'), mbInformation, MB_OK, IDOK);
  end;
  NeedsRestart := False;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Existing, Expected: String;
begin
  if CurUninstallStep = usUninstall then begin
    Expected := '"' + ExpandConstant('{app}\SarabDesktop.exe') + '" --startup --tray';
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SarabDesktop', Existing) then
      if CompareText(Existing, Expected) = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SarabDesktop');
  end;
end;
