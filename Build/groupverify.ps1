# Group flow touch pure-logic verify: Unity batchmode probe, no server needed.
# GroupDialog resident + Switch/Add/Del button dispatch to C.SwitchGroup/C.AddMember/
# C.DelMember, MirInputBox Enter/Esc routing, S.* group dispatch (list/map/radar),
# S.GroupInvite MirMessageBox YesNo, RouteTouch group-button consumption.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\groupverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.GroupVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[groupverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  group exit=$code : $line"
if ($code -eq 0 -and $line -match "\[groupverify\] PASS") { Write-Host "PASS: GroupVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
