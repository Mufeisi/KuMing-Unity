using System;
using System.IO;
using Client;
using Client.MirNetwork;
using Client.MirScenes;
using UnityEngine;
using C = ClientPackets;

namespace Crystal.Client.Rendering
{
    // PC Player 引导壳（C3 输入 + C5 引导）：Start 读 env 配置→连接登录，Update 驱动逻辑+输入，
    // OnPostRender 屏幕渲染（Update 期的 GL 绘制会被相机渲染清屏覆盖）。挂 Main.unity 主相机（BuildPC 幂等接线）。
    // 验证：pcverify.ps1 起服务器→启动 exe→等进图自动截图（CRYSTAL_AUTO_SHOT）→断言 Player.log + PNG。
    public sealed class GameBootstrap : MonoBehaviour
    {
        const long MoveIntervalMs = 500; // 走格节流（Mir2 walk 约 0.5s/格）
        // 构建包含锚点：BuildPC 把渲染 shader 序列化进此字段 → 场景→shader 依赖 → Player 构建包含（Shadr.Find 需已包含）。
        [HideInInspector] public Shader[] renderShaders;
        long _lastMoveAt;
        long _enterShotAt;
        MirDirection _lastDir = MirDirection.Up;
        bool _booted;
        bool _shot;

        void Start()
        {
            // 平台互斥：Android 归 MobileBootstrap（双组件可同挂 Main.unity，单活由平台守卫保证）。
            if (Application.platform == RuntimePlatform.Android) { enabled = false; return; }
            Application.targetFrameRate = 60;
            // 9-2 异常恢复：崩溃钩子最先注册（running.mark + crash.log），上次异常退出 → 安全模式
            CrashGuard.Init();
            // 9-2 首启设置：分辨率/全屏（Crystal.ini 持久化；首启写默认 1280×720 窗口）
            PcStartup.Apply();
            // 8-10 性能分级 + 9-2 安全模式降级：上次崩溃 → 强制窗口化默认分辨率（防全屏驱动
            // 崩溃循环）+ Medium 档；否则按设备自动分级
            if (CrashGuard.SafeMode)
            {
                try
                {
                    Screen.SetResolution(PcStartup.DefaultWidth, PcStartup.DefaultHeight, FullScreenMode.Windowed);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[game-bootstrap] safe-mode SetResolution fail {ex.GetType().Name}");
                }
                TierQualityApplier.Apply(DeviceTier.Medium);
                Debug.Log("[game-bootstrap] safe-mode: 窗口化默认分辨率 + 降档 Medium（上次异常退出）");
            }
            else
            {
                TierQualityApplier.ApplyAuto();
            }
            GameRuntime.ScreenW = Screen.width;
            GameRuntime.ScreenH = Screen.height;
            string exeDir = Path.GetDirectoryName(Application.dataPath);
            GameSession.MapDir = Env("CRYSTAL_MAP_DIR", Path.GetFullPath(Path.Combine(exeDir, "../Server/publish/Maps")));
            GameRenderer.MapAtlasDir = Env("CRYSTAL_MAP_ATLAS_DIR", Path.GetFullPath(Path.Combine(exeDir, "../assetcompile/map")));
            GameRenderer.AtlasDir = Env("CRYSTAL_ATLAS_DIR", Path.GetFullPath(Path.Combine(exeDir, "../assetcompile/all")));
            GameRenderer.BatchFloor = true;

            string host = Env("CRYSTAL_NET_HOST", "127.0.0.1");
            int port = GetInt("CRYSTAL_NET_PORT", 7000);
            string id = Env("CRYSTAL_LOGIN_ID", "pcplayer");
            string pw = Env("CRYSTAL_LOGIN_PW", "pcplayer");
            GameSession.OnEnterGame += () =>
            {
                _enterShotAt = CMain.Time + 6000;
                Debug.Log($"[pcplayer] enter-game objects={MapControl.Objects.Count}");
            };
            GameSession.OnError += m => Debug.LogError($"[pcplayer] error {m}");
            GameSession.OnSelectReady += () =>
            {
                Debug.Log($"[pcplayer] select-ready chars={GameSession.Characters.Count}");
                if (GameSession.Characters.Count > 0)
                    GameSession.SelectCharacter(0);
                else
                    GameSession.CreateCharacter("pcplayer", MirGender.Male, MirClass.Warrior);
            };

            Debug.Log($"[pcplayer] boot map={GameSession.MapDir} atlas={GameRenderer.AtlasDir}");
            GameSession.Connect(host, port);
            GameSession.Login(id, pw);
            _booted = true;
        }

        void Update()
        {
            if (!_booted) return;
            PollInput();
            GameRuntime.TickLogic();
            MaybeAutoShot();
            MaybeFpsLog();
        }

        // 9-4 性能基线：每 5s 输出 [pcplayer] fps=<当前帧率>（1s 滑动均值），供 pcperf.ps1 采样。
        float _fpsAccum;
        int _fpsFrames;
        double _fpsLogAt;
        void MaybeFpsLog()
        {
            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (Time.unscaledTime - _fpsLogAt >= 5.0)
            {
                float fps = _fpsFrames / Mathf.Max(_fpsAccum, 0.0001f);
                Debug.Log($"[pcplayer] fps={fps:F1}");
                _fpsAccum = 0; _fpsFrames = 0; _fpsLogAt = Time.unscaledTime;
            }
        }

        void OnPostRender()
        {
            if (!_booted) return;
            GameRuntime.RenderScreen();
        }

        void OnApplicationQuit()
        {
            GameRuntime.ReleaseAll();
        }

        // 验证钩子：进图后延时自动截图（CRYSTAL_AUTO_SHOT 设路径则启用），pcverify.ps1 断言产物。
        void MaybeAutoShot()
        {
            if (_shot) return;
            string path = Environment.GetEnvironmentVariable("CRYSTAL_AUTO_SHOT");
            if (string.IsNullOrEmpty(path)) return;
            if (GameSession.State != GameSessionState.InGame || GameRuntime.LastObjectDraws == 0) return;
            if (CMain.Time < _enterShotAt) return;
            _shot = true;
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[pcplayer] shot {path} objects={MapControl.Objects.Count} dps={CrystalSpriteBatch.DPSCounter}");
        }

        // WASD → 8 方向移动包；按住 Shift 变跑（C.Run）。轮询态驱动，节流走格速率。
        void PollInput()
        {
            if (GameSession.State != GameSessionState.InGame) return;
            bool up = Input.GetKey(KeyCode.W), down = Input.GetKey(KeyCode.S);
            bool left = Input.GetKey(KeyCode.A), right = Input.GetKey(KeyCode.D);
            if (!up && !down && !left && !right) return;

            MirDirection dir;
            if (up && right) dir = MirDirection.UpRight;
            else if (right && down) dir = MirDirection.DownRight;
            else if (down && left) dir = MirDirection.DownLeft;
            else if (left && up) dir = MirDirection.UpLeft;
            else if (up) dir = MirDirection.Up;
            else if (down) dir = MirDirection.Down;
            else if (left) dir = MirDirection.Left;
            else dir = MirDirection.Right;

            if (CMain.Time - _lastMoveAt < MoveIntervalMs) return;
            _lastMoveAt = CMain.Time;
            bool run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Network.Enqueue(run ? new C.Run { Direction = dir } : new C.Walk { Direction = dir });
        }

        static string Env(string name, string def)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? def : v;
        }

        static int GetInt(string name, int def)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return int.TryParse(v, out int r) ? r : def;
        }
    }
}
