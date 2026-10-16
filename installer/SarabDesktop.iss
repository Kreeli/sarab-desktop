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

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Files]
Source: "{#ProjectRoot}\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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
