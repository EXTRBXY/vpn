# Публикация NothingVpn.Tray (win-x64, Release) в каталог publish.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet clean 'NothingVpn.sln' -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish 'NothingVpn.Tray\NothingVpn.Tray.csproj' -c Release -r win-x64 --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$out = Join-Path $PSScriptRoot 'NothingVpn.Tray\bin\Release\net8.0-windows\win-x64\publish\NothingVpn.Tray.exe'
if (-not (Test-Path $out)) {
    Write-Error "Publish output not found: $out"
    exit 1
}

exit 0
