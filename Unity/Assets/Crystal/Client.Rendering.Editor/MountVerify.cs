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
    // 阶段8 第8项增量2 坐骑触控纯逻辑验证（无服务器）：
    // MountDialog 常驻隐藏；S.MountUpdate 本机（MountType/RidingMount 同步 + RefreshDialog）/
    // 他人（MapControl.Objects 分发 PlayerObject.MountUpdate）/MountType<0 隐藏面板；
    // MountDialog.Show 无坐骑弹 NoMount 不打开、有坐骑打开；MountButton→CanRide 三守卫
    // （MountType>=0/500ms 节流/站立动作）→ Ride 发 @ride；CloseButton Hide；MobileBag 坐骑
    // 按钮（左缘英雄下方）被 UiConsumer 消费开关面板 + 无坐骑弹 NoMount 不打开 + 不喂摇杆。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.MountVerify.Run -quit
    // 断言：全过输出 [mountverify] PASS exit 0。
    public static class MountVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[mountverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam + 建空场景 + MainDialog + ChatDialog + 背包 + BuffsDialog +
        // MountDialog 常驻（默认隐藏）。User.MountType 默认 -1（无坐骑）。
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

            var user = new UserObject(1) { Name = "probe", Level = 30, Class = MirClass.Warrior };
            user.Inventory = new UserItem[56];
            GameScene.User = user;
            MapObject.User = user;
            MapControl.User = user;
            MapControl.Objects[user.ObjectID] = user; // 真实加载流程 AddObject(this) 语义（MountUpdate 分发可达）

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

            var mount = new MountDialog { Parent = scene, Visible = false };
            scene.MountDialog = mount;
            return scene;
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
            CMain.Time = 10000; // 基准时钟（CanRide 的 MountTime+500 节流窗判断）

            // ===== case1 常驻创建默认隐藏 =====
            {
                var scene = NewScene();
                Check(scene.MountDialog != null && !scene.MountDialog.Visible, "case1 resident hidden");
            }

            // ===== case2 S.MountUpdate 本机 → MountType/RidingMount 同步 + RefreshDialog =====
            // 注：MountUpdate 派发含 RefreshStats → RefreshEquipmentStats 按 Mount 槽装备重算
            // MountType（逐字：先 -1 再 =坐骑 Shape）→ 夹具须装备坐骑物品（真实玩家同语义）。
            {
                var scene = NewScene();
                var mountInfo = new ItemInfo { Index = 900, Name = "horse", Type = ItemType.Mount, Shape = 5, Weight = 1, Image = 1, Durability = 0, Price = 10, StackSize = 1, Stats = new Stats() };
                GameScene.ItemInfoList.Add(mountInfo);
                MapObject.User.Equipment[(int)EquipmentSlot.Mount] = new UserItem(mountInfo) { UniqueID = 9001, Count = 1 };
                GameSession.MountUpdate(new S.MountUpdate { ObjectID = 1, MountType = 5, RidingMount = true });
                Check(MapObject.User.MountType == 5, "case2 mounttype=5");
                Check(MapObject.User.RidingMount, "case2 riding=true");
            }

            // ===== case3 S.MountUpdate 他人对象 → MapControl.Objects 分发 =====
            {
                var scene = NewScene();
                var other = new PlayerObject(200) { Name = "other" };
                MapControl.Objects[200] = other;
                GameSession.MountUpdate(new S.MountUpdate { ObjectID = 200, MountType = 3, RidingMount = false });
                Check(other.MountType == 3, "case3 other mounttype=3");
                Check(!other.RidingMount, "case3 other riding=false");
            }

            // ===== case4 S.MountUpdate MountType<0 → 面板隐藏 =====
            {
                var scene = NewScene();
                var mount = scene.MountDialog;
                MapObject.User.MountType = 5;
                mount.Show(); // 有坐骑 → 打开
                Check(mount.Visible, "case4 shown first");
                GameSession.MountUpdate(new S.MountUpdate { ObjectID = 1, MountType = -1, RidingMount = false });
                Check(!mount.Visible, "case4 hidden on unmount");
            }

            // ===== case5 MountDialog.Show 无坐骑 → NoMount 弹框 + 不打开 =====
            {
                var scene = NewScene();
                var mount = scene.MountDialog;
                MapObject.User.MountType = -1;
                mount.Show();
                Check(!mount.Visible, "case5 not shown");
                Check(FindModal() as MirMessageBox != null, "case5 nomount prompt shown");
            }

            // ===== case6 MountDialog.Show 有坐骑 → 打开 =====
            {
                var scene = NewScene();
                var mount = scene.MountDialog;
                MapObject.User.MountType = 5;
                mount.Show();
                Check(mount.Visible, "case6 shown with mount");
            }

            // ===== case7 MountButton → CanRide 通过 → Ride 发 @ride =====
            {
                var scene = NewScene();
                MapObject.User.MountType = 5;
                MapObject.User.CurrentAction = MirAction.Standing;
                MapObject.User.MountTime = 0;
                DrainPackets();
                scene.MountDialog.MountButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var chat = Last<C.Chat>(p => p as C.Chat);
                Check(chat != null && chat.Message == "@ride", "case7 ride sent @ride");
            }

            // ===== case8 CanRide 节流拒绝（MountTime+500 > Time）=====
            {
                var scene = NewScene();
                MapObject.User.MountType = 5;
                MapObject.User.CurrentAction = MirAction.Standing;
                MapObject.User.MountTime = CMain.Time + 1000; // 未来节流窗
                DrainPackets();
                scene.MountDialog.MountButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(Last<C.Chat>(p => p as C.Chat) == null, "case8 throttle blocks ride");
            }

            // ===== case9 CanRide 非站立拒绝 =====
            {
                var scene = NewScene();
                MapObject.User.MountType = 5;
                MapObject.User.CurrentAction = MirAction.Walking;
                MapObject.User.MountTime = 0;
                DrainPackets();
                scene.MountDialog.MountButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(Last<C.Chat>(p => p as C.Chat) == null, "case9 walking blocks ride");
            }

            // ===== case10 CloseButton Hide =====
            {
                var scene = NewScene();
                var mount = scene.MountDialog;
                MapObject.User.MountType = 5;
                mount.Show();
                mount.CloseButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(!mount.Visible, "case10 closed");
            }

            // ===== case11 RouteTouch 集成：坐骑按钮（左缘英雄下方）消费开关面板 + 不喂摇杆 =====
            {
                var scene = NewScene();
                MapObject.User.MountType = 5;
                var mountBtn = new MobileBag(1280, 720) { LeftAnchored = true };
                mountBtn.SetMargin(new UnityEngine.Vector2(90f, 100f + MobileBag.ButtonH + 8f));
                mountBtn.OnToggle = open => { if (open) scene.MountDialog.Show(); else scene.MountDialog.Hide(); };
                bool joystickFired = false;
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => mountBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                };
                var r = mountBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                DrainPackets();
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(scene.MountDialog.Visible, "case11 opened by tap");
                Check(!joystickFired, "case11 joystick not fed");
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!scene.MountDialog.Visible, "case11 closed by tap");
                Check(!joystickFired, "case11 joystick not fed on close");
            }

            // ===== case12 无坐骑 ToggleMount → NoMount 弹框 + 面板不打开 =====
            {
                var scene = NewScene();
                MapObject.User.MountType = -1;
                var mountBtn = new MobileBag(1280, 720) { LeftAnchored = true };
                mountBtn.SetMargin(new UnityEngine.Vector2(90f, 100f + MobileBag.ButtonH + 8f));
                mountBtn.OnToggle = open => { if (open) scene.MountDialog.Show(); else scene.MountDialog.Hide(); };
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => mountBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => { },
                    Hud = (id, ph, ui) => { },
                };
                var r = mountBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                DrainPackets();
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!scene.MountDialog.Visible, "case12 no mount no panel");
                Check(FindModal() as MirMessageBox != null, "case12 nomount prompt");
            }

            Console.WriteLine(_fail == 0 ? "[mountverify] PASS cases=12" : $"[mountverify] FAIL cases={_fail}");
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }
    }
}
