Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile("D:\ChuanQi\Kmyq\Crystal-master\Unity\Build\orient-probe.png")
$reds=0; $nonblack=0; $minX=9999;$maxX=-1;$minY=9999;$maxY=-1
for ($y=0; $y -lt 100; $y++) { for ($x=0; $x -lt 200; $x++) {
  $c=$bmp.GetPixel($x,$y)
  if ($c.R -gt 0 -or $c.G -gt 0 -or $c.B -gt 0) {
    $nonblack++
    if ($c.R -gt 200 -and $c.G -lt 50) { $reds++ }
    if($x -lt $minX){$minX=$x}; if($x -gt $maxX){$maxX=$x}
    if($y -lt $minY){$minY=$y}; if($y -gt $maxY){$maxY=$y}
  }
}}
"nonblack=$nonblack red=$reds bbox x=[$minX,$maxX] y=[$minY,$maxY]"
$bmp.Dispose()
