# P4-M4 orchestration: start server (Server.exe standalone) -> Unity interact probe -> assert -> stop server.
# Interact probe: login -> create char if none -> StartGame -> five deterministic bidirectional interactions
#   (Chat -> Bag swap -> NPC dialogue -> Pickup -> Use), CRYSTAL_COMBAT=1 adds Walk+Attack vs monster.
# Combat uses a separate account (-CombatId) so the base account never leaves spawn (stays stable across runs).
param(
    [string]$LoginId = "probe1",
    [string]$LoginPw = "probe1",
    [string]$CharName = "probe",
    [string]$Combat = "0",
    [string]$CombatId = "probecombat1",
    [string]$CombatPw = "probecombat1",
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

if ($Combat -eq "1") { $LoginId = $CombatId; $LoginPw = $CombatPw; $CharName = "probecombat" }
$log = Join-Path $root "Unity\Build\net-interact.log"
$env:CRYSTAL_NET_HOST = "127.0.0.1"
$env:CRYSTAL_NET_PORT = "$port"
$env:CRYSTAL_LOGIN_ID = $LoginId
$env:CRYSTAL_LOGIN_PW = $LoginPw
$env:CRYSTAL_CHAR_NAME = $CharName
$env:CRYSTAL_NET_TIMEOUT = "$TimeoutMs"
$env:CRYSTAL_COMBAT = $Combat
$env:CRYSTAL_OUT = Join-Path $root "Unity\Build\net-interact.png"
& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.NetProbe.RunInteract -logFile $log | Out-Null
$code = $LASTEXITCODE

if (-not $server.HasExited) { Stop-Process $server -Force }

$line = (Select-String -Path $log -Pattern "\[netprobe\]" | Select-Object -Last 1).Line
Write-Host "exit=$($code): $line"
$ok = ($code -eq 0) -and ($line -match "interact ok")
if ($ok) { Write-Host "PASS"; exit 0 } else { Write-Host "FAIL"; exit 1 }
