Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile("D:\ChuanQi\Kmyq\Crystal-master\Unity\Build\orient-probe.png")
"size=$($bmp.Width)x$($bmp.Height)"
foreach ($pt in @(@(30,30),@(30,80))) {
  $c=$bmp.GetPixel($pt[0],$pt[1])
  "$($pt[0]),$($pt[1]) -> R$($c.R) G$($c.G) B$($c.B)"
}
$bmp.Dispose()
