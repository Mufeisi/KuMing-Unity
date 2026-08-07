using System;
using Client.MirControls;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;
using MPoint = Crystal.Client.Core.MirMath.Point;
using Size = Crystal.Client.Core.MirMath.Size;

namespace Crystal.Rendering.Editor
{
    // 阶段8 0 项 移动 UI 适配层纯逻辑验证：batchmode 探针，无服务器。
    // 断言：ToUi 翻转/TouchRect 最小触控/对话框命中/触摸互斥路由/返回键/滚动冲突/输入契约时序。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.MobileUiVerify.Run -quit
    // 全过输出 [mobileuiverify] PASS exit 0。
    public static class MobileUiVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[mobileuiverify] FAIL {what}"); }
        }

        // 构造 MirControl stub 树（Location/Size setter 安全：Redraw 仅向父递归，根无父）。
        static MirControl Tree()
        {
            var root = new MirControl { Location = new MPoint(0, 0), Size = new Size(200, 200) };
            var child = new MirControl { Location = new MPoint(50, 50), Size = new Size(100, 100) };
            root.Controls.Add(child);
            return root;
        }

        public static void Run()
        {
            // 1. 翻转：raw（左下 y 上）→ ui（左上 y 下），y 镜像根因修正点。
            MobileUiAdapter.ScreenW = 1280; MobileUiAdapter.ScreenH = 720;
            Check(MobileUiAdapter.ToUi(new Vector2(100, 100)) == new Vector2(100, 620), "case1 toUi flips y");
            Check(MobileUiAdapter.ToUi(new Vector2(100, 620)) == new Vector2(100, 100), "case1 toUi round-trip");
            var pt = MobileUiAdapter.ToUiPoint(new Vector2(640, 360));
            Check(pt.X == 640 && pt.Y == 360, "case1 toUiPoint center preserved");
            pt = MobileUiAdapter.ToUiPoint(new Vector2(100, 100));
            Check(pt.X == 100 && pt.Y == 620, "case1 toUiPoint flips y");

            // 2. 最小触控尺寸：短边不足 44 外扩，足尺寸保留。
            var r = MobileUiAdapter.TouchRect(new Vector2(0, 0), new Vector2(10, 10));
            Check(Mathf.Approximately(r.x, -22f) && Mathf.Approximately(r.width, 44f) && Mathf.Approximately(r.height, 44f), "case2 min touch 44x44");
            r = MobileUiAdapter.TouchRect(new Vector2(0, 0), new Vector2(100, 60));
            Check(Mathf.Approximately(r.x, -50f) && Mathf.Approximately(r.width, 100f) && Mathf.Approximately(r.height, 60f), "case2 keep >= 44");
            Check(MobileUiAdapter.MinTouchSize >= 44f, "case2 min size constant");

            // 3. 对话框命中（注入 DialogRoot stub 树，替换默认 GameScene 树）。
            var tree = Tree();
            var prevRoot = MobileUiAdapter.DialogRoot;
            MobileUiAdapter.DialogRoot = () => tree;
            Check(MobileUiAdapter.UiHitTest(new MPoint(60, 60)), "case3 hit child");
            Check(MobileUiAdapter.UiHitTest(new MPoint(150, 150)), "case3 hit root");
            Check(!MobileUiAdapter.UiHitTest(new MPoint(250, 250)), "case3 miss outside");
            var child = tree.Controls[0];
            // 可见性隔离：child 完全在 root rect 内，(60,60) 会被 root 自身命中而短路返回，
            // 故直接以 child 为递归根断言——child 不可见即整支不可命中。
            Check(MobileUiAdapter.UiHitTest(child, new MPoint(60, 60)), "case3 child node hit");
            child.Visible = false;
            Check(!MobileUiAdapter.UiHitTest(child, new MPoint(60, 60)), "case3 invisible child miss");
            child.Visible = true;
            tree.Visible = false;
            Check(!MobileUiAdapter.UiHitTest(tree, new MPoint(60, 60)), "case3 invisible root miss");
            tree.Visible = true;
            MobileUiAdapter.DialogRoot = () => null;
            Check(!MobileUiAdapter.UiHitTest(new MPoint(60, 60)), "case3 null root miss");
            MobileUiAdapter.DialogRoot = prevRoot;

            // 4. 触摸互斥路由：消费序（UI 消费者 → 面板打开 → Down 对话框命中）+ 空间分发（摇杆 raw / HUD ui）。
            int uiCalls = 0, joyCalls = 0, hudCalls = 0;
            Vector2 joyRaw = default, hudUi = default;
            var route = new MobileUiAdapter.TouchRoute
            {
                UiConsumer = (id, ph, ui) => { uiCalls++; return false; },
                PanelOpen = false,
                DialogHit = p => false,
                Joystick = (id, ph, raw) => { joyCalls++; joyRaw = raw; },
                Hud = (id, ph, ui) => { hudCalls++; hudUi = ui; },
            };
            MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Move, new Vector2(100, 200));
            Check(uiCalls == 1 && joyCalls == 1 && hudCalls == 1, "case4 route all consumers");
            Check(joyRaw == new Vector2(100, 200) && hudUi == new Vector2(100, 520), "case4 joystick raw / hud ui");
            route.UiConsumer = (id, ph, ui) => { uiCalls++; return true; };
            MobileUiAdapter.RouteTouch(route, 1, JoystickPhase.Down, new Vector2(10, 10));
            Check(uiCalls == 2 && joyCalls == 1 && hudCalls == 1, "case4 ui consumer suppresses joystick/hud");
            route.UiConsumer = (id, ph, ui) => { uiCalls++; return false; };
            route.PanelOpen = true;
            MobileUiAdapter.RouteTouch(route, 2, JoystickPhase.Down, new Vector2(10, 10));
            Check(uiCalls == 3 && joyCalls == 1 && hudCalls == 1, "case4 panel open suppresses joystick/hud");
            route.PanelOpen = false;
            route.DialogHit = p => true;
            MobileUiAdapter.RouteTouch(route, 3, JoystickPhase.Down, new Vector2(10, 10));
            Check(uiCalls == 4 && joyCalls == 1 && hudCalls == 1, "case4 dialog hit suppresses joystick/hud");
            MobileUiAdapter.RouteTouch(route, 4, JoystickPhase.Move, new Vector2(10, 10));
            Check(joyCalls == 2, "case4 dialog hit only gates Down");

            // 5. 返回键：检测/处理注入，消费语义（Android Back → 关顶层对话框钩子）。
            int backHandled = 0;
            var prevBack = MobileUiAdapter.IsBackPressed;
            var prevHandler = MobileUiAdapter.BackHandler;
            MobileUiAdapter.IsBackPressed = () => false;
            MobileUiAdapter.BackHandler = () => { backHandled++; return true; };
            MobileUiAdapter.PollBackKey();
            Check(backHandled == 0, "case5 no back no handle");
            MobileUiAdapter.IsBackPressed = () => true;
            MobileUiAdapter.PollBackKey();
            Check(backHandled == 1, "case5 back handled");
            MobileUiAdapter.BackHandler = () => { backHandled++; return false; };
            MobileUiAdapter.PollBackKey();
            Check(backHandled == 2, "case5 unhandled back still invokes handler");
            MobileUiAdapter.BackHandler = null;
            MobileUiAdapter.PollBackKey();
            Check(backHandled == 2, "case5 null handler safe");
            MobileUiAdapter.IsBackPressed = prevBack;
            MobileUiAdapter.BackHandler = prevHandler;

            // 6. 滚动冲突：可滚动区命中判定（注入 IsScrollable 谓词；逐字 MirControl 无 Scrollable 成员故谓词为 seam）。
            var scrollNode = new MirControl { Location = new MPoint(10, 10), Size = new Size(50, 50) };
            tree.Controls.Add(scrollNode);
            var prevScroll = MobileUiAdapter.IsScrollable;
            MobileUiAdapter.IsScrollable = c => c == scrollNode;
            Check(MobileUiAdapter.ScrollConflict(tree, new MPoint(20, 20)), "case6 scroll conflict hit");
            Check(!MobileUiAdapter.ScrollConflict(tree, new MPoint(150, 150)), "case6 scroll conflict miss outside");
            scrollNode.Visible = false;
            Check(!MobileUiAdapter.ScrollConflict(tree, new MPoint(20, 20)), "case6 invisible scroll miss");
            MobileUiAdapter.IsScrollable = prevScroll;

            // 7. 输入契约时序：Tap(≤200ms)/LongPress(≥500ms)/Drag(>10px)/DoubleTap(间隔≤300ms) 边界。
            // 用例间 Reset() 隔离（双击窗/锚点不复用），时钟单调递增（Now 注入）。
            var mi = new MobileInput();
            long now = 10000;
            mi.Now = () => now;
            mi.Reset();
            mi.OnTouch(0, JoystickPhase.Down, new Vector2(100, 100)); now += 100; mi.OnTouch(0, JoystickPhase.Up, new Vector2(100, 100));
            Check(mi.LastGesture == GestureKind.Tap, "case7 tap 100ms");
            mi.Reset();
            mi.OnTouch(0, JoystickPhase.Down, new Vector2(100, 100)); now += 250; mi.OnTouch(0, JoystickPhase.Up, new Vector2(100, 100));
            Check(mi.LastGesture == GestureKind.Tap, "case7 tap 250ms (under longpress)");
            mi.Reset();
            mi.OnTouch(0, JoystickPhase.Down, new Vector2(100, 100)); now += 500; mi.OnTouch(0, JoystickPhase.Up, new Vector2(100, 100));
            Check(mi.LastGesture == GestureKind.LongPress, "case7 longpress boundary 500ms");
            mi.Reset();
            mi.OnTouch(0, JoystickPhase.Down, new Vector2(100, 100)); now += 100; mi.OnTouch(0, JoystickPhase.Move, new Vector2(150, 100)); mi.OnTouch(0, JoystickPhase.Up, new Vector2(150, 100));
            Check(mi.LastGesture == GestureKind.Drag, "case7 drag beyond threshold");
            mi.Reset();
            mi.OnTouch(0, JoystickPhase.Down, new Vector2(100, 100)); now += 100; mi.OnTouch(0, JoystickPhase.Move, new Vector2(105, 100)); mi.OnTouch(0, JoystickPhase.Up, new Vector2(105, 100));
            Check(mi.LastGesture == GestureKind.Tap, "case7 within-drag-threshold still tap");
            mi.Reset();
            mi.OnTouch(0, JoystickPhase.Down, new Vector2(200, 200)); now += 100; mi.OnTouch(0, JoystickPhase.Up, new Vector2(200, 200));
            Check(mi.LastGesture == GestureKind.Tap, "case7 first tap");
            mi.OnTouch(0, JoystickPhase.Down, new Vector2(202, 200)); now += 100; mi.OnTouch(0, JoystickPhase.Up, new Vector2(202, 200));
            Check(mi.LastGesture == GestureKind.DoubleTap, "case7 second tap within interval -> double");
            mi.Reset();
            mi.OnTouch(0, JoystickPhase.Down, new Vector2(200, 200)); now += 100; mi.OnTouch(0, JoystickPhase.Up, new Vector2(200, 200));
            mi.OnTouch(0, JoystickPhase.Down, new Vector2(200, 200)); now += 400; mi.OnTouch(0, JoystickPhase.Up, new Vector2(200, 200));
            Check(mi.LastGesture == GestureKind.Tap, "case7 after interval -> single tap again");

            if (_fail == 0)
            {
                Console.WriteLine("[mobileuiverify] PASS cases=7");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[mobileuiverify] FAIL cases=7 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
