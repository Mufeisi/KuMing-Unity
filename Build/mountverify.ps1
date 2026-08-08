# Mount touch pure-logic verify: Unity batchmode probe, no server needed.
# MountDialog resident + S.MountUpdate local sync/remote dispatch/unmount hide, Show no-mount
# prompt, CanRide guards (mount type / 500ms throttle / standing action) -> @ride, CloseButton,
# MobileBag mount button (left-anchored under hero) UiConsumer toggle + no-mount prompt.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\mountverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.MountVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[mountverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  mount exit=$code : $line"
if ($code -eq 0 -and $line -match "\[mountverify\] PASS") { Write-Host "PASS: MountVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
