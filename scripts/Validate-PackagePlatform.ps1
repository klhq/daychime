[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$minimum = '10.0.22000.0'

function Test-PackageArchive([System.IO.Stream]$Stream, [string]$Name, [int]$Depth = 0) {
    if ($Depth -gt 4) { throw "Package nesting is too deep: $Name" }
    $archive = [System.IO.Compression.ZipArchive]::new($Stream, [System.IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        $count = 0
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -ieq 'AppxManifest.xml') {
                $reader = [System.IO.StreamReader]::new($entry.Open())
                try { [xml]$manifest = $reader.ReadToEnd() }
                finally { $reader.Dispose() }
                $families = @($manifest.Package.Dependencies.TargetDeviceFamily)
                $desktop = @($families | Where-Object Name -CEQ 'Windows.Desktop')
                if ($desktop.Count -ne 1 -or [string]$desktop[0].MinVersion -cne $minimum) {
                    throw "Package must require Windows.Desktop MinVersion=$minimum : $Name"
                }
                foreach ($family in $families) {
                    if ([version]$family.MinVersion -lt [version]$minimum) {
                        throw "Package permits an OS older than Windows 11: $Name"
                    }
                }
                $count++
            }
            elseif ($entry.FullName -match '\.(msix|appx|msixbundle|appxbundle)$') {
                $nested = $entry.Open()
                try { $count += Test-PackageArchive $nested "$Name/$($entry.FullName)" ($Depth + 1) }
                finally { $nested.Dispose() }
            }
        }
        return $count
    }
    finally { $archive.Dispose() }
}

$stream = [System.IO.File]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try { $count = Test-PackageArchive $stream $PackagePath }
finally { $stream.Dispose() }
if ($count -eq 0) { throw "No application package manifest found in $PackagePath" }
Write-Host "Validated $count packaged manifest(s): Windows 11 minimum $minimum."
