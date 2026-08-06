# GameRenderer 运行时渲染核心验证：与 SceneRender 参考实现同场景 tile 层逐像素对照（diff==0）。
# 用法: powershell -ExecutionPolicy Bypass -File Build/gamerenderverify.ps1
param(
    [int]$TimeoutMs = 120000
)
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\gamerenderverify.log"

$env:CRYSTAL_MAP_DIR = Join-Path $root "Build\Server\publish\Maps"
$env:CRYSTAL_MAP_ATLAS_DIR = Join-Path $root "Build\assetcompile\map"
$env:CRYSTAL_MAP = "0.map"
$env:CRYSTAL_CENTER = "350,350"
$env:CRYSTAL_RT_W = "1152"
$env:CRYSTAL_RT_H = "640"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.GameRendererVerify.Run -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "gamerenderer-verify: (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  gamerenderer exit=$($code): $line"
if ($code -ne 0 -or $line -notmatch "PASS") { Write-Host "FAIL: gamerenderer probe failed"; exit 1 }
Write-Host "PASS: GameRenderer == SceneRender (tile layer byte-identical)"
exit 0
