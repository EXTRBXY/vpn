@echo off
setlocal
cd /d "%~dp0"
echo Building Nothing VPN application...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\Build.ps1" -Target Publish
if errorlevel 1 goto error
echo.
echo Published application is ready:
echo %~dp0artifacts\publish\win-x64\NothingVpn.Tray.exe
echo.
explorer "%~dp0artifacts\publish\win-x64"
pause
exit /b 0

:error
echo.
echo Application build failed.
pause
exit /b 1
