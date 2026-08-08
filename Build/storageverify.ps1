# Storage deposit/withdraw pure-logic verify: Unity batchmode probe, no server needed.
# S.UserStorage/NPCStorage dispatch -> StorageDialog Show; select bag cell -> tap empty storage cell ->
# C.StoreItem; tap occupied storage cell (selected bag) -> silent; tap storage item -> C.TakeBackItem;
# echo swap+unlock; pagination; CloseButton hide.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\storageverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.StorageVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[storageverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  storage exit=$code : $line"
if ($code -eq 0 -and $line -match "\[storageverify\] PASS") { Write-Host "PASS: StorageVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
