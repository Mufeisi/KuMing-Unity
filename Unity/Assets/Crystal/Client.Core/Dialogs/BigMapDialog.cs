using System;
using System.Collections.Generic;
using System.Linq;
using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirSounds;
using C = ClientPackets;
using Client.MirNetwork;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-06）：Client/MirScenes/Dialogs/BigMapDialog.cs 大地图对话框控制树。
    // BigMapDialog 大地图窗（NPC 列表滚动 + 地图视口 + 传送按钮 + 世界地图）+ WorldMapImage
    // 世界地图（MapLinkIcon 图标按钮）+ BigMapViewPort 视口（mmap 图集渲染 + 玩家雷达点 +
    // 移动点/NPC 图标定位）+ BigMapNPCRow NPC 列表行 + BigMapRecord 记录模型。
    // 裁剪：MirMessageBox（未移植弹窗基类，TeleportToNPC 去确认直发包）；DXManager 雷达点绘制
    // （渲染层未接驳，对象点/组队点不绘制）；GroupDialog 组队成员定位（迭代包6）；PathFinder
    // 寻路保留（PathFinder seam 可用，探针路径为空）；SearchTextBox 键盘搜索保留。
    public sealed class BigMapDialog : MirImageControl
    {
        MirLabel CoordinateLabel, TitleLabel;
        public MirButton CloseButton, ScrollUpButton, ScrollDownButton, ScrollBar, WorldButton, MyLocationButton, TeleportToButton, SearchButton;
        public MirTextBox SearchTextBox;

        private float GapPerRow;
        const int MaximumRows = 18;
        public int TargetMapIndex;
        private DateTime NextSearchTime;

        public BigMapViewPort ViewPort;
        public WorldMapImage WorldMap;

        private int scrollOffset;
        private int ScrollOffset
        {
            get { return scrollOffset; }
            set
            {
                scrollOffset = value;
                ScrollBar.Location = new Point(ScrollUpButton.Location.X, (int)(ScrollUpButton.Location.Y + 13 + scrollOffset * GapPerRow));
            }
        }

        private Point mouseLocation = Point.Empty;
        public Point MouseLocation
        {
            get { return mouseLocation; }
            set
            {
                if (mouseLocation == value) return;

                mouseLocation = value;
                MakeCoordinateLabel();
            }
        }

        private BigMapNPCRow selectedNPC;
        public BigMapNPCRow SelectedNPC
        {
            get { return selectedNPC; }
            set
            {
                if (value == selectedNPC) return;

                if (selectedNPC != null)
                    selectedNPC.NameLabel.ForeColour = Color.White;

                selectedNPC = value;
                if (value != null)
                {
                    selectedNPC.NameLabel.ForeColour = Color.Brown;
                    ViewPort.SelectedNPCIcon.Index = selectedNPC.Info.Icon;
                    ViewPort.SelectedNPCIcon.Visible = true;
                    var map = GameScene.Scene.MapControl;
                    TeleportToButton.Enabled = TargetMapIndex == map.Index && selectedNPC.Info.CanTeleportTo;
                }
                else
                {
                    ViewPort.SelectedNPCIcon.Visible = false;
                    TeleportToButton.Enabled = false;
                }
            }
        }

        private BigMapRecord currentRecord;
        public BigMapRecord CurrentRecord
        {
            get { return currentRecord; }
            set
            {
                if (currentRecord == value) return;
                SetButtonsVisibility(false);
                currentRecord = value;
                TitleLabel.Text = currentRecord != null ? currentRecord.MapInfo.Title : string.Empty;
                ViewPort.UserRadarDot.Visible = currentRecord != null && currentRecord.Index == GameScene.Scene.MapControl.Index;
                SetButtonsVisibility(true);
            }
        }

        public BigMapDialog()
        {
            Index = 820;
            Library = Libraries.Title;
            Sort = true;
            Location = Center;
            NotControl = false;

            ScrollUpButton = new MirButton
            {
                Index = 197,
                HoverIndex = 198,
                PressedIndex = 199,
                Location = new Point(Size.Width - 21, 48),
                Library = Libraries.Prguse2,
                Parent = this,
                Sound = SoundList.ButtonA,
            };
            ScrollUpButton.Click += (o, e) => ScrollUp();

            ScrollDownButton = new MirButton
            {
                Index = 207,
                HoverIndex = 208,
                PressedIndex = 209,
                Location = new Point(Size.Width - 21, 417),
                Library = Libraries.Prguse2,
                Parent = this,
                Sound = SoundList.ButtonA,
            };
            ScrollDownButton.Click += (o, e) => ScrollDown();

            ScrollBar = new MirButton
            {
                Index = 205,
                HoverIndex = 206,
                PressedIndex = 206,
                Location = new Point(Size.Width - 21, ScrollUpButton.Location.Y + 13),
                Library = Libraries.Prguse2,
                Parent = this,
                Movable = true,
                Sound = SoundList.ButtonA,
            };
            ScrollBar.OnMoving += (o, e) =>
            {
                var y = ScrollBar.Location.Y;
                if (y < 61)
                    y = 61;
                if (y > 398)
                    y = 398;

                var row = (y - ScrollUpButton.Location.Y + 13) / GapPerRow;
                ScrollOffset = (int)row;
                SetNPCButtonVisibility(true);
            };

            WorldButton = new MirButton
            {
                Index = 827,
                HoverIndex = 828,
                PressedIndex = 829,
                Location = new Point(250, Size.Height - 33),
                Library = Libraries.Title,
                Parent = this,
                Sound = SoundList.ButtonA,
            };
            WorldButton.Click += (o, e) => OpenWorldMap();

            MyLocationButton = new MirButton
            {
                Index = 824,
                HoverIndex = 825,
                PressedIndex = 826,
                Location = new Point(400, Size.Height - 33),
                Library = Libraries.Title,
                Parent = this,
                Sound = SoundList.ButtonA,
            };
            MyLocationButton.Click += (o, e) => TargetMyLocation();


            TeleportToButton = new MirButton
            {
                Index = 821,
                HoverIndex = 822,
                PressedIndex = 823,
                DisabledIndex = 823,
                Enabled = false,
                Location = new Point(Size.Width - 122, 432),
                Library = Libraries.Title,
                Parent = this,
                Sound = SoundList.ButtonA,
            };
            TeleportToButton.Click += (o, e) => TeleportToNPC();

            SearchButton = new MirButton
            {
                Index = 1340,
                HoverIndex = 1341,
                PressedIndex = 1342,
                Enabled = true,
                Location = new Point(23, Size.Height - 36),
                Library = Libraries.Prguse2,
                Parent = this,
                Sound = SoundList.ButtonA,
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.SearchForNPCs)
            };
            SearchButton.Click += (o, e) => Search();

            SearchTextBox = new MirTextBox
            {
                Location = new Point(59, Size.Height - 27),
                Parent = this,
                Font = new Font(Settings.FontName, 8F),
                Size = new Size(130, 10),
                MaxLength = Globals.MaxChatLength
            };
            SearchTextBox.TextBox.KeyPress += SearchTextBox_KeyPress;

            ViewPort = new BigMapViewPort()
            {
                Parent = this
            };

            CoordinateLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = Color.White,
                Location = new Point(519, 435),
                Parent = this,
            };

            TitleLabel = new MirLabel
            {
                ForeColour = Color.White,
                Parent = this,
                AutoSize = false,
                Size = new Size(699, 20),
                Location = new Point(19, 6),
                Font = new Font(Settings.FontName, 9F, FontStyle.Bold),
                DrawFormat = TextFormatFlags.HorizontalCenter
            };

            WorldMap = new WorldMapImage()
            {
                Parent = this,
                Location = new Point(10, 0)
            };

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(Size.Width - 25, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Hide();

            SearchTextBox.Enabled = false;
        }

        private void MakeCoordinateLabel()
        {
            CoordinateLabel.Text = $"[ {MouseLocation.X}, {MouseLocation.Y} ]";
        }

        public void ShowCoordinateLabel()
        {
            CoordinateLabel.Visible = true;
        }

        public void HideCoordinateLabel()
        {
            CoordinateLabel.Visible = false;
        }

        public void WorldMapSetup(WorldMapSetup setup)
        {
            WorldButton.Visible = setup.Enabled;
            WorldMap.MakeButtons(setup.Icons);
        }

        public void Toggle()
        {
            if (Visible)
                Hide();
            else
                Show();
        }

        public override void Show()
        {
            var map = GameScene.Scene.MapControl;
            if (map.BigMap <= 0) return;

            SearchTextBox.Enabled = false;

            base.Show();
            TargetMyLocation();
        }

        private void TargetMyLocation()
        {
            var map = GameScene.Scene.MapControl;
            if (map == null) return;
            SetTargetMap(map.Index);
        }

        public void SetTargetMap(int MapIndex)
        {
            TargetMapIndex = MapIndex;
            CurrentRecord = null;
            ScrollOffset = 0;
            ScrollBar.Visible = false;
            WorldMap.Visible = false;
            SearchTextBox.Visible = true;
            SelectedNPC = null;
            if (!GameScene.MapInfoList.ContainsKey(MapIndex))
                Network.Enqueue(new C.RequestMapInfo() { MapIndex = MapIndex });
            else
                CurrentRecord = GameScene.MapInfoList[MapIndex];
            Redraw();
        }

        public void SetTargetNPC(uint NPCIndex)
        {
            if (CurrentRecord?.NPCButtons == null)
                return;

            foreach (BigMapNPCRow row in CurrentRecord.NPCButtons)
            {
                if (row.Info.ObjectID != NPCIndex) continue;

                SelectedNPC = row;
                return;
            }
        }

        private void SetButtonsVisibility(bool visible)
        {
            if (currentRecord == null) return;
            SetMovementButtonsVisibility(visible);
            SetNPCButtonVisibility(visible);
        }

        private void SetMovementButtonsVisibility(bool visible)
        {
            foreach (var button in currentRecord.MovementButtons.Values)
                button.Visible = visible;
        }
        private void SetNPCButtonVisibility(bool visible)
        {
            foreach (var row in currentRecord.NPCButtons)
                row.Visible = false;

            if (!visible) return;

            for (int i = ScrollOffset; i < ScrollOffset + MaximumRows; i++)
            {
                if (i >= currentRecord.NPCButtons.Count) return;
                currentRecord.NPCButtons[i].Location = new Point(590, 50 + (i - ScrollOffset) * 21);
                currentRecord.NPCButtons[i].Visible = true;
            }

            if (currentRecord.NPCButtons.Count <= MaximumRows) return;

            ScrollBar.Visible = true;
            var scrollHeight = ScrollDownButton.Location.Y - ScrollUpButton.Location.Y - 32;
            var extraRows = currentRecord.NPCButtons.Count - MaximumRows;
            GapPerRow = scrollHeight / (float)extraRows;
        }

        public void BigMap_MouseWheel(object sender, MouseEventArgs e)
        {
            // 原 SystemInformation.MouseWheelScrollDelta（Unity 无 SystemInformation）→ 固定 120。
            int count = e.Delta / 120;

            if (count > 0)
                ScrollUp();
            else if (count < 0)
                ScrollDown();
        }

        private void ScrollUp()
        {
            if (ScrollOffset == 0) return;
            ScrollOffset--;
            SetNPCButtonVisibility(true);
        }

        private void ScrollDown()
        {
            if (ScrollOffset >= currentRecord.NPCButtons.Count - MaximumRows) return;
            ScrollOffset++;
            SetNPCButtonVisibility(true);
        }

        private void OpenWorldMap()
        {
            WorldMap.Visible = true;
            SearchTextBox.Visible = false;
            SetButtonsVisibility(false);
        }

        private void TeleportToNPC()
        {
            if (SelectedNPC == null || !SelectedNPC.Info.CanTeleportTo) return;

            // 原 MirMessageBox YesNo 确认传送花费（未移植弹窗基类）→ 直接校验并发包。
            if (GameScene.Gold < GameScene.TeleportToNPCCost) return;

            Network.Enqueue(new C.TeleportToNPC { ObjectID = SelectedNPC.Info.ObjectID });
        }

        private void Search()
        {
            if (!SearchTextBox.Enabled)
            {
                SearchTextBox.Enabled = true;
                SearchTextBox.SetFocus();
            }

            if (!string.IsNullOrWhiteSpace(SearchTextBox.Text) && SearchTextBox.Text.Length > 2) return;

            if (CMain.Now < NextSearchTime) return;

            NextSearchTime = CMain.Now.AddSeconds(1);
            Network.Enqueue(new C.SearchMap { Text = SearchTextBox.Text });
        }

        public void SearchTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (sender == null || e.KeyChar != (char)Keys.Enter) return;

            e.Handled = true;

            if (SearchButton.Enabled)
                SearchButton.InvokeMouseClick(null);
        }

        #region Disposable
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CoordinateLabel = null;
                CloseButton = null;
                ScrollUpButton = null;
                ScrollDownButton = null;
                ScrollBar = null;
                WorldButton = null;
                MyLocationButton = null;
                TeleportToButton = null;
                SearchButton = null;
                SearchTextBox = null;
                TitleLabel = null;
            }

            base.Dispose(disposing);
        }

        #endregion
    }

    public class WorldMapImage : MirImageControl
    {
        private new MirImageControl Border;
        private MirImageControl Clouds;
        private List<MirButton> ButtonList = new List<MirButton>();
        private MirLabel TitleLabel;
        public WorldMapImage()
        {
            Library = Libraries.Prguse2;
            Index = 1360;
            NotControl = false;
            Visible = false;

            Clouds = new MirImageControl()
            {
                Parent = this,
                Library = Libraries.Prguse2,
                Index = 1365,
                NotControl = true,
                Visible = true,
                Blending = true

            };

            Border = new MirImageControl()
            {
                Parent = this,
                Library = Libraries.Prguse2,
                Index = 1366,
                NotControl = true,
                Visible = true
            };

            TitleLabel = new MirLabel()
            {
                AutoSize = true,
                BackColour = Color.Black,
                ForeColour = Color.White,
                Parent = this,
                Visible = true
            };
        }

        public void MakeButtons(List<WorldMapIcon> icons)
        {
            foreach (WorldMapIcon icon in icons)
            {
                MirButton button = new MirButton()
                {
                    Index = icon.ImageIndex,
                    UseOffSet = true,
                    Library = Libraries.MapLinkIcon,
                    Parent = this,
                    Sound = SoundList.ButtonA,
                    OnlyDrawWhenActive = true
                };

                button.Click += (o, e) =>
                {
                    GameScene.Scene.BigMapDialog.SetTargetMap(icon.MapIndex);
                };
                button.MouseEnter += (o, e) =>
                {
                    TitleLabel.Text = icon.Title;
                    TitleLabel.Location = new Point(Size.Width / 2 - TitleLabel.Size.Width / 2, 10);
                };
                button.MouseLeave += (o, e) =>
                {
                    TitleLabel.Text = string.Empty;
                };

                ButtonList.Add(button);
            }
        }
    }

    public sealed class BigMapViewPort : MirControl
    {
        BigMapDialog ParentDialog;
        float ScaleX;
        float ScaleY;
        public MirImageControl SelectedNPCIcon, UserRadarDot;

        int BigMap_MouseCoordsProcessing_OffsetX, BigMap_MouseCoordsProcessing_OffsetY;

        public MirImageControl[] Players;
        public static Dictionary<string, Point> PlayerLocations = new Dictionary<string, Point>();

        public BigMapViewPort()
        {
            NotControl = false;
            Size = new Size(568, 380);

            SelectedNPCIcon = new MirImageControl
            {
                Library = Libraries.MapLinkIcon,
                NotControl = false,
                Parent = this,
                Visible = false
            };
            // ClientNPCInfo.Location 为 Shared（System.Drawing.Point），转换到 MirMath.Point 再赋值。
            SelectedNPCIcon.MouseEnter += (o, e) => ParentDialog.MouseLocation = new Point(ParentDialog.SelectedNPC.Info.Location.X, ParentDialog.SelectedNPC.Info.Location.Y);

            UserRadarDot = new MirImageControl
            {
                Library = Libraries.Prguse2,
                Index = 1350,
                Parent = this,
                Visible = false,
                NotControl = true
            };

            Players = new MirImageControl[Globals.MaxGroup];
            for (int i = 0; i < Players.Length; i++)
            {
                Players[i] = new MirImageControl
                {
                    Index = 1350,
                    Library = Libraries.Prguse2,
                    Parent = this,
                    NotControl = false,
                    Visible = false,
                };
            }

            BeforeDraw += (o, e) => OnBeforeDraw();
            MouseMove += UpdateBigMapCoordinates;
            ParentChanged += (o, e) => SetParent();
            MouseDown += OnMouseClick;
        }

        private void SetParent()
        {
            ParentDialog = (BigMapDialog)Parent;
        }

        private void UpdateBigMapCoordinates(object sender, MouseEventArgs e)
        {
            int MouseCoordsOnBigMap_MapValue_X = (int)((e.X - BigMap_MouseCoordsProcessing_OffsetX) / ScaleX);
            int MouseCoordsOnBigMap_MapValue_Y = (int)((e.Y - BigMap_MouseCoordsProcessing_OffsetY) / ScaleY);

            ParentDialog.MouseLocation = new Point(MouseCoordsOnBigMap_MapValue_X, MouseCoordsOnBigMap_MapValue_Y);
        }


        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            int X = (int)((e.X - BigMap_MouseCoordsProcessing_OffsetX) / ScaleX);
            int Y = (int)((e.Y - BigMap_MouseCoordsProcessing_OffsetY) / ScaleY);

            var path = GameScene.Scene.MapControl.PathFinder.FindPath(MapObject.User.CurrentLocation, new Point(X, Y));

            if (path == null || path.Count == 0)
            {
                GameScene.Scene.ChatDialog.ReceiveChat(GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CouldNotFindPath), ChatType.System);
            }
            else
            {
                GameScene.Scene.MapControl.CurrentPath = path;
                GameScene.Scene.MapControl.AutoPath = true;
            }
        }


        private void OnBeforeDraw()
        {
            if (!Parent.Visible) return;

            MouseMove -= UpdateBigMapCoordinates;

            BigMapRecord currentRecord;
            if (!GameScene.MapInfoList.TryGetValue(ParentDialog.TargetMapIndex, out currentRecord))
                return;

            ParentDialog.CurrentRecord = currentRecord;
            int index = currentRecord.MapInfo.BigMap;

            if (index <= 0)
                return;

            MouseMove += UpdateBigMapCoordinates;

            Size = Libraries.MiniMap.GetSize(index);
            Rectangle viewRect = new Rectangle(0, 0, Math.Min(568, Size.Width), Math.Min(380, Size.Height));

            viewRect.X = 14 + (568 - viewRect.Width) / 2;
            viewRect.Y = 52 + (380 - viewRect.Height) / 2;

            Location = viewRect.Location;
            Size = viewRect.Size;

            BigMap_MouseCoordsProcessing_OffsetX = DisplayLocation.X;
            BigMap_MouseCoordsProcessing_OffsetY = DisplayLocation.Y;

            ScaleX = Size.Width / (float)currentRecord.MapInfo.Width;
            ScaleY = Size.Height / (float)currentRecord.MapInfo.Height;

            Libraries.MiniMap.Draw(index, DisplayLocation, Size, Color.FromArgb(255, 255, 255));

            // 玩家/怪物雷达点（DXManager 渲染层未接驳）与组队成员定位（GroupDialog 迭代包6）→ 裁剪。
            // 保留玩家自身雷达点定位。
            float x = MapObject.User.CurrentLocation.X * ScaleX;
            float y = MapObject.User.CurrentLocation.Y * ScaleY;
            var s = UserRadarDot.Size;
            UserRadarDot.Location = new Point((int)x - s.Width / 2, (int)y - s.Height / 2);

            foreach (var record in ParentDialog.CurrentRecord.MovementButtons)
            {
                var movementInfo = record.Key;
                var button = record.Value;

                float bx = movementInfo.Location.X * ScaleX;
                float by = movementInfo.Location.Y * ScaleY;

                var ms = Libraries.MapLinkIcon.GetSize(button.Index);

                button.Location = new Point((int)bx - ms.Width / 2, (int)by - ms.Height / 2);
            }

            if (ParentDialog.SelectedNPC != null && SelectedNPCIcon.Visible)
            {
                float npx = ParentDialog.SelectedNPC.Info.Location.X * ScaleX;
                float npy = ParentDialog.SelectedNPC.Info.Location.Y * ScaleY;

                var ns = Libraries.MapLinkIcon.GetSize(SelectedNPCIcon.Index);

                SelectedNPCIcon.Location = new Point((int)npx - ns.Width / 2, (int)npy - ns.Height / 2);
            }
        }
    }

    public class BigMapNPCRow : MirButton
    {
        BigMapDialog ParentDialog;
        public ClientNPCInfo Info;
        public MirImageControl Icon;
        public MirLabel NameLabel;

        public BigMapNPCRow(ClientNPCInfo info)
        {
            Info = info;
            NotControl = false;
            Size = new Size(140, 25);
            Visible = false;
            Sound = SoundList.ButtonA;

            string name = Info.Name;
            if (Info.Name.Contains("_"))
            {
                string[] splitName = name.Split('_');
                name=string.Empty;
                for (int s = 0; s < splitName.Count(); s++)
                {
                    if (splitName[s] == string.Empty) continue;
                    if (s == splitName.Count() - 1)
                        name += splitName[s];
                    else name += $"({splitName[s]})";
                }
            }

            Icon = new MirImageControl
            {
                Index = info.Icon,
                Library = Libraries.MapLinkIcon,
                Location = new Point(1, 1),
                NotControl = true,
                Parent = this,
                Sound = SoundList.ButtonA
            };

            NameLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = Color.White,
                Location = new Point(23, 2),
                Parent = this,
                NotControl = true,
                Text = name,
                Sound = SoundList.ButtonA
            };

            MouseWheel += (o, e) => ParentDialog.BigMap_MouseWheel(o, e);
            ParentChanged += (o, e) => SetParent();
            Click += (o, e) => Select();
        }

        private void SetParent()
        {
            ParentDialog = (BigMapDialog)Parent;
        }

        private void Select()
        {
            ParentDialog.SelectedNPC = ParentDialog.SelectedNPC == this ? null : this;
        }
    }

    public class BigMapRecord
    {
        public int Index;
        public ClientMapInfo MapInfo;
        public Dictionary<ClientMovementInfo, MirButton> MovementButtons = new Dictionary<ClientMovementInfo, MirButton>();
        public List<BigMapNPCRow> NPCButtons = new List<BigMapNPCRow>();

        public BigMapRecord() { }
    }

}
