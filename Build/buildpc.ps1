# Build StandaloneWindows64 PC Player via Unity batchmode.
# Usage: powershell -File Build/buildpc.ps1
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\buildpc.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.BuildPC.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE

$line = (Select-String -Path $log -Pattern "\[build-pc\] OK |\[build-pc\] FAIL|\[build-pc\] exception" | Select-Object -Last 1).Line
Write-Host "  buildpc exit=$($code): $line"
$ok = ($code -eq 0) -and ($line -match "\[build-pc\] OK")
if ($ok) { Write-Host "PASS: PC Player build ok" ; exit 0 } else { Write-Host "FAIL"; exit 1 }
