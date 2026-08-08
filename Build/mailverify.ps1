# Mail flow touch pure-logic verify: Unity batchmode probe, no server needed.
# Five mail dialogs resident + S.ReceiveMail sort/Bind/NewMail/list rows, MailLockedItem view-lock,
# MailSendRequest recipient input, MailSent unlock+hide, ParcelCollected three-way, MailCost postage label,
# two-stage MirItemCell bag->mail put-in, DontTrade guard, SendButton letter compose, GoldSendLabel
# MirAmountBox, close-window refund+unlock, MobileBag mail button UiConsumer toggle+mutex.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\mailverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.MailVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[mailverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  mail exit=$code : $line"
if ($code -eq 0 -and $line -match "\[mailverify\] PASS") { Write-Host "PASS: MailVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
