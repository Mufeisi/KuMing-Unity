using System;
using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirSounds;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-06）：Client/MirScenes/Dialogs/MainDialogs.cs:3270 MagicButton 渲染核心
    // 角色技能页单格：MagIcon2 图标按钮 + 等级/经验/技能名/快捷键标签 + Prguse2 冷却遮罩。
    // 裁剪（属后续迭代）：SkillButton.Click 原打开 AssignKeyPanel（未移植，点击改发施法的按键
    // 分配在快捷键条上完成）；Spell→Hint 描述 switch（约 300 行）简化为显示技能名；
    // HeroMagic 分支保留字段（MirGridType.HeroEquipment 走不到）。
    public sealed class MagicButton : MirControl
    {
        public MirImageControl LevelImage, ExpImage;
        public MirButton SkillButton;
        public MirLabel LevelLabel, NameLabel, ExpLabel, KeyLabel;
        public ClientMagic Magic;
        public MirImageControl CoolDown;
        public bool HeroMagic;

        string[] Prefixes = new string[] { "", "CTRL", "Shift" };

        public MagicButton()
        {
            Size = new Size(231, 33);

            SkillButton = new MirButton
            {
                Index = 0,
                PressedIndex = 1,
                Library = Libraries.MagIcon2,
                Parent = this,
                Location = new Point(36, 0),
                Sound = SoundList.ButtonA,
            };

            LevelImage = new MirImageControl
            {
                Index = 516,
                Library = Libraries.Title,
                Location = new Point(73, 7),
                Parent = this,
                NotControl = true,
            };

            ExpImage = new MirImageControl
            {
                Index = 517,
                Library = Libraries.Title,
                Location = new Point(73, 19),
                Parent = this,
                NotControl = true,
            };

            LevelLabel = new MirLabel
            {
                AutoSize = true,
                Parent = this,
                Location = new Point(88, 2),
                NotControl = true,
            };

            NameLabel = new MirLabel
            {
                AutoSize = true,
                Parent = this,
                Location = new Point(109, 2),
                NotControl = true,
            };

            ExpLabel = new MirLabel
            {
                AutoSize = true,
                Parent = this,
                Location = new Point(109, 15),
                NotControl = true,
            };

            KeyLabel = new MirLabel
            {
                AutoSize = true,
                Parent = this,
                Location = new Point(2, 2),
                NotControl = true,
            };

            CoolDown = new MirImageControl
            {
                Library = Libraries.Prguse2,
                Parent = this,
                Location = new Point(36, 0),
                Opacity = 0.6F,
                NotControl = true,
                UseOffSet = true,
            };
        }

        public void Update(ClientMagic magic)
        {
            Magic = magic;

            NameLabel.Text = Magic.Name;

            LevelLabel.Text = Magic.Level.ToString();
            switch (Magic.Level)
            {
                case 0:
                    ExpLabel.Text = string.Format("{0}/{1}", Magic.Experience, Magic.Need1);
                    break;
                case 1:
                    ExpLabel.Text = string.Format("{0}/{1}", Magic.Experience, Magic.Need2);
                    break;
                case 2:
                    ExpLabel.Text = string.Format("{0}/{1}", Magic.Experience, Magic.Need3);
                    break;
                case 3:
                    ExpLabel.Text = "-";
                    break;
            }

            KeyLabel.Text = Magic.Key == 0 ? string.Empty : string.Format("{0}{1}F{2}",
                Prefixes[(Magic.Key - 1) / 8],
                Magic.Key > 8 ? Environment.NewLine : string.Empty,
                (Magic.Key - 1) % 8 + 1);

            // 原 Spell→Hint 描述 switch（约 300 行，逐技能 ClientTextKeys 描述）裁剪为技能名。
            SkillButton.Hint = Magic.Name;

            SkillButton.Index = Magic.Icon * 2;
            SkillButton.PressedIndex = Magic.Icon * 2 + 1;

            SetDelay();
        }

        public void SetDelay()
        {
            if (Magic == null) return;

            int totalFrames = 34;

            long timeLeft = Magic.CastTime + Magic.Delay - CMain.Time;

            if (timeLeft < 100)
            {
                CoolDown.Visible = false;
                return;
            }

            int delayPerFrame = (int)(Magic.Delay / totalFrames);
            int startFrame = totalFrames - (int)(timeLeft / delayPerFrame);

            if ((CMain.Time <= Magic.CastTime + Magic.Delay))
            {
                CoolDown.Visible = true;
                CoolDown.Index = 1290 + startFrame;
            }
        }
    }
}
