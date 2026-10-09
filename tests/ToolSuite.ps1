<#
.SYNOPSIS
  End-to-end test of WinCompanion's tools, driven through the real app window.

.DESCRIPTION
  Starts a scripted fake model server (fake_llm.py) and points the app at it, then "asks" the app to run each tool.
  It clicks the approval card (Allow or Deny) where one is expected and checks the real effect: files created or
  moved, windows snapped or closed, the clipboard rewritten, and so on. No cloud model and no API key are involved.

  Your settings, chat and reminders are backed up first and put back afterwards. Run it while you are not using the PC:
  it opens and moves a few windows. Build the app first (dotnet build).
#>
param(
    [string]$Scratch = "$env:TEMP\wc-toolsuite",
    [int]$Port = 18089
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

$repo = Split-Path $PSScriptRoot -Parent
$env:WINCOMPANION_SKIP_SETUP = '1'
$exe = Join-Path $repo 'bin\Debug\net8.0-windows\WinCompanion.exe'
$data = "$env:APPDATA\WinCompanion"
$backup = "$env:TEMP\wc-suite-backup"
$notes = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'WinCompanion\notes.md'
$root = [System.Windows.Automation.AutomationElement]::RootElement
$results = [System.Collections.Generic.List[object]]::new()
$A = [System.Windows.Automation.AutomationElement]

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $results.Add([pscustomobject]@{ Test = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("{0,-6} {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}

function ByProcess([int]$processId) {
    $root.FindAll('Children', (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $processId)))
}
function Find($scope, [string]$property, [string]$value) {
    $scope.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($A::$property, $value)))
}

# ---- set up -------------------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force $Scratch, $backup | Out-Null
# Start each run from an empty scratch folder (only ever inside the temp folder), so runs don't depend on each other.
if ($Scratch.StartsWith($env:TEMP, [StringComparison]::OrdinalIgnoreCase)) {
    Get-ChildItem -LiteralPath $Scratch -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
Copy-Item "$data\*" $backup -Force -ErrorAction SilentlyContinue
$existed = (Get-ChildItem $data -File).Name
$notesBefore = if (Test-Path $notes) { Get-Content $notes } else { $null }
$themeKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$themeBefore = (Get-ItemProperty $themeKey).AppsUseLightTheme

Set-Content "$Scratch\to-move.txt" 'move me'
Set-Content "$Scratch\to-delete.txt" 'delete me'
Set-Content "$Scratch\readme-test.txt" 'first line of the readme'

@{ Provider = 'local'; LocalBaseUrl = "http://127.0.0.1:$Port/v1"; LocalModel = 'fake-model'; LocalVisionModel = ''
   PowerShellTrusted = $false; Pet = 'none'; AskBeforeSendingToCloud = $true; ShareActiveApp = $false } |
    ConvertTo-Json | Set-Content "$data\settings.json"

Get-Process WinCompanion -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$server = Start-Process python -ArgumentList "`"$PSScriptRoot\fake_llm.py`"", "`"$Scratch`"", $Port -PassThru -NoNewWindow `
    -RedirectStandardError "$Scratch\server-err.txt" -RedirectStandardOutput "$Scratch\server-out.txt"
$ready = $false
foreach ($i in 1..80) {                                     # up to 40 s: Python can be slow to start on a busy PC
    Start-Sleep -Milliseconds 500
    try { Invoke-WebRequest "http://127.0.0.1:$Port/v1/models" -UseBasicParsing -TimeoutSec 2 | Out-Null; $ready = $true; break } catch { }
}
if (-not $ready) {
    Copy-Item "$backup\*" $data -Force                      # put things back before giving up
    throw "The fake model server did not start. $(Get-Content "$Scratch\server-err.txt" -Raw)"
}

$started = Get-Date
$app = Start-Process $exe -ArgumentList '--show' -PassThru
$main = $null
foreach ($i in 1..40) { Start-Sleep -Milliseconds 500; $main = ByProcess $app.Id | Select-Object -First 1; if ($main) { break } }
if (-not $main) { throw 'The app window never appeared.' }

function Ask([string]$text, [ValidateSet('none', 'allow', 'deny')][string]$card = 'none', [int]$timeout = 60) {
    $read = { try { @(Get-Content "$data\transcript.json" -Raw | ConvertFrom-Json) } catch { @() } }
    $before = (& $read).Count
    # Look the window up again each time: the app resizes as the chat grows, and an old reference can go stale.
    foreach ($try in 1..25) {
        $script:main = $null
        foreach ($candidate in (ByProcess $app.Id)) {          # the app has several windows (chat, character, hidden helpers)
            if (Find $candidate 'AutomationIdProperty' 'Input') { $script:main = $candidate; break }
        }
        if ($script:main) { break }
        Start-Sleep -Milliseconds 300
    }
    if (-not $script:main) { throw "The app window disappeared before asking: $text" }
    (Find $main 'AutomationIdProperty' 'Input').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
    (Find $main 'AutomationIdProperty' 'SendButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $sawCard = $false; $unexpected = $false
    $deadline = (Get-Date).AddSeconds($timeout)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $button = Find $main 'NameProperty' $(if ($card -eq 'deny') { 'Deny' } else { 'Allow' })
        $visible = $button -and -not $button.Current.IsOffscreen
        if ($visible) {
            $sawCard = $true
            if ($card -eq 'none') { $unexpected = $true; $button = Find $main 'NameProperty' 'Deny' }
            $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        }
        $now = & $read
        if ($now.Count -gt $before + 1 -and $now[-1].Role -in 'assistant', 'error') { break }
    }
    $now = & $read
    [pscustomobject]@{
        Reply = if ($now.Count) { [string]$now[-1].Text } else { '' }
        Card = $sawCard; UnexpectedCard = $unexpected
        Lines = @($now | Select-Object -Skip $before | ForEach-Object { $_.Text })
    }
}

# A throwaway window the window tools can safely snap, minimise and close (never touches your own windows).
$testTitle = 'WC Suite Test Window'
function Test-Windows { $root.FindAll('Children', (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $testTitle))) | ForEach-Object { $_ } }
function Start-TestWindow {
    $script = "Add-Type -AssemblyName System.Windows.Forms; `$f = New-Object System.Windows.Forms.Form; `$f.Text = '$testTitle'; " +
              "`$f.Width = 640; `$f.Height = 400; `$f.StartPosition = 'CenterScreen'; [System.Windows.Forms.Application]::Run(`$f)"
    $script:testProcess = Start-Process pwsh -ArgumentList '-NoProfile', '-STA', '-WindowStyle', 'Hidden', '-Command', $script -PassThru -WindowStyle Hidden
    foreach ($i in 1..30) { Start-Sleep -Milliseconds 500; $w = Test-Windows | Select-Object -First 1; if ($w) { return $w } }
}

try {
# ---- tests --------------------------------------------------------------------------------------------------------
Write-Host "`nRunning tool tests through the real app...`n" -ForegroundColor Cyan

$r = Ask 'TEST sysinfo'
Check 'get_system_info' ($r.Reply -match 'CPU load') $r.Reply

$r = Ask 'TEST ps_safe'
Check 'run_powershell: read-only command runs without asking' (-not $r.Card -and $r.Reply -match '\d{4}') $r.Reply

$dir = "$Scratch\made-by-ps"
$r = Ask 'TEST ps_danger' -card deny
Check 'run_powershell: approval card shown, Deny respected' ($r.Card -and -not (Test-Path $dir)) "card=$($r.Card) existsAfterDeny=$(Test-Path $dir)"
$r = Ask 'TEST ps_danger' -card allow
Check 'run_powershell: approval card shown, Allow runs it' ($r.Card -and (Test-Path $dir)) "card=$($r.Card) exists=$(Test-Path $dir)"

$r = Ask 'TEST move' -card allow
Check 'move_or_rename (approved)' ($r.Card -and (Test-Path "$Scratch\moved.txt") -and -not (Test-Path "$Scratch\to-move.txt")) $r.Reply

$r = Ask 'TEST recycle' -card allow
Check 'delete_to_recycle_bin (approved)' ($r.Card -and -not (Test-Path "$Scratch\to-delete.txt")) $r.Reply

$r = Ask 'TEST mkdir'
Check 'create_folder' (-not $r.Card -and (Test-Path "$Scratch\new-folder")) $r.Reply

$r = Ask 'TEST readfile'
Check 'read_text_file (local model: no cloud prompt)' (-not $r.Card -and $r.Reply -match 'first line of the readme') $r.Reply

$r = Ask 'TEST recent'
Check 'list_recent_files' ($r.Reply -match '\(\d{4}-\d{2}-\d{2} \d{2}:\d{2}') $r.Reply

$r = Ask 'TEST listfiles'
Check 'find_files' ($r.Reply -match '\.txt') $r.Reply

$r = Ask 'TEST shot'
$shot = "$Scratch\shot-test.png"
Check 'save_screenshot' ((Test-Path $shot) -and (Get-Item $shot).Length -gt 20000) $(if (Test-Path $shot) { "$((Get-Item $shot).Length) bytes" } else { 'no file' })

$r = Ask 'TEST look'
Check 'look_at_screen (hides the overlay, captures, asks the model)' ($r.Reply -match 'FAKE ANSWER') $r.Reply

Set-Clipboard 'This is a rather long sentence that should be made much shorter by the clipboard tool.'
$r = Ask 'TEST clip'
Check 'transform_clipboard rewrites the clipboard' ((Get-Clipboard) -eq 'SHORT TEXT') "clipboard now: $(Get-Clipboard)"

$r = Ask 'TEST note'
$r2 = Ask 'TEST readnotes'
Check 'add_note + read_notes' ($r2.Reply -match 'fake-llm test note') $r2.Reply

$r = Ask 'TEST reminder'
$r2 = Ask 'TEST remind_list'
$r3 = Ask 'TEST remind_del'
Check 'set_reminder / list_reminders / cancel_reminder' ($r.Reply -match 'Reminder set' -and $r2.Reply -match 'Your reminders' -and $r3.Reply -match 'Cancelled') "$($r.Reply) | $($r2.Reply) | $($r3.Reply)"

$r = Ask 'TEST remember'; $r2 = Ask 'TEST memories'; $r3 = Ask 'TEST forget'
Check 'remember / list_memories / forget' ($r.Reply -match 'Remembered' -and $r2.Reply -match 'teal|test colour' -and $r3.Reply -match 'Forgot') "$($r.Reply) | $($r2.Reply) | $($r3.Reply)"

$r = Ask 'TEST volume'; $r2 = Ask 'TEST volume_up'
Check 'change_volume (down then up)' ($r.Reply -match 'Volume down' -and $r2.Reply -match 'Volume up') "$($r.Reply) | $($r2.Reply)"

$r = Ask 'TEST theme'; $flipped = (Get-ItemProperty $themeKey).AppsUseLightTheme; $r2 = Ask 'TEST theme'
Check 'set_theme toggles dark/light and back' ($flipped -ne $themeBefore -and (Get-ItemProperty $themeKey).AppsUseLightTheme -eq $themeBefore) "before=$themeBefore flipped=$flipped after=$((Get-ItemProperty $themeKey).AppsUseLightTheme)"

$r = Ask 'TEST kill_prot' -card allow
Check 'kill_process refuses a protected process' ($r.Card -and $r.Reply -match 'protected') $r.Reply

$r = Ask 'TEST weather' -timeout 40
Check 'get_weather (needs internet)' ($r.Reply -match 'Chennai|couldn|Couldn') $r.Reply

$r = Ask 'TEST open'
Start-Sleep 3
$calc = Get-Process CalculatorApp, calc, Calculator -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt $started }
Check 'open_target (Calculator)' ($r.Reply -match 'Opened calc' -and $calc) $r.Reply
$calc | Stop-Process -Force -ErrorAction SilentlyContinue

# Window tools need a window to act on.
$tw = Start-TestWindow
$r = Ask 'TEST windows'
Check 'list_windows sees the test window' ($r.Reply -ne '' -and $tw) $r.Reply

$r = Ask 'TEST snap'
Start-Sleep 1
$screenWidth = $root.Current.BoundingRectangle.Width
$rect = (Test-Windows | Select-Object -First 1).Current.BoundingRectangle
Check 'window_action snap_left puts the window on the left half' ($rect.X -lt 40 -and [math]::Abs($rect.Width - $screenWidth / 2) -lt 60) "x=$([int]$rect.X) width=$([int]$rect.Width) of screen $([int]$screenWidth)"

$r = Ask 'TEST minimize'
foreach ($i in 1..10) {                                        # give Windows a moment to finish minimising
    Start-Sleep -Milliseconds 400
    $state = (Test-Windows | Select-Object -First 1).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Current.WindowVisualState
    if ($state -eq 'Minimized') { break }
}
Check 'window_action minimize' ($state -eq 'Minimized') "state=$state"

$r = Ask 'TEST closewin' -card deny
Check 'close_window: Deny keeps the window open' ($r.Card -and (Test-Windows | Select-Object -First 1)) $r.Reply
$r = Ask 'TEST closewin' -card allow
Start-Sleep 2
Check 'close_window: Allow closes the window' ($r.Card -and -not (Test-Windows | Select-Object -First 1)) $r.Reply

}
catch { Write-Host "TEST RUN ABORTED: $($_.Exception.Message)`n$($_.InvocationInfo.PositionMessage)" -ForegroundColor Red; $results.Add([pscustomobject]@{ Test = "run"; Result = "FAIL"; Detail = $_.Exception.Message }) }
finally {
# ---- tidy up ------------------------------------------------------------------------------------------------------
if ($script:testProcess) { Stop-Process -Id $script:testProcess.Id -Force -ErrorAction SilentlyContinue }
Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
Start-Sleep 1

Copy-Item "$backup\*" $data -Force                       # settings, chat, reminders, key: back exactly as they were
if ($null -ne $notesBefore) { Set-Content $notes $notesBefore }
elseif (Test-Path $notes) { (Get-Content $notes) | Where-Object { $_ -notmatch 'fake-llm test note' } | Set-Content $notes }
$leftover = (Get-ChildItem $data -File).Name | Where-Object { $_ -notin $existed }
if ($leftover) { Write-Host "`nFiles the test created in $data (delete them): $($leftover -join ', ')" -ForegroundColor Yellow }

$failed = @($results | Where-Object Result -eq 'FAIL').Count
Write-Host "`n$($results.Count - $failed) passed, $failed failed" -ForegroundColor $(if ($failed) { 'Red' } else { 'Green' })
$results | Where-Object Result -eq 'FAIL' | Format-Table -AutoSize -Wrap

}
