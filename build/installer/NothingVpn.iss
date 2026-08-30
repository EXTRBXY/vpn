; Inno Setup script for Nothing VPN WPF desktop client
; Build prerequisites:
; Use build\Build.ps1 -Target Installer. Paths are injected by the build script.
; 2) Put sing-box.exe into the publish folder next to NothingVpn.Desktop.Wpf.exe
; 3) Compile this .iss with Inno Setup (ISCC.exe)

#define MyAppName "Nothing VPN"
#define MyAppExeName "NothingVpn.Desktop.Wpf.exe"
#define MyAppMutex "Global\NothingVpn.Desktop.Wpf,Global\NothingVpn.Tray"
#define MyAppPublisher "NothingVpn"
; URL и версию при CI переопределяют: ISCC /DMyAppURL=... /DMyAppVersion=...
#ifndef MyAppURL
  #define MyAppURL "https://github.com/"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "0.5.9"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\artifacts\publish\win-x64"
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\..\artifacts\installer"
#endif
[Setup]
AppId={{A2D610D2-2A69-4D7A-9B06-6C0B4E5F5C87}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\NothingVpn
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename=NothingVpnSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
AppMutex={#MyAppMutex}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"; Flags: unchecked
Name: "autorun"; Description: "Запускать при входе в систему (текущий пользователь)"; GroupDescription: "Автозапуск:"; Flags: unchecked

[Files]
; Publish output folder (single-file self-contained exe + optional pdb)
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\sing-box.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\wintun.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\*.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[InstallDelete]
Type: files; Name: "{app}\NothingVpn.Tray.exe"
Type: files; Name: "{app}\NothingVpn.Tray.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
; В режиме PrivilegesRequired=lowest создаём ярлык только в профиле текущего пользователя,
; иначе запись в C:\Users\Public\Desktop может падать с 0x80070005 (Access denied).
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Per-user autorun (optional)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NothingVpn"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autorun

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall

[Code]
var
  UninstallRemoveUserData: Boolean;

function InitializeUninstall(): Boolean;
var
  Res: Integer;
  Msg: String;
begin
  { Тихая деинсталляция: без диалога; по умолчанию удаляем каталог данных вне каталога программы. }
  UninstallRemoveUserData := True;
  Result := True;
  if UninstallSilent then
    Exit;

  Msg :=
    'Сохранить пользовательские данные (профили, настройки)?' + #13#10 + #13#10 +
    ExpandConstant('{localappdata}\NothingVpn.Tray') + #13#10 + #13#10 +
    'Да — оставить папку.' + #13#10 +
    'Нет — удалить вместе с приложением.' + #13#10 + #13#10 +
    'Отмена — прервать удаление.';

  Res := MsgBox(Msg, mbConfirmation, MB_YESNOCANCEL or MB_DEFBUTTON2);
  if Res = IDCANCEL then
    Result := False
  else
    UninstallRemoveUserData := (Res = IDNO);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and UninstallRemoveUserData then
    DelTree(ExpandConstant('{localappdata}\NothingVpn.Tray'), True, True, True);
end;

