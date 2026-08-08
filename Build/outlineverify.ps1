# P1 sanduan OutLine.shader sprite-outline semantics verify: Unity batchmode probe, no server needed.
# CrystalSpriteOutline shader + CrystalSpriteBatch.DrawOutline (4-way offset tint copies) produce a
# 1px outside halo; shadow exceptions (16/8/8, r<0.01) must NOT be outlined; interior pixels keep source.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\outlineverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.OutlineVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[outlineverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  outline exit=$code : $line"
if ($code -eq 0 -and $line -match "\[outlineverify\] PASS") { Write-Host "PASS: OutlineVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
