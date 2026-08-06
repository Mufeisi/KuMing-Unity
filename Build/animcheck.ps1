Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile("D:\ChuanQi\Kmyq\Crystal-master\Build\anim-000-standing-d0.png")
$w=$bmp.Width; $h=$bmp.Height
$content=0; $minX=9999; $maxX=-1; $minY=9999; $maxY=-1
for ($y=0; $y -lt $h; $y++) { for ($x=0; $x -lt $w; $x++) {
  $c=$bmp.GetPixel($x,$y)
  if (-not ($c.R -eq 13 -and $c.G -eq 13 -and $c.B -eq 13)) {
    $content++
    if($x -lt $minX){$minX=$x}; if($x -gt $maxX){$maxX=$x}
    if($y -lt $minY){$minY=$y}; if($y -gt $maxY){$maxY=$y}
  }
}}
"content=$content bbox x=[$minX,$maxX] y=[$minY,$maxY] (期望 y 上界≈371 近底部)"
$bmp.Dispose()
