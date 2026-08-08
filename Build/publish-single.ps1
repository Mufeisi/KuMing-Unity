# 单 EXE 发布（实机测试用）：客户端 Build/PC → 7-Zip SFX 自解压单 EXE（Build/release/CrystalSetup.exe），
# 双击解压到临时目录并运行 Crystal.exe（Unity Player 无法真单文件，SFX 是 7z.sfx 方案，支持递归子目录）。
# 服务端：Build/Server/publish/ 已含 Release 二进制（Server.exe + 依赖 + Configs/Envir 数据目录）。
# 用法：powershell -File Build/publish-single.ps1
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$pcDir = Join-Path $root "Build\PC"
$releaseDir = Join-Path $root "Build\release"
$sfx = Join-Path $releaseDir "CrystalSetup.exe"
$sz = "C:\Program Files\7-Zip\7z.exe"
$sfxModule = "C:\Program Files\7-Zip\7z.sfx"

if (-not (Test-Path (Join-Path $pcDir "Crystal.exe"))) { Write-Host "FAIL: $pcDir\Crystal.exe missing"; exit 1 }
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

# 1. staging：exe 同级结构（Crystal.exe + Crystal_Data + Maps/mapAtlas/atlas 资源），SFX 解压后默认路径即命中
$stage = Join-Path $releaseDir "stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item (Join-Path $pcDir "*") $stage -Recurse -Force
$resDir = Join-Path $root "Build\pc-res"
foreach ($sub in @("Maps", "mapAtlas", "atlas")) {
    $src = Join-Path $resDir $sub
    if (Test-Path $src) { Copy-Item $src (Join-Path $stage $sub) -Recurse -Force }
}

# 2. 打包 stage → 临时 7z
$archive = Join-Path $env:TEMP "crystal-sfx.7z"
if (Test-Path $archive) { Remove-Item $archive -Force }
Write-Host "=== 7z 打包中（exe+资源，约 1-3 分钟）==="
& $sz a -t7z -mx=1 -y "$archive" "$stage\*" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: 7z archive"; exit 1 }

# 3. SFX 配置：解压后运行 Crystal.exe（GUI 模式静默解压到临时目录）
$cfg = Join-Path $env:TEMP "crystal-sfx.cfg"
@"
;!@Install@!UTF-8!
Title="Crystal"
BeginPrompt="Extract Crystal and launch"
RunProgram="Crystal.exe"
GUIMode="2"
;!@InstallEnd@!
"@ | Set-Content $cfg -Encoding UTF8

# 3. 拼接 SFX 单 EXE（7z.sfx + config + archive）
if (Test-Path $sfx) { Remove-Item $sfx -Force }
cmd /c "copy /b `"$sfxModule`" + `"$cfg`" + `"$archive`" `"$sfx`" >nul"
if (-not (Test-Path $sfx)) { Write-Host "FAIL: $sfx not produced"; exit 1 }
$mb = [math]::Round((Get-Item $sfx).Length / 1MB, 1)
Write-Host "PASS: $sfx ($mb MB)"
Write-Host "Server: Build\Server\publish\Server.exe (Release, 双击启动)"
