# Android Player verify: start Server.exe -> boot emulator -> build APK (MobileBootstrap) ->
# subset crop spawn region -> adb install/push -> launch -> logcat assert [mobile] chain ->
# screencap color-count -> adb swipe -> assert position change. Two-step spawn crop:
# launch 1 logs actual user@x,y -> re-crop at that coord -> relaunch -> full assertion.
param(
    [string]$LoginId = "pcplayer",
    [string]$LoginPw = "pcplayer",
    [int]$WaitSec = 300,
    [switch]$NoBuild
)
# EAP 用 Continue 而非 Stop：PS 5.1 下原生命令（adb）写 stderr（进度如 "1 file pushed"）在 Stop 下
# 会转成终止性 NativeCommandError 中断脚本；脚本所有失败判定均为显式 exit 检查，不需 Stop 兜底。
$ErrorActionPreference = "Continue"
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
$radius = 60
$port = 7000

function Test-Port([int]$p) {
    $c = New-Object System.Net.Sockets.TcpClient
    try { $c.Connect("127.0.0.1", $p); $c.Close(); return $true } catch { $c.Close(); return $false }
}
function AdbOut([string[]]$a) { (& $adb @a 2>&1) | Out-String }
function Wait-EmulatorBoot {
    & $adb wait-for-device | Out-Null
    $deadline = (Get-Date).AddSeconds(180)
    while ((Get-Date) -lt $deadline) {
        $boot = (AdbOut @("shell", "getprop sys.boot_completed")).Trim()
        if ($boot -eq "1") { Write-Host "emulator booted"; return $true }
        Start-Sleep -Seconds 5
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
    if (Test-Path $local -PathType Leaf) {
        $r = AdbOut @("push", $local, $remote)
        if ($r -notmatch "1 file pushed|pushed") { Write-Host "  push failed ($local): $r"; return $false }
        return $true
    }
    foreach ($f in (Get-ChildItem -Recurse -File $local)) {
        $rel = $f.FullName.Substring((Resolve-Path $local).Path.Length).TrimStart('\', '/').Replace('\', '/')
        $target = "$remote/$rel"
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

if (-not (Test-Path $serverExe)) { Write-Host "FAIL: $serverExe missing"; exit 1 }
if (-not (Test-Path $compiler)) { Write-Host "FAIL: AssetCompiler missing ($compiler)"; exit 1 }
if (-not (Test-Path $mapFile)) { Write-Host "FAIL: $mapFile missing"; exit 1 }

# 0. 起服务器 + 起模拟器（后台并行，等 APK 构建期间预热）
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
# 4.1 首次裁剪：地图中心 60 格（确保覆盖出生点，第二次按实际坐标重裁）
if (-not (Invoke-Subset "350,350")) { Write-Host "FAIL: subset center crop"; exit 1 }

# 2. 等服务器端口 + 等模拟器 boot
$deadline = (Get-Date).AddSeconds(180); $ready = $false
while ((Get-Date) -lt $deadline) { if (Test-Port $port) { $ready = $true; break }; Start-Sleep -Seconds 5 }
if (-not $ready) { Write-Host "FAIL: server did not open port $port"; exit 1 }
Write-Host "server ready on port $port"
if (-not (Wait-EmulatorBoot)) { Write-Host "FAIL: emulator boot timeout"; exit 1 }

# 3. 安装 APK
$inst = AdbOut @("install", "-r", $apk)
Write-Host "  install: $($inst.Trim())"
if ($inst -notmatch "Success") { Write-Host "FAIL: adb install"; exit 1 }

# 4.2 push 资源：nn0.Map + 首次中心裁剪图集（先 rm 再 push，adb 目录 push 是合并语义，防旧文件残留）
& $adb shell rm -rf "$deviceFiles/mapAtlas" 2>&1 | Out-Null
& $adb shell mkdir -p "$deviceFiles/Maps" "$deviceFiles/mapAtlas" 2>&1 | Out-Null
if (-not (Push-Device (Join-Path $publish "Maps\nn0.map") "$deviceFiles/Maps/nn0.map")) { Write-Host "FAIL: push map"; exit 1 }
if (-not (Push-Device $mapAtlas "$deviceFiles/mapAtlas")) { Write-Host "FAIL: push atlas"; exit 1 }

# 5. 首次启动：等 [mobile] user@x,y 拿实际出生坐标 → 重裁 → 重推 → 重启
$coord = ""
$attempt = 0
while ($attempt -lt 2 -and [string]::IsNullOrEmpty($coord)) {
    $attempt++
    & $adb logcat -c | Out-Null
    AdbOut @("shell", "am", "start", "-n", "$pkg/$activity") | Out-Null
    Write-Host "launch $pkg attempt=$attempt"
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        $l = Get-MobileLog "\[mobile\] (error|user@)"
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
        # 按实际出生坐标重裁并重推图集
        AdbOut @("shell", "am", "force-stop", $pkg) | Out-Null
        Write-Host "spawn coord=$coord, re-crop + re-push"
        if (-not (Invoke-Subset $coord)) { Write-Host "FAIL: subset spawn crop"; exit 1 }
        & $adb shell rm -rf "$deviceFiles/mapAtlas" 2>&1 | Out-Null
        & $adb shell mkdir -p "$deviceFiles/mapAtlas" 2>&1 | Out-Null
        if (-not (Push-Device $mapAtlas "$deviceFiles/mapAtlas")) { Write-Host "FAIL: re-push atlas"; exit 1 }
        $coord = ""
    }
}
if ([string]::IsNullOrEmpty($coord)) { Write-Host "FAIL: no [mobile] user@ in logcat"; exit 1 }

# 6. 最终断言链：connect -> login -> select -> enter-game -> user@x,y（force-stop 干净重启 + 重开 logcat 窗口）
AdbOut @("shell", "am", "force-stop", $pkg) | Out-Null
& $adb logcat -c | Out-Null
AdbOut @("shell", "am", "start", "-n", "$pkg/$activity") | Out-Null
$deadline = (Get-Date).AddSeconds($WaitSec)
$chain = @{ connect = $false; login = $false; select = $false; enter = $false; user = $false }
$coords = @()
while ((Get-Date) -lt $deadline) {
    foreach ($k in @($chain.Keys)) { # 快照枚举：循环内 $chain[$k]=$true 会改表，PS 5.1 直接枚举 $chain.Keys 抛 InvalidOperationException
        if ($chain[$k]) { continue }
        $pat = switch ($k) {
            "connect" { "\[mobile\] connect " }
            "login"   { "\[mobile\] login " }
            "select"  { "\[mobile\] select-ready" }
            "enter"   { "\[mobile\] enter-game" }
            "user"    { "\[mobile\] user@\d+,\d+" }
        }
        if ((Get-MobileLog $pat) -ne "") { $chain[$k] = $true }
    }
    $userLines = (& $adb logcat -d -s Unity 2>&1 | Select-String -Pattern "\[mobile\] user@\d+,\d+" | ForEach-Object { $_.ToString() })
    foreach ($u in $userLines) { if ($u -match "user@(\d+),(\d+)") { $c = "$($Matches[1]),$($Matches[2])"; if ($coords -notcontains $c) { $coords += $c } } }
    $done = ($chain.Values -notcontains $false) -and ($coords.Count -ge 1)
    if ($done) { break }
    Start-Sleep -Seconds 3
}
$chainText = ($chain.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join " "
Write-Host "  chain: $chainText coords=$($coords -join ' | ')"

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
# 7.5 HUD 断言（增量3）：截图 1 扫描战斗 HUD 元素——右下攻击按钮橙圆盘 + 左上 HP 红条。
# 模拟器降级 1280x720 逻辑 → 物理 2400x1080 全屏拉伸（1.875x / 1.5y）。攻击中心逻辑 (1190,560) →
# 物理 (2231,840) r60 逻辑→约 (112,90) 物理；HP 条逻辑 (20,20)-(200,34) → 物理 (37,30)-(374,51)。
# 区域放宽防拉伸/抖动偏差，只判橙红主色（HUD 是画面唯一大橙圆/红条，误报低）。
$hudOk = $false
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
# 8. 滑动移动（摇杆松手补步 → C.Walk 一格）；等位置变化。
# 多方向重试：西→东→北→南，任一方向位置变化即 PASS（出生点可能邻墙，单方向东滑会撞墙）。
# 坐标按模拟器降级后 1280x720 横屏（中心 640,360；swipe 起点即摇杆原点，位移 800px>奔跑阈值）。
$moved = $false
$swipes = @(@(1040,360,240,360), @(240,360,1040,360), @(640,620,640,100), @(640,100,640,620))
if ($chain["enter"] -and $coords.Count -ge 1) {
    foreach ($sw in $swipes) {
        & $adb shell input swipe $sw[0] $sw[1] $sw[2] $sw[3] 150 2>&1 | Out-Null
        # 模拟器 swiftshader 帧率极低（0.2fps）：触摸事件要等下一帧（3-5s）才被 PollJoystick 处理、
        # 再下一帧 Process 发送 C.Walk，到服务器回位置包又隔数秒。12s 窗口会截断，放宽到 30s。
        $swipeDeadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $swipeDeadline) {
            Start-Sleep -Seconds 3
            $userLines = (& $adb logcat -d -s Unity 2>&1 | Select-String -Pattern "\[mobile\] user@\d+,\d+" | ForEach-Object { $_.ToString() })
            foreach ($u in $userLines) { if ($u -match "user@(\d+),(\d+)") { $c = "$($Matches[1]),$($Matches[2])"; if ($coords -notcontains $c) { $coords += $c } } }
            if ($coords.Count -ge 2) { $moved = $true; break }
        }
        if ($moved) { break }
    }
    $p2 = Start-Process -FilePath $adb -ArgumentList @("exec-out", "screencap", "-p") -RedirectStandardOutput $shot2 -NoNewWindow -Wait
}
Write-Host "  moved=$moved coords=$($coords -join ' | ')"

# 9. 清理
AdbOut @("shell", "am", "force-stop", $pkg) | Out-Null
if (-not $server.HasExited) { Stop-Process $server -Force }
if ($null -ne $emulatorProc -and -not $emulatorProc.HasExited) { Stop-Process $emulatorProc -Force }

# 10. 判定
$colCount = 0
if ($sz1 -gt 10000) {
    # python 输出为字符串，PS 5.1 字符串与数字 -gt 按字典序比较（"232563" -gt 50 为 False）→ 必须 [int]::TryParse 强转
    $raw = python -c "import sys; from PIL import Image; im=Image.open(r'$shot1').convert('RGB'); print(len(im.getcolors(maxcolors=10**7)) if im.getcolors(maxcolors=10**7) else 99999)"
    [int]::TryParse(("$raw").Trim(), [ref]$colCount) | Out-Null
}
$ok = ($chain.Values -notcontains $false) -and ($moved) -and ($colCount -gt 50) -and $hudOk
Write-Host "  shot1=$sz1 colors=$colCount hud=$hudOk"
if ($ok) { Write-Host "PASS: Android login->enter->render->move ok" ; exit 0 } else { Write-Host "FAIL"; exit 1 }
