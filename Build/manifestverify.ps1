# OTA manifest 版本系统（8-9-1）verify: Unity batchmode probe, no server needed.
# 覆盖：场景1 PlanDiff（变更+缺失检出）、场景2 HTTP 端到端下载校验（MiniHttpServer）、
#   场景3 IsVersionOutdated 版本比对（无清单/版本不同 → 过期；版本一致 → 不过期；
#   版本一致+文件篡改 → PlanDiff 兜底检出）、场景4 AssetCompiler manifest 确定性
#   （dotnet 调 AssetCompiler.dll 两次同输入同 --version → Version/Files 一致，GeneratedUtc 忽略）。
# 前置：AssetCompiler 需已构建 Release（本脚本先构建）。
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\resourcesync-verify.log"

Write-Host "=== build AssetCompiler (Release) ==="
dotnet build (Join-Path $root "tools\AssetCompiler\AssetCompiler.csproj") -c Release -v q
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: AssetCompiler build"; exit 1 }

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.ResourceSyncVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[resourcesync\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  resourcesync exit=$code : $line"
if ($code -eq 0 -and $line -match "\[resourcesync\] PASS") { Write-Host "PASS: ResourceSyncVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
