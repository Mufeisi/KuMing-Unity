# Phase-6 fishing verification: start publish Server.exe (single session) -> Edge fishing sub-mode -> assert -> stop server.
# Fishing probe = NetProbe.Mode.Edge (CRYSTAL_EDGE=fishing, log asserts [netprobe] edge ok), covering
#   @make BlueFishingRod (S.NewItemInfo+S.GainedItem 真实服务器数据) -> C.EquipItem{Weapon} (S.EquipItem Success)
#   -> 客户端 HasFishingRod 断言 -> 渲染 FishingDialog+FishingStatusDialog（数据+像素断言）
#   -> S.FishingUpdate 客户端回放（Ported PlayerObject.FishingUpdate 状态链）。
#   fishing 需要 Settings.TestServer=True（@make gate）。Setup.ini 备份并翻转后起服，结束后恢复。
param(
    [string]$LoginId = "probefish1",
    [string]$LoginPw = "probefish1",
    [string]$CharName = "probefish1",
    [int]$TimeoutMs = 60000
)
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$publish = Join-Path $root "Build\Server\publish"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$port = 7000
$setup = Join-Path $publish "Configs\Setup.ini"
$setupBak = Join-Path $publish "Configs\Setup.ini.bak-fishing"

function Test-Port([int]$p) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect("127.0.0.1", $p); $c.Close(); return $true } catch { $c.Close(); return $false }
}

# TestServer is a static read once at server boot (Settings.cs:386); must flip BEFORE Start-Process.
Copy-Item $setup $setupBak -Force
(Get-Content $setup) -replace '^TestServer=False', 'TestServer=True' | Set-Content $setup -Encoding ASCII
Write-Host "Setup.ini: TestServer=True (backed up)"

$server = Start-Process -FilePath (Join-Path $publish "Server.exe") -WorkingDirectory $publish -PassThru -WindowStyle Hidden
$deadline = (Get-Date).AddSeconds(180)
$ready = $false
while ((Get-Date) -lt $deadline) {
    if (Test-Port $port) { $ready = $true; break }
    Start-Sleep -Seconds 5
}
if (-not $ready) {
    if (-not $server.HasExited) { Stop-Process $server -Force }
    Copy-Item $setupBak $setup -Force
    Write-Host "FAIL: server did not open port $port"
    exit 1
}
Write-Host "server ready on port $port"

$log = Join-Path $root "Unity\Build\net-fishing.log"
$env:CRYSTAL_NET_HOST = "127.0.0.1"
$env:CRYSTAL_NET_PORT = "$port"
$env:CRYSTAL_LOGIN_ID = $LoginId
$env:CRYSTAL_LOGIN_PW = $LoginPw
$env:CRYSTAL_CHAR_NAME = $CharName
$env:CRYSTAL_NET_TIMEOUT = "$TimeoutMs"
$env:CRYSTAL_EDGE = "fishing"
$env:CRYSTAL_MAP_DIR = Join-Path $publish "Maps"
$env:CRYSTAL_ATLAS_DIR = Join-Path $root "Build\assetcompile\all"
$env:CRYSTAL_MAP = "nn0.Map"
$env:CRYSTAL_OUT = Join-Path $root "Unity\Build\net-fishing.png"
& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.NetProbe.RunEdge -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[netprobe\]" | Select-Object -Last 1).Line
Write-Host "  fishing exit=$($code): $line"

if (-not $server.HasExited) { Stop-Process $server -Force }
Copy-Item $setupBak $setup -Force
if ($code -ne 0 -or $line -notmatch "edge ok") { Write-Host "FAIL: fishing probe failed"; exit 1 }
Write-Host "PASS: fishing probe ok"
exit 0
