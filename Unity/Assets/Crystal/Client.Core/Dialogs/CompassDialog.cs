using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-06）：Client/MirScenes/Dialogs/CompassDialog.cs
    // 指南针：屏幕中上方小罗盘，Process 计算当前位置→目标点方位角，
    // 把 Prguse2 的 _image.Index 切到 1470+offset（40 步内 40 个指向帧）。
    public class CompassDialog : MirControl
    {
        public Point Destination = Point.Empty;

        private readonly MirImageControl _image;

        public CompassDialog()
        {
            Location = new Point((Settings.ScreenWidth / 2) - 25, (Settings.ScreenHeight / 2) - 120);
            NotControl = true;
            Size = new Size(10, 10);
            Movable = false;
            Sort = true;

            _image = new MirImageControl
            {
                Parent = this,
                Index = 0,
                Library = Libraries.Prguse2,
                NotControl = true,
                UseOffSet = true,
                Location = new Point(0, 0),
                Visible = true
            };
        }

        public void ClearPoint()
        {
            Destination = Point.Empty;
        }

        public void SetPoint(Point point)
        {
            Destination = point;
        }

        public void Process()
        {
            if (Destination == Point.Empty || (Destination.X == GameScene.User.CurrentLocation.X && Destination.Y == GameScene.User.CurrentLocation.Y))
            {
                Visible = false;
                return;
            }

            Visible = true;

            float xDiff = GameScene.User.CurrentLocation.X - Destination.X;
            float yDiff = GameScene.User.CurrentLocation.Y - Destination.Y;

            var angle = Math.Atan2(xDiff * -1, yDiff) * 180 / Math.PI;

            var degree = (angle + 360) % 360;

            var offset = (double)40 / 360 * degree;

            _image.Index = (int)(1470 + Math.Floor(offset));
        }
    }
}
