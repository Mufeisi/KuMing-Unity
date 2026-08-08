# Settings trio touch pure-logic verify: Unity batchmode probe, no server needed.
# ChatOptionDialog/KeyboardLayoutDialog/HelpDialog resident + filter buttons flip Settings.Filter*
# (ToggleAllFilters all on/off), transparency switches, keybind row click -> WaitingForBind +
# CheckNewInput synthesized key (K/Delete) updates Keylist + ResetButton to defaults + prompt +
# EnforceButton toggle, HelpDialog paging, MobileBag settings button (left-anchored y=286)
# UiConsumer toggle + mutex + no joystick. Settings are all-local (no network packets).
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\settingsverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.SettingsVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[settingsverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  settings exit=$code : $line"
if ($code -eq 0 -and $line -match "\[settingsverify\] PASS") { Write-Host "PASS: SettingsVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
