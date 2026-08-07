using System;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第1项（战斗触控 HUD）摇杆逻辑层验证：喂确定性触摸序列断言 TouchJoystick 语义。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.TouchJoystickVerify.Run -quit
    // 断言：死区/奔跑阈值/8 向量化/多指忽略/松手补步（End 位移判定 ReleasedWithIntent）方向保留/复位；全过输出 [joystickverify] PASS exit 0。
    public static class TouchJoystickVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[joystickverify] FAIL {what}"); }
        }

        public static void Run()
        {
            // 1. 按下：Active，未超死区不移动。
            var j = new TouchJoystick();
            j.OnTouch(0, JoystickPhase.Down, new Vector2(200, 300));
            Check(j.Active, "case1 down active");
            Check(!j.Moving, "case1 down not moving");

            // 2. 死区内拖拽（位移 5 < 12）→ 不移动。
            j.OnTouch(0, JoystickPhase.Move, new Vector2(205, 300));
            Check(j.Active && !j.Moving, "case2 below deadzone not moving");

            // 3. 超死区拖右（位移 50 ≥ 12 且 < 64）→ Moving + Dir=Right(2)，未超奔跑阈值。
            j.OnTouch(0, JoystickPhase.Move, new Vector2(250, 300));
            Check(j.Moving, "case3 moving");
            Check(j.Dir == MirDirection.Right, "case3 dir right");
            Check(!j.Run, "case3 not run");

            // 4. 继续拖右（位移 200 ≥ 64）→ Run。
            j.OnTouch(0, JoystickPhase.Move, new Vector2(400, 300));
            Check(j.Run, "case4 run");

            // 5. 8 向量化：拖上/左/下 + 4 对角 → 对应 MirDirection（原点 200,300）。
            j.OnTouch(0, JoystickPhase.Move, new Vector2(200, 400));
            Check(j.Dir == MirDirection.Up, "case5 dir up");
            j.OnTouch(0, JoystickPhase.Move, new Vector2(100, 300));
            Check(j.Dir == MirDirection.Left, "case5 dir left");
            j.OnTouch(0, JoystickPhase.Move, new Vector2(200, 200));
            Check(j.Dir == MirDirection.Down, "case5 dir down");
            j.OnTouch(0, JoystickPhase.Move, new Vector2(300, 400));  // up-right (100,100)
            Check(j.Dir == MirDirection.UpRight, "case5 dir up-right");
            j.OnTouch(0, JoystickPhase.Move, new Vector2(300, 200));  // down-right (100,-100)
            Check(j.Dir == MirDirection.DownRight, "case5 dir down-right");
            j.OnTouch(0, JoystickPhase.Move, new Vector2(100, 200));  // down-left (-200,-100)
            Check(j.Dir == MirDirection.DownLeft, "case5 dir down-left");
            j.OnTouch(0, JoystickPhase.Move, new Vector2(100, 400));  // up-left (-200,100)
            Check(j.Dir == MirDirection.UpLeft, "case5 dir up-left");

            // 6. 多指忽略：第二指按下/移动不影响主指。
            j = new TouchJoystick();
            j.OnTouch(1, JoystickPhase.Down, new Vector2(400, 400));
            j.OnTouch(2, JoystickPhase.Down, new Vector2(800, 400)); // 第二指按下
            j.OnTouch(2, JoystickPhase.Move, new Vector2(900, 500)); // 第二指拖拽
            Check(j.Active && !j.Moving, "case6 second finger ignored");
            j.OnTouch(1, JoystickPhase.Move, new Vector2(300, 400)); // 主指拖左
            Check(j.Moving && j.Dir == MirDirection.Left, "case6 primary finger moves");

            // 7. 抬起复位：Active=false，但 LastDir/LastRun 保留 + ReleasedWithIntent 由 End 位移判定。
            j = new TouchJoystick();
            j.OnTouch(0, JoystickPhase.Down, new Vector2(100, 100));
            j.OnTouch(0, JoystickPhase.Move, new Vector2(300, 100)); // 右 200 → run
            j.OnTouch(0, JoystickPhase.Up, new Vector2(300, 100));
            Check(!j.Active && !j.Moving, "case7 up resets active");
            Check(j.LastDir == MirDirection.Right, "case7 last dir preserved");
            Check(j.LastRun, "case7 last run preserved");
            Check(j.ReleasedWithIntent, "case7 up with displacement sets intent");
            j.ClearRelease();
            Check(!j.ReleasedWithIntent, "case7 clear release");
            j.OnTouch(0, JoystickPhase.Down, new Vector2(200, 200)); // 新按下清补步标记
            Check(!j.ReleasedWithIntent, "case7 new down clears intent");

            // 8. 取消复位。
            j = new TouchJoystick();
            j.OnTouch(0, JoystickPhase.Down, new Vector2(100, 100));
            j.OnTouch(0, JoystickPhase.Cancel, new Vector2(100, 100));
            Check(!j.Active && !j.Moving, "case8 cancel resets");

            // 9. 轻点（Down→Up 无拖拽）→ 从未进入移动态，ReleasedWithIntent=false。
            j = new TouchJoystick();
            j.OnTouch(0, JoystickPhase.Down, new Vector2(100, 100));
            j.OnTouch(0, JoystickPhase.Up, new Vector2(100, 100));
            Check(!j.Active && !j.Moving, "case9 tap never moves");
            Check(!j.ReleasedWithIntent, "case9 tap no intent");

            // 10. 死区边界：恰等于 DeadZonePx → 移动（>=）；恰小 → 不移动。
            j = new TouchJoystick();
            j.OnTouch(0, JoystickPhase.Down, new Vector2(0, 0));
            j.OnTouch(0, JoystickPhase.Move, new Vector2(TouchJoystick.DeadZonePx, 0));
            Check(j.Moving, "case10 boundary(=deadzone) moving");
            j = new TouchJoystick();
            j.OnTouch(0, JoystickPhase.Down, new Vector2(0, 0));
            j.OnTouch(0, JoystickPhase.Move, new Vector2(TouchJoystick.DeadZonePx - 0.1f, 0));
            Check(!j.Moving, "case10 below deadzone not moving");

            // 11. 拖回死区保持方向（不抖动）：Moving=false 但 Dir 保留上次有效值。
            j = new TouchJoystick();
            j.OnTouch(0, JoystickPhase.Down, new Vector2(100, 100));
            j.OnTouch(0, JoystickPhase.Move, new Vector2(300, 100)); // right
            j.OnTouch(0, JoystickPhase.Move, new Vector2(105, 100)); // 拖回死区内
            Check(!j.Moving, "case11 back-to-deadzone stops moving");
            Check(j.Dir == MirDirection.Right, "case11 dir retained");

            // 12. 模拟器丢帧场景：Down→Up 间无 Move 事件（低帧率 Moved 整帧丢失），
            //     End 位移仍判移动意图 → ReleasedWithIntent=true + LastDir/LastRun 正确（Adapter 据此补步）。
            j = new TouchJoystick();
            j.OnTouch(0, JoystickPhase.Down, new Vector2(100, 100));
            j.OnTouch(0, JoystickPhase.Up, new Vector2(300, 100)); // 无 Move，位移 200 右
            Check(!j.Active, "case12 down-up resets active");
            Check(j.ReleasedWithIntent, "case12 end-displacement sets intent");
            Check(j.LastDir == MirDirection.Right, "case12 last dir from end pos");
            Check(j.LastRun, "case12 last run from end pos");

            if (_fail == 0)
            {
                Console.WriteLine("[joystickverify] PASS cases=12");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[joystickverify] FAIL cases=12 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
