using System;
using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirSounds;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-06）：Client/MirScenes/Dialogs/GuildDialog.cs 行会对话框控制树（精简两页）。
    // Prguse 180 行会 frame + Title 25 标题 + 左侧公告页（NoticeButton 93/94 + MirTextBox 多行公告
    // + Prguse2 197-206 滚动条）+ 右侧状态页（StatusButton 103/104 + Prguse 1850 底图 + 行会名/等级/成员数）
    // + Prguse2 360 关闭。
    // 裁剪（未移植控件/网络交互，渲染探针不驱动）：Members/Storage/Rank/Buff 四页（依赖
    // MirDropDownBox/MirItemCell GuildStorage/网络/行会 Buff 状态机）整页删除；MirInputBox/
    // MirMessageBox（未移植）→ 校验分支改直接 return；网络交互（C.RequestGuildInfo/
    // C.EditGuildMember/C.GuildBuffUpdate/C.GuildStorageGoldChange/C.EditGuildNotice）→ 删除；
    // Notice 滚动依赖 InputTextBox.GetFirstCharIndexFromLine/ScrollToCaret（纯 C# 输入模型无此 API）
    // → 滚动方法裁剪，公告文本由探针直设；SystemInformation.MouseWheelScrollDelta → 滚轮处理器删除；
    // MirButton.OnMoving（PositionBar 拖动）→ 订阅裁剪。
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
            Notice = new MirTextBox()
            {
                ForeColour = Color.White,
                Font = new Font(Settings.FontName, 8F),
                Enabled = false,
                Visible = true,
                Parent = NoticePage,
                Size = new Size(322, 330),
                Location = new Point(13, 1)
            };
            Notice.MultiLine();

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
            // NoticeUpButton.Click 原滚动公告（InputTextBox 无 ScrollToCaret）→ 裁剪。

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
            // NoticeDownButton.Click 原滚动公告（InputTextBox 无 ScrollToCaret）→ 裁剪。

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
            // OnMoving（拖动滚动条）→ 裁剪（渲染探针不驱动拖动）。
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
                if (MapControl.User.GuildName == "")
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

        public void RefreshInterface()
        {
            if (MapObject.User.GuildName == "")
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

            if (MapControl.User.GuildName == "")
            {
                // 原弹 MirMessageBox 提示未加入行会（未移植）→ 直接 return。
                return;
            }
            Visible = true;

            if (NoticePage.Visible)
                NoticeButton.Index = 94;
            if (StatusPage.Visible)
                StatusButton.Index = 104;
        }
    }
}
