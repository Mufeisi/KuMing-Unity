using Client.MirGraphics;
using Client.MirScenes;
using Crystal.Client.Core.MirMath;
using System.Text.RegularExpressions;
using S = ServerPackets;

namespace Client.MirObjects
{
    // 逐字移植（2026-08-08）：sanduan 提取 A2 ItemObject.cs（153 行，源参照
    // sanduan/Crystal-master/Client/MirObjects/ItemObject.cs）。地面物品/金币对象：
    // Load(S.ObjectItem) 取 FloorItems 库真尺寸 + 初始 DrawY；Load(S.ObjectGold)
    // 按金币量分级帧 112-116（<100/<200/<500/<1000/else）；Process 世界→屏幕换算 +
    // 单元格居中偏移（(CellWidth-Size)/2）+ FinalDrawLocation + DisplayRectangle；
    // Draw 单帧；MouseOver 返回所在格判定；DrawName 双重载带 CreateLabel
    // （LabelList 复用 + 数字尾缀剥离）。
    public class ItemObject : MapObject
    {
        public override ObjectType Race
        {
            get { return ObjectType.Item; }
        }

        public override bool Blocking
        {
            get { return false; }
        }

        public Size Size;

        public ItemObject(uint objectID) : base(objectID)
        {
        }

        public void Load(S.ObjectItem info)
        {
            Name = info.Name;
            NameColour = Color.FromArgb(info.NameColour.ToArgb());

            BodyLibrary = Libraries.FloorItems;

            CurrentLocation = new Point(info.Location.X, info.Location.Y);
            MapLocation = new Point(info.Location.X, info.Location.Y);
            GameScene.Scene.MapControl.AddObject(this);
            DrawFrame = info.Image;

            Size = BodyLibrary.GetTrueSize(DrawFrame);

            DrawY = CurrentLocation.Y;
        }

        public void Load(S.ObjectGold info)
        {
            Name = string.Format("Gold ({0:###,###,###})", info.Gold);

            BodyLibrary = Libraries.FloorItems;

            CurrentLocation = new Point(info.Location.X, info.Location.Y);
            MapLocation = new Point(info.Location.X, info.Location.Y);
            GameScene.Scene.MapControl.AddObject(this);

            if (info.Gold < 100)
                DrawFrame = 112;
            else if (info.Gold < 200)
                DrawFrame = 113;
            else if (info.Gold < 500)
                DrawFrame = 114;
            else if (info.Gold < 1000)
                DrawFrame = 115;
            else
                DrawFrame = 116;

            Size = BodyLibrary.GetTrueSize(DrawFrame);

            DrawY = CurrentLocation.Y;
        }

        public override void Draw()
        {
            if (BodyLibrary != null)
                BodyLibrary.Draw(DrawFrame, DrawLocation, DrawColour);
        }

        public override void Process()
        {
            DrawLocation = new Point((CurrentLocation.X - User.Movement.X + MapControl.OffSetX) * MapControl.CellWidth, (CurrentLocation.Y - User.Movement.Y + MapControl.OffSetY) * MapControl.CellHeight);
            DrawLocation.Offset((MapControl.CellWidth - Size.Width) / 2, (MapControl.CellHeight - Size.Height) / 2);
            DrawLocation.Offset(User.OffSetMove);
            DrawLocation.Offset(GlobalDisplayLocationOffset);
            FinalDrawLocation = DrawLocation;

            DisplayRectangle = new Rectangle(DrawLocation, Size);
        }

        public override bool MouseOver(Point p)
        {
            return MapControl.MapLocation == CurrentLocation;
        }

        public override void DrawName()
        {
            CreateLabel(Color.Transparent, false, true);

            if (NameLabel == null) return;
            NameLabel.Location = new Point(
                DisplayRectangle.X + (DisplayRectangle.Width - NameLabel.Size.Width) / 2,
                DisplayRectangle.Y + (DisplayRectangle.Height - NameLabel.Size.Height) / 2 - 20);
            NameLabel.Draw();
        }

        public override void DrawBehindEffects(bool effectsEnabled)
        {
        }

        public override void DrawEffects(bool effectsEnabled)
        {
        }

        public void DrawName(int y)
        {
            CreateLabel(Color.FromArgb(100, 0, 24, 48), true, false);

            NameLabel.Location = new Point(
                DisplayRectangle.X + (DisplayRectangle.Width - NameLabel.Size.Width) / 2,
                DisplayRectangle.Y + y + (DisplayRectangle.Height - NameLabel.Size.Height) / 2 - 20);
            NameLabel.Draw();
        }

        private void CreateLabel(Color backColour, bool border, bool outline)
        {
            NameLabel = null;

            for (int i = 0; i < LabelList.Count; i++)
            {
                if (LabelList[i].Text != Name || LabelList[i].Border != border || LabelList[i].BackColour != backColour || LabelList[i].ForeColour != NameColour || LabelList[i].OutLine != outline) continue;
                NameLabel = LabelList[i];
                break;
            }

            if (NameLabel != null && !NameLabel.IsDisposed) return;

            NameLabel = new MirControls.MirLabel
            {
                AutoSize = true,
                BorderColour = Color.Black,
                BackColour = backColour,
                ForeColour = NameColour,
                OutLine = outline,
                Border = border,
                Text = Regex.Replace(Name, @"\d+$", string.Empty),
            };

            LabelList.Add(NameLabel);
        }
    }
}
