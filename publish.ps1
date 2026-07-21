# Публикация NothingVpn.Tray (win-x64, Release) в каталог publish.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet clean 'NothingVpn.sln' -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish 'NothingVpn.Tray\NothingVpn.Tray.csproj' -c Release -r win-x64 --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$pub = Join-Path $PSScriptRoot 'NothingVpn.Tray\bin\Release\net8.0-windows\win-x64\publish'
$out = Join-Path $pub 'NothingVpn.Tray.exe'
if (-not (Test-Path $out)) {
    Write-Error "Publish output not found: $out"
    exit 1
}

$installed = Join-Path $env:LOCALAPPDATA 'Programs\NothingVpn'
foreach ($name in @('sing-box.exe', 'wintun.dll')) {
    $dest = Join-Path $pub $name
    if (Test-Path $dest) { continue }

    $src = Join-Path $installed $name
    if (Test-Path $src) {
        Copy-Item -LiteralPath $src -Destination $dest -Force
        Write-Host "Copied $name from installed app."
        continue
    }

    Write-Warning "$name missing in publish and not found in $installed (TUN may fail until release assets are present)."
}

exit 0
