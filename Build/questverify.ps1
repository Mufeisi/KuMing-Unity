# Quest windows touch pure-logic verify: Unity batchmode probe, no server needed.
# S.NewQuestInfo/ChangeQuest dispatch, NpcResponse quest-list gating, diary group + row
# detail open, track button toggle + 5-cap.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\questverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.QuestVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[questverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  quest exit=$code : $line"
if ($code -eq 0 -and $line -match "\[questverify\] PASS") { Write-Host "PASS: QuestVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
