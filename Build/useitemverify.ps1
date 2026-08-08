# Use-item pure-logic verify: Unity batchmode probe, no server needed.
# Double-tap inventory potion -> C.UseItem (locks cell), S.UseItem success roundtrip decrements count /
# clears last potion / unlocks, failure roundtrip unlocks without decrementing, no local heal (overflow
# impossible), weapon double-tap routes to equip chain (no C.UseItem).
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\useitemverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.UseItemVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[useitemverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  useitem exit=$code : $line"
if ($code -eq 0 -and $line -match "\[useitemverify\] PASS") { Write-Host "PASS: UseItemVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
