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

                // 2. 最小启动场景：幂等——不存在则新建空场景，存在则打开；确保挂载 AppLifecycle + TouchInput + 主相机（挂 MobileBootstrap）。
                string sceneAbs = Path.GetFullPath(ScenePath);
                var scene = File.Exists(sceneAbs)
                    ? EditorSceneManager.OpenScene(sceneAbs, OpenSceneMode.Single)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!File.Exists(sceneAbs)) Directory.CreateDirectory(Path.GetDirectoryName(sceneAbs));
                EnsureAppLifecycle(scene);
                EnsureTouchInput(scene);
                EnsureCamera(scene);
                EditorSceneManager.SaveScene(scene, sceneAbs);
                Console.WriteLine($"[build-android] scene {ScenePath} appLifecycle=true touchInput=true camera=true");

                // 2b. Android 连接配置：静态字段初始化值在 Player 构建时固化，Editor 运行时赋值不进产物，
                // 故按 env 重写 MobileConfig.cs（生成源）再构建；env 缺省回落提交默认值。
                WriteMobileConfig();
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

        // 主相机：ClearFlags=SolidColor（清屏色为背景，屏幕模式 CrystalSpriteBatch.Clear 跳过 GL.Clear），
        // MobileBootstrap 挂其上以触发 OnPostRender（GL 渲染须在相机渲染之后，否则被清屏覆盖）。与 BuildPC.EnsureCamera 同构。
        static void EnsureCamera(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var cam = root.GetComponent<Camera>();
                if (cam != null) { EnsureMobileBootstrap(root); return; }
            }
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
            EnsureMobileBootstrap(go);
        }

        // 幂等挂载 MobileBootstrap（Android 引导壳，阶段8 前置）。
        static void EnsureMobileBootstrap(GameObject cameraRoot)
        {
            if (cameraRoot.GetComponent<MobileBootstrap>() == null)
                cameraRoot.AddComponent<MobileBootstrap>();
        }

        // 按 env 重写 MobileConfig.cs（Android 连接配置生成源）；env 缺省回落提交默认值，内容不变则 git 无脏。
        static void WriteMobileConfig()
        {
            string host = EnvOr("CRYSTAL_NET_HOST", "10.0.2.2");
            int port = IntEnvOr("CRYSTAL_NET_PORT", 7000);
            string id = EnvOr("CRYSTAL_LOGIN_ID", "pcplayer");
            string pw = EnvOr("CRYSTAL_LOGIN_PW", "pcplayer");
            string path = Path.Combine("Assets", "Crystal", "Client.Rendering", "MobileConfig.cs");
            File.WriteAllText(path,
                "namespace Crystal.Client.Rendering\n" +
                "{\n" +
                "    // Android 连接配置（生成源）：BuildAndroid.Run 每次构建按 env 重写（CRYSTAL_NET_HOST/PORT/LOGIN_ID/LOGIN_PW），\n" +
                "    // env 缺省时回落到此提交值（= androidverify 默认：模拟器 10.0.2.2 → 宿主服务端）。\n" +
                "    // 静态字段的初始化值在 Player 构建时编译固化，Editor 运行时赋值不进产物，故走生成源注入。\n" +
                "    static class MobileConfig\n" +
                "    {\n" +
                $"        public const string NetHost = \"{host}\";\n" +
                $"        public const int NetPort = {port};\n" +
                $"        public const string LoginId = \"{id}\";\n" +
                $"        public const string LoginPw = \"{pw}\";\n" +
                "    }\n" +
                "}\n");
            Console.WriteLine($"[build-android] config host={host} port={port} login={id}");
        }

        static string EnvOr(string name, string def)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? def : v;
        }

        static int IntEnvOr(string name, int def)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return int.TryParse(v, out int r) ? r : def;
        }
    }
}
