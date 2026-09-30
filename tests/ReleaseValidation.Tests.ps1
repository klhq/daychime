$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot '..\scripts\Validate-Release.ps1'
[xml]$manifest = Get-Content (Join-Path $PSScriptRoot '..\src\DaychimeWidget\Package.appxmanifest')
$version = [version]$manifest.Package.Identity.Version
$tag = "v$($version.Major).$($version.Minor).$($version.Build)"

& $validator
& $validator -Tag $tag
$count = 2
foreach ($invalid in @('', 'v0.0.0', "$tag.0", "$tag-rc.1", $tag.Substring(1), $tag.ToUpperInvariant())) {
    $rejected = $false
    try { & $validator -Tag $invalid }
    catch {
        if ($_.Exception.Message -notlike '*Release tag must match*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Invalid tag was accepted: '$invalid'" }
    $count++
}
Write-Host "$count release validation scenarios passed."
