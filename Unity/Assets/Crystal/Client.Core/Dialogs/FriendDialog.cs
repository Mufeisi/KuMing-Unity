using System;
using System.Collections.Generic;
using System.Linq;
using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirSounds;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-06）：Client/MirScenes/Dialogs/FriendDialog.cs 好友/黑名单对话框控制树。
    // Title 199 好友 frame + Title 6 标题 + 好友/黑名单 tab（163/167 ↔ 164/166 状态切换）+ 12 行
    // FriendRow 翻页（Prguse2 240-245）+ Prguse 554-568 操作按钮 + MemoDialog 备注浮窗（Title 209）。
    // FriendRow 渲染：在线名绿/离线名白 + Selected 灰背景（BackColour），NameLabel 115×17。
    // 裁剪：MirInputBox/MirMessageBox（未移植弹窗）→ Add/Remove 点击留空；MailComposeLetterDialog
    // （邮件未移植）→ EmailButton 点击留空；WhisperButton 原填 ChatDialog 输入框（输入接管未接驳）
    // → 点击留空；FriendRow hover 备注浮层（CreateMemoLabel 未接驳）→ 覆写裁剪；
    // 网络交互（C.AddFriend/C.RemoveFriend/C.RefreshFriends/C.AddMemo）→ 点击留空（探针为渲染探针）。
    public sealed class FriendDialog : MirImageControl
    {
        public MirImageControl TitleLabel, FriendLabel, BlacklistLabel;
        public MirLabel PageNumberLabel;
        public MirButton CloseButton, PreviousButton, NextButton;
        public MirButton AddButton, RemoveButton, MemoButton, EmailButton, WhisperButton;
        public FriendRow[] Rows = new FriendRow[12];

        public List<ClientFriend> Friends = new List<ClientFriend>();
        private ClientFriend SelectedFriend = null;
        private bool _tempBlockedTab = false;
        private bool _blockedTab = false;

        public int SelectedIndex = 0;
        public int StartIndex = 0;
        public int Page = 0;

        public FriendDialog()
        {
            Index = 199;
            Library = Libraries.Title;
            Movable = true;
            Sort = true;
            Location = Center;

            TitleLabel = new MirImageControl
            {
                Index = 6,
                Library = Libraries.Title,
                Location = new Point(18, 8),
                Parent = this
            };

            FriendLabel = new MirImageControl
            {
                Index = 163,
                Library = Libraries.Title,
                Location = new Point(10, 34),
                Parent = this,
                Sound = SoundList.ButtonA,
            };
            FriendLabel.Click += (o, e) =>
            {
                _tempBlockedTab = false;
                UpdateDisplay();
            };

            BlacklistLabel = new MirImageControl
            {
                Index = 167,
                Library = Libraries.Title,
                Location = new Point(128, 34),
                Parent = this,
                Sound = SoundList.ButtonA,
            };
            BlacklistLabel.Click += (o, e) =>
            {
                _tempBlockedTab = true;
                UpdateDisplay();
            };

            PageNumberLabel = new MirLabel
            {
                Text = "",
                Parent = this,
                Size = new Size(83, 17),
                Location = new Point(87, 216),
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            };

            #region Buttons

            PreviousButton = new MirButton
            {
                Index = 240,
                HoverIndex = 241,
                PressedIndex = 242,
                Library = Libraries.Prguse2,
                Parent = this,
                Size = new Size(16, 16),
                Location = new Point(70, 218),
                Sound = SoundList.ButtonA,
            };
            PreviousButton.Click += (o, e) =>
            {
                Page--;
                if (Page < 0) Page = 0;
                StartIndex = Rows.Length * Page;
                Update();
            };

            NextButton = new MirButton
            {
                Index = 243,
                HoverIndex = 244,
                PressedIndex = 245,
                Library = Libraries.Prguse2,
                Parent = this,
                Size = new Size(16, 16),
                Location = new Point(171, 218),
                Sound = SoundList.ButtonA,
            };
            NextButton.Click += (o, e) =>
            {
                Page++;
                if (Page > Friends.Count() / Rows.Length) Page = Friends.Count() / Rows.Length;
                StartIndex = Rows.Length * Page;

                Update();
            };

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(237, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Hide();

            AddButton = new MirButton
            {
                Index = 554,
                HoverIndex = 555,
                PressedIndex = 556,
                Library = Libraries.Prguse,
                Location = new Point(60, 241),
                Parent = this,
                Sound = SoundList.ButtonA,
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AddFriend)
            };
            // AddButton.Click 原弹 MirInputBox 输入好友/黑名单名（未移植）→ 裁剪。

            RemoveButton = new MirButton
            {
                Index = 557,
                HoverIndex = 558,
                PressedIndex = 559,
                Library = Libraries.Prguse,
                Location = new Point(88, 241),
                Parent = this,
                Sound = SoundList.ButtonA,
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.RemoveFriend),
            };
            // RemoveButton.Click 原弹 MirMessageBox 确认移除（未移植）→ 裁剪。

            MemoButton = new MirButton
            {
                Index = 560,
                HoverIndex = 561,
                PressedIndex = 562,
                Library = Libraries.Prguse,
                Location = new Point(116, 241),
                Parent = this,
                Sound = SoundList.ButtonA,
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.FriendMemo)
            };
            MemoButton.Click += (o, e) =>
            {
                if (SelectedFriend == null) return;

                GameScene.Scene.MemoDialog.Friend = SelectedFriend;
                GameScene.Scene.MemoDialog.Show();
            };

            EmailButton = new MirButton
            {
                Index = 563,
                HoverIndex = 564,
                PressedIndex = 565,
                Library = Libraries.Prguse,
                Location = new Point(144, 241),
                Parent = this,
                Sound = SoundList.ButtonA,
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.FriendMail),
            };
            // EmailButton.Click 原打开 MailComposeLetterDialog.ComposeMail（邮件未移植）→ 裁剪。

            WhisperButton = new MirButton
            {
                Index = 566,
                HoverIndex = 567,
                PressedIndex = 568,
                Library = Libraries.Prguse,
                Location = new Point(172, 241),
                Parent = this,
                Sound = SoundList.ButtonA,
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.FriendWhisper)
            };
            // WhisperButton.Click 原填 ChatDialog 输入框 "/名字 "（输入接管未接驳）→ 裁剪。
            #endregion
        }

        private void UpdateDisplay()
        {
            if (!Visible) return;

            if (_blockedTab != _tempBlockedTab)
            {
                _blockedTab = _tempBlockedTab;

                if (_blockedTab)
                {
                    FriendLabel.Index = 164;
                    BlacklistLabel.Index = 166;
                }
                else
                {
                    FriendLabel.Index = 163;
                    BlacklistLabel.Index = 167;
                }
                Update();
                GameScene.Scene.MemoDialog.Hide();
            }
        }

        public void Update(bool clearSelection = true)
        {
            if (clearSelection)
                SelectedFriend = null;

            for (int i = 0; i < Rows.Length; i++)
            {
                if (Rows[i] != null) Rows[i].Dispose();

                Rows[i] = null;
            }

            List<ClientFriend> filteredFriends = new List<ClientFriend>();

            if (_blockedTab)
                filteredFriends = Friends.Where(e => e.Blocked).ToList();
            else
                filteredFriends = Friends.Where(e => !e.Blocked).ToList();

            int maxPage = filteredFriends.Count / Rows.Length + 1;
            if (maxPage < 1) maxPage = 1;

            PageNumberLabel.Text = (Page + 1) + " / " + maxPage;

            int maxIndex = filteredFriends.Count - 1;

            if (StartIndex > maxIndex) StartIndex = maxIndex;
            if (StartIndex < 0) StartIndex = 0;

            for (int i = 0; i < Rows.Length; i++)
            {
                if (i + StartIndex >= filteredFriends.Count) break;

                if (Rows[i] != null)
                    Rows[i].Dispose();

                Rows[i] = new FriendRow
                {
                    Friend = filteredFriends[i + StartIndex],
                    Location = new Point((i % 2) * 115 + 16, 55 + ((i) / 2) * 22),
                    Parent = this,
                };
                Rows[i].Click += (o, e) =>
                {
                    FriendRow row = (FriendRow)o;

                    if (row.Friend != SelectedFriend)
                    {
                        SelectedFriend = row.Friend;
                        SelectedIndex = FindSelectedIndex();
                        UpdateRows();
                    }
                };

                if (SelectedFriend != null)
                {
                    if (SelectedIndex == i)
                    {
                        SelectedFriend = Rows[i].Friend;
                    }
                }
            }
        }

        public void UpdateRows()
        {
            if (SelectedFriend == null)
            {
                if (Rows[0] == null) return;

                SelectedFriend = Rows[0].Friend;
            }

            for (int i = 0; i < Rows.Length; i++)
            {
                if (Rows[i] == null) continue;

                Rows[i].Selected = false;

                if (Rows[i].Friend == SelectedFriend)
                {
                    Rows[i].Selected = true;
                }

                Rows[i].UpdateInterface();
            }
        }

        public int FindSelectedIndex()
        {
            int selectedIndex = 0;
            if (SelectedFriend != null)
            {
                for (int i = 0; i < Rows.Length; i++)
                {
                    if (Rows[i] == null || SelectedFriend != Rows[i].Friend) continue;

                    selectedIndex = i;
                }
            }

            return selectedIndex;
        }

        public override void Hide()
        {
            if (!Visible) return;
            Visible = false;

            GameScene.Scene.MemoDialog.Hide();
        }
        public override void Show()
        {
            if (Visible) return;
            Visible = true;
            UpdateDisplay();
            // 原 Show 发包 C.RefreshFriends 拉好友列表（网络交互，探针直接注入 Friends）→ 裁剪。
        }
    }
    public sealed class FriendRow : MirControl
    {
        public ClientFriend Friend;
        public MirLabel NameLabel, OnlineLabel;

        public bool Selected = false;

        public FriendRow()
        {
            Sound = SoundList.ButtonA;
            Size = new Size(115, 17);

            BeforeDraw += FriendRow_BeforeDraw;

            NameLabel = new MirLabel
            {
                Location = new Point(0, 0),
                Size = new Size(115, 17),
                BackColour = Color.Empty,
                DrawFormat = TextFormatFlags.VerticalCenter,
                Parent = this,
                NotControl = true,
            };

            UpdateInterface();
        }

        void FriendRow_BeforeDraw(object sender, EventArgs e)
        {
            UpdateInterface();
        }

        public void UpdateInterface()
        {
            if (Friend == null) return;

            NameLabel.Text = Friend.Name;

            if (Friend.Online)
            {
                NameLabel.ForeColour = Color.Green;
            }
            else
            {
                NameLabel.ForeColour = Color.White;
            }

            if (Selected)
            {
                NameLabel.BackColour = Color.Gray;
            }
            else
            {
                NameLabel.BackColour = Color.Empty;
            }
        }

        // OnMouseEnter 原调 GameScene.CreateMemoLabel 显示备注浮层（未接驳）→ 裁剪。

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            Friend = null;
            NameLabel = null;

            Selected = false;
        }
    }
    public sealed class MemoDialog : MirImageControl
    {
        public MirTextBox MemoTextBox;
        public MirButton CloseButton, OKButton, CancelButton;

        public ClientFriend Friend;

        public MemoDialog()
        {
            Index = 209;
            Library = Libraries.Title;
            Movable = true;
            Sort = true;
            Location = Center;

            MemoTextBox = new MirTextBox
            {
                ForeColour = Color.White,
                Parent = this,
                Font = new Font(Settings.FontName, 8F),
                Location = new Point(15, 30),
                Size = new Size(165, 100),
            };
            MemoTextBox.MultiLine();

            OKButton = new MirButton
            {
                Index = 382,
                HoverIndex = 383,
                PressedIndex = 384,
                Parent = this,
                Library = Libraries.Title,
                Sound = SoundList.ButtonA,
                Location = new Point(30, 133)
            };
            // OKButton.Click 原发包 C.AddMemo 保存备注（网络交互，探针不驱动）→ 裁剪。

            CancelButton = new MirButton
            {
                Index = 385,
                HoverIndex = 386,
                PressedIndex = 387,
                Parent = this,
                Library = Libraries.Title,
                Sound = SoundList.ButtonA,
                Location = new Point(115, 133)
            };
            CancelButton.Click += (o, e) => Hide();

            #region Buttons

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(168, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Hide();

            #endregion
        }

        public override void Show()
        {
            if (Visible) return;
            Visible = true;


            if (Friend == null)
            {
                Hide();
                return;
            }

            MemoTextBox.Text = Friend.Memo;
            MemoTextBox.SetFocus();
            MemoTextBox.TextBox.SelectionLength = 0;
            MemoTextBox.TextBox.SelectionStart = MemoTextBox.Text.Length;
        }
    }
}
