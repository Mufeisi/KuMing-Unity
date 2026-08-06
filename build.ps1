# Crystal 可重复构建入口 (G0-3)
# 用法: powershell -ExecutionPolicy Bypass -File build.ps1 [-Configuration Debug|Release]
# 说明: PatcherWebSite 是遗留 .NET Framework 4.8 网站项目(无 csproj)，dotnet CLI 无法构建(MSB4249)，
#       需 VS2022 全 MSBuild 单独打开解决方案处理；本脚本跳过它。

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$projects = @(
    "Shared/Shared.csproj",
    "Server/Server.Library.csproj",
    "Server.MirForms/Server.csproj",
    "Controls/FixedListViewControl/CustomFormControl.csproj",
    "LibraryEditor/LibraryEditor.csproj",
    "LibraryViewer/LibraryViewer.csproj",
    "AutoPatcherAdmin/AutoPatcherAdmin.csproj",
    "AutoPatcherAdminV2/AutoPatcherAdminV2.csproj"
)

$failed = @()
foreach ($p in $projects) {
    Write-Host "=== build $p ($Configuration) ==="
    dotnet build $p -c $Configuration -v minimal
    if ($LASTEXITCODE -ne 0) {
        $failed += $p
    }
}

if ($failed.Count -gt 0) {
    Write-Host "BUILD FAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}

# 记录关键产物 SHA256
$hashOut = "docs/build-artifact-hashes.txt"
$targets = @(
    "Build/Server/$Configuration/Server.exe",
    "Build/Server/$Configuration/Server.Library.dll",
    "Build/Server/$Configuration/Shared.dll"
)
$lines = @("# Crystal build artifacts SHA256 ($(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), config=$Configuration)")
foreach ($t in $targets) {
    if (Test-Path $t) {
        $hash = (Get-FileHash $t -Algorithm SHA256).Hash
        $lines += "$hash  $t"
    } else {
        $lines += "# MISSING: $t"
    }
}
$lines | Set-Content -Path $hashOut -Encoding UTF8
Write-Host "Hashes written to $hashOut"
exit 0
