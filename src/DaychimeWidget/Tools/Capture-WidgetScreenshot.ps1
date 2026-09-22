# Captures the real, live Daychime card as it renders on the Windows 11
# Widgets board, for use as the "Add widgets" gallery preview image.
#
# The manifest (Package.appxmanifest) references one screenshot:
#   ProviderAssets\Daychime_Screenshot_Light.png
# This script replaces hand-simulated mockups with an actual screen capture of
# the pinned widget, so the "Add widgets" preview always matches the real card.
#
# Usage:
#   1. Set Windows to the light theme (Settings > Personalization > Colors).
#   2. Press Win+W to open the Widgets board.
#   3. Make sure the Daychime is pinned and sized/state the way you want
#      it to appear in the gallery (typically: medium size, clocked in).
#   4. Run this script. It finds the Widgets
#      board window, captures it, and asks you to click-drag a crop rectangle
#      around just the widget card.
#   5. Review the saved PNG in ProviderAssets before committing.
#
# This is intentionally a semi-manual tool: the Widgets board is a DWM-composed
# overlay surface that isn't reliably driven by simulated input, so a human
# needs to open/position it. The capture and crop are still exact pixels off
# the real render, not a redrawn approximation.

[CmdletBinding()]
param()

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$widgetsProcess = Get-Process -Name "Widgets" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $widgetsProcess) {
    Write-Error "Widgets.exe isn't running. Press Win+W to open the Widgets board first, then re-run this script."
    exit 1
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
public class WinApi {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

$hwnd = $widgetsProcess.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) {
    Write-Error "Couldn't find the Widgets board window handle. Make sure the board is open (Win+W), not just running in the tray."
    exit 1
}

[WinApi]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 500

$rect = New-Object RECT
[WinApi]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

if ($width -le 0 -or $height -le 0) {
    Write-Error "Widgets board window has no visible size. Is it actually open on screen?"
    exit 1
}

$bmp = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
$g.Dispose()

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fullCapturePath = Join-Path $env:TEMP "daychime-widget-board-light.png"
$bmp.Save($fullCapturePath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Output "Captured the full Widgets board to: $fullCapturePath"
Write-Output "Open it, crop tightly to just the Daychime card, and save the crop as:"
$destination = Join-Path $PSScriptRoot "..\ProviderAssets\Daychime_Screenshot_Light.png"
Write-Output "  $((Resolve-Path (Split-Path $destination)).Path)\Daychime_Screenshot_Light.png"
Write-Output ""
Write-Output "(Snipping Tool / Paint / PowerToys Image Resizer all work fine for the crop step.)"
