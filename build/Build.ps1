[CmdletBinding()]
param(
    [ValidateSet('Clean', 'Restore', 'Build', 'Test', 'Publish', 'Installer', 'All')]
    [string]$Target = 'Test',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version,
    [string]$RepositoryUrl = 'https://github.com/',
    [string]$RuntimeAssetsDirectory,
    [string]$InnoCompilerPath
)

$ErrorActionPreference = 'Stop'
$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:Solution = Join-Path $script:RepoRoot 'NothingVpn.sln'
$script:DesktopProject = Join-Path $script:RepoRoot 'src\NothingVpn.Desktop.Wpf\NothingVpn.Desktop.Wpf.csproj'
$script:ServiceProject = Join-Path $script:RepoRoot 'src\NothingVpn.Service\NothingVpn.Service.csproj'
$script:Artifacts = Join-Path $script:RepoRoot 'artifacts'
$script:PublishDirectory = Join-Path $script:Artifacts "publish\$Runtime"
$script:TestResultsDirectory = Join-Path $script:Artifacts 'test-results'
$script:InstallerOutputDirectory = Join-Path $script:Artifacts 'installer'
$script:InstallerScript = Join-Path $PSScriptRoot 'installer\NothingVpn.iss'

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Clean {
    if (Test-Path -LiteralPath $script:Artifacts) {
        $resolvedArtifacts = (Resolve-Path -LiteralPath $script:Artifacts).Path
        $expectedArtifacts = [System.IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'artifacts'))
        if ($resolvedArtifacts -ne $expectedArtifacts) {
            throw "Refusing to clean unexpected path: $resolvedArtifacts"
        }
        Remove-Item -LiteralPath $resolvedArtifacts -Recurse -Force
    }
    New-Item -ItemType Directory -Path $script:Artifacts -Force | Out-Null
}

function Invoke-Restore {
    Invoke-DotNet -Arguments @('restore', $script:Solution)
}

function Invoke-Build {
    Invoke-DotNet -Arguments @('build', $script:Solution, '-c', $Configuration, '--no-restore')
}

function Invoke-Tests {
    New-Item -ItemType Directory -Path $script:TestResultsDirectory -Force | Out-Null
    Invoke-DotNet -Arguments @(
        'test', $script:Solution,
        '-c', $Configuration,
        '--no-build', '--no-restore',
        '--logger', 'trx;LogFileName=NothingVpn.Tests.trx',
        '--results-directory', $script:TestResultsDirectory)
}

function Invoke-WpfSmokeTest {
    $application = Join-Path $script:RepoRoot "src\NothingVpn.Desktop.Wpf\bin\$Configuration\net8.0-windows\$Runtime\NothingVpn.Desktop.Wpf.exe"
    if (-not (Test-Path -LiteralPath $application)) {
        throw "WPF smoke-test executable not found: $application"
    }
    $process = Start-Process -FilePath $application -ArgumentList '--smoke-test' -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "WPF smoke test failed with exit code $($process.ExitCode)."
    }
    Write-Host 'WPF smoke test: passed'
}

function Get-VersionArguments {
    if ([string]::IsNullOrWhiteSpace($Version)) { return @() }
    return @("-p:Version=$Version", "-p:InformationalVersion=$Version")
}

function Copy-RuntimeAssets {
    # Fallback for local builds: an installed client, per-machine (step 7b) or the legacy per-user copy.
    $installedDirectories = @()
    if ($env:ProgramFiles) { $installedDirectories += Join-Path $env:ProgramFiles 'NothingVpn' }
    if ($env:LOCALAPPDATA) { $installedDirectories += Join-Path $env:LOCALAPPDATA 'Programs\NothingVpn' }

    foreach ($name in @('sing-box.exe', 'wintun.dll')) {
        $destination = Join-Path $script:PublishDirectory $name
        $candidates = @()
        if (-not [string]::IsNullOrWhiteSpace($RuntimeAssetsDirectory)) {
            $candidates += Join-Path ([System.IO.Path]::GetFullPath($RuntimeAssetsDirectory)) $name
        }
        foreach ($directory in $installedDirectories) {
            $candidates += Join-Path $directory $name
        }

        $source = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($source) {
            Copy-Item -LiteralPath $source -Destination $destination -Force
            Write-Host "Runtime asset: $name"
        } elseif ($name -eq 'sing-box.exe') {
            Write-Warning "sing-box.exe was not found. Installer creation requires it."
        } else {
            Write-Warning "wintun.dll was not found. TUN mode will be unavailable."
        }
    }
}

function Invoke-Publish {
    if (Test-Path -LiteralPath $script:PublishDirectory) {
        $resolvedPublish = [System.IO.Path]::GetFullPath($script:PublishDirectory)
        $expectedPublishRoot = [System.IO.Path]::GetFullPath((Join-Path $script:Artifacts 'publish'))
        if (-not $resolvedPublish.StartsWith($expectedPublishRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean unexpected publish path: $resolvedPublish"
        }
        Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
    }
    New-Item -ItemType Directory -Path $script:PublishDirectory -Force | Out-Null
    $versionArguments = Get-VersionArguments
    # The client and the Windows service are published into one folder; the installer ships both (step 7b).
    foreach ($project in @($script:DesktopProject, $script:ServiceProject)) {
        $arguments = @(
            'publish', $project,
            '-c', $Configuration,
            '-r', $Runtime,
            '--no-restore',
            '--output', $script:PublishDirectory) + $versionArguments
        Invoke-DotNet -Arguments $arguments
    }
    Copy-RuntimeAssets

    foreach ($name in @('NothingVpn.Desktop.Wpf.exe', 'NothingVpn.Service.exe')) {
        $output = Join-Path $script:PublishDirectory $name
        if (-not (Test-Path -LiteralPath $output)) {
            throw "Publish output not found: $output"
        }
    }
    Write-Host "Publish output: $script:PublishDirectory"
}

function Resolve-InnoCompiler {
    if (-not [string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
        $candidate = [System.IO.Path]::GetFullPath($InnoCompilerPath)
        if (Test-Path -LiteralPath $candidate) { return $candidate }
        throw "Inno Setup compiler not found: $candidate"
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $defaultPath = Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'
    if (Test-Path -LiteralPath $defaultPath) { return $defaultPath }

    throw 'Inno Setup 6 compiler (ISCC.exe) was not found.'
}

function Invoke-Installer {
    $singBox = Join-Path $script:PublishDirectory 'sing-box.exe'
    foreach ($name in @('NothingVpn.Desktop.Wpf.exe', 'NothingVpn.Service.exe')) {
        $output = Join-Path $script:PublishDirectory $name
        if (-not (Test-Path -LiteralPath $output)) {
            throw "Publish the application before creating the installer: $output"
        }
    }
    if (-not (Test-Path -LiteralPath $singBox)) {
        throw "Required runtime asset not found: $singBox"
    }

    New-Item -ItemType Directory -Path $script:InstallerOutputDirectory -Force | Out-Null
    $compiler = Resolve-InnoCompiler
    $effectiveVersion = if ([string]::IsNullOrWhiteSpace($Version)) { '0.6.8' } else { $Version }
    & $compiler `
        "/DMyAppVersion=$effectiveVersion" `
        "/DMyAppURL=$RepositoryUrl" `
        "/DPublishDir=$script:PublishDirectory" `
        "/DInstallerOutputDir=$script:InstallerOutputDirectory" `
        $script:InstallerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }

    $installer = Join-Path $script:InstallerOutputDirectory 'NothingVpnSetup.exe'
    if (-not (Test-Path -LiteralPath $installer)) {
        throw "Installer output not found: $installer"
    }
    $checksumPath = $installer + '.sha256'
    $checksum = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText(
        $checksumPath,
        "$checksum *$(Split-Path -Leaf $installer)`n",
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "Installer output: $installer"
    Write-Host "Installer SHA-256: $checksum"
}

Set-Location $script:RepoRoot
switch ($Target) {
    'Clean' { Invoke-Clean }
    'Restore' { Invoke-Restore }
    'Build' { Invoke-Restore; Invoke-Build }
    'Test' { Invoke-Restore; Invoke-Build; Invoke-Tests; Invoke-WpfSmokeTest }
    'Publish' { Invoke-Restore; Invoke-Publish }
    'Installer' { Invoke-Installer }
    'All' { Invoke-Clean; Invoke-Restore; Invoke-Build; Invoke-Tests; Invoke-WpfSmokeTest; Invoke-Publish; Invoke-Installer }
}
