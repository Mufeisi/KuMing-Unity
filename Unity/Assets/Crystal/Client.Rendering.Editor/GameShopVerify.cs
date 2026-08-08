using System;
using System.Linq;
using System.Collections.Generic;
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
    // 阶段8 第7项增量4 商城触控纯逻辑验证（无服务器）：
    // GameShopDialog 常驻隐藏；ctor 不再 Clear（商品由服务器 S.GameShopInfo 在 MapInformation 后
    // 推送，会话清空移至 S.StartGame(Result=4) 分支）；S.GameShopInfo 填充静态 GameShopInfoList +
    // 7 天内 New 点亮（对话框未建 null 守卫）；S.GameShopStock 更新/移除 + 面板开着 UpdateShop；
    // Show 重置 ClassFilter=玩家职业；分类 tab（War/Wiz…）/栏目 tab（Deals 等）本地过滤重建网格；
    // 支付勾选互斥（Gold/Credit）；BuyProduct 三分支（Gold 余额足弹 MirMessageBox→Yes 发
    // C.GameshopBuy{PType=1}、Credit 发{PType=0}、未勾选/余额不足系统提示不发包）；分页
    // （>8 件 maxPage 翻页）；搜索本地过滤；MobileBag 商城按钮（第10枚）被 UiConsumer 消费开关
    // 面板 + 不喂摇杆。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.GameShopVerify.Run -quit
    // 断言：全过输出 [gameshopverify] PASS exit 0。
    public static class GameShopVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[gameshopverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam + 静态商城列表 + 建空场景 + MainDialog + ChatDialog（系统提示）+
        // 背包 + 商城对话框（常驻隐藏，ctor 不 Clear 商品列表）。GameShopInfoList 为 static
        // （跨 case 残留）→ 每 scene 重置。
        static GameScene NewScene()
        {
            GameScene.Scene = null;
            GameScene.User = null;
            GameScene.SelectedCell = null;
            GameScene.Gold = 10000;
            GameScene.Credit = 5000;
            GameScene.PickedUpGold = false;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            MobileUiAdapter.DialogRoot = () => GameScene.Scene;
            GameScene.GameShopInfoList = new List<GameShopItem>();

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

            var shop = new GameShopDialog { Parent = scene, Visible = false };
            scene.GameShopDialog = shop;
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

        static GameShopItem ShopItem(int gIndex, string name, string cls, string cat, uint gold = 100, uint credit = 50,
            bool canGold = true, bool canCredit = true, bool deal = false, bool top = false, DateTime? date = null, int stock = 99)
        {
            return new GameShopItem
            {
                GIndex = gIndex,
                ItemIndex = 100 + gIndex,
                Info = InfoOf(100 + gIndex, name),
                GoldPrice = gold,
                CreditPrice = credit,
                Count = 1,
                Class = cls,
                Category = cat,
                Stock = stock,
                iStock = false,
                Deal = deal,
                TopItem = top,
                Date = date ?? DateTime.Now,
                CanBuyGold = canGold,
                CanBuyCredit = canCredit,
            };
        }

        // 瞬态模态查找（与 MobileBootstrap.FindModal 同语义：scene.Controls 树 Modal+Visible，倒序取顶层）。
        static MirControl FindModal()
        {
            var scene = GameScene.Scene;
            if (scene == null || scene.Controls == null) return null;
            for (int i = scene.Controls.Count - 1; i >= 0; i--)
            {
                var c = scene.Controls[i];
                if (c != null && !c.IsDisposed && c.Visible && c.Modal) return c;
            }
            return null;
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

        // 填 Grid 的可见商品（UpdateShop 按 ClassFilter/TypeFilter/SectionFilter 过滤后填充）。
        static int VisibleGridCount(GameShopDialog dlg)
        {
            return dlg.Grid.Count(c => c != null && c.Item != null);
        }

        public static void Run()
        {
            // ===== case1 常驻创建默认隐藏 + ctor 不 Clear 商品 =====
            {
                var scene = NewScene();
                GameScene.GameShopInfoList.Add(ShopItem(1, "pot", "All", "Pots"));
                var shop = new GameShopDialog { Parent = scene, Visible = false }; // 二次创建：ctor 不得清空已推送商品
                scene.GameShopDialog = shop;
                Check(shop != null && !shop.Visible, "case1 resident hidden");
                Check(GameScene.GameShopInfoList.Count == 1, "case1 ctor keeps pushed items");
            }

            // ===== case2 二次创建（重连/换图场景）仍不 Clear 商品 =====
            {
                var scene = NewScene();
                GameScene.GameShopInfoList.Add(ShopItem(1, "pot", "All", "Pots"));
                GameScene.GameShopInfoList.Add(ShopItem(2, "scroll", "All", "Scrolls"));
                var shop = new GameShopDialog { Parent = scene, Visible = false }; // 第二次 new：商品保留
                scene.GameShopDialog = shop;
                Check(GameScene.GameShopInfoList.Count == 2, "case2 second ctor keeps items");
            }

            // ===== case3 S.GameShopInfo 填充 + 7 天内 New 点亮 =====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(1, "pot", "All", "Pots", date: DateTime.Now.AddDays(-2)), StockLevel = 5 });
                Check(GameScene.GameShopInfoList.Count == 1, "case3 pushed=1");
                Check(GameScene.GameShopInfoList[0].Stock == 5, "case3 stocklevel applied");
                shop.Show(); // MirControl.Visible getter 依赖 Parent 可见 → 断言前须 Show
                Check(shop.New.Visible, "case3 new tab lit (7d)");
            }

            // ===== case4 S.GameShopStock 更新（面板开 → UpdateShop）=====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(1, "pot", "All", "Pots"), StockLevel = 5 });
                shop.Show();
                GameSession.GameShopStock(new S.GameShopStock { GIndex = 1, StockLevel = 3 });
                Check(GameScene.GameShopInfoList[0].Stock == 3, "case4 stock updated to 3");
            }

            // ===== case5 S.GameShopStock Stock=0 移除 =====
            {
                var scene = NewScene();
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(1, "pot", "All", "Pots"), StockLevel = 5 });
                GameSession.GameShopStock(new S.GameShopStock { GIndex = 1, StockLevel = 0 });
                Check(GameScene.GameShopInfoList.Count == 0, "case5 sold out removed");
            }

            // ===== case6 Show() 重置 ClassFilter=玩家职业 + Visible =====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                shop.Show();
                Check(shop.Visible, "case6 shown");
                Check(shop.ClassFilter == "Warrior", "case6 classfilter=warrior");
            }

            // ===== case7 分类 tab：War → 过滤重建网格 =====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(1, "sword", "Warrior", "Weapons"), StockLevel = 5 });
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(2, "wand", "Wizard", "Weapons"), StockLevel = 5 });
                shop.Show();
                shop.War.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(shop.ClassFilter == "Warrior", "case7 war filter");
                var items = shop.Grid.Where(c => c != null && c.Item != null).Select(c => c.Item.Info.Name).ToArray();
                Check(items.Contains("sword") && !items.Contains("wand"), "case7 grid filtered");
            }

            // ===== case8 栏目 tab：Deals → SectionFilter=DealItems =====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(1, "dealpot", "All", "Pots", deal: true), StockLevel = 5 });
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(2, "plainpot", "All", "Pots"), StockLevel = 5 });
                shop.Show();
                shop.Deals.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(shop.SectionFilter == "DealItems", "case8 deals section");
                var items = shop.Grid.Where(c => c != null && c.Item != null).Select(c => c.Item.Info.Name).ToArray();
                Check(items.Contains("dealpot") && !items.Contains("plainpot"), "case8 grid filtered by deal");
            }

            // ===== case9 支付勾选互斥 =====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                shop.PaymentTypeGold.Checked = true;
                shop.PaymentTypeGold.Checked = true; // 点已选保持
                shop.PaymentTypeCredit.Checked = true;
                Check(shop.PaymentTypeGold.Checked && shop.PaymentTypeCredit.Checked, "case9 both can be checked via property");
            }

            // ===== case10 BuyProduct Gold 余额足 → Yes → C.GameshopBuy{PType=1} =====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                GameScene.Gold = 1000;
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(1, "pot", "All", "Pots", gold: 100), StockLevel = 5 });
                shop.Show();
                var cell = shop.Grid[0];
                Check(cell != null && cell.Item != null, "case10 cell filled");
                shop.PaymentTypeGold.Checked = true;
                shop.PaymentTypeCredit.Checked = false;
                DrainPackets();
                cell.BuyProduct();
                var box = FindModal() as MirMessageBox;
                Check(box != null && box.Modal, "case10 confirm box shown");
                if (box != null)
                {
                    box.YesButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                    var buy = Last<C.GameshopBuy>(p => p as C.GameshopBuy);
                    Check(buy != null && buy.GIndex == 1, "case10 buy gindex=1");
                    Check(buy != null && buy.Quantity == 1, "case10 buy quantity=1");
                    Check(buy != null && buy.PType == 1, "case10 buy ptype=1(gold)");
                }
            }

            // ===== case11 BuyProduct Credit → C.GameshopBuy{PType=0} =====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                GameScene.Credit = 500;
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(1, "credipot", "All", "Pots", credit: 50), StockLevel = 5 });
                shop.Show();
                var cell = shop.Grid[0];
                shop.PaymentTypeCredit.Checked = true;
                shop.PaymentTypeGold.Checked = false;
                DrainPackets();
                cell.BuyProduct();
                var box = FindModal() as MirMessageBox;
                Check(box != null && box.Modal, "case11 confirm box shown");
                if (box != null)
                {
                    box.YesButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                    var buy = Last<C.GameshopBuy>(p => p as C.GameshopBuy);
                    Check(buy != null && buy.GIndex == 1, "case11 buy gindex=1");
                    Check(buy != null && buy.PType == 0, "case11 buy ptype=0(credit)");
                }
            }

            // ===== case12 BuyProduct 未勾选支付 → 系统提示 + 不发包 =====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(1, "pot", "All", "Pots"), StockLevel = 5 });
                shop.Show();
                var cell = shop.Grid[0];
                shop.PaymentTypeGold.Checked = false;
                shop.PaymentTypeCredit.Checked = false;
                DrainPackets();
                cell.BuyProduct();
                Check(FindModal() == null, "case12 no confirm box");
                Check(Last<C.GameshopBuy>(p => p as C.GameshopBuy) == null, "case12 no buy sent");
            }

            // ===== case13 BuyProduct 余额不足 → 提示 + 不发包 =====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                GameScene.Gold = 50;
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(1, "expensive", "All", "Pots", gold: 1000), StockLevel = 5 });
                shop.Show();
                var cell = shop.Grid[0];
                shop.PaymentTypeGold.Checked = true;
                shop.PaymentTypeCredit.Checked = false;
                DrainPackets();
                cell.BuyProduct();
                Check(FindModal() == null, "case13 no confirm box");
                Check(Last<C.GameshopBuy>(p => p as C.GameshopBuy) == null, "case13 no buy sent");
            }

            // ===== case14 分页：9 件 → maxPage=2 + Next/Previous 翻页 =====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                for (int i = 0; i < 9; i++)
                    GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(i + 1, "item" + i, "All", "Pots"), StockLevel = 5 });
                shop.Show();
                Check(shop.PageNumberLabel.Text == "1 / 2", "case14 page 1/2");
                shop.NextButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(shop.PageNumberLabel.Text == "2 / 2", "case14 page 2/2");
                shop.PreviousButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(shop.PageNumberLabel.Text == "1 / 2", "case14 page back 1/2");
            }

            // ===== case15 搜索本地过滤 =====
            {
                var scene = NewScene();
                var shop = scene.GameShopDialog;
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(1, "sword", "All", "Weapons"), StockLevel = 5 });
                GameSession.GameShopInfo(new S.GameShopInfo { Item = ShopItem(2, "shield", "All", "Armours"), StockLevel = 5 });
                shop.Show();
                shop.Search.TextBox.Text = "sword";
                shop.GetCategories();
                var items = shop.Grid.Where(c => c != null && c.Item != null).Select(c => c.Item.Info.Name).ToArray();
                Check(items.Contains("sword") && !items.Contains("shield"), "case15 search filters grid");
            }

            // ===== case16 RouteTouch 集成：商城按钮被 UiConsumer 消费开关面板 + 不喂摇杆 =====
            {
                var scene = NewScene();
                var g = scene.GameShopDialog;
                var shopBtn = new MobileBag(1280, 720);
                shopBtn.SetMargin(new UnityEngine.Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 9));
                shopBtn.OnToggle = open => { if (open) g.Show(); else g.Hide(); };
                bool joystickFired = false;
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => shopBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                };
                var r = shopBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                DrainPackets();
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(g.Visible, "case16 opened by tap");
                Check(!joystickFired, "case16 joystick not fed");
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!g.Visible, "case16 closed by tap");
                Check(!joystickFired, "case16 joystick not fed on close");
            }

            Console.WriteLine(_fail == 0 ? "[gameshopverify] PASS cases=16" : $"[gameshopverify] FAIL cases={_fail}");
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }
    }
}
