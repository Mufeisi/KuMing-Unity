using System;
using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirObjects;
using S = ServerPackets;

namespace Client.MirScenes
{
    // Crystal.Client.Core 的 GameScene seam（占位）：满足 MapObject/Effect/PlayerObject 的场景契约。
    // 真实 GameScene 落地时替换。
    public class GameScene : MirControl
    {
        public static GameScene Scene;
        public static bool Observing;
        public static uint NPCID;
        public static long NPCTime;
        // 商店价格倍率（NPCGoodsDialog/MirGoodsCell 价格显示），旧客户端 GameScene.cs 同源默认 1。
        public static float NPCRate = 1f;

        public static UserObject User;
        // 英雄（旧客户端 GameScene.cs 同源）：HeroInventoryDialog/HeroInfoPanel 读写
        // AutoPot/HPItem 等 UserHeroObject 专属字段，故类型为 UserHeroObject。
        public static UserHeroObject Hero;
        public static long AttackTime, SpellTime, LastRunTime;
        public AttackMode AMode;
        public PetMode PMode;

        // 施法入口（旧客户端 GameScene.cs 同源）：快捷栏/技能页点击触发，网络层落地后接入 UseSpell 包。
        public void UseSpell(int index) { }

        // 英雄头像图集索引（旧客户端 GameScene.cs:6076 同源）：1400 + 职业 + 10*性别。
        public int HeroAvatar(MirClass job, MirGender gender)
        {
            return 1400 + (byte)job + 10 * (byte)gender;
        }

        // 物品交互状态（背包/装备格子共用，旧客户端 GameScene.cs:243-249 同源）
        public static UserItem HoverItem, SelectedItem;
        public static bool PickedUpGold;
        public static long UseItemTime, PickUpTime, DropViewTime, TargetDeadTime;
        public static uint Gold, Credit;
        public static MirItemCell SelectedCell;

        public static List<ItemInfo> ItemInfoList = new List<ItemInfo>();
        public static List<ClientQuestInfo> QuestInfoList = new List<ClientQuestInfo>();
        public static List<UserItem> ChatItemList = new List<UserItem>();
        // 商城商品列表（迭代包9，GameShopDialog 数据源）：服务器 GameShopInfo 包推送填充。
        public static List<GameShopItem> GameShopInfoList = new List<GameShopItem>();
        // 仓库数组（StorageDialog Grid 绑定，旧客户端 GameScene.cs 同源）；镜像客户端本地仓库快照。
        public static UserItem[] Storage = new UserItem[Globals.StorageGridSize];

        public static void Bind(UserItem item) { }

        public static bool CanMove;
        public static bool CanRun;

        public MirControl ItemLabel;
        public MirControl MailLabel;

        public MapControl MapControl;
        public Dialogs.ChatDialog ChatDialog;
        public Dialogs.MainDialog MainDialog;
        public Dialogs.InventoryDialog InventoryDialog;
        public Dialogs.CharacterDialog CharacterDialog;
        public Dialogs.ChatNoticeDialog ChatNoticeDialog;
        public Dialogs.NPCDialog NPCDialog;
        public Dialogs.NPCGoodsDialog NPCGoodsDialog;
        public Dialogs.StorageDialog StorageDialog;
        public Dialogs.MountDialog MountDialog;
        public Dialogs.FishingDialog FishingDialog;
        public Dialogs.BuffDialog BuffsDialog;
        public Dialogs.BuffDialog HeroBuffsDialog;
        // 英雄系统（旧客户端 GameScene.cs 同源）：HeroDialog=英雄装备/技能（CharacterDialog
        // HeroEquipment 分支）、HeroInventoryDialog=英雄背包、HeroSpawnState=英雄当前状态、
        // MaximumHeroCount=可建英雄数、HeroStorage=英雄列表快照（HeroManageDialog 读写）。
        // HeroBehaviourPanel/HeroMenuPanel/HeroManageDialog 由 InitInGameDialogs 常驻创建
        // （8-8-1；旧版 GameScene ctor 建），HeroDialog/HeroInventoryDialog/HeroBeltDialog/
        // HeroBuffsDialog 依赖 Hero 由 S.HeroInformation 创建（旧版逐字）。
        public Dialogs.CharacterDialog HeroDialog;
        public Dialogs.HeroInventoryDialog HeroInventoryDialog;
        public Dialogs.HeroBeltDialog HeroBeltDialog;
        public Dialogs.HeroBehaviourPanel HeroBehaviourPanel;
        public Dialogs.HeroMenuPanel HeroMenuPanel;
        public Dialogs.HeroManageDialog HeroManageDialog;
        public HeroSpawnState HeroSpawnState;
        public bool HasHero;
        public static int MaximumHeroCount;
        public static ClientHeroInformation[] HeroStorage;
        public Dialogs.SkillBarDialog SkillBarDialog;
        public Dialogs.QuestListDialog QuestListDialog;
        public Dialogs.QuestDetailDialog QuestDetailDialog;
        public Dialogs.QuestDiaryDialog QuestDiaryDialog;
        public Dialogs.QuestTrackingDialog QuestTrackingDialog;
        public Dialogs.BigMapDialog BigMapDialog;
        public Dialogs.MiniMapDialog MiniMapDialog;
        public Dialogs.FishingStatusDialog FishingStatusDialog;
        public Dialogs.GuildDialog GuildDialog;
        public Dialogs.GroupDialog GroupDialog;
        public Dialogs.FriendDialog FriendDialog;
        public Dialogs.MemoDialog MemoDialog;
        public Dialogs.MailListDialog MailListDialog;
        public Dialogs.TradeDialog TradeDialog;
        public Dialogs.GuestTradeDialog GuestTradeDialog;
        public Dialogs.TrustMerchantDialog TrustMerchantDialog;
        public Dialogs.HelpDialog HelpDialog;
        // 聊天设置对话框（迭代包10，ChatOptionDialog.cs）：字段名与旧客户端 GameScene.cs:104 一致。
        public Dialogs.ChatOptionDialog ChatOptionDialog;
        // 按键绑定设置对话框（迭代包10，KeyboardLayoutDialog.cs）：字段名与旧客户端 GameScene.cs:141 一致。
        // WaitingForBind/CheckNewInput 由渲染层键盘钩子驱动（旧客户端 GameScene_KeyDown），探针直接调用。
        public Dialogs.KeyboardLayoutDialog KeyboardLayoutDialog;
        public Dialogs.MailComposeLetterDialog MailComposeLetterDialog;
        public Dialogs.MailComposeParcelDialog MailComposeParcelDialog;
        public Dialogs.MailReadLetterDialog MailReadLetterDialog;
        public Dialogs.MailReadParcelDialog MailReadParcelDialog;
        // 迭代包9（商城+扩展系统）：商城/打孔镶嵌/举报/指南针对话框。字段名与旧客户端 GameScene.cs
        // 一致（CompassControl 为 CompassDialog）。
        public Dialogs.GameShopDialog GameShopDialog;
        public Dialogs.SocketDialog SocketDialog;
        public Dialogs.ReportDialog ReportDialog;
        public Dialogs.CompassDialog CompassControl;
        public MirImageControl DuraStatusPanel;

        // 大地图数据（BigMapDialog 视口/列表，旧客户端 GameScene.cs 同源）：由 MapInfo 包填充。
        public static Dictionary<int, Dialogs.BigMapRecord> MapInfoList = new Dictionary<int, Dialogs.BigMapRecord>();
        // NPC 传送花费（BigMapDialog.TeleportToNPC 校验，旧客户端 GameScene.cs 同源）。
        public static uint TeleportToNPCCost = 1000;
        // 新邮件闪烁状态（MiniMapDialog.Process，旧客户端 GameScene.cs 同源，实例字段）。
        public bool NewMail;
        public int NewMailCounter;

        // 商城数据回调（迭代包9，旧客户端 GameScene.cs:6679-6698 同源）：GameShopInfo 追加商品，
        // 7 天内新品点亮 New 标签；GameShopStock 按 GIndex 更新/移除库存，商城可见时刷新。
        // public：NetProbe 的 OnPacket 分发调用（旧客户端同文件 private，网络层落地前由探针代理）。
        public void GameShopUpdate(S.GameShopInfo p)
        {
            p.Item.Stock = p.StockLevel;
            GameShopInfoList.Add(p.Item);
            // 进图前（InitInGameDialogs 未建对话框）不触碰 New 按钮（8-7-4 null 守卫）。
            if (p.Item.Date > CMain.Now.AddDays(-7) && GameShopDialog != null) GameShopDialog.New.Visible = true;
        }

        public void GameShopStock(S.GameShopStock p)
        {
            for (int i = 0; i < GameShopInfoList.Count; i++)
            {
                if (GameShopInfoList[i].GIndex == p.GIndex)
                {
                    if (p.StockLevel == 0) GameShopInfoList.Remove(GameShopInfoList[i]);
                    else GameShopInfoList[i].Stock = p.StockLevel;

                    // 进图前（InitInGameDialogs 未建对话框）仅落静态列表（8-7-4 null 守卫）。
                    if (GameShopDialog != null && GameShopDialog.Visible) GameShopDialog.UpdateShop();
                }
            }
        }

        public override void Redraw() { }

        // 鼠标输入事件入口（迭代包2）：移植 MirScene.OnMouseDown/Up/Move/Click 的
        // MouseControl/ActiveControl 分发语义（Client/MirControls/MirScene.cs:77-141）。
        // 双击检测（8-2-3 增量）：对齐旧客户端 MirScene 双击窗口语义，但命中控件记录
        // MouseControl（目标 Up 已 Deactivate 清 ActiveControl，Click 实际经 hover 兜底分发）。
        // OnMouseClick 增补 MouseControl 兜底：MirControl.OnMouseUp 已 Deactivate 清空
        // ActiveControl，释放时指针仍在按钮上则仍应触发 Click（与 WinForms 按下释放同控件语义一致）。
        public override void OnMouseDown(MouseEventArgs e)
        {
            if (!Enabled) return;
            if (MouseControl != null && MouseControl != this)
                MouseControl.OnMouseDown(e);
            else
                base.OnMouseDown(e);
        }
        public override void OnMouseUp(MouseEventArgs e)
        {
            if (!Enabled) return;
            if (MouseControl != null && MouseControl != this)
                MouseControl.OnMouseUp(e);
            else
                base.OnMouseUp(e);
        }
        public override void OnMouseMove(MouseEventArgs e)
        {
            if (!Enabled) return;
            if (MouseControl != null && MouseControl != this && MouseControl.Moving)
                MouseControl.OnMouseMove(e);
            else
                base.OnMouseMove(e);
        }
        // 双击窗口（8-2-3）：对齐旧客户端 SystemInformation.DoubleClickTime（约 500ms）。
        public const long DoubleClickTimeMs = 500;
        static long _lastClickTime;
        static MouseButtons _lastButton;
        static MirControl _clickedControl;

        public override void OnMouseClick(MouseEventArgs e)
        {
            if (!Enabled) return;
            if (_lastButton == e.Button && CMain.Time - _lastClickTime <= DoubleClickTimeMs)
            {
                OnMouseDoubleClick(e);
                return;
            }
            _lastClickTime = 0;

            // 点击命中基准：ActiveControl 已被 OnMouseUp Deactivate 清空，实际命中经
            // MouseControl（hover 递归）兜底（见下），故 _clickedControl 记录真实命中控件。
            MirControl hit = null;
            if (ActiveControl != null && ActiveControl.IsMouseOver(CMain.MPoint) && ActiveControl != this)
                hit = ActiveControl;
            else if (MouseControl != null && MouseControl != this && MouseControl.IsMouseOver(CMain.MPoint))
                hit = MouseControl;

            if (hit != null) hit.OnMouseClick(e);
            else base.OnMouseClick(e);

            _clickedControl = hit;
            _lastClickTime = CMain.Time;
            _lastButton = e.Button;
        }

        // 双击分发（8-2-3）：两次点击落在同一控件（_clickedControl==hit）才走 OnMouseDoubleClick
        // （MirItemCell 双击=使用/卸下）；否则退化为单次点击（防跨控件误触发）。
        public override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (!Enabled) return;
            _lastClickTime = 0;
            _lastButton = MouseButtons.None;

            MirControl hit = null;
            if (ActiveControl != null && ActiveControl.IsMouseOver(CMain.MPoint) && ActiveControl != this)
                hit = ActiveControl;
            else if (MouseControl != null && MouseControl != this && MouseControl.IsMouseOver(CMain.MPoint))
                hit = MouseControl;

            if (hit != null)
            {
                if (hit == _clickedControl) hit.OnMouseDoubleClick(e);
                else hit.OnMouseClick(e);
            }
            else
            {
                if (_clickedControl == null) base.OnMouseDoubleClick(e);
                else base.OnMouseClick(e);
            }
        }

        public Color GradeNameColor(ItemGrade grade)
        {
            switch (grade)
            {
                case ItemGrade.Common:
                    return Color.Yellow;
                case ItemGrade.Rare:
                    return Color.DeepSkyBlue;
                case ItemGrade.Legendary:
                    return Color.FromArgb(255, 255, 140, 0);
                case ItemGrade.Mythical:
                    return Color.FromArgb(255, 221, 160, 221);
                case ItemGrade.Heroic:
                    return Color.Red;
                default:
                    return Color.Yellow;
            }
        }

        // 邮件悬浮标签（MailItemRow.OnMouseEnter/Leave，旧客户端 GameScene.cs:9877/6816 同源）：
        // hover 行时在 MailLabel 容器叠加 发件人/日期/金币/已读 状态。
        public void CreateMailLabel(ClientMail mail)
        {
            if (mail == null)
            {
                DisposeMailLabel();
                return;
            }

            if (MailLabel != null && !MailLabel.IsDisposed) return;

            MailLabel = new MirControl
            {
                BackColour = Color.FromArgb(255, 50, 50, 50),
                Border = true,
                BorderColour = Color.Gray,
                DrawControlTexture = true,
                NotControl = true,
                Parent = this,
                Opacity = 0.7F
            };

            MirLabel nameLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = Color.Yellow,
                Location = new Point(4, 4),
                OutLine = true,
                Parent = MailLabel,
                Text = mail.SenderName
            };

            MailLabel.Size = new Size(Math.Max(MailLabel.Size.Width, nameLabel.DisplayRectangle.Right + 4),
                Math.Max(MailLabel.Size.Height, nameLabel.DisplayRectangle.Bottom));

            MirLabel dateLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = Color.White,
                Location = new Point(4, MailLabel.DisplayRectangle.Bottom),
                OutLine = true,
                Parent = MailLabel,
                Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.DateSent, mail.DateSent.ToString("dd/MM/yy H:mm:ss"))
            };

            MailLabel.Size = new Size(Math.Max(MailLabel.Size.Width, dateLabel.DisplayRectangle.Right + 4),
                Math.Max(MailLabel.Size.Height, dateLabel.DisplayRectangle.Bottom));

            if (mail.Gold > 0)
            {
                MirLabel goldLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.White,
                    Location = new Point(4, MailLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = MailLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.MailGoldLabel) + mail.Gold
                };

                MailLabel.Size = new Size(Math.Max(MailLabel.Size.Width, goldLabel.DisplayRectangle.Right + 4),
                    Math.Max(MailLabel.Size.Height, goldLabel.DisplayRectangle.Bottom));
            }

            MirLabel openedLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = Color.Red,
                Location = new Point(4, MailLabel.DisplayRectangle.Bottom),
                OutLine = true,
                Parent = MailLabel,
                Text = mail.Opened ? GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.MailHeaderOld) : GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.MailHeaderNew)
            };

            MailLabel.Size = new Size(Math.Max(MailLabel.Size.Width, openedLabel.DisplayRectangle.Right + 4),
                Math.Max(MailLabel.Size.Height, openedLabel.DisplayRectangle.Bottom));
        }

        public void DisposeMailLabel()
        {
            if (MailLabel != null && !MailLabel.IsDisposed)
                MailLabel.Dispose();
            MailLabel = null;
        }

        public MirControl NameInfoLabel(UserItem item)
        {
            HoverItem = item;

            string GradeString = "";
            switch (HoverItem.Info.Grade)
            {
                case ItemGrade.None:
                    break;
                case ItemGrade.Common:
                    GradeString = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemGradeCommon);
                    break;
                case ItemGrade.Rare:
                    GradeString = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemGradeRare);
                    break;
                case ItemGrade.Legendary:
                    GradeString = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemGradeLegendary);
                    break;
                case ItemGrade.Mythical:
                    GradeString = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemGradeMythical);
                    break;
                case ItemGrade.Heroic:
                    GradeString = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemGradeHeroic);
                    break;
            }
            MirLabel nameLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = GradeNameColor(HoverItem.Info.Grade),
                Location = new Point(4, 4),
                OutLine = true,
                Parent = ItemLabel,
                Text = HoverItem.Info.Grade != ItemGrade.None ? string.Format("{0}{1}{2}", HoverItem.Info.FriendlyName, "\n", GradeString) : HoverItem.Info.FriendlyName,
            };

            if (HoverItem.RefineAdded > 0)
                nameLabel.Text = "(*)" + nameLabel.Text;

            ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, nameLabel.DisplayRectangle.Right + 4),
                Math.Max(ItemLabel.Size.Height, nameLabel.DisplayRectangle.Bottom));

            string text = "";

            if (HoverItem.Info.Durability > 0)
            {
                switch (HoverItem.Info.Type)
                {
                    case ItemType.Amulet:
                        if (HoverItem.CurrentDura > 0 || HoverItem.MaxDura > 0)
                            text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.UsageCurrentMax, HoverItem.CurrentDura, HoverItem.MaxDura);
                        break;
                    case ItemType.Ore:
                        if (HoverItem.CurrentDura > 0)
                            text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.PurityValue, Math.Floor(HoverItem.CurrentDura / 1000M));
                        break;
                    case ItemType.Meat:
                        if (HoverItem.CurrentDura > 0)
                            text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.QualityValue, Math.Floor(HoverItem.CurrentDura / 1000M));
                        break;
                    case ItemType.Mount:
                        if (HoverItem.CurrentDura > 0 || HoverItem.MaxDura > 0)
                            text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.LoyaltyValues, HoverItem.CurrentDura, HoverItem.MaxDura);
                        break;
                    case ItemType.Food:
                        if (HoverItem.CurrentDura > 0)
                            text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.NutritionValue, HoverItem.CurrentDura);
                        break;
                    case ItemType.Gem:
                    case ItemType.Potion:
                    case ItemType.Transform:
                    case ItemType.SealedHero:
                        break;
                    case ItemType.Pets:
                        if ((HoverItem.Info.Shape == 26 || HoverItem.Info.Shape == 28) && HoverItem.CurrentDura > 0)//WonderDrug, Knapsack
                        {
                            string strTime = global::Functions.PrintTimeSpanFromSeconds((HoverItem.CurrentDura * 3600), false);
                            text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.DurationValue, strTime);
                        }
                        break;
                    default:
                        int current = (int)Math.Floor(HoverItem.CurrentDura / 1000M);
                        int maximum = (int)Math.Floor(HoverItem.MaxDura / 1000M);
                        if (current > 0 || maximum > 0)
                            text = string.Format("{0} {1}/{2}", GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.Durability), current, maximum);
                        break;
                }
            }

            string baseText = string.Empty;
            switch (HoverItem.Info.Type)
            {
                case ItemType.Nothing:
                    break;
                case ItemType.Weapon:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeWeapon);
                    break;
                case ItemType.Armour:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeArmour);
                    break;
                case ItemType.Helmet:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeHelmet);
                    break;
                case ItemType.Necklace:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeNecklace);
                    break;
                case ItemType.Bracelet:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeBracelet);
                    break;
                case ItemType.Ring:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeRing);
                    break;
                case ItemType.Amulet:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeAmulet);
                    break;
                case ItemType.Belt:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeBelt);
                    break;
                case ItemType.Boots:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeBoots);
                    break;
                case ItemType.Stone:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeStone);
                    break;
                case ItemType.Torch:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeTorch);
                    break;
                case ItemType.Potion:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypePotion);
                    break;
                case ItemType.Ore:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeOre);
                    break;
                case ItemType.Meat:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeMeat);
                    break;
                case ItemType.CraftingMaterial:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeCraftingMaterial);
                    break;
                case ItemType.Scroll:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeScroll);
                    break;
                case ItemType.Gem:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeGem);
                    break;
                case ItemType.Mount:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeMount);
                    break;
                case ItemType.Book:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeBook);
                    break;
                case ItemType.Script:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeScript);
                    break;
                case ItemType.Reins:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeReins);
                    break;
                case ItemType.Bells:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeBells);
                    break;
                case ItemType.Saddle:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeSaddle);
                    break;
                case ItemType.Ribbon:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeRibbon);
                    break;
                case ItemType.Mask:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeMask);
                    break;
                case ItemType.Food:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeFood);
                    break;
                case ItemType.Hook:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeHook);
                    break;
                case ItemType.Float:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeFloat);
                    break;
                case ItemType.Bait:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeBait);
                    break;
                case ItemType.Finder:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeFinder);
                    break;
                case ItemType.Reel:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeReel);
                    break;
                case ItemType.Fish:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeFish);
                    break;
                case ItemType.Quest:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeQuest);
                    break;
                case ItemType.Awakening:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeAwakening);
                    break;
                case ItemType.Pets:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypePets);
                    break;
                case ItemType.Transform:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeTransform);
                    break;
                case ItemType.Deco:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeDeco);
                    break;
                case ItemType.MonsterSpawn:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeMonsterSpawn);
                    break;
                case ItemType.SealedHero:
                    baseText = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemTypeSealedHero);
                    break;
            }

            if (HoverItem.WeddingRing != -1)
            {
                baseText += GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.WeddingRing);
            }

            List<string> tailParts = new List<string>();
            if (HoverItem.Weight > 0)
                tailParts.Add(string.Format("{0} {1}", GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.Weight), HoverItem.Weight));
            if (!string.IsNullOrEmpty(text))
                tailParts.Add(text);

            if (tailParts.Count > 0)
            {
                string tailText = string.Join("  ", tailParts.ToArray());
                baseText = string.IsNullOrEmpty(baseText)
                    ? tailText
                    : string.Format("{0}\n{1}", baseText, tailText);
            }

            MirLabel etcLabel = new MirLabel
            {
                AutoSize = true,
                ForeColour = Color.White,
                Location = new Point(4, nameLabel.DisplayRectangle.Bottom),
                OutLine = true,
                Parent = ItemLabel,
                Text = baseText
            };

            ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, etcLabel.DisplayRectangle.Right + 4),
                Math.Max(ItemLabel.Size.Height, etcLabel.DisplayRectangle.Bottom + 4));

            #region OUTLINE
            MirControl outLine = new MirControl
            {
                BackColour = Color.FromArgb(255, 50, 50, 50),
                Border = true,
                BorderColour = Color.Gray,
                NotControl = true,
                Parent = ItemLabel,
                Opacity = 0.4F,
                Location = new Point(0, 0)
            };
            outLine.Size = ItemLabel.Size;
            #endregion

            return outLine;
        }

        private Stats GetTotalAddedStats(UserItem item, ushort level, MirClass job)
        {
            Stats total = new Stats();

            if (item == null) return total;

            total.Add(item.AddedStats);

            if (item.Slots == null || item.Slots.Length == 0) return total;

            for (int i = 0; i < item.Slots.Length; i++)
            {
                UserItem socketItem = item.Slots[i];
                if (socketItem == null) continue;

                ItemInfo socketInfo = Functions.GetRealItem(socketItem.Info, level, job, ItemInfoList);

                if (socketItem.CurrentDura == 0 && socketInfo.Durability > 0) continue;

                total.Add(socketInfo.Stats);
                total.Add(socketItem.AddedStats);
            }

            return total;
        }

        public MirControl AttackInfoLabel(UserItem item)
        {
            ushort level = MapObject.User.Level;
            MirClass job = MapObject.User.Class;
            HoverItem = item;
            ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);
            Stats addedStats = GetTotalAddedStats(item, level, job);

            ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            bool fishingItem = false;

            switch (HoverItem.Info.Type)
            {
                case ItemType.Hook:
                case ItemType.Float:
                case ItemType.Bait:
                case ItemType.Finder:
                case ItemType.Reel:
                    fishingItem = true;
                    break;
                case ItemType.Weapon:
                    if (Globals.FishingRodShapes.Contains(HoverItem.Info.Shape))
                        fishingItem = true;
                    break;
                default:
                    fishingItem = false;
                    break;
            }

            int count = 0;
            int minValue = 0;
            int maxValue = 0;
            int addValue = 0;
            string text = "";

            #region Dura gem
            minValue = realItem.Durability;

            if (minValue > 0 && realItem.Type == ItemType.Gem)
            {
                switch (realItem.Shape)
                {
                    default:
                        text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AddsDurability, minValue / 1000);
                        break;
                    case 8:
                        text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.SealsFor, global::Functions.PrintTimeSpanFromSeconds(minValue * 60));
                        break;
                }

                count++;
                MirLabel DuraLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DuraLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DuraLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region DC
            minValue = realItem.Stats[Stat.MinDC];
            maxValue = realItem.Stats[Stat.MaxDC];
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.MaxDC] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                    text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.DC, minValue, maxValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                else
                    text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AddsDC, minValue + maxValue + addValue);
                MirLabel DCLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DCLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DCLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region MC

            minValue = realItem.Stats[Stat.MinMC];
            maxValue = realItem.Stats[Stat.MaxMC];
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.MaxMC] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                    text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.MC, minValue, maxValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                else
                    text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AddsMC, minValue + maxValue + addValue);
                MirLabel MCLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MCLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MCLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region SC

            minValue = realItem.Stats[Stat.MinSC];
            maxValue = realItem.Stats[Stat.MaxSC];
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.MaxSC] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                    text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.SC, minValue, maxValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                else
                    text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AddsSC, minValue + maxValue + addValue);
                MirLabel SCLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, SCLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, SCLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region LUCK / SUCCESS

            minValue = realItem.Stats[Stat.Luck];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.Luck] : 0;

            if (minValue != 0 || addValue != 0)
            {
                count++;

                if (realItem.Type == ItemType.Pets && realItem.Shape == 28)
                {
                    text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.BagWeightPercent, minValue + addValue);
                }
                else if (realItem.Type == ItemType.Potion && realItem.Shape == 4)
                {
                    text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ExpPercent, minValue + addValue);
                }
                else if (realItem.Type == ItemType.Potion && realItem.Shape == 5)
                {
                    text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.DropPercent, minValue + addValue);
                }
                else
                {
                    text = string.Format(minValue + addValue > 0 ? GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.Luck) : GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CursePlusValue),
                        Math.Abs(minValue + addValue));
                }

                MirLabel LUCKLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, LUCKLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, LUCKLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region ACC

            minValue = realItem.Stats[Stat.Accuracy];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.Accuracy] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                    text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.Accuracy, minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                else
                    text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AddsAccuracyValue, minValue + maxValue + addValue);
                MirLabel ACCLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, ACCLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, ACCLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region HOLY

            minValue = realItem.Stats[Stat.Holy];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel HOLYLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.Holy, minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, HOLYLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, HOLYLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region ASPEED

            minValue = realItem.Stats[Stat.AttackSpeed];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.AttackSpeed] : 0;

            if (minValue != 0 || maxValue != 0 || addValue != 0)
            {
                string plus = (addValue + minValue < 0) ? String.Empty : "+";

                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                {
                    string negative = "+";
                    if (addValue < 0) negative = String.Empty;

                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AttackSpeedValue), plus, minValue + addValue) + (addValue != 0 ? $" ({negative}{addValue})" : string.Empty);
                }
                else
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AddAttackSpeed), minValue + maxValue + addValue);

                MirLabel ASPEEDLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, ASPEEDLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, ASPEEDLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region FREEZING

            minValue = realItem.Stats[Stat.Freezing];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.Freezing] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                {
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.FreezingPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                }
                else text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AddsFreezingPlus), minValue + maxValue + addValue);

                MirLabel FREEZINGLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, FREEZINGLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, FREEZINGLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region POISON

            minValue = realItem.Stats[Stat.PoisonAttack];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.PoisonAttack] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                {
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.PoisonPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                }
                else
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AddsPoisonPlus), minValue + maxValue + addValue);

                MirLabel POISONLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, POISONLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, POISONLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region CRITICALRATE / FLEXIBILITY

            minValue = realItem.Stats[Stat.CriticalRate];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.CriticalRate] : 0;

            if ((minValue > 0 || maxValue > 0 || addValue > 0) && (realItem.Type != ItemType.Gem))
            {
                count++;
                MirLabel CRITICALRATELabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.CriticalChancePlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty)
                };

                if (fishingItem)
                {
                    CRITICALRATELabel.Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.FlexibilityPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                }

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, CRITICALRATELabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, CRITICALRATELabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region CRITICALDAMAGE

            minValue = realItem.Stats[Stat.CriticalDamage];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.CriticalDamage] : 0;

            if ((minValue > 0 || maxValue > 0 || addValue > 0) && (realItem.Type != ItemType.Gem))
            {
                count++;
                MirLabel CRITICALDAMAGELabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.CriticalDamagePlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, CRITICALDAMAGELabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, CRITICALDAMAGELabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region Reflect

            minValue = realItem.Stats[Stat.Reflect];
            maxValue = 0;
            addValue = 0;

            if ((minValue > 0 || maxValue > 0 || addValue > 0) && (realItem.Type != ItemType.Gem))
            {
                count++;
                MirLabel ReflectLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.ReflectChance), minValue)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, ReflectLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, ReflectLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region Hpdrain

            minValue = realItem.Stats[Stat.HPDrainRatePercent];
            maxValue = 0;
            addValue = 0;

            if ((minValue > 0 || maxValue > 0 || addValue > 0) && (realItem.Type != ItemType.Gem))
            {
                count++;
                MirLabel HPdrainLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.HPDrainRate), minValue)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, HPdrainLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, HPdrainLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region Exp Rate

            minValue = realItem.Stats[Stat.ExpRatePercent];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.ExpRatePercent] : 0;

            if (minValue != 0 || maxValue != 0 || addValue != 0)
            {
                string plus = (addValue + minValue < 0) ? string.Empty : "+";

                count++;
                string negative = "+";
                if (addValue < 0) negative = String.Empty;
                text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.ExpRate), plus, minValue + addValue) + (addValue != 0 ? $" ({negative}{addValue}%)" : String.Empty);

                MirLabel expRateLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, expRateLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, expRateLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region Drop Rate

            minValue = realItem.Stats[Stat.ItemDropRatePercent];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.ItemDropRatePercent] : 0;

            if (minValue != 0 || maxValue != 0 || addValue != 0)
            {
                string plus = (addValue + minValue < 0) ? String.Empty : "+";

                count++;
                string negative = "+";
                if (addValue < 0) negative = String.Empty;
                text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.DropRate), plus, minValue + addValue) + (addValue != 0 ? $" ({negative}{addValue}%)" : string.Empty);

                MirLabel dropRateLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, dropRateLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, dropRateLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region Gold Rate

            minValue = realItem.Stats[Stat.GoldDropRatePercent];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.GoldDropRatePercent] : 0;

            if (minValue != 0 || maxValue != 0 || addValue != 0)
            {
                string plus = (addValue + minValue < 0) ? String.Empty : "+";

                count++;
                string negative = "+";
                if (addValue < 0) negative = "";
                text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.GoldRate), plus, minValue + addValue) + (addValue != 0 ? $" ({negative}{addValue}%)" : String.Empty);

                MirLabel goldRateLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, goldRateLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, goldRateLabel.DisplayRectangle.Bottom));
            }

            #endregion

            if (count > 0)
            {
                ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

                #region OUTLINE
                MirControl outLine = new MirControl
                {
                    BackColour = Color.FromArgb(255, 50, 50, 50),
                    Border = true,
                    BorderColour = Color.Gray,
                    NotControl = true,
                    Parent = ItemLabel,
                    Opacity = 0.4F,
                    Location = new Point(0, 0)
                };
                outLine.Size = ItemLabel.Size;
                #endregion

                return outLine;
            }
            else
            {
                ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            }
            return null;
        }

        public MirControl DefenceInfoLabel(UserItem item)
        {
            ushort level = MapObject.User.Level;
            MirClass job = MapObject.User.Class;
            HoverItem = item;
            ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);
            Stats addedStats = GetTotalAddedStats(item, level, job);

            ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            bool fishingItem = false;

            switch (HoverItem.Info.Type)
            {
                case ItemType.Hook:
                case ItemType.Float:
                case ItemType.Bait:
                case ItemType.Finder:
                case ItemType.Reel:
                    fishingItem = true;
                    break;
                case ItemType.Weapon:
                    if (HoverItem.Info.Shape == 49 || HoverItem.Info.Shape == 50)
                        fishingItem = true;
                    break;
                default:
                    fishingItem = false;
                    break;
            }

            int count = 0;
            int minValue = 0;
            int maxValue = 0;
            int addValue = 0;

            string text = "";
            #region AC

            minValue = realItem.Stats[Stat.MinAC];
            maxValue = realItem.Stats[Stat.MaxAC];
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.MaxAC] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AC), minValue, maxValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                else
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AddsAC), minValue + maxValue + addValue);
                MirLabel ACLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                if (fishingItem)
                {
                    if (HoverItem.Info.Type == ItemType.Float)
                    {
                        ACLabel.Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.NibbleChance), minValue, maxValue + addValue) +
                                       (addValue > 0 ? $" (+{addValue})" : String.Empty);
                    }
                    else if (HoverItem.Info.Type == ItemType.Finder)
                    {
                        ACLabel.Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.FinderIncrease), minValue, maxValue + addValue) +
                                       (addValue > 0 ? $" (+{addValue})" : String.Empty);
                    }
                    else
                    {
                        ACLabel.Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.SuccessChance), maxValue + addValue) + (addValue > 0 ? $" (+{addValue})" : String.Empty);
                    }
                }

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, ACLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, ACLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region MAC

            minValue = realItem.Stats[Stat.MinMAC];
            maxValue = realItem.Stats[Stat.MaxMAC];
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.MaxMAC] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MAC), minValue, maxValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                else
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AddsMAC), minValue + maxValue + addValue);
                MirLabel MACLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                if (fishingItem)
                {
                    MACLabel.Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AutoReelChance), maxValue + addValue);
                }

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MACLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MACLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region MAXHP

            if (HoverItem.Info.Type != ItemType.MonsterSpawn)
            {
                minValue = realItem.Stats[Stat.HP];
                maxValue = 0;
                addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.HP] : 0;

                if (minValue > 0 || maxValue > 0 || addValue > 0)
                {
                    count++;
                    MirLabel MAXHPLabel = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MaxHpPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : String.Empty)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXHPLabel.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, MAXHPLabel.DisplayRectangle.Bottom));
                }
            }

            #endregion

            #region MAXMP

            minValue = realItem.Stats[Stat.MP];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.MP] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel MAXMPLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MaxMpPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : String.Empty)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXMPLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MAXMPLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region MAXHPRATE

            minValue = realItem.Stats[Stat.HPRatePercent];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel MAXHPRATELabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MaxHpPlusPercent), minValue + addValue)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXHPRATELabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MAXHPRATELabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region MAXMPRATE

            minValue = realItem.Stats[Stat.MPRatePercent];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel MAXMPRATELabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MaxMpPlusPercent), minValue + addValue)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXMPRATELabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MAXMPRATELabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region MAXACRATE

            minValue = realItem.Stats[Stat.MaxACRatePercent];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel MAXACRATE = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MaxAcPlusPercent), minValue + addValue)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXACRATE.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MAXACRATE.DisplayRectangle.Bottom));
            }

            #endregion

            #region MAXMACRATE

            minValue = realItem.Stats[Stat.MaxMACRatePercent];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel MAXMACRATELabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MaxMacPlusPercent), minValue + addValue)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXMACRATELabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MAXMACRATELabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region HEALTH_RECOVERY

            minValue = realItem.Stats[Stat.HealthRecovery];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.HealthRecovery] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel HEALTH_RECOVERYLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.HealthRecoveryPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : String.Empty)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, HEALTH_RECOVERYLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, HEALTH_RECOVERYLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region MANA_RECOVERY

            minValue = realItem.Stats[Stat.SpellRecovery];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.SpellRecovery] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel MANA_RECOVERYLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.ManaRecoveryPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : String.Empty)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MANA_RECOVERYLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MANA_RECOVERYLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region POISON_RECOVERY

            minValue = realItem.Stats[Stat.PoisonRecovery];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.PoisonRecovery] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel POISON_RECOVERYabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.PoisonRecoveryPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : String.Empty)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, POISON_RECOVERYabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, POISON_RECOVERYabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region AGILITY

            minValue = realItem.Stats[Stat.Agility];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.Agility] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.Agility), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                else
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AddsAgilityPlus), minValue + maxValue + addValue);

                MirLabel AGILITYLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, AGILITYLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, AGILITYLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region STRONG

            minValue = realItem.Stats[Stat.Strong];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.Strong] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel STRONGLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.StrongPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, STRONGLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, STRONGLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region POISON_RESIST

            minValue = realItem.Stats[Stat.PoisonResist];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.PoisonResist] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.PoisonResistPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                else
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AddPoisonResistPlus), minValue + maxValue + addValue);
                MirLabel POISON_RESISTLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, POISON_RESISTLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, POISON_RESISTLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region MAGIC_RESIST

            minValue = realItem.Stats[Stat.MagicResist];
            maxValue = 0;
            addValue = (!HoverItem.Info.NeedIdentify || HoverItem.Identified) ? addedStats[Stat.MagicResist] : 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                if (HoverItem.Info.Type != ItemType.Gem)
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MagicResistPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : string.Empty);
                else
                    text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AddMagicResistPlus), minValue + maxValue + addValue);
                MirLabel MAGIC_RESISTLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAGIC_RESISTLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MAGIC_RESISTLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region MAX_DC_RATE

            minValue = realItem.Stats[Stat.MaxDCRatePercent];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel MAXDCRATE = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MaxDcPlusPercent), minValue + addValue)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXDCRATE.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MAXDCRATE.DisplayRectangle.Bottom));
            }
            #endregion

            #region MAX_MC_RATE

            minValue = realItem.Stats[Stat.MaxMCRatePercent];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel MAXMCRATE = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MaxMcPlusPercent), minValue + addValue)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXMCRATE.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MAXMCRATE.DisplayRectangle.Bottom));
            }
            #endregion

            #region MAX_SC_RATE

            minValue = realItem.Stats[Stat.MaxSCRatePercent];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel MAXSCRATE = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MaxScPlusPercent), minValue + addValue)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, MAXSCRATE.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, MAXSCRATE.DisplayRectangle.Bottom));
            }
            #endregion

            #region DAMAGE_REDUCTION

            minValue = realItem.Stats[Stat.DamageReductionPercent];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel DAMAGEREDUC = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.AllDamageReductionPlusPercent), minValue + addValue)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DAMAGEREDUC.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DAMAGEREDUC.DisplayRectangle.Bottom));
            }
            #endregion
            if (count > 0)
            {
                ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

                #region OUTLINE
                MirControl outLine = new MirControl
                {
                    BackColour = Color.FromArgb(255, 50, 50, 50),
                    Border = true,
                    BorderColour = Color.Gray,
                    NotControl = true,
                    Parent = ItemLabel,
                    Opacity = 0.4F,
                    Location = new Point(0, 0)
                };
                outLine.Size = ItemLabel.Size;
                #endregion

                return outLine;
            }
            else
            {
                ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            }
            return null;
        }

        public MirControl WeightInfoLabel(UserItem item)
        {
            ushort level = MapObject.User.Level;
            MirClass job = MapObject.User.Class;
            HoverItem = item;
            ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            int count = 0;
            int minValue = 0;
            int maxValue = 0;
            int addValue = 0;

            #region HANDWEIGHT

            minValue = realItem.Stats[Stat.HandWeight];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel HANDWEIGHTLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.HandWeightPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : String.Empty)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, HANDWEIGHTLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, HANDWEIGHTLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region WEARWEIGHT

            minValue = realItem.Stats[Stat.WearWeight];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel WEARWEIGHTLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.WearWeightPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : String.Empty)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, WEARWEIGHTLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, WEARWEIGHTLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region BAGWEIGHT

            minValue = realItem.Stats[Stat.BagWeight];
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel BAGWEIGHTLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.BagWeightPlus), minValue + addValue) + (addValue > 0 ? $" (+{addValue})" : String.Empty)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, BAGWEIGHTLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, BAGWEIGHTLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region FASTRUN

            minValue = realItem.CanFastRun == true ? 1 : 0;
            maxValue = 0;
            addValue = 0;

            if (minValue > 0 || maxValue > 0 || addValue > 0)
            {
                count++;
                MirLabel BAGWEIGHTLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.InstantRun)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, BAGWEIGHTLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, BAGWEIGHTLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region TIME & RANGE
            minValue = 0;
            maxValue = 0;
            addValue = 0;

            if (HoverItem.Info.Type == ItemType.Potion && HoverItem.Info.Durability > 0)
            {
                count++;
                MirLabel TNRLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.TimeValue), global::Functions.PrintTimeSpanFromSeconds(HoverItem.Info.Durability * 60))
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, TNRLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, TNRLabel.DisplayRectangle.Bottom));
            }

            if (HoverItem.Info.Type == ItemType.Transform && HoverItem.Info.Durability > 0)
            {
                count++;
                MirLabel TNRLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = addValue > 0 ? Color.FromArgb(255, 0, 255, 255) : Color.White,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.TimeValue), global::Functions.PrintTimeSpanFromSeconds(HoverItem.Info.Durability, false))
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, TNRLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, TNRLabel.DisplayRectangle.Bottom));
            }

            #endregion

            if (count > 0)
            {
                ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

                #region OUTLINE
                MirControl outLine = new MirControl
                {
                    BackColour = Color.FromArgb(255, 50, 50, 50),
                    Border = true,
                    BorderColour = Color.Gray,
                    NotControl = true,
                    Parent = ItemLabel,
                    Opacity = 0.4F,
                    Location = new Point(0, 0)
                };
                outLine.Size = ItemLabel.Size;
                #endregion

                return outLine;
            }
            else
            {
                ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            }
            return null;
        }

        public MirControl NeedInfoLabel(UserItem item)
        {
            ushort level = MapObject.User.Level;
            MirClass job = MapObject.User.Class;
            HoverItem = item;
            ItemInfo realItem = Functions.GetRealItem(item.Info, level, job, ItemInfoList);

            ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            int count = 0;

            #region LEVEL
            if (realItem.RequiredAmount > 0)
            {
                count++;
                string text;
                Color colour = Color.White;
                switch (realItem.RequiredType)
                {
                    case RequiredType.Level:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RequiredLevel), realItem.RequiredAmount);
                        if (MapObject.User.Level < realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    case RequiredType.MaxAC:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RequiredAC), realItem.RequiredAmount);
                        if (MapObject.User.Stats[Stat.MaxAC] < realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    case RequiredType.MaxMAC:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RequiredMAC), realItem.RequiredAmount);
                        if (MapObject.User.Stats[Stat.MaxMAC] < realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    case RequiredType.MaxDC:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RequiredDC), realItem.RequiredAmount);
                        if (MapObject.User.Stats[Stat.MaxDC] < realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    case RequiredType.MaxMC:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RequiredMC), realItem.RequiredAmount);
                        if (MapObject.User.Stats[Stat.MaxMC] < realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    case RequiredType.MaxSC:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RequiredSC), realItem.RequiredAmount);
                        if (MapObject.User.Stats[Stat.MaxSC] < realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    case RequiredType.MaxLevel:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.MaximumLevel), realItem.RequiredAmount);
                        if (MapObject.User.Level > realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    case RequiredType.MinAC:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RequiredBaseAC), realItem.RequiredAmount);
                        if (MapObject.User.Stats[Stat.MinAC] < realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    case RequiredType.MinMAC:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RequiredBaseMAC), realItem.RequiredAmount);
                        if (MapObject.User.Stats[Stat.MinMAC] < realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    case RequiredType.MinDC:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RequiredBaseDC), realItem.RequiredAmount);
                        if (MapObject.User.Stats[Stat.MinDC] < realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    case RequiredType.MinMC:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RequiredBaseMC), realItem.RequiredAmount);
                        if (MapObject.User.Stats[Stat.MinMC] < realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    case RequiredType.MinSC:
                        text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RequiredBaseSC), realItem.RequiredAmount);
                        if (MapObject.User.Stats[Stat.MinSC] < realItem.RequiredAmount)
                            colour = Color.Red;
                        break;
                    default:
                        text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.UnknownTypeRequired);
                        break;
                }

                MirLabel LEVELLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = colour,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, LEVELLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, LEVELLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region CLASS
            if (realItem.RequiredClass != RequiredClass.None)
            {
                count++;
                Color colour = Color.White;

                switch (MapObject.User.Class)
                {
                    case MirClass.Warrior:
                        if (!realItem.RequiredClass.HasFlag(RequiredClass.Warrior))
                            colour = Color.Red;
                        break;
                    case MirClass.Wizard:
                        if (!realItem.RequiredClass.HasFlag(RequiredClass.Wizard))
                            colour = Color.Red;
                        break;
                    case MirClass.Taoist:
                        if (!realItem.RequiredClass.HasFlag(RequiredClass.Taoist))
                            colour = Color.Red;
                        break;
                    case MirClass.Assassin:
                        if (!realItem.RequiredClass.HasFlag(RequiredClass.Assassin))
                            colour = Color.Red;
                        break;
                    case MirClass.Archer:
                        if (!realItem.RequiredClass.HasFlag(RequiredClass.Archer))
                            colour = Color.Red;
                        break;
                }

                MirLabel CLASSLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = colour,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.ClassRequired), realItem.RequiredClass.ToLocalizedString())
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, CLASSLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, CLASSLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region BUYING - SELLING PRICE
            if (item.Price() > 0)
            {
                count++;
                string text;
                var colour = Color.White;

                text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.SellingPriceGold), ((long)(item.Price() / 2)).ToString("###,###,##0"));

                var costLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = colour,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, costLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, costLabel.DisplayRectangle.Bottom));
            }


            #endregion

            if (count > 0)
            {
                ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

                #region OUTLINE
                MirControl outLine = new MirControl
                {
                    BackColour = Color.FromArgb(255, 50, 50, 50),
                    Border = true,
                    BorderColour = Color.Gray,
                    NotControl = true,
                    Parent = ItemLabel,
                    Opacity = 0.4F,
                    Location = new Point(0, 0)
                };
                outLine.Size = ItemLabel.Size;
                #endregion

                return outLine;
            }
            else
            {
                ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            }
            return null;
        }

        public MirControl BindInfoLabel(UserItem item)
        {
            HoverItem = item;

            ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

            int count = 0;

            #region DONT_DEATH_DROP

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontDeathdrop))
            {
                count++;
                MirLabel DONT_DEATH_DROPLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CantDropOnDeath)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_DEATH_DROPLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DONT_DEATH_DROPLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region DONT_DROP

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontDrop))
            {
                count++;
                MirLabel DONT_DROPLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CantDrop)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_DROPLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DONT_DROPLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region DONT_UPGRADE

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontUpgrade))
            {
                count++;
                MirLabel DONT_UPGRADELabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CantUpgrade)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_UPGRADELabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DONT_UPGRADELabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region DONT_SELL

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontSell))
            {
                count++;
                MirLabel DONT_SELLLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CantSell)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_SELLLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DONT_SELLLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region DONT_TRADE

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontTrade))
            {
                count++;
                MirLabel DONT_TRADELabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CantTrade)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_TRADELabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DONT_TRADELabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region DONT_STORE

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontStore))
            {
                count++;
                MirLabel DONT_STORELabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CantStore)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_STORELabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DONT_STORELabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region DONT_REPAIR

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DontRepair))
            {
                count++;
                MirLabel DONT_REPAIRLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CantRepair)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_REPAIRLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DONT_REPAIRLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region DONT_SPECIALREPAIR

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.NoSRepair))
            {
                count++;
                MirLabel DONT_REPAIRLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CantSpecialRepair)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_REPAIRLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DONT_REPAIRLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region BREAK_ON_DEATH

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.BreakOnDeath))
            {
                count++;
                MirLabel DONT_REPAIRLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.BreaksOnDeath)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_REPAIRLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DONT_REPAIRLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region DONT_DESTROY_ON_DROP

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.DestroyOnDrop))
            {
                count++;
                MirLabel DONT_DODLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.DestroyedWhenDropped)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, DONT_DODLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, DONT_DODLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region NoWeddingRing

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.NoWeddingRing))
            {
                count++;
                MirLabel No_WedLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CannotBeWeddingRing)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, No_WedLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, No_WedLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region NoHero

            if (HoverItem.Info.Bind != BindMode.None && HoverItem.Info.Bind.HasFlag(BindMode.NoHero))
            {
                count++;
                MirLabel No_HeroLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CannotBeUsedByHero)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, No_HeroLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, No_HeroLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region BIND_ON_EQUIP

            if ((HoverItem.Info.Bind.HasFlag(BindMode.BindOnEquip)) & HoverItem.SoulBoundId == -1)
            {
                count++;
                MirLabel BOELabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.SoulBindsOnEquip)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, BOELabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, BOELabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region CURSED

            if ((!HoverItem.Info.NeedIdentify || HoverItem.Identified) && HoverItem.Cursed)
            {
                count++;
                MirLabel CURSEDLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.Cursed)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, CURSEDLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, CURSEDLabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region Gems

            if (HoverItem.Info.Type == ItemType.Gem)
            {
                #region UseOn text
                count++;
                string Text = "";
                if (HoverItem.Info.Unique == SpecialItemMode.None)
                {
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CannotBeUsedOnAnyItem);
                }
                else
                {
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.CanBeUsedOn);
                }
                MirLabel GemUseOn = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = Text
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, GemUseOn.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, GemUseOn.DisplayRectangle.Bottom));
                #endregion
                #region Weapon text
                count++;
                if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Paralize))
                {
                    MirLabel GemWeapon = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AfterWeapon)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, GemWeapon.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, GemWeapon.DisplayRectangle.Bottom));
                }
                #endregion
                #region Armour text
                count++;
                if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Teleport))
                {
                    MirLabel GemArmour = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AfterArmour)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, GemArmour.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, GemArmour.DisplayRectangle.Bottom));
                }
                #endregion
                #region Helmet text
                count++;
                if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.ClearRing))
                {
                    MirLabel Gemhelmet = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AfterHelmet)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemhelmet.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, Gemhelmet.DisplayRectangle.Bottom));
                }
                #endregion
                #region Necklace text
                count++;
                if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Protection))
                {
                    MirLabel Gemnecklace = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AfterNecklace)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemnecklace.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, Gemnecklace.DisplayRectangle.Bottom));
                }
                #endregion
                #region Bracelet text
                count++;
                if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Revival))
                {
                    MirLabel GemBracelet = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AfterBracelet)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, GemBracelet.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, GemBracelet.DisplayRectangle.Bottom));
                }
                #endregion
                #region Ring text
                count++;
                if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Muscle))
                {
                    MirLabel GemRing = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AfterRing)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, GemRing.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, GemRing.DisplayRectangle.Bottom));
                }
                #endregion
                #region Amulet text
                count++;
                if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Flame))
                {
                    MirLabel Gemamulet = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AfterAmulet)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemamulet.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, Gemamulet.DisplayRectangle.Bottom));
                }
                #endregion
                #region Belt text
                count++;
                if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Healing))
                {
                    MirLabel Gembelt = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AfterBelt)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gembelt.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, Gembelt.DisplayRectangle.Bottom));
                }
                #endregion
                #region Boots text
                count++;
                if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Probe))
                {
                    MirLabel Gemboots = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AfterBoots)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemboots.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, Gemboots.DisplayRectangle.Bottom));
                }
                #endregion
                #region Stone text
                count++;
                if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.Skill))
                {
                    MirLabel Gemstone = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.Stone)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemstone.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, Gemstone.DisplayRectangle.Bottom));
                }
                #endregion
                #region Torch text
                count++;
                if (HoverItem.Info.Unique.HasFlag(SpecialItemMode.NoDuraLoss))
                {
                    MirLabel Gemtorch = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.White,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AfterCandle)
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, Gemtorch.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, Gemtorch.DisplayRectangle.Bottom));
                }
                #endregion
            }

            #endregion

            #region EXPIRE

            if (HoverItem.ExpireInfo != null)
            {
                double remainingSeconds = (HoverItem.ExpireInfo.ExpiryDate - CMain.Now).TotalSeconds;

                count++;
                MirLabel EXPIRELabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = remainingSeconds > 0 ? GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.ExpiresIn), global::Functions.PrintTimeSpanFromSeconds(remainingSeconds)) : GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.Expired)
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, EXPIRELabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, EXPIRELabel.DisplayRectangle.Bottom));
            }

            #endregion

            #region SEALED

            if (HoverItem.SealedInfo != null)
            {
                double remainingSeconds = (HoverItem.SealedInfo.ExpiryDate - CMain.Now).TotalSeconds;

                if (remainingSeconds > 0)
                {
                    count++;
                    MirLabel SEALEDLabel = new MirLabel
                    {
                        AutoSize = true,
                        ForeColour = Color.Red,
                        Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                        OutLine = true,
                        Parent = ItemLabel,
                        Text = remainingSeconds > 0 ? GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.SealedFor), global::Functions.PrintTimeSpanFromSeconds(remainingSeconds)) : string.Empty
                    };

                    ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, SEALEDLabel.DisplayRectangle.Right + 4),
                        Math.Max(ItemLabel.Size.Height, SEALEDLabel.DisplayRectangle.Bottom));
                }
            }

            #endregion

            if (HoverItem.RentalInformation?.RentalLocked == false)
            {
                count++;
                MirLabel OWNERLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.FromArgb(255, 189, 183, 107),
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ItemRentedFrom) + HoverItem.RentalInformation.OwnerName
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, OWNERLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, OWNERLabel.DisplayRectangle.Bottom));

                double remainingTime = (HoverItem.RentalInformation.ExpiryDate - CMain.Now).TotalSeconds;

                count++;
                MirLabel RENTALLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.FromArgb(255, 240, 230, 140),
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = remainingTime > 0 ? GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RentalExpiresIn), global::Functions.PrintTimeSpanFromSeconds(remainingTime)) : "Rental expired"
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, RENTALLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, RENTALLabel.DisplayRectangle.Bottom));
            }
            else if (HoverItem.RentalInformation?.RentalLocked == true && HoverItem.RentalInformation.ExpiryDate > CMain.Now)
            {
                count++;
                var remainingTime = (HoverItem.RentalInformation.ExpiryDate - CMain.Now).TotalSeconds;
                var RentalLockLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.FromArgb(255, 189, 183, 107),
                    Location = new Point(4, ItemLabel.DisplayRectangle.Bottom),
                    OutLine = true,
                    Parent = ItemLabel,
                    Text = remainingTime > 0
                        ? GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.RentalLockExpiresIn), global::Functions.PrintTimeSpanFromSeconds(remainingTime))
                        : "Rental lock expired"
                };

                ItemLabel.Size = new Size(Math.Max(ItemLabel.Size.Width, RentalLockLabel.DisplayRectangle.Right + 4),
                    Math.Max(ItemLabel.Size.Height, RentalLockLabel.DisplayRectangle.Bottom));
            }

            if (count > 0)
            {
                ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height + 4);

                #region OUTLINE
                MirControl outLine = new MirControl
                {
                    BackColour = Color.FromArgb(255, 50, 50, 50),
                    Border = true,
                    BorderColour = Color.Gray,
                    NotControl = true,
                    Parent = ItemLabel,
                    Opacity = 0.4F,
                    Location = new Point(0, 0)
                };
                outLine.Size = ItemLabel.Size;
                #endregion

                return outLine;
            }
            else
            {
                ItemLabel.Size = new Size(ItemLabel.Size.Width, ItemLabel.Size.Height - 4);
            }
            return null;
        }

        public void CreateItemLabel(UserItem item)
        {
            if (item == null || HoverItem != item)
            {
                DisposeItemLabel();

                if (item == null)
                {
                    HoverItem = null;
                    return;
                }
            }

            if (item == HoverItem && ItemLabel != null && !ItemLabel.IsDisposed) return;
            HoverItem = item;

            ItemLabel = new MirControl
            {
                BackColour = Color.FromArgb(255, 0, 0, 0),
                Border = true,
                BorderColour = ((HoverItem.CurrentDura == 0 && HoverItem.MaxDura != 0) ? Color.Red : Color.FromArgb(255, 148, 146, 148)),
                DrawControlTexture = true,
                NotControl = true,
                Parent = this,
                Opacity = 0.8F
            };

            MirControl[] outlines = new MirControl[6];
            outlines[0] = NameInfoLabel(item);
            outlines[1] = AttackInfoLabel(item);
            outlines[2] = DefenceInfoLabel(item);
            outlines[3] = WeightInfoLabel(item);
            outlines[4] = NeedInfoLabel(item);
            outlines[5] = BindInfoLabel(item);

            foreach (var outline in outlines)
            {
                if (outline != null)
                {
                    outline.Size = new Size(ItemLabel.Size.Width, outline.Size.Height);
                }
            }
        }

        public void DisposeItemLabel()
        {
            if (ItemLabel != null && !ItemLabel.IsDisposed)
                ItemLabel.Dispose();
            ItemLabel = null;
        }
    }
}
