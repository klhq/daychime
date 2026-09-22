[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x86', 'x64', 'ARM64')]
    [string]$Architecture = 'x64',

    [switch]$Package,
    [string]$CertificateThumbprint,
    [switch]$Install,
    [switch]$Clean,
    [string]$OutputDirectory,
    [string]$AppInstallerUri,
    [string]$PackageUri,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\WorkdayWidget\CsConsoleWidgetProvider.csproj'

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $candidate = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
            Select-Object -First 1
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }

    $fallback = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
    if (Test-Path -LiteralPath $fallback) { return $fallback }

    throw 'MSBuild was not found. Install Visual Studio 2022 with the Windows application development workload.'
}

function Find-SignTool {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidate = Get-ChildItem -LiteralPath $kitsRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $candidate) { throw 'SignTool was not found. Install the Windows 10/11 SDK.' }
    return $candidate.FullName
}

Push-Location $repoRoot
try {
    if ($Clean) {
        foreach ($path in @(
                (Join-Path $repoRoot 'src\WorkdayWidget\bin'),
                (Join-Path $repoRoot 'src\WorkdayWidget\obj'),
                (Join-Path $repoRoot 'src\WorkdayWidget\AppPackages')
            )) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
        }
    }

    $msbuild = Find-MSBuild
    $arguments = @(
        $project,
        '/m',
        '/restore',
        "/p:Configuration=$Configuration",
        "/p:Platform=$Architecture"
    )

    if ($Package) {
        $arguments += @(
            '/p:AppxBundle=Never',
            '/p:UapAppxPackageBuildMode=SideloadOnly',
            '/p:GenerateAppxPackageOnBuild=true'
        )
    }

    & $msbuild @arguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

    if (-not $Package) {
        Write-Host "Build succeeded ($Configuration / $Architecture)."
        return
    }

    $msix = Get-ChildItem -Path (Join-Path $repoRoot 'src\WorkdayWidget\AppPackages') -Filter '*.msix' -Recurse |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $msix) { throw 'Packaging succeeded but no .msix file was found.' }

    if (($AppInstallerUri -and -not $PackageUri) -or ($PackageUri -and -not $AppInstallerUri)) {
        throw 'AppInstallerUri and PackageUri must be supplied together.'
    }
    if (($AppInstallerUri -or $PackageUri) -and -not $OutputDirectory) {
        throw 'OutputDirectory is required when creating an App Installer file.'
    }

    if ($CertificateThumbprint) {
        $signTool = Find-SignTool
        $signArguments = @('sign', '/fd', 'SHA256', '/sha1', $CertificateThumbprint, '/s', 'My')
        if ($TimestampServer) {
            $signArguments += @('/tr', $TimestampServer, '/td', 'SHA256')
        }
        $signArguments += $msix.FullName
        & $signTool @signArguments
        if ($LASTEXITCODE -ne 0) { throw "Signing failed with exit code $LASTEXITCODE." }
    }

    if ($Install) {
        if (-not $CertificateThumbprint) {
            Write-Warning 'Installing an unsigned package will normally fail. Pass -CertificateThumbprint to sign it first.'
        }
        Get-Process -Name WorkdayWidget -ErrorAction SilentlyContinue | Stop-Process -Force
        Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
    }

    if ($OutputDirectory) {
        $outputPath = [System.IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
        New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
        $releaseMsix = Join-Path $outputPath 'WorkdayWidget.msix'
        Copy-Item -LiteralPath $msix.FullName -Destination $releaseMsix -Force
        Write-Host "Release MSIX: $releaseMsix"

        if ($AppInstallerUri) {
            [xml]$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'src\WorkdayWidget\Package.appxmanifest')
            $identity = $manifest.Package.Identity
            $appInstaller = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Version="$($identity.Version)" Uri="$AppInstallerUri">
  <MainPackage Name="$($identity.Name)" Publisher="$($identity.Publisher)" Version="$($identity.Version)" ProcessorArchitecture="$Architecture" Uri="$PackageUri" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="24" />
  </UpdateSettings>
</AppInstaller>
"@
            $appInstallerPath = Join-Path $outputPath 'WorkdayWidget.appinstaller'
            Set-Content -LiteralPath $appInstallerPath -Value $appInstaller -Encoding utf8NoBOM
            Write-Host "App Installer: $appInstallerPath"
        }
    }
    else {
        Write-Host "MSIX: $($msix.FullName)"
    }
}
finally {
    Pop-Location
}
