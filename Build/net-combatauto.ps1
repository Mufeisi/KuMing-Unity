# 阶段8 增量2 E2E: start Server.exe -> Unity batchmode NetProbe.RunCombatAuto ->
# MobileCombat 自动战斗对真实服务器（索敌->追击->攻击->击杀）-> assert combatauto ok.
# 击杀判据：S.ObjectDied(Type=0, 非玩家) 计数 >= 2，且我方攻击命中（S.ObjectStruck AttackerID==玩家）> 0。
# 独立账号 probecombat1（与 base 账号隔离，位置不受其他 E2E 漂移影响）。
param(
    [string]$LoginId = "probecombat1",
    [string]$LoginPw = "probecombat1",
    [string]$CharName = "probecombat",
    [int]$TimeoutMs = 100000
)
$ErrorActionPreference = "Continue"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$publish = Join-Path $root "Build\Server\publish"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$port = 7000

function Test-Port([int]$p) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect("127.0.0.1", $p); $c.Close(); return $true } catch { $c.Close(); return $false }
}

if (-not (Test-Path (Join-Path $publish "Server.exe"))) { Write-Host "FAIL: Server.exe missing"; exit 1 }

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

$log = Join-Path $root "Unity\Build\net-combatauto.log"
$env:CRYSTAL_NET_HOST = "127.0.0.1"
$env:CRYSTAL_NET_PORT = "$port"
$env:CRYSTAL_LOGIN_ID = $LoginId
$env:CRYSTAL_LOGIN_PW = $LoginPw
$env:CRYSTAL_CHAR_NAME = $CharName
$env:CRYSTAL_NET_TIMEOUT = "$TimeoutMs"
$env:CRYSTAL_MAP_DIR = Join-Path $publish "Maps"
$env:CRYSTAL_ATLAS_DIR = Join-Path $root "Build\assetcompile\all"
$env:CRYSTAL_MAP = "nn0.Map"
$env:CRYSTAL_OUT = Join-Path $root "Unity\Build\net-combatauto.png"
& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.NetProbe.RunCombatAuto -logFile $log | Out-Null
$code = $LASTEXITCODE

if (-not $server.HasExited) { Stop-Process $server -Force }

$line = (Select-String -Path $log -Pattern "\[netprobe\]" | Select-Object -Last 1).Line
$kills = (Select-String -Path $log -Pattern "combatauto kill=" | Measure-Object).Count
Write-Host "exit=$code kills=$kills : $line"
$ok = ($code -eq 0) -and ($line -match "combatauto ok")
if ($ok) { Write-Host "PASS: MobileCombat auto-combat vs real server ok"; exit 0 } else { Write-Host "FAIL"; exit 1 }
