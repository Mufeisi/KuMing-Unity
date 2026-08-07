# BagInteractVerify pure-logic verify: Unity batchmode probe, no server needed.
# Assert MirItemCell tap-select/empty-deselect/outside-ignore/page-state/page-clear-selection/close/quest-page.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\baginteractverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.BagInteractVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[baginteractverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  baginteract exit=$code : $line"
if ($code -eq 0 -and $line -match "\[baginteractverify\] PASS") { Write-Host "PASS: BagInteract pure logic ok" ; exit 0 }
Write-Host "FAIL"; exit 1
