Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName Microsoft.VisualBasic

$createdNew = $false
$singleInstance = [System.Threading.Mutex]::new($true, 'WorkdayWidget.SingleInstance', [ref]$createdNew)
if (-not $createdNew) { exit }

$appDirectory = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Workday Widget'
$stateFile = Join-Path $appDirectory 'today.json'
$logFile = Join-Path $appDirectory 'workday-log.csv'
New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null

$workHours = 8
$breakHours = 1

function Get-TodayState {
    if (-not (Test-Path -LiteralPath $stateFile)) { return $null }
    try {
        $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        if ($state.Date -eq (Get-Date).ToString('yyyy-MM-dd')) { return $state }
    } catch { }
    return $null
}

function Save-State([datetime]$clockIn) {
    [PSCustomObject]@{
        Date = $clockIn.ToString('yyyy-MM-dd')
        ClockIn = $clockIn.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding utf8
}

function Add-Log([datetime]$clockIn) {
    $today = $clockIn.ToString('yyyy-MM-dd')
    $oldRows = @()
    if (Test-Path -LiteralPath $logFile) {
        $oldRows = @(Import-Csv -LiteralPath $logFile | Where-Object { $_.Date -ne $today })
    }
    $newRow = [PSCustomObject]@{
        Date = $today
        ClockIn = $clockIn.ToString('HH:mm')
        ExpectedFinish = $clockIn.AddHours($workHours + $breakHours).ToString('HH:mm')
    }
    @($oldRows) + @($newRow) | Export-Csv -LiteralPath $logFile -NoTypeInformation -Encoding utf8
}

function Remove-TodayLog {
    if (-not (Test-Path -LiteralPath $logFile)) { return }
    $today = (Get-Date).ToString('yyyy-MM-dd')
    $rows = @(Import-Csv -LiteralPath $logFile | Where-Object { $_.Date -ne $today })
    if ($rows.Count -eq 0) {
        Remove-Item -LiteralPath $logFile -Force
    } else {
        $rows | Export-Csv -LiteralPath $logFile -NoTypeInformation -Encoding utf8
    }
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Workday Widget'
$form.ClientSize = New-Object System.Drawing.Size(390, 255)
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedToolWindow
$form.MaximizeBox = $false
$form.MinimizeBox = $true
$form.TopMost = $true
$form.BackColor = [System.Drawing.Color]::FromArgb(24, 27, 33)
$form.ForeColor = [System.Drawing.Color]::White
$form.StartPosition = 'Manual'
$workArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$form.Location = New-Object System.Drawing.Point(($workArea.Right - $form.Width - 24), 36)

function New-Label([string]$text, [int]$x, [int]$y, [int]$width, [int]$height, [float]$size, [System.Drawing.FontStyle]$style) {
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $text
    $label.Location = New-Object System.Drawing.Point($x, $y)
    $label.Size = New-Object System.Drawing.Size($width, $height)
    $label.Font = New-Object System.Drawing.Font('Segoe UI', $size, $style)
    $label.ForeColor = [System.Drawing.Color]::White
    $label.BackColor = [System.Drawing.Color]::Transparent
    $form.Controls.Add($label)
    return $label
}

$title = New-Label 'WORKDAY' 20 16 180 22 11 ([System.Drawing.FontStyle]::Bold)
$subtitle = New-Label 'TODAY  /  8H WORK + 1H BREAK' 20 40 300 18 8 ([System.Drawing.FontStyle]::Regular)
$subtitle.ForeColor = [System.Drawing.Color]::FromArgb(177, 190, 205)
$timeCaption = New-Label 'TIME LEFT UNTIL FINISH' 20 68 300 17 8 ([System.Drawing.FontStyle]::Bold)
$timeCaption.ForeColor = [System.Drawing.Color]::FromArgb(145, 158, 176)
$remainingLabel = New-Label '' 20 83 350 41 26 ([System.Drawing.FontStyle]::Bold)
$remainingLabel.ForeColor = [System.Drawing.Color]::FromArgb(109, 220, 160)
$divider = New-Object System.Windows.Forms.Label
$divider.Location = New-Object System.Drawing.Point(20, 132)
$divider.Size = New-Object System.Drawing.Size(350, 1)
$divider.BackColor = [System.Drawing.Color]::FromArgb(62, 68, 79)
$form.Controls.Add($divider)
$clockInLabel = New-Label '' 20 145 170 22 10 ([System.Drawing.FontStyle]::Regular)
$clockInLabel.ForeColor = [System.Drawing.Color]::FromArgb(210, 216, 225)
$finishLabel = New-Label '' 204 145 166 22 10 ([System.Drawing.FontStyle]::Regular)
$finishLabel.ForeColor = [System.Drawing.Color]::FromArgb(210, 216, 225)

$clockButton = New-Object System.Windows.Forms.Button
$clockButton.Location = New-Object System.Drawing.Point(20, 198)
$clockButton.Size = New-Object System.Drawing.Size(170, 34)
$clockButton.FlatStyle = 'Flat'
$clockButton.FlatAppearance.BorderSize = 0
$clockButton.BackColor = [System.Drawing.Color]::FromArgb(65, 132, 235)
$clockButton.ForeColor = [System.Drawing.Color]::White
$clockButton.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($clockButton)

$historyButton = New-Object System.Windows.Forms.Button
$historyButton.Text = 'Open History'
$historyButton.Location = New-Object System.Drawing.Point(200, 198)
$historyButton.Size = New-Object System.Drawing.Size(170, 34)
$historyButton.FlatStyle = 'Flat'
$historyButton.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(95, 105, 120)
$historyButton.BackColor = [System.Drawing.Color]::FromArgb(46, 50, 58)
$historyButton.ForeColor = [System.Drawing.Color]::White
$historyButton.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Regular)
$form.Controls.Add($historyButton)

function Refresh-Widget {
    $state = Get-TodayState
    if ($null -eq $state) {
        $clockInLabel.Text = 'CLOCK IN  /  --:--'
        $finishLabel.Text = 'FINISH  /  --:--'
        $remainingLabel.Text = 'READY TO START'
        $remainingLabel.ForeColor = [System.Drawing.Color]::FromArgb(177, 190, 205)
        $clockButton.Text = 'Clock In Now'
        return
    }
    $clockIn = [datetime]::Parse($state.ClockIn)
    $finish = $clockIn.AddHours($workHours + $breakHours)
    $remaining = $finish - (Get-Date)
    $clockInLabel.Text = 'CLOCK IN  /  ' + $clockIn.ToString('HH:mm')
    $finishLabel.Text = 'FINISH  /  ' + $finish.ToString('HH:mm')
    $clockButton.Text = 'Edit Clock-in'
    if ($remaining.TotalSeconds -gt 0) {
        $remainingLabel.Text = ('{0:hh\\:mm\\:ss}' -f $remaining)
        $remainingLabel.ForeColor = [System.Drawing.Color]::FromArgb(109, 220, 160)
    } else {
        $remainingLabel.Text = 'WORKDAY COMPLETE'
        $remainingLabel.ForeColor = [System.Drawing.Color]::FromArgb(109, 220, 160)
    }
}

$clockButton.Add_Click({
    $state = Get-TodayState
    if ($null -eq $state) {
        $now = Get-Date
        Save-State $now
        Add-Log $now
    } else {
        $currentClockIn = [datetime]::Parse($state.ClockIn)
        $value = [Microsoft.VisualBasic.Interaction]::InputBox(
            'Enter today''s clock-in time (24-hour HH:mm):',
            'Edit clock-in',
            $currentClockIn.ToString('HH:mm')
        )
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            try {
                $time = [datetime]::ParseExact($value.Trim(), 'HH:mm', [System.Globalization.CultureInfo]::InvariantCulture)
                $updated = (Get-Date).Date.Add($time.TimeOfDay)
                Save-State $updated
                Add-Log $updated
            } catch {
                [System.Windows.Forms.MessageBox]::Show(
                    'Please use a 24-hour time, for example 09:15.',
                    'Invalid time',
                    [System.Windows.Forms.MessageBoxButtons]::OK,
                    [System.Windows.Forms.MessageBoxIcon]::Warning
                ) | Out-Null
            }
        }
    }
    Refresh-Widget
})

$historyButton.Add_Click({
    if (-not (Test-Path -LiteralPath $logFile)) {
        [System.Windows.Forms.MessageBox]::Show('No history yet.', 'Workday Widget') | Out-Null
        return
    }
    Start-Process -FilePath $logFile
})

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1000
$timer.Add_Tick({ Refresh-Widget })
$form.Add_Shown({ Refresh-Widget; $timer.Start() })
$form.Add_FormClosed({ $timer.Stop() })
[void]$form.ShowDialog()
$singleInstance.ReleaseMutex()
$singleInstance.Dispose()
