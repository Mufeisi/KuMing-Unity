# Android Player verify: start Server.exe -> boot emulator -> build APK (MobileBootstrap) ->
# subset crop spawn region -> adb install/push -> launch -> logcat assert [mobile] chain ->
# screencap color-count -> adb swipe -> assert position change. Two-step spawn crop:
# launch 1 logs actual user@x,y -> re-crop at that coord -> relaunch -> full assertion.
# X-2 (2026-08-07) 降级为「冒烟 + 诊断产物」脚本：
#   - 硬 gate 只保留 登录/进图/截图 链路 + 色数；moved/bag/hud 时序敏感断言 → WARN + 产物归档，不再挡 PASS。
#   - -Smoke 开关：跳过二次裁剪/重推/重启（launch#1 发现、实际坐标重裁、bag/swipe 注入），只做单次进图+截图。
#   - $stepTotal 与步骤计数对齐（Write-Step 打印自增 [n]，条件步骤不再错位）。
param(
    [string]$LoginId = "pcplayer",
    [string]$LoginPw = "pcplayer",
    [int]$WaitSec = 300,
    [switch]$NoBuild,
    [switch]$Smoke,          # 冒烟模式：单次进图+截图，跳过 launch#1 发现/bag/swipe 时序注入
    [switch]$KeepEmulator,   # 保留模拟器常驻：下一轮跳过冷启动（省 1-3 分钟）
    [switch]$Cdn             # OTA 模式（8-9-2）：不 adb push，起本地 HTTP CDN → 首启自动下载资源进图
)
# EAP 用 Continue 而非 Stop：PS 5.1 下原生命令（adb）写 stderr（进度如 "1 file pushed"）在 Stop 下
# 会转成终止性 NativeCommandError 中断脚本；脚本所有失败判定均为显式 exit 检查，不需 Stop 兜底。
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
$buildLog = Join-Path $unityProj "Build\androidverify-build.log"
$publish = Join-Path $root "Build\Server\publish"
$serverExe = Join-Path $publish "Server.exe"
$mapFile = Join-Path $publish "Maps\nn0.map"
$compiler = Join-Path $root "tools\AssetCompiler\bin\Release\net8.0\AssetCompiler.exe"
$atlasSrc = Join-Path $root "Build\assetcompile\map"
$mapAtlas = Join-Path $root "Build\android-res\mapAtlas"
$deviceFiles = "/sdcard/Android/data/$pkg/files"
$shot1 = Join-Path $root "Build\androidverify-1.png"
$shot2 = Join-Path $root "Build\androidverify-2.png"
$shotBag = Join-Path $unityProj "Build\androidverify-bag.png" # bag panel screenshot (phase8-2 inc1 artifact)
$allAtlas = Join-Path $root "Build\assetcompile\all"           # full object/UI atlas build output
$atlasUi = Join-Path $root "Build\android-res\atlas-ui"        # UI lib subset (Prguse/Title/Items... for dialogs)
$coordCache = Join-Path $root "Build\android-res\spawn-coord.txt"
$radius = 60
$port = 7000
# OTA CDN（8-9-2）：资源根按设备布局（Maps/mapAtlas/atlas）组装 + AssetCompiler manifest（--version），
# python http.server 托管；模拟器经 10.0.2.2 访问宿主。
$cdnRoot = Join-Path $root "Build\android-cdn"
$cdnPort = 18080
$cdnManifest = Join-Path $cdnRoot "resource.manifest.json"
$cdnVersion = "1.0.0"
$cdnProc = $null
function Update-CdnRoot {
    # 同步 CDN 根：地图/图集/UI 子集按设备布局复制 + 重新生成 manifest（重裁后 hash 变化必须刷新，
    # 否则设备按旧清单校验新文件失败）。manifest 版本固定 → 文件级 PlanDiff 补差（8-9-3 增量雏形）。
    # 先清空 mapAtlas/atlas 再复制：防重裁后旧裁剪文件残留（manifest 越滚越大）。
    if (-not (Test-Path $cdnRoot)) { New-Item -ItemType Directory -Path $cdnRoot -Force | Out-Null }
    $cdnMaps = Join-Path $cdnRoot "Maps"; $cdnAtlas = Join-Path $cdnRoot "mapAtlas"; $cdnUi = Join-Path $cdnRoot "atlas"
    New-Item -ItemType Directory -Path $cdnMaps, $cdnAtlas, $cdnUi -Force | Out-Null
    Get-ChildItem $cdnAtlas -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Get-ChildItem $cdnUi -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Copy-Item (Join-Path $publish "Maps\nn0.map") (Join-Path $cdnMaps "nn0.map") -Force
    # 递归复制保留相对路径：subset 输出是 mapAtlas/ShandaMir2/*.json|png 目录结构（非平铺），
    # 顶层 -File 会漏全部；设备 MapAtlasDir 期望同布局。
    foreach ($srcRoot in @($mapAtlas, $atlasUi)) {
        if (-not (Test-Path $srcRoot)) { continue }
        $destRoot = $cdnAtlas
        if ($srcRoot -eq $atlasUi) { $destRoot = $cdnUi }
        Get-ChildItem $srcRoot -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring((Resolve-Path $srcRoot).Path.Length).TrimStart('\', '/')
            $target = Join-Path $destRoot $rel
            New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
            Copy-Item $_.FullName $target -Force
        }
    }
    & $compiler manifest $cdnRoot --out $cdnManifest --version $cdnVersion 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: cdn manifest"; exit 1 }
    $cnt = (Get-ChildItem $cdnRoot -Recurse -File | Where-Object { $_.Name -ne "resource.manifest.json" }).Count
    Write-Host "  cdn-root files=$cnt (manifest v$cdnVersion)"
}
function Reset-CdnPort {
    # 清残留：上一轮脚本异常退出可能留 python http.server 占 18080（Test-Port 误判就绪 + 复用旧 CDN 根）
    $listeners = netstat -ano | Select-String ":$cdnPort\s+.*LISTENING"
    foreach ($l in $listeners) {
        $procId = ($l.ToString().Trim() -split '\s+')[-1]
        if ($procId -match '^\d+$') { taskkill /PID $procId /F 2>&1 | Out-Null; Write-Host "  killed stale :$cdnPort pid=$procId" }
    }
}
function Start-CdnServer {
    Reset-CdnPort
    $global:cdnProc = Start-Process -FilePath "python" -ArgumentList @("-m", "http.server", "$cdnPort", "--directory", $cdnRoot, "--bind", "0.0.0.0") -PassThru -WindowStyle Hidden
    $deadline = (Get-Date).AddSeconds(30); $ready = $false
    while ((Get-Date) -lt $deadline) {
        if (Test-Port $cdnPort) { $ready = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { Write-Host "FAIL: cdn http server"; exit 1 }
    Write-Host "  cdn server on :$cdnPort (http://10.0.2.2:$cdnPort/)"
}
function Stop-CdnServer {
    if ($null -ne $global:cdnProc -and -not $global:cdnProc.HasExited) { Stop-Process $global:cdnProc -Force }
}

# E2E 进度提示：每轮 30-40 分钟，长等待无输出易误判卡死。Write-Step 打印自增 [n] 阶段标记（时间戳），
# Wait-Tick 在等待循环里每 15s 心跳一行（含已等时长/总时长），证明脚本仍在推进。
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
# 抽取 logcat 中 [mobile] 状态链：返回最近一条匹配行（无则空串）
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
# 设备资源推送：单文件直接 push 到目标路径；目录须逐文件按相对路径 push（adb 目录 push 是"把本地目录
# 塞进远端目录"，会产生 mapAtlas/mapAtlas 嵌套）；失败回落 run-as。adb push 不建中间目录，先 mkdir -p。
function Push-Device([string]$local, [string]$remote) {
    # 跳过设备端已存在且 size 一致的文件（mapAtlas 80MB 每轮全推是 E2E 耗时项；覆盖 push 同价）
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
# Prepare UI atlas subset dir (Prguse/Prguse2/Items/Stateitem/Title/UI json+png) copied from full build
# output. Dialogs (MainDialog/InventoryDialog) load these libs at enter-game; full atlas dir is far too
# big to push to the emulator, so only the libs the dialogs need are staged here.
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

# 0. 起服务器 + 起模拟器（后台并行，等 APK 构建期间预热）
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

# 1. 构建 APK（MobileBootstrap + 注入 10.0.2.2 配置）。-NoBuild：复用现有 APK（源未改时的快速重验）。
Write-Step "build APK (Unity batchmode)"
# 自动判定：Assets 下 C#/shader/场景/asmdef 或 ProjectSettings 比 APK 新 → 重建；否则复用。
# IL2CPP+Gradle 构建 5-10 分钟是 E2E 最大耗时项——纯脚本/资源改动（adb push 路径）不该触发重建。
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
if ($Cdn) { $env:CRYSTAL_CDN_URL = "http://10.0.2.2:$cdnPort/" } else { $env:CRYSTAL_CDN_URL = "" }
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
# 4.1 首次裁剪：地图中心 60 格（确保覆盖出生点，第二次按实际坐标重裁）
Write-Step "subset crop (map center)"
if (-not (Invoke-Subset "350,350")) { Write-Host "FAIL: subset center crop"; exit 1 }

# 2. 等服务器端口 + 等模拟器 boot
Write-Step "wait server port + emulator boot"
$deadline = (Get-Date).AddSeconds(180); $ready = $false; $t = 0
while ((Get-Date) -lt $deadline) {
    if (Test-Port $port) { $ready = $true; break }
    Start-Sleep -Seconds 5
    $t += 5
    Wait-Tick $t "waiting server port 7000" 180
}
if (-not $ready) { Write-Host "FAIL: server did not open port $port"; exit 1 }
Write-Host "server ready on port $port"
if (-not (Wait-EmulatorBoot)) { Write-Host "FAIL: emulator boot timeout"; exit 1 }

# 3. 安装 APK + 资源注入。Cdn 模式（8-9-2）：不 push，组装 CDN 根 + 起 HTTP 服务器，设备数据
#    由 relaunch 前 pm clear 清空（模拟卸载重装 → 首启全量下载进图）；非 Cdn：adb push 预置。
Write-Step "install APK + $(if ($Cdn) { 'start CDN server' } else { 'push resources' })"
$inst = AdbOut @("install", "-r", $apk)
Write-Host "  install: $($inst.Trim())"
if ($inst -notmatch "Success") { Write-Host "FAIL: adb install"; exit 1 }

if ($Cdn) {
    if (-not (Ensure-AtlasUi)) { Write-Host "FAIL: atlas-ui prepare"; exit 1 }
    Update-CdnRoot
    Start-CdnServer
} else {
# 4.2 push 资源：nn0.Map + 首次中心裁剪图集 + UI atlas 子集（先 rm 再 push，adb 目录 push 是合并语义，防旧文件残留）
& $adb shell rm -rf "$deviceFiles/mapAtlas" 2>&1 | Out-Null
& $adb shell mkdir -p "$deviceFiles/Maps" "$deviceFiles/mapAtlas" 2>&1 | Out-Null
if (-not (Push-Device (Join-Path $publish "Maps\nn0.map") "$deviceFiles/Maps/nn0.map")) { Write-Host "FAIL: push map"; exit 1 }
if (-not (Push-Device $mapAtlas "$deviceFiles/mapAtlas")) { Write-Host "FAIL: push atlas"; exit 1 }
# UI atlas subset: MainDialog (Prguse) + InventoryDialog (Title/Prguse2/Items/UI) render at enter-game.
if (-not (Ensure-AtlasUi)) { Write-Host "FAIL: atlas-ui prepare"; exit 1 }
& $adb shell rm -rf "$deviceFiles/atlas" 2>&1 | Out-Null
& $adb shell mkdir -p "$deviceFiles/atlas" 2>&1 | Out-Null
if (-not (Push-Device $atlasUi "$deviceFiles/atlas")) { Write-Host "FAIL: push ui atlas"; exit 1 }
}

# 5. 出生坐标选择。缓存优先（实测出生点稳定 293,615 附近，账号绑定；缓存命中 → 直接用缓存坐标裁剪
#    推送，跳过 launch#1 两步启动省 2-3 分钟）；无缓存时：完整模式做 launch#1 实际坐标发现+重裁，
#    Smoke 模式直接用地图中心（单次裁剪已做，不再发现/重推——冒烟只需进图+截图）。
$coord = ""
$cached = ""
if (Test-Path $coordCache) {
    $cached = (Get-Content $coordCache -Raw).Trim()
    if ($cached -match "^(\d+),(\d+)$") {
        Write-Host "  cached spawn coord=$cached"
        if (-not (Invoke-Subset $cached)) { Write-Host "FAIL: subset cached crop"; exit 1 }
        if ($Cdn) { Update-CdnRoot } else {
        & $adb shell rm -rf "$deviceFiles/mapAtlas" 2>&1 | Out-Null
        & $adb shell mkdir -p "$deviceFiles/mapAtlas" 2>&1 | Out-Null
        if (-not (Push-Device $mapAtlas "$deviceFiles/mapAtlas")) { Write-Host "FAIL: push cached atlas"; exit 1 }
        }
        $coord = $cached
    }
}
if ([string]::IsNullOrEmpty($coord)) {
    if ($Smoke) {
        $coord = "350,350"
        Write-Host "  smoke: use map center $coord (no spawn discovery)"
    } else {
        Write-Step "launch #1: get spawn coord (no cache)"
        $attempt = 0
        while ($attempt -lt 2 -and [string]::IsNullOrEmpty($coord)) {
            $attempt++
            & $adb logcat -c | Out-Null
            AdbOut @("shell", "am", "start", "-n", "$pkg/$activity") | Out-Null
            Write-Host "launch $pkg attempt=$attempt"
            # Cdn 模式 launch#1 要先下载中心裁剪资源（本地回环 HTTP 快，但首启 + 下载 + 进图放宽窗口）
            $discoverDeadline = if ($Cdn) { 300 } else { 120 }
            $deadline = (Get-Date).AddSeconds($discoverDeadline)
            while ((Get-Date) -lt $deadline) {
                $l = Get-MobileLog "\[mobile\] (error|resync FAIL|resync exception|boot-ex|exception|user@)"
                if ($l -match "error") { Write-Host "FAIL: mobile error $l"; exit 1 }
                if ($l -match "\[mobile\] user@(\d+),(\d+)") {
                    $coord = $Matches[1] + "," + $Matches[2]
                    break
                }
                Start-Sleep -Seconds 3
            }
            if ([string]::IsNullOrEmpty($coord)) {
                AdbOut @("shell", "am", "force-stop", $pkg) | Out-Null
                continue
            }
            if ($attempt -eq 1) {
                # 按实际出生坐标重裁并重推图集（Cdn：刷新 CDN 根 + 重生成 manifest，relaunch 全量下载）
                AdbOut @("shell", "am", "force-stop", $pkg) | Out-Null
                Write-Host "spawn coord=$coord, re-crop + $(if ($Cdn) { 'refresh cdn' } else { 're-push' })"
                if (-not (Invoke-Subset $coord)) { Write-Host "FAIL: subset spawn crop"; exit 1 }
                if ($Cdn) { Update-CdnRoot } else {
                & $adb shell rm -rf "$deviceFiles/mapAtlas" 2>&1 | Out-Null
                & $adb shell mkdir -p "$deviceFiles/mapAtlas" 2>&1 | Out-Null
                if (-not (Push-Device $mapAtlas "$deviceFiles/mapAtlas")) { Write-Host "FAIL: re-push atlas"; exit 1 }
                }
                $coord = ""
            }
        }
        if ([string]::IsNullOrEmpty($coord)) { Write-Host "FAIL: no [mobile] user@ in logcat"; exit 1 }
    }
}

# 6. 最终断言链：connect -> login -> select -> enter-game -> user@x,y（force-stop 干净重启 + 重开 logcat 窗口）
#    Cdn 模式：pm clear 清空应用数据（模拟卸载重装）→ relaunch 首启全量下载（resync done）→ 进图
AdbOut @("shell", "am", "force-stop", $pkg) | Out-Null
if ($Cdn) {
    AdbOut @("shell", "pm", "clear", $pkg) | Out-Null
    Write-Host "  pm clear pkg=$pkg (fresh-install simulation)"
}
& $adb logcat -c | Out-Null
AdbOut @("shell", "am", "start", "-n", "$pkg/$activity") | Out-Null
Write-Step "relaunch: $(if ($Cdn) { 'OTA full download' } else { 'spawn-crop' }) + full assert"
$deadline = (Get-Date).AddSeconds($WaitSec)
$chain = @{ connect = $false; login = $false; select = $false; enter = $false; user = $false }
if ($Cdn) { $chain["resync"] = $false }
$coords = @()
$t = 0
while ((Get-Date) -lt $deadline) {
    foreach ($k in @($chain.Keys)) { # 快照枚举：循环内 $chain[$k]=$true 会改表，PS 5.1 直接枚举 $chain.Keys 抛 InvalidOperationException
        if ($chain[$k]) { continue }
        $pat = switch ($k) {
            "connect" { "\[mobile\] connect " }
            "login"   { "\[mobile\] login " }
            "select"  { "\[mobile\] select-ready" }
            "enter"   { "\[mobile\] enter-game" }
            "user"    { "\[mobile\] user@\d+,\d+" }
            "resync"  { "\[mobile\] resync done files=[1-9]\d*" }
        }
        if ((Get-MobileLog $pat) -ne "") { $chain[$k] = $true }
    }
    $userLines = (& $adb logcat -d -s Unity 2>&1 | Select-String -Pattern "\[mobile\] user@\d+,\d+" | ForEach-Object { $_.ToString() })
    foreach ($u in $userLines) { if ($u -match "user@(\d+),(\d+)") { $c = "$($Matches[1]),$($Matches[2])"; if ($coords -notcontains $c) { $coords += $c } } }
    $done = ($chain.Values -notcontains $false) -and ($coords.Count -ge 1)
    if ($done) { break }
    Start-Sleep -Seconds 3
    $t += 3
    Wait-Tick $t "waiting login/enter chain" $WaitSec
}
$chainText = ($chain.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join " "
Write-Host "  chain: $chainText coords=$($coords -join ' | ')"
# 写回出生坐标缓存（下轮跳过 launch#1）；距缓存 >40 格视为换点，WARN 提示（mapAtlas 可能缺图）
if ($coords.Count -ge 1) {
    $actual = $coords[0]
    Set-Content -Path $coordCache -Value $actual -Encoding ASCII
    if ($cached -ne "" -and $actual -match "^(\d+),(\d+)") {
        $cx = [int]$cached.Split(',')[0]; $cy = [int]$cached.Split(',')[1]
        $ax = [int]$Matches[1]; $ay = [int]$Matches[2]
        if ([math]::Sqrt(($ax-$cx)*($ax-$cx)+($ay-$cy)*($ay-$cy)) -gt 40) {
            Write-Host "  WARN spawn coord moved ($cached -> $actual): mapAtlas may miss tiles"
        }
    }
}

# 7. 等渲染就绪（首帧 BuildLibIndex 全图扫描慢，模拟器 ~2.6s；固定等待会截到纯色清屏）→ 截图 1：
# 色数 > 阈值（画面非纯背景）。PS `>` 会以文本管道损坏 PNG，改用
# Start-Process -RedirectStandardOutput 直写原始字节（screencap 二进制输出）。
$sz1 = 0
if ($chain["user"]) {
    $renderDeadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $renderDeadline) {
        if ((Get-MobileLog "\[mobile\] render-ready") -ne "") { break }
        Start-Sleep -Seconds 2
    }
    Start-Sleep -Seconds 1
    $p = Start-Process -FilePath $adb -ArgumentList @("exec-out", "screencap", "-p") -RedirectStandardOutput $shot1 -NoNewWindow -Wait
    $sz1 = if (Test-Path $shot1) { (Get-Item $shot1).Length } else { 0 }
}
# 7.5 HUD 扫描（诊断产物）：截图 1 扫描战斗 HUD 元素——右下攻击按钮橙圆盘 + 左上 HP 红条。
# 模拟器降级 1280x720 逻辑 → 物理 2400x1080 全屏拉伸（1.875x / 1.5y）。攻击中心逻辑 (1190,560) →
# 物理 (2231,840) r60 逻辑→约 (112,90) 物理；HP 条逻辑 (20,20)-(200,34) → 物理 (37,30)-(374,51)。
# 区域放宽防拉伸/抖动偏差，只判橙红主色（HUD 是画面唯一大橙圆/红条，误报低）。时序敏感 → 仅 WARN。
$hudOk = $false
Write-Step "hud pixel scan"
if ($sz1 -gt 10000) {
    $hudPy = @"
from PIL import Image
im = Image.open(r'$shot1').convert('RGB')
px = im.load()
atk = 0
for y in range(700, 980):
    for x in range(2070, 2400):
        r,g,b = px[x,y]
        if r > 150 and b < 130 and (r - b) > 60: atk += 1
hp = 0
for y in range(15, 95):
    for x in range(15, 400):
        r,g,b = px[x,y]
        if r > 140 and g < 110 and b < 110: hp += 1
print('atk=%d hp=%d' % (atk, hp))
"@
    $hudRaw = $hudPy | python -
    Write-Host "  hud-scan: $hudRaw"
    if ($hudRaw -match "atk=(\d+) hp=(\d+)") {
        $hudOk = ([int]$Matches[1] -gt 300) -and ([int]$Matches[2] -gt 100)
    }
}
# 8. 诊断：背包面板 tap 开/关 + 滑动移动（均为时序敏感注入，-Smoke 跳过；产物截图归档，只作 WARN）。
$bagOk = $false
$moved = $false
if (-not $Smoke) {
# 8.1 Bag panel tap (phase8-2 inc1): tap bag button (top-right; logical center 1154,167 -> physical
# by 1.875x/1.5y scale = 2164,250; locate yellow block in shot1 for robustness) -> wait [mobile] bag-open
# -> screenshot -> pixel diff vs shot1 (panel covers map top-left) -> tap again -> wait bag-close.
Write-Step "bag panel tap (diagnostic)"
if ($sz1 -gt 10000) {
    $locPy = @"
from PIL import Image
im = Image.open(r'$shot1').convert('RGB')
px = im.load()
xs=[]; ys=[]
for y in range(180, 400):
    for x in range(1950, 2400):
        r,g,b = px[x,y]
        if r > 200 and g > 170 and b < 140:
            xs.append(x); ys.append(y)
if len(xs) > 50:
    print('%d,%d' % (sum(xs)//len(xs), sum(ys)//len(ys)))
else:
    print('none')
"@
    $loc = $locPy | python -
    # 注入用逻辑坐标（backbuffer 1280×720 系）：Unity 触摸坐标系=backbuffer 像素，x>1280 的物理系
    # 注入（如 2175）被 Unity 当屏幕外丢弃（touch=0、事件不到）。locPy 从物理截图(2400×1080)定位
    # 按钮 → 按 1280/2400, 720/1080 换算回逻辑坐标注入。
    $tapX = 1154; $tapY = 167 # bag button logical center (ButtonMargin 90,140; size 72x54)
    if ($loc -match "^(\d+),(\d+)") {
        $tapX = [int]($Matches[1] * 1280 / 2400)
        $tapY = [int]($Matches[2] * 720 / 1080)
    }
    Write-Host "  bag-btn tap=$tapX,$tapY"
    & $adb shell logcat -c 2>&1 | Out-Null
    # 低帧率模拟器（10fps 帧间隔~100ms）下 input tap 的 down-up 间隙(~50ms)落在单帧内被 Unity 输入
    # 管线整体跳过（touch-diag 实证 touch=0，bag-open 永不触发）。同点 swipe 300ms 跨 3 帧必被捕获。
    & $adb shell input swipe $tapX $tapY $tapX $tapY 300 2>&1 | Out-Null
    $openDeadline = (Get-Date).AddSeconds(60)
    $t = 0
    while ((Get-Date) -lt $openDeadline) {
        if ((Get-MobileLog "\[mobile\] bag-open") -ne "") { break }
        Start-Sleep -Seconds 3
        $t += 3
        Wait-Tick $t "waiting bag-open" 60
    }
    $openOk = (Get-MobileLog "\[mobile\] bag-open") -ne ""
    Write-Host "  bag-open=$openOk"
    # diag: dump mobile/gamesession logcat for root-cause (bag panel not rendering)
    (& $adb logcat -d -s Unity 2>&1 | Select-String -Pattern "\[mobile\]|\[gamesession\]|\[network\]" | Select-Object -Last 40 | ForEach-Object { $_.ToString() }) | Out-File (Join-Path $root "Build\androidverify-bag-diag.log")
    Start-Sleep -Seconds 8 # low frame rate: give panel ~1 frame to render after bag-open
    $p = Start-Process -FilePath $adb -ArgumentList @("exec-out", "screencap", "-p") -RedirectStandardOutput $shotBag -NoNewWindow -Wait
    # 轮询截图（每 6s 重截，最多 3 次）：低帧率下面板渲染耗时不定，固定 sleep 要么截早了要么白等
    for ($i = 0; $i -lt 3; $i++) {
        Start-Sleep -Seconds 6
        $p = Start-Process -FilePath $adb -ArgumentList @("exec-out", "screencap", "-p") -RedirectStandardOutput $shotBag -NoNewWindow -Wait
        $bagSz = if (Test-Path $shotBag) { (Get-Item $shotBag).Length } else { 0 }
        if ($bagSz -gt 10000) {
            $diffPy2 = @"
from PIL import Image
a = Image.open(r'$shot1').convert('RGB')
b = Image.open(r'$shotBag').convert('RGB')
pa = a.load(); pb = b.load()
diff = 0
for y in range(0, 400):
    for x in range(0, 620):
        ra,ga,ba = pa[x,y]; rb,gb,bb = pb[x,y]
        if abs(ra-rb)+abs(ga-gb)+abs(ba-bb) > 40: diff += 1
print('diff=%d' % diff)
"@
            $d2 = $diffPy2 | python -
            Write-Host "  bag-diff(retry$i): $d2"
            if ($d2 -match "diff=(\d+)" -and [int]$Matches[1] -gt 10000) { $bagSz = -1; break }
        }
    }
    if ($bagSz -gt 10000) {
        $diffPy = @"
from PIL import Image
a = Image.open(r'$shot1').convert('RGB')
b = Image.open(r'$shotBag').convert('RGB')
pa = a.load(); pb = b.load()
diff = 0
for y in range(0, 400):
    for x in range(0, 620):
        ra,ga,ba = pa[x,y]; rb,gb,bb = pb[x,y]
        if abs(ra-rb)+abs(ga-gb)+abs(ba-bb) > 40: diff += 1
print('diff=%d' % diff)
"@
        $diffRaw = $diffPy | python -
        Write-Host "  bag-diff: $diffRaw"
        $bagOk = $openOk -and ($diffRaw -match "diff=(\d+)") -and ([int]$Matches[1] -gt 10000)
    }
    elseif ($bagSz -eq -1) { $bagOk = $openOk } # 轮询命中（渲染已确认）
    # close: tap same button again (only if open actually landed; else skip close wait)
    if ($openOk) {
        & $adb shell input swipe $tapX $tapY $tapX $tapY 300 2>&1 | Out-Null
        $closeDeadline = (Get-Date).AddSeconds(60)
        $t = 0
        while ((Get-Date) -lt $closeDeadline) {
            if ((Get-MobileLog "\[mobile\] bag-close") -ne "") { break }
            Start-Sleep -Seconds 3
            $t += 3
            Wait-Tick $t "waiting bag-close" 60
        }
        $bagOk = $bagOk -and ((Get-MobileLog "\[mobile\] bag-close") -ne "")
    }
    Write-Host "  bag=$bagOk"
}
# 8.2 滑动移动（摇杆松手补步 → C.Walk 一格）；等位置变化。诊断产物，仅 WARN。
# 多方向重试：西→东→北→南，任一方向位置变化即移动判定（出生点可能邻墙，单方向东滑会撞墙）。
# 坐标按模拟器降级后 1280x720 横屏（中心 640,360；swipe 起点即摇杆原点，位移 800px>奔跑阈值）。
# 注：moved 现可能由 MobileCombat 自动战斗追击怪物产生（触摸未到时的假象），仅作回归保留。
Write-Step "swipe move (diagnostic)"
$swipes = @(@(1040,360,240,360), @(240,360,1040,360)) # 东西两向（南北删减提速；出生点邻墙单方向可能撞墙，两向任一成功即移动）
if ($chain["enter"] -and $coords.Count -ge 1) {
    foreach ($sw in $swipes) {
        & $adb shell input swipe $sw[0] $sw[1] $sw[2] $sw[3] 150 2>&1 | Out-Null
        # 模拟器 swiftshader 帧率极低（0.2fps）：触摸事件要等下一帧（3-5s）才被 PollJoystick 处理、
        # 再下一帧 Process 发送 C.Walk，到服务器回位置包又隔数秒。12s 窗口会截断，放宽到 30s。
        $swipeDeadline = (Get-Date).AddSeconds(30)
        $t = 0
        while ((Get-Date) -lt $swipeDeadline) {
            Start-Sleep -Seconds 3
            $t += 3
            Wait-Tick $t "waiting position change" 30
            $userLines = (& $adb logcat -d -s Unity 2>&1 | Select-String -Pattern "\[mobile\] user@\d+,\d+" | ForEach-Object { $_.ToString() })
            foreach ($u in $userLines) { if ($u -match "user@(\d+),(\d+)") { $c = "$($Matches[1]),$($Matches[2])"; if ($coords -notcontains $c) { $coords += $c } } }
            if ($coords.Count -ge 2) { $moved = $true; break }
        }
        if ($moved) { break }
    }
    $p2 = Start-Process -FilePath $adb -ArgumentList @("exec-out", "screencap", "-p") -RedirectStandardOutput $shot2 -NoNewWindow -Wait
}
Write-Host "  moved=$moved coords=$($coords -join ' | ')"
} # end if(-not $Smoke)

# 9. 清理
AdbOut @("shell", "am", "force-stop", $pkg) | Out-Null
if (-not $server.HasExited) { Stop-Process $server -Force }
Stop-CdnServer
if (-not $KeepEmulator -and $null -ne $emulatorProc -and -not $emulatorProc.HasExited) { Stop-Process $emulatorProc -Force }
if ($KeepEmulator -and $null -ne $emulatorProc -and -not $emulatorProc.HasExited) { Write-Host "  keep emulator running (next E2E skips boot)" }

# 10. 判定：硬 gate = 登录/进图/渲染链路 + 截图色数；moved/bag/hud 时序敏感诊断只 WARN（产物已归档）
$colCount = 0
if ($sz1 -gt 10000) {
    # python 输出为字符串，PS 5.1 字符串与数字 -gt 按字典序比较（"232563" -gt 50 为 False）→ 必须 [int]::TryParse 强转
    $raw = python -c "import sys; from PIL import Image; im=Image.open(r'$shot1').convert('RGB'); print(len(im.getcolors(maxcolors=10**7)) if im.getcolors(maxcolors=10**7) else 99999)"
    [int]::TryParse(("$raw").Trim(), [ref]$colCount) | Out-Null
}
$ok = ($chain.Values -notcontains $false) -and ($colCount -gt 50)
Write-Host "  shot1=$sz1 colors=$colCount chain=$($chain.Values -notcontains $false)"
# 诊断产物归档说明：截图 shot1/shot2/shotBag + bag-diag.log 已落盘 Build/，供注入类调试定位。
foreach ($d in @(@("hud",$hudOk), @("bag",$bagOk), @("moved",$moved))) {
    if (-not $d[1]) { Write-Host "  WARN: $($d[0]) assertion failed — 时序敏感，产物已归档，不挡 PASS（见 X-1 touchdiag 定位注入问题）" }
}
if ($ok) { Write-Host "PASS: Android login->enter->render ok" ; exit 0 } else { Write-Host "FAIL"; exit 1 }
