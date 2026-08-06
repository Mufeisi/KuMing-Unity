# P4-M1 orchestration: start server (Server.exe standalone) -> Unity login probe (positive+negative) -> assert -> stop server.
# Server untouched; account self-registers if missing (DB persisted, reruns idempotent).
param(
    [string]$LoginId = "probe1",
    [string]$LoginPw = "probe1",
    [string]$WrongPw = "wrongpw9",
    [int]$TimeoutMs = 60000
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

# 1. Start server and wait for port
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

function Run-Probe([string]$id, [string]$pw, [string]$tag) {
    $log = Join-Path $root "Unity\Build\net-login-$tag.log"
    $env:CRYSTAL_NET_HOST = "127.0.0.1"
    $env:CRYSTAL_NET_PORT = "$port"
    $env:CRYSTAL_LOGIN_ID = $id
    $env:CRYSTAL_LOGIN_PW = $pw
    $env:CRYSTAL_NET_TIMEOUT = "$TimeoutMs"
    & $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.NetProbe.RunLogin -logFile $log | Out-Null
    $code = $LASTEXITCODE
    $line = (Select-String -Path $log -Pattern "\[netprobe\]" | Select-Object -Last 1).Line
    return @{ code = $code; line = $line }
}

$pos = Run-Probe $LoginId $LoginPw "pos"
$neg = Run-Probe $LoginId $WrongPw "neg"

# 3. Stop server
if (-not $server.HasExited) { Stop-Process $server -Force }

# 4. Assert
$posOk = ($pos.code -eq 0) -and ($pos.line -match "login ok")
$negOk = ($neg.code -eq 1) -and ($neg.line -match "fail=")
Write-Host "POS exit=$($pos.code): $($pos.line)"
Write-Host "NEG exit=$($neg.code): $($neg.line)"
if ($posOk -and $negOk) { Write-Host "PASS"; exit 0 } else { Write-Host "FAIL"; exit 1 }
