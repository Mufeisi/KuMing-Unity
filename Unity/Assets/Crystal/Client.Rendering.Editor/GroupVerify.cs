using System;
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
using MPoint = Crystal.Client.Core.MirMath.Point;
using SDPoint = System.Drawing.Point;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第6项 组队流程触控纯逻辑验证（无服务器）：
    // GroupDialog 常驻（挂 scene 默认隐藏）+ 组队按钮 wire 静态数据；Switch/Add/Del 按钮点击接回
    // C.SwitchGroup/C.AddMember/C.DelMember；Add/Del 弹 MirInputBox（Modal 挂 scene）输入成员名
    // OK 发包；MirInputBox Enter/Esc 键盘路由（软键盘桥提交 → OK / Back → Cancel 取消）；
    // S.SwitchGroup/DeleteGroup/DeleteMember/AddMember/GroupMembersMap/SendMemberLocation 分发
    // 刷新静态数据（AllowGroup/GroupList/GroupMembersMap）+ 大地图雷达（BigMapViewPort.PlayerLocations，
    // 封包 Point → MPoint）；S.GroupInvite 弹 MirMessageBox YesNo（Yes → C.GroupInvite{true}+开窗，
    // Esc → No 拒绝）；RouteTouch 集成（组队按钮被 UiConsumer 消费）。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.GroupVerify.Run -quit
    // 断言：全过输出 [groupverify] PASS exit 0。
    public static class GroupVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[groupverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Scene/User/Objects + GroupDialog/BigMapViewPort 静态数据）+ 建空场景
        // + MainDialog（ChatDialog ctor 读其 Location）+ ChatDialog（守卫消息 ReceiveChat）+ GroupDialog 常驻。
        static GameScene NewScene()
        {
            GameScene.Scene = null;
            GameScene.User = null;
            GameScene.NPCID = 0;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            GroupDialog.AllowGroup = false;
            GroupDialog.GroupList.Clear();
            GroupDialog.GroupMembersMap.Clear();
            BigMapViewPort.PlayerLocations.Clear();
            MobileUiAdapter.DialogRoot = () => GameScene.Scene;

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

            var group = new GroupDialog { Parent = scene, Visible = false };
            scene.GroupDialog = group;
            return scene;
        }

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

        static C.SwitchGroup LastSwitch() => Last(p => p as C.SwitchGroup);
        static C.AddMember LastAdd() => Last(p => p as C.AddMember);
        static C.DelMember LastDel() => Last(p => p as C.DelMember);
        static C.GroupInvite LastInvite() => Last(p => p as C.GroupInvite);

        static int CountAdd()
        {
            int n = 0;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.AddMember) n++;
            return n;
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

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;
            CMain.Time = 10000;

            // ===== case1 常驻创建：GroupDialog 挂 scene 默认隐藏 + 静态数据可读写 =====
            {
                var scene = NewScene();
                Check(scene.GroupDialog != null, "case1 group dialog attached");
                Check(!scene.GroupDialog.Visible, "case1 hidden by default");
                GroupDialog.GroupList.Add("probe");
                Check(GroupDialog.GroupList.Count == 1 && GroupDialog.GroupList[0] == "probe", "case1 static list rw");
            }

            // ===== case2 SwitchButton.Click → C.SwitchGroup{!AllowGroup}（开/关两拍，回声驱动）=====
            {
                var scene = NewScene();
                var g = scene.GroupDialog;
                DrainPackets();
                g.SwitchButton.InvokeMouseClick(EventArgs.Empty);
                var sw = LastSwitch();
                Check(sw != null && sw.AllowGroup, "case2 switch open");
                // 允许态由服务器回声 S.SwitchGroup 更新（见 case6）；回声后再点 → 取反关。
                GameSession.GroupSwitch(new S.SwitchGroup { AllowGroup = true });
                DrainPackets();
                g.SwitchButton.InvokeMouseClick(EventArgs.Empty);
                sw = LastSwitch();
                Check(sw != null && !sw.AllowGroup, "case2 switch close");
            }

            // ===== case3 AddButton.Click → MirInputBox 弹窗 + 输入成员名 OK → C.AddMember =====
            {
                var scene = NewScene();
                var g = scene.GroupDialog;
                DrainPackets();
                g.AddButton.InvokeMouseClick(EventArgs.Empty);
                var modal = FindModal();
                Check(modal is MirInputBox, "case3 input box modal shown");
                var box = modal as MirInputBox;
                Check(box != null && box.Modal, "case3 modal flag");
                if (box == null) { /* 断言已记 fail，跳过后续 */ }
                else
                {
                    box.InputTextBox.Text = "Alice";
                    box.OKButton.InvokeMouseClick(EventArgs.Empty);
                    var add = LastAdd();
                    Check(add != null && add.Name == "Alice", "case3 add member sent");
                    Check(FindModal() == null, "case3 input box dismissed");
                }
            }

            // ===== case4 DelButton.Click → MirInputBox → OK → C.DelMember =====
            {
                var scene = NewScene();
                var g = scene.GroupDialog;
                GroupDialog.GroupList.Add("probe"); // 队长即自身，DelMember 守卫通过
                DrainPackets();
                g.DelButton.InvokeMouseClick(EventArgs.Empty);
                var box = FindModal() as MirInputBox;
                Check(box != null, "case4 del input box shown");
                if (box == null) { /* 断言已记 fail */ }
                else
                {
                    box.InputTextBox.Text = "Bob";
                    box.OKButton.InvokeMouseClick(EventArgs.Empty);
                    var del = LastDel();
                    Check(del != null && del.Name == "Bob", "case4 del member sent");
                }
            }

            // ===== case5 AddMember(string) 直发 + 人数上限/队长守卫 =====
            {
                var scene = NewScene();
                var g = scene.GroupDialog;
                DrainPackets();
                g.AddMember("Carl");
                var add = LastAdd();
                Check(add != null && add.Name == "Carl", "case5 direct add sent");
                // 人数上限：15 人（MaxGroup）满队 → AddMember 不发包（守卫 GroupHasMaxMembers）
                DrainPackets();
                GroupDialog.GroupList.Clear();
                GroupDialog.GroupList.Add("probe"); // 队长
                for (int i = 1; i < Globals.MaxGroup; i++) GroupDialog.GroupList.Add("m" + i);
                g.AddMember("Over");
                Check(CountAdd() == 0, "case5 max-group guard");
                // 队长守卫：非队长（列表首位非己）→ AddMember 不发包（YouAreNotGroupLeader）
                DrainPackets();
                GroupDialog.GroupList.Clear();
                GroupDialog.GroupList.Add("other");
                g.AddMember("X");
                Check(CountAdd() == 0, "case5 not-leader guard");
            }

            // ===== case6 S.SwitchGroup → AllowGroup 同步 + 关队清列表 =====
            {
                var scene = NewScene();
                GroupDialog.GroupList.Add("probe");
                GameSession.GroupSwitch(new S.SwitchGroup { AllowGroup = true });
                Check(GroupDialog.AllowGroup, "case6 allow on");
                Check(GroupDialog.GroupList.Count == 1, "case6 list kept while open");
                GameSession.GroupSwitch(new S.SwitchGroup { AllowGroup = false });
                Check(!GroupDialog.AllowGroup, "case6 allow off");
                Check(GroupDialog.GroupList.Count == 0, "case6 close clears list");
            }

            // ===== case7 S.DeleteGroup → 清列表/字典/雷达 =====
            {
                var scene = NewScene();
                GroupDialog.GroupList.Add("probe");
                GroupDialog.GroupMembersMap["probe"] = "0";
                BigMapViewPort.PlayerLocations["probe"] = new MPoint(1, 1);
                GameSession.GroupDelete();
                Check(GroupDialog.GroupList.Count == 0, "case7 list cleared");
                Check(GroupDialog.GroupMembersMap.Count == 0, "case7 map cleared");
                Check(BigMapViewPort.PlayerLocations.Count == 0, "case7 radar cleared");
            }

            // ===== case8 S.DeleteMember → 列表/字典/雷达三处移除 =====
            {
                var scene = NewScene();
                GroupDialog.GroupList.Add("probe");
                GroupDialog.GroupList.Add("A");
                GroupDialog.GroupMembersMap["A"] = "1";
                BigMapViewPort.PlayerLocations["A"] = new MPoint(2, 2);
                GameSession.GroupDeleteMember(new S.DeleteMember { Name = "A" });
                Check(!GroupDialog.GroupList.Contains("A") && GroupDialog.GroupList.Contains("probe"), "case8 member removed");
                Check(!GroupDialog.GroupMembersMap.ContainsKey("A"), "case8 member map removed");
                Check(!BigMapViewPort.PlayerLocations.ContainsKey("A"), "case8 member radar removed");
            }

            // ===== case9 S.AddMember → 入列去重 =====
            {
                var scene = NewScene();
                GameSession.GroupAddMember(new S.AddMember { Name = "A" });
                GameSession.GroupAddMember(new S.AddMember { Name = "A" });
                Check(GroupDialog.GroupList.Count == 1 && GroupDialog.GroupList[0] == "A", "case9 add dedup");
            }

            // ===== case10 S.GroupMembersMap → upsert =====
            {
                var scene = NewScene();
                GameSession.GroupMembersMap(new S.GroupMembersMap { PlayerName = "A", PlayerMap = "0" });
                Check(GroupDialog.GroupMembersMap.ContainsKey("A") && GroupDialog.GroupMembersMap["A"] == "0", "case10 map insert");
                GameSession.GroupMembersMap(new S.GroupMembersMap { PlayerName = "A", PlayerMap = "1" });
                Check(GroupDialog.GroupMembersMap["A"] == "1", "case10 map update");
            }

            // ===== case11 S.SendMemberLocation → 雷达 upsert（封包 Point → MPoint）=====
            {
                var scene = NewScene();
                GameSession.GroupMemberLocation(new S.SendMemberLocation { MemberName = "A", MemberLocation = new SDPoint(5, 6) });
                Check(BigMapViewPort.PlayerLocations.ContainsKey("A") && BigMapViewPort.PlayerLocations["A"].X == 5 && BigMapViewPort.PlayerLocations["A"].Y == 6, "case11 radar insert");
                GameSession.GroupMemberLocation(new S.SendMemberLocation { MemberName = "A", MemberLocation = new SDPoint(7, 8) });
                var loc = BigMapViewPort.PlayerLocations["A"];
                Check(loc.X == 7 && loc.Y == 8, "case11 radar update");
            }

            // ===== case12 S.GroupInvite → MirMessageBox YesNo + Yes → C.GroupInvite{true} + 开窗 =====
            {
                var scene = NewScene();
                var g = scene.GroupDialog;
                DrainPackets();
                GameSession.GroupInvite(new S.GroupInvite { Name = "inviter" });
                var box = FindModal() as MirMessageBox;
                Check(box != null && box.Modal, "case12 invite box shown");
                if (box == null) { /* 断言已记 fail */ }
                else
                {
                    box.YesButton.InvokeMouseClick(EventArgs.Empty);
                    var inv = LastInvite();
                    Check(inv != null && inv.AcceptInvite, "case12 accept sent");
                    Check(g.Visible, "case12 group dialog shown on accept");
                }
            }

            // ===== case13 模态 Esc → No 拒绝（移动端 Back → Esc 语义）=====
            {
                var scene = NewScene();
                DrainPackets();
                GameSession.GroupInvite(new S.GroupInvite { Name = "inviter" });
                var modal = FindModal();
                Check(modal != null, "case13 invite box present");
                if (modal != null)
                {
                    modal.OnKeyPress(new KeyPressEventArgs((char)Keys.Escape));
                    var inv = LastInvite();
                    Check(inv != null && !inv.AcceptInvite, "case13 esc declines");
                    Check(FindModal() == null, "case13 box dismissed");
                }
            }

            // ===== case14 MirInputBox Esc → Cancel 取消（Back 语义）+ 无发包 =====
            {
                var scene = NewScene();
                var g = scene.GroupDialog;
                DrainPackets();
                g.AddButton.InvokeMouseClick(EventArgs.Empty);
                var modal = FindModal();
                Check(modal is MirInputBox, "case14 input box present");
                if (modal != null)
                {
                    modal.OnKeyPress(new KeyPressEventArgs((char)Keys.Escape));
                    Check(CountAdd() == 0, "case14 esc sends nothing");
                    Check(FindModal() == null, "case14 input box dismissed");
                }
            }

            // ===== case15 MirInputBox Enter → OK（软键盘桥提交链）=====
            {
                var scene = NewScene();
                var g = scene.GroupDialog;
                DrainPackets();
                g.AddButton.InvokeMouseClick(EventArgs.Empty);
                var box = FindModal() as MirInputBox;
                Check(box != null, "case15 input box present");
                if (box == null) { /* 断言已记 fail */ }
                else
                {
                    box.InputTextBox.Text = "Dora";
                    DrainPackets();
                    box.OnKeyPress(new KeyPressEventArgs((char)Keys.Enter));
                    var add = LastAdd();
                    Check(add != null && add.Name == "Dora", "case15 enter submits add");
                }
            }

            // ===== case16 RouteTouch 集成：组队按钮被 UiConsumer 消费 → OnToggle 开关面板 =====
            {
                var scene = NewScene();
                var g = scene.GroupDialog;
                var groupBtn = new MobileBag(1280, 720);
                groupBtn.SetMargin(new UnityEngine.Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 4));
                groupBtn.OnToggle = open => { if (open) g.Show(); else g.Hide(); };
                bool joystickFired = false;
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => groupBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                };
                var r = groupBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(g.Visible, "case16 route opens group panel");
                Check(!joystickFired, "case16 group tap consumes joystick");
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!g.Visible, "case16 route closes group panel");
            }

            // 还原全局 seam（防污染后续探针）。
            GroupDialog.AllowGroup = false;
            GroupDialog.GroupList.Clear();
            GroupDialog.GroupMembersMap.Clear();
            BigMapViewPort.PlayerLocations.Clear();
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            MobileUiAdapter.DialogRoot = null;
            DrainPackets();

            if (_fail == 0)
            {
                Console.WriteLine("[groupverify] PASS cases=16");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[groupverify] FAIL cases=16 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
