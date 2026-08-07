# PC Player runtime verify: start Server.exe -> launch Crystal.exe -> wait auto-screenshot -> assert -> cleanup.
# Flow: player boots -> connects -> login -> enter -> renders -> auto-shot PNG (CRYSTAL_AUTO_SHOT) after 6s in game.
param(
    [string]$LoginId = "pcplayer",
    [string]$LoginPw = "pcplayer",
    [int]$WaitSec = 150
)
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$publish = Join-Path $root "Build\Server\publish"
$exe = Join-Path $root "Build\PC\Crystal.exe"
$shot = Join-Path $root "Build\pcplayer-shot.png"
$playerLog = Join-Path $env:USERPROFILE "AppData\LocalLow\Mir2\Crystal\Player.log"
$port = 7000

function Test-Port([int]$p) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect("127.0.0.1", $p); $c.Close(); return $true } catch { $c.Close(); return $false }
}

if (-not (Test-Path $exe)) { Write-Host "FAIL: $exe missing (run buildpc.ps1 first)"; exit 1 }

$server = Start-Process -FilePath (Join-Path $publish "Server.exe") -WorkingDirectory $publish -PassThru -WindowStyle Hidden
$deadline = (Get-Date).AddSeconds(180)
$ready = $false
while ((Get-Date) -lt $deadline) {
    if (Test-Port $port) { $ready = $true; break }
    Start-Sleep -Seconds 5
}
if (-not $ready) {
    if (-not $server.HasExited) { Stop-Process $server -Force }
    Write-Host "FAIL: server did not open port $port"
    exit 1
}
Write-Host "server ready on port $port"

if (Test-Path $shot) { Remove-Item $shot -Force }
# Unity truncates Player.log at launch (it is NOT append-only across runs), so any
# per-run line filter computed from the pre-launch file is wrong. Delete the log
# before launching so every line we match below belongs to this run.
if (Test-Path $playerLog) { Remove-Item $playerLog -Force -ErrorAction SilentlyContinue }
$env:CRYSTAL_NET_HOST = "127.0.0.1"
$env:CRYSTAL_NET_PORT = "$port"
$env:CRYSTAL_LOGIN_ID = $LoginId
$env:CRYSTAL_LOGIN_PW = $LoginPw
$env:CRYSTAL_MAP_DIR = Join-Path $publish "Maps"
$env:CRYSTAL_MAP_ATLAS_DIR = Join-Path $root "Build\assetcompile\map"
$env:CRYSTAL_ATLAS_DIR = Join-Path $root "Build\assetcompile\all"
$env:CRYSTAL_AUTO_SHOT = $shot

$client = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Write-Host "launched Crystal.exe pid=$($client.Id)"

$deadline = (Get-Date).AddSeconds($WaitSec)
$shotOk = $false
while ((Get-Date) -lt $deadline) {
    if ($client.HasExited) {
        Write-Host "FAIL: player exited early code=$($client.ExitCode)"
        break
    }
    $hit = if (Test-Path $playerLog) { Select-String -Path $playerLog -Pattern "\[pcplayer\] shot " } else { $null }
    $sz = if (Test-Path $shot) { (Get-Item $shot).Length } else { 0 }
    if ($hit -and $sz -gt 10000) { $shotOk = $true; break }
    Start-Sleep -Seconds 3
}
if (-not $client.HasExited) { Stop-Process $client -Force }
if (-not $server.HasExited) { Stop-Process $server -Force }

$size = if (Test-Path $shot) { (Get-Item $shot).Length } else { 0 }
$shotLine = if (Test-Path $playerLog) { (Select-String -Path $playerLog -Pattern "\[pcplayer\] (shot|error|enter-game)" | Select-Object -Last 1).Line } else { "" }
Write-Host "  player shotOk=$shotOk size=$size : $shotLine"
$ok = $shotOk -and ($size -gt 10000)
if ($ok) { Write-Host "PASS: PC Player boot->login->enter->render->shot ok" ; exit 0 } else { Write-Host "FAIL"; exit 1 }
