using UnityEngine;

namespace Crystal.Client.Rendering
{
    // sanduan Light.shader 光源时间脉冲（火焰/光源闪烁语义）：
    //   brightness = 0.975 + 0.025·sin(9t)，alpha = 0.975 + 0.05·sin(9t)（t = 秒）。
    // 运行时 DrawLights 每帧以当前时间调制光源 tint（Modulate）；本类为脉冲公式单一事实来源，
    // LightRender 探针的 CPU 期望与 GPU 绘制同走此公式。alpha 峰值 1.025 在 GPU 混色输出
    // 处饱和 1，Modulate 显式 clamp 保证字节级确定性（0.925..1.0 有效区间）。
    public static class LightPulse
    {
        public static float Brightness(float timeSec) => 0.975f + 0.025f * Mathf.Sin(timeSec * 9f);

        public static float Alpha(float timeSec)
        {
            float a = 0.975f + 0.05f * Mathf.Sin(timeSec * 9f);
            return a > 1f ? 1f : a;
        }

        // 光源绘制色：tint.rgb × brightness，alpha = Alpha(t)（additive 阶段 src.a=grad.a×此值）。
        public static Color Modulate(Color tint, float timeSec)
        {
            float b = Brightness(timeSec);
            return new Color(tint.r * b, tint.g * b, tint.b * b, Alpha(timeSec));
        }
    }
}
