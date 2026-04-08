# Публикация NothingVpn.Tray (win-x64, Release) в каталог publish.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet publish 'NothingVpn.Tray\NothingVpn.Tray.csproj' -c Release -r win-x64
exit $LASTEXITCODE
