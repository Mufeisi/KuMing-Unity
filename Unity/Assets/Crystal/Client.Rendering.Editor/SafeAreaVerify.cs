using System;
using Client.MirControls;
using Crystal.Client.Core.MirMath;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第5项 增量1 探针：安全区适配 + 软键盘触控接线。
    // 安全区：SafeArea 四 inset（Provider 注入假刘海/Home indicator）→ MobileHud（攻击圆心上抬/血条下移）
    // + MobileBag 右上按钮列（右/顶内缩）+ 派生按钮（装备/任务/地图 SetMargin 列）继承；inset=0 回归不漂移。
    // 软键盘：RouteTouch Down 命中可见 MirTextBox → SoftKeyboardBridge.Focus（Open 弹 TouchScreenKeyboard，
    // 初始文本/长度透传）→ Poll 文本回流 → Enter 提交（KeyPress 进控件树）+ 解绑；框外/不可见/禁用不聚焦。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.SafeAreaVerify.Run -quit
    // 断言：全过输出 [safeareaverify] PASS cases=7 exit 0。
    public static class SafeAreaVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[safeareaverify] FAIL {what}"); }
        }

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
                OpenedText = text; OpenedMax = maxLength; OpenedPassword = password;
                OpenCount++; Active = true; Submitted = Canceled = false;
            }
            public void Close() { CloseCount++; Active = false; }
        }

        static MobileHud _hud;
        static MobileBag _bag;
        static MirImageControl _dlg; // 对话框容器（含输入框）
        static MirTextBox _box;
        static FakeKeyboard _fake;

        static void NewScene(Vector4 insets)
        {
            SafeArea.Provider = () => insets;
            _fake = new FakeKeyboard();
            SoftKeyboardBridge.Keyboard = _fake;
            SoftKeyboardBridge.Unfocus();
            _hud = new MobileHud(1280, 720);
            _bag = new MobileBag(1280, 720);
            _dlg = new MirImageControl { Visible = true };
            _box = new MirTextBox
            {
                Parent = _dlg,
                Visible = true,
                Location = new Point(100, 100),
                Size = new Size(200, 30),
                Text = "hello",
            };
            MobileUiAdapter.DialogRoot = () => _dlg;
        }

        public static void Run()
        {
            // ===== case1 安全区四 inset 注入读值（消费方读单一来源）=====
            {
                NewScene(new Vector4(10, 44, 20, 34));
                Check(Mathf.Approximately(SafeArea.Left, 10f) && Mathf.Approximately(SafeArea.Top, 44f)
                      && Mathf.Approximately(SafeArea.Right, 20f) && Mathf.Approximately(SafeArea.Bottom, 34f),
                      "case1 insets read through provider");
            }

            // ===== case2 MobileHud 刘海偏移：攻击圆心上抬（底 34）+ 血条下移（顶 44/左 10）=====
            {
                NewScene(new Vector4(10, 44, 20, 34));
                Check(Mathf.Approximately(_hud.AttackCenter.x, 1280f - 20f - 90f)
                      && Mathf.Approximately(_hud.AttackCenter.y, 720f - 34f - 160f),
                      "case2 hud attack inset");
                Check(Mathf.Approximately(_hud.HpPos.x, 10f + MobileHud.HpBarPos.x)
                      && Mathf.Approximately(_hud.HpPos.y, 44f + MobileHud.HpBarPos.y),
                      "case2 hud hp inset");
            }

            // ===== case3 MobileBag 右上按钮 + 派生按钮列继承（右 20 内缩、顶 44 下移）=====
            {
                NewScene(new Vector4(10, 44, 20, 34));
                var r = _bag.ButtonRect;
                Check(Mathf.Approximately(r.xMax, 1280f - MobileBag.ButtonMargin.x - 20f)
                      && Mathf.Approximately(r.y, MobileBag.ButtonMargin.y + 44f),
                      "case3 bag rect inset");
                var equip = new MobileBag(1280, 720);
                equip.SetMargin(new UnityEngine.Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + MobileBag.ButtonH + 8f));
                Check(Mathf.Approximately(equip.ButtonRect.y, MobileBag.ButtonMargin.y + MobileBag.ButtonH + 8f + 44f),
                      "case3 derived button inherits top inset");
            }

            // ===== case4 inset=0 回归：布局与旧基准一致（探针契约不漂移）=====
            {
                NewScene(new Vector4(0, 0, 0, 0));
                Check(Mathf.Approximately(_hud.AttackCenter.x, 1280f - 90f)
                      && Mathf.Approximately(_hud.AttackCenter.y, 720f - 160f)
                      && Mathf.Approximately(_hud.HpPos.x, 20f) && Mathf.Approximately(_hud.HpPos.y, 20f),
                      "case4 hud zero-inset baseline");
                var r = _bag.ButtonRect;
                Check(Mathf.Approximately(r.xMax, 1280f - MobileBag.ButtonMargin.x)
                      && Mathf.Approximately(r.y, MobileBag.ButtonMargin.y),
                      "case4 bag zero-inset baseline");
            }

            // ===== case5 软键盘触控聚焦：命中输入框 → Focus + Open（初始文本/长度）+ 文本回流 + Enter 提交 =====
            {
                NewScene(new Vector4(0, 0, 0, 0));
                var hit = new MPoint(150, 110); // 框内 (100,100)+(200,30)
                Check(MobileUiAdapter.TryFocusTextBox(hit), "case5 focus hit");
                Check(SoftKeyboardBridge.ActiveBox == _box && _fake.OpenCount == 1
                      && _fake.OpenedText == "hello" && _fake.OpenedMax == _box.MaxLength
                      && !_fake.OpenedPassword, "case5 focus opens keyboard with box state");
                _fake.Text = "hello world";
                SoftKeyboardBridge.Poll();
                Check(_box.Text == "hello world", "case5 poll syncs text to input model");
                char captured = '\0';
                _box.TextBox.KeyPress += (s, e) => captured = e.KeyChar;
                _fake.Submitted = true;
                SoftKeyboardBridge.Poll();
                Check(captured == (char)Keys.Enter && SoftKeyboardBridge.ActiveBox == null,
                      "case5 enter submits and unfocus");
            }

            // ===== case6 命中条件：框外 / 不可见 / 禁用 不聚焦 =====
            {
                NewScene(new Vector4(0, 0, 0, 0));
                Check(!MobileUiAdapter.TryFocusTextBox(new MPoint(10, 10)), "case6 outside no focus");
                _box.Visible = false;
                Check(!MobileUiAdapter.TryFocusTextBox(new MPoint(150, 110)), "case6 invisible no focus");
                _box.Visible = true;
                _box.TextBox.Enabled = false;
                Check(!MobileUiAdapter.TryFocusTextBox(new MPoint(150, 110)), "case6 disabled no focus");
                Check(SoftKeyboardBridge.ActiveBox == null, "case6 still unfocused");
            }

            // ===== case7 RouteTouch 集成：Down 落在输入框 → 聚焦；对话框命中消费（不喂摇杆）=====
            {
                NewScene(new Vector4(0, 0, 0, 0));
                bool joystickFired = false;
                MobileUiAdapter.RouteTouch(new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => false,
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                }, 0, JoystickPhase.Down, new UnityEngine.Vector2(150f, 720f - 110f)); // raw → ui (150, 110) 框内
                Check(SoftKeyboardBridge.ActiveBox == _box, "case7 route down focuses box");
                Check(!joystickFired, "case7 dialog hit consumes joystick");
            }

            // 还原全局 seam。
            SafeArea.Provider = () => new Vector4(0, 0, 0, 0);
            MobileUiAdapter.DialogRoot = null;
            SoftKeyboardBridge.Keyboard = null;
            SoftKeyboardBridge.Unfocus();

            if (_fail == 0)
            {
                Console.WriteLine("[safeareaverify] PASS cases=7");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[safeareaverify] FAIL cases=7 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
