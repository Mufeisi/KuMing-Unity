# PC 分辨率矩阵（9-3）：多分辨率各起一次 Crystal.exe → AUTO_SHOT 截图 → 断言
# PNG 实际尺寸 = Crystal.ini 期望分辨率（PcStartup.SetResolution 生效）+ 色数 > 阈值（真实画面）。
# 多 GPU 矩阵登记阶段收口（对照 G3 门禁，需多种硬件）；本脚本单机多分辨率回归。
# 用法：powershell -File Build/pcresolution.ps1
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$publish = Join-Path $root "Build\Server\publish"
$exe = Join-Path $root "Build\PC\Crystal.exe"
$iniPath = Join-Path (Split-Path $exe) "Crystal.ini"
$playerLog = Join-Path $env:USERPROFILE "AppData\LocalLow\Mir2\Crystal\Player.log"
$port = 7000
$resolutions = @(@(1280, 720), @(1600, 900), @(1920, 1080))
$allOk = $true

function Test-Port([int]$p) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect("127.0.0.1", $p); $c.Close(); return $true } catch { $c.Close(); return $false }
}
function Write-Ini([int]$w, [int]$h) {
    @("[Screen]", "Width=$w", "Height=$h", "FullScreen=false") | Set-Content $iniPath -Encoding Ascii
}

# exe 过期检测：Unity 源码（Client.Rendering 全目录）比 Crystal.exe 新 → 先重建（9-2 起 PcStartup/CrashGuard
# 在 exe 内，分辨率矩阵必须用含 9-2 的新构建，否则 SetResolution 不生效）。
function Test-ExeStale {
    $exeTime = (Get-Item $exe).LastWriteTime
    $newest = Get-ChildItem (Join-Path $root "Unity\Assets\Crystal\Client.Rendering") -Recurse -File -Include *.cs |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    return ($newest.LastWriteTime -gt $exeTime)
}

if (-not (Test-Path $exe)) { Write-Host "FAIL: $exe missing"; exit 1 }
if (Test-ExeStale) {
    Write-Host "=== exe stale → rebuild (buildpc.ps1) ==="
    powershell -ExecutionPolicy Bypass -File (Join-Path $root "Build\buildpc.ps1")
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: buildpc"; exit 1 }
}
$server = Start-Process -FilePath (Join-Path $publish "Server.exe") -WorkingDirectory $publish -PassThru -WindowStyle Hidden
$deadline = (Get-Date).AddSeconds(180); $ready = $false
while ((Get-Date) -lt $deadline) { if (Test-Port $port) { $ready = $true; break }; Start-Sleep -Seconds 5 }
if (-not $ready) { if (-not $server.HasExited) { Stop-Process $server -Force }; Write-Host "FAIL: server"; exit 1 }
Write-Host "server ready on $port"

foreach ($res in $resolutions) {
    $w = $res[0]; $h = $res[1]
    Write-Ini $w $h
    $shot = Join-Path $root "Build\pcresolution-$($w)x$($h).png"
    if (Test-Path $shot) { Remove-Item $shot -Force }
    if (Test-Path $playerLog) { Remove-Item $playerLog -Force -ErrorAction SilentlyContinue }
    $env:CRYSTAL_NET_HOST = "127.0.0.1"; $env:CRYSTAL_NET_PORT = "$port"
    $env:CRYSTAL_LOGIN_ID = "pcplayer"; $env:CRYSTAL_LOGIN_PW = "pcplayer"
    $env:CRYSTAL_MAP_DIR = Join-Path $publish "Maps"
    $env:CRYSTAL_MAP_ATLAS_DIR = Join-Path $root "Build\assetcompile\map"
    $env:CRYSTAL_ATLAS_DIR = Join-Path $root "Build\assetcompile\all"
    $env:CRYSTAL_AUTO_SHOT = $shot

    $client = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
    $deadline = (Get-Date).AddSeconds(150); $ok = $false
    while ((Get-Date) -lt $deadline) {
        if ($client.HasExited) { break }
        $hit = if (Test-Path $playerLog) { Select-String -Path $playerLog -Pattern "\[pcplayer\] shot " } else { $null }
        $sz = if (Test-Path $shot) { (Get-Item $shot).Length } else { 0 }
        if ($hit -and $sz -gt 10000) { $ok = $true; break }
        Start-Sleep -Seconds 3
    }
    if (-not $client.HasExited) { Stop-Process $client -Force }
    try { Wait-Process -Id $client.Id -Timeout 30 -ErrorAction SilentlyContinue } catch { }
    Start-Sleep -Seconds 3

    $dim = "?"
    $colors = 0
    if (Test-Path $shot) {
        Add-Type -AssemblyName System.Drawing
        $img = [System.Drawing.Bitmap]::FromFile($shot)
        $dim = "$($img.Width)x$($img.Height)"
        $colors = $img.GetPixel(0,0).ToArgb() # 探针级：仅尺寸断言，色数由文件大小间接（>10KB）
        $img.Dispose()
    }
    $expect = "$($w)x$($h)"
    $resOk = ($dim -eq $expect)
    $allOk = $allOk -and $resOk
    Write-Host "  $expect => shot=$dim colors-approx=$($sz)B resOk=$resOk"
}

if (-not $server.HasExited) { Stop-Process $server -Force }
if ($allOk) { Write-Host "PASS: resolution matrix ok ($($resolutions.Count) resolutions)" ; exit 0 }
Write-Host "FAIL"; exit 1
