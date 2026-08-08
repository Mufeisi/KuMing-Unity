# PC 发布物打包 + 补丁清单（9-2）：把 Build/PC（Crystal.exe + Data + KeyBinds.ini）组装为
# 可分发发布物 Build/publish-pc/（含默认 Crystal.ini 首启配置），并用 AssetCompiler manifest
# 生成补丁清单 resource.manifest.json（--version 由参数给定，默认 1.0.0）。
# 用途：AutoPatcherAdminV2 上传清单+文件到 FTP/HTTP；客户端/回滚按清单校验（ResourceSync 同构机制）。
# 用法：powershell -File Build/pcpublish.ps1 [-Version 1.0.0]
param(
    [string]$Version = "1.0.0"
)
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$src = Join-Path $root "Build\PC"
$out = Join-Path $root "Build\publish-pc"
$compiler = Join-Path $root "tools\AssetCompiler\bin\Release\net8.0\AssetCompiler.exe"

if (-not (Test-Path (Join-Path $src "Crystal.exe"))) { Write-Host "FAIL: $src\Crystal.exe 不存在（先跑 Build/buildpc.ps1）"; exit 1 }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
Copy-Item $src $out -Recurse
# 首启默认配置（PcStartup 首启无 ini 时会自行写默认，此文件保证随包分发含显式配置）
$ini = Join-Path $out "Crystal.ini"
if (-not (Test-Path $ini)) {
    @("[Screen]", "Width=1280", "Height=720", "FullScreen=false") | Set-Content $ini -Encoding Ascii
}
& $compiler manifest $out --out (Join-Path $out "resource.manifest.json") --version $Version
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: manifest"; exit 1 }
$cnt = (Get-ChildItem $out -Recurse -File | Where-Object { $_.Name -ne "resource.manifest.json" }).Count
$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host "PASS: publish-pc files=$cnt size=$([math]::Round($size,1))MB manifest v$Version"
