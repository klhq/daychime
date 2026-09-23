[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x86', 'x64', 'ARM64')]
    [string]$Architecture = 'x64',

    [switch]$StoreUpload,
    [switch]$Clean,
    [switch]$CleanOnly,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\DaychimeWidget\DaychimeWidget.csproj'

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

Push-Location $repoRoot
try {
    if ($Clean) {
        foreach ($path in @(
                (Join-Path $repoRoot 'src\DaychimeWidget\bin'),
                (Join-Path $repoRoot 'src\DaychimeWidget\obj'),
                (Join-Path $repoRoot 'src\DaychimeWidget\AppPackages'),
                (Join-Path $repoRoot 'src\DaychimeWidget\BundleArtifacts'),
                (Join-Path $repoRoot 'artifacts\DaymarkWidget.msixupload')
            )) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
        }

        if ($CleanOnly) {
            Write-Host 'Clean succeeded.'
            return
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

    if ($StoreUpload) {
        $arguments += @(
            '/p:AppxBundle=Always',
            '/p:UapAppxPackageBuildMode=StoreUpload',
            '/p:GenerateAppxPackageOnBuild=true',
            '/p:AppxPackageSigningEnabled=false',
            '/p:AppxSymbolPackageEnabled=false'
        )
    }

    & $msbuild @arguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

    if (-not $StoreUpload) {
        Write-Host "Build succeeded ($Configuration / $Architecture)."
        return
    }

    $msixUpload = Get-ChildItem -Path (Join-Path $repoRoot 'src\DaychimeWidget\AppPackages') -Filter '*.msixupload' -Recurse |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $msixUpload) { throw 'Store packaging succeeded but no .msixupload file was found.' }

    if ($OutputDirectory) {
        $outputPath = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
            [System.IO.Path]::GetFullPath($OutputDirectory)
        }
        else {
            [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
        }
        New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
        $storeUploadPath = Join-Path $outputPath 'Daychime.msixupload'
        Copy-Item -LiteralPath $msixUpload.FullName -Destination $storeUploadPath -Force
        Write-Host "Store upload: $storeUploadPath"
    }
    else {
        Write-Host "Store upload: $($msixUpload.FullName)"
    }
}
finally {
    Pop-Location
}
