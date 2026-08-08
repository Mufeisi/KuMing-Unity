# Trade flow touch pure-logic verify: Unity batchmode probe, no server needed.
# TradeDialog/GuestTradeDialog resident + S.TradeRequest/Accept/Gold/Item/Confirm/Cancel/
# DepositTradeItem/RetrieveTradeItem echo, two-stage MirItemCell deposit/retrieve,
# GoldLabel MirAmountBox (C.TradeGold), MobileTrade tap->C.TradeRequest throttle.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\tradeverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.TradeVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[tradeverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  trade exit=$code : $line"
if ($code -eq 0 -and $line -match "\[tradeverify\] PASS") { Write-Host "PASS: TradeVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
