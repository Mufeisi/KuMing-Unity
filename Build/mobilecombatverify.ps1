# MobileCombat pure-logic verify: Unity batchmode probe, no server needed.
# Assert auto-combat target-acquire/radius/dead-skip/adjacent-attack+cooldown/path-chase+walk-throttle/retarget.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\mobilecombatverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.MobileCombatVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[combatverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  combat exit=$code : $line"
if ($code -eq 0 -and $line -match "\[combatverify\] PASS") { Write-Host "PASS: MobileCombat pure logic ok" ; exit 0 }
Write-Host "FAIL"; exit 1
