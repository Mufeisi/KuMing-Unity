# P2 sanduan 软键盘桥（TouchScreenKeyboard ↔ MirTextBox 逻辑层）确定性验证：Unity batchmode 探针。
# SoftKeyboardBridge 纯逻辑核心 + ISoftKeyboard Fake 注入：绑定开键盘（文本/密码/最大长度透传）、
# 轮询文本同步、Enter 提交→KeyPress(Enter) 进控件树+解绑、取消/解绑关键盘、重复绑定先解旧。
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\softkeyboardverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.SoftKeyboardVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[softkeyboardverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  softkeyboard exit=$code : $line"
if ($code -eq 0 -and $line -match "\[softkeyboardverify\] PASS") { Write-Host "PASS: SoftKeyboardVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
