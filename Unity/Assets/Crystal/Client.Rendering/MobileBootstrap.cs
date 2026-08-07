using System;
using System.IO;
using Client;
using Client.MirNetwork;
using Client.MirScenes;
using UnityEngine;
using C = ClientPackets;

namespace Crystal.Client.Rendering
{
    // Android 引导壳（阶段8 前置）：连接 10.0.2.2 → 自动登录 → 自动选号 → 进图 → 渲染 + 滑动移动。
    // 资源目录：persistentDataPath/{Maps,mapAtlas,atlas}（adb push 预置；阶段8 主任务由 ResourceSync OTA 接管）。
    // 配置优先级：静态字段（BuildAndroid env 注入）→ env → 默认值。平台互斥：非 Android 禁用自身，
    // 与 GameBootstrap 同挂 Main.unity 无害（BuildPC/BuildAndroid 各自挂载幂等，单活由平台守卫保证）。
    // 验证：androidverify.ps1 起服务器→adb 装 APK→push 资源→启动→logcat 断言
    // [mobile] connect/login/select/enter/user@x,y + 截图色数 + 滑动移动（位置变化）。
    public sealed class MobileBootstrap : MonoBehaviour
    {
        const long MoveIntervalMs = 500; // 走格节流（Mir2 walk 约 0.5s/格，与 PC PollInput 同频）
        const float SwipeMinPx = 40f;    // 滑动判定最小位移（轻点忽略，UI 触控走 TouchInputAdapter）
        const long PosLogMs = 1000;      // 位置心跳节流（[mobile] user@x,y 供 androidverify 解析实际出生坐标）

        // 构建时注入锚点：BuildAndroid 重写 MobileConfig.cs（env：CRYSTAL_NET_HOST/PORT/LOGIN_ID/LOGIN_PW），
        // env 缺省回落提交默认值（10.0.2.2:7000 pcplayer/pcplayer）；运行时 env 在 Android 不可用，故走生成源。
        public static string NetHost = MobileConfig.NetHost;
        public static int NetPort = MobileConfig.NetPort;
        public static string LoginId = MobileConfig.LoginId;
        public static string LoginPw = MobileConfig.LoginPw;

        bool _booted;
        bool _renderReady;
        bool _swiping;
        Vector2 _swipeStart;
        long _lastMoveAt;
        long _lastPosLogAt;
        string _lastLoggedPos;

        void Start()
        {
            if (Application.platform != RuntimePlatform.Android) { enabled = false; return; }
            Application.targetFrameRate = 60;
            // 模拟器（swiftshader 软渲染）2400x1080 全屏负载过高 → 帧率<5，触摸 Began 帧被合并丢失
            // （swipe 移动失效根因）。降 backbuffer 分辨率换取帧率；真机 GPU 非 SwiftShader 保持原生。
            bool emulator = SystemInfo.graphicsDeviceName.Contains("SwiftShader");
            if (emulator)
                Screen.SetResolution(1280, 720, true);

            string dataDir = Application.persistentDataPath;
            GameSession.MapDir = Path.Combine(dataDir, "Maps");
            GameRenderer.MapAtlasDir = Path.Combine(dataDir, "mapAtlas");
            GameRenderer.AtlasDir = Path.Combine(dataDir, "atlas");
            GameRenderer.BatchFloor = true;
            if (emulator)
            {
                GameRuntime.ScreenW = 1280; // SetResolution 异步生效前显式对齐渲染尺寸
                GameRuntime.ScreenH = 720;
            }
            else
            {
                GameRuntime.ScreenW = Screen.width;
                GameRuntime.ScreenH = Screen.height;
            }

            string host = Env("CRYSTAL_NET_HOST", NetHost);
            int port = GetInt("CRYSTAL_NET_PORT", NetPort);
            string id = Env("CRYSTAL_LOGIN_ID", LoginId);
            string pw = Env("CRYSTAL_LOGIN_PW", LoginPw);

            GameSession.OnEnterGame += () => Debug.Log($"[mobile] enter-game objects={MapControl.Objects.Count}");
            GameSession.OnError += m => Debug.LogError($"[mobile] error {m}");
            GameSession.OnSelectReady += () =>
            {
                Debug.Log($"[mobile] select-ready chars={GameSession.Characters.Count}");
                if (GameSession.Characters.Count > 0) GameSession.SelectCharacter(0);
                else GameSession.CreateCharacter("mobile", MirGender.Male, MirClass.Warrior);
            };

            Debug.Log($"[mobile] boot maps={GameSession.MapDir} atlas={GameRenderer.AtlasDir}");
            try
            {
                GameSession.Connect(host, port);
                Debug.Log($"[mobile] connect {host}:{port}");
                GameSession.Login(id, pw);
                Debug.Log($"[mobile] login {id}");
                _booted = true;
            }
            catch (Exception ex)
            {
                // IL2CPP 下 Debug.Log(ex.ToString()) 对 TypeInitializationException 会再次抛异常（"Couldn't extract
                // exception string"），故手工遍历内层链并把类型名+消息写入文件（logcat 里不刷屏，adb pull 取证）。
                var sb = new System.Text.StringBuilder();
                for (Exception e = ex; e != null; e = e.InnerException)
                    sb.AppendLine($"[{e.GetType().FullName}] {e.Message}");
                string logPath = Path.Combine(Application.persistentDataPath, "mobile-boot-error.log");
                try { File.WriteAllText(logPath, sb.ToString()); } catch (Exception io) { sb.AppendLine($"[IO] {io.Message}"); }
                Debug.LogError($"[mobile] boot-ex {ex.GetType().Name}: {ex.Message} inner-chain:\n{sb}");
            }
        }

        void Update()
        {
            if (!_booted) return;
            PollSwipe();
            GameRuntime.TickLogic();
            LogPosition();
            // 渲染就绪钩子：首帧 BuildLibIndex 全图扫描慢（模拟器 swiftshader 约 2.6s），
            // androidverify 等此日志后再截图/swipe（避免纯色误判 + 低帧率触摸丢失）。
            // 判据用 FramesRendered（首帧渲染完成）而非 LastObjectDraws：出生点周围可能无怪/NPC
            // （objects=0 → drawn=0），对象数判据在空区域永不触发。
            if (!_renderReady && GameRuntime.FramesRendered > 0)
            {
                _renderReady = true;
                Debug.Log($"[mobile] render-ready frames={GameRuntime.FramesRendered} draws={GameRuntime.LastObjectDraws} dps={CrystalSpriteBatch.DPSCounter}");
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

        // 滑动 → 8 方向走格（Mir2 移动语义：C.Walk，与 PC WASD 同通道）。
        // 只消费主触点滑动：按下锁定起点，抬起按位移主轴向发走格；轻点/取消忽略（UI 触控留 TouchInputAdapter）。
        void PollSwipe()
        {
            if (GameSession.State != GameSessionState.InGame || GameSession.User == null) return;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began)
                {
                    _swiping = true;
                    _swipeStart = t.position;
                }
                else if (t.phase == TouchPhase.Ended && _swiping)
                {
                    _swiping = false;
                    Vector2 d = t.position - _swipeStart;
                    if (Mathf.Abs(d.x) < SwipeMinPx && Mathf.Abs(d.y) < SwipeMinPx) return; // 轻点
                    if (CMain.Time - _lastMoveAt < MoveIntervalMs) return;
                    _lastMoveAt = CMain.Time;
                    MirDirection dir;
                    if (Mathf.Abs(d.x) > Mathf.Abs(d.y)) dir = d.x > 0 ? MirDirection.Right : MirDirection.Left;
                    else dir = d.y > 0 ? MirDirection.Down : MirDirection.Up;
                    Network.Enqueue(new C.Walk { Direction = dir });
                }
                else if (t.phase == TouchPhase.Canceled)
                {
                    _swiping = false;
                }
            }
        }

        // 位置心跳：进图后节流打印实际坐标（androidverify 解析出生点 → 按实际坐标重裁区域 → 二次 push 重启）。
        void LogPosition()
        {
            if (GameSession.State != GameSessionState.InGame || GameSession.User == null) return;
            if (CMain.Time - _lastPosLogAt < PosLogMs) return;
            _lastPosLogAt = CMain.Time;
            string pos = $"{GameSession.User.Movement.X},{GameSession.User.Movement.Y}";
            if (pos == _lastLoggedPos) return;
            _lastLoggedPos = pos;
            Debug.Log($"[mobile] user@{pos}");
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
