# Phase-6 edge verification: start publish Server.exe (single session) -> run Edge sub-mode probes -> assert -> stop server.
# Edge probe = NetProbe.Mode.Edge (CRYSTAL_EDGE selects sub-mode, log asserts [netprobe] edge ok), covering
#   del(delete char) / run(run) / split(split stack) / revive(revive) / recon(disconnect-reconnect) / autopath(pathfind+AutoRun) / magic(cast spell).
#   magic needs Settings.TestServer=True (@giveskill gate); split needs it too (@make gate).
#   Setup.ini is backed up and flipped BEFORE server start (static read at boot), restored after.
param(
    [string]$Edge = "del,run,split,revive,recon,autopath,magic",
    [string]$LoginId = "probeedge1",
    [string]$LoginPw = "probeedge1",
    [string]$CharName = "probeedge1",
    # Haste: 便宜真施放（cost=5MP @lvl1，Warrior base MP=11），无目标无道具依赖，Cast=true；
    # Lightning 需 45MP，fresh level-1 Warrior 法力不足 -> cost>MP 早退不发 S.Magic。
    [string]$Spell = "Haste",
    [int]$TimeoutMs = 60000
)
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$publish = Join-Path $root "Build\Server\publish"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$port = 7000
$setup = Join-Path $publish "Configs\Setup.ini"
$setupBak = Join-Path $publish "Configs\Setup.ini.bak-edge"
$setupFlipped = $false

function Test-Port([int]$p) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect("127.0.0.1", $p); $c.Close(); return $true } catch { $c.Close(); return $false }
}

function Flip-TestServer([bool]$on) {
    if (-not (Test-Path $setup)) { Write-Host "WARN: Setup.ini missing at $setup"; return }
    if ($on -and -not $setupFlipped) {
        Copy-Item $setup $setupBak -Force
        (Get-Content $setup) -replace '^TestServer=False', 'TestServer=True' | Set-Content $setup -Encoding ASCII
        $script:setupFlipped = $true
        Write-Host "Setup.ini: TestServer=True (backed up)"
    } elseif (-not $on -and $setupFlipped) {
        Copy-Item $setupBak $setup -Force
        $script:setupFlipped = $false
        Write-Host "Setup.ini restored"
    }
}

function Invoke-Edge([string]$name, [string]$lid, [string]$lpw, [string]$cname) {
    $log = Join-Path $root "Unity\Build\net-edge-$name.log"
    $env:CRYSTAL_NET_HOST = "127.0.0.1"
    $env:CRYSTAL_NET_PORT = "$port"
    $env:CRYSTAL_LOGIN_ID = $lid
    $env:CRYSTAL_LOGIN_PW = $lpw
    $env:CRYSTAL_CHAR_NAME = $cname
    $env:CRYSTAL_NET_TIMEOUT = "$TimeoutMs"
    $env:CRYSTAL_EDGE = $name
    $env:CRYSTAL_EDGE_SPELL = $Spell
    $env:CRYSTAL_MAP_DIR = Join-Path $publish "Maps"
    $env:CRYSTAL_ATLAS_DIR = Join-Path $root "Build\assetcompile\all"
    $env:CRYSTAL_MAP = "nn0.Map"
    $env:CRYSTAL_OUT = Join-Path $root "Unity\Build\net-edge-$name.png"
    & $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.NetProbe.RunEdge -logFile $log | Out-Null
    $code = $LASTEXITCODE
    $line = (Select-String -Path $log -Pattern "\[netprobe\]" | Select-Object -Last 1).Line
    Write-Host "  edge=$name exit=$($code): $line"
    return ($code -eq 0) -and ($line -match "edge ok")
}

$edges = @($Edge.Split(','))
# TestServer is a static read once at server boot (Settings.cs:386); must flip before Start-Process if magic (giveskill) or split (make) is selected.
$needTestServer = ($edges -contains "magic") -or ($edges -contains "split")
if ($needTestServer) { Flip-TestServer $true }
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

$allOk = $true
foreach ($e in $edges) {
    $e = $e.Trim()
    if (-not $e) { continue }
    if ($e -eq "del") {
        # del needs a unique account AND unique char name (both <=15 chars, Shared Globals.Max*):
        #   soft-delete leaves the char name globally reserved (Envir.CharacterExists has no Deleted filter),
        #   so the shared account's char must use a name never created by del.
        $delId = "p$(Get-Date -Format 'yyyyMMddHHmmss')"
        $delChar = "c$(Get-Date -Format 'yyyyMMddHHmmss')"
        if (-not (Invoke-Edge $e $delId $LoginPw $delChar)) { $allOk = $false }
    } else {
        if (-not (Invoke-Edge $e $LoginId $LoginPw $CharName)) { $allOk = $false }
    }
}

if (-not $server.HasExited) { Stop-Process $server -Force }
Flip-TestServer $false
if (-not $allOk) { Write-Host "FAIL: one or more edge probes failed"; exit 1 }
Write-Host "PASS: all edge probes ok"
exit 0
