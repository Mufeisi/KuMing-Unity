using System;
using System.IO;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Crystal.Rendering.Editor
{
    // 阶段6 C5 PC Player 构建（2026-08-06）：StandaloneWindows64 发布 + Main 场景接线（主相机 + GameBootstrap + AppLifecycle）
    // + 渲染 shader 打入 always-included（Player 构建默认剥离未引用 shader，Shadr.Find 运行时需已包含）。
    // 用法：Unity.exe -batchmode -projectPath <proj> -executeMethod Crystal.Rendering.Editor.BuildPC.Run -quit
    // env: CRYSTAL_PC_OUT（默认 Build/PC/Crystal.exe）。产物：exe + Player.log 验证（pcverify.ps1 编排）。
    public static class BuildPC
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        static readonly string[] ShaderPaths =
        {
            "Assets/Crystal/Client.Rendering/Shaders/CrystalSprite.shader",
            "Assets/Crystal/Client.Rendering/Shaders/CrystalSpriteAdditive.shader",
            "Assets/Crystal/Client.Rendering/Shaders/CrystalSpriteReplace.shader",
            "Assets/Crystal/Client.Rendering/Shaders/CrystalSpriteMultiply.shader",
        };

        public static void Run()
        {
            try
            {
                PlayerSettings.companyName = "Mir2";
                PlayerSettings.productName = "Crystal";
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 720;
                PlayerSettings.runInBackground = true;
                Console.WriteLine("[build-pc] player-settings 1280x720 windowed runInBackground");

                // 1. 场景接线：幂等——存在则打开，确保主相机（触发 OnPostRender + 清屏色）+ GameBootstrap + AppLifecycle。
                string sceneAbs = Path.GetFullPath(ScenePath);
                var scene = File.Exists(sceneAbs)
                    ? EditorSceneManager.OpenScene(sceneAbs, OpenSceneMode.Single)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!File.Exists(sceneAbs)) Directory.CreateDirectory(Path.GetDirectoryName(sceneAbs));
                EnsureCamera(scene);
                EnsureAppLifecycle(scene);

                // 2. 渲染 shader 打入构建（Player 默认剥离未引用 shader，CrystalSpriteBatch 运行时 Shader.Find 需已包含）。
                // 方式：GameBootstrap.renderShaders 序列化 Shader 引用 → 场景→shader 依赖 → 构建包含（免 GraphicsSettings API）。
                // 顺序：须在 SaveScene 前赋值，否则场景未持久化 shader 引用 → 构建剥离 → Player Shader.Find 返回 null。
                var bootstrap = FindGameBootstrap(scene);
                var shaders = new System.Collections.Generic.List<Shader>();
                foreach (string sp in ShaderPaths)
                {
                    var sh = AssetDatabase.LoadAssetAtPath<Shader>(sp);
                    if (sh == null) { Console.WriteLine($"[build-pc] shader missing {sp}"); continue; }
                    shaders.Add(sh);
                }
                bootstrap.renderShaders = shaders.ToArray();
                Console.WriteLine($"[build-pc] shader refs={shaders.Count}");

                EditorSceneManager.SaveScene(scene, sceneAbs);
                Console.WriteLine($"[build-pc] scene {ScenePath} camera=true bootstrap=true appLifecycle=true");
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

                // 3. 构建 StandaloneWindows64（默认落到仓库根 Build/PC/，与 Build/Android 一致；cwd 是 Unity 工程目录）。
                string exe = Environment.GetEnvironmentVariable("CRYSTAL_PC_OUT");
                if (string.IsNullOrEmpty(exe))
                    exe = Path.Combine(Environment.CurrentDirectory, "..", "Build", "PC", "Crystal.exe");
                exe = Path.GetFullPath(exe);
                Directory.CreateDirectory(Path.GetDirectoryName(exe));

                var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, exe, BuildTarget.StandaloneWindows64, BuildOptions.None);
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    Console.WriteLine($"[build-pc] FAIL result={report.summary.result} errors={report.summary.totalErrors} (details in logFile)");
                    EditorApplication.Exit(1);
                    return;
                }
                long size = DirSize(Path.GetDirectoryName(exe));
                Console.WriteLine($"[build-pc] OK exe={exe} buildSize={size / 1024 / 1024}MB");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[build-pc] exception {ex}");
                EditorApplication.Exit(1);
            }
        }

        // 构建目录总大小（exe launcher 本身很小，真实体积在 Crystal_Data + UnityPlayer.dll）。
        static long DirSize(string dir)
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                try { total += new FileInfo(f).Length; } catch { }
            return total;
        }

        // 主相机：ClearFlags=SolidColor（清屏色为背景，屏幕模式 CrystalSpriteBatch.Clear 跳过 GL.Clear），GameBootstrap 挂其上
        // 以触发 OnPostRender（GL 渲染须在相机渲染之后，否则被清屏覆盖）。
        static void EnsureCamera(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var cam = root.GetComponent<Camera>();
                if (cam != null) { EnsureBootstrapOnCamera(root, scene); return; }
            }
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
            EnsureBootstrapOnCamera(go, scene);
        }

        static void EnsureBootstrapOnCamera(GameObject cameraRoot, Scene scene)
        {
            if (cameraRoot.GetComponent<GameBootstrap>() == null)
                cameraRoot.AddComponent<GameBootstrap>();
        }

        static GameBootstrap FindGameBootstrap(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var b = root.GetComponent<GameBootstrap>();
                if (b != null) return b;
            }
            return null;
        }

        // 幂等挂载 AppLifecycle（Android 同款生命周期骨架，PC 上为 OnApplicationQuit/暂停钩子）。
        static void EnsureAppLifecycle(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponent<AppLifecycle>() != null) return;
            var go = new GameObject("AppLifecycle");
            go.AddComponent<AppLifecycle>();
        }
    }
}
