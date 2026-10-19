; Inno Setup script for Nothing VPN: per-machine установка в Program Files и служба NothingVpn (ADR-0001, шаг 7б).
; Собирается через build\Build.ps1 -Target Installer поверх build\Build.ps1 -Target Publish: пути подставляет скрипт
; сборки, в каталоге публикации должны быть NothingVpn.Desktop.Wpf.exe, NothingVpn.Service.exe и sing-box.exe.

#define MyAppName "Nothing VPN"
#define MyAppExeName "NothingVpn.Desktop.Wpf.exe"
#define MyServiceExeName "NothingVpn.Service.exe"
; Имя службы задано в коде (NothingVpnWindowsService.Name), канал — NothingVpn.Service.
#define MyServiceName "NothingVpn"
#define MyAppMutex "Global\NothingVpn.Desktop.Wpf,Global\NothingVpn.Tray"
#define MyAppPublisher "NothingVpn"
; URL и версию при CI переопределяют: ISCC /DMyAppURL=... /DMyAppVersion=...
#ifndef MyAppURL
  #define MyAppURL "https://github.com/"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "0.6.8"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\artifacts\publish\win-x64"
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\..\artifacts\installer"
#endif

[Setup]
; AppId прежний: по нему Windows и Inno узнают приложение; старая per-user запись в HKCU с тем же AppId
; убирается миграцией (см. [Code], MigrateUserInstall).
AppId={{A2D610D2-2A69-4D7A-9B06-6C0B4E5F5C87}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; Служба от LocalSystem запускает файлы из {app}, поэтому каталог — только Program Files (запись — администраторам).
; Выбор каталога и прежний каталог (у старых версий — %LOCALAPPDATA%\Programs, доступный пользователю) отключены.
DefaultDirName={autopf}\NothingVpn
DisableDirPage=yes
UsePreviousAppDir=no
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename=NothingVpnSetup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Манифест установщика остаётся asInvoker: неповышенный интерфейс запускает его, и Setup сам запрашивает UAC.
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
AppMutex={#MyAppMutex}
; Журнал установки (%TEMP%\Setup Log *.txt) нужен для ручной проверки и разбора отказов службы.
SetupLogging=yes
; Каталог данных пользователя упоминается только в вопросе при деинсталляции; пользовательские настройки установщик
; не пишет (автозапуск и миграцию делает процесс от имени исходного пользователя).
UsedUserAreasWarning=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"; Flags: unchecked
Name: "autorun"; Description: "Запускать при входе в систему (текущий пользователь)"; GroupDescription: "Автозапуск:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\{#MyServiceExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\sing-box.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\wintun.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\*.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[InstallDelete]
Type: files; Name: "{app}\NothingVpn.Tray.exe"
Type: files; Name: "{app}\NothingVpn.Tray.pdb"

[Icons]
; Ярлыки — для всех пользователей: программа и служба ставятся на машину, а при UAC от другого администратора
; личные папки установщика — это профиль администратора, а не того, кто будет пользоваться VPN.
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Интерфейс — от имени исходного пользователя, без прав администратора. Без skipifsilent: тихое обновление
; из интерфейса снова запускает его.
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall runasoriginaluser

[Code]
const
  ServiceName = '{#MyServiceName}';
  ServiceDisplayName = '{#MyAppName}';
  SC_MANAGER_CONNECT = $0001;
  SERVICE_QUERY_STATUS = $0004;
  SERVICE_START = $0010;
  SERVICE_STOP = $0020;
  SERVICE_DELETE = $00010000;
  SERVICE_CONTROL_STOP = 1;
  SERVICE_STOPPED = 1;
  SERVICE_STOP_PENDING = 3;
  SERVICE_RUNNING = 4;
  SC_STATUS_PROCESS_INFO = 0;
  SYNCHRONIZE = $00100000;
  ERROR_SERVICE_DOES_NOT_EXIST = 1060;
  ERROR_SERVICE_ALREADY_RUNNING = 1056;
  ERROR_SERVICE_MARKED_FOR_DELETE = 1072;
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'NothingVpn';

type
  TServiceStatus = record
    dwServiceType: Cardinal;
    dwCurrentState: Cardinal;
    dwControlsAccepted: Cardinal;
    dwWin32ExitCode: Cardinal;
    dwServiceSpecificExitCode: Cardinal;
    dwCheckPoint: Cardinal;
    dwWaitHint: Cardinal;
  end;
  TServiceStatusProcess = record
    dwServiceType: Cardinal;
    dwCurrentState: Cardinal;
    dwControlsAccepted: Cardinal;
    dwWin32ExitCode: Cardinal;
    dwServiceSpecificExitCode: Cardinal;
    dwCheckPoint: Cardinal;
    dwWaitHint: Cardinal;
    dwProcessId: Cardinal;
    dwServiceFlags: Cardinal;
  end;

function OpenSCManager(lpMachineName, lpDatabaseName: String; dwDesiredAccess: Cardinal): THandle;
  external 'OpenSCManagerW@advapi32.dll stdcall';
function OpenService(hSCManager: THandle; lpServiceName: String; dwDesiredAccess: Cardinal): THandle;
  external 'OpenServiceW@advapi32.dll stdcall';
function CloseServiceHandle(hSCObject: THandle): Boolean;
  external 'CloseServiceHandle@advapi32.dll stdcall';
function ControlService(hService: THandle; dwControl: Cardinal; var lpServiceStatus: TServiceStatus): Boolean;
  external 'ControlService@advapi32.dll stdcall';
function QueryServiceStatusEx(hService: THandle; InfoLevel: Cardinal; var lpBuffer: TServiceStatusProcess;
  cbBufSize: Cardinal; var pcbBytesNeeded: Cardinal): Boolean;
  external 'QueryServiceStatusEx@advapi32.dll stdcall';
function StartService(hService: THandle; dwNumServiceArgs: Cardinal; lpServiceArgVectors: Cardinal): Boolean;
  external 'StartServiceW@advapi32.dll stdcall';
function DeleteService(hService: THandle): Boolean;
  external 'DeleteService@advapi32.dll stdcall';
function OpenProcess(dwDesiredAccess: Cardinal; bInheritHandle: Boolean; dwProcessId: Cardinal): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(hHandle: THandle; dwMilliseconds: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

var
  UninstallRemoveUserData: Boolean;

{ ---------- Служба ---------- }

function OpenNothingVpnService(Access: Cardinal; var Service: THandle): Boolean;
var
  Manager: THandle;
  Error: Cardinal;
begin
  Result := False;
  Service := 0;
  Manager := OpenSCManager('', 'ServicesActive', SC_MANAGER_CONNECT);
  if Manager = 0 then
  begin
    Log(Format('OpenSCManager failed: %d', [DLLGetLastError()]));
    Exit;
  end;
  Service := OpenService(Manager, ServiceName, Access);
  if Service = 0 then
  begin
    Error := DLLGetLastError();
    if Error <> ERROR_SERVICE_DOES_NOT_EXIST then
      Log(Format('OpenService failed: %d', [Error]));
  end
  else
    Result := True;
  CloseServiceHandle(Manager);
end;

function ServiceExists(): Boolean;
var
  Service: THandle;
begin
  Result := OpenNothingVpnService(SERVICE_QUERY_STATUS, Service);
  if Result then
    CloseServiceHandle(Service);
end;

function QueryState(Service: THandle; var Status: TServiceStatusProcess): Boolean;
var
  Needed: Cardinal;
begin
  Result := QueryServiceStatusEx(Service, SC_STATUS_PROCESS_INFO, Status, 36 { SERVICE_STATUS_PROCESS }, Needed);
end;

{ Останавливает службу и ждёт выхода её процесса: иначе exe службы ещё занят, и замена файлов упадёт. }
function StopNothingVpnService(): Boolean;
var
  Service, Process: THandle;
  Status: TServiceStatusProcess;
  Control: TServiceStatus;
  ProcessId: Cardinal;
  Waited: Integer;
begin
  Result := True;
  if not OpenNothingVpnService(SERVICE_STOP or SERVICE_QUERY_STATUS, Service) then
    Exit;
  try
    if not QueryState(Service, Status) then
    begin
      Log(Format('QueryServiceStatusEx failed: %d', [DLLGetLastError()]));
      Result := False;
      Exit;
    end;
    if Status.dwCurrentState = SERVICE_STOPPED then
      Exit;

    ProcessId := Status.dwProcessId;
    Log(Format('Stopping service %s (state %d, pid %d)', [ServiceName, Status.dwCurrentState, ProcessId]));
    if (Status.dwCurrentState <> SERVICE_STOP_PENDING) and not ControlService(Service, SERVICE_CONTROL_STOP, Control) then
      Log(Format('ControlService(STOP) failed: %d', [DLLGetLastError()]));

    Waited := 0;
    while QueryState(Service, Status) and (Status.dwCurrentState <> SERVICE_STOPPED) and (Waited < 30000) do
    begin
      Sleep(250);
      Waited := Waited + 250;
    end;
    Result := QueryState(Service, Status) and (Status.dwCurrentState = SERVICE_STOPPED);
    if not Result then
    begin
      Log('Service did not stop in 30 s');
      Exit;
    end;

    if ProcessId <> 0 then
    begin
      Process := OpenProcess(SYNCHRONIZE, False, ProcessId);
      if Process <> 0 then
      begin
        WaitForSingleObject(Process, 15000);
        CloseHandle(Process);
      end;
    end;
    Log('Service stopped');
  finally
    CloseServiceHandle(Service);
  end;
end;

function StartNothingVpnService(): Boolean;
var
  Service: THandle;
  Error: Cardinal;
  Status: TServiceStatusProcess;
  Waited: Integer;
begin
  Result := False;
  if not OpenNothingVpnService(SERVICE_START or SERVICE_QUERY_STATUS, Service) then
    Exit;
  try
    if not StartService(Service, 0, 0) then
    begin
      Error := DLLGetLastError();
      Log(Format('StartService failed: %d', [Error]));
      if Error <> ERROR_SERVICE_ALREADY_RUNNING then
        Exit;
    end;
    Waited := 0;
    while QueryState(Service, Status) and (Status.dwCurrentState <> SERVICE_RUNNING)
      and (Status.dwCurrentState <> SERVICE_STOPPED) and (Waited < 30000) do
    begin
      Sleep(250);
      Waited := Waited + 250;
    end;
    Result := QueryState(Service, Status) and (Status.dwCurrentState = SERVICE_RUNNING);
    Log(Format('Service state after start: %d', [Status.dwCurrentState]));
  finally
    CloseServiceHandle(Service);
  end;
end;

function DeleteNothingVpnService(): Boolean;
var
  Service: THandle;
begin
  Result := True;
  if not OpenNothingVpnService(SERVICE_DELETE, Service) then
    Exit;
  try
    Result := DeleteService(Service);
    if not Result then
    begin
      Log(Format('DeleteService failed: %d', [DLLGetLastError()]));
      Result := DLLGetLastError() = ERROR_SERVICE_MARKED_FOR_DELETE;
    end;
  finally
    CloseServiceHandle(Service);
  end;
end;

function Sc(const Params: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    and (ResultCode = 0);
  Log(Format('sc.exe %s -> %d', [Params, ResultCode]));
end;

{ Регистрирует службу (или обновляет путь и параметры уже зарегистрированной): LocalSystem, автозапуск,
  путь в кавычках (без кавычек путь с пробелами — классическая подмена C:\Program.exe). }
function RegisterNothingVpnService(): Boolean;
var
  Command, Settings: String;
begin
  Settings := 'binPath= "\"' + ExpandConstant('{app}\{#MyServiceExeName}') + '\"" start= auto obj= LocalSystem'
    + ' DisplayName= "' + ServiceDisplayName + '"';
  if ServiceExists() then
    Command := 'config '
  else
    Command := 'create ';
  Result := Sc(Command + ServiceName + ' ' + Settings);
  if not Result then
    Exit;

  Sc('description ' + ServiceName + ' "Ядро Nothing VPN: запускает sing-box по командам интерфейса."');
  { Падение — перезапуск через 5 с, 10 с, затем раз в минуту; счётчик сбрасывается через сутки. failureflag —
    то же при остановке с кодом ошибки (например, имя канала занято чужим процессом). }
  Sc('failure ' + ServiceName + ' reset= 86400 actions= restart/5000/restart/10000/restart/60000');
  Sc('failureflag ' + ServiceName + ' 1');
end;

{ ---------- Установка ---------- }

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopNothingVpnService() then
    Result := 'Не удалось остановить службу Nothing VPN. Закройте Nothing VPN, перезагрузите компьютер и повторите установку.';
end;

{ Убирает старую per-user установку (≤ 0.6.8) и переносит автозапуск — от имени исходного пользователя, чтобы
  работать с его HKCU и профилем даже при UAC от другого администратора. Старый деинсталлятор не запускается:
  в тихом режиме он удаляет каталог данных %LOCALAPPDATA%\NothingVpn.Tray. }
procedure MigrateUserInstall();
var
  Params: String;
  ResultCode: Integer;
begin
  Params := '--migrate-user-install';
  if WizardIsTaskSelected('autorun') then
    Params := Params + ' --autorun';
  try
    if ExecAsOriginalUser(ExpandConstant('{app}\{#MyServiceExeName}'), Params, ExpandConstant('{app}'),
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Log(Format('User install migration (%s) -> %d', [Params, ResultCode]))
    else
      Log(Format('User install migration did not start: %d', [ResultCode]));
  except
    Log('User install migration failed: ' + GetExceptionMessage());
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    Exit;

  if not RegisterNothingVpnService() then
    SuppressibleMsgBox('Не удалось зарегистрировать службу Nothing VPN. Подключение будет недоступно; ' +
      'подробности — в журнале установки. Попробуйте переустановить приложение.', mbError, MB_OK, IDOK)
  else if not StartNothingVpnService() then
    SuppressibleMsgBox('Служба Nothing VPN зарегистрирована, но не запустилась. Подробности — в журнале событий ' +
      'Windows (Приложение, источник NothingVpn). Попробуйте перезагрузить компьютер.', mbError, MB_OK, IDOK);

  MigrateUserInstall();
end;

{ ---------- Деинсталляция ---------- }

function IsReparsePoint(const Path: String): Boolean;
var
  Find: TFindRec;
begin
  Result := False;
  if FindFirst(Path, Find) then
  begin
    Result := (Find.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0;
    FindClose(Find);
  end;
end;

{ Удаление каталога с правами администратора: ссылку удаляем саму, не заходя внутрь (дополнительно к RedirectionGuard). }
procedure DeleteDirectorySafely(const Path: String);
begin
  if not DirExists(Path) then
    Exit;
  if IsReparsePoint(Path) then
    RemoveDir(Path)
  else
    DelTree(Path, True, True, True);
end;

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

procedure RemoveAutorunOfThisInstall();
var
  Command: String;
begin
  if RegQueryStringValue(HKCU, RunKey, RunValue, Command)
    and (Pos(Lowercase(ExpandConstant('{app}\{#MyAppExeName}')), Lowercase(Command)) > 0) then
    RegDeleteValue(HKCU, RunKey, RunValue);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { До удаления файлов: служба держит свой exe и sing-box. }
    if not StopNothingVpnService() then
      Log('Service did not stop before uninstall');
    if not DeleteNothingVpnService() then
      Log('Service was not deleted');
    RegDeleteKeyIncludingSubkeys(HKLM, 'SYSTEM\CurrentControlSet\Services\EventLog\Application\' + ServiceName);
    { Автозапуск убирается только у пользователя, который удаляет программу: HKCU других пользователей недоступен. }
    RemoveAutorunOfThisInstall();
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { Временные конфиги и rule-set службы. }
    DeleteDirectorySafely(ExpandConstant('{commonappdata}\NothingVpn'));
    if UninstallRemoveUserData then
      DeleteDirectorySafely(ExpandConstant('{localappdata}\NothingVpn.Tray'));
  end;
end;
