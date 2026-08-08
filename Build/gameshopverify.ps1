# GameShop touch pure-logic verify: Unity batchmode probe, no server needed.
# GameShopDialog resident + ctor keeps pushed items (session clear moved to S.StartGame branch),
# S.GameShopInfo fill + New tab, GameShopStock update/remove, Show class-filter reset, class/section
# tabs local filtering, payment type radio, BuyProduct three-way (Gold/Credit confirm packet,
# no-selection/insufficient prompt), paging, search, MobileBag gameshop button UiConsumer toggle.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\gameshopverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.GameShopVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[gameshopverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  gameshop exit=$code : $line"
if ($code -eq 0 -and $line -match "\[gameshopverify\] PASS") { Write-Host "PASS: GameShopVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
