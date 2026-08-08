# Shop buy/sell pure-logic verify: Unity batchmode probe, no server needed.
# S.NPCGoods -> GetItemInfo resolve -> NPCGoodsDialog render+Show; cell tap select ->
# BuyButton -> C.BuyItem{count capped by StackSize/gold/listing}; CloseButton hide.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\shopverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.ShopVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[shopverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  shop exit=$code : $line"
if ($code -eq 0 -and $line -match "\[shopverify\] PASS") { Write-Host "PASS: ShopVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
