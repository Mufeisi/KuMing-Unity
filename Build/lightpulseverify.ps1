# P1 sanduan Light.shader 光源脉冲语义验证：CRYSTAL_TIME 调制下 lightTex 字节级 CPU/GPU 对照。
# 3 个确定性时刻：t=0（sin0: b=a=0.975 基线衰减）、t=pi/18（sin1: b=1,a=clamp1.025=1 峰值）、
# t=pi/6（sin-1: b=0.95,a=0.925 谷底）。三时刻全 PASS 才通过（复用 R5 LightRender 三段检）。
param()
$ErrorActionPreference = "Stop"
$root = "D:\ChuanQi\Kmyq\Crystal-master"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe"
$proj = Join-Path $root "Unity"

$times = @(
  @{ tag = "t0";      t = "0" },
  @{ tag = "tpeak";   t = "0.1745329252" },  # pi/18, sin(9t)=1
  @{ tag = "ttrough"; t = "0.5235987756" }   # pi/6,  sin(9t)=-1
)
$env:CRYSTAL_LIGHTS = "160,100,4;220,140,2,255,180,80"
$env:CRYSTAL_DARKNESS = "20,20,20"
$env:CRYSTAL_RT_W = "320"
$env:CRYSTAL_RT_H = "200"

$ok = $true
foreach ($tc in $times) {
  $log = Join-Path $root "Unity\Build\lightpulseverify-$($tc.tag).log"
  $env:CRYSTAL_TIME = $tc.t
  $env:CRYSTAL_OUT = "Build\light-pulse-$($tc.tag).png"
  & $unity -batchmode -projectPath $proj -executeMethod Crystal.Rendering.Editor.LightRender.Run -quit -logFile $log | Out-Null
  $code = $LASTEXITCODE
  $line = (Select-String -Path $log -Pattern "\[lightpulseverify\] (PASS|FAIL)" | Select-Object -Last 1).Line
  Write-Host "  pulse $($tc.tag) t=$($tc.t) exit=$code : $line"
  if ($code -ne 0 -or $line -notmatch "\[lightpulseverify\] PASS") { $ok = $false }
}
Remove-Item Env:CRYSTAL_TIME -ErrorAction SilentlyContinue
if ($ok) { Write-Host "PASS: LightPulseVerify ok (3 times)"; exit 0 }
Write-Host "FAIL"; exit 1
