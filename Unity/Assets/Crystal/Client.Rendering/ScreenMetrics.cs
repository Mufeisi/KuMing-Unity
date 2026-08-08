using Client;

namespace Crystal.Client.Rendering
{
    // P2 分辨率缩放统一（对照 sanduan SizeRatio，决策见 docs/sanduan-extraction.md C2）：
    // 移动 UI 屏幕尺寸单一扇出点。渲染高度真值在 GameRuntime（PC 壳/探针直写），本类把
    // 触摸 y 翻转基准（MobileUiAdapter.ScreenH）+ 对话框布局（Settings.ScreenWidth/Height）
    // 统一对齐到渲染真值——消灭 MobileBootstrap/GameSession 各处手动分散同步
    // （X-1 y 镜像教训：翻转基准与渲染高度漂移即镜像 bug）。坐标空间保持原生 1:1
    // （无逻辑分辨率、无黑边缩放），该契约由 ResolutionVerify 探针钉死。
    public static class ScreenMetrics
    {
        public static int W = 1280, H = 720; // 缺省与模拟器 backbuffer 对齐（同 MobileUiAdapter）

        // 以渲染真值对齐 UI 消费方（值未变早退，每帧调用零成本）。
        public static void Set(int w, int h)
        {
            if (W == w && H == h) return;
            W = w;
            H = h;
            MobileUiAdapter.ScreenW = w;
            MobileUiAdapter.ScreenH = h;
            Settings.ScreenWidth = w;
            Settings.ScreenHeight = h;
        }
    }
}
