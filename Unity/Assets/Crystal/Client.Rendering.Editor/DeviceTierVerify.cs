using Crystal.Client.Assets;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // 阶段7 第 5 项 + 阶段8 8-10 探针（batchmode）：DeviceCapability 分级决策表 + 档位配置 + 应用映射。
    // 场景1：注入 5 组 profile 断言 Classify（高/中/低内存/低GPU/边界中）。
    // 场景2：For 三档配置单调性（渲染缩放/阴影质量/纹理等级）。
    // 场景3：TierQualityApplier.Apply 映射到真实消费点（GameRuntime.RenderScale/DrawDistanceScale、
    //   AtlasLibrary.TextureLevel）+ 热重载切换 + 幂等。探针为独立 Editor 进程，静态消费点不跨进程污染。
    public static class DeviceTierVerify
    {
        public static void Run()
        {
            try
            {
                int cases = 0;
                bool ok = ClassifyCase(ref cases) & QualityCase(ref cases) & ApplyCase(ref cases);
                var real = DeviceCapability.SampleUnity();
                Debug.Log($"[device-tier] unity sample cores={real.CpuCores} mem={real.SystemMemoryMB}MB gpuMem={real.GpuMemoryMB}MB maxTex={real.MaxTextureSize} => {DeviceCapability.Classify(real)}");
                Debug.Log($"[device-tier] {(ok ? "PASS" : "FAIL")} cases={cases}");
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[device-tier] exception {ex}");
                EditorApplication.Exit(1);
            }
        }

        static bool ClassifyCase(ref int cases)
        {
            bool ok = true;
            ok &= Check(DeviceCapability.Classify(P(16384, 16, 8192, 16384)) == DeviceTier.High, "高配=>High");
            ok &= Check(DeviceCapability.Classify(P(6144, 8, 3072, 8192)) == DeviceTier.Medium, "中配=>Medium");
            ok &= Check(DeviceCapability.Classify(P(2048, 4, 1024, 4096)) == DeviceTier.Low, "低内存=>Low");
            ok &= Check(DeviceCapability.Classify(P(4096, 6, 512, 2048)) == DeviceTier.Low, "低GPU=>Low");
            ok &= Check(DeviceCapability.Classify(P(4096, 6, 2048, 4096)) == DeviceTier.Medium, "边界=>Medium");
            Debug.Log($"[device-tier] classify-case ok={ok}");
            if (ok) cases++;
            return ok;
        }

        static bool QualityCase(ref int cases)
        {
            var h = DeviceCapability.For(DeviceTier.High);
            var m = DeviceCapability.For(DeviceTier.Medium);
            var l = DeviceCapability.For(DeviceTier.Low);
            bool ok = true;
            ok &= Check(h.RenderScale >= m.RenderScale && m.RenderScale >= l.RenderScale, "渲染缩放单调");
            ok &= Check(h.ShadowQuality >= m.ShadowQuality && m.ShadowQuality >= l.ShadowQuality, "阴影质量单调");
            ok &= Check(h.TextureLevel <= m.TextureLevel && m.TextureLevel <= l.TextureLevel, "纹理等级单调");
            ok &= Check(h.ParticleScale >= m.ParticleScale && m.ParticleScale >= l.ParticleScale && h.DrawDistanceScale >= l.DrawDistanceScale, "粒子/绘制距离单调");
            Debug.Log($"[device-tier] quality-case ok={ok}");
            if (ok) cases++;
            return ok;
        }

        static DeviceProfile P(int mem, int cores, int gpuMem, int maxTex) =>
            new DeviceProfile { SystemMemoryMB = mem, CpuCores = cores, GpuMemoryMB = gpuMem, MaxTextureSize = maxTex };

        // 场景3（8-10）：TierQualityApplier.Apply 映射到真实消费点 + 热重载 + 幂等。
        static bool ApplyCase(ref int cases)
        {
            bool ok = true;
            TierQualityApplier.Apply(DeviceTier.Low);
            ok &= Check(GameRuntime.RenderScale == 0.7f && GameRuntime.DrawDistanceScale == 0.6f
                && AtlasLibrary.TextureLevel == 2, "L 档 → 消费点 renderScale=0.7 drawDist=0.6 textureLevel=2");
            TierQualityApplier.Apply(DeviceTier.High);
            ok &= Check(GameRuntime.RenderScale == 1.0f && GameRuntime.DrawDistanceScale == 1.0f
                && AtlasLibrary.TextureLevel == 0, "H 档 → 消费点 renderScale=1.0 drawDist=1.0 textureLevel=0");
            TierQualityApplier.Apply(DeviceTier.Medium);
            ok &= Check(GameRuntime.RenderScale == 0.85f && GameRuntime.DrawDistanceScale == 0.8f
                && AtlasLibrary.TextureLevel == 1, "M 档 → 消费点 renderScale=0.85 drawDist=0.8 textureLevel=1");
            // 热重载：L→H 直接切换（值覆盖，下帧生效）
            TierQualityApplier.Apply(DeviceTier.Low);
            TierQualityApplier.Apply(DeviceTier.High);
            ok &= Check(GameRuntime.RenderScale == 1.0f && AtlasLibrary.TextureLevel == 0,
                "热重载 L→H → 消费点刷新");
            // 幂等：重复 Apply 同档无副作用
            TierQualityApplier.Apply(DeviceTier.High);
            ok &= Check(GameRuntime.RenderScale == 1.0f && TierQualityApplier.CurrentTier == DeviceTier.High,
                "幂等：重复 Apply H 档稳定");
            // 消费点默认值（未分级前）与 H 档一致，探针顺序断言已覆盖；还原为 H（不污染后续同进程逻辑）
            TierQualityApplier.Apply(DeviceTier.High);
            Debug.Log($"[device-tier] apply-case ok={ok}");
            if (ok) cases++;
            return ok;
        }

        static bool Check(bool cond, string label)
        {
            Debug.Log($"[device-tier]   {label}: {(cond ? "ok" : "FAIL")}");
            return cond;
        }
    }
}
