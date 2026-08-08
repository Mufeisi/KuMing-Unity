using System;
using System.IO;
using UnityEngine;

namespace Crystal.Client.Rendering
{
    // 阶段9 9-2（安装/补丁/首启/异常恢复）：PC 崩溃日志 + 安全模式判定。
    // running.mark：启动时写（运行中），正常退出（Application.quitting）清除；下次启动发现
    // mark 残留 = 上次异常退出 → SafeMode（消费：GameBootstrap 窗口化 + 降档）。
    // crash.log 轮转 3 份（crash.log.0/1/2 + crash.log）：logMessageReceived（Error/Exception/Assert）
    // + AppDomain.UnhandledException 双钩子写盘。探针可注入 CrashLogDir 隔离。
    public static class CrashGuard
    {
        public static string CrashLogDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "crash");
        public const int MaxLogs = 3;
        const string MarkFile = "running.mark";

        public static bool SafeMode;
        public static bool Initialized;

        public static void Init()
        {
            if (Initialized) return;
            Initialized = true;
            try { Directory.CreateDirectory(CrashLogDir); } catch { }
            string mark = Path.Combine(CrashLogDir, MarkFile);
            SafeMode = File.Exists(mark); // 上次异常退出（mark 未清）→ 安全模式
            try { File.WriteAllText(mark, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); } catch { }
            Application.logMessageReceived += OnLog;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            Application.quitting += () => { try { File.Delete(mark); } catch { } };
            Debug.Log($"[crash-guard] init safeMode={SafeMode} dir={CrashLogDir}");
        }

        // 探针/诊断用：直接写一条崩溃记录（不依赖钩子触发时序）。
        public static void WriteCrash(string condition, string stacktrace)
        {
            try
            {
                Directory.CreateDirectory(CrashLogDir);
                // 轮转：crash.log.2 ← crash.log.1 ← crash.log.0 ← crash.log
                string cur = Path.Combine(CrashLogDir, "crash.log");
                if (File.Exists(cur))
                {
                    for (int i = MaxLogs - 1; i > 0; i--)
                    {
                        string src = Path.Combine(CrashLogDir, i == 1 ? cur + ".0" : cur + $".{i - 1}");
                        string dst = Path.Combine(CrashLogDir, i == MaxLogs - 1 ? cur + $".{MaxLogs - 1}" : cur + $".{i}");
                        if (File.Exists(src)) { File.Delete(dst); File.Move(src, dst); }
                    }
                    File.Move(cur, cur + ".0");
                }
                File.WriteAllText(cur,
                    $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n{condition}\n{stacktrace}\n");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[crash-guard] write fail {ex.GetType().Name}: {ex.Message}");
            }
        }

        static void OnLog(string condition, string stacktrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                WriteCrash(condition, stacktrace);
        }

        static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            WriteCrash("UnhandledException", e.ExceptionObject?.ToString() ?? "(null)");
        }
    }
}
