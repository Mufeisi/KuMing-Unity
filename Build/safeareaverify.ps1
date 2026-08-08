# Safe-area + soft-keyboard touch wiring verify: Unity batchmode probe.
# SafeArea four-inset injection -> HUD/backpack/button column anchor offsets;
# MirTextBox touch focus -> SoftKeyboardBridge.Focus -> text sync -> Enter submit.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\safeareaverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.SafeAreaVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[safeareaverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  safearea exit=$code : $line"
if ($code -eq 0 -and $line -match "\[safeareaverify\] PASS") { Write-Host "PASS: SafeAreaVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
