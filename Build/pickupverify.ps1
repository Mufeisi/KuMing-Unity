# Ground-pickup pure-logic verify: Unity batchmode probe, no server needed.
# Map tap (ui space) -> tile -> nearest ItemObject target; adjacent -> C.PickUp (throttled),
# non-adjacent -> PathFinder walk then pick up; target removed (S.ObjectRemove) -> auto clear.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\pickupverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.PickupVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[pickupverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  pickup exit=$code : $line"
if ($code -eq 0 -and $line -match "\[pickupverify\] PASS") { Write-Host "PASS: PickupVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
