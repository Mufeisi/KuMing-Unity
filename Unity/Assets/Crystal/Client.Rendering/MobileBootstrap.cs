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
        const long PosLogMs = 1000;      // 位置心跳节流（[mobile] user@x,y 供 androidverify 解析实际出生坐标）

        // 构建时注入锚点：BuildAndroid 重写 MobileConfig.cs（env：CRYSTAL_NET_HOST/PORT/LOGIN_ID/LOGIN_PW），
        // env 缺省回落提交默认值（10.0.2.2:7000 pcplayer/pcplayer）；运行时 env 在 Android 不可用，故走生成源。
        public static string NetHost = MobileConfig.NetHost;
        public static int NetPort = MobileConfig.NetPort;
        public static string LoginId = MobileConfig.LoginId;
        public static string LoginPw = MobileConfig.LoginPw;

        bool _booted;
        bool _renderReady;
        bool _hudTexReady;
        float _lastFpsLogAt; // 帧率诊断日志节流（模拟器 swiftshader 帧率低，确认 Unity 主循环活动）
        long _lastMoveAt;
        long _lastStepAt;   // 最近一次发出移动包时刻（服务器 _stepCounter>0 才允许 Run，模拟助跑）
        long _lastPosLogAt;
        string _lastLoggedPos;
        readonly TouchJoystick _joystick = new TouchJoystick();
        readonly MobileCombat _combat = new MobileCombat(); // 自动战斗（增量2）：索敌→追击→普攻
        readonly MobileHud _hud = new MobileHud(1280, 720); // 战斗 HUD（增量3）：攻击按钮+血条，尺寸每帧 SetScreen 同步
        Texture2D _attackTex, _hpTex, _mpTex;               // HUD 纹理（圆盘/满条，惰性生成一次）

        void Start()
        {
            if (Application.platform != RuntimePlatform.Android) { enabled = false; return; }
            Application.targetFrameRate = 60;
            CMain.LogImpl = Debug.Log; // 还原旧客户端 CMain.Log 到 Unity 日志（Android logcat 可见）
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
            // 帧率/触摸诊断日志（真时间节流）：确认主循环活动与触摸事件是否到达 Unity
            if (Time.unscaledTime - _lastFpsLogAt > 5f)
            {
                _lastFpsLogAt = Time.unscaledTime;
                Debug.Log($"[mobile] fps={1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f):F1} touch={Input.touchCount}");
            }
            PollJoystick();
            GameRuntime.TickLogic();
            if (!_joystick.Active) _combat.Tick(); // 手动摇杆优先：拖动时暂停自动战斗
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
            RenderHud();
        }

        // 战斗 HUD（增量3）：场景渲染后开第二个批次画攻击按钮+血条。移动摇杆为触控优先通道，
        // HUD 按钮与摇杆共存（左下/右下不重叠；同 TouchJoystick 纯逻辑层模式，OnTouch 喂入）。
        void RenderHud()
        {
            if (GameSession.State != GameSessionState.InGame) return;
            if (!_hudTexReady) EnsureHudTextures();
            SyncHudStats();
            if (_hud.ScreenW != GameRuntime.ScreenW || _hud.ScreenH != GameRuntime.ScreenH)
                _hud.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);

            CrystalSpriteBatch.Begin(null, GameRuntime.ScreenW, GameRuntime.ScreenH);
            CrystalSpriteBatch.SetBlend(false, 1f, CrystalBlendMode.NORMAL); // 场景残留 additive 混合会漂白 HUD
            _hud.Render(_attackTex, _hpTex, _mpTex);
            CrystalSpriteBatch.End();
        }

        // 血条数据：HP/MP 由 HealthChanged 实时同步（GameSession），MaxHP/MP 来自进图 UserInformation 的 Stats。
        void SyncHudStats()
        {
            var u = GameSession.User;
            if (u == null) return;
            _hud.Hp = u.HP;
            _hud.Mp = u.MP;
            _hud.MaxHp = u.Stats[Stat.HP];
            _hud.MaxMp = u.Stats[Stat.MP];
        }

        // HUD 纹理惰性生成：攻击按钮=程序化圆盘（直径=2*AttackRadius，点过滤像素风），
        // 血条=满条纯色（Render 按 HpRatio 裁剪 src）。全屏幕不透明按钮区外透明。
        void EnsureHudTextures()
        {
            _hudTexReady = true;
            int d = (int)(MobileHud.AttackRadius * 2f);
            _attackTex = new Texture2D(d, d, TextureFormat.RGBA32, false);
            var px = new Color32[d * d];
            float r = MobileHud.AttackRadius;
            for (int y = 0; y < d; y++)
                for (int x = 0; x < d; x++)
                {
                    float dist = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    px[y * d + x] = dist <= r ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            _attackTex.SetPixels32(px);
            _attackTex.Apply();
            _attackTex.filterMode = FilterMode.Point;

            int bw = (int)MobileHud.HpBarSize.x, bh = (int)MobileHud.HpBarSize.y;
            _hpTex = SolidTexture(bw, bh);
            _mpTex = SolidTexture(bw, bh);
        }

        static Texture2D SolidTexture(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px);
            t.Apply();
            t.filterMode = FilterMode.Point;
            return t;
        }

        void OnApplicationQuit()
        {
            GameRuntime.ReleaseAll();
        }

        // 触控移动摇杆（阶段8 第1项）：浮动摇杆——按住节流连续走（Mir2 walk 0.5s/格），
        // 超奔跑阈值切跑（C.Run）；松手时若刚拖拽过（上一帧 Moving）补发一步，保证快速轻滑（如 adb swipe 150ms）也触发移动。
        // 奔跑需助跑：服务器 HumanObject.CanRun 要求 _stepCounter>0（Walk 累积、静止 700ms 清零），
        // 静止直接发 C.Run 会被拒（原地不动）；故 700ms 内发过移动包才切 Run，否则首包/补步发 C.Walk。
        // TouchJoystick 纯逻辑层喂 Input.touches；移动通道与 PC WASD 同（C.Walk/C.Run 8 向）。
        void PollJoystick()
        {
            if (GameSession.State != GameSessionState.InGame || GameSession.User == null) return;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                var phase = t.phase switch
                {
                    TouchPhase.Began => JoystickPhase.Down,
                    TouchPhase.Moved => JoystickPhase.Move,
                    TouchPhase.Ended => JoystickPhase.Up,
                    TouchPhase.Canceled => JoystickPhase.Cancel,
                    _ => JoystickPhase.Move, // Stationary：保持当前方向
                };
                _joystick.OnTouch(t.fingerId, phase, t.position);
                _hud.OnTouch(t.fingerId, phase, t.position); // 攻击按钮独立于摇杆（右下区），Down 命中才激活
            }

            bool moving = _joystick.Active && _joystick.Moving;
            if (moving)
            {
                if (CMain.Time - _lastMoveAt >= MoveIntervalMs)
                {
                    _lastMoveAt = CMain.Time;
                    bool ready = CMain.Time - _lastStepAt < 700; // 700ms 内移动过 → 服务器步数已积累，可跑
                    Network.Enqueue(ready && _joystick.Run
                        ? new C.Run { Direction = _joystick.Dir }
                        : new C.Walk { Direction = _joystick.Dir });
                    _lastStepAt = CMain.Time;
                }
            }
            else if (_joystick.ReleasedWithIntent)
            {
                // 松手补一步：ReleasedWithIntent 由 Ended 位置位移判定（Moved 整帧丢失也触发移动）。
                // 轻扫意图=一格，且静止起跑无助跑，一律 C.Walk（C.Run 会被服务器拒）。
                _joystick.ClearRelease();
                if (CMain.Time - _lastMoveAt >= MoveIntervalMs)
                {
                    _lastMoveAt = CMain.Time;
                    Network.Enqueue(new C.Walk { Direction = _joystick.LastDir });
                    _lastStepAt = CMain.Time;
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
