Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile("D:\ChuanQi\Kmyq\Crystal-master\Build\anim-000-standing-d0.png")
$w=$bmp.Width; $h=$bmp.Height
$content=0; $bg=0
$minX=9999; $maxX=-1; $minY=9999; $maxY=-1
for ($y=0; $y -lt $h; $y++) { for ($x=0; $x -lt $w; $x++) {
  $c=$bmp.GetPixel($x,$y)
  $isBg = ($c.R -eq 13 -and $c.G -eq 13 -and $c.B -eq 13)
  if ($isBg) { $bg++ } else {
    $content++
    if($x -lt $minX){$minX=$x}; if($x -gt $maxX){$maxX=$x}
    if($y -lt $minY){$minY=$y}; if($y -gt $maxY){$maxY=$y}
  }
}}
"bg=$bg content=$content"
"content_bbox x=[$minX,$maxX] y=[$minY,$maxY]"
# 每帧锚点 y=352（h-48）处的非背景列跨度 → 4 帧应在 x≈54..694
$line=""
for ($x=0; $x -lt $w; $x+=10) { if (-not ($bmp.GetPixel($x,352).R -eq 13 -and $bmp.GetPixel($x,352).G -eq 13 -and $bmp.GetPixel($x,352).B -eq 13)) { $line += "$x " } }
"anchor_row_nonbg_x: $line"
$bmp.Dispose()
