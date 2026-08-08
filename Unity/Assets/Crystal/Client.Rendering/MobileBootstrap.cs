using System;
using System.IO;
using Client;
using Client.MirControls;
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
        bool _prevAutoPath; // 上一帧 MapControl.AutoPath（上升沿检测：视口点击设寻路 → 关地图窗）
        readonly TouchJoystick _joystick = new TouchJoystick();
        readonly MobileCombat _combat = new MobileCombat(); // 自动战斗（增量2）：索敌→追击→普攻
        readonly MobilePickup _pickup = new MobilePickup(); // 地面拾取（增量5）：地图 tap 设目标→走位→C.PickUp
        readonly MobileAutoPath _autoPath = new MobileAutoPath(); // 自动寻路（8-4-2）：大地图视口点击→逐格 C.Walk
        readonly MobileNpc _npc = new MobileNpc();           // NPC 对话（增量6）：地图 tap 命中 NPC→C.CallNPC 拉对话
        readonly MobileHud _hud = new MobileHud(1280, 720); // 战斗 HUD（增量3）：攻击按钮+血条，尺寸每帧 SetScreen 同步
        readonly MobileBag _bag = new MobileBag(1280, 720); // 背包按钮（增量1）：右上角开/关背包面板
        readonly MobileBag _equip = new MobileBag(1280, 720); // 装备按钮（增量3）：背包按钮下方开/关装备窗口（绿 tint）
        readonly MobileBag _quest = new MobileBag(1280, 720);  // 任务按钮（8-4-1）：装备下方开/关任务日记（蓝 tint）
        readonly MobileBag _map = new MobileBag(1280, 720);    // 大地图按钮（8-4-2）：任务下方开/关大地图（紫 tint）
        readonly MobileBag _group = new MobileBag(1280, 720);  // 组队按钮（8-6-1）：地图下方开/关组队面板（红 tint）
        readonly MobileBag _friend = new MobileBag(1280, 720); // 好友按钮（8-6-2）：组队下方开/关好友/黑名单面板（青 tint）
        readonly MobileBag _guild = new MobileBag(1280, 720);  // 行会按钮（8-6-3）：好友下方开/关行会面板（黄 tint）
        readonly MobileTrade _trade = new MobileTrade();       // 交易请求（8-7-1）：地图 tap 命中玩家→C.TradeRequest
        readonly MobileBag _mail = new MobileBag(1280, 720);   // 邮件按钮（8-7-2）：行会下方开/关邮件列表（粉 tint）
        readonly MobileBag _market = new MobileBag(1280, 720); // 拍卖行按钮（8-7-3）：邮件下方开/关拍卖行面板（橙 tint）
        readonly MobileBag _gameShop = new MobileBag(1280, 720); // 商城按钮（8-7-4）：拍卖行下方开/关商城面板（青绿 tint）
        readonly MobileChat _chat = new MobileChat(1280, 720); // 聊天（8-5-2）：底部左缘聊天/频道按钮
        Texture2D _attackTex, _hpTex, _mpTex, _bagTex, _chatTex, _groupTex, _friendTex, _guildTex, _mailTex; // HUD 纹理（圆盘/满条/方块，惰性生成一次）

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
            // 任务按钮（8-4-1）：装备按钮正下方，蓝 tint 与背包黄/装备绿区分；开/关任务日记面板。
            _quest.OnToggle = ToggleQuest;
            _quest.SetMargin(new Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 2));
            _quest.TintOpen = new Color(0.5f, 0.7f, 1f, 0.95f);
            _quest.TintClosed = new Color(0.3f, 0.45f, 0.8f, 0.95f);
            // 大地图按钮（8-4-2）：任务按钮正下方，紫 tint 与背包黄/装备绿/任务蓝区分；开/关大地图窗。
            _map.OnToggle = ToggleBigMap;
            _map.SetMargin(new Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 3));
            _map.TintOpen = new Color(0.85f, 0.6f, 1f, 0.95f);
            _map.TintClosed = new Color(0.6f, 0.35f, 0.85f, 0.95f);
            // 组队按钮（8-6-1）：地图按钮正下方，红 tint 与背包黄/装备绿/任务蓝/地图紫区分；开/关组队面板。
            _group.OnToggle = ToggleGroup;
            _group.SetMargin(new Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 4));
            _group.TintOpen = new Color(1f, 0.5f, 0.5f, 0.95f);
            _group.TintClosed = new Color(0.8f, 0.3f, 0.3f, 0.95f);
            // 好友按钮（8-6-2）：组队按钮正下方，青 tint 与背包黄/装备绿/任务蓝/地图紫/组队红区分；开/关好友/黑名单面板。
            _friend.OnToggle = ToggleFriend;
            _friend.SetMargin(new Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 5));
            _friend.TintOpen = new Color(0.4f, 0.85f, 0.9f, 0.95f);
            _friend.TintClosed = new Color(0.25f, 0.6f, 0.65f, 0.95f);
            _guild.OnToggle = ToggleGuild;
            _guild.SetMargin(new Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 6));
            _guild.TintOpen = new Color(1f, 0.85f, 0.4f, 0.95f);
            _guild.TintClosed = new Color(0.7f, 0.55f, 0.25f, 0.95f);
            // 邮件按钮（8-7-2）：行会按钮正下方，粉 tint 与背包黄/装备绿/任务蓝/地图紫/组队红/好友青/行会黄区分；
            // 开/关邮件列表（MailListDialog，Show 刷新 User.Mail 分页）。
            _mail.OnToggle = ToggleMail;
            _mail.SetMargin(new Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 7));
            _mail.TintOpen = new Color(1f, 0.6f, 0.8f, 0.95f);
            _mail.TintClosed = new Color(0.75f, 0.35f, 0.55f, 0.95f);

            // 开/关拍卖行（TrustMerchantDialog，Show 发 C.MarketSearch 拉首页 + 开背包）。
            _market.OnToggle = ToggleMarket;
            _market.SetMargin(new Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 8));
            _market.TintOpen = new Color(1f, 0.75f, 0.35f, 0.95f);
            _market.TintClosed = new Color(0.7f, 0.5f, 0.2f, 0.95f);

            // 开/关商城（GameShopDialog，Show 重置分类为玩家职业 + 本地过滤重建）。
            _gameShop.OnToggle = ToggleGameShop;
            _gameShop.SetMargin(new Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 9));
            _gameShop.TintOpen = new Color(0.4f, 0.9f, 0.85f, 0.95f);
            _gameShop.TintClosed = new Color(0.2f, 0.6f, 0.55f, 0.95f);
            // 聊天（8-5-2）：底部左缘聊天/频道按钮。OnOpenInput=开输入框（首次开注入当前频道前缀）
            // + 弹软键盘；OnChannel=切换频道前缀（输入框开着则重写文本前缀并重开软键盘使初始文本生效，
            // 服务器按 !/@ 前缀分频道）。发送走软键盘 Enter（SoftKeyboardBridge Submitted → ChatTextBox_KeyPress
            // → C.Chat），不另设发送按钮。接线逻辑集中在 MobileChat 静态助手（探针共用）。
            _chat.OnOpenInput = () => MobileChat.OpenInput(GameScene.Scene?.ChatDialog, _chat.Channel);
            _chat.OnChannel = ch => MobileChat.ApplyChannel(GameScene.Scene?.ChatDialog, ch);
            // 返回键钩子（8-0 适配层）：Android Back → 关顶层对话框（当前最小形态=关背包面板），无对话框则未消费。
            // Hide()（增量2）顺带清选中+Tooltip；装备窗口（增量3）优先关（顶层先关）。
            MobileUiAdapter.BackHandler = () =>
            {
                var scene = GameScene.Scene;
                // 瞬态模态（8-6-1）：MirMessageBox/MirInputBox 弹窗（组队邀请/成员名输入）挂 scene 树且
                // Modal 阻断下层，Back → Esc 语义（MirMessageBox YesNo→No 拒绝，MirInputBox→Cancel 取消）。
                var modal = FindModal(scene);
                if (modal != null)
                {
                    modal.OnKeyPress(new KeyPressEventArgs((char)Keys.Escape));
                    return true;
                }
                // 聊天输入（8-5-2）：输入框开（软键盘弹出中）Back 先关输入（对齐 PC Escape 隐藏清空语义）。
                var chat = scene != null ? scene.ChatDialog : null;
                if (chat != null && MobileChat.CloseInput(chat)) return true;
                // 大地图（8-4-2）：移动端地图按钮打开，Back 关闭（顶层先关）+ 打断在途寻路。
                var bigMap = scene != null ? scene.BigMapDialog : null;
                if (bigMap != null && bigMap.Visible) { bigMap.Hide(); _autoPath.Cancel(); return true; }
                // 组队面板（8-6-1）：移动端组队按钮打开，Back 关闭（同任务/地图按钮面板）。
                var group = scene != null ? scene.GroupDialog : null;
                if (group != null && group.Visible) { group.Hide(); return true; }
                // 备注浮窗（8-6-2）：好友面板子窗（MemoDialog），Back 先关浮窗（顶层先关，好友面板保留）。
                var memo = scene != null ? scene.MemoDialog : null;
                if (memo != null && memo.Visible) { memo.Hide(); return true; }
                // 好友面板（8-6-2）：移动端好友按钮打开，Back 关闭（同组队/任务/地图按钮面板）。
                var friend = scene != null ? scene.FriendDialog : null;
                if (friend != null && friend.Visible) { friend.Hide(); return true; }
                // 行会面板（8-6-3）：移动端行会按钮打开，Back 关闭（同组队/好友按钮面板）。
                var guild = scene != null ? scene.GuildDialog : null;
                if (guild != null && guild.Visible) { guild.Hide(); return true; }
                // 邮件五窗（8-7-2）：子窗（读信/读包裹/写信/寄包裹）为列表顶层，Back 先关子窗
                // （寄包裹窗 Hide 走 Reset 退金币+逐格解源格锁），再关邮件列表（按钮打开的面板层）。
                var compP = scene != null ? scene.MailComposeParcelDialog : null;
                if (compP != null && compP.Visible) { compP.Hide(); return true; }
                var compL = scene != null ? scene.MailComposeLetterDialog : null;
                if (compL != null && compL.Visible) { compL.Hide(); return true; }
                var readP = scene != null ? scene.MailReadParcelDialog : null;
                if (readP != null && readP.Visible) { readP.Hide(); return true; }
                var readL = scene != null ? scene.MailReadLetterDialog : null;
                if (readL != null && readL.Visible) { readL.Hide(); return true; }
                var mailList = scene != null ? scene.MailListDialog : null;
                if (mailList != null && mailList.Visible) { mailList.Hide(); return true; }
                // 拍卖行（8-7-3）：独立顶层窗（与背包并存，Show 自带开背包），Back 在邮件之后、装备面板之前关闭。
                var marketDlg = scene != null ? scene.TrustMerchantDialog : null;
                if (marketDlg != null && marketDlg.Visible) { marketDlg.Hide(); return true; }
                // 商城（8-7-4）：独立顶层窗（无需背包），Back 在拍卖行之后关闭。
                var gameShopDlg = scene != null ? scene.GameShopDialog : null;
                if (gameShopDlg != null && gameShopDlg.Visible) { gameShopDlg.Hide(); return true; }
                var chr = scene != null ? scene.CharacterDialog : null;
                if (chr != null && chr.Visible) { GameScene.SelectedCell = null; chr.Hide(); return true; }
                // 仓库（8-3-3）：开仓库时 NPC 对话已关（S.NPCStorage），Back 优先关仓库（顶层）。
                var store = scene != null ? scene.StorageDialog : null;
                if (store != null && store.Visible) { store.Hide(); return true; }
                // NPC 商店（8-3-2）：叠在对话+背包上，Back 优先关商店（顶层先关）。
                var goods = scene != null ? scene.NPCGoodsDialog : null;
                if (goods != null && goods.Visible) { goods.Hide(); return true; }
                // 任务详情（8-4-1）：点日记/列表行打开，Back 优先关（顶层先关）。
                var qdet = scene != null ? scene.QuestDetailDialog : null;
                if (qdet != null && qdet.Visible) { qdet.Hide(); return true; }
                // 任务日记（8-4-1）：移动端任务按钮打开，Back 关闭。
                var qdia = scene != null ? scene.QuestDiaryDialog : null;
                if (qdia != null && qdia.Visible) { qdia.Hide(); return true; }
                // 任务列表（8-4-1）：随 NPC 对话连带打开，Back 优先关（Hide 连带关 NPC 对话，须在 npc 前）。
                var qlist = scene != null ? scene.QuestListDialog : null;
                if (qlist != null && qlist.Visible) { qlist.Hide(); return true; }
                // NPC 对话框（增量6）：顶层先关（NPC 对话可与背包并存，Back 优先关对话）。
                var npc = scene != null ? scene.NPCDialog : null;
                if (npc != null && npc.Visible) { npc.Hide(); return true; }
                // 交易面板（8-7-1）：移动端 tap 玩家发 C.TradeRequest 打开；Back 复用关闭按钮完整语义
                // （Hide 双方 + C.TradeCancel 取消交易），同 NPC 商店 Back 优先关（顶层先关）。
                var trade = scene != null ? scene.TradeDialog : null;
                if (trade != null && trade.Visible)
                {
                    trade.CloseButton.InvokeMouseClick(EventArgs.Empty);
                    return true;
                }
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
            bool uiOpen = uiSc != null && ((uiSc.InventoryDialog?.Visible == true) || (uiSc.CharacterDialog?.Visible == true) || (uiSc.NPCDialog?.Visible == true) || (uiSc.NPCGoodsDialog?.Visible == true) || (uiSc.StorageDialog?.Visible == true) || (uiSc.QuestDiaryDialog?.Visible == true) || (uiSc.QuestListDialog?.Visible == true) || (uiSc.QuestDetailDialog?.Visible == true) || (uiSc.BigMapDialog?.Visible == true) || (uiSc.GroupDialog?.Visible == true) || (uiSc.FriendDialog?.Visible == true) || (uiSc.GuildDialog?.Visible == true) || (uiSc.TradeDialog?.Visible == true) || (uiSc.MailListDialog?.Visible == true) || (uiSc.MailComposeLetterDialog?.Visible == true) || (uiSc.MailComposeParcelDialog?.Visible == true) || (uiSc.MailReadLetterDialog?.Visible == true) || (uiSc.MailReadParcelDialog?.Visible == true) || (uiSc.TrustMerchantDialog?.Visible == true) || (uiSc.GameShopDialog?.Visible == true));
            // 大地图视口点击已设自动寻路（TouchInputAdapter 点击链 OnMouseClick）→ 关地图窗，在世界走位
            // （地图窗遮挡无用，且 uiOpen 门控会暂停寻路 tick）。仅检测 AutoPath 上升沿（false→true），
            // 避免寻路激活中重开地图被本帧立刻关闭。
            var mapDlg = uiSc != null ? uiSc.BigMapDialog : null;
            bool autoPathNow = uiSc != null && uiSc.MapControl != null && uiSc.MapControl.AutoPath;
            if (autoPathNow && !_prevAutoPath && mapDlg != null && mapDlg.Visible)
            {
                mapDlg.Hide();
                Debug.Log("[mobile] autopath-set close-bigmap");
            }
            _prevAutoPath = autoPathNow;
            if (!_joystick.Active && !uiOpen)
            {
                if (_pickup.Active) _pickup.Tick();
                else if (_autoPath.Active) _autoPath.Tick();
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
            if (_equip.ScreenW != GameRuntime.ScreenW || _equip.ScreenH != GameRuntime.ScreenH)
                _equip.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);
            if (_quest.ScreenW != GameRuntime.ScreenW || _quest.ScreenH != GameRuntime.ScreenH)
                _quest.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);
            if (_map.ScreenW != GameRuntime.ScreenW || _map.ScreenH != GameRuntime.ScreenH)
                _map.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);
            if (_chat.ScreenW != GameRuntime.ScreenW || _chat.ScreenH != GameRuntime.ScreenH)
                _chat.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);
            if (_group.ScreenW != GameRuntime.ScreenW || _group.ScreenH != GameRuntime.ScreenH)
                _group.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);
            if (_friend.ScreenW != GameRuntime.ScreenW || _friend.ScreenH != GameRuntime.ScreenH)
                _friend.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);
            if (_guild.ScreenW != GameRuntime.ScreenW || _guild.ScreenH != GameRuntime.ScreenH)
                _guild.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);
            if (_mail.ScreenW != GameRuntime.ScreenW || _mail.ScreenH != GameRuntime.ScreenH)
                _mail.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);
            if (_market.ScreenW != GameRuntime.ScreenW || _market.ScreenH != GameRuntime.ScreenH)
                _market.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);
            if (_gameShop.ScreenW != GameRuntime.ScreenW || _gameShop.ScreenH != GameRuntime.ScreenH)
                _gameShop.SetScreen(GameRuntime.ScreenW, GameRuntime.ScreenH);

            var scene = GameScene.Scene;
            var main = scene != null ? scene.MainDialog : null;
            var inv = scene != null ? scene.InventoryDialog : null;
            var chr = scene != null ? scene.CharacterDialog : null;
            var qdia = scene != null ? scene.QuestDiaryDialog : null;
            var qlist = scene != null ? scene.QuestListDialog : null;
            var qdet = scene != null ? scene.QuestDetailDialog : null;
            var qtrk = scene != null ? scene.QuestTrackingDialog : null;
            var bigMap = scene != null ? scene.BigMapDialog : null;
            var mini = scene != null ? scene.MiniMapDialog : null;
            var chat = scene != null ? scene.ChatDialog : null;
            var group = scene != null ? scene.GroupDialog : null;
            var friend = scene != null ? scene.FriendDialog : null;
            var guild = scene != null ? scene.GuildDialog : null;
            var trade = scene != null ? scene.TradeDialog : null;
            var guest = scene != null ? scene.GuestTradeDialog : null;
            var mailList = scene != null ? scene.MailListDialog : null;
            var compLetter = scene != null ? scene.MailComposeLetterDialog : null;
            var compParcel = scene != null ? scene.MailComposeParcelDialog : null;
            var readLetter = scene != null ? scene.MailReadLetterDialog : null;
            var readParcel = scene != null ? scene.MailReadParcelDialog : null;
            var market = scene != null ? scene.TrustMerchantDialog : null;
            var gameShop = scene != null ? scene.GameShopDialog : null;
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
            // 任务四窗（8-4-1）：面板开才预热字形（日记/列表/详情含 MirLabel 文本，批前须合帧）。
            if (qdia != null && qdia.Visible) UiText.WarmTree(qdia);
            if (qlist != null && qlist.Visible) UiText.WarmTree(qlist);
            if (qdet != null && qdet.Visible) UiText.WarmTree(qdet);
            if (qtrk != null && qtrk.Visible) UiText.WarmTree(qtrk);
            // 大地图（8-4-2）：开才预热字形（TitleLabel/坐标标签/行名），批前须合帧。
            if (bigMap != null && bigMap.Visible) UiText.WarmTree(bigMap);
            // 小地图（8-4-3）：常驻 HUD（旧客户端 GameScene.Process 每帧调）。Process 先刷坐标/地图名
            // → WarmTree 预构建最新字形 → 批次内 DrawText 只命中缓存（同 main/inv 模式）。
            if (mini != null)
            {
                try { mini.Process(); } catch (Exception ex) { Debug.LogError($"[mobile] mini-process {ex.GetType().Name}: {ex.Message}"); }
                UiText.WarmTree(mini);
            }
            // 聊天窗（8-5-2）：常驻底部（旧客户端 GameScene 每帧 Draw），ChatLines 文本标签批前须合帧。
            if (chat != null) UiText.WarmTree(chat);
            // 组队面板（8-6-1）：开才预热字形（成员名/标题标签），批前须合帧（同任务/地图面板模式）。
            if (group != null && group.Visible) UiText.WarmTree(group);
            // 好友面板（8-6-2）：开才预热字形（好友行名/标题/页号标签），批前须合帧（同组队面板模式）。
            if (friend != null && friend.Visible) UiText.WarmTree(friend);
            // 行会面板（8-6-3）：开才预热字形（公告 25 行标签/状态页标签），批前须合帧（同组队面板模式）。
            if (guild != null && guild.Visible) UiText.WarmTree(guild);
            // 交易面板（8-7-1）：开才预热字形（双方名称/金币标签），批前须合帧（同组队面板模式）。
            if (trade != null && trade.Visible) UiText.WarmTree(trade);
            if (guest != null && guest.Visible) UiText.WarmTree(guest);
            // 邮件五窗（8-7-2）：开才预热字形（列表行发件人/摘要、读信正文、寄包裹金额/邮资标签），
            // 批前须合帧（同交易面板模式）。寄包裹窗开时背包面板通常同开（选物放入），背包已预热。
            if (mailList != null && mailList.Visible) UiText.WarmTree(mailList);
            if (compLetter != null && compLetter.Visible) UiText.WarmTree(compLetter);
            if (compParcel != null && compParcel.Visible) UiText.WarmTree(compParcel);
            if (readLetter != null && readLetter.Visible) UiText.WarmTree(readLetter);
            if (readParcel != null && readParcel.Visible) UiText.WarmTree(readParcel);
            if (market != null && market.Visible) UiText.WarmTree(market); // 拍卖行（8-7-3）：筛选树/行标签/搜索框批前合帧
            if (gameShop != null && gameShop.Visible) UiText.WarmTree(gameShop); // 商城（8-7-4）：商品格/价格/库存/分页标签批前合帧
            // 备注浮窗（8-6-2）：MemoDialog 为好友面板独立子窗（Title 209），开才预热字形（多行文本框）。
            var memo = scene != null ? scene.MemoDialog : null;
            if (memo != null && memo.Visible) UiText.WarmTree(memo);
            // 瞬态模态（8-6-1）：MirMessageBox/MirInputBox 挂 scene.Controls 树（Modal=true），移动端无
            // 独立渲染通路，批前统一预热字形；ActiveModal 无缓存槽位，遍历 Controls 即得唯一模态。
            var modalControls = scene != null ? scene.Controls : null;
            if (modalControls != null)
                for (int i = 0; i < modalControls.Count; i++)
                {
                    var mc = modalControls[i];
                    if (mc != null && mc.Visible && mc.Modal) UiText.WarmTree(mc);
                }

            CrystalSpriteBatch.Begin(null, GameRuntime.ScreenW, GameRuntime.ScreenH);
            CrystalSpriteBatch.SetBlend(false, 1f, CrystalBlendMode.NORMAL); // 场景残留 additive 混合会漂白 HUD
            if (main != null) main.Draw();
            if (mini != null) mini.Draw(); // 小地图常驻（右上角，Visible 默认 true），背包面板打开时仍显示
            if (chat != null) chat.Draw(); // 聊天窗常驻底部（8-5-2），输入框/历史滚动均在窗内
            if (inv != null && inv.Visible) inv.Draw();
            if (chr != null && chr.Visible) chr.Draw();
            if (qdia != null && qdia.Visible) qdia.Draw();
            if (qlist != null && qlist.Visible) qlist.Draw();
            if (qdet != null && qdet.Visible) qdet.Draw();
            if (qtrk != null && qtrk.Visible) qtrk.Draw();
            if (bigMap != null && bigMap.Visible) bigMap.Draw();
            if (group != null && group.Visible) group.Draw(); // 组队面板（8-6-1）：与任务/地图同层（背包面板之上）
            if (friend != null && friend.Visible) friend.Draw(); // 好友面板（8-6-2）：与组队同层（背包面板之上）
            if (guild != null && guild.Visible) guild.Draw(); // 行会面板（8-6-3）：与组队/好友同层（背包面板之上）
            if (trade != null && trade.Visible) trade.Draw(); // 交易面板（8-7-1）：与组队/好友同层（背包面板之上）
            if (guest != null && guest.Visible) guest.Draw(); // 对方交易面板（8-7-1）：随本方面板开
            // 邮件五窗（8-7-2）：列表为面板层（同组队/好友/行会），子窗（读/写/寄）为顶层（同备注浮窗）。
            if (mailList != null && mailList.Visible) mailList.Draw();
            if (compLetter != null && compLetter.Visible) compLetter.Draw();
            if (compParcel != null && compParcel.Visible) compParcel.Draw();
            if (readLetter != null && readLetter.Visible) readLetter.Draw();
            if (readParcel != null && readParcel.Visible) readParcel.Draw();
            if (market != null && market.Visible) market.Draw(); // 拍卖行（8-7-3）：与组队/好友同层（背包面板之上）
            if (gameShop != null && gameShop.Visible) gameShop.Draw(); // 商城（8-7-4）：独立顶层窗
            if (memo != null && memo.Visible) memo.Draw(); // 备注浮窗（8-6-2）：好友子窗最顶层
            if (modalControls != null)
                for (int i = 0; i < modalControls.Count; i++)
                {
                    var mc = modalControls[i];
                    if (mc != null && mc.Visible && mc.Modal) mc.Draw(); // 瞬态模态最顶层（组队邀请/成员名输入）
                }
            _bag.Render(_bagTex);
            _equip.Render(_bagTex);
            _quest.Render(_bagTex);
            _map.Render(_bagTex);
            _chat.Render(_chatTex);
            _group.Render(_groupTex);
            _friend.Render(_friendTex);
            _guild.Render(_guildTex);
            _mail.Render(_mailTex);
            _market.Render(_bagTex);
            _gameShop.Render(_bagTex);
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
            _chatTex = SolidTexture((int)MobileChat.ButtonW, (int)MobileChat.ButtonH); // 聊天按钮白色方块（Render tint 上色）
            _groupTex = SolidTexture((int)MobileBag.ButtonW, (int)MobileBag.ButtonH); // 组队按钮白色方块（同背包按钮尺寸）
            _friendTex = SolidTexture((int)MobileBag.ButtonW, (int)MobileBag.ButtonH); // 好友按钮白色方块（同背包按钮尺寸）
            _guildTex = SolidTexture((int)MobileBag.ButtonW, (int)MobileBag.ButtonH); // 行会按钮白色方块（同背包按钮尺寸）
            _mailTex = SolidTexture((int)MobileBag.ButtonW, (int)MobileBag.ButtonH); // 邮件按钮白色方块（同背包按钮尺寸）
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
            var storeDlg = scene != null ? scene.StorageDialog : null;
            var qdia = scene != null ? scene.QuestDiaryDialog : null;
            var qlist = scene != null ? scene.QuestListDialog : null;
            var qdet = scene != null ? scene.QuestDetailDialog : null;
            var bigMap = scene != null ? scene.BigMapDialog : null;
            var group = scene != null ? scene.GroupDialog : null;
            var friend = scene != null ? scene.FriendDialog : null;
            var guild = scene != null ? scene.GuildDialog : null;
            var tradeDlg = scene != null ? scene.TradeDialog : null;
            bool bagOpen = (inv != null && inv.Visible) || (chr != null && chr.Visible) || (npcDlg != null && npcDlg.Visible) || (goodsDlg != null && goodsDlg.Visible) || (storeDlg != null && storeDlg.Visible) || (qdia != null && qdia.Visible) || (qlist != null && qlist.Visible) || (qdet != null && qdet.Visible) || (bigMap != null && bigMap.Visible) || (group != null && group.Visible) || (friend != null && friend.Visible) || (guild != null && guild.Visible) || (tradeDlg != null && tradeDlg.Visible) || (scene != null && scene.MailListDialog != null && scene.MailListDialog.Visible) || (scene != null && scene.MailComposeLetterDialog != null && scene.MailComposeLetterDialog.Visible) || (scene != null && scene.MailComposeParcelDialog != null && scene.MailComposeParcelDialog.Visible) || (scene != null && scene.MailReadLetterDialog != null && scene.MailReadLetterDialog.Visible) || (scene != null && scene.MailReadParcelDialog != null && scene.MailReadParcelDialog.Visible) || (scene != null && scene.TrustMerchantDialog != null && scene.TrustMerchantDialog.Visible) || (scene != null && scene.GameShopDialog != null && scene.GameShopDialog.Visible); // 面板打开期间摇杆停用（按钮仍可点击关闭）
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
                    UiConsumer = (id, ph, ui) => _bag.OnTouch(id, ph, ui) || _equip.OnTouch(id, ph, ui) || _quest.OnTouch(id, ph, ui) || _map.OnTouch(id, ph, ui) || _chat.OnTouch(id, ph, ui) || _group.OnTouch(id, ph, ui) || _friend.OnTouch(id, ph, ui) || _guild.OnTouch(id, ph, ui) || _mail.OnTouch(id, ph, ui) || _market.OnTouch(id, ph, ui) || _gameShop.OnTouch(id, ph, ui), // 背包/装备/任务/地图/聊天/组队/好友/行会/邮件/拍卖行/商城按钮（ui 空间，短路：背包先消费）
                    PanelOpen = bagOpen,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),                       // 可见对话框命中（ui 空间）
                    // 摇杆（raw 空间）→ 地图 tap 判定：Down 清旧目标（任何新触=移动意图或重新指定），
                    // Up 且无拖拽位移（ReleasedWithIntent false）且非 HUD 按钮区 → 地图 tap →
                    // NPC 优先（命中即消费）→ 玩家交易（命中即消费）→ 未命中落回拾取。
                    // TapAt 返回 false（无物品/距离外）即目标保持清空，不发包。
                    Joystick = (id, ph, rawPos) =>
                    {
                        _joystick.OnTouch(id, ph, rawPos);
                        var ui = MobileUiAdapter.ToUiPoint(rawPos);
                        if (ph == JoystickPhase.Down) { _pickup.Cancel(); _autoPath.Cancel(); return; }
                        if (ph == JoystickPhase.Up && !_joystick.ReleasedWithIntent && !_hud.Hit(MobileUiAdapter.ToUi(rawPos)))
                        {
                            var mc = scene != null ? scene.MapControl : null;
                            if (!_npc.TapAt(mc, ui)) if (!_trade.TapAt(mc, ui)) _pickup.TapAt(mc, ui);
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
                _autoPath.Cancel(); // 摇杆移动同样打断自动寻路
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

        // 任务日记开/关（8-4-1）：切换 QuestDiaryDialog.Visible。打开走 Show→DisplayQuests
        // （按 GameScene.User.CurrentQuests 分组重建）+ Cancel 摇杆/HUD/拾取（同背包）；关闭同 Hide。
        // 日志 [mobile] quest-open/close 供 E2E 数据断言。
        void ToggleQuest(bool open)
        {
            var qdia = GameScene.Scene != null ? GameScene.Scene.QuestDiaryDialog : null;
            if (qdia == null) return;
            try
            {
                if (open)
                {
                    if (!qdia.Visible)
                    {
                        qdia.Show();
                        _joystick.Cancel();
                        _hud.Cancel();
                        _pickup.Cancel();
                    }
                }
                else
                {
                    qdia.Hide();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[mobile] quest-toggle {ex.GetType().Name}: {ex.Message}");
            }
            Debug.Log($"[mobile] quest-{(open ? "open" : "close")} visible={qdia.Visible}");
        }

        // 大地图开/关（8-4-2）：切换 BigMapDialog.Visible。打开走 Show→TargetMyLocation→SetTargetMap
        // （当前图已有记录直接显示，否则发 C.RequestMapInfo 等服务端 S.NewMapInfo 回填）+ 面板互斥
        // （关背包/装备）+ Cancel 摇杆/HUD/拾取/寻路；视口点击设自动寻路后 Update 自动关窗在世界走位。
        // 日志 [mobile] map-open/close 供 E2E 数据断言。
        void ToggleBigMap(bool open)
        {
            var bigMap = GameScene.Scene != null ? GameScene.Scene.BigMapDialog : null;
            if (bigMap == null) return;
            try
            {
                if (open)
                {
                    if (!bigMap.Visible)
                    {
                        var inv = GameScene.Scene != null ? GameScene.Scene.InventoryDialog : null;
                        if (inv != null && inv.Visible) inv.Hide();
                        var chr = GameScene.Scene != null ? GameScene.Scene.CharacterDialog : null;
                        if (chr != null && chr.Visible) chr.Hide();
                        bigMap.Show();
                        _joystick.Cancel();
                        _hud.Cancel();
                        _pickup.Cancel();
                        _autoPath.Cancel();
                    }
                }
                else
                {
                    bigMap.Hide();
                    _autoPath.Cancel();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[mobile] map-toggle {ex.GetType().Name}: {ex.Message}");
            }
            Debug.Log($"[mobile] map-{(open ? "open" : "close")} visible={bigMap.Visible}");
        }

        // 组队面板开/关（8-6-1）：切换 GroupDialog.Visible（静态数据由 S.SwitchGroup/AddMember/
        // DeleteMember/DeleteGroup/GroupMembersMap 分发维护）。打开走 Show + 面板互斥（关背包/装备）
        // + Cancel 摇杆/HUD/拾取/寻路。日志 [mobile] group-open/close 供 E2E 数据断言。
        void ToggleGroup(bool open)
        {
            var scene = GameScene.Scene;
            var group = scene != null ? scene.GroupDialog : null;
            if (group == null) return;
            try
            {
                if (open)
                {
                    if (!group.Visible)
                    {
                        var inv = scene != null ? scene.InventoryDialog : null;
                        if (inv != null && inv.Visible) inv.Hide();
                        var chr = scene != null ? scene.CharacterDialog : null;
                        if (chr != null && chr.Visible) chr.Hide();
                        group.Show();
                        _joystick.Cancel();
                        _hud.Cancel();
                        _pickup.Cancel();
                        _autoPath.Cancel();
                    }
                }
                else
                {
                    group.Hide();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[mobile] group-toggle {ex.GetType().Name}: {ex.Message}");
            }
            Debug.Log($"[mobile] group-{(open ? "open" : "close")} visible={group.Visible}");
        }

        // 好友面板开关（8-6-2）：镜像组队面板——开时互斥隐藏背包/角色/组队面板并取消操作，
        // 首次开注入 Whisper seam（移动端私聊弹软键盘）；关只 Hide。
        void ToggleFriend(bool open)
        {
            var scene = GameScene.Scene;
            var friend = scene != null ? scene.FriendDialog : null;
            if (friend == null) return;
            try
            {
                if (open)
                {
                    if (!friend.Visible)
                    {
                        friend.WhisperAction = name => MobileChat.OpenWhisper(GameScene.Scene?.ChatDialog, name);
                        var inv = scene != null ? scene.InventoryDialog : null;
                        if (inv != null && inv.Visible) inv.Hide();
                        var chr = scene != null ? scene.CharacterDialog : null;
                        if (chr != null && chr.Visible) chr.Hide();
                        var group = scene != null ? scene.GroupDialog : null;
                        if (group != null && group.Visible) group.Hide();
                        friend.Show();
                        _joystick.Cancel();
                        _hud.Cancel();
                        _pickup.Cancel();
                        _autoPath.Cancel();
                    }
                }
                else
                {
                    friend.Hide();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[mobile] friend-toggle {ex.GetType().Name}: {ex.Message}");
            }
            Debug.Log($"[mobile] friend-{(open ? "open" : "close")} visible={friend.Visible}");
        }

        // 行会面板开关（8-6-3）：镜像组队面板——开时互斥隐藏背包/角色/组队/好友面板并取消操作。
        // GuildDialog.Show 内置守卫：未加入行会弹 MirMessageBox 提示（不打开面板）；关只 Hide。
        void ToggleGuild(bool open)
        {
            var scene = GameScene.Scene;
            var guild = scene != null ? scene.GuildDialog : null;
            if (guild == null) return;
            try
            {
                if (open)
                {
                    if (!guild.Visible)
                    {
                        var inv = scene != null ? scene.InventoryDialog : null;
                        if (inv != null && inv.Visible) inv.Hide();
                        var chr = scene != null ? scene.CharacterDialog : null;
                        if (chr != null && chr.Visible) chr.Hide();
                        var group = scene != null ? scene.GroupDialog : null;
                        if (group != null && group.Visible) group.Hide();
                        var friend = scene != null ? scene.FriendDialog : null;
                        if (friend != null && friend.Visible) friend.Hide();
                        guild.Show();
                        _joystick.Cancel();
                        _hud.Cancel();
                        _pickup.Cancel();
                        _autoPath.Cancel();
                    }
                }
                else
                {
                    guild.Hide();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[mobile] guild-toggle {ex.GetType().Name}: {ex.Message}");
            }
            Debug.Log($"[mobile] guild-{(open ? "open" : "close")} visible={guild.Visible}");
        }

        // 邮件列表开/关（8-7-2）：切换 MailListDialog.Visible。开走 Show→UpdateInterface（刷新
        // User.Mail 分页/行，S.ReceiveMail 已维护排序）+ 面板互斥（关背包/装备/组队/好友/行会）
        // + Cancel 摇杆/HUD/拾取/寻路。关只 Hide（子窗读/写/寄由各自按钮或 Back 关）。
        void ToggleMail(bool open)
        {
            var scene = GameScene.Scene;
            var mail = scene != null ? scene.MailListDialog : null;
            if (mail == null) return;
            try
            {
                if (open)
                {
                    if (!mail.Visible)
                    {
                        var inv = scene != null ? scene.InventoryDialog : null;
                        if (inv != null && inv.Visible) inv.Hide();
                        var chr = scene != null ? scene.CharacterDialog : null;
                        if (chr != null && chr.Visible) chr.Hide();
                        var group = scene != null ? scene.GroupDialog : null;
                        if (group != null && group.Visible) group.Hide();
                        var friend = scene != null ? scene.FriendDialog : null;
                        if (friend != null && friend.Visible) friend.Hide();
                        var guild = scene != null ? scene.GuildDialog : null;
                        if (guild != null && guild.Visible) guild.Hide();
                        mail.Show();
                        _joystick.Cancel();
                        _hud.Cancel();
                        _pickup.Cancel();
                        _autoPath.Cancel();
                    }
                }
                else
                {
                    mail.Hide();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[mobile] mail-toggle {ex.GetType().Name}: {ex.Message}");
            }
            Debug.Log($"[mobile] mail-{(open ? "open" : "close")} visible={mail.Visible}");
        }

        // 拍卖行开/关（8-7-3）：切换 TrustMerchantDialog.Visible。开走 Show()——内部
        // TMerchantDialog(Market) 发 C.MarketSearch 拉首页 + 开背包（对齐旧客户端 NPC 入口语义）
        // + 面板互斥（关装备/组队/好友/行会/邮件列表；背包保持开——市场面板选物寄售需要）
        // + Cancel 摇杆/HUD/拾取/寻路。关只 Hide（Hide 清 Listings + 解寄售源格锁 + 背包归位，
        // 对话框自身语义）。
        void ToggleMarket(bool open)
        {
            var scene = GameScene.Scene;
            var market = scene != null ? scene.TrustMerchantDialog : null;
            if (market == null) return;
            try
            {
                if (open)
                {
                    if (!market.Visible)
                    {
                        var chr = scene != null ? scene.CharacterDialog : null;
                        if (chr != null && chr.Visible) chr.Hide();
                        var group = scene != null ? scene.GroupDialog : null;
                        if (group != null && group.Visible) group.Hide();
                        var friend = scene != null ? scene.FriendDialog : null;
                        if (friend != null && friend.Visible) friend.Hide();
                        var guild = scene != null ? scene.GuildDialog : null;
                        if (guild != null && guild.Visible) guild.Hide();
                        var mail = scene != null ? scene.MailListDialog : null;
                        if (mail != null && mail.Visible) mail.Hide();
                        market.Show();
                        _joystick.Cancel();
                        _hud.Cancel();
                        _pickup.Cancel();
                        _autoPath.Cancel();
                    }
                }
                else
                {
                    market.Hide();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[mobile] market-toggle {ex.GetType().Name}: {ex.Message}");
            }
            Debug.Log($"[mobile] market-{(open ? "open" : "close")} visible={market.Visible}");
        }

        // 商城开/关（8-7-4）：切换 GameShopDialog.Visible。开走 Show()——内部 ClassFilter=玩家职业 +
        // ResetTabs/ResetClass/GetCategories 本地过滤重建（无发包，商品由服务器 StartGame 时
        // S.GameShopInfo 推送填充）+ 面板互斥（关背包/装备/组队/好友/行会/邮件/拍卖行；商城
        // 无需背包——对比市场选物寄售需要背包保持开）+ Cancel 摇杆/HUD/拾取/寻路。关只 Hide。
        void ToggleGameShop(bool open)
        {
            var scene = GameScene.Scene;
            var shop = scene != null ? scene.GameShopDialog : null;
            if (shop == null) return;
            try
            {
                if (open)
                {
                    if (!shop.Visible)
                    {
                        var inv = scene != null ? scene.InventoryDialog : null;
                        if (inv != null && inv.Visible) inv.Hide();
                        var chr = scene != null ? scene.CharacterDialog : null;
                        if (chr != null && chr.Visible) chr.Hide();
                        var group = scene != null ? scene.GroupDialog : null;
                        if (group != null && group.Visible) group.Hide();
                        var friend = scene != null ? scene.FriendDialog : null;
                        if (friend != null && friend.Visible) friend.Hide();
                        var guild = scene != null ? scene.GuildDialog : null;
                        if (guild != null && guild.Visible) guild.Hide();
                        var mail = scene != null ? scene.MailListDialog : null;
                        if (mail != null && mail.Visible) mail.Hide();
                        var market = scene != null ? scene.TrustMerchantDialog : null;
                        if (market != null && market.Visible) market.Hide();
                        shop.Show();
                        _joystick.Cancel();
                        _hud.Cancel();
                        _pickup.Cancel();
                        _autoPath.Cancel();
                    }
                }
                else
                {
                    shop.Hide();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[mobile] gameshop-toggle {ex.GetType().Name}: {ex.Message}");
            }
            Debug.Log($"[mobile] gameshop-{(open ? "open" : "close")} visible={shop.Visible}");
        }

        // 瞬态模态查找（8-6-1）：MirMessageBox/MirInputBox 挂 scene.Controls 树且 Modal=true 阻断下层。
        // 移动端无 Esc 键，Back → Esc 语义（YesNo→No 拒绝，Input→Cancel 取消）前先定位最顶层模态。
        static MirControl FindModal(GameScene scene)
        {
            if (scene == null) return null;
            var controls = scene.Controls;
            if (controls == null) return null;
            for (int i = 0; i < controls.Count; i++)
            {
                var c = controls[i];
                if (c != null && c.Visible && c.Modal) return c;
            }
            return null;
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
