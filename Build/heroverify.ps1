# Hero panel touch pure-logic verify: Unity batchmode probe, no server needed.
# HeroMenuPanel/HeroBehaviourPanel/HeroManageDialog resident + S.HeroInformation builds Hero and
# hero dialog set, ManageHeroes/ChangeHero storage+current, UpdateHeroSpawnState show/hide (resident
# not dispose), UnlockHeroAutoPot/SetAutoPotValue/SetAutoPotItem/SetHeroBehaviour, TakeBack/
# TransferHeroItem echo swap (no BeltDialog), HeroHealthChanged/GainHeroExperience/HeroLevelChanged,
# MobileBag hero button (left-anchored) UiConsumer toggle + no-hero guard.
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"
$log = Join-Path $root "Unity\Build\heroverify.log"

& $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.HeroVerify.Run -quit -logFile $log | Out-Null
$code = $LASTEXITCODE
$line = (Select-String -Path $log -Pattern "\[heroverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
Write-Host "  hero exit=$code : $line"
if ($code -eq 0 -and $line -match "\[heroverify\] PASS") { Write-Host "PASS: HeroVerify ok" ; exit 0 }
Write-Host "FAIL"; exit 1
