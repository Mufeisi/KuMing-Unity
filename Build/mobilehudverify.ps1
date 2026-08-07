# MobileHud pure-logic verify: Unity batchmode probe, no server needed.
# Assert attack-button hit/outside/cooldown/cancel/slide-out + hpbar ratio/layout anchoring.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\mobilehudverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.MobileHudVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[hudverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  hud exit=$code : $line"
if ($code -eq 0 -and $line -match "\[hudverify\] PASS") { Write-Host "PASS: MobileHud pure logic ok" ; exit 0 }
Write-Host "FAIL"; exit 1
