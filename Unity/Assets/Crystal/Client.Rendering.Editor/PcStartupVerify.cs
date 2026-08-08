using System.IO;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // 阶段9 9-2 探针（batchmode）：PC 首启设置 + 崩溃恢复/安全模式（纯逻辑，无渲染调用）。
    // 场景1 首启：无 ini → 写默认 + IsFirstRun + 值正确 + Settings 契约同步。
    // 场景2 二次启动：ini 存在 → 读值 + IsFirstRun=false。
    // 场景3 持久化：改 Width=1366 → 重读 = 1366 + Resolution 档 1366。
    // 场景4 崩溃标记：无 mark → SafeMode=false；残留 mark → Init → SafeMode=true。
    // 场景5 crash.log：WriteCrash 落盘 + 内容 + 轮转 3 份。
    // 注入隔离目录（探针进程不污染 exe 旁）；探针不调 PcStartup.Apply（SetResolution 渲染调用）。
    public static class PcStartupVerify
    {
        public static void Run()
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "pcstartup-verify-" + System.Guid.NewGuid().ToString("N"));
                int cases = 0;
                bool ok = SettingsCase(Path.Combine(root, "set"), ref cases)
                       & CrashCase(Path.Combine(root, "crash"), ref cases);
                try { Directory.Delete(root, true); } catch { }
                Debug.Log($"[pc-startup] {(ok ? "PASS" : "FAIL")} cases={cases}");
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[pc-startup] exception {ex}");
                EditorApplication.Exit(1);
            }
        }

        static bool SettingsCase(string dir, ref int cases)
        {
            Directory.CreateDirectory(dir);
            string ini = Path.Combine(dir, "Crystal.ini");
            bool ok = true;
            // 场景1 首启
            PcStartup.IniPath = ini;
            PcStartup.EnsureSettings();
            ok &= Check(PcStartup.IsFirstRun && File.Exists(ini)
                && PcStartup.ScreenWidth == PcStartup.DefaultWidth && PcStartup.ScreenHeight == PcStartup.DefaultHeight
                && !PcStartup.FullScreen, "1 首启：无 ini → 写默认 1280×720 窗口 + IsFirstRun");
            ok &= Check(global::Client.Settings.ScreenWidth == 1280 && global::Client.Settings.Resolution == 1024,
                "1 Settings 契约同步：ScreenWidth=1280 Resolution=1024");
            // 场景2 二次启动
            PcStartup.EnsureSettings();
            ok &= Check(!PcStartup.IsFirstRun && PcStartup.ScreenWidth == 1280, "2 二次启动：读 ini + IsFirstRun=false");
            // 场景3 持久化改值
            var reader = new InIReader(ini);
            reader.Write("Screen", "Width", 1366);
            reader.Write("Screen", "FullScreen", true);
            reader.Save();
            PcStartup.EnsureSettings();
            ok &= Check(PcStartup.ScreenWidth == 1366 && PcStartup.FullScreen
                && global::Client.Settings.Resolution == 1366, "3 持久化：改 Width=1366 FullScreen → 重读生效 + Resolution 1366");
            Debug.Log($"[pc-startup] settings-case ok={ok}");
            if (ok) cases++;
            return ok;
        }

        static bool CrashCase(string dir, ref int cases)
        {
            Directory.CreateDirectory(dir);
            bool ok = true;
            // 场景4a 干净启动：无 mark → SafeMode=false
            CrashGuard.CrashLogDir = dir;
            CrashGuard.Initialized = false;
            CrashGuard.Init();
            ok &= Check(!CrashGuard.SafeMode, "4a 干净启动（无残留 mark）→ SafeMode=false");
            // 场景4b 上次异常退出：写残留 mark → 重置后重新 Init → SafeMode=true
            File.WriteAllText(Path.Combine(dir, "running.mark"), "stale");
            CrashGuard.Initialized = false;
            CrashGuard.Init();
            ok &= Check(CrashGuard.SafeMode, "4b 残留 mark → SafeMode=true（上次异常退出判定）");
            // 场景5 crash.log 写盘 + 轮转 3 份
            CrashGuard.WriteCrash("boom-1", "stack-1");
            CrashGuard.WriteCrash("boom-2", "stack-2");
            CrashGuard.WriteCrash("boom-3", "stack-3");
            CrashGuard.WriteCrash("boom-4", "stack-4");
            ok &= Check(File.Exists(Path.Combine(dir, "crash.log"))
                && File.ReadAllText(Path.Combine(dir, "crash.log")).Contains("boom-4"),
                "5 crash.log 写盘（最新 boom-4）");
            ok &= Check(File.Exists(Path.Combine(dir, "crash.log.0"))
                && File.Exists(Path.Combine(dir, "crash.log.1"))
                && File.Exists(Path.Combine(dir, "crash.log.2")),
                "5 轮转 3 份（crash.log.0/1/2）");
            Debug.Log($"[pc-startup] crash-case ok={ok}");
            if (ok) cases++;
            return ok;
        }

        static bool Check(bool cond, string label)
        {
            Debug.Log($"[pc-startup]   {label}: {(cond ? "ok" : "FAIL")}");
            return cond;
        }
    }
}
