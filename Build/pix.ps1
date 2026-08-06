Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile("D:\ChuanQi\Kmyq\Crystal-master\Build\anim-000-standing-d0.png")
foreach ($pt in @(@(54,247),@(104,297),@(104,79),@(200,100),@(200,300),@(54,29),@(800,153),@(500,90),@(500,250))) {
  $c=$bmp.GetPixel($pt[0],$pt[1])
  "$($pt[0]),$($pt[1]) -> R$($c.R) G$($c.G) B$($c.B) A$($c.A)"
}
$bmp.Dispose()
