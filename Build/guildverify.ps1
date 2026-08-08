# Guild flow touch pure-logic verify: Unity batchmode probe, no server needed.
# GuildDialog resident + Show throttle (NoticeChanged/5s) C.RequestGuildInfo{Type=0},
# S.GuildStatus/GuildNoticeChange(25-row window + scroll)/GuildMemberChange/GuildInvite/
# GuildExpGain echo, NotInGuild prompt, RouteTouch consumption.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\guildverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.GuildVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[guildverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  guild exit=$code : $line"
if ($code -eq 0 -and $line -match "\[guildverify\] PASS") { Write-Host "PASS: GuildVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
