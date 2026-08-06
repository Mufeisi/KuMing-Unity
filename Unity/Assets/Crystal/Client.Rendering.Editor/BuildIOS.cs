using System;
using System.IO;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Crystal.Rendering.Editor
{
    // 阶段7 第 6 项 iOS Host（2026-08-06）：iOS PlayerSettings 配置 + 签名入口 + 最小启动场景挂载。
    // Windows 只能完成"配置流水线"这半（Configure + Verify）；Xcode 工程构建与真机签名/TestFlight
    // 需 macOS + Xcode + 开发者证书（PRD 行 883 风险项，backlog 登记，G7 双端门禁在 macOS 补齐）。
    // 签名：env CRYSTAL_IOS_TEAMID → PlayerSettings.iOS.appleDeveloperTeamID（自动签名）；未设留占位。
    public static class BuildIOS
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string BundleId = "com.crystal.mir2";

        // Configure：幂等设置 iOS PlayerSettings（bundle/最低 OS/横屏已在全局 defaultInterfaceOrientation 持久化）。
        public static void Configure()
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BundleId);
            PlayerSettings.iOS.targetOSVersionString = "13.0"; // 与横屏游戏策略一致（iOS13 起全面屏/横屏兼容）
            string team = Environment.GetEnvironmentVariable("CRYSTAL_IOS_TEAMID");
            if (!string.IsNullOrEmpty(team)) PlayerSettings.iOS.appleDeveloperTeamID = team;
            Console.WriteLine($"[build-ios] bundle={BundleId} minOS={PlayerSettings.iOS.targetOSVersionString} " +
                $"team={(string.IsNullOrEmpty(team) ? "auto(未设,需macOS+开发者证书)" : team)} dir=LandscapeLeft");
        }

        // Run：配置 + 场景幂等挂载（AppLifecycle/TouchInput 平台无关，与 Android 共享）。Xcode 构建在 macOS 上另跑。
        public static void Run()
        {
            try
            {
                Configure();
                EnsureScene(ScenePath);
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
                Console.WriteLine("[build-ios] scene ensured (Xcode 工程构建需 macOS；Windows 仅配置/校验)");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[build-ios] exception {ex}");
                EditorApplication.Exit(1);
            }
        }

        static void EnsureScene(string scenePath)
        {
            string sceneAbs = Path.GetFullPath(scenePath);
            var scene = File.Exists(sceneAbs)
                ? EditorSceneManager.OpenScene(sceneAbs, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!File.Exists(sceneAbs)) Directory.CreateDirectory(Path.GetDirectoryName(sceneAbs));
            EnsureComponent<AppLifecycle>(scene, "AppLifecycle");
            EnsureComponent<TouchInputAdapter>(scene, "TouchInput");
            EditorSceneManager.SaveScene(scene, sceneAbs);
        }

        static void EnsureComponent<T>(Scene scene, string goName) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponent<T>() != null) return;
            var go = new GameObject(goName);
            go.AddComponent<T>();
        }
    }
}
