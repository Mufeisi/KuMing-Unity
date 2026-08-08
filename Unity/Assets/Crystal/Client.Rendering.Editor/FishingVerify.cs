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
    // 阶段8 第8项增量3 钓鱼触控纯逻辑验证（无服务器）：
    // FishingDialog/FishingStatusDialog 常驻隐藏；S.FishingUpdate 本机（Progress/Chance 同步 +
    // ChanceLabel 文本 + Fishing 显隐状态条）/他人（MapControl.Objects 分发 PlayerObject.
    // FishingUpdate 无竿隐藏渔具窗）；FishingDialog.Show 无鱼竿弹 NoFishingRod 不打开、有鱼竿
    // 打开；FishButton→C.FishingCast{CastOut=false}；AutoCastButton→C.FishingChangeAutocast 往返；
    // FishingStatusDialog.Cancel→C.FishingCast+Hide；CloseButton Hide；MobileBag 钓鱼按钮
    // （左缘坐骑下方 y=224）被 UiConsumer 消费开关 + 无鱼竿弹 NoFishingRod 不打开 + 不喂摇杆。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.FishingVerify.Run -quit
    // 断言：全过输出 [fishingverify] PASS exit 0。
    public static class FishingVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[fishingverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam + 建空场景 + MainDialog + ChatDialog + 背包 + BuffsDialog +
        // 钓鱼两窗常驻（默认隐藏）。HasFishingRod 依赖 Globals.FishingRodShapes（{49,50}）+ Weapon。
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
            MapControl.Objects[user.ObjectID] = user; // 真实加载流程 AddObject(this) 语义（FishingUpdate 分发可达）

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

            var fishing = new FishingDialog { Parent = scene, Visible = false };
            scene.FishingDialog = fishing;
            var status = new FishingStatusDialog { Parent = scene, Visible = false };
            scene.FishingStatusDialog = status;
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
            // ===== case1 常驻创建默认隐藏 =====
            {
                var scene = NewScene();
                Check(scene.FishingDialog != null && !scene.FishingDialog.Visible, "case1 fishing resident hidden");
                Check(scene.FishingStatusDialog != null && !scene.FishingStatusDialog.Visible, "case1 status resident hidden");
            }

            // ===== case2 S.FishingUpdate 本机 Fishing=true → 状态条 Show + 进度/几率同步 =====
            {
                var scene = NewScene();
                GameSession.FishingUpdate(new S.FishingUpdate { ObjectID = 1, Fishing = true, ProgressPercent = 40, ChancePercent = 50, FishingPoint = new System.Drawing.Point(0, 0), FoundFish = false });
                var status = scene.FishingStatusDialog;
                Check(status.Visible, "case2 status shown");
                Check(status.ProgressPercent == 40, "case2 progress=40");
                Check(status.ChancePercent == 50, "case2 chance=50");
                Check(status.ChanceLabel.Text == "50%", "case2 chance label 50%");
            }

            // ===== case3 S.FishingUpdate 本机 Fishing=false → 状态条隐藏 =====
            {
                var scene = NewScene();
                GameSession.FishingUpdate(new S.FishingUpdate { ObjectID = 1, Fishing = true, ProgressPercent = 40, ChancePercent = 50, FishingPoint = new System.Drawing.Point(0, 0), FoundFish = false });
                GameSession.FishingUpdate(new S.FishingUpdate { ObjectID = 1, Fishing = false, ProgressPercent = 0, ChancePercent = 0, FishingPoint = new System.Drawing.Point(0, 0), FoundFish = false });
                Check(!scene.FishingStatusDialog.Visible, "case3 status hidden");
            }

            // ===== case4 S.FishingUpdate 他人对象 → MapControl.Objects 分发 =====
            {
                var scene = NewScene();
                var other = new PlayerObject(200) { Name = "other" };
                MapControl.Objects[200] = other;
                other.Fishing = false;
                GameSession.FishingUpdate(new S.FishingUpdate { ObjectID = 200, Fishing = true, ProgressPercent = 0, ChancePercent = 0, FishingPoint = new System.Drawing.Point(0, 0), FoundFish = false });
                Check(other.Fishing, "case4 other fishing=true");
                Check(!scene.FishingStatusDialog.Visible, "case4 status untouched (not local)");
            }

            // ===== case5 FishingDialog.Show 无鱼竿 → NoFishingRod 弹框 + 不打开 =====
            {
                var scene = NewScene();
                var fishing = scene.FishingDialog;
                MapObject.User.Weapon = 1; // 非鱼竿 Shape
                fishing.Show();
                Check(!fishing.Visible, "case5 not shown");
                Check(FindModal() as MirMessageBox != null, "case5 norod prompt shown");
            }

            // ===== case6 FishingDialog.Show 有鱼竿 → 打开 =====
            {
                var scene = NewScene();
                var fishing = scene.FishingDialog;
                MapObject.User.Weapon = 49; // Globals.FishingRodShapes 含 49
                fishing.Show();
                Check(fishing.Visible, "case6 shown with rod");
            }

            // ===== case7 FishButton → C.FishingCast{CastOut=false} =====
            {
                var scene = NewScene();
                var status = scene.FishingStatusDialog;
                status.Show();
                DrainPackets();
                status.FishButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var cast = Last<C.FishingCast>(p => p as C.FishingCast);
                Check(cast != null && !cast.CastOut, "case7 cast sent");
            }

            // ===== case8 AutoCastButton → C.FishingChangeAutocast 往返 =====
            // _canAutoCast 由 BeforeDraw 按鱼竿+卷线轮（Slots[Reel]）装备驱动 → 探针装备后
            // 手动 Draw() 触发（batchmode 无渲染循环，ChanceBar_BeforeDraw 空库早退安全）。
            {
                var scene = NewScene();
                var status = scene.FishingStatusDialog;
                var rodInfo = new ItemInfo { Index = 901, Name = "rod", Type = ItemType.Weapon, Shape = 49, Weight = 1, Image = 1, Durability = 0, Price = 10, StackSize = 1, Stats = new Stats() };
                GameScene.ItemInfoList.Add(rodInfo);
                var rod = new UserItem(rodInfo) { UniqueID = 9101, Count = 1 };
                rod.Slots = new UserItem[5];
                rod.Slots[(int)FishingSlot.Reel] = new UserItem(rodInfo) { UniqueID = 9102, Count = 1 };
                MapObject.User.Weapon = 49;
                MapObject.User.Equipment[(int)EquipmentSlot.Weapon] = rod;
                status.Show();
                status.Draw(); // 触发 BeforeDraw → _canAutoCast=true
                DrainPackets();
                status.AutoCastButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var ac = Last<C.FishingChangeAutocast>(p => p as C.FishingChangeAutocast);
                Check(ac != null && ac.AutoCast, "case8 autocast on");
                DrainPackets();
                status.AutoCastButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                ac = Last<C.FishingChangeAutocast>(p => p as C.FishingChangeAutocast);
                Check(ac != null && !ac.AutoCast, "case8 autocast off");
            }

            // ===== case9 FishingDialog.CloseButton → Hide =====
            {
                var scene = NewScene();
                var fishing = scene.FishingDialog;
                MapObject.User.Weapon = 49;
                fishing.Show();
                fishing.CloseButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(!fishing.Visible, "case9 fishing closed");
            }

            // ===== case10 FishingStatusDialog.Cancel → C.FishingCast + Hide =====
            {
                var scene = NewScene();
                var status = scene.FishingStatusDialog;
                status.Show();
                DrainPackets();
                status.Cancel();
                var cast = Last<C.FishingCast>(p => p as C.FishingCast);
                Check(cast != null && !cast.CastOut, "case10 cancel cast sent");
                Check(!status.Visible, "case10 status hidden");
            }

            // ===== case11 RouteTouch 集成：钓鱼按钮（左缘坐骑下方 y=224）消费开关 + 不喂摇杆 =====
            {
                var scene = NewScene();
                MapObject.User.Weapon = 49;
                var fishingBtn = new MobileBag(1280, 720) { LeftAnchored = true };
                fishingBtn.SetMargin(new UnityEngine.Vector2(90f, 100f + (MobileBag.ButtonH + 8f) * 2));
                fishingBtn.OnToggle = open => { if (open) scene.FishingDialog.Show(); else scene.FishingDialog.Hide(); };
                bool joystickFired = false;
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => fishingBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                };
                var r = fishingBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                DrainPackets();
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(scene.FishingDialog.Visible, "case11 opened by tap");
                Check(!joystickFired, "case11 joystick not fed");
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!scene.FishingDialog.Visible, "case11 closed by tap");
                Check(!joystickFired, "case11 joystick not fed on close");
            }

            // ===== case12 无鱼竿 ToggleFishing → NoFishingRod 弹框 + 面板不打开 =====
            {
                var scene = NewScene();
                MapObject.User.Weapon = 1;
                var fishingBtn = new MobileBag(1280, 720) { LeftAnchored = true };
                fishingBtn.SetMargin(new UnityEngine.Vector2(90f, 100f + (MobileBag.ButtonH + 8f) * 2));
                fishingBtn.OnToggle = open => { if (open) scene.FishingDialog.Show(); else scene.FishingDialog.Hide(); };
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => fishingBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => { },
                    Hud = (id, ph, ui) => { },
                };
                var r = fishingBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                DrainPackets();
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!scene.FishingDialog.Visible, "case12 no rod no panel");
                Check(FindModal() as MirMessageBox != null, "case12 norod prompt");
            }

            // ===== case13 FishingStatusDialog.CloseButton → Hide =====
            {
                var scene = NewScene();
                var status = scene.FishingStatusDialog;
                status.Show();
                status.CloseButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(!status.Visible, "case13 status closed");
            }

            // ===== case14 S.FishingUpdate 无竿他人 → FishingDialog.Hide 不炸（null 守卫）=====
            {
                var scene = NewScene();
                var other = new PlayerObject(300) { Name = "other2" };
                MapControl.Objects[300] = other;
                GameSession.FishingUpdate(new S.FishingUpdate { ObjectID = 300, Fishing = false, ProgressPercent = 0, ChancePercent = 0, FishingPoint = new System.Drawing.Point(0, 0), FoundFish = false });
                Check(true, "case14 no-rod remote dispatch safe");
            }

            Console.WriteLine(_fail == 0 ? "[fishingverify] PASS cases=14" : $"[fishingverify] FAIL cases={_fail}");
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }
    }
}
