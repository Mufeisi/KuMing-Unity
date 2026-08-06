using System;
using System.Collections.Generic;
using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirSounds;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-06）：Client/MirScenes/Dialogs/MainDialogs.cs:1764 MiniMapDialog 小地图控制树。
    // 大/小两档切换（Index 2090/2091）+ mmap 图集视口渲染（MiniMap_BeforeDraw）+ 地图/坐标标签
    // + 大地图/邮件切换按钮 + AMode/PMode 模式标签 + NPC 任务图标（裁剪）。
    // 裁剪：MailButton（MailListDialog 未移植）；CMain.InputKeys.GetKey（Unity 无 KeybindOptions，
    // Hint 按键名传空）；DXManager 雷达点绘制 + npc.GetAvailableQuests 任务图标（渲染层/NPCObject 未接驳）。
    public sealed class MiniMapDialog : MirImageControl
    {
        public MirImageControl LightSetting, NewMail;
        public MirButton ToggleButton, BigMapButton, MailButton;
        public MirLabel LocationLabel, MapNameLabel;
        private float _fade = 1F;
        private bool _bigMode = true;

        public MirLabel AModeLabel, PModeLabel;

        public List<MirLabel> QuestIcons = new List<MirLabel>();

        public MiniMapDialog()
        {
            Index = 2090;
            Library = Libraries.Prguse;
            Location = new Point(Settings.ScreenWidth - 126, 0);
            PixelDetect = true;

            BeforeDraw += MiniMap_BeforeDraw;
            AfterDraw += MiniMapDialog_AfterDraw;

            MapNameLabel = new MirLabel
            {
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                Parent = this,
                Size = new Size(120, 18),
                Location = new Point(2, 2),
                NotControl = true,
            };

            LocationLabel = new MirLabel
            {
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                Parent = this,
                Size = new Size(56, 18),
                Location = new Point(46, 131),
                NotControl = true,
            };

            MailButton = new MirButton
            {
                Index = 2099,
                HoverIndex = 2100,
                PressedIndex = 2101,
                Parent = this,
                Location = new Point(4, 131),
                Library = Libraries.Prguse,
                Sound = SoundList.ButtonA,
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.Mail)
            };
            // MailButton.Click 原打开 GameScene.Scene.MailListDialog.Toggle()（Mail 未移植）→ 裁剪。

            NewMail = new MirImageControl
            {
                Index = 544,
                Location = new Point(5, 132),
                Parent = this,
                Library = Libraries.Prguse,
                Visible = false,
                NotControl = true
            };

            BigMapButton = new MirButton
            {
                Index = 2096,
                HoverIndex = 2097,
                PressedIndex = 2098,
                Parent = this,
                Location = new Point(25, 131),
                Library = Libraries.Prguse,
                Sound = SoundList.ButtonA,
                // 原 Hint 拼接 CMain.InputKeys.GetKey(KeybindOptions.Bigmap)（Unity 无 KeybindOptions）→ 按键名空。
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.BigMapKey, "")
            };
            BigMapButton.Click += (o, e) => GameScene.Scene.BigMapDialog.Toggle();

            ToggleButton = new MirButton
            {
                Index = 2102,
                HoverIndex = 2103,
                PressedIndex = 2104,
                Parent = this,
                Location = new Point(109, 3),
                Library = Libraries.Prguse,
                Sound = SoundList.ButtonA,
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.MiniMapKey, "")
            };
            ToggleButton.Click += (o, e) => Toggle();

            LightSetting = new MirImageControl
            {
                Index = 2093,
                Library = Libraries.Prguse,
                Parent = this,
                Location = new Point(102, 131),
            };


            AModeLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = Color.Yellow,
                OutLineColour = Color.Black,
                Parent = this,
                Location = new Point(115, 125)
            };

            PModeLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = Color.Yellow,
                OutLineColour = Color.Black,
                Parent = this,
                Location = new Point(230, 125),
                Visible = false
            };
        }

        private void MiniMapDialog_AfterDraw(object sender, EventArgs e)
        {

        }

        private void MiniMap_BeforeDraw(object sender, EventArgs e)
        {

            foreach (var icon in QuestIcons)
                icon.Dispose();

            QuestIcons.Clear();

            MapControl map = GameScene.Scene.MapControl;
            if (map == null) return;

            if (map.MiniMap == 0 && Index != 2091)
            {
                SetSmallMode();
            }
            else if (map.MiniMap > 0 && _bigMode && Index == 2091)
            {
                SetBigMode();
            }

            if (map.MiniMap <= 0 || Index != 2090 || Libraries.MiniMap == null)
            {
                return;
            }

            Rectangle viewRect = new Rectangle(0, 0, 120, 108);
            Point drawLocation = Location;
            drawLocation.Offset(3, 22);

            Size miniMapSize = Libraries.MiniMap.GetSize(map.MiniMap);
            float scaleX = miniMapSize.Width / (float)map.Width;
            float scaleY = miniMapSize.Height / (float)map.Height;

            viewRect.Location = new Point(
                (int)(scaleX * MapObject.User.CurrentLocation.X) - viewRect.Width / 2,
                (int)(scaleY * MapObject.User.CurrentLocation.Y) - viewRect.Height / 2);

            if (viewRect.Right >= miniMapSize.Width)
                viewRect.X = miniMapSize.Width - viewRect.Width;
            if (viewRect.Bottom >= miniMapSize.Height)
                viewRect.Y = miniMapSize.Height - viewRect.Height;

            if (viewRect.X < 0) viewRect.X = 0;
            if (viewRect.Y < 0) viewRect.Y = 0;

            Libraries.MiniMap.Draw(map.MiniMap, viewRect, drawLocation, Color.FromArgb(255, 255, 255), _fade);

            // 对象雷达点 + NPC 任务图标（DXManager 渲染层未接驳 / NPCObject.GetAvailableQuests 未移植）→ 裁剪。
        }

        public void Toggle()
        {
            if (_fade == 0F)
            {
                _bigMode = true;
                SetBigMode();
                _fade = 1F;
            }
            else
            {
                _bigMode = false;
                SetSmallMode();
                _fade = 0;
            }

            Redraw();
        }

        private void SetSmallMode()
        {
            Index = 2091;
            int y = Size.Height - 23;
            MailButton.Location = new Point(4, y);
            NewMail.Location = new Point(5, y + 1);
            BigMapButton.Location = new Point(25, y);
            LocationLabel.Location = new Point(46, y);
            LightSetting.Location = new Point(102, y);

            GameScene.Scene.DuraStatusPanel.Location = new Point(GameScene.Scene.MiniMapDialog.Location.X + 86,
            GameScene.Scene.MiniMapDialog.Size.Height);
        }

        private void SetBigMode()
        {
            Index = 2090;
            int y = Size.Height - 23;
            MailButton.Location = new Point(4, y);
            NewMail.Location = new Point(5, y + 1);
            BigMapButton.Location = new Point(25, y);
            LocationLabel.Location = new Point(46, y);
            LightSetting.Location = new Point(102, y);

            GameScene.Scene.DuraStatusPanel.Location = new Point(GameScene.Scene.MiniMapDialog.Location.X + 86,
            GameScene.Scene.MiniMapDialog.Size.Height);
        }

        public void Process()
        {
            MapControl map = GameScene.Scene.MapControl;
            if (map == null) return;

            MapNameLabel.Text = map.Title;
            // 原 Functions.PointToString（Shared 侧签名是 System.Drawing.Point）→ 本地格式化 MirMath.Point。
            LocationLabel.Text = string.Format("{0}, {1}", MapObject.User.CurrentLocation.X, MapObject.User.CurrentLocation.Y);

            GameScene.Scene.MainDialog.SModeLabel.Location = new Point((GameScene.Scene.MiniMapDialog.Location.X - 3) - GameScene.Scene.MainDialog.Location.X,
            (GameScene.Scene.MiniMapDialog.Size.Height + 150) - Settings.ScreenHeight);
            GameScene.Scene.MainDialog.AModeLabel.Location = new Point((GameScene.Scene.MiniMapDialog.Location.X - 3) - GameScene.Scene.MainDialog.Location.X,
            (GameScene.Scene.MiniMapDialog.Size.Height + 165) - Settings.ScreenHeight);
            GameScene.Scene.MainDialog.PModeLabel.Location = new Point((GameScene.Scene.MiniMapDialog.Location.X - 3) - GameScene.Scene.MainDialog.Location.X,
            (GameScene.Scene.MiniMapDialog.Size.Height + 180) - Settings.ScreenHeight);

            if (GameScene.Scene.NewMail)
            {
                double time = (CMain.Time) / 100D;

                if (Math.Round(time) % 10 < 5 || GameScene.Scene.NewMailCounter >= 10)
                {
                    NewMail.Visible = true;
                }
                else
                {
                    if (NewMail.Visible)
                    {
                        GameScene.Scene.NewMailCounter++;
                    }

                    NewMail.Visible = false;
                }
            }
            else
            {
                NewMail.Visible = false;
            }
        }
    }
}
