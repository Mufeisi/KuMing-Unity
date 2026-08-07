# MobileUiAdapter pure-logic verify: Unity batchmode probe, no server needed.
# Assert coordinate flip / min-touch / dialog hit / touch mutex route / back key / scroll conflict / input contract timing.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\mobileuiverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.MobileUiVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[mobileuiverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  uiverify exit=$code : $line"
if ($code -eq 0 -and $line -match "\[mobileuiverify\] PASS") { Write-Host "PASS: MobileUiAdapter pure logic ok" ; exit 0 }
Write-Host "FAIL"; exit 1
