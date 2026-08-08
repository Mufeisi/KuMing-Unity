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
    // 阶段8 第7项增量2 邮件流程触控纯逻辑验证（无服务器）：
    // 五窗（MailList/ComposeLetter/ComposeParcel/ReadLetter/ReadParcel）常驻隐藏；S.ReceiveMail 全量表
    // 回流 → 排序（未锁在前再 DateSent 倒序，旧客户端逐字）+ 逐封 Bind + 未读置 NewMail + 列表刷新行；
    // S.MailLockedItem 回声中视锁（GetCell）；S.MailSendRequest 弹 MirInputBox 输收件人 → ComposeMail 开
    // 寄包裹窗 + 开背包；S.MailSent 解锁背包全格 + 关寄包裹窗；S.ParcelCollected 三分支（-1/0 弹
    // MirMessageBox、1 关读包裹窗）；S.MailCost 邮资刷 ParcelCostLabel；MirItemCell 两段式（选中背包源格
    // →点空邮件格）放入 Items 槽 + 源格锁 + ItemsIdx + CalculatePostage；禁邮物品（DontTrade）拦截提示；
    // MailListDialog.SendButton → MirInputBox → 开写信窗；GoldSendLabel 点弹 MirAmountBox → 金累加/扣减；
    // 关窗 Reset 退款 + 逐格 C.MailLockedItem{Locked=false}；MobileBag 邮件按钮（第8枚）被 UiConsumer
    // 消费开关邮件列表 + 互斥关背包。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.MailVerify.Run -quit
    // 断言：全过输出 [mailverify] PASS exit 0。
    public static class MailVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[mailverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Scene/User/SelectedCell/Gold/Objects）+ 建空场景 + MainDialog
        // （ChatDialog ctor 读其 Location）+ ChatDialog（DontTrade 拦截提示 ReceiveChat）+ 背包
        // （显式尺寸：MailComposeParcelDialog ctor 读 InventoryDialog.Size.Width）+ 邮件五窗（常驻隐藏）。
        // 邮件数组为 static（跨 case 残留）→ 每 scene 重置。
        static GameScene NewScene()
        {
            GameScene.Scene = null;
            GameScene.User = null;
            GameScene.SelectedCell = null;
            GameScene.Gold = 0;
            GameScene.PickedUpGold = false;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            MobileUiAdapter.DialogRoot = () => GameScene.Scene;
            MailComposeParcelDialog.Items = new UserItem[5];
            MailComposeParcelDialog.ItemsIdx = new ulong[5];

            var user = new UserObject(1) { Name = "probe", Level = 30 };
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

            // 邮件五窗（顺序契约：MailComposeParcelDialog ctor 读 InventoryDialog.Size.Width）。
            var mailList = new MailListDialog { Parent = scene, Visible = false };
            scene.MailListDialog = mailList;
            var composeLetter = new MailComposeLetterDialog { Parent = scene, Visible = false };
            scene.MailComposeLetterDialog = composeLetter;
            var composeParcel = new MailComposeParcelDialog { Parent = scene, Visible = false };
            scene.MailComposeParcelDialog = composeParcel;
            var readLetter = new MailReadLetterDialog { Parent = scene, Visible = false };
            scene.MailReadLetterDialog = readLetter;
            var readParcel = new MailReadParcelDialog { Parent = scene, Visible = false };
            scene.MailReadParcelDialog = readParcel;
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

        // 邮件构造（字段直填，对齐 ClientMail 无参 ctor）。
        static ClientMail Mail(ulong id, string sender, DateTime sent, bool locked = false, bool opened = false, uint gold = 0)
        {
            return new ClientMail
            {
                MailID = id,
                SenderName = sender,
                Message = "hi",
                Opened = opened,
                Locked = locked,
                CanReply = true,
                DateSent = sent,
                Gold = gold,
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

        // 格点击（邮件两段式）：直接调 MirItemCell.OnMouseClick（选中/两段式逻辑在 override 内）。
        static void ClickCell(MirItemCell cell)
        {
            cell.OnMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;
            CMain.Time = 10000;

            // ===== case1 常驻创建：邮件五窗挂 scene 默认隐藏 =====
            {
                var scene = NewScene();
                Check(scene.MailListDialog != null && scene.MailComposeLetterDialog != null
                    && scene.MailComposeParcelDialog != null && scene.MailReadLetterDialog != null
                    && scene.MailReadParcelDialog != null, "case1 dialogs attached");
                Check(!scene.MailListDialog.Visible && !scene.MailComposeLetterDialog.Visible
                    && !scene.MailComposeParcelDialog.Visible && !scene.MailReadLetterDialog.Visible
                    && !scene.MailReadParcelDialog.Visible, "case1 hidden by default");
            }

            // ===== case2 S.ReceiveMail 全量表：排序（未锁前再 DateSent 倒序）+ Bind + NewMail + 列表刷新行 =====
            {
                var scene = NewScene();
                var p = new S.ReceiveMail();
                p.Mail.Add(Mail(1, "old", new DateTime(2026, 1, 1, 10, 0, 0), locked: false, opened: true));
                p.Mail.Add(Mail(2, "locked", new DateTime(2026, 1, 3, 10, 0, 0), locked: true));
                p.Mail.Add(Mail(3, "new", new DateTime(2026, 1, 2, 10, 0, 0)));
                GameSession.ReceiveMail(p);
                Check(MapObject.User.Mail.Count == 3, "case2 mail count");
                Check(MapObject.User.Mail[0].SenderName == "new", "case2 unlocked newest first");
                Check(MapObject.User.Mail[1].SenderName == "old", "case2 unlocked older second");
                Check(MapObject.User.Mail[2].SenderName == "locked", "case2 locked last");
                Check(scene.NewMail, "case2 new-mail marker set (unopened present)");
                Check(scene.MailListDialog.Rows[0] != null && scene.MailListDialog.Rows[0].Mail.SenderName == "new", "case2 list rows refreshed");
            }

            // ===== case3 S.ReceiveMail 全已读 → NewMail 清除 =====
            {
                var scene = NewScene();
                var p = new S.ReceiveMail();
                p.Mail.Add(Mail(4, "read", new DateTime(2026, 1, 1), opened: true));
                GameSession.ReceiveMail(p);
                Check(!scene.NewMail, "case3 no new-mail marker when all read");
            }

            // ===== case4 S.MailLockedItem 回声：GetCell 视锁 =====
            {
                var scene = NewScene();
                var sword = MakeItem(500, 9001);
                MapObject.User.Inventory[6] = sword;
                GameSession.MailLockedItem(new S.MailLockedItem { UniqueID = 9001, Locked = true });
                Check(scene.InventoryDialog.GetCell(9001) != null && scene.InventoryDialog.GetCell(9001).Locked, "case4 cell locked");
                GameSession.MailLockedItem(new S.MailLockedItem { UniqueID = 9001, Locked = false });
                Check(!scene.InventoryDialog.GetCell(9001).Locked, "case4 cell unlocked");
            }

            // ===== case5 S.MailSendRequest → MirInputBox 输收件人 → OK 开寄包裹窗 + 开背包 =====
            {
                var scene = NewScene();
                GameSession.MailSendRequest();
                var box = FindModal() as MirInputBox;
                Check(box != null && box.Modal, "case5 recipient input shown");
                if (box == null) { /* fail 已记 */ }
                else
                {
                    box.InputTextBox.Text = "Alice";
                    box.OKButton.InvokeMouseClick(EventArgs.Empty);
                    Check(scene.MailComposeParcelDialog.Visible, "case5 compose parcel opened");
                    Check(scene.InventoryDialog.Visible, "case5 inventory shown");
                    Check(FindModal() == null, "case5 input dismissed");
                }
            }

            // ===== case6 S.MailSent 发送成功：解锁背包全格 + 关寄包裹窗 =====
            {
                var scene = NewScene();
                scene.InventoryDialog.Grid[0].Locked = true;
                scene.InventoryDialog.Grid[1].Locked = true;
                scene.MailComposeParcelDialog.ComposeMail("Alice");
                Check(scene.MailComposeParcelDialog.Visible, "case6 parcel visible before");
                GameSession.MailSent(new S.MailSent { Result = 0 });
                Check(!scene.InventoryDialog.Grid[0].Locked && !scene.InventoryDialog.Grid[1].Locked, "case6 inventory unlocked");
                Check(!scene.MailComposeParcelDialog.Visible, "case6 parcel hidden");
            }

            // ===== case7 S.ParcelCollected -1 → MirMessageBox 无可领包裹 =====
            {
                var scene = NewScene();
                GameSession.ParcelCollected(new S.ParcelCollected { Result = -1 });
                var box = FindModal() as MirMessageBox;
                Check(box != null && box.Modal, "case7 no-parcels prompt shown");
            }

            // ===== case8 S.ParcelCollected 0 → MirMessageBox 全部已领 =====
            {
                var scene = NewScene();
                GameSession.ParcelCollected(new S.ParcelCollected { Result = 0 });
                Check(FindModal() as MirMessageBox != null, "case8 all-collected prompt shown");
            }

            // ===== case9 S.ParcelCollected 1 → 关读包裹窗 =====
            {
                var scene = NewScene();
                scene.MailReadParcelDialog.ReadMail(Mail(5, "sender", new DateTime(2026, 1, 1), opened: true, gold: 100));
                Check(scene.MailReadParcelDialog.Visible, "case9 read parcel visible before");
                GameSession.ParcelCollected(new S.ParcelCollected { Result = 1 });
                Check(!scene.MailReadParcelDialog.Visible, "case9 read parcel hidden");
            }

            // ===== case10 S.MailCost 邮资回流：寄包裹窗可见时刷 ParcelCostLabel =====
            {
                var scene = NewScene();
                scene.MailComposeParcelDialog.Visible = true;
                GameSession.MailCost(new S.MailCost { Cost = 123 });
                Check(scene.MailComposeParcelDialog.ParcelCostLabel.Text == "123", "case10 postage label refreshed");
            }

            // ===== case11 两段式放入：选中背包源格 → 点空邮件格 → Items/ItemsIdx + 源格锁 + 清选中 + 刷邮资 =====
            {
                var scene = NewScene();
                var sword = MakeItem(501, 9002);
                MapObject.User.Inventory[6] = sword;
                DrainPackets();
                ClickCell(scene.InventoryDialog.Grid[0]); // Item=sword → 选中源格
                Check(GameScene.SelectedCell == scene.InventoryDialog.Grid[0], "case11 bag cell selected");
                ClickCell(scene.MailComposeParcelDialog.Cells[0]); // 空邮件格 → 两段式放入
                Check(MailComposeParcelDialog.Items[0] == sword, "case11 mail slot set");
                Check(MailComposeParcelDialog.ItemsIdx[0] == sword.UniqueID, "case11 items idx set");
                Check(scene.InventoryDialog.Grid[0].Locked, "case11 bag cell locked");
                Check(GameScene.SelectedCell == null, "case11 selection cleared");
                Check(Last(p => p as C.MailCost) != null, "case11 postage sent");
            }

            // ===== case12 禁邮物品（DontTrade）：拦截提示 + 不移物 + 不发邮资 =====
            {
                var scene = NewScene();
                var info = InfoOf(502, "banned");
                info.Bind = BindMode.DontTrade;
                GameScene.ItemInfoList.Add(info);
                var banned = new UserItem(info) { UniqueID = 9003, Count = 1 };
                MapObject.User.Inventory[6] = banned;
                DrainPackets();
                ClickCell(scene.InventoryDialog.Grid[0]);
                ClickCell(scene.MailComposeParcelDialog.Cells[0]);
                Check(MailComposeParcelDialog.Items[0] == null, "case12 no move");
                Check(MailComposeParcelDialog.ItemsIdx[0] == 0, "case12 idx clear");
                Check(Last(p => p as C.MailCost) == null, "case12 no postage");
                Check(GameScene.SelectedCell == scene.InventoryDialog.Grid[0], "case12 selection retained (old client)");
            }

            // ===== case13 MailListDialog.SendButton → MirInputBox → OK 开写信窗 =====
            {
                var scene = NewScene();
                DrainPackets();
                scene.MailListDialog.SendButton.InvokeMouseClick(EventArgs.Empty);
                var box = FindModal() as MirInputBox;
                Check(box != null && box.Modal, "case13 send input shown");
                if (box == null) { /* fail 已记 */ }
                else
                {
                    box.InputTextBox.Text = "Bob";
                    box.OKButton.InvokeMouseClick(EventArgs.Empty);
                    Check(scene.MailComposeLetterDialog.Visible, "case13 compose letter opened");
                    Check(FindModal() == null, "case13 input dismissed");
                }
            }

            // ===== case14 GoldSendLabel → MirAmountBox：金额累加 + 金币扣减 + 标签刷新 + 刷邮资 =====
            {
                var scene = NewScene();
                var compP = scene.MailComposeParcelDialog;
                GameScene.Gold = 5000;
                GameScene.SelectedCell = null;
                DrainPackets();
                compP.GoldSendLabel.InvokeMouseClick(EventArgs.Empty);
                var box = FindModal() as MirAmountBox;
                Check(box != null, "case14 amount box shown");
                if (box == null) { /* fail 已记 */ }
                else
                {
                    Check(box.MaxAmount == 5000, "case14 max is gold");
                    box.InputTextBox.Text = "123";
                    box.OKButton.InvokeMouseClick(EventArgs.Empty);
                    Check(compP.GiftGoldAmount == 123, "case14 gift gold accumulated");
                    Check(GameScene.Gold == 4877, "case14 gold deducted");
                    Check(compP.GoldSendLabel.Text == "123", "case14 gold label refreshed");
                    Check(Last(p => p as C.MailCost) != null, "case14 postage sent");
                }
            }

            // ===== case15 关窗 Reset：退款 + 清槽 + 逐格 C.MailLockedItem{Locked=false} =====
            {
                var scene = NewScene();
                var compP = scene.MailComposeParcelDialog;
                var sword = MakeItem(503, 9004);
                compP.Cells[0].Item = sword;
                scene.InventoryDialog.Grid[0].Locked = true;
                compP.GiftGoldAmount = 100;
                compP.Visible = true;
                DrainPackets();
                compP.Hide();
                Check(compP.GiftGoldAmount == 0, "case15 gift gold reset");
                Check(GameScene.Gold == 100, "case15 gold refunded");
                Check(MailComposeParcelDialog.Items[0] == null, "case15 slot cleared");
                Check(MailComposeParcelDialog.ItemsIdx[0] == 0, "case15 idx cleared");
                var unlock = Last(p => p as C.MailLockedItem);
                Check(unlock != null && !unlock.Locked && unlock.UniqueID == sword.UniqueID, "case15 unlock sent");
                Check(!compP.Visible, "case15 parcel hidden");
            }

            // ===== case16 RouteTouch 集成：邮件按钮被 UiConsumer 消费 → 开列表 + 互斥关背包 =====
            {
                var scene = NewScene();
                var mail = scene.MailListDialog;
                var inv = scene.InventoryDialog;
                inv.Show(); // 预开背包验证互斥
                var mailBtn = new MobileBag(1280, 720);
                mailBtn.SetMargin(new UnityEngine.Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 7));
                mailBtn.OnToggle = open => { if (open) { inv.Hide(); mail.Show(); } else mail.Hide(); };
                bool joystickFired = false;
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => mailBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                };
                var r = mailBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(mail.Visible, "case16 route opens mail panel");
                Check(!joystickFired, "case16 mail tap consumes joystick");
                Check(!inv.Visible, "case16 mail open mutex hides inventory");
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!mail.Visible, "case16 route closes mail panel");
            }

            // 还原全局 seam（防污染后续探针）。
            MobileUiAdapter.DialogRoot = null;
            GameScene.SelectedCell = null;
            GameScene.Gold = 0;
            MailComposeParcelDialog.Items = new UserItem[5];
            MailComposeParcelDialog.ItemsIdx = new ulong[5];
            GameScene.ItemInfoList.Clear();
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            DrainPackets();

            if (_fail == 0)
            {
                Console.WriteLine("[mailverify] PASS cases=16");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[mailverify] FAIL cases=16 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
