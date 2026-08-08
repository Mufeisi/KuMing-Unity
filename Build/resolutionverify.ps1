# P2 分辨率缩放统一（对照 sanduan SizeRatio）确定性验证：Unity batchmode 探针。
# ScreenMetrics 单一扇出（渲染真值→触摸翻转基准+对话框布局）、ToUi 纯镜像 y 翻转（无黑边）、
# HUD/背包边缘锚点重算、MinTouchSize 触控下限。对照决策：sanduan SizeRatio 死代码，不引入黑边缩放。
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\resolutionverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.ResolutionVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[resolutionverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  resolution exit=$code : $line"
if ($code -eq 0 -and $line -match "\[resolutionverify\] PASS") { Write-Host "PASS: ResolutionVerify ok"; exit 0 }
Write-Host "FAIL"; exit 1
