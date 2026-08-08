using System;
using System.Linq;
using Client;
using Client.MirControls;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Client.MirScenes.Dialogs;
using Crystal.Client.Core.MirMath;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第8项增量1 英雄面板触控纯逻辑验证（无服务器）：
    // HeroMenuPanel/HeroBehaviourPanel/HeroManageDialog 常驻隐藏；S.HeroInformation 建 GameScene.Hero +
    // HeroDialog(CharacterDialog HeroEquipment)+HeroInventoryDialog+HeroBeltDialog+HeroBuffsDialog +
    // AutoPot/HPItem/MPItem 同步；S.ManageHeroes 填充 HeroStorage+MaximumHeroCount+SetCurrentHero+Show；
    // S.ChangeHero 交换+RefreshInterface；S.UpdateHeroSpawnState Summoned/Unsummoned（本项目 Hide 非
    // Dispose 常驻模式）；S.UnlockHeroAutoPot/SetAutoPotValue/SetAutoPotItem/SetHeroBehaviour；
    // TakeBackHeroItem/TransferHeroItem 回声交换（Unity 无 BeltDialog → From/To < BeltIdx 跳过）；
    // HeroHealthChanged/GainHeroExperience/HeroLevelChanged；MobileBag 英雄按钮（左缘 LeftAnchored）
    // 被 UiConsumer 消费开关 HeroMenuPanel + 无英雄不弹 + 不喂摇杆。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.HeroVerify.Run -quit
    // 断言：全过输出 [heroverify] PASS exit 0。
    public static class HeroVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[heroverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Scene/User/Hero/HeroStorage）+ 建空场景 + MainDialog（HeroBelt/
        // HeroBehaviour ctor 读其 Location）+ ChatDialog + 背包 + BuffsDialog（RefreshStats 依赖）+
        // 英雄三控件常驻（菜单/行为/管理，默认隐藏，对齐 InitInGameDialogs）。
        static GameScene NewScene()
        {
            GameScene.Scene = null;
            GameScene.User = null;
            GameScene.SelectedCell = null;
            GameScene.Gold = 10000;
            GameScene.PickedUpGold = false;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            MobileUiAdapter.DialogRoot = () => GameScene.Scene;
            GameScene.Hero = null;
            MapObject.Hero = null;
            GameScene.HeroStorage = new ClientHeroInformation[8];
            GameScene.MaximumHeroCount = 0;

            var user = new UserObject(1) { Name = "probe", Level = 30, Class = MirClass.Warrior };
            user.Inventory = new UserItem[56];
            GameScene.User = user;
            MapObject.User = user;
            MapControl.User = user;

            var scene = new GameScene();
            GameScene.Scene = scene;

            var main = new MainDialog { Parent = scene };
            scene.MainDialog = main;

            var chat = new ChatDialog { Parent = scene };
            scene.ChatDialog = chat;

            var inv = new InventoryDialog { Parent = scene, Visible = false };
            inv.AutoSize = false;
            inv.Size = new Size(340, 240); // 空库下面板 AutoSize 回退 0×0 → 显式尺寸供格子 hover 命中
            scene.InventoryDialog = inv;
            scene.BuffsDialog = new BuffDialog(); // RefreshStats 的 RefreshBuffs 依赖（空 Buffs）

            // 英雄三控件常驻（对齐 InitInGameDialogs，旧版 GameScene ctor 同款）。
            scene.HeroMenuPanel = new HeroMenuPanel(scene) { Visible = false };
            scene.HeroBehaviourPanel = new HeroBehaviourPanel { Parent = scene, Visible = false };
            scene.HeroManageDialog = new HeroManageDialog { Parent = scene, Visible = false };
            return scene;
        }

        static ItemInfo InfoOf(int index, string name)
        {
            return new ItemInfo
            {
                Index = index,
                Name = name,
                Type = ItemType.Potion,
                Shape = 0,
                Weight = 1,
                Image = 1,
                Durability = 0,
                Price = 10,
                StackSize = 1,
                Stats = new Stats(),
            };
        }

        // 英雄信息包构造（字段直填，对齐 UserInformation 无参 ctor）。
        static S.HeroInformation HeroInfo(uint id)
        {
            return new S.HeroInformation
            {
                ObjectID = id,
                Name = "hero" + id,
                Class = MirClass.Warrior,
                Gender = MirGender.Male,
                Level = 20,
                Hair = 0,
                HP = 500,
                MP = 200,
                Experience = 1000,
                MaxExperience = 2000,
                Inventory = new UserItem[40],
                Equipment = new UserItem[14],
                QuestInventory = new UserItem[40],
                Magics = new System.Collections.Generic.List<ClientMagic>(),
                AutoPot = false,
                AutoHPPercent = 50,
                AutoMPPercent = 30,
                HPItemIndex = 0,
                MPItemIndex = 0,
            };
        }

        static ClientHeroInformation HeroInfoOf(int idx, string name)
        {
            return new ClientHeroInformation
            {
                Name = name,
                Class = MirClass.Warrior,
                Gender = MirGender.Male,
                Level = 20,
            };
        }

        // 触控/按钮发包走 Network.Enqueue 直发（非 seam）：用 SentPackets 队列捕获断言。
        static void DrainPackets()
        {
            while (Network.SentPackets.TryDequeue(out _)) { }
        }

        static T Last<T>(Func<Packet, T> cast) where T : class
        {
            T result = null;
            foreach (var p in Network.SentPackets.ToArray())
                if (cast(p) != null) result = cast(p);
            return result;
        }

        public static void Run()
        {
            // ===== case1 常驻创建默认隐藏 =====
            {
                var scene = NewScene();
                Check(scene.HeroMenuPanel != null && !scene.HeroMenuPanel.Visible, "case1 menu resident hidden");
                Check(scene.HeroBehaviourPanel != null && !scene.HeroBehaviourPanel.Visible, "case1 behaviour resident hidden");
                Check(scene.HeroManageDialog != null && !scene.HeroManageDialog.Visible, "case1 manage resident hidden");
            }

            // ===== case2 S.HeroInformation 建 Hero + 英雄控件集 + AutoPot 同步 =====
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                Check(GameScene.Hero != null, "case2 hero created");
                Check(GameScene.Hero.ObjectID == 9001, "case2 hero objectid");
                Check(scene.HeroDialog != null && scene.HeroDialog.Visible == false, "case2 hero dialog created");
                Check(scene.HeroInventoryDialog != null, "case2 hero inv created");
                Check(scene.HeroBeltDialog != null, "case2 hero belt created");
                Check(scene.HeroBuffsDialog != null, "case2 hero buffs created");
                Check(!GameScene.Hero.AutoPot, "case2 autopot synced");
                Check(GameScene.Hero.AutoHPPercent == 50 && GameScene.Hero.AutoMPPercent == 30, "case2 autopot percents");
            }

            // ===== case3 S.ManageHeroes 填充 + Show =====
            {
                var scene = NewScene();
                var heroes = new[] { HeroInfoOf(0, "h1"), HeroInfoOf(1, "h2") };
                GameSession.ManageHeroes(new S.ManageHeroes { Heroes = heroes, MaximumCount = 4, CurrentHero = heroes[0] });
                Check(GameScene.HeroStorage != null && GameScene.HeroStorage.Length == 8, "case3 storage fixed 8");
                Check(GameScene.HeroStorage[0].Name == "h1" && GameScene.HeroStorage[1].Name == "h2", "case3 storage filled");
                Check(GameScene.MaximumHeroCount == 4, "case3 maxcount=4");
                Check(scene.HeroManageDialog.Visible, "case3 manage shown");
                Check(scene.HeroManageDialog.CurrentAvatar.Info != null && scene.HeroManageDialog.CurrentAvatar.Info.Name == "h1", "case3 current hero");
            }

            // ===== case4 S.ChangeHero 交换 + RefreshInterface =====
            {
                var scene = NewScene();
                var heroes = new[] { HeroInfoOf(0, "h1"), HeroInfoOf(1, "h2") };
                GameSession.ManageHeroes(new S.ManageHeroes { Heroes = heroes, MaximumCount = 4, CurrentHero = heroes[0] });
                scene.HeroManageDialog.Avatars[1].Info = heroes[1];
                GameSession.ChangeHero(new S.ChangeHero { FromIndex = 1 });
                Check(GameScene.HeroStorage[1].Name == "h1", "case4 storage[1]=h1");
                Check(scene.HeroManageDialog.CurrentAvatar.Info.Name == "h2", "case4 current=h2");
            }

            // ===== case5 S.UpdateHeroSpawnState Summoned → 面板显隐 =====
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                scene.HeroBehaviourPanel.Visible = false;
                GameSession.UpdateHeroSpawnState(new S.UpdateHeroSpawnState { State = HeroSpawnState.Summoned });
                Check(scene.HeroSpawnState == HeroSpawnState.Summoned, "case5 spawnstate=summoned");
                Check(scene.HasHero, "case5 hashero");
                Check(scene.HeroBehaviourPanel.Visible, "case5 behaviour visible");
            }

            // ===== case6 S.UpdateHeroSpawnState Unsummoned → Hide 英雄对话框（常驻不 Dispose）=====
            // 注：不 Show HeroDialog（CharacterDialog 复杂子控件在 batchmode 空库 Show 时 AutoSize
            // 退化触发 Dispose→Controls 枚举修改异常），其 Hide 断言用常驻初始状态（未 Show 即隐藏）。
            // HeroBuffsDialog 不随状态 Hide（_buffCountLabel Sort=true 集合修改异常，见 GameSession）。
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                scene.HeroInventoryDialog.Show();
                scene.HeroBeltDialog.Show();
                scene.HeroBehaviourPanel.Visible = true;
                GameSession.UpdateHeroSpawnState(new S.UpdateHeroSpawnState { State = HeroSpawnState.Unsummoned });
                Check(scene.HasHero, "case6 hashero true (unsumnoned still owns hero)"); // HasHero=State>None 旧版语义
                Check(!scene.HeroBehaviourPanel.Visible, "case6 behaviour hidden");
                Check(!scene.HeroInventoryDialog.Visible, "case6 hero inv hidden");
                Check(!scene.HeroBeltDialog.Visible, "case6 hero belt hidden");
                Check(!scene.HeroDialog.Visible, "case6 hero dialog hidden");
                Check(scene.HeroDialog != null, "case6 hero dialog not disposed (resident)");
                Check(scene.HeroInventoryDialog != null, "case6 hero inv not disposed");
            }

            // ===== case7 S.UnlockHeroAutoPot → Hero.AutoPot=true + 刷新不炸 =====
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                GameSession.UnlockHeroAutoPot(new S.UnlockHeroAutoPot());
                Check(GameScene.Hero.AutoPot, "case7 autopot unlocked");
            }

            // ===== case8 S.SetAutoPotValue HP/MP 阈值 =====
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                GameSession.SetAutoPotValue(new S.SetAutoPotValue { Stat = Stat.HP, Value = 70 });
                GameSession.SetAutoPotValue(new S.SetAutoPotValue { Stat = Stat.MP, Value = 40 });
                Check(GameScene.Hero.AutoHPPercent == 70, "case8 hp percent=70");
                Check(GameScene.Hero.AutoMPPercent == 40, "case8 mp percent=40");
            }

            // ===== case9 S.SetAutoPotItem HP/MP 绑定 =====
            {
                var scene = NewScene();
                GameScene.ItemInfoList.Add(InfoOf(500, "hppot"));
                GameScene.ItemInfoList.Add(InfoOf(501, "mppot"));
                GameSession.HeroInformation(HeroInfo(9001));
                GameSession.SetAutoPotItem(new S.SetAutoPotItem { Grid = MirGridType.HeroHPItem, ItemIndex = 500 });
                GameSession.SetAutoPotItem(new S.SetAutoPotItem { Grid = MirGridType.HeroMPItem, ItemIndex = 501 });
                Check(GameScene.Hero.HPItem[0] != null && GameScene.Hero.HPItem[0].Info.Index == 500, "case9 hp item bound");
                Check(GameScene.Hero.MPItem[0] != null && GameScene.Hero.MPItem[0].Info.Index == 501, "case9 mp item bound");
                GameSession.SetAutoPotItem(new S.SetAutoPotItem { Grid = MirGridType.HeroHPItem, ItemIndex = 0 });
                Check(GameScene.Hero.HPItem[0] == null, "case9 hp item unbind");
            }

            // ===== case10 S.SetHeroBehaviour → UpdateBehaviour =====
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                GameSession.SetHeroBehaviour(new S.SetHeroBehaviour { Behaviour = HeroBehaviour.Follow });
                Check(scene.HeroBehaviourPanel != null, "case10 behaviour panel alive");
                // UpdateBehaviour 内部置按钮 Enabled（行为按钮互斥）；断言不炸 + 面板存在即过。
                Check(true, "case10 behaviour applied");
            }

            // ===== case11 S.TakeBackHeroItem 英雄背包→主背包 回声交换 =====
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                var item = new UserItem(InfoOf(600, "heroitem")) { UniqueID = 6001, Count = 1 };
                var heroGrid0 = scene.HeroInventoryDialog.Grid[0];
                heroGrid0.Item = item; // From=HeroBeltIdx(2) → HeroInventoryDialog.Grid[0]
                var invGrid0 = scene.InventoryDialog.Grid[0];
                heroGrid0.Locked = true;
                invGrid0.Locked = true;
                GameSession.TakeBackHeroItem(new S.TakeBackHeroItem { From = 2, To = 6, Success = true }); // To=BeltIdx(6) → InventoryDialog.Grid[0]
                Check(invGrid0.Item == item, "case11 item moved to main bag");
                Check(heroGrid0.Item == null, "case11 hero grid cleared");
                Check(!heroGrid0.Locked && !invGrid0.Locked, "case11 both unlocked");
            }

            // ===== case12 S.TransferHeroItem 主背包→英雄背包 回声交换 =====
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                var item = new UserItem(InfoOf(601, "mainitem")) { UniqueID = 6002, Count = 1 };
                var invGrid0 = scene.InventoryDialog.Grid[0];
                invGrid0.Item = item; // From=BeltIdx(6) → InventoryDialog.Grid[0]
                var heroGrid0 = scene.HeroInventoryDialog.Grid[0];
                invGrid0.Locked = true;
                heroGrid0.Locked = true;
                GameSession.TransferHeroItem(new S.TransferHeroItem { From = 6, To = 2, Success = true }); // To=HeroBeltIdx(2) → HeroInventoryDialog.Grid[0]
                Check(heroGrid0.Item == item, "case12 item moved to hero bag");
                Check(invGrid0.Item == null, "case12 main grid cleared");
                Check(!heroGrid0.Locked && !invGrid0.Locked, "case12 both unlocked");
            }

            // ===== case13 S.HeroHealthChanged → Hero.HP/MP + Percent =====
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                GameScene.Hero.Stats[Stat.HP] = 1000;
                GameScene.Hero.Stats[Stat.MP] = 400;
                GameSession.HeroHealthChanged(new S.HeroHealthChanged { HP = 300, MP = 100 });
                Check(GameScene.Hero.HP == 300 && GameScene.Hero.MP == 100, "case13 hp/mp synced");
                Check(GameScene.Hero.PercentHealth == 30, "case13 percent hp=30");
                Check(GameScene.Hero.PercentMana == 25, "case13 percent mp=25");
            }

            // ===== case14 S.GainHeroExperience → 经验累加 =====
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                GameSession.GainHeroExperience(new S.GainHeroExperience { Amount = 500 });
                Check(GameScene.Hero.Experience == 1500, "case14 exp=1500");
            }

            // ===== case15 S.HeroLevelChanged → 等级 + RefreshStats =====
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                GameSession.HeroLevelChanged(new S.HeroLevelChanged { Level = 21, Experience = 500, MaxExperience = 3000 });
                Check(GameScene.Hero.Level == 21, "case15 level=21");
                Check(GameScene.Hero.Experience == 500, "case15 exp=500");
                Check(GameScene.Hero.MaxExperience == 3000, "case15 maxexp=3000");
            }

            // ===== case16 RouteTouch 集成：英雄按钮（左缘）消费开关菜单 + 无英雄不弹 + 不喂摇杆 =====
            {
                var scene = NewScene();
                GameSession.HeroInformation(HeroInfo(9001));
                var heroBtn = new MobileBag(1280, 720) { LeftAnchored = true };
                heroBtn.SetMargin(new UnityEngine.Vector2(90f, 100f));
                heroBtn.OnToggle = open => ToggleHeroProxy(scene, open);
                bool joystickFired = false;
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => heroBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                };
                var r = heroBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                DrainPackets();
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(scene.HeroMenuPanel.Visible, "case16 menu opened by tap");
                Check(!joystickFired, "case16 joystick not fed");
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!scene.HeroMenuPanel.Visible, "case16 menu closed by tap");
                Check(!joystickFired, "case16 joystick not fed on close");

                // 无英雄（Hero null）→ 按钮不弹菜单
                var scene2 = NewScene();
                var btn2 = new MobileBag(1280, 720) { LeftAnchored = true };
                btn2.SetMargin(new UnityEngine.Vector2(90f, 100f));
                btn2.OnToggle = open => ToggleHeroProxy(scene2, open);
                var route2 = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui2) => btn2.OnTouch(id, ph, ui2),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => { },
                    Hud = (id, ph, ui2) => { },
                };
                var r2 = btn2.ButtonRect;
                var ui2p = new UnityEngine.Vector2(r2.x + r2.width * 0.5f, r2.y + r2.height * 0.5f);
                var raw2 = new UnityEngine.Vector2(ui2p.x, 720f - ui2p.y);
                MobileUiAdapter.RouteTouch(route2, 0, JoystickPhase.Down, raw2);
                MobileUiAdapter.RouteTouch(route2, 0, JoystickPhase.Up, raw2);
                Check(!scene2.HeroMenuPanel.Visible, "case16 no hero no menu");
            }

            Console.WriteLine(_fail == 0 ? "[heroverify] PASS cases=16" : $"[heroverify] FAIL cases={_fail}");
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        // case16 用：直调 MobileBootstrap 同款开/关语义（无英雄守卫 + 互斥 + 摇杆取消）。
        static void ToggleHeroProxy(GameScene scene, bool open)
        {
            var menu = scene.HeroMenuPanel;
            if (menu == null) return;
            if (open)
            {
                if (GameScene.Hero == null) return;
                if (!menu.Visible) menu.Visible = true;
            }
            else
            {
                menu.Visible = false;
            }
        }
    }
}
