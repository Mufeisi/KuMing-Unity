using System;
using Client.MirControls;
using Client.MirScenes;
using UnityEngine;
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Client.Rendering
{
    // 阶段8 0 项：移动 UI 适配层——触摸「设备空间 → UI 空间」唯一翻转点 + 命中/互斥/返回键/滚动冲突。
    // 从 MobileBootstrap 提取（UiHitTest/坐标翻转/触摸互斥），统一按钮命中尺寸，供后续所有对话框触控任务复用，
    // 禁止各窗口自实现 hit-test/翻转/手感（移动输入契约见 MobileInput + docs/mobile-ui-spec.md）。
    //
    // 坐标约定（X-1 touchdiag v2 实证，9 点验证）：
    //   设备空间（raw，Unity Input.touch.position）= backbuffer 像素系，左下原点 y 上（1280×720）。
    //   UI 空间（ui）= MirControl rect / MobileBag / MobileHud / 渲染布局，左上原点 y 下。
    //   唯一翻转点 ToUi(raw)=(raw.x, ScreenH-raw.y)。命中类消费者一律收 ui；TouchJoystick 例外收 raw——
    //   其方向量化以 y 上为正（TouchJoystickVerify case5 原生 y 上），翻转让上下反转，故「提取不改行为」。
    public static class MobileUiAdapter
    {
        // backbuffer 尺寸（MobileBootstrap 每帧 SetScreen 同步；缺省与模拟器 1280×720 对齐）。
        public static int ScreenW = 1280;
        public static int ScreenH = 720;

        // 统一按钮最小触控尺寸（px，Apple/Google 触控指南下限）。现有按钮合规：背包 72×54、攻击 r60；
        // 后续新窗口按钮一律经 TouchRect 保证 ≥ MinTouchSize，不各自硬编码更小命中区。
        public const float MinTouchSize = 44f;

        // 设备空间 → UI 空间（唯一翻转点；y 镜像 bug 根因在此修正）。
        public static Vector2 ToUi(Vector2 raw) => new Vector2(raw.x, ScreenH - raw.y);
        public static MPoint ToUiPoint(Vector2 raw) => new MPoint((int)raw.x, ScreenH - (int)raw.y);
        public static MPoint ToUiPoint(float x, float y) => new MPoint((int)x, ScreenH - (int)y);

        // 触控尺寸归一：给定期望中心与尺寸，短边不足 MinTouchSize 时以中心外扩补齐。
        public static Rect TouchRect(Vector2 center, Vector2 size)
        {
            var w = Mathf.Max(size.x, MinTouchSize);
            var h = Mathf.Max(size.y, MinTouchSize);
            return new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h);
        }

        // ---- 对话框命中（提取自 MobileBootstrap.UiHitTest；y 翻转已前移到 ToUi）----
        // 注入钩子：探针 stub 掉真实 GameScene 树；运行时默认取 GameScene.Scene 根（递归含 MainDialog/各对话框）。
        public static Func<MirControl> DialogRoot = () =>
        {
            var scene = GameScene.Scene;
            return scene != null ? scene : null; // GameScene : MirControl 隐式上转
        };

        public static bool UiHitTest(MPoint p) => UiHitTest(DialogRoot(), p);

        public static bool UiHitTest(MirControl ctrl, MPoint p)
        {
            if (ctrl == null || !ctrl.Visible) return false;
            if (ctrl.DisplayRectangle.Contains(p)) return true;
            if (ctrl.Controls != null)
                for (int i = 0; i < ctrl.Controls.Count; i++)
                    if (UiHitTest(ctrl.Controls[i], p)) return true;
            return false;
        }

        // ---- 触摸互斥路由（提取自 MobileBootstrap.PollJoystick 分发序）----
        // 序：① UI 命中类消费者（背包按钮）先消费；② 面板打开期间其余触摸不喂摇杆/HUD；③ Down 落在可见
        // 对话框区域（含滚动区）不激活摇杆；④ 放行后摇杆收 raw、HUD 收 ui。
        public struct TouchRoute
        {
            public Func<int, JoystickPhase, Vector2, bool> UiConsumer; // 命中类按钮：true=已消费
            public bool PanelOpen;                                     // 面板打开：其余触摸不喂摇杆/HUD
            public Func<MPoint, bool> DialogHit;                       // 可见对话框区域命中（注入，探针 stub）
            public Action<int, JoystickPhase, Vector2> Joystick;       // 摇杆（raw 空间）
            public Action<int, JoystickPhase, Vector2> Hud;            // HUD（ui 空间）
        }

        public static void RouteTouch(TouchRoute r, int fingerId, JoystickPhase phase, Vector2 raw)
        {
            var ui = ToUi(raw);
            if (r.UiConsumer != null && r.UiConsumer(fingerId, phase, ui)) return;
            if (r.PanelOpen) return;
            if (phase == JoystickPhase.Down && r.DialogHit != null && r.DialogHit(ToUiPoint(raw)))
            {
                // 8-5-1 软键盘桥触控接线：Down 落在可见对话框内 → 命中 MirTextBox 则聚焦弹软键盘
                // （MirTextBox.OnMouseDown 已 SetFocus 画光标，本步只补 TouchScreenKeyboard）。
                TryFocusTextBox(ToUiPoint(raw));
                return;
            }
            if (r.Joystick != null) r.Joystick(fingerId, phase, raw);
            if (r.Hud != null) r.Hud(fingerId, phase, ui);
        }

        // ---- 软键盘触控聚焦（8-5-1）----
        // Down 命中可见且启用的 MirTextBox → SoftKeyboardBridge.Focus（Open TouchScreenKeyboard，
        // 初始文本/密码/最大长度走框属性）；不可见/禁用跳过。递归子树（对话框内输入框）。
        // 启用态读 InputTextBox.Enabled（MirControl.Enabled getter 为 internal，跨程序集不可读）。
        public static bool TryFocusTextBox(MPoint p) => TryFocusTextBox(DialogRoot(), p);

        public static bool TryFocusTextBox(MirControl ctrl, MPoint p)
        {
            if (ctrl == null || !ctrl.Visible) return false;
            if (ctrl is MirTextBox tb
                && tb.TextBox != null && !tb.TextBox.IsDisposed && tb.TextBox.Enabled
                && tb.DisplayRectangle.Contains(p))
            {
                SoftKeyboardBridge.Focus(tb);
                return true;
            }
            if (ctrl.Controls != null)
                for (int i = 0; i < ctrl.Controls.Count; i++)
                    if (TryFocusTextBox(ctrl.Controls[i], p)) return true;
            return false;
        }

        // ---- 返回键（Android Back → 关顶层对话框）----
        // 检测与处理分离，均可注入（探针 stub；运行时默认 Escape=Android Back，处理由调用方接顶层对话框）。
        public static Func<bool> IsBackPressed = () => Input.GetKeyDown(KeyCode.Escape);
        public static Func<bool> BackHandler; // 返回 true=已消费（对话框已关/已有处理），不冒泡

        public static void PollBackKey()
        {
            if (IsBackPressed() && BackHandler != null && BackHandler()) { /* 已消费 */ }
        }

        // ---- 滚动冲突规则（对话框内滚动 vs 摇杆移动互斥）----
        // 逐字移植的 MirControl 无通用 Scrollable 成员（滚动为各控件自带，保逐字保真不改），故以可注入谓词为
        // seam：有滚动区的对话框/控件经 IsScrollable 注册；其 DisplayRectangle 区域内的 Down 由对话框消费
        // （走 DialogHit，不激活摇杆）。阶段8 滚动窗口落地时接线，当前缺省无滚动控件。
        public static Func<MirControl, bool> IsScrollable = ctrl => false;

        public static bool ScrollConflict(MirControl ctrl, MPoint p)
        {
            if (ctrl == null || !ctrl.Visible) return false;
            if (IsScrollable(ctrl) && ctrl.DisplayRectangle.Contains(p)) return true;
            if (ctrl.Controls != null)
                for (int i = 0; i < ctrl.Controls.Count; i++)
                    if (ScrollConflict(ctrl.Controls[i], p)) return true;
            return false;
        }
    }
}
