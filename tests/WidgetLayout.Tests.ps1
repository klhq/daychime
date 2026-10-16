[CmdletBinding()]
param(
    [string]$NodePath = 'node',
    [string]$PlaywrightModule,
    [ValidateRange(0,2)][int]$GalleryLanguage = 0
)
$ErrorActionPreference = 'Stop'
$layoutRepo = Split-Path -Parent $PSScriptRoot
$layoutDependencies = Join-Path $layoutRepo 'artifacts\widget-layout-check'
New-Item -ItemType Directory -Path $layoutDependencies -Force | Out-Null

function Get-LayoutDependency([string]$Name, [string]$Version, [string]$Folder) {
    $layoutTarget = Join-Path $layoutDependencies $Folder
    $layoutManifest = Join-Path $layoutTarget 'package\package.json'
    if ((Test-Path -LiteralPath $layoutManifest) -and
        (Get-Content -LiteralPath $layoutManifest -Raw | ConvertFrom-Json).version -eq $Version) { return }
    $layoutMetadata = Invoke-RestMethod -Uri "https://registry.npmjs.org/$Name/$Version"
    $layoutArchive = Join-Path $layoutDependencies "$Name-$Version.tgz"
    Invoke-WebRequest -Uri $layoutMetadata.dist.tarball -OutFile $layoutArchive
    $layoutHash = [System.Security.Cryptography.SHA512]::Create()
    try { $layoutIntegrity = 'sha512-' + [Convert]::ToBase64String($layoutHash.ComputeHash([IO.File]::ReadAllBytes($layoutArchive))) }
    finally { $layoutHash.Dispose() }
    if ($layoutIntegrity -ne $layoutMetadata.dist.integrity) { throw "Integrity check failed for $Name $Version" }
    New-Item -ItemType Directory -Path $layoutTarget -Force | Out-Null
    tar -xf $layoutArchive -C $layoutTarget
    if ($LASTEXITCODE -ne 0) { throw "Could not extract $Name $Version" }
}

Get-LayoutDependency 'adaptivecards' '3.0.6' 'adaptivecards'
Get-LayoutDependency 'adaptivecards-templating' '2.3.1' 'adaptivecards-templating'
Get-LayoutDependency 'adaptivecards-designer' '2.4.4' 'adaptivecards-designer'
# The templating bundle expects this release's browser AEL interface.
Get-LayoutDependency 'adaptive-expressions' '4.11.0' 'adaptive-expressions-4.11'

$layoutPreviousDeps = $env:DAYCHIME_LAYOUT_DEPS
$layoutPreviousPlaywright = $env:DAYCHIME_PLAYWRIGHT_MODULE
try {
    $env:DAYCHIME_LAYOUT_DEPS = $layoutDependencies
    if ($PlaywrightModule) { $env:DAYCHIME_PLAYWRIGHT_MODULE = $PlaywrightModule }
    & $NodePath (Join-Path $PSScriptRoot 'WidgetLayout.Tests.cjs') $GalleryLanguage
    if ($LASTEXITCODE -ne 0) { throw 'Widget layout checks failed.' }
}
finally {
    $env:DAYCHIME_LAYOUT_DEPS = $layoutPreviousDeps
    $env:DAYCHIME_PLAYWRIGHT_MODULE = $layoutPreviousPlaywright
}
