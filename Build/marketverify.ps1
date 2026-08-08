# Auction house touch pure-logic verify: Unity batchmode probe, no server needed.
# TrustMerchantDialog resident + S.NPCMarket/NPCMarketPage fill+paging, ConsignItem echo, MarketFail 0-10
# prompt+throttle reset, MarketSuccess prompt, filter tree/Find/Refresh packets, BuyButton three-way
# (Consign direct / Auction MirAmountBox bid / UserMode get-back), SellNow/CollectSold, consign flow
# (bag cell -> ItemCell_Click -> PriceTextBox -> SellItemButton), four-panel switch, MobileBag market
# button UiConsumer toggle.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\marketverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.MarketVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[marketverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  market exit=$code : $line"
if ($code -eq 0 -and $line -match "\[marketverify\] PASS") { Write-Host "PASS: MarketVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
