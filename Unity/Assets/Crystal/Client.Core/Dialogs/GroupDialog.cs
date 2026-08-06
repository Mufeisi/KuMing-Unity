using System;
using System.Collections.Generic;
using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirSounds;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-06）：Client/MirScenes/Dialogs/GroupDialog.cs 组队对话框控制树。
    // Prguse 120 组队 frame + Title 5 标题 + Prguse2 360 关闭 + 允许组队 Switch（114-119 状态切换）
    // + 添加/移除成员按钮（Title 133-135/136-138，未入队/队长态切 130-132）+ 成员 8 格标签
    // （Globals.MaxGroup）+ 成员 Hint（GroupMembersMap 在线位置）。
    // 裁剪：MirInputBox（未移植弹窗基类）→ Add/Del 私有弹输入框方法删除；
    // 网络交互（C.SwitchGroup/C.AddMember/C.DelMember）→ 点击留空（探针为渲染探针）。
    public sealed class GroupDialog : MirImageControl
    {
        public static bool AllowGroup;
        public static List<string> GroupList = new List<string>();
        public static Dictionary<string, string> GroupMembersMap = new Dictionary<string, string>();

        public MirImageControl TitleLabel;
        public MirButton SwitchButton, CloseButton, AddButton, DelButton;
        public MirLabel[] GroupMembers;

        public GroupDialog()
        {
            Index = 120;
            Library = Libraries.Prguse;
            Movable = true;
            Sort = true;
            Location = Center;

            GroupMembers = new MirLabel[Globals.MaxGroup];

            GroupMembers[0] = new MirLabel
            {
                AutoSize = true,
                Location = new Point(16, 33),
                Parent = this,
                NotControl = false,
            };

            for (int i = 1; i < GroupMembers.Length; i++)
            {
                GroupMembers[i] = new MirLabel
                {
                    AutoSize = true,
                    Location = new Point(((i + 1) % 2) * 100 + 16, 55 + ((i - 1) / 2) * 20),
                    Parent = this,
                    NotControl = false,
                };
            }

            TitleLabel = new MirImageControl
            {
                Index = 5,
                Library = Libraries.Title,
                Location = new Point(18, 8),
                Parent = this
            };

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(206, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Hide();

            SwitchButton = new MirButton
            {
                HoverIndex = 115,
                Index = 114,
                Location = new Point(25, 219),
                Library = Libraries.Prguse,
                Parent = this,
                PressedIndex = 116,
                Sound = SoundList.ButtonA,
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.GroupSwitch)
            };
            // SwitchButton.Click 原发包 C.SwitchGroup（网络交互，探针不驱动）→ 裁剪。

            AddButton = new MirButton
            {
                HoverIndex = 134,
                Index = 133,
                Location = new Point(70, 219),
                Library = Libraries.Title,
                Parent = this,
                PressedIndex = 135,
                Sound = SoundList.ButtonA,
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.GroupAdd)
            };
            // AddButton.Click 原弹 MirInputBox 输入成员名（未移植）→ 裁剪。

            DelButton = new MirButton
            {
                HoverIndex = 137,
                Index = 136,
                Location = new Point(140, 219),
                Library = Libraries.Title,
                Parent = this,
                PressedIndex = 138,
                Sound = SoundList.ButtonA,
                Hint = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.GroupRemove)
            };
            // DelButton.Click 原弹 MirInputBox 输入成员名（未移植）→ 裁剪。

            BeforeDraw += GroupPanel_BeforeDraw;

            GroupList.Clear();
        }

        private void GroupPanel_BeforeDraw(object sender, EventArgs e)
        {
            if (GroupList.Count == 0)
            {
                AddButton.Index = 130;
                AddButton.HoverIndex = 131;
                AddButton.PressedIndex = 132;
            }
            else
            {
                AddButton.Index = 133;
                AddButton.HoverIndex = 134;
                AddButton.PressedIndex = 135;
            }
            if (GroupList.Count > 0 && GroupList[0] != MapObject.User.Name)
            {
                AddButton.Visible = false;
                DelButton.Visible = false;
            }
            else
            {
                AddButton.Visible = true;
                DelButton.Visible = true;
            }

            if (AllowGroup)
            {
                SwitchButton.Index = 117;
                SwitchButton.HoverIndex = 118;
                SwitchButton.PressedIndex = 119;
            }
            else
            {
                SwitchButton.Index = 114;
                SwitchButton.HoverIndex = 115;
                SwitchButton.PressedIndex = 116;
            }

            for (int i = 0; i < GroupMembers.Length; i++)
                GroupMembers[i].Text = i >= GroupList.Count ? string.Empty : GroupList[i];

            foreach (var player in GroupMembersMap)
            {
                for (int i = 0; i < GroupMembers.Length; i++)
                {
                    string playersName = GroupMembers[i].Text;

                    if (player.Key == playersName)
                        GroupMembers[i].Hint = player.Value;
                }
            }
        }

        // 原 public AddMember(string) 发包 C.AddMember（网络交互，探针不驱动）→ 裁剪。
    }
}
