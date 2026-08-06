using UnityEngine;

namespace Crystal.Client.Rendering
{
    // 阶段7 第 5 项（设备性能分级）：L/M/H 三级判定 + 每档质量配置（PRD 11.2）。
    // Classify 为纯函数决策表（探针注入假 profile 断言，无 Unity 依赖输入）；
    // SampleUnity 从 SystemInfo/Screen 采真实设备特征。
    // TierQuality 五维度对齐 PRD "内部渲染分辨率/粒子密度/阴影灯光/远处对象更新/纹理等级"，
    // 消费点（动态降级应用）属阶段8 移动渲染——本骨架建立分级机制与档位配置。

    public enum DeviceTier { Low, Medium, High }

    // 设备特征输入（纯数据；分类判定唯一依据）。
    public sealed class DeviceProfile
    {
        public int CpuCores;
        public int SystemMemoryMB;
        public int GpuMemoryMB;
        public int MaxTextureSize;
    }

    // 每档质量配置（动态降级五维度；数值为初始档位，运行期可再降）。
    public sealed class TierQuality
    {
        public float RenderScale;       // 内部渲染分辨率缩放（1.0/0.85/0.7）
        public float ParticleScale;     // 粒子密度系数（1.0/0.75/0.5）
        public int ShadowQuality;       // 阴影/灯光质量（2/1/0）
        public float DrawDistanceScale; // 远处对象更新频率/绘制距离（1.0/0.8/0.6）
        public int TextureLevel;        // 纹理等级（0 全/1 中/2 低）
    }

    public static class DeviceCapability
    {
        // 决策表：内存≥8GB 且核数≥8 且 GPU 上限高 → High；内存<4GB 或核数<6 或 GPU 明显低 → Low；其余 Medium。
        public static DeviceTier Classify(DeviceProfile p)
        {
            bool gpuHigh = p.MaxTextureSize >= 8192 && p.GpuMemoryMB >= 4096;
            bool gpuLow = p.MaxTextureSize < 4096 || p.GpuMemoryMB < 1024;
            if (p.SystemMemoryMB >= 8192 && p.CpuCores >= 8 && gpuHigh) return DeviceTier.High;
            if (p.SystemMemoryMB < 4096 || p.CpuCores < 6 || gpuLow) return DeviceTier.Low;
            return DeviceTier.Medium;
        }

        public static DeviceProfile SampleUnity()
        {
            return new DeviceProfile
            {
                CpuCores = SystemInfo.processorCount,
                SystemMemoryMB = SystemInfo.systemMemorySize,
                GpuMemoryMB = SystemInfo.graphicsMemorySize,
                MaxTextureSize = SystemInfo.maxTextureSize,
            };
        }

        public static TierQuality For(DeviceTier t) => t switch
        {
            DeviceTier.High => new TierQuality { RenderScale = 1.0f, ParticleScale = 1.0f, ShadowQuality = 2, DrawDistanceScale = 1.0f, TextureLevel = 0 },
            DeviceTier.Low => new TierQuality { RenderScale = 0.7f, ParticleScale = 0.5f, ShadowQuality = 0, DrawDistanceScale = 0.6f, TextureLevel = 2 },
            _ => new TierQuality { RenderScale = 0.85f, ParticleScale = 0.75f, ShadowQuality = 1, DrawDistanceScale = 0.8f, TextureLevel = 1 },
        };
    }
}
