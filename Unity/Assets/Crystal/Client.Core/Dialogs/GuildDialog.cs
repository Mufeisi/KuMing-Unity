using System;
using System.Collections.Generic;
using Crystal.Client.Core.MirMath;
using Client;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirSounds;
using C = ClientPackets;

namespace Client.MirScenes.Dialogs
{
    // 行会对话框控制树（8-6-3 接回网络/滚动）：Client/MirScenes/Dialogs/GuildDialog.cs 精简两页
    // （公告/状态）。Prguse 180 行会 frame + Title 25 标题 + 左侧公告页（NoticeButton 93/94 +
    // 25 行公告窗口标签 + Prguse2 197-206 滚动条）+ 右侧状态页（StatusButton 103/104 + Prguse 1850
    // 底图 + 行会名/等级/成员数）+ Prguse2 360 关闭。
    // 网络交互（8-6-3 接回）：Show → C.RequestGuildInfo{Type=0} 拉公告（NoticeChanged 标记 + 5s 节流，
    // 旧客户端同款）；S.GuildStatus/GuildNoticeChange/GuildMemberChange/GuildInvite/GuildExpGain
    // 由 GameSession 分发（GuildStatus 同步 User.GuildName/GuildRankName）。
    // 公告滚动（纯 C#）：原 InputTextBox.GetFirstCharIndexFromLine/ScrollToCaret（WinForms API）不可用，
    // 改为 25 行标签窗口——Notice 保留 MirTextBox 作数据源（NetProbe 直写 Notice.Text），实际渲染
    // 委托 NoticeLineLabels[i] 逐行标签（TextGlyphBuilder 不折行，单段含 \n 文本只画一行）。
    // Up/Down 按钮 + PositionBar 拖动（OnMoving）驱动 NoticeScrollIndex；长行不折行（渲染限制，
    // 滚动窗口计数不受影响）。SystemInformation.MouseWheelScrollDelta 滚轮处理器裁剪（移动端无滚轮）。
    // 裁剪（同旧端口）：Members/Storage/Rank/Buff 四页整页删除（依赖 MirDropDownBox/MirItemCell
    // GuildStorage/网络/行会 Buff 状态机）；Buff 契约保留空列表（UserObject.cs:667-675 引用）。
    public sealed class GuildDialog : MirImageControl
    {
        #region NoticeBase
        public MirLabel GuildName;
        public MirButton CloseButton;
        #endregion

        #region GuildLeft
        public MirButton NoticeButton;
        public MirImageControl NoticePage;
        public MirImageControl TitleLabel;
        #endregion

        #region GuildRight
        public MirButton StatusButton;
        public MirImageControl StatusPage, StatusPageBase;
        #endregion

        #region DataValues
        public byte Level;
        public long Experience;
        public long MaxExperience;
        public int MemberCount;
        public int MaxMembers;
        // 行会 Buff 契约（UserObject.cs:667-675 引用，旧桩保留下来的空列表）：Buff 页未移植。
        public List<GuildBuff> EnabledBuffs = new List<GuildBuff>();

        public GuildBuffInfo FindGuildBuffInfo(int Index)
        {
            // 原查找 EnabledBuffs 中 Info；Buff 页未移植，返回 null（与旧桩契约一致）。
            return null;
        }
        #endregion

        #region NoticePagePub
        public int NoticeScrollIndex = 0;
        public MirButton NoticeUpButton, NoticeDownButton, NoticePositionBar;
        public MirTextBox Notice;
        public const int NoticeLineCount = 25; // 可见公告行数（旧客户端 322×330 / 行高 13 的窗口）
        public MirLabel[] NoticeLineLabels;
        // 公告全文（滚动数学数据源；渲染窗口 = NoticeLineLabels 25 行）。
        private List<string> _noticeLines = new List<string>();
        public string[] NoticeLines => _noticeLines.ToArray();
        public static bool NoticeChanged = true;  // 公告已变更 → 下次 Show 拉取（旧客户端同款）
        public static bool MembersChanged = true; // 成员表已变更（成员页未移植，占位契约同旧客户端）
        public static long LastNoticeRequest = 0; // 拉取节流（旧客户端 5s）
        #endregion

        #region StatusPagePub
        public MirLabel StatusLevelLabel;
        public MirLabel StatusHeaders;
        public MirLabel StatusGuildName, StatusLevel, StatusMembers;
        #endregion

        public GuildDialog()
        {
            Index = 180;
            Library = Libraries.Prguse;
            Movable = true;
            Sort = true;
            Location = Center;

            BeforeDraw += (o, e) => RefreshInterface();

            #region TabUI

            TitleLabel = new MirImageControl
            {
                Index = 25,
                Library = Libraries.Title,
                Location = new Point(18, 9),
                Parent = this
            };

            NoticeButton = new MirButton
            {
                Library = Libraries.Title,
                Index = 93,
                PressedIndex = 94,
                Sound = SoundList.ButtonA,
                Parent = this,
                Location = new Point(20, 38)
            };
            NoticeButton.Click += (o, e) => LeftDialog(0);

            StatusButton = new MirButton
            {
                Library = Libraries.Title,
                Parent = this,
                Index = 103,
                Location = new Point(501, 38),
                Sound = SoundList.ButtonA,
            };
            StatusButton.Click += (o, e) => RightDialog(0);

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(565, 4),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA
            };
            CloseButton.Click += (o, e) => Hide();
            #endregion

            #region NoticePageUI
            NoticePage = new MirImageControl()
            {
                Parent = this,
                Size = new Size(352, 372),
                Location = new Point(0, 60),
                Visible = true
            };
            // 25 行公告窗口标签（渲染主体）：TextGlyphBuilder 不折行，逐行独立 MirLabel（同聊天行模式）。
            NoticeLineLabels = new MirLabel[NoticeLineCount];
            for (int i = 0; i < NoticeLineCount; i++)
            {
                NoticeLineLabels[i] = new MirLabel
                {
                    ForeColour = Color.White,
                    Font = new Font(Settings.FontName, 8F),
                    Visible = true,
                    NotControl = true,
                    Parent = NoticePage,
                    Size = new Size(322, 13),
                    Location = new Point(13, 1 + i * 13)
                };
            }
            // Notice 保留作数据源（NetProbe 直写 Notice.Text / Notice.MultiText 整表），渲染委托标签。
            Notice = new MirTextBox()
            {
                ForeColour = Color.White,
                Font = new Font(Settings.FontName, 8F),
                Enabled = false,
                Visible = false,
                Parent = NoticePage,
                Size = new Size(322, 330),
                Location = new Point(13, 1)
            };
            Notice.MultiLine();
            // Notice 文本直写 → 同步全文 + 窗口标签。
            Notice.TextBox.TextChanged += (o, e) => SyncFromTextBox();

            NoticeUpButton = new MirButton
            {
                HoverIndex = 198,
                Index = 197,
                Visible = true,
                Library = Libraries.Prguse2,
                Location = new Point(337, 1),
                Size = new Size(16, 14),
                Parent = NoticePage,
                PressedIndex = 199,
                Sound = SoundList.ButtonA
            };
            // 公告上滚（8-6-3 接回）：旧客户端 NoticeUpButton.Click 逐字移植（无 ScrollToCaret → 窗口平移）。
            NoticeUpButton.Click += (o, e) =>
            {
                if (NoticeScrollIndex == 0) return;
                NoticeScrollIndex--;
                RefreshNotice();
            };

            NoticeDownButton = new MirButton
            {
                HoverIndex = 208,
                Index = 207,
                Visible = true,
                Library = Libraries.Prguse2,
                Location = new Point(337, 318),
                Size = new Size(16, 14),
                Parent = NoticePage,
                PressedIndex = 209,
                Sound = SoundList.ButtonA
            };
            // 公告下滚（8-6-3 接回）：末端守卫 + 窗口平移。
            NoticeDownButton.Click += (o, e) =>
            {
                if (NoticeScrollIndex >= Math.Max(0, _noticeLines.Count - NoticeLineCount)) return;
                NoticeScrollIndex++;
                RefreshNotice();
            };

            NoticePositionBar = new MirButton
            {
                Index = 206,
                Library = Libraries.Prguse2,
                Location = new Point(337, 16),
                Parent = NoticePage,
                Movable = true,
                Visible = true,
                Sound = SoundList.None
            };
            // 滚动条拖动（8-6-3 接回）：OnMoving 按 y 反算 NoticeScrollIndex（纯 C# 版 NoticePositionBar_OnMoving）。
            NoticePositionBar.OnMoving += NoticePositionBar_OnMoving;
            #endregion

            #region StatusPageUI
            StatusPage = new MirImageControl()
            {
                Parent = this,
                Size = new Size(230, 372),
                Location = new Point(355, 60),
                Visible = true
            };
            StatusPageBase = new MirImageControl()
            {
                Parent = StatusPage,
                Library = Libraries.Prguse,
                Index = 1850,
                Visible = true,
                Location = new Point(10, 2)
            };
            StatusPage.BeforeDraw += (o, e) =>
            {
                if (string.IsNullOrEmpty(MapControl.User.GuildName))
                {
                    StatusGuildName.Text = "";
                    StatusLevel.Text = "";
                    StatusMembers.Text = "";
                }
                else
                {
                    StatusGuildName.Text = string.Format("{0}", MapObject.User.GuildName);
                    StatusLevel.Text = string.Format("{0}", Level);
                    StatusMembers.Text = string.Format("{0}{1}", MemberCount, MaxMembers == 0 ? "" : ("/" + MaxMembers.ToString()));
                }
            };
            StatusHeaders = new MirLabel()
            {
                Location = new Point(7, 47),
                DrawFormat = TextFormatFlags.Right,
                Size = new Size(75, 300),
                NotControl = true,
                Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.GuildNameLevelMembers),
                Visible = true,
                Parent = StatusPage,
                ForeColour = Color.Gray,
            };
            StatusGuildName = new MirLabel()
            {
                Location = new Point(82, 47),
                Size = new Size(120, 200),
                NotControl = true,
                Text = "",
                Visible = true,
                Parent = StatusPage
            };
            StatusLevel = new MirLabel()
            {
                Location = new Point(82, 73),
                Size = new Size(120, 200),
                NotControl = true,
                Text = "",
                Visible = true,
                Parent = StatusPage
            };
            StatusMembers = new MirLabel()
            {
                Location = new Point(82, 99),
                Size = new Size(120, 200),
                NotControl = true,
                Text = "",
                Visible = true,
                Parent = StatusPage
            };
            #endregion
        }

        // 公告整表回声（S.GuildNoticeChange → GameSession.GuildNoticeChange）：存全文 + 归零滚动索引 + 渲染首屏。
        public void NoticeChange(List<string> newnotice)
        {
            NoticeScrollIndex = 0;
            _noticeLines = newnotice ?? new List<string>();
            Notice.MultiText = _noticeLines.ToArray(); // 数据源同步（TextChanged → SyncFromTextBox，幂等）
            NoticeChanged = false;
            RefreshNotice(); // 同文本不触发 TextChanged 的兜底
        }

        // 公告滚动刷新：窗口标签 = _noticeLines[NoticeScrollIndex..+25]，滚动条位置同步。
        public void RefreshNotice()
        {
            int total = _noticeLines.Count;
            int maxIndex = Math.Max(0, total - NoticeLineCount);
            if (NoticeScrollIndex > maxIndex) NoticeScrollIndex = maxIndex;
            for (int i = 0; i < NoticeLineCount; i++)
            {
                int src = NoticeScrollIndex + i;
                NoticeLineLabels[i].Text = src < total ? _noticeLines[src] : "";
            }
            UpdateNoticeScrollPosition();
        }

        // Notice 文本直写同步（NetProbe/探针）：全文读回 _noticeLines，再渲染窗口。
        private void SyncFromTextBox()
        {
            var src = Notice.MultiText;
            _noticeLines = src == null ? new List<string>() : new List<string>(src);
            if (_noticeLines.Count == 1 && _noticeLines[0] == "") _noticeLines.Clear();
            RefreshNotice();
        }

        // 滚动条位置（旧客户端 289px 轨道 / (总数-25) 区间，y ∈ [16, 298]）。
        private void UpdateNoticeScrollPosition()
        {
            int maxIndex = Math.Max(0, _noticeLines.Count - NoticeLineCount);
            if (maxIndex == 0)
            {
                NoticePositionBar.Location = new Point(337, 16);
                return;
            }
            int interval = 289 / maxIndex;
            int y = 16 + (NoticeScrollIndex * interval);
            if (y > 298) y = 298;
            if (y < 16) y = 16;
            NoticePositionBar.Location = new Point(337, y);
        }

        // 滚动条拖动（OnMoving）：按 y 反算 NoticeScrollIndex（旧客户端 NoticePositionBar_OnMoving 纯 C# 版）。
        private void NoticePositionBar_OnMoving(object sender, MouseEventArgs e)
        {
            int maxIndex = Math.Max(0, _noticeLines.Count - NoticeLineCount);
            if (maxIndex == 0) return;
            int interval = 289 / maxIndex;
            int location = NoticePositionBar.Location.Y - 16;
            int idx = interval > 0 ? location / interval : 0;
            NoticeScrollIndex = Math.Max(0, Math.Min(idx, maxIndex));
            RefreshNotice();
        }

        public void RefreshInterface()
        {
            if (string.IsNullOrEmpty(MapObject.User.GuildName))
            {
                Hide();
                return;
            }
        }

        #region DialogPages
        public void RightDialog(byte Rpageid)
        {
            StatusPage.Visible = false;

            StatusButton.Index = 103;

            switch (Rpageid)
            {
                case 0:
                    StatusPage.Visible = true;
                    StatusButton.Index = 104;
                    break;
            }
        }
        public void LeftDialog(byte Lpageid)
        {
            NoticePage.Visible = false;

            NoticeButton.Index = 93;

            switch (Lpageid)
            {
                case 0:
                    NoticePage.Visible = true;
                    NoticeButton.Index = 94;
                    break;
            }
        }
        #endregion

        public override void Show()
        {
            if (Visible) return;

            if (string.IsNullOrEmpty(MapControl.User.GuildName))
            {
                // 未加入行会（8-6-3 接回）：MirMessageBox 提示后返回（旧客户端同款）。
                var box = new MirMessageBox(
                    GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.NotInGuild),
                    MirMessageBoxButtons.OK);
                box.Show();
                return;
            }
            Visible = true;

            if (NoticePage.Visible)
                NoticeButton.Index = 94;
            if (StatusPage.Visible)
                StatusButton.Index = 104;

            // 公告变更 → 拉整表（旧客户端 5s 节流）。
            if (NoticeChanged && LastNoticeRequest < CMain.Time)
            {
                LastNoticeRequest = CMain.Time + 5000;
                NoticeChanged = false;
                Network.Enqueue(new C.RequestGuildInfo { Type = 0 });
            }
        }
    }
}
