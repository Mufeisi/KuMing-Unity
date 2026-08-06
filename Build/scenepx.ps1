Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile("D:\ChuanQi\Kmyq\Crystal-master\Build\scene-render-0.png")
foreach($p in @(@(600,60),@(600,150),@(600,230),@(600,300),@(630,230),@(580,160))){
  $c=$bmp.GetPixel($p[0],$p[1])
  "{0},{1} R={2} G={3} B={4} A={5}" -f $p[0],$p[1],$c.R,$c.G,$c.B,$c.A
}
$bmp.Dispose()
