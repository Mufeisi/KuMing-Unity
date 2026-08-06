using System;
using Crystal.Client.Rendering;
using UnityEditor;

namespace Crystal.Rendering.Editor
{
    // 阶段7 第 3 项（触控 Input Adapter）逻辑层验证：喂确定性触摸序列断言 TouchInputMapper 语义。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.TouchInputVerify.Run -quit
    // 断言：点击/拖拽/阈值边界/取消/非法序列；全过输出 [touchverify] PASS exit 0。
    public static class TouchInputVerify
    {
        public static void Run()
        {
            int fail = 0;
            void Check(bool cond, string what)
            {
                if (!cond) { fail++; Console.WriteLine($"[touchverify] FAIL {what}"); }
            }

            // 1. 单点点击：down → up 无拖拽 → Click。
            var m = new TouchInputMapper();
            m.OnTouchDown(100, 100);
            Check(m.IsTouching, "case1 down->IsTouching");
            Check(!m.IsDragging, "case1 down->!IsDragging");
            bool click = m.OnTouchUp(100, 100);
            Check(click, "case1 up click=true");
            Check(!m.IsTouching, "case1 up->!IsTouching");

            // 2. 拖拽：位移超阈值 → 翻拖拽态，Up 不产生 Click。
            m = new TouchInputMapper();
            m.OnTouchDown(100, 100);
            bool flipped = m.OnTouchMove(50, 50);
            Check(flipped, "case2 move flips dragging");
            Check(m.IsDragging, "case2 IsDragging");
            click = m.OnTouchUp(50, 50);
            Check(!click, "case2 up click=false");

            // 3. 未超阈值移动：位移 ~5.66 < 10 → 不翻拖拽，Up 仍 Click。
            m = new TouchInputMapper();
            m.OnTouchDown(100, 100);
            flipped = m.OnTouchMove(104, 104);
            Check(!flipped, "case3 move no flip");
            Check(!m.IsDragging, "case3 !IsDragging");
            click = m.OnTouchUp(104, 104);
            Check(click, "case3 click=true");

            // 4. 阈值边界：dx²+dy² 严格大于 DragThresholdPx² 才翻。
            m = new TouchInputMapper();
            m.DragThresholdPx = 10f;
            m.OnTouchDown(0, 0);
            flipped = m.OnTouchMove(10, 0); // 100 不>100 → 不翻
            Check(!flipped, "case4 boundary(=th) no flip");
            flipped = m.OnTouchMove(11, 0); // 121 > 100 → 翻
            Check(flipped, "case4 beyond(>th) flips");

            // 5. 已拖拽后 move 不重复翻转。
            m = new TouchInputMapper();
            m.OnTouchDown(0, 0);
            m.OnTouchMove(50, 0);
            bool flip2 = m.OnTouchMove(100, 0);
            Check(!flip2, "case5 already-dragging no re-flip");

            // 6. 取消中止：清除状态，后续非法 Up 不产生 Click。
            m = new TouchInputMapper();
            m.OnTouchDown(100, 100);
            m.OnTouchCancel();
            Check(!m.IsTouching && !m.IsDragging, "case6 cancel clears");
            click = m.OnTouchUp(100, 100);
            Check(!click, "case6 up after cancel click=false");

            // 7. 未触摸直接 Up/Move 忽略（非法序列防御）。
            m = new TouchInputMapper();
            click = m.OnTouchUp(0, 0);
            Check(!click, "case7 up without down click=false");
            bool moved = m.OnTouchMove(50, 50);
            Check(!moved && !m.IsTouching && !m.IsDragging, "case7 move without down ignored");

            // 8. 触摸态在 Up 后复位。
            m = new TouchInputMapper();
            m.OnTouchDown(1, 1);
            m.OnTouchUp(1, 1);
            Check(!m.IsTouching && !m.IsDragging, "case8 state reset after up");

            if (fail == 0)
            {
                Console.WriteLine("[touchverify] PASS cases=8");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[touchverify] FAIL cases=8 fail={fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
