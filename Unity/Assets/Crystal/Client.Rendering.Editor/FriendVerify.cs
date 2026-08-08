using System;
using Client;
using Client.MirControls;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Client.MirScenes.Dialogs;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第6项 好友流程触控纯逻辑验证（无服务器）：
    // FriendDialog/MemoDialog 常驻（挂 scene 默认隐藏）+ 好友按钮 wire 静态数据；Show 发 C.RefreshFriends；
    // Add 弹 MirInputBox（好友/黑名单 tab 分流 Blocked）→ C.AddFriend；Remove 弹 MirMessageBox YesNo 确认 →
    // C.RemoveFriend{CharacterIndex}；MemoButton 开 MemoDialog（预填 Friend.Memo），OK → C.AddMemo；Whisper
    // 离线守卫 + WhisperAction seam（移动端接 MobileChat.OpenWhisper 弹软键盘）；S.FriendUpdate 整表回声填
    // Friends + 可见时重建行；黑名单 tab 过滤 + 12 行翻页（Next/Prev）；RouteTouch 集成（好友按钮被 UiConsumer 消费）。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.FriendVerify.Run -quit
    // 断言：全过输出 [friendverify] PASS exit 0。
    public static class FriendVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[friendverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Scene/User/Objects）+ 建空场景 + MainDialog（ChatDialog ctor 读其 Location）
        // + ChatDialog（守卫消息 ReceiveChat）+ FriendDialog/MemoDialog 常驻（隐藏）。
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

            // 备注浮窗先建（FriendDialog.Hide/MemoButton 引 scene.MemoDialog）。
            var memo = new MemoDialog { Parent = scene, Visible = false };
            scene.MemoDialog = memo;
            var friend = new FriendDialog { Parent = scene, Visible = false };
            scene.FriendDialog = friend;
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

        static C.AddFriend LastAddFriend() => Last(p => p as C.AddFriend);
        static C.RemoveFriend LastRemove() => Last(p => p as C.RemoveFriend);
        static C.AddMemo LastMemo() => Last(p => p as C.AddMemo);
        static C.RefreshFriends LastRefresh() => Last(p => p as C.RefreshFriends);

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

        // 填充好友列表并重建行（模拟服务器整表回声）：Show 后 GameSession.FriendUpdate。
        static void Feed(GameScene scene, params ClientFriend[] friends)
        {
            var p = new S.FriendUpdate();
            foreach (var f in friends) p.Friends.Add(f);
            GameSession.FriendUpdate(p);
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;
            CMain.Time = 10000;

            // ===== case1 常驻创建：FriendDialog/MemoDialog 挂 scene 默认隐藏 =====
            {
                var scene = NewScene();
                Check(scene.FriendDialog != null && scene.MemoDialog != null, "case1 dialogs attached");
                Check(!scene.FriendDialog.Visible && !scene.MemoDialog.Visible, "case1 hidden by default");
            }

            // ===== case2 Show → 可见 + 发 C.RefreshFriends 拉整表 =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                DrainPackets();
                f.Show();
                Check(f.Visible, "case2 shown");
                Check(LastRefresh() != null, "case2 refresh sent");
            }

            // ===== case3 好友 tab Add → MirInputBox 弹窗 + OK → C.AddFriend{Blocked=false} =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                DrainPackets();
                f.AddButton.InvokeMouseClick(EventArgs.Empty);
                var box = FindModal() as MirInputBox;
                Check(box != null && box.Modal, "case3 add input modal shown");
                if (box == null) { /* fail 已记 */ }
                else
                {
                    box.InputTextBox.Text = "Alice";
                    box.OKButton.InvokeMouseClick(EventArgs.Empty);
                    var add = LastAddFriend();
                    Check(add != null && add.Name == "Alice" && !add.Blocked, "case3 add friend sent");
                    Check(FindModal() == null, "case3 input dismissed");
                }
            }

            // ===== case4 黑名单 tab Add → C.AddFriend{Blocked=true} =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                f.Show(); // 面板须可见，tab 切换才生效（UpdateDisplay 门控 Visible）
                DrainPackets();
                f.BlacklistLabel.InvokeMouseClick(EventArgs.Empty);
                f.AddButton.InvokeMouseClick(EventArgs.Empty);
                var box = FindModal() as MirInputBox;
                Check(box != null, "case4 blacklist input shown");
                if (box == null) { /* fail 已记 */ }
                else
                {
                    box.InputTextBox.Text = "Bob";
                    box.OKButton.InvokeMouseClick(EventArgs.Empty);
                    var add = LastAddFriend();
                    Check(add != null && add.Name == "Bob" && add.Blocked, "case4 add blocked sent");
                }
            }

            // ===== case5 S.FriendUpdate 回声：隐藏填列表不炸；可见重建行 =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                Feed(scene, new ClientFriend { Index = 1, Name = "A", Online = true }, new ClientFriend { Index = 2, Name = "B", Online = false });
                Check(f.Friends.Count == 2, "case5 hidden fill");
                f.Show();
                DrainPackets(); // 清掉 Show 的 RefreshFriends
                Feed(scene, new ClientFriend { Index = 3, Name = "C", Online = true });
                Check(f.Rows[0] != null && f.Rows[0].Friend != null && f.Rows[0].Friend.Name == "C", "case5 visible rebuild");
                Check(f.Rows[1] == null, "case5 row count matches");
            }

            // ===== case6 行点选中 → Selected + 选中迁移 =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                f.Show();
                Feed(scene, new ClientFriend { Index = 1, Name = "A", Online = true }, new ClientFriend { Index = 2, Name = "B", Online = false });
                if (f.Rows[0] != null && f.Rows[1] != null)
                {
                    f.Rows[0].InvokeMouseClick(EventArgs.Empty);
                    Check(f.Rows[0].Selected, "case6 row selected");
                    f.Rows[1].InvokeMouseClick(EventArgs.Empty);
                    Check(!f.Rows[0].Selected && f.Rows[1].Selected, "case6 selection moves");
                }
            }

            // ===== case7 Remove → MirMessageBox YesNo 确认 → Yes → C.RemoveFriend =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                f.Show();
                Feed(scene, new ClientFriend { Index = 7, Name = "A", Online = true });
                if (f.Rows[0] != null) f.Rows[0].InvokeMouseClick(EventArgs.Empty);
                DrainPackets();
                f.RemoveButton.InvokeMouseClick(EventArgs.Empty);
                var box = FindModal() as MirMessageBox;
                Check(box != null && box.Modal, "case7 confirm shown");
                if (box == null) { /* fail 已记 */ }
                else
                {
                    box.YesButton.InvokeMouseClick(EventArgs.Empty);
                    var rm = LastRemove();
                    Check(rm != null && rm.CharacterIndex == 7, "case7 remove sent");
                    Check(FindModal() == null, "case7 confirm dismissed");
                }
            }

            // ===== case8 Remove 未选中守卫：无弹窗无发包 =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                f.Show();
                DrainPackets();
                f.RemoveButton.InvokeMouseClick(EventArgs.Empty);
                Check(FindModal() == null, "case8 no-selection guard");
                Check(LastRemove() == null, "case8 nothing sent");
            }

            // ===== case9 Whisper 在线 → WhisperAction seam（移动端接 OpenWhisper 弹软键盘）=====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                f.Show();
                Feed(scene, new ClientFriend { Index = 9, Name = "Zed", Online = true });
                if (f.Rows[0] != null) f.Rows[0].InvokeMouseClick(EventArgs.Empty);
                string whisperName = null;
                f.WhisperAction = name => whisperName = name;
                f.WhisperButton.InvokeMouseClick(EventArgs.Empty);
                Check(whisperName == "Zed", "case9 whisper seam fired");
            }

            // ===== case10 Whisper 离线守卫：seam 不调用 + 系统消息提示 =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                f.Show();
                Feed(scene, new ClientFriend { Index = 10, Name = "Off", Online = false });
                if (f.Rows[0] != null) f.Rows[0].InvokeMouseClick(EventArgs.Empty);
                bool fired = false;
                f.WhisperAction = name => fired = true;
                f.WhisperButton.InvokeMouseClick(EventArgs.Empty);
                Check(!fired, "case10 offline guard blocks whisper");
            }

            // ===== case11 MemoButton → MemoDialog 预填 → OK → C.AddMemo + 浮窗关 =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                f.Show();
                Feed(scene, new ClientFriend { Index = 11, Name = "M", Online = true, Memo = "old" });
                if (f.Rows[0] != null) f.Rows[0].InvokeMouseClick(EventArgs.Empty);
                DrainPackets();
                f.MemoButton.InvokeMouseClick(EventArgs.Empty);
                var memo = scene.MemoDialog;
                Check(memo.Visible, "case11 memo shown");
                Check(memo.MemoTextBox.Text == "old", "case11 memo prefilled");
                memo.MemoTextBox.Text = "new note";
                memo.OKButton.InvokeMouseClick(EventArgs.Empty);
                var am = LastMemo();
                Check(am != null && am.CharacterIndex == 11 && am.Memo == "new note", "case11 memo sent");
                Check(!memo.Visible, "case11 memo hidden after ok");
            }

            // ===== case12 黑名单 tab 过滤：好友/黑名单各只显示对应成员 =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                f.Show();
                Feed(scene,
                    new ClientFriend { Index = 1, Name = "F1", Blocked = false },
                    new ClientFriend { Index = 2, Name = "B1", Blocked = true },
                    new ClientFriend { Index = 3, Name = "F2", Blocked = false });
                Check(f.Rows[0] != null && f.Rows[0].Friend.Name == "F1" && f.Rows[1].Friend.Name == "F2", "case12 friend tab filter");
                f.BlacklistLabel.InvokeMouseClick(EventArgs.Empty);
                Check(f.Rows[0] != null && f.Rows[0].Friend.Name == "B1", "case12 blacklist tab filter");
                Check(f.Rows[1] == null, "case12 blacklist row count");
                f.FriendLabel.InvokeMouseClick(EventArgs.Empty);
                Check(f.Rows[0] != null && f.Rows[0].Friend.Name == "F1", "case12 back to friend tab");
            }

            // ===== case13 翻页：13 人 → Next/Prev 页标签 1/2 ↔ 2/2 =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                f.Show();
                var friends = new ClientFriend[13];
                for (int i = 0; i < 13; i++) friends[i] = new ClientFriend { Index = i + 1, Name = "P" + (i + 1) };
                Feed(scene, friends);
                Check(f.Rows[11] != null && f.Rows[11].Friend.Name == "P12", "case13 page1 filled");
                Check(f.PageNumberLabel.Text == "1 / 2", "case13 page label 1/2");
                f.NextButton.InvokeMouseClick(EventArgs.Empty);
                Check(f.Rows[0] != null && f.Rows[0].Friend.Name == "P13", "case13 next page");
                Check(f.PageNumberLabel.Text == "2 / 2", "case13 page label 2/2");
                f.PreviousButton.InvokeMouseClick(EventArgs.Empty);
                Check(f.Rows[0] != null && f.Rows[0].Friend.Name == "P1", "case13 prev page");
                Check(f.PageNumberLabel.Text == "1 / 2", "case13 page label back");
            }

            // ===== case14 Memo Cancel → 浮窗关且不发包 =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                f.Show();
                Feed(scene, new ClientFriend { Index = 14, Name = "C", Online = true });
                if (f.Rows[0] != null) f.Rows[0].InvokeMouseClick(EventArgs.Empty);
                f.MemoButton.InvokeMouseClick(EventArgs.Empty);
                var memo = scene.MemoDialog;
                Check(memo.Visible, "case14 memo shown");
                DrainPackets();
                memo.CancelButton.InvokeMouseClick(EventArgs.Empty);
                Check(!memo.Visible, "case14 memo canceled");
                Check(LastMemo() == null, "case14 nothing sent");
            }

            // ===== case15 CloseButton → 面板关 + MemoDialog 联动隐藏 =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                f.Show();
                Feed(scene, new ClientFriend { Index = 15, Name = "D", Online = true });
                if (f.Rows[0] != null) f.Rows[0].InvokeMouseClick(EventArgs.Empty);
                f.MemoButton.InvokeMouseClick(EventArgs.Empty);
                Check(scene.MemoDialog.Visible, "case15 memo open");
                f.CloseButton.InvokeMouseClick(EventArgs.Empty);
                Check(!f.Visible, "case15 panel closed");
                Check(!scene.MemoDialog.Visible, "case15 memo hidden with panel");
            }

            // ===== case16 RouteTouch 集成：好友按钮被 UiConsumer 消费 → OnToggle 开关面板 + Show 发 Refresh =====
            {
                var scene = NewScene();
                var f = scene.FriendDialog;
                var friendBtn = new MobileBag(1280, 720);
                friendBtn.SetMargin(new UnityEngine.Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 5));
                friendBtn.OnToggle = open => { if (open) f.Show(); else f.Hide(); };
                bool joystickFired = false;
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => friendBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                };
                var r = friendBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                DrainPackets();
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(f.Visible, "case16 route opens friend panel");
                Check(!joystickFired, "case16 friend tap consumes joystick");
                Check(LastRefresh() != null, "case16 refresh sent on open");
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!f.Visible, "case16 route closes friend panel");
            }

            // 还原全局 seam（防污染后续探针）。
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
                Console.WriteLine("[friendverify] PASS cases=16");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[friendverify] FAIL cases=16 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
