# P4-M5 orchestration: start server (Server.exe standalone) -> Unity dual-open probe -> assert -> stop server.
# Dual-open probe: A (probe1/probe) enters, B (probe2/probe2b, scripted raw socket) enters -> mutual visibility
#   (A sees S.ObjectPlayer{B} + S.ObjectWalk{B}; B sees S.ObjectPlayer{A}). -SoakMs > 0 appends a sustained-play
#   soak: B keeps walking/chatting until the deadline while A processes packets without disconnecting.
param(
    [string]$LoginId = "probe1",
    [string]$LoginPw = "probe1",
    [string]$CharName = "probe",
    [string]$BLoginId = "probe2",
    [string]$BLoginPw = "probe2",
    [string]$BCharName = "probe2b",
    [int]$SoakMs = 0,
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

$log = Join-Path $root "Unity\Build\net-dualopen.log"
$env:CRYSTAL_NET_HOST = "127.0.0.1"
$env:CRYSTAL_NET_PORT = "$port"
$env:CRYSTAL_LOGIN_ID = $LoginId
$env:CRYSTAL_LOGIN_PW = $LoginPw
$env:CRYSTAL_CHAR_NAME = $CharName
$env:CRYSTAL_B_LOGIN_ID = $BLoginId
$env:CRYSTAL_B_LOGIN_PW = $BLoginPw
$env:CRYSTAL_B_CHAR = $BCharName
$env:CRYSTAL_SOAK_MS = "$SoakMs"
$env:CRYSTAL_NET_TIMEOUT = "$TimeoutMs"
& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.NetProbe.RunDualOpen -logFile $log | Out-Null
$code = $LASTEXITCODE

if (-not $server.HasExited) { Stop-Process $server -Force }

$line = (Select-String -Path $log -Pattern "\[netprobe\]" | Select-Object -Last 1).Line
Write-Host "exit=$($code): $line"
$ok = ($code -eq 0) -and ($line -match "dualopen ok")
if ($ok) { Write-Host "PASS"; exit 0 } else { Write-Host "FAIL"; exit 1 }
