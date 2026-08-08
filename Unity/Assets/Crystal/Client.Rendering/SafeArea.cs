using System;
using UnityEngine;

namespace Crystal.Client.Rendering
{
    // 阶段8 第5项 增量1：安全区适配——刘海/Home indicator 遮罩下移动 UI 锚点偏移的单一来源。
    // 从 Unity Screen.safeArea（左下原点）换算四边距屏幕边缘 px（UI 空间：左上原点，top=距上缘/bottom=距下缘），
    // Provider seam 注入（探针 stub 假刘海；batchmode 下 Screen.safeArea=全屏 → inset 全 0 不破坏现有布局契约）。
    // 消费方（MobileHud 血条/攻击按钮、MobileBag 右上按钮列及派生按钮）一律读本类 inset 做锚点偏移，
    // 禁止各自硬编码 safeArea 读数（对齐 8-0 适配层单一扇出）。inset 变化伴随分辨率/旋转变化 →
    // 消费方 SetScreen 触发重算时读到最新值（软键盘弹出等局部遮挡留给 8-5-2 聊天 UI）。
    public static class SafeArea
    {
        // 注入 seam：返回 (left, top, right, bottom) 距屏幕边缘 px。默认 Unity 原生安全区。
        public static Func<Vector4> Provider = () =>
        {
            var sa = Screen.safeArea;
            return new Vector4(
                sa.x,                                   // left：距左缘
                Screen.height - (sa.y + sa.height),     // top：距上缘（safeArea 左下原点，y+height=顶部）
                Screen.width - (sa.x + sa.width),       // right：距右缘
                sa.y);                                  // bottom：距下缘
        };

        public static Vector4 Insets => Provider();
        public static float Left => Insets.x;
        public static float Top => Insets.y;
        public static float Right => Insets.z;
        public static float Bottom => Insets.w;
    }
}
