# P0 sanduan object-model (SpellObject + ItemObject) pure-logic verify: Unity batchmode probe, no server needed.
# SpellObject Load frame selection per Spell enum + Process frame advance/wrap, ItemObject Load (item + gold tiers),
# GameSession dispatch (ObjectSpell lands / ObjectItem+ObjectGold skip when FloorItems atlas absent).
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\objectmodelverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.ObjectModelVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[objectmodelverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  objectmodel exit=$code : $line"
if ($code -eq 0 -and $line -match "\[objectmodelverify\] PASS") { Write-Host "PASS: ObjectModelVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
