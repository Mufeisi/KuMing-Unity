using System;
using System.Collections.Generic;
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
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第7项 交易流程触控纯逻辑验证（无服务器）：
    // S.TradeRequest 被邀弹 MirMessageBox YesNo → Yes/No 发 C.TradeReply{AcceptInvite}；S.TradeAccept
    // 记对方名 + 开双方面板（背包移右上）；S.TradeGold/TradeItem 对方改动回声 → 记数 + 解锁 + 刷新；
    // ConfirmButton 锁定/解锁 → C.TradeConfirm{Locked}；S.TradeConfirm 完成 → TradeReset（清物品/金币/
    // 解锁/隐藏双方）；S.TradeCancel Unlock 仅解锁 / 否则重置+提示；DepositTradeItem/RetrieveTradeItem
    // 回声成功 → 本地移物品 + 解锁（From<BeltIdx 腰带格跳过）；MirItemCell 两段式触控（选中源格→点空
    // 目标格）发 C.DepositTradeItem/RetrieveTradeItem；GoldLabel 点弹 MirAmountBox（8-5-1 软键盘输入）→
    // C.TradeGold{Amount} + TradeGoldAmount 累加（Gold>0 且无选中守卫）；MobileTrade 地图 tap 玩家 →
    // C.TradeRequest（3000ms 节流，非己方/非死亡，命中消费）；CloseButton 关双方 + C.TradeCancel。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.TradeVerify.Run -quit
    // 断言：全过输出 [tradeverify] PASS exit 0。
    public static class TradeVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[tradeverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Scene/User/SelectedCell/Gold/Objects/Guest 静态）+ 建空场景 +
        // MainDialog + 背包（显式尺寸供父矩形命中）+ 交易双方面板（常驻隐藏）。玩家供 GameScene.User
        // （TradeConfirm/TradeGoldAmount）+ MapObject.User（Grid ItemArray 取背包/交易数组）。
        static GameScene NewScene()
        {
            GameScene.Scene = null;
            GameScene.User = null;
            GameScene.SelectedCell = null;
            GameScene.Gold = 0;
            GameScene.PickedUpGold = false;
            GuestTradeDialog.GuestItems = new UserItem[10]; // GuestName/GuestGold 为实例字段，随新实例复位
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;

            var user = new UserObject(1) { Name = "probe" };
            GameScene.User = user;
            MapObject.User = user;
            MapControl.User = user;

            var scene = new GameScene();
            GameScene.Scene = scene;

            var main = new MainDialog { Parent = scene };
            scene.MainDialog = main;

            var inv = new InventoryDialog { Parent = scene, Visible = false };
            inv.AutoSize = false;
            inv.Size = new Size(340, 240); // 空库下面板 AutoSize 回退 0×0 → 显式尺寸供格子 hover 命中
            scene.InventoryDialog = inv;

            var trade = new TradeDialog { Parent = scene, Visible = false };
            scene.TradeDialog = trade;
            var guest = new GuestTradeDialog { Parent = scene, Visible = false };
            scene.GuestTradeDialog = guest;
            return scene;
        }

        // 地图夹具（MobileTrade tap 用例）：30x30 全空网格 + 玩家（同 NpcVerify.NewMap）。
        static MapControl NewMap(int px, int py, out UserObject user)
        {
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            GameScene.Scene = null;
            GameScene.User = null;
            GuestTradeDialog.GuestItems = new UserItem[10];
            MapControl.OffSetX = 0;
            MapControl.OffSetY = 0;

            var mc = new MapControl { Width = 30, Height = 30, M2CellInfo = new CellInfo[30, 30] };
            for (int x = 0; x < 30; x++)
                for (int y = 0; y < 30; y++)
                    mc.M2CellInfo[x, y] = new CellInfo();
            mc.PathFinder = new PathFinder(mc);

            user = new UserObject(1)
            {
                Movement = new MPoint(px, py),
                CurrentLocation = new MPoint(px, py),
                OffSetMove = MPoint.Empty,
                Direction = MirDirection.Up,
                Name = "probe",
            };
            MapObject.User = user;
            MapControl.User = user;
            GameScene.Scene = new GameScene { MapControl = mc };
            return mc;
        }

        // PlayerObject ctor 自动注册 Objects+ObjectsList；仅需补位置字段。
        static PlayerObject SpawnPlayer(uint id, int x, int y, bool dead = false)
        {
            return new PlayerObject(id)
            {
                Movement = new MPoint(x, y),
                CurrentLocation = new MPoint(x, y),
                Dead = dead,
            };
        }

        // 格 → 屏（ui 空间）：屏→格逆变换（同 NpcVerify.UiOf）。
        static MPoint UiOf(MapControl mc, UserObject user, MPoint tile)
        {
            return new MPoint(
                (tile.X - user.Movement.X + MapControl.OffSetX) * MapControl.CellWidth,
                (tile.Y - user.Movement.Y + MapControl.OffSetY) * MapControl.CellHeight);
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
                StackSize = 100,
                Stats = new Stats(),
            };
        }

        static UserItem MakeItem(int index, ulong uid)
        {
            var info = InfoOf(index, "it" + index);
            GameScene.ItemInfoList.Add(info);
            return new UserItem(info) { UniqueID = uid, Count = 1 };
        }

        // 点按分发（与 TouchInputAdapter 同链路：Move 更新 hover → Down 置 ActiveControl → Up+Click）。
        static void Tap(MPoint p)
        {
            var sc = GameScene.Scene;
            CMain.MPoint = p;
            sc.OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0));
            sc.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
            sc.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
            sc.OnMouseClick(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        }

        static MPoint Center(MirControl c)
        {
            var r = c.DisplayRectangle;
            return new MPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        // 格点击（交易两段式）：直接调 MirItemCell.OnMouseClick（选中/两段式逻辑在 override 内，
        // 不依赖两面板几何命中）。
        static void ClickCell(MirItemCell cell)
        {
            cell.OnMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
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

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;

            // ===== case1 常驻创建：TradeDialog + GuestTradeDialog 挂 scene 默认隐藏 =====
            {
                var scene = NewScene();
                Check(scene.TradeDialog != null && scene.GuestTradeDialog != null, "case1 dialogs attached");
                Check(!scene.TradeDialog.Visible && !scene.GuestTradeDialog.Visible, "case1 hidden by default");
            }

            // ===== case2 S.TradeRequest → MirMessageBox YesNo（模态） =====
            {
                var scene = NewScene();
                GameSession.TradeRequest(new S.TradeRequest { Name = "Alice" });
                var box = FindModal() as MirMessageBox;
                Check(box != null && box.Modal && box.YesButton != null && box.NoButton != null, "case2 request prompt shown");
            }

            // ===== case3 Yes → C.TradeReply{AcceptInvite=true} =====
            {
                var scene = NewScene();
                DrainPackets();
                GameSession.TradeRequest(new S.TradeRequest { Name = "Alice" });
                var box = FindModal() as MirMessageBox;
                if (box == null) { /* fail 已记 */ }
                else
                {
                    box.YesButton.InvokeMouseClick(EventArgs.Empty);
                    var reply = Last(p => p as C.TradeReply);
                    Check(reply != null && reply.AcceptInvite, "case3 accept sent");
                    Check(FindModal() == null, "case3 prompt dismissed");
                }
            }

            // ===== case4 No → C.TradeReply{AcceptInvite=false} =====
            {
                var scene = NewScene();
                DrainPackets();
                GameSession.TradeRequest(new S.TradeRequest { Name = "Alice" });
                var box = FindModal() as MirMessageBox;
                if (box == null) { /* fail 已记 */ }
                else
                {
                    box.NoButton.InvokeMouseClick(EventArgs.Empty);
                    var reply = Last(p => p as C.TradeReply);
                    Check(reply != null && !reply.AcceptInvite, "case4 reject sent");
                }
            }

            // ===== case5 S.TradeAccept → 记对方名 + 双方面板 Show + 背包移右上 =====
            {
                var scene = NewScene();
                GameSession.TradeAccept(new S.TradeAccept { Name = "Alice" });
                Check(scene.GuestTradeDialog.GuestName == "Alice", "case5 guest name set");
                Check(scene.TradeDialog.Visible && scene.GuestTradeDialog.Visible, "case5 both shown");
                Check(scene.InventoryDialog.Visible, "case5 inventory shown");
            }

            // ===== case6 S.TradeGold → GuestGold + 解锁 + 金币标签刷新 =====
            {
                var scene = NewScene();
                var dlg = scene.TradeDialog;
                var guest = scene.GuestTradeDialog;
                dlg.ChangeLockState(true);
                GameSession.TradeGold(new S.TradeGold { Amount = 1234 });
                Check(guest.GuestGold == 1234, "case6 guest gold set");
                Check(!GameScene.User.TradeLocked, "case6 unlocked");
                Check(guest.GuestGoldLabel.Text == "1,234", "case6 gold label refreshed");
            }

            // ===== case7 S.TradeItem → GuestItems + 解锁 + 刷新 =====
            {
                var scene = NewScene();
                var dlg = scene.TradeDialog;
                var sword = MakeItem(500, 9001);
                dlg.ChangeLockState(true);
                GameSession.TradeItem(new S.TradeItem { TradeItems = new[] { sword } });
                Check(GuestTradeDialog.GuestItems[0] == sword, "case7 guest items set");
                Check(!GameScene.User.TradeLocked, "case7 unlocked");
            }

            // ===== case8 ConfirmButton 锁定/解锁 → C.TradeConfirm{Locked} + 态切换 =====
            {
                var scene = NewScene();
                var dlg = scene.TradeDialog;
                GameScene.User.TradeLocked = false;
                DrainPackets();
                dlg.ConfirmButton.InvokeMouseClick(EventArgs.Empty);
                var c1 = Last(p => p as C.TradeConfirm);
                Check(c1 != null && c1.Locked, "case8 lock sent");
                Check(GameScene.User.TradeLocked, "case8 locked state");
                DrainPackets();
                dlg.ConfirmButton.InvokeMouseClick(EventArgs.Empty);
                var c2 = Last(p => p as C.TradeConfirm);
                Check(c2 != null && !c2.Locked, "case8 unlock sent");
            }

            // ===== case9 S.TradeConfirm → TradeReset（清物品/金币/解锁/隐藏双方） =====
            {
                var scene = NewScene();
                var dlg = scene.TradeDialog;
                dlg.Grid[0].Item = MakeItem(501, 9002);
                GameScene.User.TradeGoldAmount = 500;
                dlg.ChangeLockState(true);
                dlg.Show();
                scene.GuestTradeDialog.Show();
                GameSession.TradeConfirm();
                Check(dlg.Grid[0].Item == null, "case9 trade items cleared");
                Check(GameScene.User.TradeGoldAmount == 0, "case9 gold cleared");
                Check(!GameScene.User.TradeLocked, "case9 unlocked");
                Check(!dlg.Visible && !scene.GuestTradeDialog.Visible, "case9 both hidden");
            }

            // ===== case10 S.TradeCancel{Unlock=true} → 仅解锁 =====
            {
                var scene = NewScene();
                var dlg = scene.TradeDialog;
                dlg.ChangeLockState(true);
                dlg.Show();
                GameSession.TradeCancel(new S.TradeCancel { Unlock = true });
                Check(!GameScene.User.TradeLocked, "case10 unlock only");
                Check(dlg.Visible, "case10 dialog stays");
            }

            // ===== case11 S.TradeCancel{Unlock=false} → TradeReset + 提示框 =====
            {
                var scene = NewScene();
                var dlg = scene.TradeDialog;
                dlg.Grid[0].Item = MakeItem(502, 9003);
                dlg.Show();
                scene.GuestTradeDialog.Show();
                GameSession.TradeCancel(new S.TradeCancel { Unlock = false });
                Check(!dlg.Visible && !scene.GuestTradeDialog.Visible, "case11 both hidden");
                Check(dlg.Grid[0].Item == null, "case11 items cleared");
                Check(FindModal() as MirMessageBox != null, "case11 cancel prompt shown");
            }

            // ===== case12 S.DepositTradeItem 放入回声：背包→交易格移物品 + 解锁 =====
            {
                var scene = NewScene();
                var sword = MakeItem(503, 9004);
                MapObject.User.Inventory[6] = sword; // InventoryDialog.Grid[0].ItemSlot=6
                scene.InventoryDialog.Grid[0].Locked = true;
                scene.TradeDialog.Grid[0].Locked = true;
                scene.TradeDialog.ChangeLockState(true);
                GameSession.DepositTradeItem(new S.DepositTradeItem { From = 6, To = 0, Success = true });
                Check(scene.TradeDialog.Grid[0].Item == sword && MapObject.User.Inventory[6] == null, "case12 deposit echo moved");
                Check(!scene.InventoryDialog.Grid[0].Locked && !scene.TradeDialog.Grid[0].Locked, "case12 cells unlocked");
                Check(!GameScene.User.TradeLocked, "case12 trade unlocked");
            }

            // ===== case13 S.RetrieveTradeItem 取回回声：交易格→背包 + 解锁 =====
            {
                var scene = NewScene();
                var sword = MakeItem(504, 9005);
                scene.TradeDialog.Grid[0].Item = sword;
                scene.TradeDialog.Grid[0].Locked = true;
                scene.InventoryDialog.Grid[0].Locked = true;
                scene.TradeDialog.ChangeLockState(true);
                GameSession.RetrieveTradeItem(new S.RetrieveTradeItem { From = 0, To = 6, Success = true });
                Check(MapObject.User.Inventory[6] == sword && scene.TradeDialog.Grid[0].Item == null, "case13 retrieve echo moved");
                Check(!scene.TradeDialog.Grid[0].Locked && !scene.InventoryDialog.Grid[0].Locked, "case13 cells unlocked");
                Check(!GameScene.User.TradeLocked, "case13 trade unlocked");
            }

            // ===== case14 两段式触控放入：选中背包源格 → 点空交易目标格 → C.DepositTradeItem{From=6,To=0} =====
            {
                var scene = NewScene();
                var sword = MakeItem(505, 9006);
                MapObject.User.Inventory[6] = sword;
                DrainPackets();
                ClickCell(scene.InventoryDialog.Grid[0]); // Item=sword → 选中源格
                Check(GameScene.SelectedCell == scene.InventoryDialog.Grid[0], "case14 bag cell selected");
                ClickCell(scene.TradeDialog.Grid[0]); // 空交易格 → 两段式放入
                var dep = Last(p => p as C.DepositTradeItem);
                Check(dep != null && dep.From == 6 && dep.To == 0, "case14 deposit packet");
                Check(scene.InventoryDialog.Grid[0].Locked && scene.TradeDialog.Grid[0].Locked, "case14 both locked");
                Check(GameScene.SelectedCell == null, "case14 selection cleared");
            }

            // ===== case15 两段式触控取回：选中交易源格 → 点空背包目标格 → C.RetrieveTradeItem{From=0,To=6} =====
            {
                var scene = NewScene();
                scene.TradeDialog.Grid[0].Item = MakeItem(506, 9007);
                DrainPackets();
                ClickCell(scene.TradeDialog.Grid[0]); // Item=sword → 选中源格
                Check(GameScene.SelectedCell == scene.TradeDialog.Grid[0], "case15 trade cell selected");
                ClickCell(scene.InventoryDialog.Grid[0]); // 空背包格 → 两段式取回
                var ret = Last(p => p as C.RetrieveTradeItem);
                Check(ret != null && ret.From == 0 && ret.To == 6, "case15 retrieve packet");
                Check(scene.TradeDialog.Grid[0].Locked && scene.InventoryDialog.Grid[0].Locked, "case15 both locked");
            }

            // ===== case16 GoldLabel → MirAmountBox 弹框 + 输入 + OK → C.TradeGold{Amount} + 累加 =====
            {
                var scene = NewScene();
                var dlg = scene.TradeDialog;
                GameScene.Gold = 5000;
                GameScene.SelectedCell = null;
                DrainPackets();
                dlg.GoldLabel.InvokeMouseClick(EventArgs.Empty);
                var box = FindModal() as MirAmountBox;
                Check(box != null, "case16 amount box shown");
                if (box == null) { /* fail 已记 */ }
                else
                {
                    Check(box.MaxAmount == 5000, "case16 max is gold");
                    box.InputTextBox.Text = "123";
                    box.OKButton.InvokeMouseClick(EventArgs.Empty);
                    var gold = Last(p => p as C.TradeGold);
                    Check(gold != null && gold.Amount == 123, "case16 trade gold sent");
                    Check(GameScene.User.TradeGoldAmount == 123, "case16 gold amount accumulated");
                }
            }

            // ===== case17 Gold=0 守卫：无金币不弹框（对齐旧客户端 Gold>0 条件） =====
            {
                var scene = NewScene();
                var dlg = scene.TradeDialog;
                GameScene.Gold = 0;
                GameScene.SelectedCell = null;
                DrainPackets();
                dlg.GoldLabel.InvokeMouseClick(EventArgs.Empty);
                Check(FindModal() == null, "case17 no box with zero gold");
                Check(Last(p => p as C.TradeGold) == null, "case17 no gold packet");
            }

            // ===== case18 MobileTrade 命中玩家：发 C.TradeRequest（节流首发） =====
            {
                var calls = new List<C.TradeRequest>();
                MobileTrade.SendTradeRequest = () => calls.Add(new C.TradeRequest());
                var mc = NewMap(10, 10, out var user);
                SpawnPlayer(7001, 11, 10);
                CMain.Time = 10000;
                var tradeCtrl = new MobileTrade();
                Check(tradeCtrl.TapAt(mc, UiOf(mc, user, new MPoint(11, 10))), "case18 tap hit player");
                Check(calls.Count == 1, "case18 request sent");
            }

            // ===== case19 无玩家：拒绝 + 不发包（落回拾取） =====
            {
                var mc = NewMap(10, 10, out var user);
                var calls = new List<C.TradeRequest>();
                MobileTrade.SendTradeRequest = () => calls.Add(new C.TradeRequest());
                var tradeCtrl = new MobileTrade();
                Check(!tradeCtrl.TapAt(mc, UiOf(mc, user, new MPoint(12, 10))), "case19 no player rejected");
                Check(calls.Count == 0, "case19 no request");
            }

            // ===== case20 死亡玩家排除 =====
            {
                var mc = NewMap(10, 10, out var user);
                var calls = new List<C.TradeRequest>();
                MobileTrade.SendTradeRequest = () => calls.Add(new C.TradeRequest());
                SpawnPlayer(7003, 11, 10, dead: true);
                var tradeCtrl = new MobileTrade();
                Check(!tradeCtrl.TapAt(mc, UiOf(mc, user, new MPoint(11, 10))), "case20 dead excluded");
                Check(calls.Count == 0, "case20 no request");
            }

            // ===== case21 节流 3000ms：期内消费不重发，期后重发 =====
            {
                var mc = NewMap(10, 10, out var user);
                var calls = new List<C.TradeRequest>();
                MobileTrade.SendTradeRequest = () => calls.Add(new C.TradeRequest());
                SpawnPlayer(7004, 11, 10);
                var tradeCtrl = new MobileTrade();
                CMain.Time = 10000;
                tradeCtrl.TapAt(mc, UiOf(mc, user, new MPoint(11, 10)));
                Check(calls.Count == 1, "case21 first request");
                CMain.Time = 10400;
                Check(tradeCtrl.TapAt(mc, UiOf(mc, user, new MPoint(11, 10))), "case21 throttled still consumes");
                Check(calls.Count == 1, "case21 throttled no resend");
                CMain.Time = 13000;
                tradeCtrl.TapAt(mc, UiOf(mc, user, new MPoint(11, 10)));
                Check(calls.Count == 2, "case21 resend after cooldown");
            }

            // ===== case22 CloseButton → Hide 双方 + C.TradeCancel =====
            {
                var scene = NewScene();
                var dlg = scene.TradeDialog;
                dlg.Show();
                scene.GuestTradeDialog.Show();
                DrainPackets();
                dlg.CloseButton.InvokeMouseClick(EventArgs.Empty);
                Check(!dlg.Visible && !scene.GuestTradeDialog.Visible, "case22 both hidden");
                Check(Last(p => p as C.TradeCancel) != null, "case22 cancel sent");
            }

            // 还原全局 seam + 静态委托（防污染后续探针）。
            MobileTrade.SendTradeRequest = () => Network.Enqueue(new C.TradeRequest());
            GameScene.SelectedCell = null;
            GameScene.Gold = 0;
            GuestTradeDialog.GuestItems = new UserItem[10];
            GameScene.ItemInfoList.Clear();
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.User = null;
            DrainPackets();

            if (_fail == 0)
            {
                Console.WriteLine("[tradeverify] PASS cases=22");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[tradeverify] FAIL cases=22 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
