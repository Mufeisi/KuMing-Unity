namespace Crystal.Client.Rendering
{
    // 阶段7 第 3 项（触控 Input Adapter）：触摸手势 → Mir 鼠标语义翻译器（纯逻辑层）。
    // 单指主触摸：Down 锁定、Move 超拖拽阈值转拖拽、Up 未拖拽=Click、Cancel 中止。
    // 与 Unity Input 解耦（不依赖 Input.touches/TouchPhase），可确定性单测（TouchInputVerify）。
    // Adapter 把 Unity 触摸流映射到 OnTouchXxx，并据此分发 CMain.MPoint + GameScene.Scene 鼠标事件。
    public sealed class TouchInputMapper
    {
        // 拖拽判定阈值（逻辑像素）：位移超过此值视为拖拽，Up 不再触发 Click。
        // 统一手感（8-0 输入契约）：单一来源 MobileInput.DragThresholdPx，禁各自为政。
        public float DragThresholdPx = MobileInput.DragThresholdPx;

        bool _touching;
        bool _dragging;
        float _startX, _startY;

        public bool IsTouching => _touching;
        public bool IsDragging => _dragging;

        // 按下：锁定起点，重置拖拽态。
        public void OnTouchDown(float x, float y)
        {
            _touching = true;
            _dragging = false;
            _startX = x;
            _startY = y;
        }

        // 移动：未拖拽且位移超阈值时转拖拽（返回 true 表示本次调用发生了拖拽翻转）。
        // 未处于按下状态（非法序列）返回 false 且不改变状态。
        public bool OnTouchMove(float x, float y)
        {
            if (!_touching || _dragging) return false;
            float dx = x - _startX;
            float dy = y - _startY;
            if (dx * dx + dy * dy > DragThresholdPx * DragThresholdPx)
            {
                _dragging = true;
                return true;
            }
            return false;
        }

        // 抬起：返回是否 Click（未拖拽）；清除触摸/拖拽态。
        public bool OnTouchUp(float x, float y)
        {
            bool click = _touching && !_dragging;
            _touching = false;
            _dragging = false;
            return click;
        }

        // 取消（系统中断）：清除触摸/拖拽态，不产生 Click。
        public void OnTouchCancel()
        {
            _touching = false;
            _dragging = false;
        }
    }
}
