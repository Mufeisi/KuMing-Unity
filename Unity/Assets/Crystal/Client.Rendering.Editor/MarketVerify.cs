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
    // 阶段8 第7项增量3 拍卖行触控纯逻辑验证（无服务器）：
    // TrustMerchantDialog 常驻隐藏；S.NPCMarket 整表回流（Bind+Show+UserMode+翻页归零+刷新）、
    // S.NPCMarketPage 追加翻页（面板关守卫）；筛选树/FindButton/RefreshButton 发包
    // （C.MarketSearch{C.MarketRefresh}）；BuyButton 三分支（Consign/GameShop 直发 C.MarketBuy、
    // Auction 弹 MirAmountBox 出价 C.MarketBuy{BidPrice}（3000ms 节流）、UserMode 取回
    // C.MarketGetBack）；SellNowButton/CollectSoldButton 发包；寄售（背包格→ItemCell_Click→
    // SellItemSlot→PriceTextBox→SellItemButton 发 C.ConsignItem）；S.ConsignItem 回声解锁/清格；
    // S.MarketFail 0-10 提示+节流归零；S.MarketSuccess 弹提示；四面板切换发包；MobileBag 拍卖行
    // 按钮（第9枚）被 UiConsumer 消费开关面板 + 不喂摇杆。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.MarketVerify.Run -quit
    // 断言：全过输出 [marketverify] PASS exit 0。
    public static class MarketVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[marketverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam + TrustMerchantDialog 静态（UserMode/MarketType/Selected/
        // SellItemSlot/MarketTime/SearchTime 跨 case 残留）+ 建空场景 + MainDialog + ChatDialog +
        // 背包（显式尺寸）+ 拍卖行面板（常驻隐藏，Size 硬编码 492x478 不退化）。
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
            TrustMerchantDialog.UserMode = false;
            TrustMerchantDialog.MarketType = 0;
            TrustMerchantDialog.Selected = null;
            TrustMerchantDialog.SellItemSlot = null;
            TrustMerchantDialog.MarketTime = 0;
            TrustMerchantDialog.SearchTime = 0;

            var user = new UserObject(1) { Name = "probe", Level = 30 };
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

            var market = new TrustMerchantDialog { Parent = scene, Visible = false };
            scene.TrustMerchantDialog = market;
            return scene;
        }

        static ItemInfo InfoOf(int index, string name, ItemType type)
        {
            return new ItemInfo
            {
                Index = index,
                Name = name,
                Type = type,
                Shape = 0,
                Weight = 1,
                Image = 1,
                Durability = 0,
                Price = 10,
                StackSize = 1,
                Stats = new Stats(),
            };
        }

        static UserItem MakeItem(int index, ulong uid, ItemType type = ItemType.Potion)
        {
            var info = InfoOf(index, "it" + index, type);
            GameScene.ItemInfoList.Add(info);
            return new UserItem(info) { UniqueID = uid, Count = 1 };
        }

        static ClientAuction Auction(ulong id, UserItem item, MarketItemType type, uint price = 100)
        {
            return new ClientAuction
            {
                AuctionID = id,
                Item = item,
                Seller = "seller" + id,
                Price = price,
                ConsignmentDate = DateTime.UtcNow,
                ItemType = type,
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

        // 拍卖行面板里弹出的 MirAmountBox（不依赖 Modal 标志，树遍历找可见实例）。
        static MirAmountBox FindAmountBox()
        {
            var scene = GameScene.Scene;
            if (scene == null || scene.Controls == null) return null;
            for (int i = scene.Controls.Count - 1; i >= 0; i--)
            {
                var c = scene.Controls[i] as MirAmountBox;
                if (c != null && !c.IsDisposed && c.Visible) return c;
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

        // 选中行（static Selected 跨 case 残留 → 每次建新行）。
        static void SelectRow(TrustMerchantDialog dlg, ClientAuction auction)
        {
            var row = new TrustMerchantDialog.AuctionRow { Listing = auction };
            TrustMerchantDialog.Selected = row;
        }

        public static void Run()
        {
            // ===== case1 常驻创建默认隐藏 =====
            {
                var scene = NewScene();
                Check(scene.TrustMerchantDialog != null && !scene.TrustMerchantDialog.Visible, "case1 resident hidden");
            }

            // ===== case2 S.NPCMarket 整表 → Show + UserMode + 翻页归零 =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                var auctions = new List<ClientAuction>();
                for (int i = 0; i < 10; i++) auctions.Add(Auction((ulong)(100 + i), MakeItem(i, (ulong)(200 + i)), MarketItemType.Consign));
                DrainPackets();
                GameSession.NPCMarket(new S.NPCMarket { Listings = auctions, UserMode = false, Pages = 1 });
                Check(dlg.Visible, "case2 shown");
                Check(dlg.Listings.Count == 10, "case2 listings=10");
                Check(!TrustMerchantDialog.UserMode, "case2 usermode=false");
                Check(dlg.Page == 0, "case2 page=0");
                Check(dlg.PageCount == 1, "case2 pagecount=1");
                Check(scene.InventoryDialog.Visible, "case2 bag opened by Show");
            }

            // ===== case3 S.NPCMarket 21 件 → PageCount=3 =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                var auctions = new List<ClientAuction>();
                for (int i = 0; i < 21; i++) auctions.Add(Auction((ulong)(300 + i), MakeItem(i, (ulong)(400 + i)), MarketItemType.Consign));
                DrainPackets();
                GameSession.NPCMarket(new S.NPCMarket { Listings = auctions, UserMode = false, Pages = 3 }); // 21 件 10/页 → 3 页（服务器算好 Pages）
                Check(dlg.PageCount == 3, "case3 pagecount=3");
                Check(TrustMerchantDialog.UserMode == false, "case3 usermode");
            }

            // ===== case4 S.NPCMarketPage 追加一页 → Page 上翻 =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                var p1 = new List<ClientAuction>();
                for (int i = 0; i < 10; i++) p1.Add(Auction((ulong)(500 + i), MakeItem(i, (ulong)(600 + i)), MarketItemType.Consign));
                DrainPackets();
                GameSession.NPCMarket(new S.NPCMarket { Listings = p1, UserMode = false, Pages = 1 });
                var p2 = new List<ClientAuction>();
                for (int i = 0; i < 10; i++) p2.Add(Auction((ulong)(700 + i), MakeItem(i, (ulong)(800 + i)), MarketItemType.Consign));
                DrainPackets();
                GameSession.NPCMarketPage(new S.NPCMarketPage { Listings = p2 });
                Check(dlg.Listings.Count == 20, "case4 listings=20");
                Check(dlg.Page == 1, "case4 page=1");
            }

            // ===== case5 S.NPCMarketPage 面板关守卫：不追加 =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                var p1 = new List<ClientAuction> { Auction(1, MakeItem(0, 2), MarketItemType.Consign) };
                DrainPackets();
                GameSession.NPCMarket(new S.NPCMarket { Listings = p1, UserMode = false, Pages = 1 });
                dlg.Hide();
                var p2 = new List<ClientAuction> { Auction(3, MakeItem(1, 4), MarketItemType.Consign) };
                DrainPackets();
                GameSession.NPCMarketPage(new S.NPCMarketPage { Listings = p2 });
                Check(dlg.Listings.Count == 0, "case5 hidden no append"); // Hide 清空 + Visible 守卫拦截追加
            }

            // ===== case6 FindButton 搜索发包 =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                dlg.Show();
                TrustMerchantDialog.UserMode = false;
                dlg.SearchTextBox.Text = "sword";
                DrainPackets();
                dlg.FindButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var m = Last<C.MarketSearch>(p => p as C.MarketSearch);
                Check(m != null, "case6 search sent");
                Check(m != null && m.Match == "sword", "case6 match=sword");
                Check(m != null && !m.Usermode, "case6 usermode=false");
                Check(m != null && m.MarketType == MarketPanelType.Market, "case6 markettype");
            }

            // ===== case7 筛选树点击发包 =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                dlg.Show();
                TrustMerchantDialog.UserMode = false;
                DrainPackets();
                var btn = dlg.FilterButtons.Where(b => b.Visible && !b.IsDisposed).Skip(1).FirstOrDefault(); // [1]=weapon（[0]=Show All 不发包，Type=Weapon）
                if (btn != null)
                {
                    btn.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                    var m = Last<C.MarketSearch>(p => p as C.MarketSearch);
                    Check(m != null, "case7 filter search sent");
                    Check(m != null && m.Type != ItemType.Nothing, "case7 filter type set");
                    Check(m != null && !m.Usermode, "case7 filter usermode=false");
                }
                else
                {
                    Check(false, "case7 no filter button");
                }
            }

            // ===== case8 RefreshButton 发包 =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                dlg.Show();
                DrainPackets();
                dlg.RefreshButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(Last<C.MarketRefresh>(p => p as C.MarketRefresh) != null, "case8 refresh sent");
            }

            // ===== case9 BuyButton Consign 直发 C.MarketBuy =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                var auction = Auction(11, MakeItem(0, 12), MarketItemType.Consign, 100);
                SelectRow(dlg, auction);
                DrainPackets();
                dlg.BuyButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var buy = Last<C.MarketBuy>(p => p as C.MarketBuy);
                Check(buy != null && buy.AuctionID == 11, "case9 buy sent auctionid=11");
                Check(buy != null && buy.BidPrice == 0, "case9 bidprice=0");
            }

            // ===== case10 BuyButton Auction 弹 MirAmountBox → 输入 → OK 发 C.MarketBuy{BidPrice} =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                var auction = Auction(21, MakeItem(0, 22), MarketItemType.Auction, 500);
                SelectRow(dlg, auction);
                DrainPackets();
                dlg.BuyButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var box = FindAmountBox();
                Check(box != null, "case10 bid box shown");
                if (box != null)
                {
                    box.InputTextBox.TextBox.Text = "500"; // 直写底层文本触发 TextChanged → Amount
                    DrainPackets();
                    box.OKButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                    var buy = Last<C.MarketBuy>(p => p as C.MarketBuy);
                    Check(buy != null && buy.AuctionID == 21, "case10 bid auctionid=21");
                    Check(buy != null && buy.BidPrice == 500, "case10 bidprice=500");
                    Check(FindAmountBox() == null, "case10 bid box closed");
                }
            }

            // ===== case11 BuyButton 节流：MarketTime 未到不发包 =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                var auction = Auction(31, MakeItem(0, 32), MarketItemType.Consign);
                SelectRow(dlg, auction);
                TrustMerchantDialog.MarketTime = CMain.Time + 3000;
                DrainPackets();
                dlg.BuyButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(Last<C.MarketBuy>(p => p as C.MarketBuy) == null, "case11 throttle blocks buy");
                Check(FindAmountBox() == null, "case11 throttle no box");
            }

            // ===== case12 BuyButton UserMode 取回 C.MarketGetBack =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                TrustMerchantDialog.UserMode = true;
                var auction = Auction(41, MakeItem(0, 42), MarketItemType.Consign);
                SelectRow(dlg, auction);
                DrainPackets();
                dlg.BuyButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var gb = Last<C.MarketGetBack>(p => p as C.MarketGetBack);
                Check(gb != null && gb.AuctionID == 41, "case12 getback auctionid=41");
            }

            // ===== case13 SellNowButton 发包 =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                var auction = Auction(51, MakeItem(0, 52), MarketItemType.Consign);
                SelectRow(dlg, auction);
                DrainPackets();
                dlg.SellNowButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var sn = Last<C.MarketSellNow>(p => p as C.MarketSellNow);
                Check(sn != null && sn.AuctionID == 51, "case13 sellnow auctionid=51");
            }

            // ===== case14 CollectSoldButton UserMode → 取回已售 + 刷新 =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                TrustMerchantDialog.UserMode = true;
                DrainPackets();
                dlg.CollectSoldButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var gb = Last<C.MarketGetBack>(p => p as C.MarketGetBack);
                Check(gb != null && gb.Mode == MarketCollectionMode.Sold, "case14 collectsold mode=sold");
                Check(Last<C.MarketRefresh>(p => p as C.MarketRefresh) != null, "case14 refresh sent");
            }

            // ===== case15 寄售：背包格 → ItemCell_Click → PriceTextBox → SellItemButton =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                var user = MapObject.User;
                var item = MakeItem(0, 61);
                user.Inventory[6] = item; // Grid[0].ItemSlot=6
                var grid0 = scene.InventoryDialog.Grid[0];
                grid0.Item = item;
                dlg.Show();
                dlg.TMerchantDialog(MarketPanelType.Consign);
                DrainPackets();
                GameScene.SelectedCell = grid0;
                dlg.ItemCell.OnMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(TrustMerchantDialog.SellItemSlot == item, "case15 sellitemslot set");
                dlg.PriceTextBox.TextBox.Text = "100"; // 直写底层文本触发 TextBox_TextChanged → Amount
                DrainPackets();
                dlg.SellItemButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var ci = Last<C.ConsignItem>(p => p as C.ConsignItem);
                Check(ci != null && ci.UniqueID == 61, "case15 consign uid=61");
                Check(ci != null && ci.Price == 100, "case15 consign price=100");
                Check(ci != null && ci.Type == MarketPanelType.Consign, "case15 consign type");
            }

            // ===== case16 S.ConsignItem 回声成功：解锁 + 清格 + RefreshStats =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                var item = MakeItem(0, 71);
                MapObject.User.Inventory[6] = item;
                var grid0 = scene.InventoryDialog.Grid[0];
                grid0.Item = item;
                grid0.Locked = true;
                GameSession.ConsignItem(new S.ConsignItem { UniqueID = 71, Success = true });
                Check(!grid0.Locked, "case16 unlock");
                Check(grid0.Item == null, "case16 cleared");
            }

            // ===== case17 S.ConsignItem 回声失败：仅解锁保格 =====
            {
                var scene = NewScene();
                var dlg = scene.TrustMerchantDialog;
                var item = MakeItem(0, 81);
                MapObject.User.Inventory[6] = item;
                var grid0 = scene.InventoryDialog.Grid[0];
                grid0.Item = item;
                grid0.Locked = true;
                GameSession.ConsignItem(new S.ConsignItem { UniqueID = 81, Success = false });
                Check(!grid0.Locked, "case17 unlock");
                Check(grid0.Item == item, "case17 kept");
            }

            // ===== case18 S.MarketFail Reason 0 → 提示 + 节流归零 =====
            {
                var scene = NewScene();
                TrustMerchantDialog.MarketTime = CMain.Time + 3000;
                GameSession.MarketFail(new S.MarketFail { Reason = 0 });
                var box = FindModal() as MirMessageBox;
                Check(box != null && box.Modal, "case18 fail prompt shown");
                Check(TrustMerchantDialog.MarketTime == 0, "case18 markettime reset");
            }

            // ===== case19 S.MarketSuccess → 弹 Message + 节流归零 =====
            {
                var scene = NewScene();
                TrustMerchantDialog.MarketTime = CMain.Time + 3000;
                GameSession.MarketSuccess(new S.MarketSuccess { Message = "sold ok" });
                var box = FindModal() as MirMessageBox;
                Check(box != null && box.Modal, "case19 success prompt shown");
                Check(TrustMerchantDialog.MarketTime == 0, "case19 markettime reset");
            }

            // ===== case20 RouteTouch 集成：拍卖行按钮被 UiConsumer 消费开关面板 + 不喂摇杆 =====
            {
                var scene = NewScene();
                var g = scene.TrustMerchantDialog;
                var marketBtn = new MobileBag(1280, 720);
                marketBtn.SetMargin(new UnityEngine.Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 8));
                marketBtn.OnToggle = open => ToggleMarketProxy(scene, open);
                bool joystickFired = false;
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => marketBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                };
                var r = marketBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                DrainPackets();
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(g.Visible, "case20 opened by tap");
                Check(!joystickFired, "case20 joystick not fed");
                Check(Last<C.MarketSearch>(p => p as C.MarketSearch) != null, "case20 search sent on open");
                DrainPackets();
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!g.Visible, "case20 closed by tap");
                Check(!joystickFired, "case20 joystick not fed on close");
            }

            Console.WriteLine(_fail == 0 ? "[marketverify] PASS cases=20" : $"[marketverify] FAIL cases={_fail}");
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        // case20 用：直调 MobileBootstrap 同款开/关语义（互斥关其他面板 + Show/Hide）。
        static void ToggleMarketProxy(GameScene scene, bool open)
        {
            var market = scene.TrustMerchantDialog;
            if (market == null) return;
            if (open)
            {
                if (!market.Visible)
                {
                    market.Show();
                }
            }
            else
            {
                market.Hide();
            }
        }
    }
}
