# Chat touch verify: Unity batchmode probe.
# ChatDialog resident + chat/channel buttons -> open input + soft keyboard focus,
# channel prefix cycle, keyboard text sync + Enter submit (C.Chat), back-close semantics.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\chatverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.ChatVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[chatverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  chat exit=$code : $line"
if ($code -eq 0 -and $line -match "\[chatverify\] PASS") { Write-Host "PASS: ChatVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
