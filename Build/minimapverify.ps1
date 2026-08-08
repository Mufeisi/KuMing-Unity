# Mini map touch pure-logic verify: Unity batchmode probe, no server needed.
# MiniMapDialog resident control tree, big/small mode toggle, Process label refresh,
# BigMapButton open-big-map, BeforeDraw mode adapt, DuraStatusPanel seam guard.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\minimapverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.MiniMapVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[minimapverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  minimap exit=$code : $line"
if ($code -eq 0 -and $line -match "\[minimapverify\] PASS") { Write-Host "PASS: MiniMapVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
