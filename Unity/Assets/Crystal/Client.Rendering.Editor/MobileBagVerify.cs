using System;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第2项 增量1 背包按钮纯逻辑验证：
    // 喂虚拟坐标断言 MobileBag 命中/toggle/消费语义/Cancel 不 toggle/松手容错/连点翻转/
    // 屏幕重设重布局。无需服务器（OnToggle 注入断言开关次数与开态）。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.MobileBagVerify.Run -quit
    // 断言：全过输出 [bagverify] PASS exit 0。
    public static class MobileBagVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[bagverify] FAIL {what}"); }
        }

        public static void Run()
        {
            // ===== case1 命中 toggle：Down 按钮内 → Up → Open 翻转 + OnToggle(true) =====
            {
                int toggleCalls = 0; bool lastOpen = false;
                var bag = new MobileBag(1280, 720);
                bag.OnToggle = b => { toggleCalls++; lastOpen = b; };
                Vector2 c = bag.ButtonRect.center;
                Check(bag.HitTest(c), "case1 center inside");
                bag.OnTouch(0, JoystickPhase.Down, c);
                Check(!bag.Open, "case1 not open on down");
                bag.OnTouch(0, JoystickPhase.Up, c);
                Check(bag.Open, "case1 open after up");
                Check(toggleCalls == 1 && lastOpen, "case1 onToggle once true");
            }

            // ===== case2 按钮外不触发：Down/Up 远离按钮 → Open 不变、不 toggle =====
            {
                int toggleCalls = 0;
                var bag = new MobileBag(1280, 720);
                bag.OnToggle = b => toggleCalls++;
                Vector2 far = new Vector2(50f, 50f); // 左上（血条区），远离右上按钮
                Check(!bag.HitTest(far), "case2 far outside");
                bag.OnTouch(0, JoystickPhase.Down, far);
                bag.OnTouch(0, JoystickPhase.Up, far);
                Check(!bag.Open && toggleCalls == 0, "case2 outside no toggle");
            }

            // ===== case3 消费语义：Down 命中返回 true（调用方不喂摇杆/HUD），未命中返回 false =====
            {
                var bag = new MobileBag(1280, 720);
                Vector2 c = bag.ButtonRect.center;
                bool consumedHit = bag.OnTouch(0, JoystickPhase.Down, c);
                bool consumedMiss = bag.OnTouch(1, JoystickPhase.Down, new Vector2(50f, 50f));
                Check(consumedHit && !consumedMiss, "case3 consume hit only");
                bag.OnTouch(0, JoystickPhase.Cancel, c); // 清理按下态
            }

            // ===== case4 Cancel 不 toggle：Down 命中 → Cancel → Open 不变、按下态清 =====
            {
                int toggleCalls = 0;
                var bag = new MobileBag(1280, 720);
                bag.OnToggle = b => toggleCalls++;
                Vector2 c = bag.ButtonRect.center;
                bag.OnTouch(0, JoystickPhase.Down, c);
                bag.OnTouch(0, JoystickPhase.Cancel, c);
                Check(!bag.Open && toggleCalls == 0, "case4 cancel no toggle");
                // Cancel 后按下态已清：后续 Up 不误触发
                bag.OnTouch(0, JoystickPhase.Up, c);
                Check(toggleCalls == 0, "case4 post-cancel up no toggle");
            }

            // ===== case5 连点翻转：Down→Up 两次 → Open 翻回 false =====
            {
                int toggleCalls = 0;
                var bag = new MobileBag(1280, 720);
                bag.OnToggle = b => toggleCalls++;
                Vector2 c = bag.ButtonRect.center;
                bag.OnTouch(0, JoystickPhase.Down, c); bag.OnTouch(0, JoystickPhase.Up, c);
                Check(bag.Open && toggleCalls == 1, "case5 first toggle open");
                bag.OnTouch(0, JoystickPhase.Down, c); bag.OnTouch(0, JoystickPhase.Up, c);
                Check(!bag.Open && toggleCalls == 2, "case5 second toggle close");
            }

            // ===== case6 松手容错（Began 丢失）：直接 Up 命中按钮且未按下 → 仍 toggle（模拟器低帧率 tap 帧合并） =====
            {
                int toggleCalls = 0;
                var bag = new MobileBag(1280, 720);
                bag.OnToggle = b => toggleCalls++;
                Vector2 c = bag.ButtonRect.center;
                bag.OnTouch(0, JoystickPhase.Up, c); // 无 Down 前置
                Check(bag.Open && toggleCalls == 1, "case6 lost-down up still toggles");
            }

            // ===== case7 屏幕重设重布局：SetScreen 后按钮右下锚定，旧坐标不命中、新坐标命中 =====
            {
                var bag = new MobileBag(1280, 720);
                Vector2 oldCenter = bag.ButtonRect.center;
                Vector2 oldInner = bag.ButtonRect.center + new Vector2(1f, 1f);
                bag.SetScreen(720, 1280); // 竖屏：按钮随 ScreenW 重算
                Rect r = bag.ButtonRect;
                Check(Mathf.Approximately(r.xMax, 720f - MobileBag.ButtonMargin.x), "case7 relayout right-margin");
                Check(!bag.HitTest(oldCenter) && !bag.HitTest(oldInner), "case7 old coords miss after relayout");
                Check(bag.HitTest(r.center), "case7 new center hit");
            }

            // ===== case8 Open 状态跨 SetScreen 保留 =====
            {
                var bag = new MobileBag(1280, 720);
                Vector2 c = bag.ButtonRect.center;
                bag.OnTouch(0, JoystickPhase.Down, c); bag.OnTouch(0, JoystickPhase.Up, c);
                Check(bag.Open, "case8 open before relayout");
                bag.SetScreen(720, 1280);
                Check(bag.Open, "case8 open preserved after relayout");
            }

            if (_fail == 0)
            {
                Console.WriteLine("[bagverify] PASS cases=8");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[bagverify] FAIL cases=8 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
