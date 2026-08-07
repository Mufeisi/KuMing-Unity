# TouchJoystick pure-logic verify: Unity batchmode probe, no server needed.
# Assert joystick deadzone/run-threshold/8-way quantize/multi-touch/release-preserve/reset.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\joystickverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.TouchJoystickVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[joystickverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  joystick exit=$code : $line"
if ($code -eq 0 -and $line -match "\[joystickverify\] PASS") { Write-Host "PASS: TouchJoystick pure logic ok" ; exit 0 }
Write-Host "FAIL"; exit 1
