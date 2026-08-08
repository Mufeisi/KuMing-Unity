# 移动端断线重连（G8 缺口 2/4）verify: Unity batchmode probe, no server needed.
# 覆盖：场景1 延迟 3s 触发重连一次、场景2 Armed 防风暴（重复 Arm 不增计数）、场景3 Reset 清态。
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\mobilereconnectverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.MobileReconnectVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[mobile-reconnect\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  mobilereconnect exit=$code : $line"
if ($code -eq 0 -and $line -match "\[mobile-reconnect\] PASS") { Write-Host "PASS: MobileReconnectVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
