# touchdiag.ps1 (X-1 v2): 模拟器触摸注入诊断——定位「adb 触摸注入是否到达 Unity + 坐标映射 + HUD 命中区镜像」。
# v1 负例断言（物理 x>1280 应被丢弃）实证失败：注入物理 x=2200 仍 touch=1 到达 Unity。
# v2 依据 9 点实验重写：变换式 raw=(dx*1280/2400, 720-dy*720/1080)（x 缩放 0.5333，y 翻转+缩放 0.6667），
# 仅 raw_x>=1280 或 raw_y=0（显示 dx=2400 或 dy=1080 精确边缘）被 Unity 丢弃。
# 起 Server + 复用/装 APK + 启动进图 → 正例同点 swipe（显示 1200,540→raw 640,360）断言 n>0 + raw 匹配 →
# 负例同点 swipe（显示 2400,540→raw 1280,360 边缘丢弃）断言 touch=0 → 命中报告打印各 HUD 按钮
# 「命中区注入点 vs 渲染位置」并标 y 镜像（X-1 根因：渲染=左上原点、触摸=左下原点，bag/hud hit test 未翻转）。
# 用法：Build/touchdiag.ps1 [-NoBuild] [-KeepEmulator] [-LoginId x] [-LoginPw y]
# 验证：exit 0 且输出 positive n=N>0 raw=(640,360)、negative touch=0、hit-rect 摘要。
param(
    [string]$LoginId = "pcplayer",
    [string]$LoginPw = "pcplayer",
    [switch]$NoBuild,       # 复用现有 APK（源码未改的快速重验；默认自动判定，同 androidverify）
    [switch]$KeepEmulator   # 保留模拟器常驻：下一轮跳过冷启动（省 1-3 分钟）
)
# EAP 用 Continue 而非 Stop：PS 5.1 下原生命令（adb）写 stderr（进度如 "1 file pushed"）在 Stop 下
# 会转成终止性 NativeCommandError 中断脚本；所有失败判定均为显式 exit 检查。
$ErrorActionPreference = "Continue"
# 输出编码 UTF-8：PS 5.1 默认控制台代码页（GBK）写 stdout，中文 Write-Host 经管道/重定向捕获时乱码。
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$sdk = Join-Path $env:LOCALAPPDATA "Android\Sdk"
$adb = Join-Path $sdk "platform-tools\adb.exe"
$emulator = Join-Path $sdk "emulator\emulator.exe"
$avd = "Medium_Phone_API_36.1"
$pkg = "com.crystal.mir2"
$activity = "com.unity3d.player.UnityPlayerGameActivity"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$unityProj = Join-Path $root "Unity"
$apk = Join-Path $unityProj "Build\Android\crystal.apk"
$buildLog = Join-Path $unityProj "Build\touchdiag-build.log"
$publish = Join-Path $root "Build\Server\publish"
$serverExe = Join-Path $publish "Server.exe"
$mapFile = Join-Path $publish "Maps\nn0.map"
$compiler = Join-Path $root "tools\AssetCompiler\bin\Release\net8.0\AssetCompiler.exe"
$atlasSrc = Join-Path $root "Build\assetcompile\map"
$mapAtlas = Join-Path $root "Build\android-res\mapAtlas"
$deviceFiles = "/sdcard/Android/data/$pkg/files"
$allAtlas = Join-Path $root "Build\assetcompile\all"
$atlasUi = Join-Path $root "Build\android-res\atlas-ui"
$coordCache = Join-Path $root "Build\android-res\spawn-coord.txt"
$radius = 60
$port = 7000

# ===== 坐标映射（X-1 v2 实证，9 点验证）=====
# 显示系（adb input 注入 + 截图）2400x1080，y 向下（0=顶）；backbuffer 系（Unity touch.position）1280x720，y 向上（0=底）。
# DisplayToRaw(dx,dy) = (dx*1280/2400, 720 - dy*720/1080)；RawToDisplay(bx,by) = (bx*2400/1280, (720-by)*1080/720)。
# 丢弃边界：raw_x>=1280（dx>=2400）或 raw_y=0（dy=1080）——正例用显示 (1200,540)->raw (640,360) 空闲区，
# 负例用显示 (2400,540)->raw (1280,360) 触发真丢弃（v1 的"物理 x>1280 被丢弃"假设为误判：2200 仍可达）。
$posSwipe = @(1200, 540)     # 正例：显示坐标，映射 raw=(640,360) 屏幕中央空闲区（非任何 HUD 按钮）
$negSwipe = @(2400, 540)     # 负例：显示 x=2400 -> raw_x=1280 精确右缘，Unity 应整体跳过（touch=0）

# HUD 按钮布局（源码 MobileBag/MobileHud 常量，backbuffer 触摸系 y 向上）：
# bag 按钮 rect (1118,140,72,54) 中心 (1154,167)；攻击圆心 (1190,560) r60。渲染用左上原点 -> 显示 y 与触摸 y 镜像。
# 命中区注入点 = RawToDisplay(编码 rect)（让当前代码 hit test 触发）；渲染位置 = 左上原点布局直接放大。
$hudZones = @(
    @{ Name = "bag";    Rx = 1118; Ry = 140; Rw = 72; Rh = 54 },
    @{ Name = "attack"; Cx = 1190; Cy = 560; Rr = 60 }
)
# 渲染（显示系，左上原点）：bag 显示 rect (2096,210,135,81) 中心 (2163,250)；攻击显示圆心 (2231,840) r112。
$hudRender = @{
    "bag"    = @(2096, 210, 2163, 250);  # 显示 rect 左上 + 中心
    "attack" = @(2231, 840)              # 显示圆心
}
function DisplayToRaw([int]$dx, [int]$dy) {
    return @([int]($dx * 1280 / 2400), 720 - [int]($dy * 720 / 1080))
}
function RawToDisplay([int]$bx, [int]$by) {
    return @([int]($bx * 2400 / 1280), [int]((720 - $by) * 1080 / 720))
}
# 命中分类（backbuffer 触摸系，按源码编码 rect）：返回 "bag-button"/"attack-button"/"free"
function Get-HitZone([int]$rx, [int]$ry) {
    if ($rx -ge 1118 -and $rx -le 1190 -and $ry -ge 140 -and $ry -le 194) { return "bag-button" }
    if ([math]::Sqrt(($rx - 1190) * ($rx - 1190) + ($ry - 560) * ($ry - 560)) -le 60) { return "attack-button" }
    return "free"
}

$script:stepIdx = 0
function Write-Step([string]$name) {
    $script:stepIdx++
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] [$($script:stepIdx)] $name"
}
function Wait-Tick([int]$elapsed, [string]$what, [int]$total) {
    if ($elapsed -gt 0 -and ($elapsed % 15 -eq 0)) {
        Write-Host "    ... $what ... ${elapsed}s / ${total}s"
    }
}
function Test-Port([int]$p) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect("127.0.0.1", $p); $c.Close(); return $true } catch { $c.Close(); return $false }
}
function AdbOut([string[]]$a) { (& $adb @a 2>&1) | Out-String }
function Wait-EmulatorBoot {
    & $adb wait-for-device | Out-Null
    $deadline = (Get-Date).AddSeconds(180)
    $t = 0
    while ((Get-Date) -lt $deadline) {
        $boot = (AdbOut @("shell", "getprop sys.boot_completed")).Trim()
        if ($boot -eq "1") { Write-Host "emulator booted"; return $true }
        Start-Sleep -Seconds 5
        $t += 5
        Wait-Tick $t "booting emulator" 180
    }
    return $false
}
function Get-MobileLog([string]$pattern) {
    $out = & $adb logcat -d -s Unity 2>&1
    $line = ($out | Select-String -Pattern $pattern | Select-Object -Last 1)
    if ($null -eq $line) { return "" }
    return $line.ToString()
}
function Invoke-Subset([string]$center) {
    & $compiler subset --map $mapFile --center $center --radius $radius --atlas $atlasSrc --out $mapAtlas 2>&1 | Out-Host
    return $LASTEXITCODE -eq 0
}
function Push-Device([string]$local, [string]$remote) {
    function Test-UpToDate([string]$lp, [string]$rp) {
        $sz = (AdbOut @("shell", "stat", "-c", "%s", $rp)).Trim()
        return ($sz -eq "$((Get-Item $lp).Length)")
    }
    if (Test-Path $local -PathType Leaf) {
        if (Test-UpToDate $local $remote) { return $true }
        $r = AdbOut @("push", $local, $remote)
        if ($r -notmatch "1 file pushed|pushed") { Write-Host "  push failed ($local): $r"; return $false }
        return $true
    }
    foreach ($f in (Get-ChildItem -Recurse -File $local)) {
        $rel = $f.FullName.Substring((Resolve-Path $local).Path.Length).TrimStart('\', '/').Replace('\', '/')
        $target = "$remote/$rel"
        if (Test-UpToDate $f.FullName $target) { continue }
        AdbOut @("shell", "mkdir", "-p", ($target | Split-Path)) | Out-Null
        $r = AdbOut @("push", $f.FullName, $target)
        if ($r -notmatch "1 file pushed|pushed") {
            Write-Host "  adb push failed $rel ($r), try run-as"
            $pkgPath = $target.Replace("/sdcard/Android/data/$pkg/files", "$pkg/files")
            AdbOut @("shell", "run-as", $pkg, "mkdir", "-p", ($pkgPath | Split-Path)) | Out-Null
            AdbOut @("shell", "run-as", $pkg, "cp", "/sdcard/Android/data/$pkg/files/$rel", $pkgPath) | Out-Null
        }
    }
    return $true
}
function Ensure-AtlasUi {
    if (Test-Path $atlasUi) { return $true }
    if (-not (Test-Path $allAtlas)) { Write-Host "FAIL: $allAtlas missing"; return $false }
    New-Item -ItemType Directory -Path $atlasUi -Force | Out-Null
    foreach ($lib in @("Prguse","Prguse2","Items","Stateitem","Title","UI")) {
        $json = Join-Path $allAtlas "$lib.json"
        if (-not (Test-Path $json)) { Write-Host "FAIL: atlas lib missing $json"; return $false }
        Copy-Item $json $atlasUi
        Get-ChildItem (Join-Path $allAtlas "$lib`_p*.png") -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item $_.FullName $atlasUi }
    }
    return $true
}

if (-not (Test-Path $serverExe)) { Write-Host "FAIL: $serverExe missing"; exit 1 }
if (-not (Test-Path $compiler)) { Write-Host "FAIL: AssetCompiler missing ($compiler)"; exit 1 }
if (-not (Test-Path $mapFile)) { Write-Host "FAIL: $mapFile missing"; exit 1 }

# 1. 起服务器 + 起模拟器（后台并行）
Write-Step "start server + boot emulator"
$server = Start-Process -FilePath $serverExe -WorkingDirectory $publish -PassThru -WindowStyle Hidden
$emulatorProc = $null
$deviceList = AdbOut @("devices")
if ($deviceList -notmatch "$avd|emulator-\d+") {
    $emulatorProc = Start-Process -FilePath $emulator -ArgumentList @("-avd", $avd, "-no-snapshot-save", "-no-boot-anim", "-gpu", "swiftshader_indirect") -PassThru
    Write-Host "started emulator pid=$($emulatorProc.Id)"
} else {
    Write-Host "emulator already running"
}

# 2. 构建 APK（复用 androidverify 的 NoBuild 自动判定：Assets 下 C#/shader/场景 比 APK 新 → 重建）
Write-Step "build APK (reuse if fresh)"
if (-not $NoBuild -and (Test-Path $apk)) {
    $srcNewest = Get-ChildItem (Join-Path $unityProj "Assets") -Recurse -File -Include *.cs,*.asmdef,*.shader,*.unity | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $psNewest = Get-ChildItem (Join-Path $unityProj "ProjectSettings") -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $apkTime = (Get-Item $apk).LastWriteTime
    if ($srcNewest.LastWriteTime -le $apkTime -and $psNewest.LastWriteTime -le $apkTime) {
        Write-Host "  NoBuild(auto): Assets unchanged since $apk"
        $NoBuild = $true
    }
}
if (-not $NoBuild) {
$env:CRYSTAL_APK_OUT = $apk
$env:CRYSTAL_NET_HOST = "10.0.2.2"; $env:CRYSTAL_NET_PORT = "$port"
$env:CRYSTAL_LOGIN_ID = $LoginId; $env:CRYSTAL_LOGIN_PW = $LoginPw
& $unity -batchmode -projectPath $unityProj -executeMethod Crystal.Rendering.Editor.BuildAndroid.Run -quit -logFile $buildLog | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $buildLog -Pattern "\[build-android\] OK |\[build-android\] FAIL|\[build-android\] exception" | Select-Object -Last 1).Line
Write-Host "  build exit=$code : $line"
if (($code -ne 0) -or ($line -notmatch "\[build-android\] OK")) {
    if (-not $server.HasExited) { Stop-Process $server -Force }
    Write-Host "FAIL: APK build"
    exit 1
}
} else {
    Write-Host "  NoBuild: reuse $apk"
    if (-not (Test-Path $apk)) { Write-Host "FAIL: -NoBuild but $apk missing"; exit 1 }
}

# 3. 资源裁剪：优先出生坐标缓存，无则地图中心（诊断只需进图，缺远端 tile 不影响触摸链路）
Write-Step "subset crop (cached spawn coord or map center)"
$coord = ""
if (Test-Path $coordCache) {
    $cached = (Get-Content $coordCache -Raw).Trim()
    if ($cached -match "^(\d+),(\d+)$") { $coord = $cached; Write-Host "  cached spawn coord=$coord" }
}
if ([string]::IsNullOrEmpty($coord)) { $coord = "350,350"; Write-Host "  no cache, use map center $coord" }
if (-not (Invoke-Subset $coord)) { Write-Host "FAIL: subset crop $coord"; exit 1 }

# 4. 等服务器端口 + 等模拟器 boot
Write-Step "wait server port + emulator boot"
$deadline = (Get-Date).AddSeconds(180); $ready = $false; $t = 0
while ((Get-Date) -lt $deadline) {
    if (Test-Port $port) { $ready = $true; break }
    Start-Sleep -Seconds 5
    $t += 5
    Wait-Tick $t "waiting server port $port" 180
}
if (-not $ready) { Write-Host "FAIL: server did not open port $port"; exit 1 }
Write-Host "server ready on port $port"
if (-not (Wait-EmulatorBoot)) { Write-Host "FAIL: emulator boot timeout"; exit 1 }

# 5. 安装 APK + push 资源
Write-Step "install APK + push resources"
$inst = AdbOut @("install", "-r", $apk)
Write-Host "  install: $($inst.Trim())"
if ($inst -notmatch "Success") { Write-Host "FAIL: adb install"; exit 1 }
& $adb shell mkdir -p "$deviceFiles/Maps" "$deviceFiles/mapAtlas" 2>&1 | Out-Null
if (-not (Push-Device (Join-Path $publish "Maps\nn0.map") "$deviceFiles/Maps/nn0.map")) { Write-Host "FAIL: push map"; exit 1 }
if (-not (Push-Device $mapAtlas "$deviceFiles/mapAtlas")) { Write-Host "FAIL: push atlas"; exit 1 }
if (-not (Ensure-AtlasUi)) { Write-Host "FAIL: atlas-ui prepare"; exit 1 }
& $adb shell mkdir -p "$deviceFiles/atlas" 2>&1 | Out-Null
if (-not (Push-Device $atlasUi "$deviceFiles/atlas")) { Write-Host "FAIL: push ui atlas"; exit 1 }

# 6. 启动 → 等 render-ready（= InGame 首帧渲染完成，触摸链路就绪）。swiftshader 低帧率，放宽到 300s
Write-Step "launch + wait render-ready"
AdbOut @("shell", "am", "force-stop", $pkg) | Out-Null
& $adb logcat -c | Out-Null
AdbOut @("shell", "am", "start", "-n", "$pkg/$activity") | Out-Null
$deadline = (Get-Date).AddSeconds(300); $ready = $false; $t = 0
while ((Get-Date) -lt $deadline) {
    if ((Get-MobileLog "\[mobile\] render-ready") -ne "") { $ready = $true; break }
    if ((Get-MobileLog "\[mobile\] error") -ne "") { Write-Host "FAIL: mobile error"; exit 1 }
    Start-Sleep -Seconds 5
    $t += 5
    Wait-Tick $t "waiting render-ready" 300
}
if (-not $ready) { Write-Host "FAIL: no render-ready in logcat"; exit 1 }
$userLine = Get-MobileLog "\[mobile\] user@\d+,\d+"
Write-Host "  render-ready, user=$userLine"

# 7. 诊断阶段（核心）：正例显示 (1200,540)->raw (640,360) 断言 touch>0 + raw 匹配（注入+映射双验证）；
#    负例显示 (2400,540)->raw (1280,360) 精确右缘，Unity 应丢弃（touch=0）。同点 swipe 300ms 跨多帧必被捕获
#    （低帧率 tap 的 down-up 间隙落单帧内可能被整体跳过——v1 实证）。
function Read-TouchRaw {
    $d = Get-MobileLog "\[mobile\] touch-diag"
    if ($d -match "raw=\((\d+),(\d+)\)") { return @([int]$Matches[1], [int]$Matches[2]) }
    return $null
}
function Wait-TouchDiag([int]$timeoutSec) {
    $deadline = (Get-Date).AddSeconds($timeoutSec); $t = 0
    while ((Get-Date) -lt $deadline) {
        $r = Read-TouchRaw
        if ($null -ne $r) { return $r }
        Start-Sleep -Seconds 2
        $t += 2
        Wait-Tick $t "waiting touch-diag" $timeoutSec
    }
    return $null
}

Write-Step "positive swipe (display 1200,540 -> expect raw 640,360, touch>0)"
& $adb logcat -c | Out-Null
& $adb shell input swipe $posSwipe[0] $posSwipe[1] $posSwipe[0] $posSwipe[1] 300 2>&1 | Out-Null
$posRaw = Wait-TouchDiag 30
$posHit = if ($null -ne $posRaw) { Get-HitZone $posRaw[0] $posRaw[1] } else { "-" }
Write-Host "  positive: inject display ($($posSwipe[0]),$($posSwipe[1])) raw=$($posRaw -join ',') zone=$posHit (expect raw 640,360)"
if ($null -eq $posRaw) {
    Write-Host "FAIL: positive swipe no touch-diag — injection not reaching Unity"; exit 1
}
if ([math]::Abs($posRaw[0] - 640) -gt 2 -or [math]::Abs($posRaw[1] - 360) -gt 2) {
    Write-Host "FAIL: positive raw=$($posRaw -join ',') != expected (640,360) — coordinate mapping broken"; exit 1
}

Write-Step "negative swipe (display 2400,540 -> raw_x 1280, expect touch=0 edge drop)"
Start-Sleep -Seconds 3 # 留出 touch-diag 2s 节流窗口，避免正例残留线干扰负例判定
& $adb logcat -c | Out-Null
& $adb shell input swipe $negSwipe[0] $negSwipe[1] $negSwipe[0] $negSwipe[1] 300 2>&1 | Out-Null
$negRaw = Wait-TouchDiag 30
Write-Host "  negative: inject display ($($negSwipe[0]),$($negSwipe[1])) raw=$($negRaw -join ',') (expect none — edge drop)"
if ($null -ne $negRaw) {
    Write-Host "FAIL: negative swipe unexpectedly reached Unity (raw=$($negRaw -join ','))"; exit 1
}

# 8. 命中报告：每个 HUD 按钮打印「编码命中区注入点(显示)」vs「渲染位置(显示)」并标 y 镜像。
#    注入点 = RawToDisplay(编码触摸 rect)（让当前代码 hit test 触发）；渲染位置 = 左上原点布局放大。
#    X-1 根因：渲染=左上原点、触摸=左下原点，bag/hud hit test 未翻转 → 两列 y 镜像（编码命中区在镜像侧）。
Write-Step "hit-rect summary (inject-to-hit vs rendered, mirror flag)"
$zoneInject = @{
    "bag"    = (RawToDisplay 1154 167)   # 编码 rect 中心 -> 显示注入点
    "attack" = (RawToDisplay 1190 560)
}
foreach ($z in $hudZones) {
    $name = $z.Name
    $inj = $zoneInject[$name]
    $rend = $hudRender[$name]
    $mirror = ""
    if ($name -eq "bag")    { $mirror = if ([math]::Abs($inj[1] - $rend[3]) -gt 40) { "  <-- Y-MIRRORED (命中区在镜像侧，tap 可见按钮 miss)" } else { "" } }
    if ($name -eq "attack") { $mirror = if ([math]::Abs($inj[1] - $rend[1]) -gt 40) { "  <-- Y-MIRRORED (命中区在镜像侧，tap 可见按钮 miss)" } else { "" } }
    Write-Host "  [$name] inject-to-hit display=($($inj -join ',')) rendered display=($($rend -join ','))$mirror"
}
# 例：bag 编码 rect 中心 (1154,167) -> 注入显示 (2164,830)；渲染在显示 (2163,250)（右上）。y 镜像 => tap 可见按钮失手。

# 9. 清理 + 判定
AdbOut @("shell", "am", "force-stop", $pkg) | Out-Null
if (-not $server.HasExited) { Stop-Process $server -Force }
if (-not $KeepEmulator -and $null -ne $emulatorProc -and -not $emulatorProc.HasExited) { Stop-Process $emulatorProc -Force }
if ($KeepEmulator -and $null -ne $emulatorProc -and -not $emulatorProc.HasExited) { Write-Host "  keep emulator running (next run skips boot)" }

Write-Host "PASS: touch-injection diagnostic ok (positive raw=(640,360) n>0, negative touch=0 edge drop, mapping verified)"
exit 0
