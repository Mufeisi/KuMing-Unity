using System;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // 阶段7 第 6 项探针（batchmode）：BuildIOS.Configure 后断言 iOS PlayerSettings 配置完整性。
    // bundle=com.crystal.mir2 / 横屏 LandscapeLeft / minOS 非空 / TeamID 签名入口（env 注入或占位）。
    // Windows 验证的是"配置流水线"这半；Xcode 构建+真机签名需 macOS，不在本探针范围。
    public static class BuildIOSVerify
    {
        public static void Run()
        {
            try
            {
                BuildIOS.Configure();
                int cases = 0;
                bool ok = true;

                string bundle = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.iOS);
                ok &= Check(bundle == "com.crystal.mir2", $"bundle={bundle}");
                cases++;

                ok &= Check(PlayerSettings.defaultInterfaceOrientation == UIOrientation.LandscapeLeft,
                    $"orientation={PlayerSettings.defaultInterfaceOrientation}");
                cases++;

                string minOS = PlayerSettings.iOS.targetOSVersionString;
                ok &= Check(!string.IsNullOrEmpty(minOS), $"minOS={minOS}");
                cases++;

                string team = PlayerSettings.iOS.appleDeveloperTeamID;
                Debug.Log($"[build-ios]   team={(string.IsNullOrEmpty(team) ? "auto(未设,需macOS+证书)" : team)} 签名入口: {(string.IsNullOrEmpty(team) ? "占位" : "显式")}");

                Debug.Log($"[build-ios] {(ok ? "PASS" : "FAIL")} cases={cases}");
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[build-ios] exception {ex}");
                EditorApplication.Exit(1);
            }
        }

        static bool Check(bool cond, string label)
        {
            Debug.Log($"[build-ios]   {label}: {(cond ? "ok" : "FAIL")}");
            return cond;
        }
    }
}
