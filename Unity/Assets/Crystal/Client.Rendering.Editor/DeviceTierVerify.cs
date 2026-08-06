using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // 阶段7 第 5 项探针（batchmode）：DeviceCapability 分级决策表 + 档位配置。
    // 场景1：注入 5 组 profile 断言 Classify（高/中/低内存/低GPU/边界中）。
    // 场景2：For 三档配置单调性（渲染缩放/阴影质量/纹理等级）。
    // 场景3：SampleUnity 真实采样输出当前设备档（信息性，不参与 pass）。
    public static class DeviceTierVerify
    {
        public static void Run()
        {
            try
            {
                int cases = 0;
                bool ok = ClassifyCase(ref cases) & QualityCase(ref cases);
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

        static bool Check(bool cond, string label)
        {
            Debug.Log($"[device-tier]   {label}: {(cond ? "ok" : "FAIL")}");
            return cond;
        }
    }
}
