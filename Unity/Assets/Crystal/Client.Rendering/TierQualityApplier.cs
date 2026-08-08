using Crystal.Client.Assets;
using UnityEngine;

namespace Crystal.Client.Rendering
{
    // 阶段8 8-10（性能分级动态降级落地）：DeviceCapability 分级 → TierQuality 应用到真实渲染栈。
    // 三维度真实生效（2D 精灵批渲染栈）：
    //   RenderScale       → GameRuntime.RenderScale（内部渲染分辨率缩放）
    //   DrawDistanceScale → GameRuntime.DrawDistanceScale（远处对象更新/绘制距离裁剪）
    //   TextureLevel      → AtlasLibrary.TextureLevel（页纹理最近邻降采样，仅新加载生效）
    // ParticleScale/ShadowQuality 配置保留暂不消费：2D 栈运行时未接入特效绘制（GameRuntime.Render
    // 走 lib.DrawIndex 不画 o.Draw() 特效）且无灯光/阴影管线——无可作用对象，接入特效绘制后补（backlog）。
    // 热重载：Apply(tier) 幂等可运行时重调（值直接覆盖静态消费点，下帧生效）。
    public static class TierQualityApplier
    {
        public static DeviceTier CurrentTier = DeviceTier.Medium;
        public static TierQuality Current = DeviceCapability.For(DeviceTier.Medium);

        // 启动采样：SampleUnity() → Classify → For → Apply。
        public static DeviceTier ApplyAuto() => Apply(DeviceCapability.Classify(DeviceCapability.SampleUnity()));

        public static DeviceTier Apply(DeviceTier tier)
        {
            CurrentTier = tier;
            Current = DeviceCapability.For(tier);
            GameRuntime.RenderScale = Current.RenderScale;
            GameRuntime.DrawDistanceScale = Current.DrawDistanceScale;
            AtlasLibrary.TextureLevel = Current.TextureLevel;
            Debug.Log($"[tier-quality] apply {tier} renderScale={Current.RenderScale} drawDist={Current.DrawDistanceScale} textureLevel={Current.TextureLevel}");
            return tier;
        }
    }
}
