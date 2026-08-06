Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile("D:\ChuanQi\Kmyq\Crystal-master\Build\light-render.png")
$l = [System.Drawing.Bitmap]::FromFile("D:\ChuanQi\Kmyq\Crystal-master\Build\light-render-light.png")
"final 320x200"
foreach($p in @(@(10,10),@(50,20),@(100,70),@(160,100),@(210,140),@(300,190))){
  $c=$bmp.GetPixel($p[0],$p[1]); "{0},{1} R={2} G={3} B={4}" -f $p[0],$p[1],$c.R,$c.G,$c.B
}
"light 320x200"
foreach($p in @(@(10,10),@(100,70),@(210,140),@(160,100))){
  $c=$l.GetPixel($p[0],$p[1]); "L {0},{1} R={2} G={3} B={4}" -f $p[0],$p[1],$c.R,$c.G,$c.B
}
$bmp.Dispose(); $l.Dispose()
