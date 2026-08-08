# Fishing touch pure-logic verify: Unity batchmode probe, no server needed.
# FishingDialog/FishingStatusDialog resident + S.FishingUpdate local sync (progress/chance label +
# status bar show/hide) / remote dispatch (no-rod hide guard), Show no-rod prompt, FishButton cast,
# AutoCastButton toggle, Cancel cast+hide, CloseButtons, MobileBag fishing button (left-anchored
# under mount) UiConsumer toggle + no-rod prompt.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\fishingverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.FishingVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[fishingverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  fishing exit=$code : $line"
if ($code -eq 0 -and $line -match "\[fishingverify\] PASS") { Write-Host "PASS: FishingVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
