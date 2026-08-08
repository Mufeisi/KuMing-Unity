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
        float _lastTouchDiagAt; // 触摸诊断日志节流（坐标系实证：backbuffer vs 物理）
        long _lastMoveAt;
        long _lastStepAt;   // 最近一次发出移动包时刻（服务器 _stepCounter>0 才允许 Run，模拟助跑）
        long _lastPosLogAt;
        string _lastLoggedPos;
        readonly TouchJoystick _joystick = new TouchJoystick();
        readonly MobileCombat _combat = new MobileCombat(); // 自动战斗（增量2）：索敌→追击→普攻
        readonly MobilePickup _pickup = new MobilePickup(); // 地面拾取（增量5）：地图 tap 设目标→走位→C.PickUp
        readonly MobileNpc _npc = new MobileNpc();           // NPC 对话（增量6）：地图 tap 命中 NPC→C.CallNPC 拉对话
        readonly MobileHud _hud = new MobileHud(1280, 720); // 战斗 HUD（增量3）：攻击按钮+血条，尺寸每帧 SetScreen 同步
        readonly MobileBag _bag = new MobileBag(1280, 720); // 背包按钮（增量1）：右上角开/关背包面板
        readonly MobileBag _equip = new MobileBag(1280, 720); // 装备按钮（增量3）：背包按钮下方开/关装备窗口（绿 tint）
        Texture2D _attackTex, _hpTex, _mpTex, _bagTex;      // HUD 纹理（圆盘/满条/方块，惰性生成一次）

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
            // 分辨率单一扇出（P2 分辨率缩放统一）：渲染真值 → 触摸翻转基准 + 对话框布局，
            // 消灭三处手动分散同步；Start 即对齐，消除与 TouchInputAdapter 的 Update 排序依赖。
            ScreenMetrics.Set(GameRuntime.ScreenW, GameRuntime.ScreenH);
            // 文本桥（R8 管线，阶段8 第2项）：TextRenderer seam 静态委托 → Unity 动态字体字形，
            // 主循环/对话框标签绘制依赖（MirLabel.DrawText 未装实现时是 no-op）。PreWarm 预热
            // ASCII 字形集，WarmTree 批前预构建在 RenderHud 每帧调用（缓存命中，仅首帧合成）。
            UiText.Install();
            UiText.PreWarm(8);
            _bag.OnToggle = ToggleBag;
            _equip.OnToggle = ToggleChar;
            // 装备按钮锚点：背包按钮正下方（90, 140+54+8），绿 tint 与背包黄区分（E2E 颜色定位）。
            _equip.SetMargin(new Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + MobileBag.ButtonH + 8f));
            _equip.TintOpen = new Color(0.45f, 1f, 0.5f, 0.95f);
            _equip.TintClosed = new Color(0.3f, 0.7f, 0.3f, 0.95f);
            // 返回键钩子（8-0 适配层）：Android Back → 关顶层对话框（当前最小形态=关背包面板），无对话框则未消费。
            // Hide()（增量2）顺带清选中+Tooltip；装备窗口（增量3）优先关（顶层先关）。
            MobileUiAdapter.BackHandler = () =>
            {
                var scene = GameScene.Scene;
                var chr = scene != null ? scene.CharacterDialog : null;
                if (chr != null && chr.Visible) { GameScene.SelectedCell = null; chr.Hide(); return true; }
                // NPC 商店（8-3-2）：叠在对话+背包上，Back 优先关商店（顶层先关）。
                var goods = scene != null ? scene.NPCGoodsDialog : null;
                if (goods != null && goods.Visible) { goods.Hide(); return true; }
                // NPC 对话框（增量6）：顶层先关（NPC 对话可与背包并存，Back 优先关对话）。
                var npc = scene != null ? scene.NPCDialog : null;
                if (npc != null && npc.Visible) { npc.Hide(); return true; }
                var inv = scene != null ? scene.InventoryDialog : null;
                if (inv != null && inv.Visible) { inv.Hide(); return true; }
                return false;
            };

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

            // P2 软键盘桥：默认接 TouchScreenKeyboard 原生软键盘（MirTextBox 聚焦时 Focus(box) 打开）。
            SoftKeyboardBridge.Keyboard = new UnitySoftKeyboard();

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
            // 分辨率扇出（P2 统一：渲染真值→UI 消费方，早退零成本）+ 返回键轮询（Android Back → 关顶层对话框）。
            ScreenMetrics.Set(GameRuntime.ScreenW, GameRuntime.ScreenH);
            MobileUiAdapter.PollBackKey();
            SoftKeyboardBridge.Poll(); // 软键盘文本/提交/取消轮询（无活跃框则 no-op）
            // 帧率/触摸诊断日志（真时间节流）：确认主循环活动与触摸事件是否到达 Unity
            if (Time.unscaledTime - _lastFpsLogAt > 5f)
            {
                _lastFpsLogAt = Time.unscaledTime;
                Debug.Log($"[mobile] fps={1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f):F1} touch={Input.touchCount}");
            }
            PollJoystick();
            GameRuntime.TickLogic();
            // 手动摇杆优先：拖动时暂停自动战斗；背包/装备/NPC 对话面板打开期间同样暂停（面板操作不被打断）。
            // 拾取目标激活时让位给拾取走位/拾取（索敌会覆盖目标格，抢走位）。
            var uiSc = GameScene.Scene;
            bool uiOpen = uiSc != null && ((uiSc.InventoryDialog?.Visible == true) || (uiSc.CharacterDialog?.Visible == true) || (uiSc.NPCDialog?.Visible == true) || (uiSc.NPCGoodsDialog?.Visible == true));
            if (!_joystick.Active && !uiOpen)
            {
                if (_pickup.Active) _pickup.Tick();
                else _combat.Tick();
            }
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

        // HUD 渲染（增量1 扩展）：场景渲染后开第二个批次画战斗 HUD + HUD 状态条（MainDialog）+ 背包面板
        // （InventoryDialog，Visible 时）+ 背包开/关按钮。移动摇杆为触控优先通道，HUD 按钮与摇杆共存。
        void RenderHud()
        {
            if (GameSession.State != GameSessionState.InGame) return;
            if (!_hudTexReady) EnsureHudTextures();
            SyncHudStats();
            if (_hud.ScreenW != GameRuntime.ScreenW || _hud.ScreenH != GameRuntime.ScreenH)
                _hud.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);
            if (_bag.ScreenW != GameRuntime.ScreenW || _bag.ScreenH != GameRuntime.ScreenH)
                _bag.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);

            var scene = GameScene.Scene;
            var main = scene != null ? scene.MainDialog : null;
            var inv = scene != null ? scene.InventoryDialog : null;
            var chr = scene != null ? scene.CharacterDialog : null;
            // 文本字形必须批前合成（R8 实证：batch 内 GetTextTexture 读字体图集 GetPixels32 返回透明）。
            // Process 先刷新标签文本 → WarmTree 预构建最新字形 → 批次内 DrawText 只命中缓存。
            if (main != null)
            {
                try { main.Process(); } catch (Exception ex) { Debug.LogError($"[mobile] main-process {ex.GetType().Name}: {ex.Message}"); }
                UiText.WarmTree(main);
            }
            if (inv != null && inv.Visible)
            {
                try { inv.Process(); } catch (Exception ex) { Debug.LogError($"[mobile] inv-process {ex.GetType().Name}: {ex.Message}"); }
                UiText.WarmTree(inv);
            }
            if (chr != null && chr.Visible)
            {
                UiText.WarmTree(chr); // 装备窗口无 Process（BeforeDraw 刷标签），仅预热字形
            }

            CrystalSpriteBatch.Begin(null, GameRuntime.ScreenW, GameRuntime.ScreenH);
            CrystalSpriteBatch.SetBlend(false, 1f, CrystalBlendMode.NORMAL); // 场景残留 additive 混合会漂白 HUD
            if (main != null) main.Draw();
            if (inv != null && inv.Visible) inv.Draw();
            if (chr != null && chr.Visible) chr.Draw();
            _bag.Render(_bagTex);
            _equip.Render(_bagTex);
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
            _bagTex = SolidTexture((int)MobileBag.ButtonW, (int)MobileBag.ButtonH); // 背包按钮白色方块（Render tint 上色）
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
        // 触摸互斥（8-0 适配层 RouteTouch）：唯一 y 翻转在 MobileUiAdapter.ToUi——背包按钮/HUD/MirControl 命中
        // 收 ui 空间（左上原点），摇杆收 raw 空间（左下原点，方向量化以 y 上为正）；消费序=背包按钮→面板打开
        // →Down 对话框命中→放行。面板打开期间摇杆整体停用（ToggleBag 已 Cancel 摇杆/HUD）。
        void PollJoystick()
        {
            if (GameSession.State != GameSessionState.InGame || GameSession.User == null) return;
            var scene = GameScene.Scene;
            var inv = scene != null ? scene.InventoryDialog : null;
            var chr = scene != null ? scene.CharacterDialog : null;
            var npcDlg = scene != null ? scene.NPCDialog : null;
            var goodsDlg = scene != null ? scene.NPCGoodsDialog : null;
            bool bagOpen = (inv != null && inv.Visible) || (chr != null && chr.Visible) || (npcDlg != null && npcDlg.Visible) || (goodsDlg != null && goodsDlg.Visible); // 面板打开期间摇杆停用（按钮仍可点击关闭）
            // 触摸坐标：透传 t.position（Unity backbuffer 像素系，X-1 touchdiag 实证），翻转由适配层统一完成。
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
                Vector2 raw = t.position;
                if (Time.unscaledTime - _lastTouchDiagAt > 2f)
                {
                    _lastTouchDiagAt = Time.unscaledTime;
                    Debug.Log($"[mobile] touch-diag n={Input.touchCount} raw=({t.position.x:F0},{t.position.y:F0}) screen=({Screen.width},{Screen.height}) pos=({raw.x:F0},{raw.y:F0})");
                }
                MobileUiAdapter.RouteTouch(new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => _bag.OnTouch(id, ph, ui) || _equip.OnTouch(id, ph, ui), // 背包/装备按钮（ui 空间，短路：背包先消费）
                    PanelOpen = bagOpen,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),                       // 可见对话框命中（ui 空间）
                    // 摇杆（raw 空间）→ 地图 tap 判定：Down 清旧目标（任何新触=移动意图或重新指定），
                    // Up 且无拖拽位移（ReleasedWithIntent false）且非 HUD 按钮区 → 地图 tap →
                    // NPC 优先（命中即消费），未命中落回拾取。TapAt 返回 false（无物品/距离外）即目标保持清空，不发包。
                    Joystick = (id, ph, rawPos) =>
                    {
                        _joystick.OnTouch(id, ph, rawPos);
                        var ui = MobileUiAdapter.ToUiPoint(rawPos);
                        if (ph == JoystickPhase.Down) { _pickup.Cancel(); return; }
                        if (ph == JoystickPhase.Up && !_joystick.ReleasedWithIntent && !_hud.Hit(MobileUiAdapter.ToUi(rawPos)))
                        {
                            var mc = scene != null ? scene.MapControl : null;
                            if (!_npc.TapAt(mc, ui)) _pickup.TapAt(mc, ui);
                        }
                    },
                    Hud = (id, ph, ui) => _hud.OnTouch(id, ph, ui),                      // HUD（ui 空间）
                }, t.fingerId, phase, raw);
            }
            if (bagOpen) return; // 面板打开：不驱动移动（含松手补步）

            bool moving = _joystick.Active && _joystick.Moving;
            if (moving)
            {
                _pickup.Cancel(); // 移动优先：摇杆拖拽立即打断拾取目标
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

        // 背包开/关（增量1）：切换 InventoryDialog.Visible；首次打开 RefreshInventory（格可见性）+ Process
        // （负重/金币标签）。面板打开时 Cancel 摇杆/HUD（防手指锁残留）；日志 [mobile] bag-open/close
        // 供 androidverify 数据断言。面板打开期间场景照常渲染（RenderHud 同批次画面板）。
        void ToggleBag(bool open)
        {
            var inv = GameScene.Scene != null ? GameScene.Scene.InventoryDialog : null;
            if (inv == null) return;
            try
            {
                if (open)
                {
                    if (!inv.Visible)
                    {
                        var chr = GameScene.Scene != null ? GameScene.Scene.CharacterDialog : null;
                        if (chr != null && chr.Visible) chr.Hide(); // 面板互斥：开背包关装备窗口
                        inv.RefreshInventory();
                        inv.Process();
                    }
                    inv.Visible = true;
                    _joystick.Cancel();
                    _hud.Cancel();
                    _pickup.Cancel(); // 面板打开：打断在途拾取走位（用户转入 UI 操作）
                }
                else
                {
                    inv.Hide(); // Hide()（增量2）顺带清选中+Tooltip
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[mobile] bag-toggle {ex.GetType().Name}: {ex.Message}");
            }
            Debug.Log($"[mobile] bag-{(open ? "open" : "close")} visible={inv.Visible}");
        }

        // 装备窗口开/关（增量3）：切换 CharacterDialog.Visible（默认角色页）。打开时 Cancel 摇杆/HUD
        // （防手指锁残留）+ 清选中；关闭同背包（Hide + 清选中）。日志 [mobile] char-open/close 供
        // androidverify 断言。装备格双击卸下、背包格双击穿戴走 MirItemCell 鼠标链（GameScene 双击分发）。
        void ToggleChar(bool open)
        {
            var chr = GameScene.Scene != null ? GameScene.Scene.CharacterDialog : null;
            if (chr == null) return;
            try
            {
                if (open)
                {
                    if (!chr.Visible)
                    {
                        var inv = GameScene.Scene != null ? GameScene.Scene.InventoryDialog : null;
                        if (inv != null && inv.Visible) inv.Hide(); // 面板互斥：开装备窗口关背包
                        chr.ShowCharacterPage();
                        chr.Visible = true;
                        GameScene.SelectedCell = null;
                        _joystick.Cancel();
                        _hud.Cancel();
                        _pickup.Cancel(); // 面板互斥：开装备窗口同样打断拾取
                    }
                }
                else
                {
                    GameScene.SelectedCell = null;
                    chr.Hide();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[mobile] char-toggle {ex.GetType().Name}: {ex.Message}");
            }
            Debug.Log($"[mobile] char-{(open ? "open" : "close")} visible={chr.Visible}");
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
