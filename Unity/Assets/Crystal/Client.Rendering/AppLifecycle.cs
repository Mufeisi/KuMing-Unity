using System;
using UnityEngine;

namespace Crystal.Client.Rendering
{
    // 阶段7 移动平台骨架（PRD 第 2 项：挂起与恢复）：平台生命周期回调记录。
    // Android 上应用切后台触发 OnApplicationPause(true)/OnApplicationFocus(false)，回前台触发恢复。
    // 后续接入点：挂起时断开网络/保存状态，恢复时重连。logcat 按 "[app-lifecycle]" 可 grep。
    public class AppLifecycle : MonoBehaviour
    {
        DateTime? _pauseStart;

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                _pauseStart = DateTime.Now;
                Debug.Log("[app-lifecycle] pause");
            }
            else
            {
                double ms = _pauseStart.HasValue ? (DateTime.Now - _pauseStart.Value).TotalMilliseconds : 0;
                _pauseStart = null;
                Debug.Log($"[app-lifecycle] resume pausedMs={(int)ms}");
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            Debug.Log($"[app-lifecycle] focus={hasFocus}");
        }
    }
}
