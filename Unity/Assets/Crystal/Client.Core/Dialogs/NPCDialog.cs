using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirSounds;
using C = ClientPackets;
using Client.MirNetwork;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-05）：Client/MirScenes/Dialogs/NPCDialogs.cs NPCDialog 的对话窗核心。
    // NPC 对话文本分页渲染（最多 8 行）+ 选项按钮（R 正则 `{文本/@动作}` 点击发 C.CallNPC）
    // + 彩色文本（C 正则 `{文本/颜色名}`）+ 滚动条（Up/Down/PositionBar）+ 关闭按钮。
    // 裁剪的扩展（属后续迭代/依赖对话框）：BigButton 快捷按钮（B 正则）、Quest/Help 按钮、
    // Monster/NPC/Item 链接 tooltip、外部链接（L 正则）、MouseWheel 滚轮、PositionBar 拖拽移动、
    // Color.FromName 颜色名解析（MirMath.Color 无命名色，NewColour 统一黄）。
    public sealed class NPCDialog : MirImageControl
    {
        public static Regex R = new Regex(@"<((.*?)\/(\@.*?))>");
        public static Regex C = new Regex(@"{((.*?)\/(.*?))}");

        public MirButton CloseButton, UpButton, DownButton, PositionBar;
        public MirLabel[] TextLabel;
        public List<MirLabel> TextButtons;

        public MirLabel NameLabel;

        Font font = new Font(Settings.FontName, 9F);

        public List<string> CurrentLines = new List<string>();
        private int _index = 0;
        public int MaximumLines = 8;

        public NPCDialog()
        {
            Index = 995;
            Library = Libraries.Prguse;

            TextLabel = new MirLabel[30];
            TextButtons = new List<MirLabel>();
            Size = Size;
            AutoSize = false;

            Sort = true;

            NameLabel = new MirLabel
            {
                Text = "",
                Parent = this,
                Font = new Font(Settings.FontName, 10F, FontStyle.Bold),
                ForeColour = Color.FromArgb(222, 184, 135), // BurlyWood（MirMath.Color 无命名色）
                Location = new Point(30, 6),
                AutoSize = true
            };

            UpButton = new MirButton
            {
                Index = 197,
                HoverIndex = 198,
                PressedIndex = 199,
                Library = Libraries.Prguse2,
                Parent = this,
                Size = new Size(16, 14),
                Location = new Point(417, 34),
                Sound = SoundList.ButtonA,
                Visible = false
            };
            UpButton.Click += (o, e) =>
            {
                if (_index <= 0) return;

                _index--;

                NewText(CurrentLines, false);
                UpdatePositionBar();
            };

            DownButton = new MirButton
            {
                Index = 207,
                HoverIndex = 208,
                Library = Libraries.Prguse2,
                PressedIndex = 209,
                Parent = this,
                Size = new Size(16, 14),
                Location = new Point(417, 175),
                Sound = SoundList.ButtonA,
                Visible = false
            };
            DownButton.Click += (o, e) =>
            {
                if (_index + MaximumLines >= CurrentLines.Count) return;

                _index++;

                NewText(CurrentLines, false);
                UpdatePositionBar();
            };

            PositionBar = new MirButton
            {
                Index = 205,
                HoverIndex = 206,
                PressedIndex = 206,
                Library = Libraries.Prguse2,
                Location = new Point(417, 47),
                Parent = this,
                Sound = SoundList.None,
                Visible = false
            };

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(413, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Hide();
        }

        private void UpdatePositionBar()
        {
            if (CurrentLines.Count <= MaximumLines) return;

            int interval = 108 / (CurrentLines.Count - MaximumLines);

            int x = 417;
            int y = 48 + (_index * interval);

            if (y >= 155) y = 155;
            if (y <= 47) y = 47;

            PositionBar.Location = new Point(x, y);
        }

        private void ButtonClicked(string action)
        {
            if (action == "@Exit")
            {
                Hide();
                return;
            }

            if (CMain.Time <= GameScene.NPCTime) return;

            GameScene.NPCTime = CMain.Time + 5000;
            Network.Enqueue(new C.CallNPC { ObjectID = GameScene.NPCID, Key = $"[{action}]" });
        }

        public void NewText(List<string> lines, bool resetIndex = true)
        {
            Size = TrueSize;

            if (resetIndex)
            {
                _index = 0;
                CurrentLines = lines;
                UpdatePositionBar();
            }

            if (lines.Count > MaximumLines)
            {
                Index = 385;
                UpButton.Visible = true;
                DownButton.Visible = true;
                PositionBar.Visible = true;
            }
            else
            {
                Index = 384;
                UpButton.Visible = false;
                DownButton.Visible = false;
                PositionBar.Visible = false;
            }

            for (int i = 0; i < TextButtons.Count; i++)
                TextButtons[i].Dispose();

            for (int i = 0; i < TextLabel.Length; i++)
            {
                if (TextLabel[i] != null) TextLabel[i].Text = "";
            }

            TextButtons.Clear();

            int lastLine = lines.Count > MaximumLines ? ((MaximumLines + _index) > lines.Count ? lines.Count : (MaximumLines + _index)) : lines.Count;

            for (int i = _index; i < lastLine; i++)
            {
                TextLabel[i] = new MirLabel
                {
                    Font = font,
                    DrawFormat = TextFormatFlags.WordBreak,
                    Visible = true,
                    Parent = this,
                    Size = new Size(420, 20),
                    Location = new Point(8, 34 + (i - _index) * 18),
                    NotControl = true
                };

                if (i >= lines.Count)
                {
                    TextLabel[i].Text = string.Empty;
                    continue;
                }

                string currentLine = lines[i];
                List<Match> matchList = R.Matches(currentLine).Cast<Match>().ToList();
                matchList.AddRange(C.Matches(currentLine).Cast<Match>());

                int oldLength = currentLine.Length;

                foreach (Match match in matchList.OrderBy(o => o.Index).ToList())
                {
                    int offSet = oldLength - currentLine.Length;

                    bool hasMultipleGroups = match.Groups.Count > 3 && match.Groups[2].Captures.Count > 0 && match.Groups[3].Captures.Count > 0;

                    if (hasMultipleGroups)
                    {
                        Capture capture = match.Groups[1].Captures[0];
                        string txt = match.Groups[2].Captures[0].Value;
                        string action = match.Groups[3].Captures[0].Value;

                        currentLine = currentLine.Remove(capture.Index - 1 - offSet, capture.Length + 2).Insert(capture.Index - 1 - offSet, txt);
                        string text2 = currentLine.Substring(0, capture.Index - 1 - offSet) + " ";
                        Size size2 = TextRenderer.MeasureText(CMain.Graphics, text2, TextLabel[i].Font, TextLabel[i].Size, TextFormatFlags.TextBoxControl);

                        if (R.Match(match.Value).Success)
                            NewButton(txt, action, TextLabel[i].Location.Add(new Point(size2.Width - 10, 0)));

                        if (C.Match(match.Value).Success)
                            NewColour(txt, action, TextLabel[i].Location.Add(new Point(size2.Width - 10, 0)));
                    }
                }
                TextLabel[i].Text = currentLine;
            }
        }

        private void NewButton(string text, string key, Point p)
        {
            MirLabel temp = new MirLabel
            {
                AutoSize = true,
                Visible = true,
                Parent = this,
                Location = p,
                Text = text,
                ForeColour = Color.Yellow,
                Font = font
            };

            temp.MouseEnter += (o, e) => temp.ForeColour = Color.Red;
            temp.MouseLeave += (o, e) => temp.ForeColour = Color.Yellow;
            temp.MouseDown += (o, e) => temp.ForeColour = Color.Yellow;
            temp.MouseUp += (o, e) => temp.ForeColour = Color.Red;

            temp.Click += (o, e) =>
            {
                ButtonClicked(key);
            };

            TextButtons.Add(temp);
        }

        private void NewColour(string text, string colour, Point p)
        {
            // 旧客户端用 Color.FromName(colour) 解析颜色名；MirMath.Color 无命名色，
            // 统一黄（迭代包3 裁剪颜色名映射，C 正则彩色文本仍保留渲染路径）。
            Color textColour = Color.Yellow;

            MirLabel temp = new MirLabel
            {
                AutoSize = true,
                Visible = true,
                Parent = this,
                Location = p,
                Text = text,
                ForeColour = textColour,
                Font = font
            };

            TextButtons.Add(temp);
        }

        public override void Show()
        {
            GameScene.Scene.InventoryDialog.Location = new Point(Size.Width + 5, 0);
            Visible = true;
        }
    }
}
