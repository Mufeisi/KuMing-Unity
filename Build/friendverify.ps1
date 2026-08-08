# Friend flow touch pure-logic verify: Unity batchmode probe, no server needed.
# FriendDialog/MemoDialog resident + Add/Remove/Memo/Whisper button dispatch to
# C.AddFriend/C.RemoveFriend/C.AddMemo + Show→C.RefreshFriends, S.FriendUpdate echo,
# blacklist-tab filter, 12-row paging, Whisper seam guard, RouteTouch consumption.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\friendverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.FriendVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[friendverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  friend exit=$code : $line"
if ($code -eq 0 -and $line -match "\[friendverify\] PASS") { Write-Host "PASS: FriendVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
