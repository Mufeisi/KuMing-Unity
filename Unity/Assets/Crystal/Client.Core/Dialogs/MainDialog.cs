using Client.MirControls;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirSounds;
using Crystal.Client.Core.MirMath;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-05）：Client/MirScenes/Dialogs/MainDialogs.cs MainDialog 的 HUD 核心状态条。
    // 范围（用户决策 Q2）：HP/MP 血魔球（HealthOrb 源矩形裁剪）、等级/经验条（ExperienceBar 裁剪）、
    // 由真实 S.UserInformation 驱动的标签（HP/MP/Level/Name/经验百分比/攻击·宠物·技能模式）。
    // 未移植（后续迭代）：顶部功能按钮（Inventory/Character/Skill/Quest/Option/Menu/GameShop）、
    // Hero 面板、Gold/Weight/Space 标签、WeightBar。
    public sealed class MainDialog : MirImageControl
    {
        public static UserObject User
        {
            get { return MapObject.User; }
            set { MapObject.User = value; }
        }

        public MirImageControl ExperienceBar;
        public MirControl HealthOrb;
        public MirLabel HealthLabel, ManaLabel, TopLabel, BottomLabel, LevelLabel, CharacterName, ExperienceLabel, AModeLabel, PModeLabel, SModeLabel;
        public MirButton InventoryButton, CharacterButton, SkillButton;

        public bool HPOnly
        {
            get { return User != null && User.Class == MirClass.Warrior && User.Level < 26; }
        }

        public MainDialog()
        {
            Index = Settings.Resolution == 800 ? 0 : Settings.Resolution == 1024 ? 1 : 2;
            Library = Libraries.Prguse;
            Location = new Point(((Settings.ScreenWidth / 2) - (Size.Width / 2)), Settings.ScreenHeight - Size.Height);
            PixelDetect = true;

            HealthOrb = new MirControl
            {
                Parent = this,
                Location = new Point(0, 30),
                NotControl = true,
            };

            HealthOrb.BeforeDraw += HealthOrb_BeforeDraw;

            HealthLabel = new MirLabel
            {
                AutoSize = true,
                Location = new Point(0, 27),
                Parent = HealthOrb
            };
            HealthLabel.SizeChanged += Label_SizeChanged;

            ManaLabel = new MirLabel
            {
                AutoSize = true,
                Location = new Point(0, 42),
                Parent = HealthOrb
            };
            ManaLabel.SizeChanged += Label_SizeChanged;

            TopLabel = new MirLabel
            {
                Size = new Size(85, 30),
                DrawFormat = TextFormatFlags.HorizontalCenter,
                Location = new Point(9, 20),
                Parent = HealthOrb,
            };

            BottomLabel = new MirLabel
            {
                Size = new Size(85, 30),
                DrawFormat = TextFormatFlags.HorizontalCenter,
                Location = new Point(9, 50),
                Parent = HealthOrb,
            };

            LevelLabel = new MirLabel
            {
                AutoSize = true,
                Parent = this,
                Location = new Point(5, 108)
            };

            CharacterName = new MirLabel
            {
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                Parent = this,
                Location = new Point(6, 120),
                Size = new Size(90, 16)
            };


            ExperienceBar = new MirImageControl
            {
                Index = Settings.Resolution != 800 ? 8 : 7,
                Library = Libraries.Prguse,
                Location = new Point(9, 143),
                Parent = this,
                DrawImage = false,
                NotControl = true,
            };
            ExperienceBar.BeforeDraw += ExperienceBar_BeforeDraw;

            ExperienceLabel = new MirLabel
            {
                AutoSize = true,
                Parent = ExperienceBar,
                NotControl = true,
            };

            AModeLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = Color.Yellow,
                OutLineColour = Color.Black,
                Parent = this,
                Location = new Point(Settings.Resolution != 800 ? 899 : 675, Settings.Resolution != 800 ? -448 : -280),
                Visible = Settings.ModeView
            };

            PModeLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = Color.Orange,
                OutLineColour = Color.Black,
                Parent = this,
                Location = new Point(230, 125),
                Visible = Settings.ModeView
            };

            SModeLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = Color.LimeGreen,
                OutLineColour = Color.Black,
                Parent = this,
                Location = new Point(Settings.Resolution != 800 ? 899 : 675, Settings.Resolution != 800 ? -463 : -295),
                Visible = Settings.ModeView
            };

            // 顶部功能按钮（迭代包2 交互）：Inventory/Character/Skill 开关注对话框（已移植者）。
            // 裁剪（依赖未移植对话框，留待对应迭代包）：Quest/Option/Menu/GameShop；Hint 提示文本。
            InventoryButton = new MirButton
            {
                HoverIndex = 1904,
                Index = 1903,
                Library = Libraries.Prguse,
                Location = new Point(Size.Width - 96, 76),
                Parent = this,
                PressedIndex = 1905,
                Sound = SoundList.ButtonA,
            };
            InventoryButton.Click += (o, e) =>
            {
                if (GameScene.Scene.InventoryDialog.Visible)
                    GameScene.Scene.InventoryDialog.Hide();
                else
                    GameScene.Scene.InventoryDialog.Show();
            };

            CharacterButton = new MirButton
            {
                HoverIndex = 1901,
                Index = 1900,
                Library = Libraries.Prguse,
                Location = new Point(Size.Width - 119, 76),
                Parent = this,
                PressedIndex = 1902,
                Sound = SoundList.ButtonA,
            };
            CharacterButton.Click += (o, e) =>
            {
                if (GameScene.Scene.CharacterDialog.Visible && GameScene.Scene.CharacterDialog.CharacterPage.Visible)
                    GameScene.Scene.CharacterDialog.Hide();
                else
                {
                    GameScene.Scene.CharacterDialog.Show();
                    GameScene.Scene.CharacterDialog.ShowCharacterPage();
                }
            };

            SkillButton = new MirButton
            {
                HoverIndex = 1907,
                Index = 1906,
                Library = Libraries.Prguse,
                Location = new Point(Size.Width - 73, 76),
                Parent = this,
                PressedIndex = 1908,
                Sound = SoundList.ButtonA,
            };
            SkillButton.Click += (o, e) =>
            {
                if (GameScene.Scene.CharacterDialog.Visible && GameScene.Scene.CharacterDialog.SkillPage.Visible)
                    GameScene.Scene.CharacterDialog.Hide();
                else
                {
                    GameScene.Scene.CharacterDialog.Show();
                    GameScene.Scene.CharacterDialog.ShowSkillPage();
                }
            };
        }

        public void Process()
        {
            switch (GameScene.Scene.AMode)
            {
                case AttackMode.Peace:
                    AModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AttackMode_Peace);
                    break;
                case AttackMode.Group:
                    AModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AttackMode_Group);
                    break;
                case AttackMode.Guild:
                    AModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AttackMode_Guild);
                    break;
                case AttackMode.EnemyGuild:
                    AModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AttackMode_EnemyGuild);
                    break;
                case AttackMode.RedBrown:
                    AModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AttackMode_RedBrown);
                    break;
                case AttackMode.All:
                    AModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AttackMode_All);
                    break;
            }

            switch (GameScene.Scene.PMode)
            {
                case PetMode.Both:
                    PModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.PetMode_Both);
                    break;
                case PetMode.MoveOnly:
                    PModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.PetMode_MoveOnly);
                    break;
                case PetMode.AttackOnly:
                    PModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.PetMode_AttackOnly);
                    break;
                case PetMode.None:
                    PModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.PetMode_None);
                    break;
                case PetMode.FocusMasterTarget:
                    PModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.PetMode_FocusMasterTarget);
                    break;
            }

            switch (Settings.SkillMode)
            {
                case true:
                    SModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.SkillModeTilde);
                    break;
                case false:
                    SModeLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.SkillModeCtrl);
                    break;
            }

            if (Settings.HPView)
            {
                HealthLabel.Text = string.Format("HP {0}/{1}", User.HP, User.Stats[Stat.HP]);
                ManaLabel.Text = HPOnly ? "" : string.Format("MP {0}/{1} ", User.MP, User.Stats[Stat.MP]);
                TopLabel.Text = string.Empty;
                BottomLabel.Text = string.Empty;
            }
            else
            {
                if (HPOnly)
                {
                    TopLabel.Text = string.Format("{0}\n" + "--", User.HP);
                    BottomLabel.Text = string.Format("{0}", User.Stats[Stat.HP]);
                }
                else
                {
                    TopLabel.Text = string.Format(" {0}    {1} \n" + "---------------", User.HP, User.MP);
                    BottomLabel.Text = string.Format(" {0}    {1} ", User.Stats[Stat.HP], User.Stats[Stat.MP]);
                }
                HealthLabel.Text = string.Empty;
                ManaLabel.Text = string.Empty;
            }

            LevelLabel.Text = User.Level.ToString();
            ExperienceLabel.Text = string.Format("{0:#0.##%}", User.Experience / (double)User.MaxExperience);
            ExperienceLabel.Location = new Point((ExperienceBar.Size.Width / 2) - 20, -10);
            CharacterName.Text = User.Name;
        }

        private void Label_SizeChanged(object sender, EventArgs e)
        {
            if (!(sender is MirLabel l)) return;

            l.Location = new Point(50 - (l.Size.Width / 2), l.Location.Y);
        }

        private void HealthOrb_BeforeDraw(object sender, EventArgs e)
        {
            if (Libraries.Prguse == null) return;

            int height;
            if (User != null && User.HP != User.Stats[Stat.HP])
                height = (int)(80 * User.HP / (float)User.Stats[Stat.HP]);
            else
                height = 80;

            if (height < 0) height = 0;
            if (height > 80) height = 80;

            int orbImage = 4;

            bool hpOnly = false;

            if (HPOnly)
            {
                hpOnly = true;
                orbImage = 6;
            }

            Rectangle r = new Rectangle(0, 80 - height, hpOnly ? 100 : 50, height);
            Libraries.Prguse.Draw(orbImage, r, new Point(((Settings.ScreenWidth / 2) - (Size.Width / 2)), HealthOrb.DisplayLocation.Y + 80 - height), Color.White, false);

            if (hpOnly) return;

            if (User.MP != User.Stats[Stat.MP])
                height = (int)(80 * User.MP / (float)User.Stats[Stat.MP]);
            else
                height = 80;

            if (height < 0) height = 0;
            if (height > 80) height = 80;
            r = new Rectangle(51, 80 - height, 50, height);

            Libraries.Prguse.Draw(4, r, new Point(((Settings.ScreenWidth / 2) - (Size.Width / 2)) + 51, HealthOrb.DisplayLocation.Y + 80 - height), Color.White, false);
        }

        private void ExperienceBar_BeforeDraw(object sender, EventArgs e)
        {
            if (ExperienceBar.Library == null) return;

            double percent = MapObject.User.Experience / (double)MapObject.User.MaxExperience;
            if (percent > 1) percent = 1;
            if (percent <= 0) return;

            Rectangle section = new Rectangle
            {
                Size = new Size((int)((ExperienceBar.Size.Width - 3) * percent), ExperienceBar.Size.Height)
            };

            ExperienceBar.Library.Draw(ExperienceBar.Index, section, ExperienceBar.DisplayLocation, Color.White, false);
        }
    }
}
