<#
.SYNOPSIS
  Clicks through every item of the tray menu and every control on the Settings screen of the real app.

.DESCRIPTION
  Uses Windows UI Automation to press the buttons and read back what happened (registry, files, windows, text).
  Your settings are backed up and restored, and "Start with Windows" is put back as it was. Run it while you are not
  using the PC: windows open and close. Build the app first (dotnet build).
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$repo = Split-Path $PSScriptRoot -Parent
$env:WINCOMPANION_SKIP_SETUP = '1'
$exe = Join-Path $repo 'bin\Debug\net8.0-windows\WinCompanion.exe'
$data = "$env:APPDATA\WinCompanion"
$backup = "$env:TEMP\wc-ui-backup"
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$A = [System.Windows.Automation.AutomationElement]
$root = $A::RootElement
$results = [System.Collections.Generic.List[object]]::new()
$ellipsis = [string][char]0x2026

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $results.Add([pscustomobject]@{ Test = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("{0,-5} {1}  {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}
function Windows-Of($id) { $root.FindAll('Children', (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $id))) | ForEach-Object { $_ } }
function By($scope, $property, [string]$value) {
    $scope.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition($A::$property, $value)))
}
function ByNameLike($scope, [string]$pattern) {
    $scope.FindAll('Descendants', [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like $pattern } | Select-Object -First 1
}
function Press($element) { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Toggle($element) { $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
function IsOn($element) { $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On' }
function Pick($element) { $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function IsSelected($element) { $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected }
function Text($element) { $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function SetText($element, [string]$v) { $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($v) }

$script:app = $null
function Launch([string[]]$arguments) {
    Get-Process WinCompanion -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 1500
    $script:app = Start-Process $exe -ArgumentList $arguments -PassThru
}
function Wait-For([scriptblock]$find, [int]$seconds = 20) {
    foreach ($i in 1..($seconds * 4)) { Start-Sleep -Milliseconds 250; $e = & $find; if ($e) { return $e } }
}
function Flyout { Windows-Of $script:app.Id | Where-Object { $_.Current.Name -eq 'Tessa menu' } | Select-Object -First 1 }
# The menu hides itself when something else takes focus; start a fresh copy of the app with it open if that happened.
function Open-Flyout { $f = Flyout; if (-not $f) { Launch @('--flyout'); $f = Wait-For { Flyout } }; $f }
# UI Automation sometimes drops the chat window from its list while a character window is on screen, so ask for it by its
# real window handle first (the window itself stays visible throughout; this only affects how it is found).
function MainWindow {
    $script:app.Refresh()
    if ($script:app.MainWindowHandle -ne [IntPtr]::Zero) {
        $e = $A::FromHandle($script:app.MainWindowHandle)
        if ($e -and (By $e 'AutomationIdProperty' 'Input')) { return $e }
    }
    Windows-Of $script:app.Id | Where-Object { By $_ 'AutomationIdProperty' 'Input' } | Select-Object -First 1
}
function Stop-App { if ($script:app) { Stop-Process -Id $script:app.Id -Force -ErrorAction SilentlyContinue } }

# ---- set up -------------------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force $backup | Out-Null
Copy-Item "$data\*" $backup -Force -ErrorAction SilentlyContinue
$existed = (Get-ChildItem $data -File).Name
$runBefore = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).WinCompanion
$started = Get-Date

try {
    Write-Host "`nTray menu" -ForegroundColor Cyan
    Launch @('--flyout')
    $fly = Wait-For { Flyout }
    Check 'tray menu opens' ([bool]$fly)
    $names = @('Open Tessa', "Settings$ellipsis", 'Action log', 'Data folder', 'Exit')
    $missing = $names | Where-Object { -not (By $fly 'NameProperty' $_) }
    Check 'tray menu shows all its buttons' (-not $missing) "missing: $($missing -join ', ')"
    Check 'tray menu shows the wake word and start-with-Windows switches' ((ByNameLike $fly '*Wake word*') -and (ByNameLike $fly '*Start with Windows*'))

    # Start with Windows: the switch must write (and remove) the real Run entry, pointing at this exe.
    $fly = Open-Flyout; $startup = ByNameLike $fly '*Start with Windows*'
    $wasOn = IsOn $startup
    Toggle $startup; Start-Sleep -Milliseconds 600
    $now = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).WinCompanion
    $turnedOn = -not $wasOn
    Check "Start with Windows: switching $(if ($turnedOn) { 'on' } else { 'off' }) updates the registry" ($(if ($turnedOn) { $now -like '*WinCompanion.exe*' } else { -not $now })) "Run value: $now"
    $fly = Open-Flyout; $startup = ByNameLike $fly '*Start with Windows*'
    Toggle $startup; Start-Sleep -Milliseconds 600
    $back = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).WinCompanion
    Check 'Start with Windows: switching back restores it' ($back -eq $runBefore) "before=[$runBefore] now=[$back]"

    # Wake word: turning it on opens the microphone briefly; it must not raise an error.
    $fly = Open-Flyout; $wake = ByNameLike $fly '*Wake word*'
    Toggle $wake; Start-Sleep -Seconds 2
    $on = IsOn $wake
    Toggle $wake; Start-Sleep -Milliseconds 800
    $chat = try { (Get-Content "$data\transcript.json" -Raw | ConvertFrom-Json) | Where-Object Role -eq 'error' } catch { @() }
    Check 'wake word switch turns on and off without an error' ($on -and -not ($chat | Where-Object { $_.Text -match 'Speech recognition' })) "on=$on"

    $fly = Open-Flyout
    Press (By $fly 'NameProperty' 'Open Tessa')
    $main = Wait-For { MainWindow } 8
    Check 'Open Tessa shows the chat window' ([bool]$main)

    Launch @('--flyout'); $fly = Wait-For { Flyout }
    Press (By $fly 'NameProperty' "Settings$ellipsis")
    $main = Wait-For { MainWindow } 8
    Check 'Settings... opens the settings screen' ($main -and (By (MainWindow) 'NameProperty' 'Save'))

    Launch @('--flyout'); $fly = Wait-For { Flyout }
    Press (By $fly 'NameProperty' 'Action log')
    $log = Wait-For { Get-Process notepad -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt $started } | Select-Object -First 1 } 8
    Check 'Action log opens the log file' ([bool]$log)
    if ($log) { Get-Process notepad | Where-Object { $_.StartTime -gt $started } | Stop-Process -Force -ErrorAction SilentlyContinue }

    Launch @('--flyout'); $fly = Wait-For { Flyout }
    Press (By $fly 'NameProperty' 'Data folder')
    $explorer = Wait-For { $root.FindAll('Children', [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like '*WinCompanion*' -and $_.Current.ClassName -eq 'CabinetWClass' } | Select-Object -First 1 } 10
    Check 'Data folder opens in Explorer' ([bool]$explorer)
    if ($explorer) { try { $explorer.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { } }

    Launch @('--flyout'); $fly = Wait-For { Flyout }
    $pid_ = $script:app.Id
    Press (By $fly 'NameProperty' 'Exit')
    Start-Sleep -Seconds 2
    Check 'Exit quits the app' ($null -eq (Get-Process -Id $pid_ -ErrorAction SilentlyContinue))

    Write-Host "`nSettings screen" -ForegroundColor Cyan
    Launch @('--settings')
    $main = Wait-For { MainWindow }
    $id = { param($n) By (MainWindow) 'AutomationIdProperty' $n }

    Pick (& $id 'ProviderLocal'); Start-Sleep -Milliseconds 400
    Check 'choosing Local shows the server fields' ((& $id 'LocalUrl') -and (& $id 'DetectButton') -and -not (& $id 'GeminiKeyBox'))
    Pick (& $id 'ProviderGemini'); Start-Sleep -Milliseconds 400
    Check 'choosing Gemini shows the key box and hides the server fields' ((& $id 'GeminiKeyBox') -and -not (& $id 'LocalUrl'))
    Pick (& $id 'ProviderLocal'); Start-Sleep -Milliseconds 400

    foreach ($preset in @(@('Ollama', '11434'), @('LM Studio', '1234'), @('llama.cpp', '8080'))) {
        Press (By (MainWindow) 'NameProperty' $preset[0]); Start-Sleep -Milliseconds 200
        Check "preset $($preset[0]) fills the URL" ((Text (& $id 'LocalUrl')) -like "*:$($preset[1])/v1")
    }
    SetText (& $id 'LocalModel') 'my-test-model'
    Check 'model field accepts text' ((Text (& $id 'LocalModel')) -eq 'my-test-model')
    SetText (& $id 'LocalVision') 'my-vision-model'
    Check 'vision model field accepts text' ((Text (& $id 'LocalVision')) -eq 'my-vision-model')

    Press (& $id 'DetectButton')
    foreach ($i in 1..24) { Start-Sleep -Milliseconds 500; $status = (By (MainWindow) 'AutomationIdProperty' 'LocalStatus').Current.Name; if ($status -notlike 'Looking*') { break } }
    Check 'Detect finishes with a result or a clear error (no crash, no hang)' ($status -notlike 'Looking*' -and (Get-Process -Id $script:app.Id -ErrorAction SilentlyContinue)) $status

    foreach ($sw in 'PsAsk', 'PsTrusted') { Pick (& $id $sw) }
    Check 'PowerShell mode can be switched' (IsSelected (& $id 'PsTrusted'))
    Pick (& $id 'PsAsk')
    foreach ($sw in 'PrivacyAsk') { $before = IsOn (& $id $sw); Toggle (& $id $sw); Check "privacy switch $sw toggles" ((IsOn (& $id $sw)) -ne $before) }

    # The character options are the radio buttons that have no automation id of their own.
    $choices = @((MainWindow).FindAll('Descendants', (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton))) |
        Where-Object { -not $_.Current.AutomationId })
    Check 'character picker lists the characters plus None' ($choices.Count -ge 12) "$($choices.Count) options: $(($choices | ForEach-Object { $_.Current.Name }) -join ', ')"

    # Preview each kind of character (walker, flyer, sitter), then dismiss it.
    foreach ($pick in 'Real Dog', 'Real Heron', 'Real Cat (sitting)') {
        # Start from a fresh copy of the settings screen each time, so one preview can't affect the next.
        Launch @('--settings'); Wait-For { MainWindow } | Out-Null
        $radio = (MainWindow).FindAll('Descendants', (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton))) |
            Where-Object { $_.Current.Name -eq $pick } | Select-Object -First 1
        if (-not $radio) { Check "preview $pick" $false 'not in the picker'; continue }
        Pick $radio
        Press (By (MainWindow) 'NameProperty' 'Preview')
        $petWin = Wait-For { Windows-Of $script:app.Id | Where-Object { $_.Current.Name -eq 'Tessa pet' } | Select-Object -First 1 } 25
        $done = if ($petWin) { Wait-For { By $petWin 'AutomationIdProperty' 'DoneButton' } 25 }
        Check "preview $pick shows it with its speech bubble" ([bool]$done)
        if ($done) { Press $done; Wait-For { -not (Windows-Of $script:app.Id | Where-Object { $_.Current.Name -eq 'Tessa pet' }) } 15 | Out-Null }
    }

    Launch @('--settings'); Wait-For { MainWindow } | Out-Null
    $choices = @((MainWindow).FindAll('Descendants', (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton))) | Where-Object { -not $_.Current.AutomationId })
    Pick ($choices | Where-Object { $_.Current.Name -eq 'None' } | Select-Object -First 1)
    Press (By (MainWindow) 'NameProperty' 'Cancel'); Start-Sleep -Milliseconds 500
    Check 'Cancel closes the settings screen' (-not (By (MainWindow) 'NameProperty' 'Save') -or (By (MainWindow) 'NameProperty' 'Save').Current.IsOffscreen)

    Launch @('--settings'); $main = Wait-For { MainWindow }
    Pick (By (MainWindow) 'AutomationIdProperty' 'ProviderLocal'); Start-Sleep -Milliseconds 300
    SetText (By (MainWindow) 'AutomationIdProperty' 'LocalModel') 'saved-model-name'
    Press (By (MainWindow) 'NameProperty' 'Save'); Start-Sleep -Seconds 1
    $saved = Get-Content "$data\settings.json" -Raw | ConvertFrom-Json
    Check 'Save writes the settings' ($saved.Provider -eq 'local' -and $saved.LocalModel -eq 'saved-model-name') "provider=$($saved.Provider) model=$($saved.LocalModel)"
}
catch { Check 'run completed' $false $_.Exception.Message; Write-Host $_.InvocationInfo.PositionMessage }
finally {
    Stop-App
    Start-Sleep -Seconds 1
    Copy-Item "$backup\*" $data -Force                                    # settings, chat, reminders, key as they were
    if ($runBefore) { Set-ItemProperty $runKey -Name WinCompanion -Value $runBefore } elseif ((Get-ItemProperty $runKey -ErrorAction SilentlyContinue).WinCompanion) { Remove-ItemProperty $runKey -Name WinCompanion }
    $leftover = (Get-ChildItem $data -File).Name | Where-Object { $_ -notin $existed }
    if ($leftover) { Write-Host "`nFiles the test created in $data (delete them): $($leftover -join ', ')" -ForegroundColor Yellow }
}

$failed = @($results | Where-Object Result -eq 'FAIL').Count
Write-Host "`n$($results.Count - $failed) passed, $failed failed" -ForegroundColor $(if ($failed) { 'Red' } else { 'Green' })
$results | Where-Object Result -eq 'FAIL' | Format-Table -AutoSize -Wrap
