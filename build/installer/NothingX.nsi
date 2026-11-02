; NothingX — установщик (NSIS 3, Unicode). Тот же сценарий, что build\installer\NothingX.iss (Inno Setup):
; per-machine установка в %ProgramFiles%\NothingX, служба NothingX (LocalSystem, автозапуск, перезапуск при сбое).
; Ставится рядом с Nothing VPN: каталог, служба, канал, мьютекс, каталоги данных и запись удаления — свои.
;
; Сборка (из корня репозитория, после publish в artifacts\publish\win-x64):
;   makensis -DVERSION=1.0.0 -DPUBLISH_DIR=artifacts/publish/win-x64 -DOUT_DIR=artifacts/installer build/installer/NothingX.nsi

Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma
RequestExecutionLevel admin

!ifndef VERSION
  !error "VERSION не задан: makensis -DVERSION=x.y.z"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\..\artifacts\publish\win-x64"
!endif
!ifndef OUT_DIR
  !define OUT_DIR "..\..\artifacts\installer"
!endif

!define APP_NAME "NothingX"
!define APP_EXE "NothingX.exe"
!define SERVICE_EXE "NothingX.Service.exe"
!define SERVICE_NAME "NothingX"
!define APP_MUTEX "Global\NothingX.Desktop"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\NothingX"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define RUN_VALUE "NothingX"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"

Name "${APP_NAME}"
OutFile "${OUT_DIR}\NothingXSetup.exe"
InstallDir "$PROGRAMFILES64\NothingX"
BrandingText "${APP_NAME} ${VERSION}"
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1049 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=1049 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1049 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1049 "FileDescription" "Установка ${APP_NAME}"
VIAddVersionKey /LANG=1049 "LegalCopyright" "NothingX (форк EXTRBXY/vpn)"

!define MUI_ICON "..\..\src\NothingVpn.Desktop.Wpf\Assets\app.ico"
!define MUI_UNICON "..\..\src\NothingVpn.Desktop.Wpf\Assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_COMPONENTSPAGE_NODESC
!define MUI_WELCOMEPAGE_TEXT "Мастер установит ${APP_NAME} ${VERSION} в $PROGRAMFILES64\NothingX.$\r$\n$\r$\nNothingX ставится отдельно от Nothing VPN: у него своя папка, своя служба и свои настройки, обе программы могут стоять на одном компьютере.$\r$\n$\r$\nНажмите «Далее», чтобы продолжить."
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Запустить ${APP_NAME}"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchAsUser

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Russian"

Var RemoveUserData

; ---------- Служба ----------

; $R0 — ожидаемое состояние ("STOPPED"/"RUNNING"), $R1 — сколько ждать (×250 мс). Возврат в $R2: 1 — дождались.
!macro WAIT_SERVICE_STATE_BODY
  StrCpy $R2 0
  ${For} $R3 1 $R1
    nsExec::ExecToStack '"$SYSDIR\sc.exe" query ${SERVICE_NAME}'
    Pop $R4
    Pop $R5
    ${If} $R4 == 1060
      StrCpy $R2 1
      ${Break}
    ${EndIf}
    Push $R5
    Push $R0
    Call ${UN}StrContains
    Pop $R6
    ${If} $R6 == 1
      StrCpy $R2 1
      ${Break}
    ${EndIf}
    Sleep 250
  ${Next}
!macroend

!macro STOP_SERVICE_BODY
  nsExec::ExecToStack '"$SYSDIR\sc.exe" query ${SERVICE_NAME}'
  Pop $0
  Pop $1
  ${If} $0 != 1060
    DetailPrint "Остановка службы ${SERVICE_NAME}..."
    nsExec::Exec '"$SYSDIR\sc.exe" stop ${SERVICE_NAME}'
    Pop $0
    StrCpy $R0 "STOPPED"
    StrCpy $R1 120
    Call ${UN}WaitServiceState
    ${If} $R2 != 1
      DetailPrint "Служба не остановилась за 30 с."
    ${EndIf}
    ; Процесс службы освобождает exe чуть позже смены состояния.
    Sleep 1500
  ${EndIf}
!macroend

!macro DEFINE_SERVICE_FUNCS UN
  !define /redef UN "${UN}"
  ; Push haystack, Push needle → Pop 1|0. Регистры $1-$6 сохраняются.
  Function ${UN}StrContains
    Exch $1 ; needle
    Exch
    Exch $2 ; haystack
    Push $3
    Push $4
    Push $5
    Push $6
    StrLen $3 $1
    StrCpy $5 0
    StrCpy $4 0
    ${Do}
      StrCpy $6 $2 $3 $4
      ${If} $6 == ""
        ${Break}
      ${EndIf}
      ${If} $6 == $1
        StrCpy $5 1
        ${Break}
      ${EndIf}
      IntOp $4 $4 + 1
    ${Loop}
    StrCpy $1 $5
    Pop $6
    Pop $5
    Pop $4
    Pop $3
    Pop $2
    Exch $1
  FunctionEnd

  Function ${UN}WaitServiceState
    !insertmacro WAIT_SERVICE_STATE_BODY
  FunctionEnd

  Function ${UN}StopService
    !insertmacro STOP_SERVICE_BODY
  FunctionEnd
!macroend

!insertmacro DEFINE_SERVICE_FUNCS ""
!insertmacro DEFINE_SERVICE_FUNCS "un."

Function RegisterAndStartService
  nsExec::ExecToStack '"$SYSDIR\sc.exe" query ${SERVICE_NAME}'
  Pop $0
  Pop $1
  ; Путь в кавычках: без них путь с пробелами — классическая подмена C:\Program.exe.
  ${If} $0 == 1060
    nsExec::ExecToLog '"$SYSDIR\sc.exe" create ${SERVICE_NAME} binPath= "\"$INSTDIR\${SERVICE_EXE}\"" start= auto obj= LocalSystem DisplayName= "${APP_NAME}"'
  ${Else}
    nsExec::ExecToLog '"$SYSDIR\sc.exe" config ${SERVICE_NAME} binPath= "\"$INSTDIR\${SERVICE_EXE}\"" start= auto obj= LocalSystem DisplayName= "${APP_NAME}"'
  ${EndIf}
  Pop $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP|MB_OK "Не удалось зарегистрировать службу ${APP_NAME} (код $0). Подключение будет недоступно. Попробуйте переустановить приложение." /SD IDOK
    Return
  ${EndIf}

  nsExec::ExecToLog '"$SYSDIR\sc.exe" description ${SERVICE_NAME} "Ядро NothingX: запускает sing-box и Xray (XHTTP) по командам интерфейса."'
  Pop $0
  ; Падение — перезапуск через 5 с, 10 с, затем раз в минуту; счётчик сбрасывается через сутки.
  nsExec::ExecToLog '"$SYSDIR\sc.exe" failure ${SERVICE_NAME} reset= 86400 actions= restart/5000/restart/10000/restart/60000'
  Pop $0
  nsExec::ExecToLog '"$SYSDIR\sc.exe" failureflag ${SERVICE_NAME} 1'
  Pop $0

  DetailPrint "Запуск службы ${SERVICE_NAME}..."
  nsExec::ExecToLog '"$SYSDIR\sc.exe" start ${SERVICE_NAME}'
  Pop $0
  StrCpy $R0 "RUNNING"
  StrCpy $R1 120
  Call WaitServiceState
  ${If} $R2 != 1
    MessageBox MB_ICONEXCLAMATION|MB_OK "Служба ${APP_NAME} зарегистрирована, но не запустилась. Подробности — в журнале событий Windows (Приложение, источник ${SERVICE_NAME}). Попробуйте перезагрузить компьютер." /SD IDOK
  ${EndIf}
FunctionEnd

; Интерфейс — от имени пользователя без прав администратора: explorer.exe запускает файл со своим (обычным) токеном.
Function LaunchAsUser
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${APP_EXE}"'
FunctionEnd

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP|MB_OK "${APP_NAME} работает только в 64-битной Windows." /SD IDOK
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext all

  ; Аналог AppMutex у Inno: запущенный интерфейс держит свои файлы.
  ${Do}
    System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "${APP_MUTEX}") p .r0'
    ${If} $0 == 0
      ${Break}
    ${EndIf}
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_ICONEXCLAMATION|MB_RETRYCANCEL "${APP_NAME} сейчас запущен. Закройте его (значок в трее → «Выход») и нажмите «Повтор»." /SD IDCANCEL IDRETRY +2
    Abort
  ${Loop}
FunctionEnd

Section "${APP_NAME} (обязательно)" SecMain
  SectionIn RO
  SetRegView 64
  SetShellVarContext all

  Call StopService

  SetOutPath "$INSTDIR"
  SetOverwrite on
  File "${PUBLISH_DIR}\${APP_EXE}"
  File "${PUBLISH_DIR}\${SERVICE_EXE}"
  File "${PUBLISH_DIR}\sing-box.exe"
  File "${PUBLISH_DIR}\nothingx-xray.exe"
  File /nonfatal "${PUBLISH_DIR}\wintun.dll"
  File /nonfatal "${PUBLISH_DIR}\*.pdb"
  SetOutPath "$INSTDIR\licenses"
  File /nonfatal "${PUBLISH_DIR}\licenses\*.txt"
  SetOutPath "$INSTDIR"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Удалить ${APP_NAME}.lnk" "$INSTDIR\uninstall.exe"

  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "NothingX"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKLM "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "EstimatedSize" "$0"

  Call RegisterAndStartService
SectionEnd

Section /o "Ярлык на рабочем столе" SecDesktop
  SetShellVarContext all
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
SectionEnd

Section /o "Запускать при входе в Windows" SecAutorun
  ; HKCU пользователя, который подтвердил установку.
  WriteRegStr HKCU "${RUN_KEY}" "${RUN_VALUE}" '"$INSTDIR\${APP_EXE}"'
SectionEnd

; ---------- Удаление ----------

Function un.onInit
  SetRegView 64
  StrCpy $RemoveUserData 1
  IfSilent done
  MessageBox MB_ICONQUESTION|MB_YESNOCANCEL|MB_DEFBUTTON2 "Сохранить пользовательские данные (профили, настройки)?$\r$\n$\r$\n$LOCALAPPDATA\NothingX$\r$\n$\r$\nДа — оставить папку.$\r$\nНет — удалить вместе с приложением.$\r$\nОтмена — прервать удаление." IDYES keep IDNO done
  Abort
  keep:
    StrCpy $RemoveUserData 0
  done:
FunctionEnd

Section "Uninstall"
  SetRegView 64

  ; Каталог данных — текущего пользователя (до переключения на all).
  SetShellVarContext current
  StrCpy $9 "$LOCALAPPDATA\NothingX"

  ReadRegStr $0 HKCU "${RUN_KEY}" "${RUN_VALUE}"
  ${If} $0 == '"$INSTDIR\${APP_EXE}"'
    DeleteRegValue HKCU "${RUN_KEY}" "${RUN_VALUE}"
  ${EndIf}

  SetShellVarContext all
  Call un.StopService
  nsExec::ExecToLog '"$SYSDIR\sc.exe" delete ${SERVICE_NAME}'
  Pop $0
  DeleteRegKey HKLM "SYSTEM\CurrentControlSet\Services\EventLog\Application\${SERVICE_NAME}"

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\${SERVICE_EXE}"
  Delete "$INSTDIR\sing-box.exe"
  Delete "$INSTDIR\nothingx-xray.exe"
  Delete "$INSTDIR\wintun.dll"
  Delete "$INSTDIR\*.pdb"
  Delete "$INSTDIR\licenses\*.txt"
  RMDir "$INSTDIR\licenses"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Удалить ${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"
  Delete "$DESKTOP\${APP_NAME}.lnk"

  ; Временные конфиги и rule-set службы.
  ; SetShellVarContext all: $APPDATA = %ProgramData%.
  RMDir /r "$APPDATA\NothingX"
  ${If} $RemoveUserData == 1
    RMDir /r "$9"
  ${EndIf}

  DeleteRegKey HKLM "${UNINSTALL_KEY}"
SectionEnd
