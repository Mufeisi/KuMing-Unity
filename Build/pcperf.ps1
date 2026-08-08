# PC 性能基线采样（9-4，按数据优化第一步）：起服 + 客户端 → 收集 [pcplayer] fps 行（每 5s）
# + 进图耗时（boot→enter-game，Player.log 行序）→ 输出基线数据 + 断言存活。
# 产出：Build/pcperf-baseline.txt（基线数据，写回 migration-status）。
# 用法：powershell -File Build/pcperf.ps1 [-Seconds 60]
param(
    [int]$Seconds = 60
)
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$publish = Join-Path $root "Build\Server\publish"
$exe = Join-Path $root "Build\PC\Crystal.exe"
$playerLog = Join-Path $env:USERPROFILE "AppData\LocalLow\Mir2\Crystal\Player.log"
$port = 7000
$baseline = Join-Path $root "Build\pcperf-baseline.txt"

function Test-Port([int]$p) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect("127.0.0.1", $p); $c.Close(); return $true } catch { $c.Close(); return $false }
}
function Test-ExeStale {
    $exeTime = (Get-Item $exe).LastWriteTime
    $newest = Get-ChildItem (Join-Path $root "Unity\Assets\Crystal\Client.Rendering") -Recurse -File -Include *.cs |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    return ($newest.LastWriteTime -gt $exeTime)
}

if (-not (Test-Path $exe)) { Write-Host "FAIL: $exe missing"; exit 1 }
if (Test-ExeStale) {
    Write-Host "=== exe stale → rebuild ==="
    powershell -ExecutionPolicy Bypass -File (Join-Path $root "Build\buildpc.ps1")
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: buildpc"; exit 1 }
}
$server = Start-Process -FilePath (Join-Path $publish "Server.exe") -WorkingDirectory $publish -PassThru -WindowStyle Hidden
$deadline = (Get-Date).AddSeconds(180); $ready = $false
while ((Get-Date) -lt $deadline) { if (Test-Port $port) { $ready = $true; break }; Start-Sleep -Seconds 5 }
if (-not $ready) { if (-not $server.HasExited) { Stop-Process $server -Force }; Write-Host "FAIL: server"; exit 1 }

if (Test-Path $playerLog) { Remove-Item $playerLog -Force -ErrorAction SilentlyContinue }
$env:CRYSTAL_NET_HOST = "127.0.0.1"; $env:CRYSTAL_NET_PORT = "$port"
$env:CRYSTAL_LOGIN_ID = "pcplayer"; $env:CRYSTAL_LOGIN_PW = "pcplayer"
$env:CRYSTAL_MAP_DIR = Join-Path $publish "Maps"
$env:CRYSTAL_MAP_ATLAS_DIR = Join-Path $root "Build\assetcompile\map"
$env:CRYSTAL_ATLAS_DIR = Join-Path $root "Build\assetcompile\all"
Remove-Item Env:CRYSTAL_AUTO_SHOT -ErrorAction SilentlyContinue

$bootAt = Get-Date
$client = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Write-Host "launched Crystal.exe pid=$($client.Id) sample=$Seconds s"
$deadline = (Get-Date).AddSeconds($Seconds)
$crashed = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    if ($client.HasExited) { $crashed = $true; Write-Host "FAIL: player exited code=$($client.ExitCode)"; break }
}
if (-not $client.HasExited) { Stop-Process $client -Force }
try { Wait-Process -Id $client.Id -Timeout 30 -ErrorAction SilentlyContinue } catch { }
if (-not $server.HasExited) { Stop-Process $server -Force }

if ($crashed) { Write-Host "FAIL: crash"; exit 1 }
$fps = @()
if (Test-Path $playerLog) {
    $fps = @(Select-String -Path $playerLog -Pattern "\[pcplayer\] fps=([0-9.]+)" | ForEach-Object { [double]$_.Matches[0].Groups[1].Value })
}
$enterLine = if (Test-Path $playerLog) { (Select-String -Path $playerLog -Pattern "\[pcplayer\] enter-game" | Select-Object -First 1) } else { $null }
$enterAt = if ($enterLine) { $enterLine.LineNumber } else { -1 }
# 进图耗时：boot 行 → enter-game 行（行号差 × 每行耗时不可靠；用日志时间戳差，若 Unity 日志无时间戳则用行距估算）
$bootLine = if (Test-Path $playerLog) { (Select-String -Path $playerLog -Pattern "\[pcplayer\] boot" | Select-Object -First 1) } else { $null }
$lineGap = if ($bootLine -and $enterLine) { $enterLine.LineNumber - $bootLine.LineNumber } else { -1 }

$avgFps = if ($fps.Count -gt 0) { [math]::Round(($fps | Measure-Object -Average).Average, 1) } else { -1 }
$minFps = if ($fps.Count -gt 0) { [math]::Round(($fps | Measure-Object -Minimum).Minimum, 1) } else { -1 }
$p95 = if ($fps.Count -gt 1) {
    $sorted = $fps | Sort-Object
    [math]::Round($sorted[[math]::Floor($sorted.Count * 0.95)], 1)
} else { -1 }
$summary = "PC 性能基线（$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')）：fps avg=$avgFps min=$minFps p95=$p95（低=差）samples=$($fps.Count) 进图行距=$lineGap 挂机=$Seconds s"
Set-Content $baseline $summary -Encoding UTF8
Write-Host "  $summary"
Write-Host "  baseline -> $baseline"
if ($fps.Count -gt 0) { Write-Host "PASS: perf sample ok (fps avg=$avgFps min=$minFps)" ; exit 0 }
Write-Host "FAIL: no fps samples"; exit 1
