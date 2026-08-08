using System;
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
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第5项 增量2 聊天触控纯逻辑验证（无服务器）：
    // ChatDialog 常驻（挂 scene，输入框默认隐藏）；聊天按钮 tap → OpenInput（首次开注入当前频道前缀
    // + SetChatText 聚焦显示 + SoftKeyboardBridge.Focus 弹软键盘）；频道按钮循环 0 附近/1 全员 !/2 行会 @；
    // 软键盘文本回流（Poll SyncText）+ Enter 提交（Submitted → ChatTextBox_KeyPress → C.Chat，SentPackets 断言）；
    // 开着切频道重写前缀 + 重开软键盘；未开切频道不触碰文本；按钮区外不消费；Cancel 抑制；CloseInput（Back 语义）幂等；
    // RouteTouch 集成（UiConsumer 消费聊天按钮，不喂摇杆）。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.ChatVerify.Run -quit
    // 断言：全过输出 [chatverify] PASS cases=11 exit 0。
    public static class ChatVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[chatverify] FAIL {what}"); }
        }

        // 对齐真实 TouchScreenKeyboard：Open 初始文本即成为键盘内容（Poll SyncText 以键盘文本覆盖框文本）。
        class FakeKeyboard : ISoftKeyboard
        {
            public string Text { get; set; } = string.Empty;
            public bool Active { get; set; }
            public bool Submitted { get; set; }
            public bool Canceled { get; set; }
            public string OpenedText; public int OpenedMax; public bool OpenedPassword;
            public int OpenCount, CloseCount;

            public void Open(string text, int maxLength, bool password)
            {
                Text = text;
                OpenedText = text; OpenedMax = maxLength; OpenedPassword = password;
                OpenCount++; Active = true; Submitted = Canceled = false;
            }
            public void Close() { CloseCount++; Active = false; }
        }

        static MobileChat _chat;
        static ChatDialog _dlg;
        static FakeKeyboard _fake;

        // 探针夹具：清空全局 seam + 建 MainDialog（ChatDialog ctor 读 MainDialog.Location，顺序契约同 NetProbe）。
        static void NewScene()
        {
            _fake = new FakeKeyboard();
            SoftKeyboardBridge.Keyboard = _fake;
            SoftKeyboardBridge.Unfocus();
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.User = null;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;

            var mc = new MapControl { Width = 30, Height = 30, M2CellInfo = new CellInfo[30, 30] };
            for (int x = 0; x < 30; x++)
                for (int y = 0; y < 30; y++)
                    mc.M2CellInfo[x, y] = new CellInfo();
            mc.PathFinder = new PathFinder(mc);

            var user = new UserObject(1)
            {
                Movement = new MPoint(1, 1),
                CurrentLocation = new MPoint(1, 1),
                OffSetMove = MPoint.Empty,
                Direction = MirDirection.Up,
                Name = "probe",
            };
            MapObject.User = user;
            MapControl.User = user;
            GameScene.User = user;

            var scene = new GameScene { MapControl = mc };
            GameScene.Scene = scene;
            var main = new MainDialog { Parent = scene };
            scene.MainDialog = main;
            _dlg = new ChatDialog { Parent = scene };
            scene.ChatDialog = _dlg;
            MobileUiAdapter.DialogRoot = () => GameScene.Scene;

            _chat = new MobileChat(1280, 720);
            _chat.OnOpenInput = () => MobileChat.OpenInput(_dlg, _chat.Channel);
            _chat.OnChannel = ch => MobileChat.ApplyChannel(_dlg, ch);
        }

        // ui 空间按钮中心点。
        static UnityEngine.Vector2 Center(Rect r) => new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);

        static void DrainPackets()
        {
            while (Network.SentPackets.TryDequeue(out _)) { }
        }

        static C.Chat LastChat()
        {
            C.Chat result = null;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.Chat c) result = c;
            return result;
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;

            // ===== case1 常驻创建：ChatDialog 挂 scene + ChatTextBox 存在 + 输入框默认隐藏 =====
            {
                NewScene();
                var scene = GameScene.Scene;
                Check(scene.ChatDialog == _dlg, "case1 chatdialog attached");
                Check(_dlg.ChatTextBox != null, "case1 chattextbox exists");
                Check(!_dlg.ChatTextBox.Visible, "case1 input hidden by default");
            }

            // ===== case2 聊天按钮 tap：开输入框 + 聚焦 + 弹软键盘（初始文本=当前频道前缀 ""）=====
            {
                NewScene();
                var c = Center(_chat.ChatRect);
                Check(_chat.OnTouch(0, JoystickPhase.Down, c) && _chat.OnTouch(0, JoystickPhase.Up, c), "case2 chat tap consumed");
                Check(_dlg.ChatTextBox.Visible, "case2 input shown");
                Check(SoftKeyboardBridge.ActiveBox == _dlg.ChatTextBox, "case2 keyboard focused");
                Check(_fake.OpenCount == 1 && _fake.OpenedText == "", "case2 open with channel-0 prefix");
            }

            // ===== case3 频道循环：0 附近 → 1 全员 → 2 行会 → 0 =====
            {
                NewScene();
                var c = Center(_chat.ChannelRect);
                _chat.OnTouch(0, JoystickPhase.Down, c); _chat.OnTouch(0, JoystickPhase.Up, c);
                Check(_chat.Channel == 1, "case3 channel 1");
                _chat.OnTouch(0, JoystickPhase.Down, c); _chat.OnTouch(0, JoystickPhase.Up, c);
                Check(_chat.Channel == 2, "case3 channel 2");
                _chat.OnTouch(0, JoystickPhase.Down, c); _chat.OnTouch(0, JoystickPhase.Up, c);
                Check(_chat.Channel == 0, "case3 channel wrap 0");
            }

            // ===== case4 软键盘输入回流 + Enter 提交 → C.Chat（无频道前缀）=====
            {
                NewScene();
                var c = Center(_chat.ChatRect);
                _chat.OnTouch(0, JoystickPhase.Down, c); _chat.OnTouch(0, JoystickPhase.Up, c); // 开输入
                _fake.Text = "hello";
                SoftKeyboardBridge.Poll();
                Check(_dlg.ChatTextBox.Text == "hello", "case4 poll syncs text");
                DrainPackets();
                _fake.Submitted = true;
                SoftKeyboardBridge.Poll();
                var chat = LastChat();
                Check(chat != null && chat.Message == "hello", "case4 enter sends C.Chat");
                Check(!_dlg.ChatTextBox.Visible && _dlg.ChatTextBox.Text == string.Empty, "case4 input hidden+cleared");
                Check(SoftKeyboardBridge.ActiveBox == null, "case4 unfocus after submit");
            }

            // ===== case5 频道前缀注入发送：channel 1（!）开输入带前缀 → C.Chat 含 "!" =====
            {
                NewScene();
                var cc = Center(_chat.ChannelRect);
                _chat.OnTouch(0, JoystickPhase.Down, cc); _chat.OnTouch(0, JoystickPhase.Up, cc); // → 1 全员
                var bc = Center(_chat.ChatRect);
                _chat.OnTouch(0, JoystickPhase.Down, bc); _chat.OnTouch(0, JoystickPhase.Up, bc); // 开输入
                Check(_dlg.ChatTextBox.Text == "!" && _fake.OpenedText == "!", "case5 prefix injected on open");
                _fake.Text = "!hello";
                SoftKeyboardBridge.Poll();
                Check(_dlg.ChatTextBox.Text == "!hello", "case5 poll keeps prefix");
                DrainPackets();
                _fake.Submitted = true;
                SoftKeyboardBridge.Poll();
                var chat = LastChat();
                Check(chat != null && chat.Message == "!hello", "case5 enter sends prefixed C.Chat");
            }

            // ===== case6 开着切频道：重写文本前缀 + 重开软键盘（初始文本生效）=====
            {
                NewScene();
                var bc = Center(_chat.ChatRect);
                var cc = Center(_chat.ChannelRect);
                _chat.OnTouch(0, JoystickPhase.Down, bc); _chat.OnTouch(0, JoystickPhase.Up, bc); // 开输入（channel 0）
                _fake.Text = "hi";
                SoftKeyboardBridge.Poll();
                Check(_dlg.ChatTextBox.Text == "hi", "case6 typed hi");
                _chat.OnTouch(0, JoystickPhase.Down, cc); _chat.OnTouch(0, JoystickPhase.Up, cc); // → 1 全员
                Check(_dlg.ChatTextBox.Text == "!hi", "case6 prefix rewritten");
                Check(_fake.OpenCount == 2 && _fake.OpenedText == "!hi", "case6 keyboard reopened with prefix");
                _chat.OnTouch(0, JoystickPhase.Down, cc); _chat.OnTouch(0, JoystickPhase.Up, cc); // → 2 行会
                Check(_dlg.ChatTextBox.Text == "@hi" && _fake.OpenedText == "@hi", "case6 guild prefix");
                _chat.OnTouch(0, JoystickPhase.Down, cc); _chat.OnTouch(0, JoystickPhase.Up, cc); // → 0 附近
                Check(_dlg.ChatTextBox.Text == "hi" && _fake.OpenedText == "hi", "case6 back to nearby strips prefix");
            }

            // ===== case7 未开切频道：不触碰输入文本、不弹软键盘（仅按钮色相切换）=====
            {
                NewScene();
                var cc = Center(_chat.ChannelRect);
                _chat.OnTouch(0, JoystickPhase.Down, cc); _chat.OnTouch(0, JoystickPhase.Up, cc);
                Check(_chat.Channel == 1, "case7 channel switched");
                Check(!_dlg.ChatTextBox.Visible && _dlg.ChatTextBox.Text == string.Empty, "case7 input untouched");
                Check(_fake.OpenCount == 0, "case7 no keyboard");
            }

            // ===== case8 按钮区外 tap：不消费（落回世界/摇杆）=====
            {
                NewScene();
                Check(!_chat.OnTouch(0, JoystickPhase.Down, new UnityEngine.Vector2(500, 300)), "case8 outside not consumed");
            }

            // ===== case9 Cancel 抑制：按下后系统打断 → Up 不触发开输入 =====
            {
                NewScene();
                var bc = Center(_chat.ChatRect);
                _chat.OnTouch(0, JoystickPhase.Down, bc);
                _chat.OnTouch(0, JoystickPhase.Cancel, bc);
                _chat.OnTouch(0, JoystickPhase.Up, bc);
                Check(!_dlg.ChatTextBox.Visible && _fake.OpenCount == 0, "case9 cancel suppresses tap");
            }

            // ===== case10 CloseInput（Back 语义）：关输入+清空+解绑软键盘；幂等 =====
            {
                NewScene();
                var bc = Center(_chat.ChatRect);
                _chat.OnTouch(0, JoystickPhase.Down, bc); _chat.OnTouch(0, JoystickPhase.Up, bc);
                Check(MobileChat.CloseInput(_dlg), "case10 close returns true when open");
                Check(!_dlg.ChatTextBox.Visible && _dlg.ChatTextBox.Text == string.Empty, "case10 closed+cleared");
                Check(SoftKeyboardBridge.ActiveBox == null, "case10 keyboard unfocused");
                Check(!MobileChat.CloseInput(_dlg), "case10 close idempotent false");
            }

            // ===== case11 RouteTouch 集成：聊天按钮被 UiConsumer 消费，不喂摇杆，输入框开 =====
            {
                NewScene();
                bool joystickFired = false;
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => _chat.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                };
                var bc = Center(_chat.ChatRect); // ui 空间 → raw 空间（唯一翻转点）
                var raw = new UnityEngine.Vector2(bc.x, 720f - bc.y);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(_dlg.ChatTextBox.Visible, "case11 route opens input");
                Check(!joystickFired, "case11 chat tap consumes joystick");
            }

            // 还原全局 seam。
            SoftKeyboardBridge.Keyboard = null;
            SoftKeyboardBridge.Unfocus();
            MobileUiAdapter.DialogRoot = null;

            if (_fail == 0)
            {
                Console.WriteLine("[chatverify] PASS cases=11");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[chatverify] FAIL cases=11 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
