<#
.SYNOPSIS
  A small end-to-end check of the Gemini path, using the key already saved in the app (about five requests in total).

.DESCRIPTION
  1. A plain question (no tools).
  2. A question about the PC, which makes Gemini use a tool.
  3. A request that needs a screenshot: the app must ask before sending it, and this script answers "Deny", so nothing is sent.
  Your settings, chat and reminders are backed up and restored. Needs a Gemini key saved in Settings and a working internet connection.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'bin\Debug\net8.0-windows\WinCompanion.exe'
$data = "$env:APPDATA\WinCompanion"
$backup = "$env:TEMP\wc-gemini-backup"
$A = [System.Windows.Automation.AutomationElement]
$root = $A::RootElement

New-Item -ItemType Directory -Force $backup | Out-Null
Copy-Item "$data\*" $backup -Force
$existed = (Get-ChildItem $data -File).Name
$settings = Get-Content "$data\settings.json" -Raw | ConvertFrom-Json
if ($settings.Provider -ne 'gemini') { throw "Settings are not set to Gemini (they are '$($settings.Provider)')." }
if (-not (Test-Path "$data\apikey.bin")) { throw 'No Gemini key is saved in the app.' }

function By($scope, $property, [string]$value) { $scope.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($A::$property, $value))) }
function Main { foreach ($w in ($root.FindAll('Children', (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $script:app.Id))))) { if (By $w 'AutomationIdProperty' 'Input') { return $w } } }
$read = { try { @(Get-Content "$data\transcript.json" -Raw | ConvertFrom-Json) } catch { @() } }

function Ask([string]$text, [string]$cardAnswer = 'Deny', [int]$timeout = 90) {
    $before = (& $read).Count
    $m = Main
    (By $m 'AutomationIdProperty' 'Input').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
    (By $m 'AutomationIdProperty' 'SendButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $cards = @()
    $deadline = (Get-Date).AddSeconds($timeout)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $m = Main
        $button = if ($m) { By $m 'NameProperty' $cardAnswer }
        if ($button -and -not $button.Current.IsOffscreen) {
            $cards += (By $m 'AutomationIdProperty' 'ConfirmText').Current.Name
            $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        }
        $now = & $read
        if ($now.Count -gt $before + 1 -and $now[-1].Role -in 'assistant', 'error') { break }
    }
    $now = & $read
    [pscustomobject]@{ Reply = [string]$now[-1].Text; Role = $now[-1].Role; Cards = $cards; Lines = @($now | Select-Object -Skip $before | ForEach-Object { "[$($_.Role)] $($_.Text)" }) }
}

$results = @()
function Check([string]$name, [bool]$ok, [string]$detail) { $script:results += [pscustomobject]@{ Test = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }) }; Write-Host ("{0,-5} {1}`n      {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' }) }

try {
    Get-Process WinCompanion -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep 2
    $env:WINCOMPANION_DEBUG_LLM = ''; $env:WINCOMPANION_SKIP_SETUP = '1'
    $script:app = Start-Process $exe -ArgumentList '--show' -PassThru
    foreach ($i in 1..40) { Start-Sleep -Milliseconds 500; if (Main) { break } }

    $r = Ask 'What is 17 times 23? Answer with just the number.'
    Check 'Gemini answers a plain question' ($r.Role -eq 'assistant' -and $r.Reply -match '391') $r.Reply

    $r = Ask 'How much free disk space do I have on each drive?' -cardAnswer 'Deny'
    $usedTool = $r.Lines | Where-Object { $_ -match '^\[tool\]' }
    Check 'Gemini uses a tool for a question about the PC' ($r.Role -eq 'assistant' -and $usedTool) (($r.Lines | Select-Object -First 4) -join "`n      ")

    $r = Ask "What's on my screen right now?" -cardAnswer 'Deny'
    $asked = $r.Cards | Where-Object { $_ -match 'screenshot' -and $_ -match 'Gemini' }
    Check 'asks before sending a screenshot, and Deny keeps it on the PC' ([bool]$asked -and $r.Reply -notmatch '^\s*$') "card: $($r.Cards -join ' | ')`n      reply: $($r.Reply)"
}
catch { Write-Host "ABORTED: $($_.Exception.Message)" -ForegroundColor Red; $results += [pscustomobject]@{ Test = 'run'; Result = 'FAIL' } }
finally {
    Stop-Process -Id $script:app.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep 1
    Copy-Item "$backup\*" $data -Force
    $leftover = (Get-ChildItem $data -File).Name | Where-Object { $_ -notin $existed }
    if ($leftover) { Write-Host "Files this test created in $data (delete them): $($leftover -join ', ')" -ForegroundColor Yellow }
}
$failed = @($results | Where-Object Result -eq 'FAIL').Count
Write-Host "`n$($results.Count - $failed) passed, $failed failed" -ForegroundColor $(if ($failed) { 'Red' } else { 'Green' })
