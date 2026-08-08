# PC 安装/补丁/首启/异常恢复（9-2）verify: Unity batchmode probe, no server needed.
# 覆盖：场景1 首启写默认、场景2 二次启动读值、场景3 设置持久化（1366/FullScreen→Resolution 档）、
#   场景4 崩溃残留 mark → 安全模式判定、场景5 crash.log 写盘 + 轮转 3 份。
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\pcstartupverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.PcStartupVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[pc-startup\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  pcstartup exit=$code : $line"
if ($code -eq 0 -and $line -match "\[pc-startup\] PASS") { Write-Host "PASS: PcStartupVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
