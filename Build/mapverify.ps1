# Big map touch pure-logic verify: Unity batchmode probe, no server needed.
# S.NewMapInfo/WorldMapSetup dispatch, record build, viewport autopath,
# movement button / npc row tap, teleport gold gate, MobileAutoPath step drive.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\mapverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.MapVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[mapverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  map exit=$code : $line"
if ($code -eq 0 -and $line -match "\[mapverify\] PASS") { Write-Host "PASS: MapVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
