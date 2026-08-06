# Phase-6 weather verification: Unity batchmode -> WeatherRender.RunWeather -> assert -> exit.
# Pure client probe (no server needed): real Weather.Lib atlas (G3 external supplement snapshot,
# sha256 9A065B7D...) -> Libraries.Weather -> GameScene.UpdateWeather particle engines
# (Rain=164/Snow=43/Fog=0) -> Process stepping -> RT -> PNG + data/pixel assertions.
param(
    [int]$TimeoutMs = 120000
)
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\net-weather.log"

$env:CRYSTAL_ATLAS_DIR = Join-Path $root "Build\assetcompile\all"
$env:CRYSTAL_OUT = Join-Path $root "Unity\Build\net-weather.png"
$env:CRYSTAL_RT_W = "1024"
$env:CRYSTAL_RT_H = "768"
$env:CRYSTAL_WEATHER = "Rain,Snow,Fog"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.WeatherRender.RunWeather -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[netprobe\] weather" | Select-Object -Last 1).Line
Write-Host "  weather exit=$($code): $line"
if ($code -ne 0 -or $line -notmatch "weather ok") { Write-Host "FAIL: weather probe failed"; exit 1 }
Write-Host "PASS: weather probe ok"
exit 0
