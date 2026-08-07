using System;
using Client;
using UnityEngine;

namespace Crystal.Client.Rendering
{
    // 阶段8 0 项：移动输入契约手势分类器（全窗口统一手感，禁止各自为政）。
    // 契约常量（docs/mobile-ui-spec.md §6 输入契约）：单击 ≤200ms、长按 500ms、拖动超 DragThresholdPx、
    // 双击间隔 ≤300ms；摇杆死区/奔跑阈值归属 TouchJoystick.DeadZonePx=12 / RunThresholdPx=64（DRY 不复定义）。
    // 后续对话框触控一律经 MobileInput 识别 Tap/LongPress/Drag/DoubleTap，不各自定手感；
    // 探针喂确定性时钟序列断言时序边界（MobileUiVerify case6）。
    public enum GestureKind
    {
        None,
        Tap,
        LongPress,
        Drag,
        DoubleTap
    }

    public sealed class MobileInput
    {
        public const long TapMaxMs = 200;            // 单击上限：Up-Up 内、位移未超阈值 → Tap
        public const long LongPressMs = 500;         // 长按下限：按住超此值 → LongPress
        public const float DragThresholdPx = 10f;    // 拖动阈值：位移超此 → Drag
        public const long DoubleTapIntervalMs = 300; // 双击间隔：两次 Tap 的 Up-Up 间隔 ≤ 此 → DoubleTap
        public const float DoubleTapTolerancePx = 32f; // 双击位置容差（两次 Tap 落点偏离 ≤ 此视为同键）

        // 时钟注入（探针确定性；运行时 = CMain.Time，与 MobileHud/MobileCombat 同源）。
        public Func<long> Now = () => CMain.Time;

        public GestureKind LastGesture { get; private set; } = GestureKind.None;
        public Vector2 LastPosition { get; private set; }
        public float LastDurationMs { get; private set; }

        bool _down;
        long _downTime;
        Vector2 _downPos;
        float _maxDrag;
        long _lastTapUpTime;
        Vector2 _lastTapPos;

        // 复位手势状态（探针用例隔离：清除双击窗与按下锚点，避免相邻用例手势链式污染）。
        public void Reset()
        {
            _down = false;
            _lastTapUpTime = 0;
            _lastTapPos = Vector2.zero;
            LastGesture = GestureKind.None;
        }

        // 喂入触摸事件（单指手势跟踪：Down 记锚点，Move 累计最大位移，Up 按 契约优先级判定）。
        public void OnTouch(int id, JoystickPhase phase, Vector2 pos)
        {
            if (phase == JoystickPhase.Down)
            {
                _down = true;
                _downTime = Now();
                _downPos = pos;
                _maxDrag = 0f;
                LastGesture = GestureKind.None;
                return;
            }
            if (!_down) return; // 只跟踪已按下的手指（Began 丢失的 Ended 不判定）
            if (phase == JoystickPhase.Move)
            {
                _maxDrag = Mathf.Max(_maxDrag, (pos - _downPos).magnitude);
                return;
            }
            if (phase == JoystickPhase.Cancel)
            {
                _down = false;
                LastGesture = GestureKind.None;
                return;
            }
            // Up：按 拖动 > 长按 > 双击/单击 优先级判定。
            long upTime = Now();
            _down = false;
            LastDurationMs = upTime - _downTime;
            float disp = (pos - _downPos).magnitude;
            _maxDrag = Mathf.Max(_maxDrag, disp);
            if (_maxDrag > DragThresholdPx)
            {
                LastGesture = GestureKind.Drag;
                LastPosition = pos;
                return;
            }
            if (LastDurationMs >= LongPressMs)
            {
                LastGesture = GestureKind.LongPress;
                LastPosition = pos;
                return;
            }
            bool withinInterval = upTime - _lastTapUpTime <= DoubleTapIntervalMs;
            bool nearLastTap = (pos - _lastTapPos).magnitude <= DoubleTapTolerancePx;
            _lastTapUpTime = upTime;
            _lastTapPos = pos;
            LastGesture = withinInterval && nearLastTap ? GestureKind.DoubleTap : GestureKind.Tap;
            LastPosition = pos;
        }
    }
}
