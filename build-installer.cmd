@echo off
setlocal
cd /d "%~dp0"
echo Packaging the existing published application with Inno Setup...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\Build.ps1" -Target Installer
if errorlevel 1 goto error
echo.
echo Installer is ready:
echo %~dp0artifacts\installer\NothingVpnSetup.exe
echo.
explorer "%~dp0artifacts\installer"
pause
exit /b 0

:error
echo.
echo Installer packaging failed.
echo Build and test the application first with build-app.cmd.
pause
exit /b 1
