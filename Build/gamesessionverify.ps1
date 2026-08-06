# GameSession runtime network-session verify: start Server.exe -> GameSessionVerify -> assert -> stop server.
# Flow: login -> (create char) -> enter game -> object spawn -> GameRenderer render non-blank assert.
param(
    [string]$LoginId = "pcplayer",
    [string]$LoginPw = "pcplayer",
    [string]$CharName = "pcplayer",
    [int]$TimeoutMs = 90000
)
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$publish = Join-Path $root "Build\Server\publish"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$port = 7000

function Test-Port([int]$p) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect("127.0.0.1", $p); $c.Close(); return $true } catch { $c.Close(); return $false }
}

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

$log = Join-Path $root "Unity\Build\gamesessionverify.log"
$env:CRYSTAL_NET_HOST = "127.0.0.1"
$env:CRYSTAL_NET_PORT = "$port"
$env:CRYSTAL_LOGIN_ID = $LoginId
$env:CRYSTAL_LOGIN_PW = $LoginPw
$env:CRYSTAL_CHAR_NAME = $CharName
$env:CRYSTAL_NET_TIMEOUT = "$TimeoutMs"
$env:CRYSTAL_MAP_DIR = Join-Path $publish "Maps"
$env:CRYSTAL_MAP_ATLAS_DIR = Join-Path $root "Build\assetcompile\map"
$env:CRYSTAL_ATLAS_DIR = Join-Path $root "Build\assetcompile\all"
& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.GameSessionVerify.Run -logFile $log | Out-Null
$code = $LASTEXITCODE

if (-not $server.HasExited) { Stop-Process $server -Force }

$line = (Select-String -Path $log -Pattern "gamesession-verify: (PASS|FAIL)|\[gamesession\] error" | Select-Object -Last 1).Line
Write-Host "  gamesession exit=$($code): $line"
$ok = ($code -eq 0) -and ($line -match "gamesession-verify: PASS")
if ($ok) { Write-Host "PASS: GameSession login->enter->render ok" ; exit 0 } else { Write-Host "FAIL"; exit 1 }
