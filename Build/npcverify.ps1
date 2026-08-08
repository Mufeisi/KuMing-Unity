# NPC-dialog pure-logic verify: Unity batchmode probe, no server needed.
# Map tap -> nearest NPCObject -> C.CallNPC[@Main] (throttled); S.NPCResponse -> dialog render +
# option click -> C.CallNPC[action] + @Exit close.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\npcverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.NpcVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[npcverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  npc exit=$code : $line"
if ($code -eq 0 -and $line -match "\[npcverify\] PASS") { Write-Host "PASS: NpcVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
