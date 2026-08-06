using Crystal.Client.Core.MirMath;

namespace Client.MirGraphics
{
    // DXManager 的 Client.Core seam（占位）：签名对齐旧客户端（Client/MirGraphics/DXManager.cs），空实现。
    // 真实渲染接驳时替换空体；RadarTexture/PoisonDotBackground 为毒标绘制的小纹理。
    public static class DXManager
    {
        public static Texture RadarTexture;
        public static Texture PoisonDotBackground;

        public static float Opacity = 1.0F;
        public static bool Blending;
        public static bool GrayScale;

        public static void SetGrayscale(bool value) { }
        public static void SetOpacity(float value) { }
        public static void SetBlend(bool value, float rate = 1F) { }
        public static void Draw(Texture texture, Rectangle? sourceRect, Vector3? position, Color colour) { }
    }

    // SlimDX Texture 的 Client.Core 占位：仅作为 DXManager.Draw 的参数容器。
    public sealed class Texture
    {
        public bool Disposed { get; private set; }
        public void Dispose() { Disposed = true; }
    }
}
