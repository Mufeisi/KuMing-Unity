using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // G8 缺口 2/4 探针（batchmode）：MobileReconnect 断线重连纯逻辑（注入 Now/ConnectAndLogin）。
    // 场景1 延迟触发：Arm → 延迟未到不触发 → 到时间触发一次。
    // 场景2 防风暴：Armed 期间重复 Arm 不增计数；触发后不重复。
    // 场景3 计数/复位：Attempts 递增；Reset 清态。
    public static class MobileReconnectVerify
    {
        public static void Run()
        {
            try
            {
                long now = 0;
                int connects = 0;
                MobileReconnect.Now = () => now;
                MobileReconnect.ConnectAndLogin = () => connects++;
                int cases = 0;
                bool ok = true;

                // 场景1：延迟 3s 后触发一次
                MobileReconnect.Reset();
                MobileReconnect.Arm();
                ok &= Check(MobileReconnect.Armed && MobileReconnect.Attempts == 1, "1 Arm → Armed + Attempts=1");
                now = 1000;
                MobileReconnect.Tick();
                ok &= Check(MobileReconnect.Armed && connects == 0, "1 延迟未到（1s<3s）→ 不触发");
                now = 3001;
                MobileReconnect.Tick();
                ok &= Check(!MobileReconnect.Armed && connects == 1, "1 到时间（3s）→ 触发一次");

                // 场景2：Armed 期间重复 Arm 不增计数（防风暴）
                MobileReconnect.Arm();
                MobileReconnect.Arm();
                ok &= Check(MobileReconnect.Attempts == 2, "2 重复 Arm → 计数只 +1（防风暴）");
                now += MobileReconnect.DelayMs;
                MobileReconnect.Tick();
                MobileReconnect.Tick();
                ok &= Check(connects == 2, "2 触发后 Tick 不重复");

                // 场景3：Reset 清态
                MobileReconnect.Reset();
                ok &= Check(!MobileReconnect.Armed && MobileReconnect.Attempts == 0, "3 Reset → Armed=false Attempts=0");

                // 还原静态（防污染）
                MobileReconnect.Now = () => global::Client.MirNetwork.Network.Connected ? 0 : global::Client.CMain.Time;
                MobileReconnect.ConnectAndLogin = () => { };
                Debug.Log($"[mobile-reconnect] {(ok ? "PASS" : "FAIL")} cases={(ok ? 3 : cases)}");
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[mobile-reconnect] exception {ex}");
                EditorApplication.Exit(1);
            }
        }

        static bool Check(bool cond, string label)
        {
            if (!cond) Debug.Log($"[mobile-reconnect]   FAIL {label}");
            return cond;
        }
    }
}
