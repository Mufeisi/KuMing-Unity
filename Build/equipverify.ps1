# Equip/unequip pure-logic verify: Unity batchmode probe, no server needed.
# Double-tap inventory -> C.EquipItem (wearable locks both cells / non-wearable no packet),
# S.EquipItem roundtrip swaps arrays + refreshes stats + unlocks, double-tap equipment -> C.RemoveItem,
# S.RemoveItem roundtrip returns item to inventory.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\equipverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.EquipVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[equipverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  equip exit=$code : $line"
if ($code -eq 0 -and $line -match "\[equipverify\] PASS") { Write-Host "PASS: EquipVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
