using UnityEngine;

namespace Crystal.Client.Rendering
{
    // 触摸阶段（由 Adapter 从 Unity TouchPhase 映射，与 Input.touches 解耦便于确定性单测）。
    public enum JoystickPhase { Down, Move, Up, Cancel }

    // 阶段8 第1项（战斗触控 HUD）触控移动摇杆（纯逻辑层）：
    // 浮动摇杆——触摸按下处即底盘原点，拖拽向量按 8 向量化 + 死区/奔跑阈值。
    // 与 Unity Input 解耦（不依赖 Input.touches/TouchPhase），可确定性单测（TouchJoystickVerify）。
    // Adapter（MobileBootstrap.PollJoystick）把 Input.touches 映射为 OnTouch 喂入。
    // 方向映射：屏幕 y 向上（Unity 坐标），拖右=Right(2)、拖上=Up(0)、拖左=Left(6)、拖下=Down(4)，
    // 对角走 45° 格（顺时针从 Up 起）。LastDir/LastRun 在松手复位后保留，供 Adapter 快速轻滑补一步。
    public sealed class TouchJoystick
    {
        public const float DeadZonePx = 12f;      // 位移低于此不产生移动（防手抖）
        public const float RunThresholdPx = 64f;  // 位移达到此切跑（C.Run）

        int _touchId = -1;
        Vector2 _origin, _knob;
        bool _active, _moving, _run;
        MirDirection _dir = MirDirection.Up;

        public bool Active => _active;              // 摇杆被按住
        public bool Moving => _moving;              // 拖拽超死区（有移动意图）
        public bool Run => _run;                    // 超奔跑阈值
        public MirDirection Dir => _dir;            // 8 向量化方向（Moving 时有效）
        public MirDirection LastDir { get; private set; } = MirDirection.Up; // 最近有效方向（复位后保留）
        public bool LastRun { get; private set; }   // 最近有效跑态（复位后保留）
        public bool ReleasedWithIntent { get; private set; } // 松手时位移超死区（Adapter 补步依据，Ended 无 Moved 事件也触发）
        public void ClearRelease() => ReleasedWithIntent = false; // Adapter 消费补步标记后清除（防重复补步）
        public Vector2 Origin => _origin;           // 底盘位置（HUD 渲染锚点）
        public Vector2 Knob => _knob;               // 摇杆头位置（HUD 渲染锚点）

        // 按下：锁接触点（单指——已激活时新 Down 忽略，防多指抢锁），原点定在触摸处，重置移动态。
        public void OnTouch(int id, JoystickPhase phase, Vector2 pos)
        {
            if (phase == JoystickPhase.Down)
            {
                if (_active) return; // 已有主指，后续触点忽略
                _touchId = id;
                _origin = pos;
                _knob = pos;
                _active = true;
                ReleasedWithIntent = false; // 新按下清上次松手补步标记
                UpdateVector(Vector2.zero);
            }
            else if (_active && id == _touchId)
            {
                switch (phase)
                {
                    case JoystickPhase.Move:
                        _knob = pos;
                        UpdateVector(pos - _origin);
                        break;
                    case JoystickPhase.Up:
                    case JoystickPhase.Cancel:
                        // 松手补步依据：用 End 位置与起点位移判定移动意图（低帧率下 Moved 事件可整帧丢失，
                        // 但 Ended 一定到达；位移超死区则记 ReleasedWithIntent，Adapter 据此补发一步）。
                        UpdateVector(pos - _origin);
                        ReleasedWithIntent = _moving;
                        Reset();
                        break;
                }
            }
            // 非当前触摸的 Move/Up 忽略（单指摇杆）；Down 时已被首个触点锁定。
        }

        void UpdateVector(Vector2 v)
        {
            float mag = v.magnitude;
            _moving = mag >= DeadZonePx;
            _run = mag >= RunThresholdPx;
            if (!_moving) return; // 拖回死区内保持上次方向，不抖动
            _dir = Quantize(v);
            LastDir = _dir;
            LastRun = _run;
        }

        // 拖拽向量 → 8 向 MirDirection：标准角（+x 逆时针）→ 顺时针从 Up 起 45°/格。
        static MirDirection Quantize(Vector2 v)
        {
            float deg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg; // 标准角 [-180,180]
            if (deg < 0) deg += 360f;
            float cw = (90f - deg) % 360f; // 转成顺时针从 Up 起
            if (cw < 0) cw += 360f;
            return (MirDirection)(Mathf.RoundToInt(cw / 45f) % 8);
        }

        void Reset()
        {
            _active = false;
            _moving = false;
            _run = false;
            _touchId = -1;
            _dir = MirDirection.Up;
        }
    }
}
