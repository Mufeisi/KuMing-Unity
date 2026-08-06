Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile("D:\ChuanQi\Kmyq\Crystal-master\Build\map-render-0.png")
$w=$bmp.Width; $h=$bmp.Height
function SampleRow($y){
  $n=0; $gray=0
  for ($x=0; $x -lt $w; $x+=8) { $c=$bmp.GetPixel($x,$y); $n++
    if ($c.R -eq $c.G -and $c.G -eq $c.B -and $c.R -ge 10 -and $c.R -le 16) { $gray++ }
  }
  return $gray
}
"size=${w}x${h}"
"top  row2 gray=$([math]::Round((SampleRow 2)/$([math]::Ceiling($w/8))*100,0))% (背景灰=无tile)"
"mid  row320 gray=$([math]::Round((SampleRow 320)/$([math]::Ceiling($w/8))*100,0))%"
"bot  row630 gray=$([math]::Round((SampleRow 630)/$([math]::Ceiling($w/8))*100,0))%"
$bmp.Dispose()
