# Compatibility wrapper. Prefer: .\build\Build.ps1 -Target Publish
& (Join-Path $PSScriptRoot 'build\Build.ps1') -Target Publish @args
exit $LASTEXITCODE
