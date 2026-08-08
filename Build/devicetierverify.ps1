# 设备分级动态降级（8-10）verify: Unity batchmode probe, no server needed.
# 覆盖：场景1 Classify 决策表 5 组注入断言、场景2 For 三档配置单调性、
#   场景3 TierQualityApplier.Apply 映射到真实消费点（GameRuntime.RenderScale/DrawDistanceScale、
#   AtlasLibrary.TextureLevel）+ 热重载切换 + 幂等。
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\devicetierverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.DeviceTierVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[device-tier\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  devicetier exit=$code : $line"
if ($code -eq 0 -and $line -match "\[device-tier\] PASS") { Write-Host "PASS: DeviceTierVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
