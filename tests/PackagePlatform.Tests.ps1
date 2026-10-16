$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot '..\scripts\Validate-PackagePlatform.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('daychime-platform-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null

function New-ZipBytes([hashtable]$Entries) {
    $buffer = [IO.MemoryStream]::new()
    $zip = [IO.Compression.ZipArchive]::new($buffer, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($name in $Entries.Keys) {
            $stream = $zip.CreateEntry($name).Open()
            try {
                [byte[]]$bytes = $Entries[$name]
                $stream.Write($bytes, 0, $bytes.Length)
            }
            finally { $stream.Dispose() }
        }
    }
    finally { $zip.Dispose() }
    try { return ,$buffer.ToArray() }
    finally { $buffer.Dispose() }
}

function New-AppBytes([string]$Families) {
    $xml = '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Dependencies>' + $Families + '</Dependencies></Package>'
    return ,(New-ZipBytes @{ 'AppxManifest.xml' = [Text.Encoding]::UTF8.GetBytes($xml) })
}

$good = New-AppBytes '<TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22000.0" MaxVersionTested="10.0.26100.0" />'
$bad = New-AppBytes '<TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" />'
$bundle = New-ZipBytes @{ 'app_x64.msix' = $good }
$upload = New-ZipBytes @{ 'app.msixbundle' = $bundle }
$cases = @(
    @{ Name = 'direct.msix'; Bytes = $good; Accept = $true },
    @{ Name = 'direct.appx'; Bytes = $good; Accept = $true },
    @{ Name = 'bundle.msixbundle'; Bytes = $bundle; Accept = $true },
    @{ Name = 'upload.msixupload'; Bytes = $upload; Accept = $true },
    @{ Name = 'windows10.msix'; Bytes = $bad; Accept = $false },
    @{ Name = 'raised-minimum.msix'; Bytes = (New-AppBytes '<TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.26100.0" />'); Accept = $false },
    @{ Name = 'missing-desktop.msix'; Bytes = (New-AppBytes '<TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.22000.0" />'); Accept = $false },
    @{ Name = 'extra-windows10-family.msix'; Bytes = (New-AppBytes '<TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22000.0" /><TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.17763.0" />'); Accept = $false },
    @{ Name = 'mixed.msixupload'; Bytes = (New-ZipBytes @{ 'app.msixbundle' = (New-ZipBytes @{ 'x64.msix' = $good; 'arm64.msix' = $bad }) }); Accept = $false },
    @{ Name = 'empty.msixupload'; Bytes = (New-ZipBytes @{ 'readme.txt' = [Text.Encoding]::UTF8.GetBytes('no package') }); Accept = $false },
    @{ Name = 'missing-version.msix'; Bytes = (New-AppBytes '<TargetDeviceFamily Name="Windows.Desktop" />'); Accept = $false },
    @{ Name = 'invalid.msix'; Bytes = [Text.Encoding]::UTF8.GetBytes('not a ZIP'); Accept = $false }
)

foreach ($case in $cases) {
    $path = Join-Path $fixtureRoot $case.Name
    [IO.File]::WriteAllBytes($path, $case.Bytes)
    $accepted = $true
    try { & $validator -PackagePath $path }
    catch { $accepted = $false }
    if ($accepted -ne $case.Accept) { throw "Unexpected platform validation result: $($case.Name)" }
}
Write-Host "$($cases.Count) package platform validation scenarios passed."
