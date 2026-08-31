[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$RequireElevated
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $administrator = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
} finally { $identity.Dispose() }
if ($RequireElevated -and !$administrator) { throw 'This test run requires an elevated terminal.' }
$suffix = if ($administrator) { 'elevated' } else { 'normal' }
$oldDesktopTest = $env:NOTHINGVPN_RUN_DESKTOP_TESTS
$oldExpectedElevation = $env:NOTHINGVPN_EXPECT_ELEVATED
try {
    $env:NOTHINGVPN_RUN_DESKTOP_TESTS = '1'
    $env:NOTHINGVPN_EXPECT_ELEVATED = if ($RequireElevated) { '1' } else { '0' }
    & dotnet test (Join-Path $repo 'tests/NothingVpn.Application.Tests/NothingVpn.Application.Tests.csproj') `
        -c $Configuration --no-build --no-restore `
        --filter 'FullyQualifiedName~WindowsUpdateHandoffTests' `
        --logger "trx;LogFileName=UpdateHandoff-$suffix.trx" `
        --logger 'console;verbosity=detailed' `
        --results-directory (Join-Path $repo 'artifacts/test-results')
    $testExitCode = $LASTEXITCODE
} finally {
    $env:NOTHINGVPN_RUN_DESKTOP_TESTS = $oldDesktopTest
    $env:NOTHINGVPN_EXPECT_ELEVATED = $oldExpectedElevation
}
exit $testExitCode
