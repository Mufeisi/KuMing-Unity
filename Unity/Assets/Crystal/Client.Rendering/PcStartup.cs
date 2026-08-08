using System;
using System.IO;
using UnityEngine;

namespace Crystal.Client.Rendering
{
    // 阶段9 9-2（安装/补丁/首启/异常恢复）：PC 首启设置（分辨率/全屏）。
    // 持久化：exe 旁 Crystal.ini（InIReader，仿 KeyBinds.ini 模式）；首启（无 ini）写默认
    // 1280×720 窗口化。Screen.SetResolution 应用；Settings 契约同步（ScreenWidth/Height/Resolution）。
    // 探针可注入 IniPath 隔离（batchmode 不污染 exe 旁）；EnsureSettings 纯读写判定，
    // Apply 含渲染调用（SetResolution，batchmode 探针不调）。
    public static class PcStartup
    {
        public static string IniPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Crystal.ini");
        public const int DefaultWidth = 1280, DefaultHeight = 720;

        public static bool IsFirstRun;
        public static int ScreenWidth = DefaultWidth;
        public static int ScreenHeight = DefaultHeight;
        public static bool FullScreen;

        // 首启/设置读写（无渲染调用，探针可测）：无 ini → 写默认并标记首启。
        public static void EnsureSettings()
        {
            IsFirstRun = !File.Exists(IniPath);
            var ini = new InIReader(IniPath);
            ScreenWidth = ini.ReadInt32("Screen", "Width", DefaultWidth);
            ScreenHeight = ini.ReadInt32("Screen", "Height", DefaultHeight);
            FullScreen = ini.ReadBoolean("Screen", "FullScreen", false);
            if (IsFirstRun) ini.Save(); // 首启落盘默认值（InIReader 读不到时已回写内存缓冲）
            // 契约同步：Settings.ScreenWidth/Height 与旧客户端 Resolution 档（800/1024/1366 控件图集序号）
            global::Client.Settings.ScreenWidth = ScreenWidth;
            global::Client.Settings.ScreenHeight = ScreenHeight;
            if (ScreenWidth >= 1366) global::Client.Settings.Resolution = 1366;
            else if (ScreenWidth >= 1024) global::Client.Settings.Resolution = 1024;
            else global::Client.Settings.Resolution = 800;
        }

        // 应用分辨率/全屏（Player 渲染调用；batchmode 探针不调）。
        public static void Apply()
        {
            EnsureSettings();
            try
            {
                Screen.SetResolution(ScreenWidth, ScreenHeight,
                    FullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[pc-startup] SetResolution fail {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
