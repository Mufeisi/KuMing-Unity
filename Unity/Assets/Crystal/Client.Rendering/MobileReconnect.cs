using System;
using Client;

namespace Crystal.Client.Rendering
{
    // G8 缺口补全 2/4（8-11 缺口清单：断线重连，登录域权重 8）：移动端断线自动重连（纯逻辑层）。
    // 触发源：GameSession.OnDisconnected（服务器 Disconnect 包）+ Network.Connected 轮询（客户端断网，
    // 连接丢失无 Disconnect 包）→ Arm → 延迟 DelayMs 后 Tick 触发 ConnectAndLogin（调用方注入真实
    // GameSession.Connect+Login）。防风暴：Armed 期间不重复 Arm；Attempts 计数供诊断/上限。
    // 探针注入 Now/ConnectAndLogin 断言时序（不依赖网络）。
    public static class MobileReconnect
    {
        public static Func<long> Now = () => CMain.Time;
        public static Action ConnectAndLogin = () => { }; // 调用方注入（MobileBootstrap 接 GameSession.Connect+Login）
        public const long DelayMs = 3000; // 断连后延迟（等网络/服务器恢复，防瞬断风暴）

        public static bool Armed { get; private set; }
        public static int Attempts { get; private set; }
        static long _at;

        // 断连检测命中 → 布防（3s 后重连）。Armed 期间重复断连通知忽略（防重连风暴）。
        public static void Arm()
        {
            if (Armed) return;
            Armed = true;
            Attempts++;
            _at = Now() + DelayMs;
        }

        // 每帧驱动（MobileBootstrap.Update）：到时间 → 触发 ConnectAndLogin。
        public static void Tick()
        {
            if (!Armed) return;
            if (Now() < _at) return;
            Armed = false;
            ConnectAndLogin();
        }

        public static void Reset() { Armed = false; Attempts = 0; }
    }
}
