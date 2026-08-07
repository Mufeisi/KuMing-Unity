# MobileBag pure-logic verify: Unity batchmode probe, no server needed.
# Assert bag hit/toggle/consume semantics/cancel no-toggle/release tolerance/double-tap flip/relayout.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\bagverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.MobileBagVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[bagverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  bag exit=$code : $line"
if ($code -eq 0 -and $line -match "\[bagverify\] PASS") { Write-Host "PASS: MobileBag pure logic ok" ; exit 0 }
Write-Host "FAIL"; exit 1
