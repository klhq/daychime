[CmdletBinding()]
param(
    [string]$Tag,
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\src\DaychimeWidget\Package.appxmanifest')
)

$ErrorActionPreference = 'Stop'
[xml]$manifest = Get-Content -LiteralPath $ManifestPath
$rawVersion = [string]$manifest.Package.Identity.Version
if ($rawVersion -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw 'Store manifest version must have four numeric components and end in .0.'
}
$version = [version]$rawVersion
foreach ($component in @($version.Major, $version.Minor, $version.Build, $version.Revision)) {
    if ($component -gt 65535) { throw 'Package version components cannot exceed 65535.' }
}
if ($version.Major -eq 0) { throw 'Store package major version must be greater than zero.' }
$expectedTag = "v$($version.Major).$($version.Minor).$($version.Build)"
if ($PSBoundParameters.ContainsKey('Tag') -and $Tag -cne $expectedTag) {
    throw "Release tag must match the manifest version exactly: $expectedTag"
}
Write-Host "Validated package version $version (release tag: $expectedTag)."
