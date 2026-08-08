# PC 长稳挂机 + 内存采样（9-3）：起 Server + Crystal.exe 挂机 N 分钟，每 30s 采样进程
# WorkingSet（内存），结束断言：进程存活（无崩溃）+ 内存无增长（末段均值 - 首段均值 ≤ 阈值）。
# 72h 全量长稳登记阶段收口（PRD 11.1），本脚本快内环 15 分钟（-Minutes 可调）。
# 用法：powershell -File Build/pcstability.ps1 [-Minutes 15] [-GrowMB 300]
param(
    [int]$Minutes = 15,
    [int]$GrowMB = 300
)
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$publish = Join-Path $root "Build\Server\publish"
$exe = Join-Path $root "Build\PC\Crystal.exe"
$playerLog = Join-Path $env:USERPROFILE "AppData\LocalLow\Mir2\Crystal\Player.log"
$port = 7000
$samples = New-Object System.Collections.Generic.List[long]

function Test-Port([int]$p) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect("127.0.0.1", $p); $c.Close(); return $true } catch { $c.Close(); return $false }
}
function Mem-MB($proc) {
    try { return [math]::Round((Get-Process -Id $proc.Id).WorkingSet64 / 1MB, 1) } catch { return -1 }
}

if (-not (Test-Path $exe)) { Write-Host "FAIL: $exe missing"; exit 1 }
$server = Start-Process -FilePath (Join-Path $publish "Server.exe") -WorkingDirectory $publish -PassThru -WindowStyle Hidden
$deadline = (Get-Date).AddSeconds(180); $ready = $false
while ((Get-Date) -lt $deadline) { if (Test-Port $port) { $ready = $true; break }; Start-Sleep -Seconds 5 }
if (-not $ready) { if (-not $server.HasExited) { Stop-Process $server -Force }; Write-Host "FAIL: server"; exit 1 }
Write-Host "server ready on $port"

if (Test-Path $playerLog) { Remove-Item $playerLog -Force -ErrorAction SilentlyContinue }
$env:CRYSTAL_NET_HOST = "127.0.0.1"; $env:CRYSTAL_NET_PORT = "$port"
$env:CRYSTAL_LOGIN_ID = "pcplayer"; $env:CRYSTAL_LOGIN_PW = "pcplayer"
$env:CRYSTAL_MAP_DIR = Join-Path $publish "Maps"
$env:CRYSTAL_MAP_ATLAS_DIR = Join-Path $root "Build\assetcompile\map"
$env:CRYSTAL_ATLAS_DIR = Join-Path $root "Build\assetcompile\all"
Remove-Item Env:CRYSTAL_AUTO_SHOT -ErrorAction SilentlyContinue

$client = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Write-Host "launched Crystal.exe pid=$($client.Id) hold=$Minutes min"
$end = (Get-Date).AddMinutes($Minutes)
$crashed = $false
while ((Get-Date) -lt $end) {
    Start-Sleep -Seconds 30
    if ($client.HasExited) { $crashed = $true; Write-Host "FAIL: player exited code=$($client.ExitCode)"; break }
    $mb = Mem-MB $client
    $samples.Add($mb)
    Write-Host "  t=$(($end - (Get-Date)).Minutes)m mem=${mb}MB"
}
if (-not $client.HasExited) { Stop-Process $client -Force }
if (-not $server.HasExited) { Stop-Process $server -Force }

if ($crashed) { Write-Host "FAIL: crash during hold"; exit 1 }
if ($samples.Count -lt 2) { Write-Host "FAIL: too few samples"; exit 1 }
$first = ($samples | Select-Object -First 3 | Measure-Object -Average).Average
$last = ($samples | Select-Object -Last 3 | Measure-Object -Average).Average
$grow = [math]::Round($last - $first, 1)
Write-Host "  mem first=${first}MB last=${last}MB grow=${grow}MB samples=$($samples.Count)"
if ($grow -gt $GrowMB) { Write-Host "FAIL: memory grew ${grow}MB > ${GrowMB}MB"; exit 1 }
Write-Host "PASS: hold $Minutes min alive + mem stable (grow ${grow}MB)"
