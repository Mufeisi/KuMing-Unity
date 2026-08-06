using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Crystal.Client.Rendering;

namespace Crystal.Rendering.Editor
{
    // 阶段7 Android Host（2026-08-06）：Android PlayerSettings 配置 + 最小启动场景 + APK 构建。
    // 用法：Unity.exe -batchmode -projectPath <proj> -executeMethod Crystal.Rendering.Editor.BuildAndroid.Run -quit
    // env: CRYSTAL_APK_OUT（默认 Build/Android/crystal.apk）。最低 API 固定 Android 8.0（API 26，Unity 6000.5 下限）。
    // 产物：APK + 构建日志。验证：APK 可安装/启动（真机或模拟器 ADB）。
    public static class BuildAndroid
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string BundleId = "com.crystal.mir2";

        public static void Run()
        {
            try
            {
                // 1. PlayerSettings：公司/产品/包名/最低 API（Android 8.0 API 26，匹配 mmap 4800 纹理上限的
                //    资源策略——移动端按设备性能分级降纹理，见 PRD 阶段7 第 5 项）。
                PlayerSettings.companyName = "Mir2";
                PlayerSettings.productName = "Crystal";
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId);
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26; // Unity 6000.5 最低 API 26（Android 8.0）
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
                // 屏幕方向：传奇为横屏游戏，骨架期默认 LandscapeLeft（PRD 阶段7 第 2 项；横竖屏策略决策项见 PRD 行 977，后续可切）。
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
                Console.WriteLine($"[build-android] player-settings company=Mir2 product=Crystal bundle={BundleId} minSdk=26 dir=LandscapeLeft");

                // 2. 最小启动场景：幂等——不存在则新建空场景，存在则打开；确保挂载 AppLifecycle（Android 生命周期骨架）。
                string sceneAbs = Path.GetFullPath(ScenePath);
                var scene = File.Exists(sceneAbs)
                    ? EditorSceneManager.OpenScene(sceneAbs, OpenSceneMode.Single)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!File.Exists(sceneAbs)) Directory.CreateDirectory(Path.GetDirectoryName(sceneAbs));
                EnsureAppLifecycle(scene);
                EnsureTouchInput(scene);
                EditorSceneManager.SaveScene(scene, sceneAbs);
                Console.WriteLine($"[build-android] scene {ScenePath} appLifecycle=true touchInput=true");
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

                // 3. 切 Android 目标并构建。
                string apk = Environment.GetEnvironmentVariable("CRYSTAL_APK_OUT");
                if (string.IsNullOrEmpty(apk)) apk = Path.Combine(Environment.CurrentDirectory, "Build", "Android", "crystal.apk");
                apk = Path.GetFullPath(apk);
                Directory.CreateDirectory(Path.GetDirectoryName(apk));

                var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, apk, BuildTarget.Android, BuildOptions.None);
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    Console.WriteLine($"[build-android] FAIL result={report.summary.result} errors={report.summary.totalErrors} (details in logFile)");
                    EditorApplication.Exit(1);
                    return;
                }
                long size = new FileInfo(apk).Length;
                Console.WriteLine($"[build-android] OK apk={apk} size={size / 1024 / 1024}MB");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[build-android] exception {ex}");
                EditorApplication.Exit(1);
            }
        }

        // 幂等挂载 AppLifecycle：场景根节点已含该组件则跳过，否则新建 GameObject 挂载。
        static void EnsureAppLifecycle(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponent<AppLifecycle>() != null) return;
            var go = new GameObject("AppLifecycle");
            go.AddComponent<AppLifecycle>();
        }

        // 幂等挂载 TouchInputAdapter（触控 Input Adapter，阶段7 第 3 项）。
        static void EnsureTouchInput(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponent<TouchInputAdapter>() != null) return;
            var go = new GameObject("TouchInput");
            go.AddComponent<TouchInputAdapter>();
        }
    }
}
