using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Client;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Client.MirScenes.Dialogs;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;
using C = ClientPackets;
using S = ServerPackets;
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Rendering.Editor
{
    // P4-M1/P4-M2 探针：Unity 客户端连接真实服务器，服务器零修改。
    // 共享登录状态机：S.Connected → 发 C.ClientVersion(VersionHash=空, 服务器 CheckVersion=False 免校验)
    //   → S.ClientVersion(Result=1) → 发 C.NewAccount（自适应注册：Result=8 成功 / 7 已存在）→ 发 C.Login
    //   → S.LoginSuccess{Characters}。S.Login 任何 Result 均为环境异常（注册后必成功）。
    // RunLogin（P4-M1）：登录成功即完成。
    // RunSelect（P4-M2）：登录后若无角色发 C.NewCharacter 建角色（Name/Gender/Class），C.StartGame{CharacterIndex}
    //   进图 → 断言 S.StartGame(Result=4)+S.MapInformation(FileName 非空)+S.UserInformation(ObjectID>0)。
    // RunGame（P4-M3）：进图后真实服务器对象封包驱动 MapObject（S.ObjectMonster/ObjectNPC → 创建，
    //   S.ObjectTurn/Walk/Run → ActionFeed，S.ObjectRemove → Remove），每帧 Process，收包窗口后复用 R11
    //   渲染（SceneRender.DrawMapTiles + MLibraryUnity.DrawIndex）→ 断言怪物/NPC spawn、移动、渲染非空。
    // RunInteract（P4-M4）：进图后五类确定性双向交互（聊天/背包/NPC对话/拾取/用药），CRYSTAL_COMBAT=1 增战斗
    //   （移动+近战攻击）。每项独立断言+超时窗口，全过即 interact ok。
    // RunLogout（P4-M5）：进图后 C.LogOut → S.LogOutSuccess{Characters} → 同连接重进 C.StartGame →
    //   S.UserInformation（角色持久化证明）。全过即 logout ok。
    // RunDualOpen（P4-M5）：双开互见——A（主 Network）进图后 B（脚本化 raw socket，probe2/probe2b）进图，
    //   A 断言收到 S.ObjectPlayer{B}（B 的 BroadcastInfo）+ S.ObjectWalk{B}（B 环走），B 断言收到
    //   S.ObjectPlayer{A}（网格 Add 分发）。CRYSTAL_SOAK_MS>0 追加持续游玩 soak：B 持续环走+聊天至期限，
    //   A 全程处理封包不断开。全过即 dualopen ok。
    // batchmode 用法：
    //   CRYSTAL_NET_HOST=127.0.0.1 CRYSTAL_NET_PORT=7000 CRYSTAL_LOGIN_ID=probe1 CRYSTAL_LOGIN_PW=probe1
    //   CRYSTAL_CHAR_NAME=probe CRYSTAL_NET_TIMEOUT=60000
    //   CRYSTAL_MAP_DIR=<publish/Maps> CRYSTAL_ATLAS_DIR=<assetcompile/all> CRYSTAL_MAP=nn0.Map
    //   -executeMethod Crystal.Rendering.Editor.NetProbe.RunLogin | .RunSelect | .RunGame | .RunInteract | .RunLogout | .RunDualOpen
    static class NetProbe
    {
        enum Mode { Login, Select, Game, Interact, Logout, DualOpen, Hud, Ui, Bag, UiInput, Npc, Skill, Quest, Team, Market, Hero, Shop, Settings, Edge, CombatAuto }
        enum InteractStep { Init, Chat, Bag, Npc, Pickup, Use, Combat, Done }
        enum LogoutPhase { Entering, WaitLogOut, ReEntering }

        static readonly System.Collections.Generic.List<string> _seq = new System.Collections.Generic.List<string>();
        static readonly System.Collections.Generic.List<string> _all = new System.Collections.Generic.List<string>();
        static long _dumpDeadline = -1;
        static string _id, _pw, _charName, _fail;
        static Mode _mode;
        static bool _done, _ok;
        static int _characters = -1, _charIndex = -1;
        static bool _gameEntered;
        static string _mapFile = "";
        static uint _userObjId;
        static string _userName = "";

        // Game 模式状态
        static string _mapDir, _atlasDir, _mapName, _outPath;
        static int _rtW, _rtH;
        static MapReader _mapReader;
        static bool _mapLoaded;
        static long _gameDeadline = -1;
        static int _monsterCount, _npcCount, _moveCount, _removeCount;
        static int _renderedMonsters, _renderedNPCs, _drawn;

        // CombatAuto 模式状态（阶段8 增量2：MobileCombat 自动战斗接真实服务器 E2E）。
        // 进图后 new MobileCombat（真实 Network.Enqueue），Tick 驱动索敌→追击→攻击→击杀。
        static MobileCombat _combat;
        static int _combatKills;    // 击杀数（S.ObjectDied Type=0 且非玩家）
        static int _combatHits;     // 我方攻击命中数（S.ObjectStruck AttackerID==玩家）
        static int _combatMonsters; // 视野怪物总数（S.ObjectMonster 计数，诊断索敌环境）
        static long _combatDeadline;

        // HUD 模式状态（P4-M5：S.UserInformation 驱动的 HP/MP/Level 状态条叠加渲染）
        static bool _drawHud;
        static int _userHp, _userMp, _userLevel;
        static bool _hudOk;
        static string _hudFail;

        // Ui 模式状态（迭代1：真实 MainDialog+ChatDialog 控制树渲染探针）
        static bool _drawUi;
        static bool _uiOk;
        static string _uiFail;
        static MirClass _userClass;
        static long _userExp, _userMaxExp;

        // Interact 模式状态（P4-M4：五类确定性双向交互 + 可选战斗）
        static InteractStep _istep;
        static long _istepDeadline = -1;
        static bool _chatOk, _bagOk, _npcOk, _pickupOk, _useOk, _combatOk;
        static bool _combatEnabled;
        static MPoint _userLoc;
        static MirDirection _userDir;
        static readonly System.Collections.Generic.List<(int slot, ulong uid, int idx)> _inv = new System.Collections.Generic.List<(int slot, ulong uid, int idx)>();
        static readonly System.Collections.Generic.List<(uint id, string name, MPoint loc)> _npcList = new System.Collections.Generic.List<(uint id, string name, MPoint loc)>();
        static readonly System.Collections.Generic.List<(uint id, MPoint loc)> _monList = new System.Collections.Generic.List<(uint id, MPoint loc)>();
        static int _bagA = -1, _bagB = -1;
        static ulong _potionUid;
        static uint _dropObjId;
        static bool _dropSpawned;
        static int _npcTry;
        static long _combatStepDeadline;
        static uint _combatTargetId;
        static MPoint _combatTargetLoc;
        static MirDirection _combatDir;
        static bool _combatWalking, _combatAttacked;
        static int _attackAttempts;
        static System.Collections.Generic.List<(uint id, MPoint loc)> _combatOrder;
        static int _combatOrderIdx;
        static MPoint _walkTarget;
        static int _stuckCount;
        static bool _movePending;
        static long _npcSendDeadline;
        static MPoint _pickupLoc;
        static int _pickupState; // 0=等掉落 1=等走位 2=已发 PickUp 等回复
        static bool _pickupSent;

        // Edge 模式状态（阶段6 补验：del/run/split/revive/recon/autopath/magic 七子模式）。
        // 每子模式为确定性线性状态机，封包钩子推进 _estep，ProcessEdge 处理超时与重连等待。
        enum EdgeStep
        {
            Init,       // 等 BeginEdge（进图完成触发）
            DelDelete,  // del：NewCharacterSuccess 后已发 C.DeleteCharacter，等 S.DeleteCharacterSuccess
            DelRecon,   // del：已断开，等 IP block 窗口后重连，等 S.LoginSuccess 断言 chars==0
            RunWalk,    // run：已发 C.Walk，等 S.UserLocation 确认移动（设服务器 _stepCounter）
            RunGo,      // run：已发 C.Run，等 S.UserLocation 断言 +2 格
            SplitMake,  // split：已发 @make 候选，等 S.GainedItem 判定可叠放
            SplitWait,  // split：已发 C.SplitItem，等 S.SplitItem1
            ReviveDie,  // revive：已发 @die，等 S.Death
            ReviveTown, // revive：已发 C.TownRevive，等 S.Revived
            MagicGive,  // magic：已发 @giveskill，等 S.NewMagic
            MagicCast,  // magic：已发 C.Magic，等 S.Magic{Cast=true}
            ReconGo,    // recon：已硬断 TCP，等 IP block 窗口后重连
            ReconReconn,// recon：重连登录中，等 S.StartGame(4) 重进成功
            AutoWalk,   // autopath：沿 PathFinder 路径逐节点 C.Walk，等 S.UserLocation 推进
            FishMake,   // fishing：已发 @make BlueFishingRod，等 S.NewItemInfo+S.GainedItem 捕获鱼竿
            FishEquip,  // fishing：已发 C.EquipItem{UniqueID=竿,To=Weapon}，等 S.EquipItem 确认
        }
        static string _edgeSub;              // del/run/split/revive/recon/autopath/magic
        static string _edgeSpell = "Haste";
        static EdgeStep _estep = EdgeStep.Init;
        static long _estepDeadline, _reconAt;
        static MirDirection _runDir;
        static MPoint _runWalkLoc;           // Walk 后的基准位置（Run 应 +2）
        static int _runTries;                // run：路径阻塞时换方向重走重跑次数
        static MPoint _reviveLoc, _reviveMapChanged;
        static bool _mapChangedSeen;         // revive：TownRevive 后是否收到 S.MapChanged
        static MPoint _lastLoc;              // autopath 上一节点位置
        static System.Collections.Generic.List<Node> _path;
        static int _pathIdx;
        static MPoint _pathTarget;
        static PathFinder _autoPf;           // autopath 寻路器（重寻路复用）
        static int _autoStuck;               // autopath 连续未推进次数
        static UserItem[] _edgeInv;          // split：S.UserInformation.Inventory 原始引用
        static int _splitTryIdx;             // split：@make 试叠放候选在 _edgeInv 中的下标
        static int _reconPhase;              // recon：1=已断 2=重连中；del：1=已删待重连
        static UserItem _fishRod;            // fishing：S.GainedItem 捕获的鱼竿（Info 已解析）
        static ItemInfo _fishRodInfo;        // fishing：S.NewItemInfo 捕获的鱼竿 Info（真实服务器数据）

        // Logout 模式状态（P4-M5：下线 → S.LogOutSuccess → 重进验证角色持久化）
        static LogoutPhase _logoutPhase;
        static long _logoutDeadline;

        // DualOpen 模式状态（P4-M5：双开互见 + 持续游玩 soak）
        // A = 主 Network seam（断言侧）；B = 探针内脚本化 raw socket（probe2/probe2b）。
        static string _bId, _bPw, _bChar;
        static bool _dualStarted;
        static bool _aSeenPlayerB, _aSeenWalkB;
        static bool _aUILogged;
        static uint _bObjIdFromA;
        static int _aPktCount;
        static int _shopPush; // Shop 模式：RenderShop 前/中到达的 GameShopInfo/GameShopStock 真实推送计数
        static bool _shopFrozen; // Shop 模式：RenderShop 期间冻结实时商城推送，保证合成数据确定性
        static bool _didNewAccount;
        static long _soakMs, _soakDeadline, _settleDeadline;
        static Thread _bThread;
        static bool _bStop;
        static TcpClient _bClient;
        static NetworkStream _bStream;
        static byte[] _bRaw;
        static readonly System.Collections.Generic.List<Packet> _bParsed = new System.Collections.Generic.List<Packet>();
        static int _bChars = -1, _bCharIdx = -1;
        static uint _bObjId;
        static MPoint _bLoc = default(MPoint);
        static int _bWalkCnt;
        static bool _bEntered, _bWalked, _bSawPlayerA, _bDone;
        static string _bErr;

        // soak 诊断：每 10s 打印 CMain.Time vs 真实时间 + keepalive 计数，定位 keepalive 失联
        static readonly System.Diagnostics.Stopwatch _diagSw = System.Diagnostics.Stopwatch.StartNew();
        static long _nextDiag;

        public static void RunLogin() => Run(Mode.Login);

        public static void RunSelect() => Run(Mode.Select);

        public static void RunGame()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-game.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1152);
            _rtH = GetInt("CRYSTAL_RT_H", 640);
            Run(Mode.Game);
        }

        public static void RunInteract()
        {
            _combatEnabled = GetEnv("CRYSTAL_COMBAT", "0") == "1";
            Run(Mode.Interact);
        }

        // 阶段6 补验入口：CRYSTAL_EDGE 选子模式（del/run/split/revive/recon/autopath/magic）。
        // 每子模式真实服务器确定性往返，日志断言 [netprobe] edge ok/fail。net-edge.ps1 编排。
        public static void RunEdge()
        {
            _edgeSub = GetEnv("CRYSTAL_EDGE", "run");
            _edgeSpell = GetEnv("CRYSTAL_EDGE_SPELL", "Haste");
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            // fishing 子模式 RenderFishing 渲染 RT 须初始化（否则 _rtW/_rtH=0 → GetTemporary 抛异常）。
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-fishing.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            Run(Mode.Edge);
        }

        public static void RunLogout() => Run(Mode.Logout);

        public static void RunHud()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-hud.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1152);
            _rtH = GetInt("CRYSTAL_RT_H", 640);
            _drawHud = true;
            Run(Mode.Hud);
        }

        // 迭代1：真实 MainDialog+ChatDialog 控制树渲染探针。
        // 1024×768 RT 与 Settings 同分辨率：MainDialog frame1 (0,616) 1024×152、ChatDialog (230,671) 632×68，
        // ExperienceBar (9,759) 全部落屏内。文本字形在 batch 前经 UiText.WarmTree 预构建（R8 动态字体坑）。
        public static void RunUi()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-ui.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = true;
            Run(Mode.Ui);
        }

        // 迭代包2 入口：登录→StartGame→渲染真实 InventoryDialog+CharacterDialog+Tooltip 控制树（net-bag.ps1 编排）。
        // 复用 Ui 渲染状态字段（_uiOk/_uiFail），Mode.Bag 分支在 Run 循环按 game 模式渲染。
        public static void RunInventory()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-bag.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = true;
            Run(Mode.Bag);
        }

        public static void RunDualOpen()
        {
            _bId = GetEnv("CRYSTAL_B_LOGIN_ID", "probe2");
            _bPw = GetEnv("CRYSTAL_B_LOGIN_PW", "probe2");
            _bChar = GetEnv("CRYSTAL_B_CHAR", "probe2b");
            _soakMs = GetLong("CRYSTAL_SOAK_MS", 0);
            Run(Mode.DualOpen);
        }

        // 迭代包2 输入探针入口：登录→StartGame→合成鼠标/键盘事件驱动真实 MainDialog/ChatDialog/
        // InventoryDialog 控制树（hover/pressed/click/光标）。net-input.ps1 编排。
        public static void RunUiInput()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-input.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = true;
            Run(Mode.UiInput);
        }

        // 迭代包3 入口：登录→StartGame→渲染真实 NPCDialog+NPCGoodsDialog+StorageDialog 控制树
        // （NPC 对话分页/选项按钮、商店 8 格商品列表、仓库 10x16 网格）。net-npc.ps1 编排。
        public static void RunNpc()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-npc.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = true;
            Run(Mode.Npc);
        }

        public static void RunSkill()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-skill.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = true;
            Run(Mode.Skill);
        }

        public static void RunQuest()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-quest.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = true;
            Run(Mode.Quest);
        }

        public static void RunTeam()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-team.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = true;
            Run(Mode.Team);
        }

        public static void RunMarket()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-market.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = true;
            Run(Mode.Market);
        }

        // 迭代包8 入口：登录→StartGame→进图后合成 Hero/Mount 测试数据，驱动英雄+宠物控制树渲染。
        public static void RunHero()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-hero.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = true;
            Run(Mode.Hero);
        }

        // 迭代包9 入口：登录→StartGame→进图后合成商城/打孔镶嵌/指南针/举报测试数据，
        // 驱动 GameShopDialog + SocketDialog + CompassDialog + ReportDialog 控制树渲染。
        public static void RunShop()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-shop.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = true;
            Run(Mode.Shop);
        }

        public static void RunSettings()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-settings.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = true;
            Run(Mode.Settings);
        }

        public static void RunCombatAuto()
        {
            _mapDir = GetEnv("CRYSTAL_MAP_DIR", "Build/Server/publish/Maps");
            _atlasDir = GetEnv("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all");
            _mapName = GetEnv("CRYSTAL_MAP", "nn0.Map");
            _outPath = GetEnv("CRYSTAL_OUT", "Build/net-combatauto.png");
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            _drawUi = false;
            Run(Mode.CombatAuto);
        }

        static void Run(Mode mode)
        {
            _mode = mode;
            string host = GetEnv("CRYSTAL_NET_HOST", "127.0.0.1");
            int port = GetInt("CRYSTAL_NET_PORT", 7000);
            _id = GetEnv("CRYSTAL_LOGIN_ID", "probe1");
            _pw = GetEnv("CRYSTAL_LOGIN_PW", "probe1");
            _charName = GetEnv("CRYSTAL_CHAR_NAME", "probe");
            long timeout = GetLong("CRYSTAL_NET_TIMEOUT", 60000);

            Settings.IPAddress = host;
            Settings.Port = port;
            CMain.Time = 0;
            _didNewAccount = false;
            CMain.LogImpl = UnityEngine.Debug.Log; // 还原旧客户端 CMain.Log（net-game.log 可见 keepalive 诊断）

            Network.OnPacket = OnPacket;
            Network.Connect();

            long deadline = CMain.Time + timeout;
            while (!_done && CMain.Time < deadline)
            {
                Thread.Sleep(50);
                CMain.Time += 50;
                Network.Process();
                if (_mode == Mode.Select && _dumpDeadline > 0 && CMain.Time >= _dumpDeadline)
                {
                    Console.WriteLine($"[netprobe] game-packets ({_all.Count}): {string.Join(",", _all)}");
                    Done(true, null);
                }
                else if (_mode == Mode.Game && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderGame();
                        Done(_ok, _fail);
                    }
                }
                else if (_mode == Mode.Hud && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderGame();
                        Done(_ok && _hudOk, _ok ? _hudFail : _fail);
                    }
                }
                else if (_mode == Mode.Ui && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderUi();
                        Done(_uiOk, _uiFail);
                    }
                }
                else if (_mode == Mode.Bag && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderBag();
                        Done(_uiOk, _uiFail);
                    }
                }
                else if (_mode == Mode.UiInput && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderUiInput();
                        Done(_uiOk, _uiFail);
                    }
                }
                else if (_mode == Mode.Npc && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderNpc();
                        Done(_uiOk, _uiFail);
                    }
                }
                else if (_mode == Mode.Skill && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderSkill();
                        Done(_uiOk, _uiFail);
                    }
                }
                else if (_mode == Mode.Quest && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderQuest();
                        Done(_uiOk, _uiFail);
                    }
                }
                else if (_mode == Mode.Team && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderTeam();
                        Done(_uiOk, _uiFail);
                    }
                }
                else if (_mode == Mode.Market && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderMarket();
                        Done(_uiOk, _uiFail);
                    }
                }
                else if (_mode == Mode.Hero && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderHero();
                        Done(_uiOk, _uiFail);
                    }
                }
                else if (_mode == Mode.Shop && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderShop();
                        Done(_uiOk, _uiFail);
                    }
                }
                else if (_mode == Mode.Settings && _gameEntered)
                {
                    ProcessGameFrame();
                    if (_gameDeadline > 0 && CMain.Time >= _gameDeadline)
                    {
                        RenderSettings();
                        Done(_uiOk, _uiFail);
                    }
                }
                else if (_mode == Mode.CombatAuto && _gameEntered)
                {
                    ProcessCombatFrame();
                    if (_combatKills >= 2)
                    {
                        Console.WriteLine($"[netprobe] combatauto done kills={_combatKills} hits={_combatHits} monsters={_combatMonsters}");
                        Done(true, null);
                    }
                    else if (_combatDeadline > 0 && CMain.Time >= _combatDeadline)
                        Done(false, $"combat-timeout kills={_combatKills} hits={_combatHits} monsters={_combatMonsters}");
                }
                else if (_mode == Mode.Edge)
                {
                    ProcessEdge();
                }
                else if (_mode == Mode.Interact && _gameEntered)
                {
                    ProcessInteract();
                }
                else if (_mode == Mode.Logout && _gameEntered)
                {
                    ProcessLogout();
                }
                else if (_mode == Mode.DualOpen && _gameEntered)
                {
                    ProcessDualOpen();
                    if (_diagSw.ElapsedMilliseconds >= _nextDiag)
                    {
                        _nextDiag = _diagSw.ElapsedMilliseconds + 10000;
                        Console.WriteLine($"[netprobe] diag t={CMain.Time} real={_diagSw.ElapsedMilliseconds} conn={Network.Connected} recv={_aPktCount} ka={Network.KeepAlivesSent} bAlive={(_bThread != null && _bThread.IsAlive)} bParsed={_bParsed.Count}");
                    }
                }
            }

            Network.Disconnect();
            _bStop = true;
            if (!_done) _fail = "timeout";

            string tag = _mode == Mode.Login ? "login" : _mode == Mode.Select ? "select" : _mode == Mode.Game ? "game" : _mode == Mode.Interact ? "interact" : _mode == Mode.Logout ? "logout" : _mode == Mode.Hud ? "hud" : _mode == Mode.Ui ? "ui" : _mode == Mode.Bag ? "bag" : _mode == Mode.UiInput ? "input" : _mode == Mode.Npc ? "npc" : _mode == Mode.Skill ? "skill" : _mode == Mode.Quest ? "quest" : _mode == Mode.Team ? "team" : _mode == Mode.Market ? "market" : _mode == Mode.Hero ? "hero" : _mode == Mode.Shop ? "shop" : _mode == Mode.Settings ? "settings" : _mode == Mode.Edge ? "edge" : _mode == Mode.CombatAuto ? "combatauto" : "dualopen";
            if (_ok)
                Console.WriteLine($"[netprobe] {tag} ok seq={string.Join(">", _seq)}");
            else
                Console.WriteLine($"[netprobe] {tag} fail={_fail} seq={string.Join(">", _seq)}");

            EditorApplication.Exit(_ok ? 0 : 1);
        }

        static void OnPacket(Packet p)
        {
            _all.Add(p.Index.ToString());
            _aPktCount++;
            switch (p.Index)
            {
                case (short)ServerPacketIds.Connected:
                    _seq.Add("Connected");
                    Network.Connected = true;
                    Network.Enqueue(new C.ClientVersion { VersionHash = Array.Empty<byte>() });
                    break;
                case (short)ServerPacketIds.ClientVersion:
                    var cv = (S.ClientVersion)p;
                    _seq.Add($"ClientVersion:{cv.Result}");
                    // 先登录：账号不存在时服务器回 S.Login(Result=3)，届时才建号。
                    // 旧版无条件发 C.NewAccount，每连接计数触发 24h IP ban（Envir.NewAccount AccountsMade>2）。
                    if (cv.Result == 1)
                        Network.Enqueue(new C.Login { AccountID = _id, Password = _pw });
                    else
                        Done(false, "version-rejected");
                    break;
                case (short)ServerPacketIds.NewAccount:
                    var na = (S.NewAccount)p;
                    _seq.Add($"NewAccount:{na.Result}");
                    if (na.Result == 8 || na.Result == 7)
                        Network.Enqueue(new C.Login { AccountID = _id, Password = _pw });
                    else
                        Done(false, "account-create-rejected");
                    break;
                case (short)ServerPacketIds.Login:
                    var lg = (S.Login)p;
                    _seq.Add($"Login:{lg.Result}");
                    if (lg.Result == 3 && !_didNewAccount)
                    {
                        _didNewAccount = true;
                        Network.Enqueue(new C.NewAccount
                        {
                            AccountID = _id,
                            Password = _pw,
                            BirthDate = DateTime.Now
                        });
                    }
                    else
                        Done(false, $"login-rejected:{lg.Result}");
                    break;
                case (short)ServerPacketIds.LoginSuccess:
                    var ls = (S.LoginSuccess)p;
                    _characters = ls.Characters.Count;
                    _seq.Add($"LoginSuccess:{_characters}");
                    if (_mode == Mode.Login)
                        Done(true, null);
                    else if (_mode == Mode.Edge && _edgeSub == "del" && _estep == EdgeStep.DelRecon)
                    {
                        // 软删持久化断言：GetSelectInfo 过滤 Deleted 角色，重连登录后角色列表应为 0
                        _seq.Add($"DelPersisted:{_characters}");
                        Done(_characters == 0, _characters == 0 ? null : "delete-not-persisted");
                    }
                    else if (_characters > 0)
                        StartGame(ls.Characters[0].Index);
                    else
                        Network.Enqueue(new C.NewCharacter { Name = _charName, Gender = MirGender.Male, Class = MirClass.Warrior });
                    break;
                case (short)ServerPacketIds.NewCharacter:
                    var nc = (S.NewCharacter)p;
                    _seq.Add($"NewCharacter:{nc.Result}");
                    Done(false, $"character-create-rejected:{nc.Result}");
                    break;
                case (short)ServerPacketIds.NewCharacterSuccess:
                    var ncs = (S.NewCharacterSuccess)p;
                    _charIndex = ncs.CharInfo.Index;
                    _seq.Add($"NewCharacterSuccess:{_charIndex}");
                    if (_mode == Mode.Edge && _edgeSub == "del")
                    {
                        // 建号后立即软删：C.DeleteCharacter → S.DeleteCharacterSuccess → 断开重连验证持久化
                        _estep = EdgeStep.DelDelete;
                        _estepDeadline = CMain.Time + 15000;
                        _seq.Add("SendDeleteCharacter");
                        Console.WriteLine($"[netprobe] edge del send DeleteCharacter idx={_charIndex}");
                        Network.Enqueue(new C.DeleteCharacter { CharacterIndex = _charIndex });
                    }
                    else
                        StartGame(_charIndex);
                    break;
                case (short)ServerPacketIds.StartGame:
                    var sg = (S.StartGame)p;
                    _seq.Add($"StartGame:{sg.Result}");
                    if (sg.Result == 4)
                    {
                        _gameEntered = true;
                        if (_mode == Mode.CombatAuto)
                        {
                            // MobileCombat gate 依赖 GameSession.State（NetProbe 不跑 GameSession.Process，直接设态）。
                            GameSession.State = GameSessionState.InGame;
                            _combat = new MobileCombat();
                            _combatKills = 0; _combatHits = 0; _combatMonsters = 0;
                            _combatDeadline = CMain.Time + 90000;
                            _seq.Add("CombatAutoStart");
                            Console.WriteLine($"[netprobe] combatauto start deadline={_combatDeadline}");
                        }
                        if (_mode == Mode.Edge && _edgeSub == "recon" && _estep == EdgeStep.ReconReconn)
                        {
                            // 断线重连后重进成功
                            _seq.Add("ReconReentered");
                            Done(true, null);
                        }
                        MaybeDone();
                    }
                    else Done(false, $"startgame-rejected:{sg.Result}");
                    break;
                case (short)ServerPacketIds.LogOutSuccess:
                    var lo = (S.LogOutSuccess)p;
                    _seq.Add($"LogOutSuccess:{lo.Characters.Count}");
                    if (_logoutPhase == LogoutPhase.WaitLogOut)
                    {
                        _logoutPhase = LogoutPhase.ReEntering;
                        _logoutDeadline = CMain.Time + 15000;
                        _seq.Add("ReEnter");
                        StartGame(_charIndex);
                    }
                    break;
                case (short)ServerPacketIds.LogOutFailed:
                    _seq.Add("LogOutFailed");
                    Done(false, "logout-failed");
                    break;
                case (short)ServerPacketIds.MapInformation:
                    var mi = (S.MapInformation)p;
                    _mapFile = mi.FileName ?? "";
                    _seq.Add($"MapInformation:{mi.FileName}");
                    if ((_mode == Mode.Game || _mode == Mode.Hud || _mode == Mode.Ui || _mode == Mode.Bag || _mode == Mode.UiInput || _mode == Mode.Npc || _mode == Mode.Skill || _mode == Mode.Quest || _mode == Mode.Team || _mode == Mode.Market || _mode == Mode.Hero || _mode == Mode.Shop || _mode == Mode.Settings || _mode == Mode.Edge || _mode == Mode.CombatAuto) && !_mapLoaded && _mapFile.Length > 0)
                        LoadMap();
                    MaybeDone();
                    break;
                case (short)ServerPacketIds.UserInformation:
                    var ui = (S.UserInformation)p;
                    _userObjId = ui.ObjectID;
                    _userName = ui.Name ?? "";
                    _userHp = ui.HP;
                    _userMp = ui.MP;
                    _userLevel = ui.Level;
                    _userClass = ui.Class;
                    _userExp = ui.Experience;
                    _userMaxExp = ui.MaxExperience;
                    _seq.Add($"UserInformation:{ui.ObjectID}:{ui.Name}");
                    if (_mode == Mode.DualOpen && !_aUILogged)
                    {
                        _aUILogged = true;
                        _userLoc = new MPoint(ui.Location.X, ui.Location.Y);
                        Console.WriteLine($"[netprobe] A userinfo obj={ui.ObjectID} loc={ui.Location.X},{ui.Location.Y}");
                    }
                    if (_mode == Mode.Logout && _logoutPhase == LogoutPhase.ReEntering)
                    {
                        _seq.Add($"ReEntered:{ui.ObjectID}:{ui.Name}");
                        Done(true, null);
                    }
                    else if (_mode == Mode.Edge && ui.Inventory != null)
                    {
                        _userLoc = new MPoint(ui.Location.X, ui.Location.Y);
                        _edgeInv = ui.Inventory;
                        _seq.Add($"EdgeUser:{ui.Name}@{ui.Location.X},{ui.Location.Y}");
                        if (MapObject.User == null)
                            EnsureUser(ui);
                    }
                    else if ((_mode == Mode.Game || _mode == Mode.Hud || _mode == Mode.Ui || _mode == Mode.Bag || _mode == Mode.UiInput || _mode == Mode.Npc || _mode == Mode.Skill || _mode == Mode.Quest || _mode == Mode.Team || _mode == Mode.Market || _mode == Mode.Hero || _mode == Mode.Shop || _mode == Mode.Settings || _mode == Mode.CombatAuto) && MapObject.User == null)
                        EnsureUser(ui);
                    else if (_mode == Mode.Interact && ui.Inventory != null)
                    {
                        _userLoc = new MPoint(ui.Location.X, ui.Location.Y);
                        _inv.Clear();
                        for (int i = 0; i < ui.Inventory.Length; i++)
                            if (ui.Inventory[i] != null)
                                _inv.Add((i, ui.Inventory[i].UniqueID, ui.Inventory[i].ItemIndex));
                        _seq.Add($"Inventory:{_inv.Count}");
                    }
                    MaybeDone();
                    break;
                case (short)ServerPacketIds.ObjectPlayer:
                    if (_mode == Mode.DualOpen)
                    {
                        var op = (S.ObjectPlayer)p;
                        if (op.ObjectID != _userObjId)
                        {
                            _bObjIdFromA = op.ObjectID;
                            _aSeenPlayerB = true;
                            _seq.Add($"SeenPlayerB:{op.ObjectID}:{op.Name}");
                        }
                    }
                    break;
                case (short)ServerPacketIds.ObjectMonster:
                    if (_mode == Mode.Game || _mode == Mode.Hud || _mode == Mode.CombatAuto)
                    {
                        if (_mode == Mode.CombatAuto) _combatMonsters++;
                        ObjectMonster((S.ObjectMonster)p);
                    }
                    else if (_mode == Mode.Interact)
                    {
                        var om = (S.ObjectMonster)p;
                        _monsterCount++;
                        _monList.Add((om.ObjectID, new MPoint(om.Location.X, om.Location.Y)));
                        _seq.Add($"Mon:{om.ObjectID}");
                    }
                    break;
                case (short)ServerPacketIds.ObjectNpc:
                    if (_mode == Mode.Game || _mode == Mode.Hud) ObjectNpc((S.ObjectNPC)p);
                    else if (_mode == Mode.Interact)
                    {
                        var on = (S.ObjectNPC)p;
                        _npcCount++;
                        _npcList.Add((on.ObjectID, on.Name ?? "", new MPoint(on.Location.X, on.Location.Y)));
                        _seq.Add($"Npc:{on.ObjectID}");
                    }
                    break;
                case (short)ServerPacketIds.ObjectTurn:
                    if (_mode == Mode.Game || _mode == Mode.Hud || _mode == Mode.CombatAuto) ObjectMove((S.ObjectTurn)p, MirAction.Standing);
                    break;
                case (short)ServerPacketIds.ObjectWalk:
                    if (_mode == Mode.Game || _mode == Mode.Hud || _mode == Mode.CombatAuto) ObjectMove((S.ObjectWalk)p, MirAction.Walking);
                    else if (_mode == Mode.Interact) TrackMonster(((S.ObjectWalk)p).ObjectID, new MPoint(((S.ObjectWalk)p).Location.X, ((S.ObjectWalk)p).Location.Y));
                    else if (_mode == Mode.DualOpen && _aSeenPlayerB && ((S.ObjectWalk)p).ObjectID == _bObjIdFromA)
                    {
                        _aSeenWalkB = true;
                        _seq.Add($"WalkB:{((S.ObjectWalk)p).Location.X},{((S.ObjectWalk)p).Location.Y}");
                    }
                    break;
                case (short)ServerPacketIds.ObjectRun:
                    if (_mode == Mode.Game || _mode == Mode.Hud || _mode == Mode.CombatAuto) ObjectMove((S.ObjectRun)p, MirAction.Running);
                    else if (_mode == Mode.Interact) TrackMonster(((S.ObjectRun)p).ObjectID, new MPoint(((S.ObjectRun)p).Location.X, ((S.ObjectRun)p).Location.Y));
                    break;
                case (short)ServerPacketIds.ObjectRemove:
                    if (_mode == Mode.Game || _mode == Mode.Hud || _mode == Mode.CombatAuto) ObjectRemove((S.ObjectRemove)p);
                    else if (_mode == Mode.Interact)
                    {
                        var orm = (S.ObjectRemove)p;
                        if (_istep == InteractStep.Pickup && orm.ObjectID == _dropObjId) PickupComplete();
                        for (int i = _monList.Count - 1; i >= 0; i--)
                            if (_monList[i].id == orm.ObjectID) _monList.RemoveAt(i);
                        if (_combatTargetId == orm.ObjectID && (_combatWalking || _combatAttacked))
                        {
                            _combatWalking = false;
                            _combatAttacked = false;
                            _combatOrder = null;
                            _combatOrderIdx = 0;
                            PickCombatTarget();
                        }
                    }
                    break;
                case (short)ServerPacketIds.UserLocation:
                    if (_mode == Mode.Edge)
                    {
                        var eloc = (S.UserLocation)p;
                        _userLoc = new MPoint(eloc.Location.X, eloc.Location.Y);
                        _userDir = eloc.Direction;
                        if (_edgeSub == "run") OnRunUserLoc();
                        else if (_edgeSub == "autopath") OnAutoPathUserLoc();
                    }
                    else if (_mode == Mode.Interact)
                    {
                        var uloc = (S.UserLocation)p;
                        _userLoc = new MPoint(uloc.Location.X, uloc.Location.Y);
                        _userDir = uloc.Direction;
                        _movePending = false;
                    }
                    else if (_mode == Mode.CombatAuto && MapObject.User != null)
                    {
                        // 移动确认：同步玩家位置（MobileCombat 索敌/追击距离判定依赖 CurrentLocation）。
                        var uac = (S.UserLocation)p;
                        MapObject.User.Movement = new MPoint(uac.Location.X, uac.Location.Y);
                        MapObject.User.CurrentLocation = new MPoint(uac.Location.X, uac.Location.Y);
                    }
                    break;
                case (short)ServerPacketIds.ObjectChat:
                    if (_mode == Mode.Interact)
                    {
                        var oc = (S.ObjectChat)p;
                        if (_istep == InteractStep.Chat && oc.ObjectID == _userObjId && oc.Type == ChatType.Normal && oc.Text.Contains("probe-interact-1"))
                        {
                            _chatOk = true;
                            _seq.Add("ChatOk");
                            NextStep();
                        }
                    }
                    break;
                case (short)ServerPacketIds.MoveItem:
                    if (_mode == Mode.Interact)
                    {
                        var mvi = (S.MoveItem)p;
                        if (_istep == InteractStep.Bag && mvi.Success && mvi.From == _bagA && mvi.To == _bagB)
                        {
                            _bagOk = true;
                            _seq.Add("BagOk");
                            NextStep();
                        }
                    }
                    break;
                case (short)ServerPacketIds.UseItem:
                    if (_mode == Mode.Interact)
                    {
                        var uii = (S.UseItem)p;
                        if (_istep == InteractStep.Use && uii.UniqueID == _potionUid)
                        {
                            _useOk = true;
                            _seq.Add($"UseOk:{uii.Success}");
                            NextStep();
                        }
                    }
                    break;
                case (short)ServerPacketIds.DropItem:
                    if (_mode == Mode.Interact)
                    {
                        var dpi = (S.DropItem)p;
                        if (_istep == InteractStep.Pickup && dpi.Success && dpi.UniqueID == _potionUid)
                            _dropSpawned = true;
                    }
                    break;
                case (short)ServerPacketIds.ObjectItem:
                    if (_mode == Mode.Interact && _istep == InteractStep.Pickup && !_dropSpawned)
                    {
                        var oi = (S.ObjectItem)p;
                        _dropObjId = oi.ObjectID;
                        _pickupLoc = new MPoint(oi.Location.X, oi.Location.Y);
                        _dropSpawned = true;
                        _seq.Add($"DropObj:{oi.ObjectID}@{oi.Location.X},{oi.Location.Y}");
                    }
                    break;
                case (short)ServerPacketIds.GainedItem:
                    if (_mode == Mode.Interact && _istep == InteractStep.Pickup)
                    {
                        PickupComplete();
                    }
                    else if (_mode == Mode.Edge && _edgeSub == "split" && _estep == EdgeStep.SplitMake)
                    {
                        var gi = (S.GainedItem)p;
                        var made = gi.Item;
                        _seq.Add($"MakeGained:{made.ItemIndex}:count={made.Count}");
                        if (made.Count >= 2)
                        {
                            // 可叠放：候选即起始物，@make 已合并进原槽，原 UID 存活且 Count 增加
                            var it = _edgeInv[_splitTryIdx];
                            ulong uid = (it != null && it.ItemIndex == made.ItemIndex) ? it.UniqueID : made.UniqueID;
                            _seq.Add($"Stackable:{made.ItemIndex}@uid={uid}");
                            Console.WriteLine($"[netprobe] edge split stackable idx={made.ItemIndex} count={made.Count} split uid={uid}");
                            SplitById(uid, made.ItemIndex, made.Count);
                            return;
                        }
                        // Count==1 → 不可叠放（StackSize==1 或单件），试下一候选
                        _splitTryIdx++;
                        TrySplitMake();
                    }
                    else if (_mode == Mode.Edge && _edgeSub == "fishing" && _estep == EdgeStep.FishMake)
                    {
                        var gi = (S.GainedItem)p;
                        var made = gi.Item;
                        if (_fishRodInfo == null || made.ItemIndex != _fishRodInfo.Index)
                        {
                            FailEdge("no-rod-info");
                            return;
                        }
                        made.Info = _fishRodInfo;
                        _fishRod = made;
                        _seq.Add($"RodGained:{made.UniqueID}:{_fishRodInfo.Name}:shape={_fishRodInfo.Shape}:rod={_fishRodInfo.IsFishingRod}");
                        Console.WriteLine($"[netprobe] edge fishing gained rod {_fishRodInfo.Name} shape={_fishRodInfo.Shape} uid={made.UniqueID}");
                        if (!_fishRodInfo.IsFishingRod) { FailEdge("made-not-fishing-rod"); return; }
                        _estep = EdgeStep.FishEquip;
                        _estepDeadline = CMain.Time + 15000;
                        _seq.Add("SendEquip");
                        Console.WriteLine($"[netprobe] edge fishing equip rod uid={made.UniqueID} to Weapon");
                        Network.Enqueue(new C.EquipItem { Grid = MirGridType.Inventory, UniqueID = made.UniqueID, To = (int)EquipmentSlot.Weapon });
                        return;
                    }
                    break;
                case (short)ServerPacketIds.NPCResponse:
                    if (_mode == Mode.Interact)
                    {
                        var nr = (S.NPCResponse)p;
                        if (_istep == InteractStep.Npc && nr.Page != null && nr.Page.Count > 0)
                        {
                            _npcOk = true;
                            _seq.Add($"NpcOk:{nr.Page[0].Length}");
                            NextStep();
                        }
                    }
                    break;
                case (short)ServerPacketIds.ObjectAttack:
                    if (_mode == Mode.Interact && _istep == InteractStep.Combat)
                    {
                        var oa = (S.ObjectAttack)p;
                        if (oa.ObjectID == _userObjId) _seq.Add($"OA:{oa.Direction}");
                    }
                    break;
                case (short)ServerPacketIds.ObjectDied:
                    if (_mode == Mode.Interact)
                    {
                        var od = (S.ObjectDied)p;
                        _seq.Add($"Died:{od.ObjectID}");
                    }
                    else if (_mode == Mode.CombatAuto)
                    {
                        var odc = (S.ObjectDied)p;
                        if (odc.ObjectID != _userObjId && odc.Type == 0)
                        {
                            _combatKills++;
                            _seq.Add($"Kill:{odc.ObjectID}");
                            Console.WriteLine($"[netprobe] combatauto kill={odc.ObjectID} total={_combatKills} hits={_combatHits} monsters={_combatMonsters}");
                        }
                    }
                    break;
                case (short)ServerPacketIds.Death:
                    if (_mode == Mode.Interact) _seq.Add("PlayerDeath");
                    else if (_mode == Mode.CombatAuto) Done(false, "player-died");
                    else if (_mode == Mode.Edge && _edgeSub == "revive" && _estep == EdgeStep.ReviveDie)
                    {
                        // 死亡确认 → 发送回城复活 C.TownRevive
                        var dth = (S.Death)p;
                        _reviveLoc = new MPoint(dth.Location.X, dth.Location.Y);
                        _estep = EdgeStep.ReviveTown;
                        _estepDeadline = CMain.Time + 15000;
                        _seq.Add($"PlayerDeath@{_reviveLoc.X},{_reviveLoc.Y}");
                        Console.WriteLine("[netprobe] edge revive send TownRevive");
                        Network.Enqueue(new C.TownRevive());
                    }
                    break;
                case (short)ServerPacketIds.Revived:
                    if (_mode == Mode.Edge && _edgeSub == "revive" && _estep == EdgeStep.ReviveTown)
                    {
                        _seq.Add($"Revived:mapChanged={_mapChangedSeen}");
                        Done(_mapChangedSeen, _mapChangedSeen ? null : "no-mapchanged");
                    }
                    break;
                case (short)ServerPacketIds.MapChanged:
                    if (_mode == Mode.Edge && _edgeSub == "revive")
                    {
                        var mch = (S.MapChanged)p;
                        _reviveMapChanged = new MPoint(mch.Location.X, mch.Location.Y);
                        _mapChangedSeen = true;
                        _seq.Add($"MapChanged:{mch.FileName}@{_reviveMapChanged.X},{_reviveMapChanged.Y}");
                    }
                    break;
                case (short)ServerPacketIds.ObjectRevived:
                    if (_mode == Mode.Edge && _edgeSub == "revive")
                    {
                        var orv = (S.ObjectRevived)p;
                        _seq.Add($"ObjectRevived:{orv.ObjectID}");
                    }
                    break;
                case (short)ServerPacketIds.DeleteCharacterSuccess:
                    if (_mode == Mode.Edge && _edgeSub == "del" && _estep == EdgeStep.DelDelete)
                    {
                        var dcs = (S.DeleteCharacterSuccess)p;
                        _seq.Add($"DeleteCharacterSuccess:{dcs.CharacterIndex}");
                        Console.WriteLine("[netprobe] edge del deleted, disconnect for reconnect verify");
                        _estep = EdgeStep.DelRecon;
                        _reconPhase = 1;
                        _reconAt = CMain.Time + 5500; // IPBlockSeconds=5，等窗口过期
                        Network.Disconnect();
                    }
                    break;
                case (short)ServerPacketIds.SplitItem1:
                    if (_mode == Mode.Edge && _edgeSub == "split" && _estep == EdgeStep.SplitWait)
                    {
                        var si1 = (S.SplitItem1)p;
                        _seq.Add($"SplitItem1:{si1.Success}:{si1.Count}");
                        Console.WriteLine($"[netprobe] edge split success={si1.Success} count={si1.Count}");
                        Done(si1.Success, si1.Success ? null : "split-rejected");
                    }
                    break;
                case (short)ServerPacketIds.NewItemInfo:
                    // fishing：@make 触发 CheckItemInfo → S.NewItemInfo 携带鱼竿真实 ItemInfo（Server.MirDB）
                    if (_mode == Mode.Edge && _edgeSub == "fishing" && _estep == EdgeStep.FishMake)
                    {
                        var nii = (S.NewItemInfo)p;
                        GameScene.ItemInfoList.Add(nii.Info);
                        if (nii.Info.IsFishingRod) _fishRodInfo = nii.Info;
                        _seq.Add($"NewItemInfo:{nii.Info.Index}:{nii.Info.Name}:shape={nii.Info.Shape}");
                        Console.WriteLine($"[netprobe] edge fishing iteminfo idx={nii.Info.Index} name={nii.Info.Name} type={nii.Info.Type} shape={nii.Info.Shape} reqClass={nii.Info.RequiredClass} reqType={nii.Info.RequiredType} reqAmt={nii.Info.RequiredAmount} weight={nii.Info.Weight}");
                    }
                    break;
                case (short)ServerPacketIds.EquipItem:
                    if (_mode == Mode.Edge && _edgeSub == "fishing" && _estep == EdgeStep.FishEquip)
                    {
                        var eq = (S.EquipItem)p;
                        _seq.Add($"EquipItem:{eq.Success}");
                        Console.WriteLine($"[netprobe] edge fishing equip success={eq.Success}");
                        if (!eq.Success) { FailEdge("equip-rejected"); return; }
                        _estep = EdgeStep.Init; // 渲染完即收尾，不再等新包
                        RenderFishing();
                        Done(_uiOk, _uiFail);
                    }
                    break;
                case (short)ServerPacketIds.NewMagic:
                    if (_mode == Mode.Edge && _edgeSub == "magic" && _estep == EdgeStep.MagicGive)
                    {
                        var nm = (S.NewMagic)p;
                        _seq.Add($"NewMagic:{nm.Magic.Spell}");
                        Console.WriteLine($"[netprobe] edge magic learned {nm.Magic.Spell}, cast");
                        SendMagicCast(nm.Magic.Spell);
                    }
                    break;
                case (short)ServerPacketIds.MagicLeveled:
                    // probeedge1 跨轮持久化：GIVESKILL 对已学会技能发 S.MagicLeveled 而非 S.NewMagic
                    if (_mode == Mode.Edge && _edgeSub == "magic" && _estep == EdgeStep.MagicGive)
                    {
                        var ml = (S.MagicLeveled)p;
                        _seq.Add($"MagicLeveled:{ml.Spell}");
                        Console.WriteLine($"[netprobe] edge magic already learned {ml.Spell}, cast");
                        SendMagicCast(ml.Spell);
                    }
                    break;
                case (short)ServerPacketIds.Magic:
                    if (_mode == Mode.Edge && _edgeSub == "magic" && _estep == EdgeStep.MagicCast)
                    {
                        var mg = (S.Magic)p;
                        _seq.Add($"MagicCast:{mg.Spell}:cast={mg.Cast}");
                        Done(mg.Cast, mg.Cast ? null : "magic-cast-false");
                    }
                    break;
                case (short)ServerPacketIds.ObjectMagic:
                    if (_mode == Mode.Edge && _edgeSub == "magic")
                        _seq.Add($"ObjectMagic:{((S.ObjectMagic)p).Spell}:cast={((S.ObjectMagic)p).Cast}");
                    break;
                case (short)ServerPacketIds.ObjectStruck:
                    if (_mode == Mode.Interact && _combatEnabled && _istep == InteractStep.Combat)
                    {
                        var osk = (S.ObjectStruck)p;
                        if (osk.ObjectID == _combatTargetId && osk.AttackerID == _userObjId)
                        {
                            _combatOk = true;
                            _seq.Add("CombatOk");
                            NextStep();
                        }
                    }
                    else if (_mode == Mode.CombatAuto)
                    {
                        // 我方攻击命中计数（自动战斗有效性交叉验证：ObjectDied 击杀 + 此处命中链路）。
                        var oskc = (S.ObjectStruck)p;
                        if (oskc.ObjectID != _userObjId && oskc.AttackerID == _userObjId) _combatHits++;
                    }
                    break;
                case (short)ServerPacketIds.DamageIndicator:
                    if (_mode == Mode.Interact && _istep == InteractStep.Combat)
                    {
                        var di = (S.DamageIndicator)p;
                        if (di.ObjectID == _combatTargetId) _seq.Add($"DI:{di.Type}");
                    }
                    break;
                case (short)ServerPacketIds.Struck:
                    break;
                case (short)ServerPacketIds.GameShopInfo:
                    // 真实服务器 StartGame 后自动推送商品目录；RenderShop 前（对话框未建）/中（_shopFrozen）
                    // 只计数，RenderShop 时统一合成 + 直接调用 handler 验证。
                    if (!_shopFrozen && GameScene.Scene != null && GameScene.Scene.GameShopDialog != null)
                    {
                        var gsi = (S.GameShopInfo)p;
                        GameScene.Scene.GameShopUpdate(gsi);
                    }
                    else _shopPush++;
                    break;
                case (short)ServerPacketIds.GameShopStock:
                    if (!_shopFrozen && GameScene.Scene != null && GameScene.Scene.GameShopDialog != null)
                        GameScene.Scene.GameShopStock((S.GameShopStock)p);
                    else _shopPush++;
                    break;
                case (short)ServerPacketIds.Disconnect:
                    _seq.Add("Disconnect");
                    Done(false, "server-disconnect");
                    break;
            }
        }

        static void ObjectMonster(S.ObjectMonster p)
        {
            _monsterCount++;
            EnsureObjectLib((ushort)p.Image, Libraries.Monsters, $"Monster/{(ushort)p.Image:D3}");
            if (MapControl.Objects.TryGetValue(p.ObjectID, out var ob) && ob is MonsterObject mob)
            {
                mob.Load(p, true);
                return;
            }

            mob = new MonsterObject(p.ObjectID);
            mob.Load(p, false);
        }

        static void ObjectNpc(S.ObjectNPC p)
        {
            _npcCount++;
            EnsureObjectLib(p.Image, Libraries.NPCs, $"NPC/{p.Image:D2}");
            if (MapControl.Objects.TryGetValue(p.ObjectID, out var ob) && ob is NPCObject npo)
            {
                npo.Load(p);
                return;
            }

            npo = new NPCObject(p.ObjectID);
            npo.Load(p);
        }

        static void EnsureObjectLib(ushort img, MLibrary[] slot, string key)
        {
            if (img >= slot.Length) return;
            var m = SceneRender.EnsureMLibrary(key);
            if (m != null) slot[img] = m;
        }

        static void ObjectMove(object p, MirAction action)
        {
            _moveCount++;
            uint id = 0; int x = 0, y = 0; MirDirection dir = 0;
            if (p is S.ObjectTurn t) { id = t.ObjectID; x = t.Location.X; y = t.Location.Y; dir = t.Direction; }
            else if (p is S.ObjectWalk w) { id = w.ObjectID; x = w.Location.X; y = w.Location.Y; dir = w.Direction; }
            else if (p is S.ObjectRun r) { id = r.ObjectID; x = r.Location.X; y = r.Location.Y; dir = r.Direction; }

            if (MapControl.Objects.TryGetValue(id, out var ob))
                ob.ActionFeed.Add(new QueuedAction { Action = action, Direction = dir, Location = new MPoint(x, y) });
        }

        static void ObjectRemove(S.ObjectRemove p)
        {
            _removeCount++;
            if (MapControl.Objects.TryGetValue(p.ObjectID, out var ob))
                ob.Remove();
        }

        static void LoadMap()
        {
            SceneRender._atlasDir = Path.GetFullPath(_atlasDir);
            SceneRender._mapAtlasDir = Path.GetFullPath(_atlasDir);
            string mapDir = Path.GetFullPath(_mapDir);
            string mapPath = Path.Combine(mapDir, _mapName);
            if (!File.Exists(mapPath)) mapPath = Path.Combine(mapDir, _mapFile + ".Map");
            if (!File.Exists(mapPath)) { Console.WriteLine($"[netprobe] map missing {mapPath}"); Done(false, "map-missing"); return; }

            _mapReader = new MapReader(mapPath);
            var mc = new MapControl
            {
                M2CellInfo = _mapReader.MapCells,
                Width = _mapReader.Width,
                Height = _mapReader.Height,
            };
            mc.PathFinder = new PathFinder(mc); // A* 依赖 mc.EmptyCell（Node.Walkable），须在 M2CellInfo 赋值后构造
            GameScene.Scene = new GameScene { MapControl = mc };
            GameScene.CanMove = true;
            _mapLoaded = true;
            _seq.Add($"MapLoaded:{_mapReader.Width}x{_mapReader.Height}");
        }

        static void EnsureUser(S.UserInformation ui)
        {
            var user = new UserObject(ui.ObjectID)
            {
                Movement = new MPoint(ui.Location.X, ui.Location.Y),
                CurrentLocation = new MPoint(ui.Location.X, ui.Location.Y),
                OffSetMove = MPoint.Empty,
                Name = ui.Name,
            };
            MapObject.User = user;
            _seq.Add($"UserSpawn:{ui.Location.X},{ui.Location.Y}");
        }

        static void ProcessGameFrame()
        {
            if (MapObject.User == null) return;
            foreach (var o in MapControl.ObjectsList)
            {
                if (o == MapObject.User) continue;
                o.Process();
            }
        }

        // CombatAuto 帧驱动：对象动画推进 + MobileCombat 自动战斗（索敌→追击→攻击→击杀）。
        static void ProcessCombatFrame()
        {
            ProcessGameFrame();
            _combat?.Tick();
        }

        static void RenderGame()
        {
            var user = MapObject.User;
            int cx = user.Movement.X, cy = user.Movement.Y;
            int offX = _rtW / 2 / MapControl.CellWidth;
            int offY = _rtH / 2 / MapControl.CellHeight - 1;
            int rangeX = offX + 6, rangeY = offY + 6;
            MapControl.OffSetX = offX;
            MapControl.OffSetY = offY;

            // HUD 文本纹理在 batch 开始前构建：动态字体图集填充需要非渲染上下文（batch 内构建实测字型为空）。
            Texture2D lvTex = null;
            if (_drawHud)
            {
                lvTex = BuildTextTexture("Lv " + _userLevel, 12);
                if (lvTex == null) Console.WriteLine("[netprobe] hud-text: null glyph tex");
            }

            var cells = _mapReader.MapCells;
            var libByIndex = SceneRender.BuildLibIndex(0, cells, _mapReader.Width, _mapReader.Height);

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                var floor = SceneRender.DrawMapTiles(cells, _mapReader, cx, cy, offX, offY, rangeX, rangeY, libByIndex);

                int drawn = 0;
                var sorted = MapControl.ObjectsList.OrderBy(o => o.MapLocation.Y).ThenBy(o => o.MapLocation.X);
                foreach (var o in sorted)
                {
                    if (o == user) continue;
                    if (o.Dead) continue;
                    var lib = o.BodyLibrary as MLibraryUnity;
                    if (lib == null) continue;
                    lib.DrawIndex(o.DrawFrame, o.DrawLocation, o.DrawColour, true, 1f);
                    drawn++;
                }
                if (_drawHud) RenderHud(lvTex);
                CrystalSpriteBatch.End();

                var read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();
                var fl = new Color32[px.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(px, (_rtH - 1 - y) * _rtW, fl, y * _rtW, _rtW);
                read.SetPixels32(fl);
                read.Apply();
                if (_drawHud)
                {
                    // HUD 区域 = 屏幕左上角 (0,0)-(320,70)。px 与 fl 互为垂直翻转，二者之一必为 top-down：
                    // 逐区域各计一次取 max，规避翻转约定歧义（实测 px 为 top-down，fl 为 bottom-up）。
                    Func<Color32, bool> red = c => c.r > 100 && c.r - c.g > 50 && c.r - c.b > 50;
                    Func<Color32, bool> blue = c => c.b > 100 && c.b - c.r > 50;
                    Func<Color32, bool> white = c => c.r > 100 && c.g > 100 && c.b > 100;
                    int hpPx = Mathf.Max(CountRegion(px, 0, 0, 320, 70, red), CountRegion(fl, 0, 0, 320, 70, red));
                    int mpPx = Mathf.Max(CountRegion(px, 0, 0, 320, 70, blue), CountRegion(fl, 0, 0, 320, 70, blue));
                    int lvPx = Mathf.Max(CountRegion(px, 0, 0, 320, 70, white), CountRegion(fl, 0, 0, 320, 70, white));
                    _hudOk = _userLevel > 0 && hpPx > 500 && mpPx > 3 && lvPx > 10;
                    _hudFail = $"hp={_userHp} mp={_userMp} level={_userLevel} hpPx={hpPx} mpPx={mpPx} lvPx={lvPx}";
                    Console.WriteLine($"[netprobe] hud hp={_userHp} mp={_userMp} level={_userLevel} hpPx={hpPx} mpPx={mpPx} lvPx={lvPx} pxTop={px[10 * _rtW + 10]} pxBot={px[(_rtH - 11) * _rtW + 10]}");
                }
                string fullOut = Path.GetFullPath(_outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(read);

                _renderedMonsters = 0; _renderedNPCs = 0;
                foreach (var kv in MapControl.Objects)
                {
                    var o = kv.Value;
                    if (o is MonsterObject mo2 && mo2.BodyLibrary != null) _renderedMonsters++;
                    else if (o is NPCObject npo2 && npo2.BodyLibrary != null) _renderedNPCs++;
                }

                _ok = _monsterCount > 0 && _renderedMonsters > 0 &&
                      _npcCount > 0 && _renderedNPCs > 0 &&
                      _moveCount > 0 && drawn > 0;
                _fail = _ok ? null :
                    $"monsters={_monsterCount}/{_renderedMonsters} npcs={_npcCount}/{_renderedNPCs} moves={_moveCount} removes={_removeCount} drawn={drawn} floor={floor[0]}+{floor[1]}+{floor[2]}";
                Console.WriteLine($"[netprobe] game render monsters={_monsterCount}/{_renderedMonsters} npcs={_npcCount}/{_renderedNPCs} moves={_moveCount} removes={_removeCount} drawn={drawn} wrote={fullOut}");
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        const int HudBarMax = 300;

        // 迭代1 渲染：真实 MainDialog+ChatDialog 控制树。
        // 顺序契约：UiText.Install（标签测量依赖）→ Libraries.Prguse 换 atlas-backed MLibraryUnity（图元/裁剪源）
        // → MainDialog（先建，ChatDialog ctor 读 MainDialog.Location）→ ChatNoticeDialog（Announcement 走 ShowNotice）
        // → Process（S.UserInformation 驱动标签）→ 4 类聊天注入 → StartIndex=0 顶滚显全 4 行 → WarmTree 预构建字形
        // （batch 前）→ 单 batch 渲染 → 数据+像素双重断言。彩色底断言依赖 frame2221 彩色基线全 0（已采样验证）。
        static void RenderUi()
        {
            // ProbeLang.Ensure(); // 中文语言包：推迟到全部任务完成后恢复（见任务 #18）
            UiText.Install();
            UiText.PreWarm(8);

            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "ui:prguse-missing"; return; }
            Libraries.Prguse = prguse;

            var user = MapObject.User;
            if (user == null) { _uiFail = "ui:no-user"; return; }
            // 手动丰富 S.UserInformation 字段，规避 UserObject.Load 的 RefreshStats 重依赖（BuffsDialog/ItemInfoList/player libs）。
            user.HP = _userHp;
            user.MP = _userMp;
            user.Level = (ushort)_userLevel;
            user.Class = _userClass;
            user.Experience = _userExp;
            user.MaxExperience = Math.Max(_userMaxExp, 1);
            user.Stats[Stat.HP] = Math.Max(_userHp, 1);
            user.Stats[Stat.MP] = Math.Max(_userMp, 1);

            GameScene.Scene.ChatNoticeDialog = new ChatNoticeDialog();

            var main = new MainDialog();
            GameScene.Scene.MainDialog = main;
            var chat = new ChatDialog();

            main.Process();

            chat.ReceiveChat("Welcome to Crystal, this is the announcement line", ChatType.Announcement);
            chat.ReceiveChat("System: server online and accepting connections", ChatType.System);
            chat.ReceiveChat("Shout test from the probe character", ChatType.Shout2);
            chat.ReceiveChat("Danger zone ahead, proceed with caution", ChatType.System2);
            chat.StartIndex = 0;
            chat.Update();

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                UiText.WarmTree(main);
                UiText.WarmTree(chat);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                main.Draw();
                chat.Draw();
                CrystalSpriteBatch.End();

                var read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                var fails = new System.Collections.Generic.List<string>();
                bool hpOk = main.HealthLabel.Text == string.Format("HP {0}/{1}", _userHp, Math.Max(_userHp, 1));
                if (!hpOk) fails.Add("hpLabel=" + main.HealthLabel.Text);
                bool lvlOk = main.LevelLabel.Text == _userLevel.ToString();
                if (!lvlOk) fails.Add("lvlLabel=" + main.LevelLabel.Text);
                bool nameOk = main.CharacterName.Text == _userName;
                if (!nameOk) fails.Add("nameLabel=" + main.CharacterName.Text);
                if (string.IsNullOrEmpty(main.ExperienceLabel.Text)) fails.Add("expLabel-empty");
                bool chatOk = chat.FullHistory.Count == 4 && chat.History.Count == 4 && chat.ChatLines.Count == 4;
                if (!chatOk) fails.Add("chat=" + chat.ChatLines.Count + "/" + chat.History.Count);

                // 像素断言：严格白字形（HP/角色名区，frame1 基线 0）+ orb 红区（r-b>20 区分 frame1 红基线）
                // + 聊天面板亮区 + 四行彩色底（蓝/红/绿/暗红，frame2221 彩色基线全 0）。px 为 top-down。
                Func<Color32, bool> strictWhite = c => c.r > 230 && c.g > 230 && c.b > 230;
                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
                Func<Color32, bool> orbRed = c => c.r > 70 && c.r - c.b > 20;
                Func<Color32, bool> blue = c => c.b > 100 && c.b - c.r > 50;
                Func<Color32, bool> red = c => c.r > 170 && c.r - c.g > 80 && c.r - c.b > 80;
                // MColor.Green=(0,128,0)（System.Drawing 同源），g>170 永不命中；阈值下探至 g>100。
                Func<Color32, bool> green = c => c.g > 100 && c.g - c.r > 60 && c.g - c.b > 60;
                Func<Color32, bool> darkRed = c => c.r > 100 && c.r < 170 && c.g < 40 && c.b < 40;

                int hpPx = CountRegion(px, 0, 660, 130, 40, strictWhite);
                int namePx = CountRegion(px, 0, 735, 110, 25, strictWhite);
                int orbPx = CountRegion(px, 0, 646, 105, 84, orbRed);
                int panelPx = CountRegion(px, 230, 671, 632, 68, lit);
                int bluePx = CountRegion(px, 231, 672, 630, 13, blue);
                int redPx = CountRegion(px, 231, 685, 630, 13, red);
                int greenPx = CountRegion(px, 231, 698, 630, 13, green);
                int darkRedPx = CountRegion(px, 231, 711, 630, 13, darkRed);

                if (hpPx < 5) fails.Add("hpPx=" + hpPx);
                if (namePx < 5) fails.Add("namePx=" + namePx);
                if (orbPx < 500) fails.Add("orbPx=" + orbPx);
                if (panelPx < 20000) fails.Add("panelPx=" + panelPx);
                if (bluePx < 50) fails.Add("bluePx=" + bluePx);
                if (redPx < 50) fails.Add("redPx=" + redPx);
                if (greenPx < 50) fails.Add("greenPx=" + greenPx);
                if (darkRedPx < 50) fails.Add("darkRedPx=" + darkRedPx);

                _uiOk = fails.Count == 0;
                _uiFail = $"hp={main.HealthLabel.Text} lvl={main.LevelLabel.Text} name={main.CharacterName.Text} exp={main.ExperienceLabel.Text} chat={chat.ChatLines.Count} hpPx={hpPx} namePx={namePx} orbPx={orbPx} panelPx={panelPx} blue={bluePx} red={redPx} green={greenPx} darkRed={darkRedPx}"
                    + (fails.Count > 0 ? " FAIL:" + string.Join(",", fails) : "");
                Console.WriteLine($"[netprobe] ui {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");

                // R3 行序实证：EncodeToPNG 输出翻转图（PNG row0=RT 底），须先按行翻转再编码
                // （RenderGame 同款 Array.Copy 翻转）。仅影响输出 PNG，上方像素断言用 top-down px 不受影响。
                var uiPx = read.GetPixels32();
                var uiFl = new Color32[uiPx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(uiPx, (_rtH - 1 - y) * _rtW, uiFl, y * _rtW, _rtW);
                read.SetPixels32(uiFl);
                read.Apply();

                string fullOut = Path.GetFullPath(_outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // 迭代包2 背包/装备/Tooltip 渲染探针：登录→StartGame→手工填充用户物品数据→渲染真实
        // InventoryDialog（背包格子/负重条）+ CharacterDialog（装备槽/角色立绘）+ Tooltip
        // （GameScene.CreateItemLabel 内联控制树）→ PNG + 像素断言。net-bag.ps1 编排。
        static void RenderBag()
        {
            // ProbeLang.Ensure(); // 中文语言包：推迟到全部任务完成后恢复（见任务 #18）
            UiText.Install();
            UiText.PreWarm(8);

            // 背包/装备/Tooltip 依赖图集：路径串须匹配 Build/assetcompile/all 文件名（EnsureLib rel+".json"）。
            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "bag:prguse-missing"; return; }
            Libraries.Prguse = prguse;
            var prguse2 = SceneRender.EnsureMLibrary("Prguse2");
            if (prguse2 == null) { _uiFail = "bag:prguse2-missing"; return; }
            Libraries.Prguse2 = prguse2;
            var items = SceneRender.EnsureMLibrary("Items");
            if (items == null) { _uiFail = "bag:items-missing"; return; }
            Libraries.Items = items;
            var stateItems = SceneRender.EnsureMLibrary("Stateitem");
            if (stateItems == null) { _uiFail = "bag:stateitem-missing"; return; }
            Libraries.StateItems = stateItems;
            var title = SceneRender.EnsureMLibrary("Title");
            if (title == null) { _uiFail = "bag:title-missing"; return; }
            Libraries.Title = title;
            var ui = SceneRender.EnsureMLibrary("UI");
            if (ui == null) { _uiFail = "bag:ui-missing"; return; }
            Libraries.UI_32bit = ui;

            var user = MapObject.User;
            if (user == null) { _uiFail = "bag:no-user"; return; }
            // InventoryDialog.Process 走 GameScene.User（static），须与 MapObject.User 对齐。
            GameScene.User = user;
            // 丰富字段，规避 UserObject.Load 的 RefreshStats 重依赖；物品数据手工构造（探针确定性）。
            user.HP = _userHp;
            user.MP = _userMp;
            user.Level = (ushort)_userLevel;
            user.Class = _userClass;
            user.Experience = _userExp;
            user.MaxExperience = Math.Max(_userMaxExp, 1);
            user.Hair = 1;
            user.Gender = MirGender.Male;
            user.Stats[Stat.HP] = Math.Max(_userHp, 1);
            user.Stats[Stat.MP] = Math.Max(_userMp, 1);
            user.Stats[Stat.BagWeight] = 100;
            user.Stats[Stat.WearWeight] = 100;
            user.Stats[Stat.HandWeight] = 100;
            user.CurrentBagWeight = 12;
            user.CurrentWearWeight = 8;
            user.CurrentHandWeight = 4;

            // 测试物品：背包剑（Items[1] 图标帧非空）+ 装备剑（Image=30 落 Stateitem 立绘帧）。
            var bagSwordInfo = new ItemInfo
            {
                Index = 2001,
                Name = "ProbeBagSword",
                Type = ItemType.Weapon,
                Shape = 1,
                Weight = 5,
                Image = 1,
                Durability = 10,
                StackSize = 1,
                Stats = new Stats(),
            };
            bagSwordInfo.Stats[Stat.MaxDC] = 15;
            bagSwordInfo.Stats[Stat.MinDC] = 5;
            var bagSword = new UserItem(bagSwordInfo) { UniqueID = 11, CurrentDura = 10, MaxDura = 10 };

            var equipSwordInfo = new ItemInfo
            {
                Index = 2002,
                Name = "ProbeEquipSword",
                Type = ItemType.Weapon,
                Shape = 1,
                Weight = 8,
                Image = 30,
                Durability = 20,
                StackSize = 1,
                Stats = new Stats(),
            };
            equipSwordInfo.Stats[Stat.MaxDC] = 25;
            equipSwordInfo.Stats[Stat.MinDC] = 8;
            var equipSword = new UserItem(equipSwordInfo) { UniqueID = 12, CurrentDura = 20, MaxDura = 20 };

            // Grid[0].ItemSlot=6（背包第一格跳过 0-5 腰带槽），物品须落到 Inventory[6] 才被 Grid[0] 读取。
            user.Inventory[6] = bagSword;
            user.Equipment[(int)EquipmentSlot.Weapon] = equipSword;

            // 对话框实例化 + 挂 Scene（与旧客户端 GameScene ctor 同源，探针局部持有）。
            var inv = new InventoryDialog { Parent = GameScene.Scene };
            GameScene.Scene.InventoryDialog = inv;
            // ctor 显式 Visible=false（旧客户端由 GameScene 按键打开），探针直接打开。
            inv.Visible = true;
            inv.RefreshInventory();
            inv.Process();

            var chr = new CharacterDialog(MirGridType.Equipment, user) { Parent = GameScene.Scene };
            GameScene.Scene.CharacterDialog = chr;

            // Tooltip：CreateItemLabel 内联构建（黑底 MirControl + 名称/攻防/重量/需求/绑定行），手动定位到 RT 中部。
            GameScene.Scene.CreateItemLabel(equipSword);
            var tip = GameScene.Scene.ItemLabel;
            tip.Location = new MPoint(300, 300);

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                UiText.WarmTree(inv);
                UiText.WarmTree(chr);
                UiText.WarmTree(tip);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                inv.Draw();
                chr.Draw();
                tip.Draw();
                CrystalSpriteBatch.End();

                var read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                var fails = new System.Collections.Generic.List<string>();
                // 背景 Clear(0.1f)=RGB(25,25,25)。lit=区别于背景；strictWhite=白字形；dark<12 仅命中 Tooltip 纯黑底。
                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
                Func<Color32, bool> strictWhite = c => c.r > 230 && c.g > 230 && c.b > 230;
                Func<Color32, bool> dark = c => c.r < 12 && c.g < 12 && c.b < 12;
                // MirItemCell 半透明红粉背景(255,125,125,0.5)填满整格，lit 无法区分有/无图标；
                // 用亮度谓词：白线稿图标(≈604 亮像素) vs 空格(0)，实测 sum>60 分离。
                Func<Color32, bool> bright = c => c.r + c.g + c.b > 60;

                // InventoryDialog 窗口（Title 196）位于 (0,0)；Grid[0] 相对窗口 (9,37) 有图标，Grid[1] (46,37) 空。
                int bagFramePx = CountRegion(px, 0, 0, 320, 260, lit);
                int bagIconPx = CountRegion(px, 9, 37, 36, 32, bright);
                int bagEmptyPx = CountRegion(px, 46, 37, 36, 32, bright);

                // CharacterDialog 窗口（Title 504）位于 (760,0)；角色立绘 Stateitem[30] 画在 DisplayLocation。
                int eqFramePx = CountRegion(px, 760, 0, 264, 330, lit);
                int eqSpritePx = CountRegion(px, 760, 30, 240, 300, lit);

                // Tooltip 黑底 (300,300) 起始；名称行为黄、属性行为白字形（strictWhite）。
                int tipDarkPx = CountRegion(px, 300, 300, 220, 160, dark);
                int tipWhitePx = CountRegion(px, 300, 300, 220, 160, strictWhite);

                if (bagFramePx < 1000) fails.Add("bagFrame=" + bagFramePx);
                if (bagIconPx < 100) fails.Add("bagIcon=" + bagIconPx + "/" + bagEmptyPx);
                if (eqFramePx < 500) fails.Add("eqFrame=" + eqFramePx);
                if (eqSpritePx < 500) fails.Add("eqSprite=" + eqSpritePx);
                if (tipDarkPx < 200) fails.Add("tipDark=" + tipDarkPx);
                if (tipWhitePx < 10) fails.Add("tipWhite=" + tipWhitePx);

                _uiOk = fails.Count == 0;
                _uiFail = $"inv={inv.Grid.Length} eq={chr.Grid.Length} tip={tip.Size.Width}x{tip.Size.Height} bagFrame={bagFramePx} bagIcon={bagIconPx}/{bagEmptyPx} eqFrame={eqFramePx} eqSprite={eqSpritePx} tipDark={tipDarkPx} tipWhite={tipWhitePx}"
                    + (fails.Count > 0 ? " FAIL:" + string.Join(",", fails) : "");
                Console.WriteLine($"[netprobe] bag {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");

                // R3 行序：EncodeToPNG 输出翻转图，编码前按行翻转（RenderUi 同款）。
                var bagPx = read.GetPixels32();
                var bagFl = new Color32[bagPx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(bagPx, (_rtH - 1 - y) * _rtW, bagFl, y * _rtW, _rtW);
                read.SetPixels32(bagFl);
                read.Apply();

                string fullOut = Path.GetFullPath(_outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // fishing 渲染探针（阶段6 补验，net-fishing.ps1 编排）：真实服务器 @make BlueFishingRod →
        // S.NewItemInfo（Server.MirDB 真实 ItemInfo）+ S.GainedItem → C.EquipItem{Weapon} → S.EquipItem{Success=true}
        // 为服务器驱动链；客户端态反射（Equipment[Weapon]+Weapon=shape）后断言 HasFishingRod。S.FishingUpdate
        // 由服务器发送需玩家立于水面（nn0 最近水面 (244,488) 距出生点 128 格）+ 竿内钩/饵，探针在客户端以
        // 真实格式封包回放 Ported PlayerObject.FishingUpdate 验证处理链（Fishing/FishingPoint/FoundFish 状态）。
        // 渲染真实 FishingDialog+FishingStatusDialog 控制树 → 数据+像素双断言 → PNG。
        static void RenderFishing()
        {
            UiText.Install();
            UiText.PreWarm(8);

            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "fish:prguse-missing"; return; }
            Libraries.Prguse = prguse;
            var prguse2 = SceneRender.EnsureMLibrary("Prguse2");
            if (prguse2 == null) { _uiFail = "fish:prguse2-missing"; return; }
            Libraries.Prguse2 = prguse2;
            var stateItems = SceneRender.EnsureMLibrary("Stateitem");
            if (stateItems == null) { _uiFail = "fish:stateitem-missing"; return; }
            Libraries.StateItems = stateItems;
            var title = SceneRender.EnsureMLibrary("Title");
            if (title == null) { _uiFail = "fish:title-missing"; return; }
            Libraries.Title = title;
            var ui = SceneRender.EnsureMLibrary("UI");
            if (ui == null) { _uiFail = "fish:ui-missing"; return; }
            Libraries.UI_32bit = ui;

            var user = MapObject.User;
            if (user == null) { _uiFail = "fish:no-user"; return; }
            if (_fishRod == null || _fishRodInfo == null) { _uiFail = "fish:no-rod"; return; }
            GameScene.User = user;
            // 丰富字段，规避 UserObject.Load 重依赖（RenderBag 同款）。
            user.HP = _userHp;
            user.MP = _userMp;
            user.Level = (ushort)_userLevel;
            user.Class = _userClass;
            user.Experience = _userExp;
            user.MaxExperience = Math.Max(_userMaxExp, 1);
            user.Hair = 1;
            user.Gender = MirGender.Male;
            user.Stats[Stat.HP] = Math.Max(_userHp, 1);
            user.Stats[Stat.MP] = Math.Max(_userMp, 1);
            user.Stats[Stat.BagWeight] = 100;
            user.Stats[Stat.WearWeight] = 100;
            user.Stats[Stat.HandWeight] = 100;
            user.CurrentBagWeight = 12;
            user.CurrentWearWeight = 8;
            user.CurrentHandWeight = 4;
            // 服务器已确认装备（S.EquipItem Success）→ 客户端态反射。
            user.Equipment[(int)EquipmentSlot.Weapon] = _fishRod;
            user.Weapon = _fishRodInfo.Shape;

            // 客户端 HasFishingRod 属性断言（FishingRodShapes.Contains(Weapon)）。
            bool hasRod = user.HasFishingRod;
            _seq.Add($"HasFishingRod:{hasRod}");

            // 对话框实例化 + 挂 Scene；显式 Location 保证像素断言确定性。
            var fishing = new FishingDialog { Parent = GameScene.Scene };
            GameScene.Scene.FishingDialog = fishing;
            fishing.Location = new MPoint(300, 120);
            fishing.Show(); // 需 GameScene.User.HasFishingRod，已满足
            var status = new FishingStatusDialog { Parent = GameScene.Scene };
            GameScene.Scene.FishingStatusDialog = status;
            status.Location = new MPoint(300, 420);
            status.Visible = true;
            status.ChancePercent = 60;
            status.ProgressPercent = 50;

            // S.FishingUpdate 客户端回放：真实格式封包 → Ported PlayerObject.FishingUpdate 处理链
            // （Fishing 状态切换 → QueuedAction FishingCast/FishingReel + FishingPoint + FoundFish）。
            var p1 = new S.FishingUpdate { ObjectID = (uint)_userObjId, Fishing = true, ProgressPercent = 50, ChancePercent = 60, FishingPoint = new System.Drawing.Point(_userLoc.X, _userLoc.Y), FoundFish = false };
            user.FishingUpdate(p1);
            _seq.Add($"Fishing:{user.Fishing}:point={user.FishingPoint.X},{user.FishingPoint.Y}");
            var p2 = new S.FishingUpdate { ObjectID = (uint)_userObjId, Fishing = true, ProgressPercent = 70, ChancePercent = 60, FishingPoint = new System.Drawing.Point(_userLoc.X, _userLoc.Y), FoundFish = true };
            user.FishingUpdate(p2);
            _seq.Add($"FoundFish:{user.FoundFish}");

            // 动画驱动态反射：FishButton 可见/帧数由 MirAction.FishingWait 帧1 处理（PlayerObject.cs:2455-2478），
            // 探针直接置最终态用于渲染（真实客户端动画推进后同样到达此态）。
            status.FishButton.Visible = true;
            status.FishButton.AnimationCount = user.FoundFish ? 10 : 1;

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                UiText.WarmTree(fishing);
                UiText.WarmTree(status);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                fishing.Draw();
                status.Draw();
                CrystalSpriteBatch.End();

                var read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                var fails = new System.Collections.Generic.List<string>();
                // 背景 Clear(0.1f)=RGB(25,25,25)。lit=区别于背景；bright=亮色图标；strictWhite=白字形。
                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
                Func<Color32, bool> bright = c => c.r + c.g + c.b > 60;
                Func<Color32, bool> strictWhite = c => c.r > 230 && c.g > 230 && c.b > 230;

                // FishingDialog frame（Prguse 1340）(300,120)；FishingRod 图 StateItems 1333/1335 (310,160)；
                // TitleLabel 鱼竿名 (310,124)。FishingStatusDialog frame（Prguse 1341）(300,420)；
                // ChanceBar (314,484) 宽 2.16*60≈130；ProgressBar (314,499) 宽 2.16*50≈108；
                // FishButton（Title 170 动画）(347,515)。
                int fishFramePx = CountRegion(px, 300, 120, 240, 300, lit);
                int rodPx = CountRegion(px, 300, 150, 120, 120, lit);
                int fishTitlePx = CountRegion(px, 300, 120, 200, 30, strictWhite);
                int statusFramePx = CountRegion(px, 300, 420, 260, 150, lit);
                int chancePx = CountRegion(px, 314, 484, 130, 14, lit);
                int progressPx = CountRegion(px, 314, 499, 110, 10, lit);
                int fishBtnPx = CountRegion(px, 340, 510, 50, 50, bright);

                if (!hasRod) fails.Add("hasFishingRod=false");
                if (fishFramePx < 1000) fails.Add("fishFrame=" + fishFramePx);
                if (rodPx < 100) fails.Add("rod=" + rodPx);
                if (statusFramePx < 1000) fails.Add("statusFrame=" + statusFramePx);
                if (chancePx < 30) fails.Add("chance=" + chancePx);
                if (progressPx < 30) fails.Add("progress=" + progressPx);
                if (fishBtnPx < 100) fails.Add("fishBtn=" + fishBtnPx);
                if (!user.Fishing) fails.Add("fishing-state=false");
                if (!user.FoundFish) fails.Add("foundfish=false");
                // 鱼竿名文字：字形颜色因图集而异，仅记录不阻断。
                _seq.Add($"TitlePx:{fishTitlePx}");

                _uiOk = fails.Count == 0 && hasRod && user.Fishing && user.FoundFish;
                _uiFail = $"rod={_fishRodInfo.Name}@{_fishRodInfo.Shape} fishFrame={fishFramePx} rodPx={rodPx} statusFrame={statusFramePx} chance={chancePx} progress={progressPx} fishBtn={fishBtnPx}"
                    + (fails.Count > 0 ? " FAIL:" + string.Join(",", fails) : "");
                Console.WriteLine($"[netprobe] fishing {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");

                // R3 行序：EncodeToPNG 输出翻转图，编码前按行翻转（RenderBag 同款）。
                var fishPx = read.GetPixels32();
                var fl = new Color32[fishPx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(fishPx, (_rtH - 1 - y) * _rtW, fl, y * _rtW, _rtW);
                read.SetPixels32(fl);
                read.Apply();

                string fullOut = Path.GetFullPath(_outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // 迭代包3 NPC+商店+仓库渲染探针：登录→StartGame→构造 NPC 对话行/商店商品/仓库快照→渲染真实
        // NPCDialog（对话分页/选项按钮）+ NPCGoodsDialog（8 格商店列表）+ StorageDialog（10x16 仓库网格）
        // 控制树 → 数据+像素断言 → PNG。net-npc.ps1 编排。
        static void RenderNpc()
        {
            // ProbeLang.Ensure(); // 中文语言包：推迟到全部任务完成后恢复（见任务 #18）
            UiText.Install();
            UiText.PreWarm(8);

            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "npc:prguse-missing"; return; }
            Libraries.Prguse = prguse;
            var prguse2 = SceneRender.EnsureMLibrary("Prguse2");
            if (prguse2 == null) { _uiFail = "npc:prguse2-missing"; return; }
            Libraries.Prguse2 = prguse2;
            var items = SceneRender.EnsureMLibrary("Items");
            if (items == null) { _uiFail = "npc:items-missing"; return; }
            Libraries.Items = items;
            var stateItems = SceneRender.EnsureMLibrary("Stateitem");
            if (stateItems == null) { _uiFail = "npc:stateitem-missing"; return; }
            Libraries.StateItems = stateItems;
            var title = SceneRender.EnsureMLibrary("Title");
            if (title == null) { _uiFail = "npc:title-missing"; return; }
            Libraries.Title = title;
            var ui = SceneRender.EnsureMLibrary("UI");
            if (ui == null) { _uiFail = "npc:ui-missing"; return; }
            Libraries.UI_32bit = ui;

            var user = MapObject.User;
            if (user == null) { _uiFail = "npc:no-user"; return; }
            GameScene.User = user;
            // 商店价格显示/购买判定走 GameScene.Gold（MirGoodsCell/BuyItem 引用，旧客户端同源）。
            GameScene.Gold = 10000;

            // 商店商品：两个不同 Index（Buy 面板去重按 Info.Index），同 Image=1（Items 库剑图标已验帧）。
            var shopSwordInfo = new ItemInfo
            {
                Index = 3001,
                Name = "ProbeShopSword",
                Type = ItemType.Weapon,
                Shape = 1,
                Weight = 5,
                Image = 1,
                Price = 100,
                Durability = 10,
                StackSize = 1,
                Stats = new Stats(),
            };
            var shopPotionInfo = new ItemInfo
            {
                Index = 3002,
                Name = "ProbeShopPotion",
                Type = ItemType.Potion,
                Shape = 0,
                Weight = 1,
                Image = 1,
                Price = 20,
                Durability = 1,
                StackSize = 1,
                Stats = new Stats(),
            };
            // 商店商品标记 IsShopItem（UserItem 字段；MirGoodsCell NewIcon 按此隐藏"新品"标记）。
            var shopSword = new UserItem(shopSwordInfo) { UniqueID = 21, CurrentDura = 10, MaxDura = 10, IsShopItem = true };
            var shopPotion = new UserItem(shopPotionInfo) { UniqueID = 22, CurrentDura = 1, MaxDura = 1, IsShopItem = true };

            // 仓库快照：GameScene.Storage 首格放剑（MirItemCell GridType=Storage → ItemArray 读 GameScene.Storage）。
            var storeSwordInfo = new ItemInfo
            {
                Index = 3003,
                Name = "ProbeStoreSword",
                Type = ItemType.Weapon,
                Shape = 1,
                Weight = 5,
                Image = 1,
                Durability = 10,
                StackSize = 1,
                Stats = new Stats(),
            };
            var storeSword = new UserItem(storeSwordInfo) { UniqueID = 23, CurrentDura = 10, MaxDura = 10 };

            // NPC 对话窗：NewText 填充行（含选项按钮 + 长文本触发分页滚动条）。
            var npc = new NPCDialog { Parent = GameScene.Scene };
            GameScene.Scene.NPCDialog = npc;
            npc.NewText(new System.Collections.Generic.List<string>
            {
                "欢迎来到比奇城，冒险者。",
                "这里出售各种冒险所需的补给。",
                "{购买物品/@Buy}",
                "{离开/@Exit}",
            });
            npc.Location = new MPoint(0, 0);
            npc.Visible = true;

            // 商店对话框：Buy 面板，两件商品填充首两格。
            var goods = new NPCGoodsDialog(PanelType.Buy) { Parent = GameScene.Scene };
            GameScene.Scene.NPCGoodsDialog = goods;
            goods.NewGoods(new System.Collections.Generic.List<UserItem> { shopSword, shopPotion });
            goods.Location = new MPoint(445, 0);
            goods.Visible = true;

            // 仓库对话框：Storage[0] 放剑，RefreshStorage1 显示前 80 格。
            GameScene.Storage[0] = storeSword;
            var storage = new StorageDialog { Parent = GameScene.Scene };
            GameScene.Scene.StorageDialog = storage;
            storage.RefreshStorage1();
            storage.Location = new MPoint(0, 400);
            storage.Visible = true;

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                UiText.WarmTree(npc);
                UiText.WarmTree(goods);
                UiText.WarmTree(storage);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                npc.Draw();
                goods.Draw();
                storage.Draw();
                CrystalSpriteBatch.End();

                var read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                var fails = new System.Collections.Generic.List<string>();
                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
                Func<Color32, bool> yellow = c => c.r > 200 && c.g > 200 && c.b < 100;
                Func<Color32, bool> bright = c => c.r + c.g + c.b > 60;

                // NPCDialog（Index 995 Prguse，约 445x205）位于 (0,0)：窗口 frame + 黄色选项按钮文本。
                int npcFramePx = CountRegion(px, 0, 0, 445, 205, lit);
                int npcBtnPx = CountRegion(px, 0, 0, 445, 205, yellow);

                // NPCGoodsDialog（Index 1000 Prguse）位于 (445,0)：窗口 frame + 首格商品图标（cell0 绝对 (455,34)）。
                int goodsFramePx = CountRegion(px, 445, 0, 260, 330, lit);
                int goodsCellPx = CountRegion(px, 455, 34, 205, 32, bright);

                // StorageDialog（Index 586 Prguse）位于 (0,400)：窗口 frame + Grid[0] 剑图标（绝对 (9,460)）。
                int storeFramePx = CountRegion(px, 0, 400, 380, 330, lit);
                int storeCellPx = CountRegion(px, 9, 460, 36, 32, bright);

                if (npcFramePx < 500) fails.Add("npcFrame=" + npcFramePx);
                if (npcBtnPx < 20) fails.Add("npcBtn=" + npcBtnPx);
                if (goodsFramePx < 300) fails.Add("goodsFrame=" + goodsFramePx);
                if (goodsCellPx < 40) fails.Add("goodsCell=" + goodsCellPx);
                if (storeFramePx < 500) fails.Add("storeFrame=" + storeFramePx);
                if (storeCellPx < 20) fails.Add("storeCell=" + storeCellPx);

                _uiOk = fails.Count == 0;
                _uiFail = $"npc={npc.CurrentLines.Count} goods={goods.DisplayGoods.Count} storeGrid={storage.Grid.Length} npcFrame={npcFramePx} npcBtn={npcBtnPx} goodsFrame={goodsFramePx} goodsCell={goodsCellPx} storeFrame={storeFramePx} storeCell={storeCellPx}"
                    + (fails.Count > 0 ? " FAIL:" + string.Join(",", fails) : "");
                Console.WriteLine($"[netprobe] npc {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");

                // R3 行序：EncodeToPNG 输出翻转图，编码前按行翻转（RenderUi/RenderBag 同款）。
                var npcPx = read.GetPixels32();
                var npcFl = new Color32[npcPx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(npcPx, (_rtH - 1 - y) * _rtW, npcFl, y * _rtW, _rtW);
                read.SetPixels32(npcFl);
                read.Apply();

                string fullOut = Path.GetFullPath(_outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // 迭代包4 技能探针：登录→StartGame→构造真实 CharacterDialog 技能页（Magics）+ SkillBarDialog
        // + BuffDialog 控制树 → 合成渲染 → 像素断言技能格/快捷栏/Buff 图标。
        static void RenderSkill()
        {
            // ProbeLang.Ensure(); // 中文语言包：推迟到全部任务完成后恢复（见任务 #18）
            UiText.Install();
            UiText.PreWarm(8);

            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "skill:prguse-missing"; return; }
            Libraries.Prguse = prguse;
            var prguse2 = SceneRender.EnsureMLibrary("Prguse2");
            if (prguse2 == null) { _uiFail = "skill:prguse2-missing"; return; }
            Libraries.Prguse2 = prguse2;
            var title = SceneRender.EnsureMLibrary("Title");
            if (title == null) { _uiFail = "skill:title-missing"; return; }
            Libraries.Title = title;
            var magIcon = SceneRender.EnsureMLibrary("MagIcon");
            if (magIcon == null) { _uiFail = "skill:magicon-missing"; return; }
            Libraries.MagIcon = magIcon;
            var magIcon2 = SceneRender.EnsureMLibrary("MagIcon2");
            if (magIcon2 == null) { _uiFail = "skill:magicon2-missing"; return; }
            Libraries.MagIcon2 = magIcon2;
            var buffIcon = SceneRender.EnsureMLibrary("BuffIcon");
            if (buffIcon == null) { _uiFail = "skill:bufficon-missing"; return; }
            Libraries.BuffIcon = buffIcon;

            var user = MapObject.User;
            if (user == null) { _uiFail = "skill:no-user"; return; }
            GameScene.User = user;

            // 注入两条已分配快捷键的技能（Key=1/2，Bar1 首两格；Icon=1 → 图标帧 2）。
            user.Magics.Clear();
            user.Magics.Add(new ClientMagic
            {
                Name = "ProbeFire",
                Spell = Spell.FireBall,
                Key = 1,
                Level = 1,
                Experience = 100,
                Need1 = 50,
                Need2 = 100,
                Need3 = 200,
                BaseCost = 5,
                LevelCost = 2,
                Icon = 1,
                Delay = 1000,
                CastTime = 0,
            });
            user.Magics.Add(new ClientMagic
            {
                Name = "ProbeThunder",
                Spell = Spell.ThunderBolt,
                Key = 2,
                Level = 0,
                Experience = 10,
                Need1 = 50,
                Need2 = 100,
                Need3 = 200,
                BaseCost = 8,
                LevelCost = 2,
                Icon = 1,
                Delay = 1500,
                CastTime = 0,
            });

            // CharacterDialog 技能页：ShowSkillPage 后 SkillPage 可见，RefreshInterface（BeforeDraw）
            // 按 StartIndex 分页填充 7 格 Magics。
            var chr = new CharacterDialog(MirGridType.Equipment, user) { Parent = GameScene.Scene };
            GameScene.Scene.CharacterDialog = chr;
            chr.ShowSkillPage();
            chr.Visible = true;

            // SkillBarDialog（Bar1）：先 Visible 再 Update（Update 内 !Visible 提前返回），
            // Key=1/2 命中 Cells[0]/[1] 图标 + HasSkill=true。
            var bar = new SkillBarDialog { Parent = GameScene.Scene, BarIndex = 0 };
            GameScene.Scene.SkillBarDialog = bar;
            bar.Visible = true;
            bar.Update();

            // BuffDialog：3 个 Buff 图标（Fury=76/MagicShield=30/Gold=168 均 BuffIcon 库）。
            // CreateBuff 后 Opacity 强制 1（Process 的淡出会归零；图标 Location 叠在原点即可断言）。
            var buffs = new BuffDialog { Parent = GameScene.Scene };
            GameScene.Scene.BuffsDialog = buffs;
            buffs.Visible = true;
            // CreateBuff 只插 _buffList 图标，Buffs 列表由调用方维护（旧 GameScene.CreateBuff 语义）。
            var fury = new ClientBuff { Type = BuffType.Fury, ExpireTime = CMain.Time + 60000, Stats = new Stats { [Stat.AttackSpeed] = 5 } };
            buffs.Buffs.Add(fury);
            buffs.CreateBuff(fury);
            var shield = new ClientBuff { Type = BuffType.MagicShield, ExpireTime = CMain.Time + 120000, Stats = new Stats { [Stat.MaxMC] = 10 } };
            buffs.Buffs.Add(shield);
            buffs.CreateBuff(shield);
            var gold = new ClientBuff { Type = BuffType.Gold, Infinite = true, Stats = new Stats { [Stat.GoldDropRatePercent] = 20 } };
            buffs.Buffs.Add(gold);
            buffs.CreateBuff(gold);
            buffs.Opacity = 1f;

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                UiText.WarmTree(chr);
                UiText.WarmTree(bar);
                UiText.WarmTree(buffs);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                chr.Draw();
                bar.Draw();
                buffs.Draw();
                CrystalSpriteBatch.End();

                var read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                var fails = new System.Collections.Generic.List<string>();
                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
                Func<Color32, bool> strictWhite = c => c.r > 240 && c.g > 240 && c.b > 240;
                Func<Color32, bool> bright = c => c.r + c.g + c.b > 60;

                // CharacterDialog（Title 504，约 264x360）位于 (760,0)：窗口 frame + 技能页背景。
                int charFramePx = CountRegion(px, 760, 0, 264, 360, lit);
                // 技能文本（MagicButton Name/Level/Exp 白字，第 1 格绝对 (776,98)）。
                int skillTextPx = CountRegion(px, 776, 90, 231, 220, strictWhite);
                // 技能图标（MagicButton.SkillButton MagIcon2 Icon*2=2，绝对 (812,98)）。
                int magIconPx = CountRegion(px, 812, 98, 36, 33, bright);
                // SkillBarDialog（Prguse 2190，约 215x31）位于 (0,0)：窗口 frame。
                int barFramePx = CountRegion(px, 0, 0, 215, 31, lit);
                // 快捷栏图标（Cells MagIcon，cell0 绝对 (15,3)，Key=1/2 两格有内容）。
                int barIconPx = CountRegion(px, 15, 3, 200, 25, bright);
                // BuffDialog（Prguse2 20→22，展开 3 格 69x24）CreateBuff 后移动到 (829,0)。
                int buffFramePx = CountRegion(px, 829, 0, 100, 40, lit);
                // Buff 图标（BuffIcon，叠在 BuffDialog 原点附近）。
                int buffIconPx = CountRegion(px, 829, 0, 100, 40, bright);

                if (charFramePx < 800) fails.Add("charFrame=" + charFramePx);
                if (skillTextPx < 30) fails.Add("skillText=" + skillTextPx);
                if (magIconPx < 20) fails.Add("magIcon=" + magIconPx);
                if (barFramePx < 50) fails.Add("barFrame=" + barFramePx);
                if (barIconPx < 30) fails.Add("barIcon=" + barIconPx);
                if (buffFramePx < 15) fails.Add("buffFrame=" + buffFramePx);
                if (buffIconPx < 15) fails.Add("buffIcon=" + buffIconPx);

                _uiOk = fails.Count == 0;
                _uiFail = $"chr={chr.Magics.Length} magics={user.Magics.Count} barHas={bar.HasSkill} buffs={buffs.Buffs.Count} charFrame={charFramePx} skillText={skillTextPx} magIcon={magIconPx} barFrame={barFramePx} barIcon={barIconPx} buffFrame={buffFramePx} buffIcon={buffIconPx}"
                    + (fails.Count > 0 ? " FAIL:" + string.Join(",", fails) : "");
                Console.WriteLine($"[netprobe] skill {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");

                var skillPx = read.GetPixels32();
                var skillFl = new Color32[skillPx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(skillPx, (_rtH - 1 - y) * _rtW, skillFl, y * _rtW, _rtW);
                read.SetPixels32(skillFl);
                read.Apply();

                string fullOut = Path.GetFullPath(_outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // 迭代包5 Quest 探针：登录→StartGame→构造真实 QuestList/QuestDiary/QuestDetail/QuestTracking
        // + BigMap + MiniMapDialog 控制树 → 两遍合成渲染（任务系 + 地图系）→ 像素断言任务行/追踪栏/大地图/小地图。
        // 数据注入：一个 NPC 挂一个可接任务（QuestList 可接）+ 用户已接同任务（QuestDiary/Detail/Tracking）；
        // mmap 页动态选有效 index；BigMapRecord/NPC 行由探针填充（移植版不自动构建）。
        static void RenderQuest()
        {
            // ProbeLang.Ensure(); // 中文语言包：推迟到全部任务完成后恢复（见任务 #18）
            UiText.Install();
            UiText.PreWarm(8);

            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "quest:prguse-missing"; return; }
            Libraries.Prguse = prguse;
            var prguse2 = SceneRender.EnsureMLibrary("Prguse2");
            if (prguse2 == null) { _uiFail = "quest:prguse2-missing"; return; }
            Libraries.Prguse2 = prguse2;
            var title = SceneRender.EnsureMLibrary("Title");
            if (title == null) { _uiFail = "quest:title-missing"; return; }
            Libraries.Title = title;
            var mmLib = SceneRender.EnsureMLibrary("mmap");
            if (mmLib == null) { _uiFail = "quest:mmap-missing"; return; }
            Libraries.MiniMap = mmLib;
            var link = SceneRender.EnsureMLibrary("MapLinkIcon");
            if (link == null) { _uiFail = "quest:maplink-missing"; return; }
            Libraries.MapLinkIcon = link;

            // mmap 图集动态选有效小地图页（MapReader 不暴露 miniMap 编号，页 0..53 盲扫）。
            int mm = 0;
            while (mm < 64 && Libraries.MiniMap.GetSize(mm).Width <= 0) mm++;
            if (mm >= 64) { _uiFail = "quest:mmap-empty"; return; }

            var user = MapObject.User;
            if (user == null) { _uiFail = "quest:no-user"; return; }
            GameScene.User = user;
            // QuestListDialog.ReDisplayButtons 读 MapControl.User（与 MapObject.User 同引用需同步赋值）。
            MapControl.User = user;
            // 等级 15 使 QuestDiary 任务非低等级灰字（(Level-MinLevel)>10 判灰）；Level 由探针覆盖真实角色等级。
            user.Level = 15;
            user.Name = "Probe";
            MapObject.User.CurrentLocation = new MPoint(150, 150);

            // 地图配置：Index/Title/MiniMap/BigMap 由探针注入（MapReader 只暴露 Width/Height）。
            var map = GameScene.Scene.MapControl;
            map.Index = 0;
            map.Title = "ProbeVillage";
            map.MiniMap = mm;
            map.BigMap = mm;

            // 任务/NPC 数据：一个 NPC 挂一个可接任务，用户已接同任务（Taken），供追踪栏/日记/详情显示。
            uint npcId = 2001;
            var npcObject = new NPCObject(npcId);
            // Quests 列表仅由 Load(S.ObjectNPC) 赋值（构造后 null），探针绕过 Load 需自行初始化。
            npcObject.Quests = new System.Collections.Generic.List<ClientQuestInfo>();
            MapControl.Objects[npcId] = npcObject;
            GameScene.NPCID = npcId;

            var questInfo = new ClientQuestInfo
            {
                Index = 1,
                NPCIndex = npcId,
                Name = "Probe Quest",
                Group = "Probe",
                MinLevelNeeded = 5,
                Type = QuestType.General,
                RewardExp = 100,
                RewardGold = 200,
                FinishNPCIndex = npcId
            };
            questInfo.Description.Add("Probe the village outskirts.");
            questInfo.TaskDescription.Add("Gather 5 herbs.");
            questInfo.ReturnDescription.Add("Report back to the elder.");
            npcObject.Quests.Add(questInfo);

            var progress = new ClientQuestProgress
            {
                Id = 1,
                QuestInfo = questInfo,
                Taken = true,
                Completed = false,
                New = false
            };
            progress.TaskList.Add("Gather 5 herbs.");
            user.CurrentQuests.Add(progress);

            for (int j = 0; j < Settings.TrackedQuests.Length; j++)
                Settings.TrackedQuests[j] = -1;

            // 大地图数据：MapInfoList[0] 直接填充（BigMapDialog.SetTargetMap 命中即不再 enqueue 网络请求）。
            var npcInfo = new ClientNPCInfo
            {
                ObjectID = npcId,
                Name = "VillageElder",
                Icon = 0,
                BigMapIcon = 1,
                Location = new System.Drawing.Point(150, 150),
                ShowOnBigMap = true
            };
            var mapInfo = new ClientMapInfo
            {
                Width = map.Width,
                Height = map.Height,
                BigMap = mm,
                Title = "ProbeVillage"
            };
            mapInfo.NPCs.Add(npcInfo);
            var record = new BigMapRecord { Index = 0, MapInfo = mapInfo };
            GameScene.MapInfoList[0] = record;

            // 创建顺序依赖：QuestListDialog 构造读 NPCDialog.Size；QuestSingleQuestItem 读 QuestTrackingDialog。
            var npcDialog = new NPCDialog { Parent = GameScene.Scene };
            GameScene.Scene.NPCDialog = npcDialog;

            var tracking = new QuestTrackingDialog { Parent = GameScene.Scene };
            GameScene.Scene.QuestTrackingDialog = tracking;
            tracking.Location = new MPoint(20, 500);

            var diary = new QuestDiaryDialog { Parent = GameScene.Scene };
            GameScene.Scene.QuestDiaryDialog = diary;
            diary.Location = new MPoint(350, 30);

            var list = new QuestListDialog { Parent = GameScene.Scene };
            GameScene.Scene.QuestListDialog = list;
            list.Location = new MPoint(20, 30);

            var detail = new QuestDetailDialog { Parent = GameScene.Scene };
            GameScene.Scene.QuestDetailDialog = detail;
            detail.Location = new MPoint(680, 30);

            var big = new BigMapDialog { Parent = GameScene.Scene };
            GameScene.Scene.BigMapDialog = big;
            big.Location = new MPoint(20, 30);

            // MainDialog/DuraStatusPanel 为 MiniMapDialog.Process 依赖（SModeLabel 定位 + 耐久面板位置）。
            var main = new MainDialog { Parent = GameScene.Scene };
            GameScene.Scene.MainDialog = main;
            GameScene.Scene.DuraStatusPanel = new MirImageControl();

            var mini = new MiniMapDialog { Parent = GameScene.Scene };
            GameScene.Scene.MiniMapDialog = mini;
            mini.Location = new MPoint(880, 60);

            // 状态推进：任务窗填充 + 大地图 NPC 行（BigMap 移植版不自动构建 NPCButtons，探针注入）。
            // QuestList/QuestDiary 的 Show 带 Visible 守卫（构造默认 Visible=true 时直接 return），
            // 探针直接调内部填充方法（等价旧 GameScene 每帧的 RefreshInterface/DisplayQuests 驱动）。
            diary.DisplayQuests();
            tracking.AddQuest(progress, true);
            list.CurrentNPCID = npcId;
            list.DisplayInfo();
            detail.DisplayQuestDetails(progress);
            var npcRow = new BigMapNPCRow(npcInfo) { Parent = big };
            record.NPCButtons.Add(npcRow);
            big.Show();
            mini.Process();

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                var qFails = new System.Collections.Generic.List<string>();
                var bFails = new System.Collections.Generic.List<string>();
                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
                Func<Color32, bool> strictWhite = c => c.r > 240 && c.g > 240 && c.b > 240;
                Func<Color32, bool> bright = c => c.r + c.g + c.b > 60;
                Func<Color32, bool> lime = c => c.g > 100 && c.g > c.r + 30 && c.g > c.b + 30;
                // 8F 小字号文本抗锯齿下中心像素常低于 240，用宽松亮白（含描边内字芯）。
                Func<Color32, bool> nearWhite = c => c.r > 170 && c.g > 170 && c.b > 170;

                // 遍1：任务系四窗（QuestList/QuestDiary/QuestDetail 平铺 + QuestTracking 底部）。
                UiText.WarmTree(list);
                UiText.WarmTree(diary);
                UiText.WarmTree(detail);
                UiText.WarmTree(tracking);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                list.Draw();
                diary.Draw();
                detail.Draw();
                tracking.Draw();
                CrystalSpriteBatch.End();

                var qRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                qRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                qRead.Apply();
                RenderTexture.active = null;
                var px = qRead.GetPixels32();

                // QuestListDialog（Prguse 950）位于 (20,30)：窗口 frame + 任务行 NameLabel 白字。
                int questListFramePx = CountRegion(px, 20, 30, 310, 460, lit);
                // 任务行 NameLabel（row0 绝对 (89,66)）"Probe Quest"。
                int questRowNamePx = CountRegion(px, 89, 66, 140, 17, nearWhite);
                // QuestDiaryDialog（Prguse 961）位于 (350,30)：分组 lime 标签 (383,70) + 单任务白字 (383,85)。
                int diaryGroupPx = CountRegion(px, 383, 70, 100, 15, lime);
                int diaryTaskPx = CountRegion(px, 383, 85, 280, 15, nearWhite);
                // QuestTrackingDialog（无背景）位于 (20,500)：lime 任务名 (25,520) + 白任务项 (45,535)。
                int trackNamePx = CountRegion(px, 25, 520, 150, 20, lime);
                int trackTaskPx = CountRegion(px, 45, 535, 200, 15, nearWhite);
                // QuestDetailDialog（Prguse 960）位于 (680,30)：窗口 frame。
                int detailFramePx = CountRegion(px, 680, 30, 310, 460, lit);
                // QuestList 奖励区（(25,337) 内 Title 17 装饰 at (45,403)）。
                int rewardDecoPx = CountRegion(px, 45, 403, 40, 30, bright);

                if (questListFramePx < 200) qFails.Add("questListFrame=" + questListFramePx);
                if (questRowNamePx < 15) qFails.Add("questRowName=" + questRowNamePx);
                if (diaryGroupPx < 5) qFails.Add("diaryGroup=" + diaryGroupPx);
                if (diaryTaskPx < 15) qFails.Add("diaryTask=" + diaryTaskPx);
                if (trackNamePx < 5) qFails.Add("trackName=" + trackNamePx);
                if (trackTaskPx < 15) qFails.Add("trackTask=" + trackTaskPx);
                if (detailFramePx < 200) qFails.Add("detailFrame=" + detailFramePx);
                if (rewardDecoPx < 20) qFails.Add("rewardDeco=" + rewardDecoPx);

                // 遍1 渲染存档（调试/验收）：Quest 四窗布局。
                var q1Fl = new Color32[px.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(px, (_rtH - 1 - y) * _rtW, q1Fl, y * _rtW, _rtW);
                qRead.SetPixels32(q1Fl);
                qRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-quest-pass1.png"), qRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(qRead);

                // 遍2：地图系（BigMap 大地图 + MiniMap 小地图）。
                UiText.WarmTree(big);
                UiText.WarmTree(mini);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                big.Draw();
                mini.Draw();
                CrystalSpriteBatch.End();

                var read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px2 = read.GetPixels32();

                // BigMapDialog（Title 820）位于 (20,30)：窗口 frame + mmap 视口 + NPC 行名白字 (633,82)。
                int bigFramePx = CountRegion(px2, 20, 30, 740, 460, lit);
                int bigNpcNamePx = CountRegion(px2, 610, 80, 140, 25, strictWhite);
                // MiniMapDialog（Prguse 2090）位于 (880,60)：窗口 frame + mmap 视口 (883,82) + 坐标白字 (926,191)。
                int miniFramePx = CountRegion(px2, 880, 60, 126, 160, lit);
                int miniViewPx = CountRegion(px2, 883, 82, 120, 108, bright);
                int miniCoordPx = CountRegion(px2, 926, 191, 56, 18, strictWhite);

                if (bigFramePx < 800) bFails.Add("bigFrame=" + bigFramePx);
                if (bigNpcNamePx < 5) bFails.Add("bigNpc=" + bigNpcNamePx);
                if (miniFramePx < 100) bFails.Add("miniFrame=" + miniFramePx);
                if (miniViewPx < 500) bFails.Add("miniView=" + miniViewPx);
                if (miniCoordPx < 5) bFails.Add("miniCoord=" + miniCoordPx);

                _uiOk = qFails.Count == 0 && bFails.Count == 0;
                _uiFail = $"mm={mm} map={map.Width}x{map.Height} npc={npcObject.Quests.Count} taken={user.CurrentQuests.Count} tracked={tracking.TrackedQuestsIds.Count} rows={list.Rows[0]?.Quest != null} diaryGroups={diary.TaskGroups.Count} trackLines={tracking.TaskLines.Count} questListFrame={questListFramePx} questRowName={questRowNamePx} diaryGroup={diaryGroupPx} diaryTask={diaryTaskPx} trackName={trackNamePx} trackTask={trackTaskPx} detailFrame={detailFramePx} rewardDeco={rewardDecoPx} bigFrame={bigFramePx} bigNpc={bigNpcNamePx} miniFrame={miniFramePx} miniView={miniViewPx} miniCoord={miniCoordPx}"
                    + (qFails.Count > 0 ? " FAIL:" + string.Join(",", qFails) : "")
                    + (bFails.Count > 0 ? " FAIL:" + string.Join(",", bFails) : "");
                Console.WriteLine($"[netprobe] quest {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");

                // R3 行序：EncodeToPNG 输出翻转图，编码前按行翻转（RenderSkill 同款）。
                var questPx = read.GetPixels32();
                var questFl = new Color32[questPx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(questPx, (_rtH - 1 - y) * _rtW, questFl, y * _rtW, _rtW);
                read.SetPixels32(questFl);
                read.Apply();

                string fullOut = Path.GetFullPath(_outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // 迭代包6 Team 探针：登录→StartGame→构造真实 GroupDialog/FriendDialog/GuildDialog 控制树
        // → 两遍合成渲染（组队+好友 / 行会）→ 数据+像素断言 → PNG。net-team.ps1 编排。
        // 数据注入：组队 8 成员（队长=用户自己使 Add/Del 可见）+ 允许组队；好友 12 非黑名单
        // （前 5 在线绿字 + 7 离线白字）+ 2 黑名单；行会 GuildName/Level/MemberCount/MaxMembers +
        // 公告文本直设（Notice 滚动方法裁剪，文本由探针注入）。
        // 关键点：GroupDialog 构造内 GroupList.Clear() → 静态数据须在构造后填充；
        // GuildDialog/GroupDialog 的 Show 带 Visible 守卫（构造默认 Visible=true）→ 探针直接渲染，
        // 数据填充靠各自 BeforeDraw 每帧驱动（等价旧 GameScene 循环）。
        static void RenderTeam()
        {
            // ProbeLang.Ensure(); // 中文语言包：推迟到全部任务完成后恢复（见任务 #18）
            UiText.Install();
            UiText.PreWarm(8);

            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "team:prguse-missing"; return; }
            Libraries.Prguse = prguse;
            var prguse2 = SceneRender.EnsureMLibrary("Prguse2");
            if (prguse2 == null) { _uiFail = "team:prguse2-missing"; return; }
            Libraries.Prguse2 = prguse2;
            var title = SceneRender.EnsureMLibrary("Title");
            if (title == null) { _uiFail = "team:title-missing"; return; }
            Libraries.Title = title;

            var user = MapObject.User;
            if (user == null) { _uiFail = "team:no-user"; return; }
            GameScene.User = user;
            // GroupPanel_BeforeDraw / GuildDialog.StatusPage 读 MapControl.User（与 MapObject.User 同步赋值）。
            MapControl.User = user;
            user.Name = "Probe";
            user.Level = 15;
            user.GuildName = "ProbeGuild";

            // 好友数据：12 非黑名单（前 5 在线绿字）+ 2 黑名单（黑名单 tab 默认不显示）。
            var friends = new System.Collections.Generic.List<ClientFriend>();
            for (int i = 0; i < 12; i++)
                friends.Add(new ClientFriend { Index = i + 1, Name = "Friend" + (i + 1), Memo = "Probe memo", Blocked = false, Online = i < 5 });
            friends.Add(new ClientFriend { Index = 13, Name = "Blocked1", Blocked = true, Online = false });
            friends.Add(new ClientFriend { Index = 14, Name = "Blocked2", Blocked = true, Online = true });

            // 备注浮窗实例：FriendDialog.UpdateDisplay/Hide 调 MemoDialog.Hide()（非 null 防 NRE）。
            var memo = new MemoDialog { Parent = GameScene.Scene };
            GameScene.Scene.MemoDialog = memo;

            var group = new GroupDialog { Parent = GameScene.Scene };
            GameScene.Scene.GroupDialog = group;
            group.Location = new MPoint(20, 30);
            // 组队静态数据须在构造后填充（构造内 GroupList.Clear()）。
            GroupDialog.AllowGroup = true;
            GroupDialog.GroupList.Clear();
            GroupDialog.GroupList.Add("Probe");
            for (int i = 1; i < group.GroupMembers.Length; i++)
                GroupDialog.GroupList.Add("Member" + i);
            GroupDialog.GroupMembersMap.Clear();
            GroupDialog.GroupMembersMap["Probe"] = "比奇省(150,150)";
            GroupDialog.GroupMembersMap["Member1"] = "盟重省(100,200)";

            var friend = new FriendDialog { Parent = GameScene.Scene };
            GameScene.Scene.FriendDialog = friend;
            friend.Location = new MPoint(280, 30);
            friend.Friends = friends;
            friend.Update();

            var guild = new GuildDialog { Parent = GameScene.Scene };
            GameScene.Scene.GuildDialog = guild;
            guild.Location = new MPoint(20, 30);
            guild.Level = 3;
            guild.MemberCount = 12;
            guild.MaxMembers = 50;
            guild.Notice.Text = "Welcome to ProbeGuild!";

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                var gFails = new System.Collections.Generic.List<string>();
                var sFails = new System.Collections.Generic.List<string>();
                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
                Func<Color32, bool> strictWhite = c => c.r > 240 && c.g > 240 && c.b > 240;
                Func<Color32, bool> bright = c => c.r + c.g + c.b > 60;
                // 在线好友绿字（Color.Green=(0,128,0)，g 显著高于 r/b）。
                Func<Color32, bool> green = c => c.g > 90 && c.g > c.r + 20 && c.g > c.b + 20;
                // 8F 小字号文本抗锯齿下中心像素常低于 240，用宽松亮白（含描边内字芯）。
                Func<Color32, bool> nearWhite = c => c.r > 170 && c.g > 170 && c.b > 170;

                // 遍1：组队 + 好友（平铺左上）。
                UiText.WarmTree(group);
                UiText.WarmTree(friend);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                group.Draw();
                friend.Draw();
                CrystalSpriteBatch.End();

                var gRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                gRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                gRead.Apply();
                RenderTexture.active = null;
                var px = gRead.GetPixels32();

                // GroupDialog（Prguse 120）位于 (20,30)：frame + 成员0 AutoSize 名白字 (36,63)。
                int groupFramePx = CountRegion(px, 20, 30, 220, 250, lit);
                int groupMemberPx = CountRegion(px, 36, 63, 60, 16, nearWhite);
                // FriendDialog（Title 199）位于 (280,30)：frame + 行0 在线绿字 (296,85) + 行5 离线白字 (411,129)。
                int friendFramePx = CountRegion(px, 280, 30, 255, 250, lit);
                int friendOnlinePx = CountRegion(px, 296, 85, 90, 17, green);
                int friendOfflinePx = CountRegion(px, 411, 129, 90, 17, nearWhite);

                if (groupFramePx < 400) gFails.Add("groupFrame=" + groupFramePx);
                if (groupMemberPx < 10) gFails.Add("groupMember=" + groupMemberPx);
                if (friendFramePx < 400) gFails.Add("friendFrame=" + friendFramePx);
                if (friendOnlinePx < 10) gFails.Add("friendOnline=" + friendOnlinePx);
                if (friendOfflinePx < 10) gFails.Add("friendOffline=" + friendOfflinePx);

                // 遍1 渲染存档（调试/验收）：组队+好友布局。
                var g1Fl = new Color32[px.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(px, (_rtH - 1 - y) * _rtW, g1Fl, y * _rtW, _rtW);
                gRead.SetPixels32(g1Fl);
                gRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-team-pass1.png"), gRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(gRead);

                // 遍2：行会（StatusPage 底图 + 状态标签 + 公告文本）。
                UiText.WarmTree(guild);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                guild.Draw();
                CrystalSpriteBatch.End();

                var read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px2 = read.GetPixels32();

                // GuildDialog（Prguse 180）位于 (20,30)：frame + StatusPage 内
                // StatusGuildName (457,137) / StatusLevel (457,163) / StatusMembers (457,189) + 公告 (33,91)。
                int guildFramePx = CountRegion(px2, 20, 30, 590, 440, lit);
                int guildNamePx = CountRegion(px2, 457, 137, 120, 20, nearWhite);
                int guildLevelPx = CountRegion(px2, 457, 163, 120, 20, nearWhite);
                int guildMembersPx = CountRegion(px2, 457, 189, 120, 20, nearWhite);
                int noticePx = CountRegion(px2, 33, 91, 200, 15, nearWhite);

                if (guildFramePx < 800) sFails.Add("guildFrame=" + guildFramePx);
                if (guildNamePx < 10) sFails.Add("guildName=" + guildNamePx);
                if (guildLevelPx < 5) sFails.Add("guildLevel=" + guildLevelPx);
                if (guildMembersPx < 10) sFails.Add("guildMembers=" + guildMembersPx);
                if (noticePx < 10) sFails.Add("notice=" + noticePx);

                int filledRows = friend.Rows.Count(r => r != null);
                bool member0 = group.GroupMembers[0].Text == "Probe";
                bool member7 = group.GroupMembers[7].Text == "Member7";
                bool row0Online = friend.Rows.Length > 0 && friend.Rows[0] != null && friend.Rows[0].Friend != null && friend.Rows[0].Friend.Online;
                bool row5Offline = friend.Rows.Length > 5 && friend.Rows[5] != null && friend.Rows[5].Friend != null && !friend.Rows[5].Friend.Online;
                if (filledRows != 12) gFails.Add("rows=" + filledRows);
                if (!member0) gFails.Add("member0=" + group.GroupMembers[0].Text);
                if (!member7) gFails.Add("member7=" + group.GroupMembers[7].Text);
                if (!row0Online) gFails.Add("row0Online=false");
                if (!row5Offline) gFails.Add("row5Offline=false");

                _uiOk = gFails.Count == 0 && sFails.Count == 0;
                _uiFail = $"members={group.GroupMembers[0].Text}..{group.GroupMembers[7].Text} allow={GroupDialog.AllowGroup} rows={filledRows} online={row0Online} blocked={friends.Count(f => f.Blocked)} guild={user.GuildName} lv={guild.Level} mem={guild.MemberCount}/{guild.MaxMembers} groupFrame={groupFramePx} groupMember={groupMemberPx} friendFrame={friendFramePx} friendOnline={friendOnlinePx} friendOffline={friendOfflinePx} guildFrame={guildFramePx} guildName={guildNamePx} guildLevel={guildLevelPx} guildMembers={guildMembersPx} notice={noticePx}"
                    + (gFails.Count > 0 ? " FAIL:" + string.Join(",", gFails) : "")
                    + (sFails.Count > 0 ? " FAIL:" + string.Join(",", sFails) : "");
                Console.WriteLine($"[netprobe] team {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");

                // R3 行序：EncodeToPNG 输出翻转图，编码前按行翻转（RenderQuest 同款）。
                var teamPx = read.GetPixels32();
                var teamFl = new Color32[teamPx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(teamPx, (_rtH - 1 - y) * _rtW, teamFl, y * _rtW, _rtW);
                read.SetPixels32(teamFl);
                read.Apply();

                string fullOut = Path.GetFullPath(_outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // 迭代包7 Market 探针：登录→StartGame→构造真实 Trade/GuestTrade + 邮件五窗 +
        // TrustMerchant（Market/Consign 面板）控制树 → 四遍合成渲染 → 数据+像素断言 → PNG。
        // net-market.ps1 编排。
        // 数据注入：交易双方物品/金币（Trade[0]/GuestItems[0] 直设 MirItemCell.Item，绕开
        // ItemArray 的未移植 GridType 分支）；邮件 3 封（普通/金币/带物品）；市场 5 条寄售
        // listing（Sword0-4，Consign 类型显 Seller/Expire）。
        // TrustMerchant 双实例：实例A 走 TMerchantDialog(Market)（DrawFilters 建筛选树 +
        // UpdateInterface 填 Rows，顺带真实发包 MarketSearch 被 OnPacket 忽略）；
        // 实例B 走 TMerchantDialog(Consign)（ItemCell/PriceTextBox/SellItemButton/HelpLabel）。
        static void RenderMarket()
        {
            // ProbeLang.Ensure(); // 中文语言包：推迟到全部任务完成后恢复（见任务 #18）
            UiText.Install();
            UiText.PreWarm(8);

            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "market:prguse-missing"; return; }
            Libraries.Prguse = prguse;
            var prguse2 = SceneRender.EnsureMLibrary("Prguse2");
            if (prguse2 == null) { _uiFail = "market:prguse2-missing"; return; }
            Libraries.Prguse2 = prguse2;
            var items = SceneRender.EnsureMLibrary("Items");
            if (items == null) { _uiFail = "market:items-missing"; return; }
            Libraries.Items = items;
            var stateItems = SceneRender.EnsureMLibrary("Stateitem");
            if (stateItems == null) { _uiFail = "market:stateitem-missing"; return; }
            Libraries.StateItems = stateItems;
            var title = SceneRender.EnsureMLibrary("Title");
            if (title == null) { _uiFail = "market:title-missing"; return; }
            Libraries.Title = title;
            var ui = SceneRender.EnsureMLibrary("UI");
            if (ui == null) { _uiFail = "market:ui-missing"; return; }
            Libraries.UI_32bit = ui;

            var user = MapObject.User;
            if (user == null) { _uiFail = "market:no-user"; return; }
            // TrustMerchant.UpdateInterface 读 GameScene.Gold（TotalGold 标签）；交易读 GameScene.User.Trade。
            GameScene.User = user;
            MapControl.User = user;
            GameScene.Gold = 10000;
            user.Name = "Probe";
            user.TradeLocked = false;
            user.TradeGoldAmount = 5000;
            user.Mail.Clear();

            // 测试物品：剑（Items[1] 图标帧）+ 药水（Items[1]，Shape 0）。
            var swordInfo = new ItemInfo
            {
                Index = 4001,
                Name = "ProbeSword",
                Type = ItemType.Weapon,
                Shape = 1,
                Weight = 5,
                Image = 1,
                Durability = 10,
                StackSize = 1,
                Stats = new Stats(),
            };
            swordInfo.Stats[Stat.MaxDC] = 15;
            swordInfo.Stats[Stat.MinDC] = 5;
            var sword = new UserItem(swordInfo) { UniqueID = 31, CurrentDura = 10, MaxDura = 10 };

            var potionInfo = new ItemInfo
            {
                Index = 4002,
                Name = "ProbePotion",
                Type = ItemType.Potion,
                Shape = 0,
                Weight = 1,
                Image = 1,
                Durability = 1,
                StackSize = 5,
                Stats = new Stats(),
            };
            var potion = new UserItem(potionInfo) { UniqueID = 32, CurrentDura = 1, MaxDura = 1, Count = 3 };

            // 交易数据：本方 Trade[0]=剑/金币 5000；对方 GuestItems[0]=药水/金币 3000。
            user.Trade[0] = sword;

            // InventoryDialog 实例：MailComposeParcelDialog 构造 Location 读 Scene.InventoryDialog.Size；
            // TradeAccept 也引用（探针不触发，仅保证非 null）。
            var inv = new InventoryDialog { Parent = GameScene.Scene };
            GameScene.Scene.InventoryDialog = inv;
            inv.Visible = false;

            // ---------- 遍1：交易（TradeDialog + GuestTradeDialog 平铺左上） ----------
            var trade = new TradeDialog { Parent = GameScene.Scene };
            GameScene.Scene.TradeDialog = trade;
            trade.Location = new MPoint(20, 20);
            trade.Grid[0].Item = sword;
            var guest = new GuestTradeDialog { Parent = GameScene.Scene };
            GameScene.Scene.GuestTradeDialog = guest;
            guest.Location = new MPoint(250, 20);
            guest.GuestName = "Guest";
            guest.GuestGold = 3000;
            GuestTradeDialog.GuestItems[0] = potion;
            trade.RefreshInterface();

            // ---------- 遍2：邮件五窗（MailList + 写/寄 + 读信/读包裹平铺） ----------
            var mailList = new MailListDialog { Parent = GameScene.Scene };
            GameScene.Scene.MailListDialog = mailList;
            mailList.Location = new MPoint(20, 20);

            var letterMail = new ClientMail
            {
                MailID = 1,
                SenderName = "Sender1",
                Message = "Hello probe",
                Opened = true,
                Locked = false,
                CanReply = true,
                Collected = true,
                DateSent = DateTime.Now,
            };
            var goldMail = new ClientMail
            {
                MailID = 2,
                SenderName = "Sender2",
                Message = "Gold parcel",
                Opened = false,
                Locked = true,
                CanReply = false,
                Collected = false,
                DateSent = DateTime.Now,
                Gold = 100,
            };
            var itemMail = new ClientMail
            {
                MailID = 3,
                SenderName = "Sender3",
                Message = "Item parcel",
                Opened = true,
                Locked = false,
                CanReply = false,
                Collected = false,
                DateSent = DateTime.Now,
            };
            itemMail.Items.Add(sword);
            user.Mail.Add(letterMail);
            user.Mail.Add(goldMail);
            user.Mail.Add(itemMail);
            mailList.UpdateInterface();

            var composeLetter = new MailComposeLetterDialog { Parent = GameScene.Scene };
            GameScene.Scene.MailComposeLetterDialog = composeLetter;
            composeLetter.Location = new MPoint(350, 20);
            composeLetter.ComposeMail("Recipient");

            var composeParcel = new MailComposeParcelDialog { Parent = GameScene.Scene };
            GameScene.Scene.MailComposeParcelDialog = composeParcel;
            composeParcel.Location = new MPoint(600, 20);
            composeParcel.ComposeMail("Recipient");
            // ComposeMail 内 ResetLockedCells 会清空格子（并逐格发包 MailLockedItem），先 ComposeMail 再补 Cells[0]。
            composeParcel.Cells[0].Item = potion;

            var readLetter = new MailReadLetterDialog { Parent = GameScene.Scene };
            GameScene.Scene.MailReadLetterDialog = readLetter;
            readLetter.Location = new MPoint(350, 330);
            readLetter.ReadMail(letterMail);

            var readParcel = new MailReadParcelDialog { Parent = GameScene.Scene };
            GameScene.Scene.MailReadParcelDialog = readParcel;
            readParcel.Location = new MPoint(600, 420);
            readParcel.ReadMail(itemMail);

            // ---------- 遍3/4：TrustMerchant（双实例 Market/Consign 面板） ----------
            // 市场 listing：5 条寄售（Image=1 剑图标 + 白名 + 卖家 + 到期时间）。
            var auctions = new System.Collections.Generic.List<ClientAuction>();
            for (int i = 0; i < 5; i++)
            {
                var itemInfo = new ItemInfo
                {
                    Index = 4100 + i,
                    Name = "Sword" + i,
                    Type = ItemType.Weapon,
                    Shape = 1,
                    Weight = 5,
                    Image = 1,
                    Durability = 10,
                    StackSize = 1,
                    Stats = new Stats(),
                };
                var auctionItem = new UserItem(itemInfo) { UniqueID = 40u + (uint)i, CurrentDura = 10, MaxDura = 10 };
                auctions.Add(new ClientAuction
                {
                    AuctionID = 100u + (uint)i,
                    Item = auctionItem,
                    Seller = "Seller" + i,
                    Price = 1000u * (uint)(i + 1),
                    ConsignmentDate = DateTime.Now,
                    ItemType = MarketItemType.Consign,
                });
            }

            TrustMerchantDialog.UserMode = false;
            var marketDlg = new TrustMerchantDialog { Parent = GameScene.Scene };
            marketDlg.Location = new MPoint(20, 20);
            marketDlg.TMerchantDialog(MarketPanelType.Market);
            marketDlg.Listings = auctions;
            marketDlg.Page = 0;
            marketDlg.PageCount = 1;
            marketDlg.UpdateInterface();

            var consignDlg = new TrustMerchantDialog { Parent = GameScene.Scene };
            consignDlg.Location = new MPoint(20, 20);
            consignDlg.TMerchantDialog(MarketPanelType.Consign);
            // MirItemCell.Item 为普通字段（TrustMerchant GridType 的 ItemArray 未移植），直设以渲染剑图标。
            consignDlg.ItemCell.Item = sword;

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                var tFails = new System.Collections.Generic.List<string>();
                var mFails = new System.Collections.Generic.List<string>();
                var kFails = new System.Collections.Generic.List<string>();
                var cFails = new System.Collections.Generic.List<string>();
                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
                Func<Color32, bool> strictWhite = c => c.r > 240 && c.g > 240 && c.b > 240;
                Func<Color32, bool> bright = c => c.r + c.g + c.b > 60;
                // 8F 小字号文本抗锯齿下中心像素常低于 240，用宽松亮白（含描边内字芯）。
                Func<Color32, bool> nearWhite = c => c.r > 170 && c.g > 170 && c.b > 170;

                // ================= 遍1：交易 =================
                UiText.WarmTree(trade);
                UiText.WarmTree(guest);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                trade.Draw();
                guest.Draw();
                CrystalSpriteBatch.End();

                var tRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                tRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                tRead.Apply();
                RenderTexture.active = null;
                var tpx = tRead.GetPixels32();

                // TradeDialog（Prguse 389）位于 (20,20)：frame + Grid[0] 剑图标 (30,59) + NameLabel (40,30)。
                int tradeFramePx = CountRegion(tpx, 20, 20, 204, 152, lit);
                int tradeIconPx = CountRegion(tpx, 30, 59, 36, 32, bright);
                int tradeNamePx = CountRegion(tpx, 40, 30, 150, 14, nearWhite);
                // GuestTradeDialog（Prguse 390）位于 (250,20)：frame + GuestGrid[0] 药水 (260,59)。
                int guestFramePx = CountRegion(tpx, 250, 20, 204, 152, lit);
                int guestIconPx = CountRegion(tpx, 260, 59, 36, 32, bright);
                int guestNamePx = CountRegion(tpx, 250, 30, 204, 14, nearWhite);

                if (tradeFramePx < 500) tFails.Add("tradeFrame=" + tradeFramePx);
                if (tradeIconPx < 40) tFails.Add("tradeIcon=" + tradeIconPx);
                if (tradeNamePx < 10) tFails.Add("tradeName=" + tradeNamePx);
                if (guestFramePx < 500) tFails.Add("guestFrame=" + guestFramePx);
                if (guestIconPx < 40) tFails.Add("guestIcon=" + guestIconPx);
                if (guestNamePx < 10) tFails.Add("guestName=" + guestNamePx);

                // 遍1 渲染存档：交易布局。
                var tFl = new Color32[tpx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(tpx, (_rtH - 1 - y) * _rtW, tFl, y * _rtW, _rtW);
                tRead.SetPixels32(tFl);
                tRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-market-pass1.png"), tRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tRead);

                // ================= 遍2：邮件五窗 =================
                UiText.WarmTree(mailList);
                UiText.WarmTree(composeLetter);
                UiText.WarmTree(composeParcel);
                UiText.WarmTree(readLetter);
                UiText.WarmTree(readParcel);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                mailList.Draw();
                composeLetter.Draw();
                composeParcel.Draw();
                readLetter.Draw();
                readParcel.Draw();
                CrystalSpriteBatch.End();

                var mRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                mRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                mRead.Apply();
                RenderTexture.active = null;
                var mpx = mRead.GetPixels32();

                // MailListDialog（Title 670）位于 (20,20)：frame + Row0 SenderLabel 白字 (65,75)。
                int mailListFramePx = CountRegion(mpx, 20, 20, 312, 444, lit);
                int mailRowSenderPx = CountRegion(mpx, 65, 75, 130, 20, nearWhite);
                // MailComposeLetterDialog（Title 671）位于 (350,20)：frame + RecipientName (420,55)。
                int composeLetterFramePx = CountRegion(mpx, 350, 20, 236, 300, lit);
                int composeRecipientPx = CountRegion(mpx, 420, 55, 150, 15, nearWhite);
                // MailComposeParcelDialog（Title 674）位于 (600,20)：frame + Cells[0] 药水 (627,331)。
                int composeParcelFramePx = CountRegion(mpx, 600, 20, 236, 384, lit);
                int parcelCellPx = CountRegion(mpx, 627, 331, 35, 31, bright);
                // MailReadLetterDialog（Title 672）位于 (350,330)：frame + SenderName (420,365)。
                int readLetterFramePx = CountRegion(mpx, 350, 330, 236, 300, lit);
                int readSenderPx = CountRegion(mpx, 420, 365, 150, 15, nearWhite);
                // MailReadParcelDialog（Title 675）位于 (600,420)：frame + Cells[0] 剑 (627,731)。
                int readParcelFramePx = CountRegion(mpx, 600, 420, 236, 300, lit);
                int readParcelCellPx = CountRegion(mpx, 627, 731, 35, 31, bright);

                if (mailListFramePx < 800) mFails.Add("mailListFrame=" + mailListFramePx);
                if (mailRowSenderPx < 10) mFails.Add("mailRowSender=" + mailRowSenderPx);
                if (composeLetterFramePx < 400) mFails.Add("composeLetterFrame=" + composeLetterFramePx);
                if (composeRecipientPx < 5) mFails.Add("composeRecipient=" + composeRecipientPx);
                if (composeParcelFramePx < 400) mFails.Add("composeParcelFrame=" + composeParcelFramePx);
                if (parcelCellPx < 40) mFails.Add("parcelCell=" + parcelCellPx);
                if (readLetterFramePx < 400) mFails.Add("readLetterFrame=" + readLetterFramePx);
                if (readSenderPx < 5) mFails.Add("readSender=" + readSenderPx);
                if (readParcelFramePx < 400) mFails.Add("readParcelFrame=" + readParcelFramePx);
                if (readParcelCellPx < 40) mFails.Add("readParcelCell=" + readParcelCellPx);

                // 遍2 渲染存档：邮件五窗布局。
                var mFl = new Color32[mpx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(mpx, (_rtH - 1 - y) * _rtW, mFl, y * _rtW, _rtW);
                mRead.SetPixels32(mFl);
                mRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-market-pass2.png"), mRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(mRead);

                // ================= 遍3：市场面板（TrustMerchant 实例A） =================
                UiText.WarmTree(marketDlg);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                marketDlg.Draw();
                CrystalSpriteBatch.End();

                var kRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                kRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                kRead.Apply();
                RenderTexture.active = null;
                var kpx = kRead.GetPixels32();

                // TrustMerchantDialog（Title 786）位于 (20,20) 492×478：frame + 筛选树按钮 (27,80) 起
                // + Row0 剑图标（AuctionRow IconImage (127,82) 相对 → 绝对 (147,102) 区域）。
                int marketFramePx = CountRegion(kpx, 20, 20, 492, 478, lit);
                int filterTreePx = CountRegion(kpx, 27, 80, 99, 190, bright);
                int row0IconPx = CountRegion(kpx, 147, 102, 34, 32, bright);
                int row0NamePx = CountRegion(kpx, 185, 110, 140, 20, nearWhite);
                int searchBtnPx = CountRegion(kpx, 144, 478, 30, 20, bright);

                if (marketFramePx < 2000) kFails.Add("marketFrame=" + marketFramePx);
                if (filterTreePx < 500) kFails.Add("filterTree=" + filterTreePx);
                if (row0IconPx < 40) kFails.Add("row0Icon=" + row0IconPx);
                if (row0NamePx < 10) kFails.Add("row0Name=" + row0NamePx);
                if (searchBtnPx < 30) kFails.Add("searchBtn=" + searchBtnPx);

                // 遍3 渲染存档：市场面板。
                var kFl = new Color32[kpx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(kpx, (_rtH - 1 - y) * _rtW, kFl, y * _rtW, _rtW);
                kRead.SetPixels32(kFl);
                kRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-market-pass3.png"), kRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(kRead);

                // ================= 遍4：寄售面板（TrustMerchant 实例B） =================
                UiText.WarmTree(consignDlg);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                consignDlg.Draw();
                CrystalSpriteBatch.End();

                var cRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                cRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                cRead.Apply();
                RenderTexture.active = null;
                var cpx = cRead.GetPixels32();

                // TrustMerchantDialog（Title 787）位于 (20,20)：frame + ItemCell 剑 (67,124)
                // + PriceTextBox 边框 (35,185) + SellItemButton (59,208) + HelpLabel 规则白字。
                int consignFramePx = CountRegion(cpx, 20, 20, 492, 478, lit);
                int consignItemPx = CountRegion(cpx, 67, 124, 36, 32, bright);
                int sellBtnPx = CountRegion(cpx, 59, 208, 40, 22, bright);
                int helpLabelPx = CountRegion(cpx, 28, 257, 115, 205, nearWhite);

                if (consignFramePx < 2000) cFails.Add("consignFrame=" + consignFramePx);
                if (consignItemPx < 40) cFails.Add("consignItem=" + consignItemPx);
                if (sellBtnPx < 30) cFails.Add("sellBtn=" + sellBtnPx);
                if (helpLabelPx < 50) cFails.Add("helpLabel=" + helpLabelPx);

                // 数据断言：Rows 填充、邮件行数、筛选树节点数。
                int filledRows = 0;
                for (int i = 0; i < marketDlg.Rows.Length; i++)
                    if (marketDlg.Rows[i].Listing != null) filledRows++;
                int mailRows = mailList.Rows.Count(r => r != null);
                bool row0Sword = marketDlg.Rows[0].Listing != null && marketDlg.Rows[0].Listing.Item.Info.Name == "Sword0";
                if (filledRows != 5) kFails.Add("rows=" + filledRows);
                if (!row0Sword) kFails.Add("row0=" + (marketDlg.Rows[0].Listing?.Item.Info.Name ?? "null"));
                if (mailRows != 3) mFails.Add("mailRows=" + mailRows);
                if (guest.GuestNameLabel.Text != "Guest") tFails.Add("guestName=" + guest.GuestNameLabel.Text);
                if (trade.NameLabel.Text != "Probe") tFails.Add("tradeName=" + trade.NameLabel.Text);
                if (marketDlg.Filters.Count != 8) kFails.Add("filters=" + marketDlg.Filters.Count);

                _uiOk = tFails.Count == 0 && mFails.Count == 0 && kFails.Count == 0 && cFails.Count == 0;
                _uiFail = $"trade={trade.NameLabel.Text}/5000 guest={guest.GuestNameLabel.Text}/3000 mail={mailRows}/3 rows={filledRows}/5 filters={marketDlg.Filters.Count} tradeFrame={tradeFramePx} tradeIcon={tradeIconPx} guestFrame={guestFramePx} guestIcon={guestIconPx} mailListFrame={mailListFramePx} mailRowSender={mailRowSenderPx} composeLetterFrame={composeLetterFramePx} composeRecipient={composeRecipientPx} composeParcelFrame={composeParcelFramePx} parcelCell={parcelCellPx} readLetterFrame={readLetterFramePx} readSender={readSenderPx} readParcelFrame={readParcelFramePx} readParcelCell={readParcelCellPx} marketFrame={marketFramePx} filterTree={filterTreePx} row0Icon={row0IconPx} row0Name={row0NamePx} searchBtn={searchBtnPx} consignFrame={consignFramePx} consignItem={consignItemPx} sellBtn={sellBtnPx} helpLabel={helpLabelPx}"
                    + (tFails.Count > 0 ? " FAIL:" + string.Join(",", tFails) : "")
                    + (mFails.Count > 0 ? " FAIL:" + string.Join(",", mFails) : "")
                    + (kFails.Count > 0 ? " FAIL:" + string.Join(",", kFails) : "")
                    + (cFails.Count > 0 ? " FAIL:" + string.Join(",", cFails) : "");
                Console.WriteLine($"[netprobe] market {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");

                // R3 行序：EncodeToPNG 输出翻转图，编码前按行翻转（RenderTeam 同款）。
                var mktPx = cRead.GetPixels32();
                var mktFl = new Color32[mktPx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(mktPx, (_rtH - 1 - y) * _rtW, mktFl, y * _rtW, _rtW);
                cRead.SetPixels32(mktFl);
                cRead.Apply();

                string fullOut = Path.GetFullPath(_outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, cRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(cRead);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // 迭代包9 商城+扩展系统渲染探针：登录→进图后合成商城/打孔镶嵌/指南针/举报测试数据，构建
        // GameShopDialog + SocketDialog + CompassDialog + ReportDialog 控制树 → 数据+像素断言 → 4 张 PNG。
        // net-shop.ps1 编排。坐标基线：各对话框 Location=(20,20)/(30,30) 起平铺，子控件相对坐标源自逐字移植 Layout。
        // 注意：Prguse 1633（举报框体）在本服务器图集为空帧，报告窗仅断言 下拉框/文本框/发送按钮。
        static void RenderShop()
        {
            _shopFrozen = true; // 冻结实时商城推送，保证本方法内合成数据确定性
            // ProbeLang.Ensure(); // 中文语言包：推迟到全部任务完成后恢复（见任务 #18）
            UiText.Install();
            UiText.PreWarm(8);

            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "shop:prguse-missing"; return; }
            Libraries.Prguse = prguse;
            var prguse2 = SceneRender.EnsureMLibrary("Prguse2");
            if (prguse2 == null) { _uiFail = "shop:prguse2-missing"; return; }
            Libraries.Prguse2 = prguse2;
            var prguse3 = SceneRender.EnsureMLibrary("Prguse3");
            if (prguse3 == null) { _uiFail = "shop:prguse3-missing"; return; }
            Libraries.Prguse3 = prguse3;
            var items = SceneRender.EnsureMLibrary("Items");
            if (items == null) { _uiFail = "shop:items-missing"; return; }
            Libraries.Items = items;
            var title = SceneRender.EnsureMLibrary("Title");
            if (title == null) { _uiFail = "shop:title-missing"; return; }
            Libraries.Title = title;

            var user = MapObject.User;
            if (user == null) { _uiFail = "shop:no-user"; return; }
            GameScene.User = user;
            MapControl.User = user;
            GameScene.Gold = 50000;
            GameScene.Credit = 100000;
            user.Name = "Probe";
            user.Class = MirClass.Warrior;
            user.CurrentLocation = new MPoint(100, 100);

            // 背包实例：SocketDialog.Show 定位读 InventoryDialog.Size；不绘制（Visible=false）。
            var inv = new InventoryDialog { Parent = GameScene.Scene };
            GameScene.Scene.InventoryDialog = inv;
            inv.Location = new MPoint(20, 20);
            inv.Visible = false;

            // ---------- 遍1：商城主窗 ----------
            // GameShopDialog ctor 会 Clear GameShopInfoList → 先构造再填充。
            var shop = new GameShopDialog { Parent = GameScene.Scene };
            GameScene.Scene.GameShopDialog = shop; // UpdateShop 结尾引用 GameShopDialog.Viewer

            // 7 件商品：6 件 Warrior/All（职业过滤后可见）+ 1 件 Wizard（应被过滤）。库存/货币形态全覆盖。
            GameScene.GameShopInfoList.Add(MakeShopItem(101, "BattleSword", ItemType.Weapon, "Weapons", "Warrior", 5000, 0, 10, true, false));
            GameScene.GameShopInfoList.Add(MakeShopItem(102, "GoldPotion", ItemType.Potion, "Potions", "All", 100, 0, 99, true, false));
            GameScene.GameShopInfoList.Add(MakeShopItem(103, "CreditArmor", ItemType.Armour, "Armour", "Warrior", 0, 50000, 0, false, true));
            GameScene.GameShopInfoList.Add(MakeShopItem(104, "TopMount", ItemType.Mount, "Mounts", "All", 20000, 0, 3, true, false, top: true));
            GameScene.GameShopInfoList.Add(MakeShopItem(105, "DealScroll", ItemType.Scroll, "Scrolls", "Warrior", 0, 500, 25, false, true, deal: true));
            GameScene.GameShopInfoList.Add(MakeShopItem(106, "NewRing", ItemType.Ring, "Jewellery", "All", 800, 900, 5, true, true));
            GameScene.GameShopInfoList.Add(MakeShopItem(107, "WizardStaff", ItemType.Weapon, "Weapons", "Wizard", 3000, 0, 2, true, false));

            shop.Visible = false; // Show() 带 `if (Visible) return` 守卫（MirControl.Visible 默认 true）→ 先置 false 才走 Show 主体
            shop.Show(); // ClassFilter=user.Class.ToString()=Warrior → GetCategories → UpdateShop 建格
            shop.Process(); // totalGold/totalCredits 标签
            shop.Location = new MPoint(20, 20);
            // 格子标签文本在 DrawControl→UpdateText 才计算 → 预置文本，使 WarmTree 能预建字形（R8 批内字形坑）。
            for (int i = 0; i < shop.Grid.Length; i++)
                if (shop.Grid[i].Item != null) shop.Grid[i].UpdateText();

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                var sFails = new System.Collections.Generic.List<string>();
                var oFails = new System.Collections.Generic.List<string>();
                var cFails = new System.Collections.Generic.List<string>();
                var rFails = new System.Collections.Generic.List<string>();
                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
                Func<Color32, bool> bright = c => c.r + c.g + c.b > 60;
                Func<Color32, bool> nearWhite = c => c.r > 170 && c.g > 170 && c.b > 170;
                Func<Color32, bool> dark = c => c.r < 20 && c.g < 20 && c.b < 20;

                // ================= 遍1：商城主窗 =================
                UiText.WarmTree(shop);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                shop.Draw();
                CrystalSpriteBatch.End();

                var sRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                sRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                sRead.Apply();
                RenderTexture.active = null;
                var spx = sRead.GetPixels32();

                // GameShopDialog（Title 749）位于 (20,20) 696×476：frame + Grid[0] 剑图标 (184,177)
                // + 名称黄字 (172,148) + 总金币 (143,469) + 金币勾选框 (270,469)。
                int shopFramePx = CountRegion(spx, 20, 20, 696, 476, lit);
                int shopIconPx = CountRegion(spx, 180, 172, 40, 36, bright);
                int shopNamePx = CountRegion(spx, 172, 148, 125, 15, bright);
                int goldPx = CountRegion(spx, 143, 469, 100, 20, nearWhite);
                int creditPx = CountRegion(spx, 25, 469, 100, 20, nearWhite);
                int boxPx = CountRegion(spx, 270, 469, 16, 12, bright);
                int pagePx = CountRegion(spx, 617, 466, 83, 17, bright);

                if (shopFramePx < 4000) sFails.Add("shopFrame=" + shopFramePx);
                if (shopIconPx < 40) sFails.Add("shopIcon=" + shopIconPx);
                if (shopNamePx < 10) sFails.Add("shopName=" + shopNamePx);
                if (goldPx < 3) sFails.Add("gold=" + goldPx);
                if (creditPx < 3) sFails.Add("credit=" + creditPx);
                if (boxPx < 4) sFails.Add("box=" + boxPx);
                if (pagePx < 3) sFails.Add("page=" + pagePx);

                // 遍1 渲染存档：商城布局（含行翻转 → net-shop-pass1.png / net-shop.png 最终图）。
                var sFl = new Color32[spx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(spx, (_rtH - 1 - y) * _rtW, sFl, y * _rtW, _rtW);
                sRead.SetPixels32(sFl);
                sRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-shop-pass1.png"), sRead.EncodeToPNG());

                // ================= 遍2：打孔镶嵌（SocketDialog） =================
                var stone = new UserItem(new ItemInfo { Index = 9001, Name = "DragonStone", Type = ItemType.Potion, Shape = 0, Weight = 1, Image = 1, Durability = 1, StackSize = 1, Stats = new Stats() })
                { UniqueID = 51, CurrentDura = 1, MaxDura = 1 };
                var socketed = new UserItem(new ItemInfo { Index = 9002, Name = "SocketSword", Type = ItemType.Weapon, Shape = 1, Weight = 5, Image = 1, Durability = 10, StackSize = 1, Stats = new Stats() })
                { UniqueID = 50, CurrentDura = 10, MaxDura = 10, Slots = new UserItem[] { stone, null } };

                var socket = new SocketDialog { Parent = GameScene.Scene };
                GameScene.Scene.SocketDialog = socket;
                socket.Show(MirGridType.Inventory, socketed); // 2 槽 → Index=21 (118×62)
                socket.Location = new MPoint(30, 30);

                UiText.WarmTree(socket);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                socket.Draw();
                CrystalSpriteBatch.End();

                var oRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                oRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                oRead.Apply();
                RenderTexture.active = null;
                var opx = oRead.GetPixels32();

                // SocketDialog（Prguse3 21）位于 (30,30) 118×62：frame + Grid[0] 槽龙石图标 (55,47)。
                int socketFramePx = CountRegion(opx, 30, 30, 118, 62, lit);
                int socketStonePx = CountRegion(opx, 53, 45, 36, 32, bright);

                if (socketFramePx < 300) oFails.Add("socketFrame=" + socketFramePx);
                if (socketStonePx < 40) oFails.Add("socketStone=" + socketStonePx);
                if (socket.Grid[0].Item != stone) oFails.Add("slot0=" + (socket.Grid[0].Item?.Info.Name ?? "null"));
                if (socket.Grid[2].Visible) oFails.Add("slot2visible");
                if (GameScene.SelectedItem != socketed) oFails.Add("selected");

                var oFl = new Color32[opx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(opx, (_rtH - 1 - y) * _rtW, oFl, y * _rtW, _rtW);
                oRead.SetPixels32(oFl);
                oRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-shop-pass2.png"), oRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(oRead);

                // ================= 遍3：指南针（CompassDialog） =================
                var compass = new CompassDialog { Parent = GameScene.Scene };
                GameScene.Scene.CompassControl = compass;
                compass.Location = new MPoint(30, 30);

                compass.Process(); // 无目标 → 隐藏
                bool compHidden = !compass.Visible;
                compass.SetPoint(new MPoint(160, 80));
                compass.Process(); // 方位不同 → 显示 + 指向帧 1470..1509
                bool compShown = compass.Visible;

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                compass.Draw();
                CrystalSpriteBatch.End();

                var cRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                cRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                cRead.Apply();
                RenderTexture.active = null;
                var cpx = cRead.GetPixels32();

                // CompassDialog 位于 (30,30)：Prguse2 指南针帧 20×27 绘制在 (39,31) 起（含 OX=9,OY=1 偏移）。
                int compassPx = CountRegion(cpx, 30, 30, 60, 60, lit);
                if (compassPx < 15) cFails.Add("compass=" + compassPx);
                if (!compHidden) cFails.Add("compHidden");
                if (!compShown) cFails.Add("compShown");

                compass.ClearPoint();
                compass.Process();
                if (compass.Visible) cFails.Add("compCleared");

                var cFl = new Color32[cpx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(cpx, (_rtH - 1 - y) * _rtW, cFl, y * _rtW, _rtW);
                cRead.SetPixels32(cFl);
                cRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-shop-pass3.png"), cRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(cRead);

                // ================= 遍4：举报（ReportDialog） =================
                // Prguse 1633 框体在图集为空帧，仅断言 下拉框/消息文本框/发送按钮。
                var report = new ReportDialog { Parent = GameScene.Scene };
                GameScene.Scene.ReportDialog = report;
                report.Location = new MPoint(30, 30);

                UiText.WarmTree(report);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                report.Draw();
                CrystalSpriteBatch.End();

                var rRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                rRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                rRead.Apply();
                RenderTexture.active = null;
                var rpx = rRead.GetPixels32();

                // ReportDialog 位于 (30,30)：类型下拉框 (42,65) 黑底 + 消息区 (42,87) 黑底 + 发送按钮 (290,249)。
                int reportDropPx = CountRegion(rpx, 42, 65, 170, 14, lit);
                int reportBoxPx = CountRegion(rpx, 42, 87, 330, 150, dark);
                int reportSendPx = CountRegion(rpx, 290, 249, 76, 25, bright);

                if (reportDropPx < 500) rFails.Add("reportDrop=" + reportDropPx);
                if (reportBoxPx < 10000) rFails.Add("reportBox=" + reportBoxPx);
                if (reportSendPx < 100) rFails.Add("reportSend=" + reportSendPx);

                var rFl = new Color32[rpx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(rpx, (_rtH - 1 - y) * _rtW, rFl, y * _rtW, _rtW);
                rRead.SetPixels32(rFl);
                rRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-shop-pass4.png"), rRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(rRead);

                // ================= 数据断言：商城过滤/分页/支付勾选/分类 + handler 增删 =================
                int filled = 0;
                for (int i = 0; i < shop.Grid.Length; i++)
                    if (shop.Grid[i].Item != null) filled++;
                if (filled != 6) sFails.Add("filled=" + filled);
                if (shop.Grid[0].Item == null || shop.Grid[0].Item.Info.Name != "BattleSword") sFails.Add("g0=" + (shop.Grid[0].Item?.Info.Name ?? "null"));
                if (shop.Grid.Any(g => g.Item != null && g.Item.Class == "Wizard")) sFails.Add("wizLeak");
                if (shop.PageNumberLabel.Text != "1 / 1") sFails.Add("pageText=" + shop.PageNumberLabel.Text);
                if (shop.Filters[0].Text != "Show All" || shop.Filters[0].ForeColour.R != 230) sFails.Add("f0=" + shop.Filters[0].Text);
                if (shop.Filters[1].Text != "Weapons") sFails.Add("f1=" + shop.Filters[1].Text);
                if (shop.ClassFilter != "Warrior") sFails.Add("class=" + shop.ClassFilter);
                if (!shop.PaymentTypeGold.Checked || shop.PaymentTypeCredit.Checked) sFails.Add("pay");
                // New 标签初始应隐藏：SectionFilter=Show All 无新品过滤，且未推送新品（真实推送被冻结）→ 置 true 才算泄漏。
                if (shop.New.Visible) sFails.Add("newLit");

                // handler 增删验证（相对计数，防真实推送并发干扰）。
                int before = GameScene.GameShopInfoList.Count;
                var addItem = MakeShopItem(108, "NewSword", ItemType.Weapon, "Weapons", "Warrior", 6000, 0, 4, true, false);
                GameScene.Scene.GameShopUpdate(new S.GameShopInfo { Item = addItem, StockLevel = 4 });
                if (GameScene.GameShopInfoList.Count != before + 1) sFails.Add("add=" + (GameScene.GameShopInfoList.Count - before));
                if (addItem.Stock != 4) sFails.Add("stockAdd=" + addItem.Stock);
                if (!shop.New.Visible) sFails.Add("newAfterAdd");
                GameScene.Scene.GameShopStock(new S.GameShopStock { GIndex = 108, StockLevel = 0 });
                if (GameScene.GameShopInfoList.Any(i => i.GIndex == 108)) sFails.Add("stockRemoved");
                GameScene.Scene.GameShopStock(new S.GameShopStock { GIndex = 101, StockLevel = 1 });
                var sword101 = GameScene.GameShopInfoList.First(i => i.GIndex == 101);
                if (sword101.Stock != 1) sFails.Add("stockUpdate=" + sword101.Stock);

                _uiOk = sFails.Count == 0 && oFails.Count == 0 && cFails.Count == 0 && rFails.Count == 0;
                _uiFail = $"shopPush={_shopPush} grid={filled}/6 page={shop.PageNumberLabel.Text} filters={shop.Filters[0].Text}|{shop.Filters[1].Text} class={shop.ClassFilter} shopFrame={shopFramePx} shopIcon={shopIconPx} shopName={shopNamePx} gold={goldPx} credit={creditPx} box={boxPx} page={pagePx} socketFrame={socketFramePx} socketStone={socketStonePx} compass={compassPx} reportDrop={reportDropPx} reportBox={reportBoxPx} reportSend={reportSendPx}"
                    + (sFails.Count > 0 ? " FAIL:" + string.Join(",", sFails) : "")
                    + (oFails.Count > 0 ? " FAIL:" + string.Join(",", oFails) : "")
                    + (cFails.Count > 0 ? " FAIL:" + string.Join(",", cFails) : "")
                    + (rFails.Count > 0 ? " FAIL:" + string.Join(",", rFails) : "");
                Console.WriteLine($"[netprobe] shop {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");

                // R3 行序：最终图 = 商城主窗（遍1 已行翻转，直接写出）。
                string fullOut = Path.GetFullPath(_outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, sRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(sRead);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // 迭代包10 设置探针：登录→进图后合成 ChatOptionDialog/HelpDialog/KeyboardLayoutDialog 测试数据，
        // 经 Scene 真实鼠标合成点击驱动（筛选 tab/透明切换/翻页/绑定行点击）→ 数据+像素断言 → 4 张 PNG。
        // net-settings.ps1 编排。坐标基线：各对话框 Location=(30,30)，子控件相对坐标源自逐字移植 Layout。
        static void RenderSettings()
        {
            ProbeLang.Ensure();
            UiText.Install();
            UiText.PreWarm(8);
            DiagBuild("pre-pass1");

            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "settings:prguse-missing"; return; }
            Libraries.Prguse = prguse;
            var prguse2 = SceneRender.EnsureMLibrary("Prguse2");
            if (prguse2 == null) { _uiFail = "settings:prguse2-missing"; return; }
            Libraries.Prguse2 = prguse2;
            var title = SceneRender.EnsureMLibrary("Title");
            if (title == null) { _uiFail = "settings:title-missing"; return; }
            Libraries.Title = title;
            var help = SceneRender.EnsureMLibrary("Help");
            if (help == null) { _uiFail = "settings:help-missing"; return; }
            Libraries.Help = help;

            var user = MapObject.User;
            if (user == null) { _uiFail = "settings:no-user"; return; }
            GameScene.User = user;
            MapControl.User = user;
            user.Name = "Probe";
            user.Class = MirClass.Warrior;
            user.CurrentLocation = new MPoint(100, 100);
            user.HP = 100; user.MP = 100; user.Level = 1;
            user.MaxExperience = 1000; user.Experience = 0;
            user.Stats[Stat.HP] = 100; user.Stats[Stat.MP] = 100;

            GameScene.Scene.ChatNoticeDialog = new ChatNoticeDialog();
            // ChatDialog ctor 读 Scene.MainDialog.Location；ChatOptionDialog ctor 调 ChatDialog.Update() → 顺序契约。
            var main = new MainDialog();
            GameScene.Scene.MainDialog = main;
            var chat = new ChatDialog();
            GameScene.Scene.ChatDialog = chat;
            main.Process();
            chat.ReceiveChat("System: settings probe online", ChatType.System);
            chat.Update();

            var sFails = new System.Collections.Generic.List<string>();
            var hFails = new System.Collections.Generic.List<string>();
            var kFails = new System.Collections.Generic.List<string>();
            Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
            Func<Color32, bool> bright = c => c.r + c.g + c.b > 60;
            Func<Color32, bool> nearWhite = c => c.r > 170 && c.g > 170 && c.b > 170;

            // 批前 CJK 字形预热（R8 坑广义触发：渲染后新建中文字形 Populate 几何失效，实测 post-pass4
            // arial/yahei-build 皆 6x14 px=0；ASCII 因 PreWarm 预载入图集幸免）：构造各设置对话框仅取
            // 文本 → WarmTree 预合成字形纹理进 _textTex 缓存，渲染期 DrawText 全命中缓存，绝不在渲染后新建 CJK。
            // 注意 PageLabel 字号 9（非 PreWarm 的 8）+ 翻页后文本 "N / 45" 与构造初值 "1 / 45" key 不同，
            // WarmTree 只合成当前文本——故按 DisplayPage 语义逐页预热最终文本（页标题 "N. Title" + 页码 "N / 45"）。
            var chatOptP = new ChatOptionDialog { Parent = GameScene.Scene };
            UiText.WarmTree(chatOptP);
            chatOptP.Parent = null;
            var helpP = new HelpDialog();
            for (int i = 0; i < helpP.Pages.Count; i++)
            {
                var pg = helpP.Pages[i];
                UiText.WarmText((i + 1) + ". " + pg.Title, pg.PageTitleLabel.Font);
                UiText.WarmText((i + 1) + " / " + helpP.Pages.Count, helpP.PageLabel.Font);
            }
            UiText.WarmTree(helpP);
            var kbdP = new KeyboardLayoutDialog { Parent = GameScene.Scene };
            UiText.WarmTree(kbdP);
            kbdP.Parent = null;
            // 预热诊断：缓存 CJK keys + kbdP 各行 BindName 文本（对照渲染期是否命中）。
            Console.WriteLine($"[probe] warm cjk-cache: {UiText.DumpCjkKeys()}");
            Console.WriteLine($"[probe] warm cjk-opaque: {UiText.DumpCjkOpaque()}");
            var kbdPKeys = new System.Collections.Generic.List<string>();
            for (int ri = 0; ri < kbdP.Rows.Count; ri++)
            {
                if (kbdP.Rows[ri] is KeybindRow kr && kr.BindName != null)
                    kbdPKeys.Add($"{kr.BindName.Text}[v={kr.BindName.Visible}]");
            }
            Console.WriteLine($"[probe] warm kbd rows={kbdP.Rows.Count} binds={string.Join(" | ", kbdPKeys)}");

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                // ================= 遍1：ChatOptionDialog 筛选 tab =================
                var chatOpt = new ChatOptionDialog { Parent = GameScene.Scene };
                GameScene.Scene.ChatOptionDialog = chatOpt;
                chatOpt.Location = new MPoint(30, 30);

                // 构造后初始态：8 filter 全关 → AllFiltersOff=true；筛选 tab 激活（Title 466）。
                bool initAllOff = chatOpt.AllFiltersOff;
                bool initFilterTab = chatOpt.Index == 466;
                bool initAllVisible = chatOpt.AllButton.Visible;
                bool initTransHidden = !chatOpt.TransparencyOnButton.Visible;

                // 点 AllButton → ToggleAllFilters：8 filter 全开 + AllFiltersOff=false。
                bool allClicked = ClickControl(chatOpt.AllButton);
                bool allFiltersOn = Settings.FilterNormalChat && Settings.FilterWhisperChat
                    && Settings.FilterShoutChat && Settings.FilterSystemChat && Settings.FilterLoverChat
                    && Settings.FilterMentorChat && Settings.FilterGroupChat && Settings.FilterGuildChat;
                bool allOffFlag = !chatOpt.AllFiltersOff;
                // 再点 GeneralButton → FilterNormalChat 关（其余 7 开），AllFiltersOff 仍 false。
                bool genClicked = ClickControl(chatOpt.GeneralButton);
                bool genFilterOff = !Settings.FilterNormalChat;

                UiText.WarmTree(chatOpt);
                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                chatOpt.Draw();
                CrystalSpriteBatch.End();

                var sRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                sRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                sRead.Apply();
                RenderTexture.active = null;
                var spx = sRead.GetPixels32();

                // ChatOptionDialog（Title 466，224×180）位于 (30,30)：frame + AllButton (104,77) 16×12 + ChatTabButton (108,38)。
                int coFramePx = CountRegion(spx, 30, 30, 224, 180, lit);
                int coAllPx = CountRegion(spx, 104, 77, 16, 12, bright);
                int coChatTabPx = CountRegion(spx, 108, 38, 30, 30, bright);

                if (!initAllOff) sFails.Add("initAllOff");
                if (!initFilterTab) sFails.Add("initTab=" + chatOpt.Index);
                if (!initAllVisible) sFails.Add("initAllVis");
                if (!initTransHidden) sFails.Add("initTransVis");
                if (!allClicked) sFails.Add("allClick");
                if (!allFiltersOn) sFails.Add("allFiltersOn");
                if (!allOffFlag) sFails.Add("allOffFlag");
                if (!genClicked) sFails.Add("genClick");
                if (!genFilterOff) sFails.Add("genFilterOff");
                if (coFramePx < 3000) sFails.Add("coFrame=" + coFramePx);
                if (coAllPx < 4) sFails.Add("coAll=" + coAllPx);
                if (coChatTabPx < 4) sFails.Add("coChatTab=" + coChatTabPx);

                var sFl = new Color32[spx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(spx, (_rtH - 1 - y) * _rtW, sFl, y * _rtW, _rtW);
                sRead.SetPixels32(sFl);
                sRead.Apply();
                string dir1 = Path.GetDirectoryName(Path.GetFullPath(_outPath));
                File.WriteAllBytes(Path.Combine(dir1, "net-settings-pass1.png"), sRead.EncodeToPNG());
                // 最终图 = 遍1（ChatOptionDialog 筛选 tab），与 RenderShop 终图=遍1 一致。
                File.WriteAllBytes(Path.GetFullPath(_outPath), sRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(sRead);

                // ================= 遍2：ChatOptionDialog 透明 tab =================
                // 点 ChatTabButton → SwitchTab(1)：Title 467、筛选按钮隐藏、透明按钮显示。
                bool tabClicked = ClickControl(chatOpt.ChatTabButton);
                bool transTab = chatOpt.Index == 467;
                bool filtersHidden = !chatOpt.AllButton.Visible && !chatOpt.GeneralButton.Visible;
                bool transVisible = chatOpt.TransparencyOnButton.Visible && chatOpt.TransparencyOffButton.Visible;

                // 点 TransparencyOnButton → TransparentChat=true + ChatDialog 半透明着色。
                bool transOnClicked = ClickControl(chatOpt.TransparencyOnButton);
                bool transOn = Settings.TransparentChat;
                bool chatDimmed = chat.ForeColour.R < 30 && chat.Opacity < 1f;
                // 点 TransparencyOffButton → 恢复不透明。
                bool transOffClicked = ClickControl(chatOpt.TransparencyOffButton);
                bool transOff = !Settings.TransparentChat && chat.ForeColour.R == 255 && chat.Opacity >= 1f;
                // 还原开态用于渲染存档（TransparencyOnButton 亮态 Index=474，hover 后为 HoverIndex=475）。
                ClickControl(chatOpt.TransparencyOnButton);
                bool transOnIndex = chatOpt.TransparencyOnButton.Index >= 474;

                UiText.WarmTree(chatOpt);
                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                chatOpt.Draw();
                CrystalSpriteBatch.End();

                var s2Read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                s2Read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                s2Read.Apply();
                RenderTexture.active = null;
                var s2px = s2Read.GetPixels32();

                // 透明 tab（Title 467，同 224×180）：frame + TransparencyOnButton (145,120) 20×20。
                int co2FramePx = CountRegion(s2px, 30, 30, 224, 180, lit);
                int co2OnPx = CountRegion(s2px, 145, 120, 20, 20, bright);

                if (!tabClicked) sFails.Add("tabClick");
                if (!transTab) sFails.Add("transTab=" + chatOpt.Index);
                if (!filtersHidden) sFails.Add("filtersVisible");
                if (!transVisible) sFails.Add("transNotVisible");
                if (!transOnClicked) sFails.Add("transOnClick");
                if (!transOn) sFails.Add("transOn");
                if (!chatDimmed) sFails.Add("chatDimmed f=" + chat.ForeColour.R + " o=" + chat.Opacity);
                if (!transOffClicked) sFails.Add("transOffClick");
                if (!transOff) sFails.Add("transOff");
                if (!transOnIndex) sFails.Add("transOnIndex=" + chatOpt.TransparencyOnButton.Index);
                if (co2FramePx < 3000) sFails.Add("co2Frame=" + co2FramePx);
                if (co2OnPx < 4) sFails.Add("co2On=" + co2OnPx);

                var s2Fl = new Color32[s2px.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(s2px, (_rtH - 1 - y) * _rtW, s2Fl, y * _rtW, _rtW);
                s2Read.SetPixels32(s2Fl);
                s2Read.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-settings-pass2.png"), s2Read.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(s2Read);

                chatOpt.Hide();

                // ================= 遍3：HelpDialog =================
                var helpDlg = new HelpDialog { Parent = GameScene.Scene };
                GameScene.Scene.HelpDialog = helpDlg;
                helpDlg.Location = new MPoint(30, 30);

                // ctor → DisplayPage(0)：ShortcutPage1 + "1 / 45"。
                bool helpCount = helpDlg.Pages.Count == 45;
                bool helpPage0 = helpDlg.CurrentPageNumber == 0 && helpDlg.PageLabel.Text == "1 / 45";
                bool helpPage0Shortcut = helpDlg.CurrentPage.Page is ShortcutPage1;

                // 点 NextButton → 第 2 页。
                bool nextClicked = ClickControl(helpDlg.NextButton);
                bool helpPage1 = helpDlg.PageLabel.Text == "2 / 45";

                // 跳图片页（下标 3 = Movements，ImageID=0）：HelpPage_BeforeDraw 用 Help 图集画图。
                helpDlg.DisplayPage(3);
                bool helpImgPage = helpDlg.CurrentPage.ImageID == 0 && helpDlg.CurrentPage.Page == null
                    && helpDlg.PageLabel.Text == "4 / 45";
                bool helpTitlePrefix = helpDlg.CurrentPage.PageTitleLabel.Text.StartsWith("4. ");

                UiText.WarmTree(helpDlg);
                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                helpDlg.Draw();
                CrystalSpriteBatch.End();

                var hRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                hRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                hRead.Apply();
                RenderTexture.active = null;
                var hpx = hRead.GetPixels32();

                // HelpDialog（Prguse 920）位于 (30,30)：frame + 图片页（HelpPage (12,35)→屏(42,65)，BeforeDraw +40→(42,105)）
                // + PageLabel 文字 (260,510) "4 / 44"。
                var helpSz = Libraries.Prguse.GetTrueSize(920);
                int helpFramePx = CountRegion(hpx, 30, 30, helpSz.Width, helpSz.Height, lit);
                int helpImgPx = CountRegion(hpx, 42, 105, 508, 436, lit);
                int helpPageLabelPx = CountRegion(hpx, 260, 510, 80, 20, nearWhite);

                if (!helpCount) hFails.Add("pages=" + helpDlg.Pages.Count);
                if (!helpPage0) hFails.Add("page0=" + helpDlg.PageLabel.Text);
                if (!helpPage0Shortcut) hFails.Add("page0type=" + (helpDlg.CurrentPage.Page?.GetType().Name ?? "null"));
                if (!nextClicked) hFails.Add("nextClick");
                if (!helpPage1) hFails.Add("page1=" + helpDlg.PageLabel.Text);
                if (!helpImgPage) hFails.Add("imgPage=" + helpDlg.PageLabel.Text);
                if (!helpTitlePrefix) hFails.Add("title=" + helpDlg.CurrentPage.PageTitleLabel.Text);
                if (helpFramePx < 3000) hFails.Add("helpFrame=" + helpFramePx);
                if (helpImgPx < 100) hFails.Add("helpImg=" + helpImgPx);
                if (helpPageLabelPx < 2) hFails.Add("helpPageLabel=" + helpPageLabelPx);

                var hFl = new Color32[hpx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(hpx, (_rtH - 1 - y) * _rtW, hFl, y * _rtW, _rtW);
                hRead.SetPixels32(hFl);
                hRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-settings-pass3.png"), hRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(hRead);

                helpDlg.Hide();

                // ================= 遍4：KeyboardLayoutDialog =================
                var kbd = new KeyboardLayoutDialog { Parent = GameScene.Scene };
                GameScene.Scene.KeyboardLayoutDialog = kbd;
                kbd.Location = new MPoint(30, 30);

                // ctor → UpdateText() 建行：Keylist 全量 + 分组标题。默认绑定 Inventory=F9。
                bool kbdRows = kbd.Rows.Count > 0;
                bool kbdList = CMain.InputKeys.Keylist.Count > 0;
                bool kbdGetInv = CMain.InputKeys.GetKey(KeybindOptions.Inventory) == "F9";
                bool kbdGetInvDefault = CMain.InputKeys.GetKey(KeybindOptions.Inventory, true) == "F9";

                UiText.WarmTree(kbd);
                // 渲染前诊断：kbd 行 BindName 文本 + 是否命中预热缓存。
                var kbdBinds = new System.Collections.Generic.List<string>();
                for (int ri = 0; ri < kbd.Rows.Count; ri++)
                {
                    if (kbd.Rows[ri] is KeybindRow kr && kr.BindName != null)
                        kbdBinds.Add($"{ri}:{kr.BindName.Text}");
                }
                Console.WriteLine($"[probe] kbd rows={kbd.Rows.Count} binds={string.Join(" | ", kbdBinds)}");
                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                kbd.Draw();
                CrystalSpriteBatch.End();

                var kRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                kRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                kRead.Apply();
                RenderTexture.active = null;
                var kpx = kRead.GetPixels32();

                // KeyboardLayoutDialog（Title 119）位于 (30,30)：frame + 首行 CurrentBindButton
                // （分组标题行占位 → 首 KeybindRow 在 (20,120) → 按钮屏 (390,150) 120×16，Text="  F9"）。
                var kbdSz = Libraries.Title.GetTrueSize(119);
                int kbdFramePx = CountRegion(kpx, 30, 30, kbdSz.Width, kbdSz.Height, lit);
                int kbdRowBtnPx = CountRegion(kpx, 390, 150, 120, 16, bright);

                if (!kbdRows) kFails.Add("rows=" + kbd.Rows.Count);
                if (!kbdList) kFails.Add("keylist=" + CMain.InputKeys.Keylist.Count);
                if (!kbdGetInv) kFails.Add("getInv=" + CMain.InputKeys.GetKey(KeybindOptions.Inventory));
                if (!kbdGetInvDefault) kFails.Add("getInvDef");
                if (kbdFramePx < 3000) kFails.Add("kbdFrame=" + kbdFramePx);
                if (kbdRowBtnPx < 4) kFails.Add("kbdRowBtn=" + kbdRowBtnPx);

                // 行按钮点击 → WaitingForBind 置位；再点 → 清空。
                var firstRow = kbd.Rows.OfType<KeybindRow>().FirstOrDefault();
                bool kbdRowFound = firstRow != null;
                bool rowClicked = false, cancelClicked = false, kbdWaiting = false, kbdCancel = false;
                if (firstRow != null)
                {
                    rowClicked = ClickControl(firstRow.CurrentBindButton);
                    kbdWaiting = kbd.WaitingForBind != null && kbd.WaitingForBind.function == firstRow.KeyBind.function;
                    cancelClicked = ClickControl(firstRow.CurrentBindButton);
                    kbdCancel = kbd.WaitingForBind == null;
                }

                // CheckNewInput：Ctrl+K → Key=K、RequireCtrl=1；Delete → Key=None。
                var invBind = CMain.InputKeys.Keylist.Single(x => x.function == KeybindOptions.Inventory);
                CMain.Ctrl = true;
                kbd.WaitingForBind = invBind;
                kbd.CheckNewInput(new KeyEventArgs(Keys.K));
                bool kbdKey = invBind.Key == Keys.K && invBind.RequireCtrl == 1 && kbd.WaitingForBind == null;
                bool kbdGetK = CMain.InputKeys.GetKey(KeybindOptions.Inventory) == "Ctrl + K";
                // CheckNewInput 契约：调用前须置 WaitingForBind（前一次调用已在行 336 清空）。
                kbd.WaitingForBind = invBind;
                kbd.CheckNewInput(new KeyEventArgs(Keys.Delete));
                bool kbdDel = invBind.Key == Keys.None && invBind.RequireCtrl == 2;
                bool kbdGetEmpty = CMain.InputKeys.GetKey(KeybindOptions.Inventory) == "";
                CMain.Ctrl = false;

                if (!kbdRowFound) kFails.Add("firstRow");
                if (!rowClicked) kFails.Add("rowClick");
                if (!kbdWaiting) kFails.Add("waiting=" + (kbd.WaitingForBind == null ? "null" : kbd.WaitingForBind.function.ToString()));
                if (!cancelClicked) kFails.Add("cancelClick");
                if (!kbdCancel) kFails.Add("cancel");
                if (!kbdKey) kFails.Add("checkNew");
                if (!kbdGetK) kFails.Add("getK=" + CMain.InputKeys.GetKey(KeybindOptions.Inventory));
                if (!kbdDel) kFails.Add("del");
                if (!kbdGetEmpty) kFails.Add("getEmpty=" + CMain.InputKeys.GetKey(KeybindOptions.Inventory));

                var kFl = new Color32[kpx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(kpx, (_rtH - 1 - y) * _rtW, kFl, y * _rtW, _rtW);
                kRead.SetPixels32(kFl);
                kRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-settings-pass4.png"), kRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(kRead);

                // 诊断（中文路径）：控制树坐标 + Build 纹理有效性 + Draw 管线。
                DiagBuild("post-pass4");
                Console.WriteLine($"[netprobe] diag kbd=({kbd.DisplayLocation.X},{kbd.DisplayLocation.Y}) page=({kbd.PageLabel.DisplayLocation.X},{kbd.PageLabel.DisplayLocation.Y}) '{kbd.PageLabel.Text}' rows={kbd.Rows.Count}");
                if (firstRow != null)
                    Console.WriteLine($"[netprobe] diag row=({firstRow.DisplayLocation.X},{firstRow.DisplayLocation.Y}) bind=({firstRow.BindName.DisplayLocation.X},{firstRow.BindName.DisplayLocation.Y}) '{firstRow.BindName.Text}' btn=({firstRow.CurrentBindButton.DisplayLocation.X},{firstRow.CurrentBindButton.DisplayLocation.Y})");
                var dtx = TextGlyphBuilder.Build("背包开/关", Settings.FontName, 8, true);
                if (dtx == null) Console.WriteLine("[netprobe] diag build=null");
                else
                {
                    var dpx2 = dtx.GetPixels32();
                    int dnw = 0;
                    for (int i = 0; i < dpx2.Length; i++) if (nearWhite(dpx2[i])) dnw++;
                    Console.WriteLine($"[netprobe] diag build={dtx.width}x{dtx.height} px={dnw}");
                    UnityEngine.Object.DestroyImmediate(dtx);
                }
                var dRT = RenderTexture.GetTemporary(200, 80, 24, RenderTextureFormat.ARGB32);
                try
                {
                    var dtx2 = TextGlyphBuilder.Build("移动", Settings.FontName, 8, true);
                    CrystalSpriteBatch.Begin(dRT, 200, 80);
                    CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                    if (dtx2 != null) CrystalSpriteBatch.Draw(dtx2, new Rect(0, 0, dtx2.width, dtx2.height), new Vector3(20, 20, 0), Color.white);
                    CrystalSpriteBatch.End();
                    var dr2 = new Texture2D(200, 80, TextureFormat.RGBA32, false);
                    RenderTexture.active = dRT;
                    dr2.ReadPixels(new Rect(0, 0, 200, 80), 0, 0);
                    dr2.Apply();
                    RenderTexture.active = null;
                    var dpx3 = dr2.GetPixels32();
                    int dnw3 = 0;
                    for (int i = 0; i < dpx3.Length; i++) if (nearWhite(dpx3[i])) dnw3++;
                    Console.WriteLine($"[netprobe] diag draw={dnw3}");
                    if (dtx2 != null) UnityEngine.Object.DestroyImmediate(dtx2);
                    UnityEngine.Object.DestroyImmediate(dr2);
                }
                finally { RenderTexture.ReleaseTemporary(dRT); }

                _uiOk = sFails.Count == 0 && hFails.Count == 0 && kFails.Count == 0;
                _uiFail = $"coFrame={coFramePx} coAll={coAllPx} coChatTab={coChatTabPx} co2Frame={co2FramePx} co2On={co2OnPx}"
                    + $" helpFrame={helpFramePx} helpImg={helpImgPx} helpPageLabel={helpPageLabelPx}"
                    + $" kbdFrame={kbdFramePx} kbdRowBtn={kbdRowBtnPx} pages={helpDlg.Pages.Count}"
                    + (sFails.Count > 0 ? " FAIL:" + string.Join(",", sFails) : "")
                    + (hFails.Count > 0 ? " FAIL:" + string.Join(",", hFails) : "")
                    + (kFails.Count > 0 ? " FAIL:" + string.Join(",", kFails) : "");
                Console.WriteLine($"[netprobe] settings {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // 中文零渲染触发点定位：逐字符 quads（中文字形是否有 quad）+ 图集尺寸（是否已满）。
        // pre-pass1 = 图集仅 PreWarm ASCII；post-pass4 = WarmTree 已填充海量字形。
        // quads=0 → 图集耗尽/fallback 失效；quads=5 → 图集正常，触发点在别处。
        static void DiagBuild(string tag)
        {
            var f = UnityEngine.Font.CreateDynamicFontFromOSFont(Settings.FontName, 8);
            if (f == null) { Console.WriteLine($"[netprobe] diag {tag} font=null"); return; }
            f.RequestCharactersInTexture("背包开/关", 0, UnityEngine.FontStyle.Bold);
            var ft = f.material != null ? f.material.mainTexture as UnityEngine.Texture2D : null;
            int aw = ft != null ? ft.width : 0, ah = ft != null ? ft.height : 0;
            var s = new UnityEngine.TextGenerationSettings
            {
                font = f, fontSize = 8, fontStyle = UnityEngine.FontStyle.Bold, color = UnityEngine.Color.white,
                textAnchor = UnityEngine.TextAnchor.UpperLeft, richText = false, scaleFactor = 1f, lineSpacing = 1f,
                pivot = UnityEngine.Vector2.zero, horizontalOverflow = UnityEngine.HorizontalWrapMode.Overflow,
                verticalOverflow = UnityEngine.VerticalWrapMode.Overflow, resizeTextForBestFit = false, updateBounds = false,
            };
            string quads = "";
            foreach (char c in "背包开/关")
            {
                var g = new UnityEngine.TextGenerator();
                g.Populate(c.ToString(), s);
                quads += c + "=" + (g.verts.Count / 4) + " ";
            }
            Console.WriteLine($"[netprobe] diag {tag} atlas={aw}x{ah} quads: {quads}");
            DiagBuildCjk(tag, Settings.FontName, "arial");
            DiagBuildCjk(tag, "Microsoft YaHei", "yahei");
        }

        // Build 直测（pre-pass1 vs post-pass4 二分触发点）。arial = fallback CJK；yahei = 主字体 CJK，
        // 验证"渲染后 fallback 字形几何失效"是否仅影响 fallback（若 yahei 正常 → 换字体即可修复）。
        static void DiagBuildCjk(string tag, string fontName, string label)
        {
            var tex = TextGlyphBuilder.Build("背包开/关", fontName, 8, true);
            if (tex == null) { Console.WriteLine($"[netprobe] diag {tag} {label}=null"); return; }
            var px = tex.GetPixels32();
            int n = 0;
            for (int i = 0; i < px.Length; i++) if (px[i].r > 170 && px[i].g > 170 && px[i].b > 170) n++;
            Console.WriteLine($"[netprobe] diag {tag} {label}-build={tex.width}x{tex.height} px={n}");
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // 真实鼠标点击合成：Scene.OnMouseMove/Down/Up/Click 走 MirControl hit-test → 触发目标按钮 Click 处理器。
        // 返回 hover+pressed 是否命中（UiInput 探针同语义）。按钮中心 = DisplayLocation + TrueSize/2。
        static bool ClickControl(MirControl c)
        {
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            MPoint ctr = c.DisplayLocation.Add(new MPoint(c.TrueSize.Width / 2, c.TrueSize.Height / 2));
            CMain.MPoint = ctr;
            GameScene.Scene.OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, ctr.X, ctr.Y, 0));
            bool hovered = MirControl.MouseControl == c;
            GameScene.Scene.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, ctr.X, ctr.Y, 0));
            bool pressed = MirControl.ActiveControl == c;
            GameScene.Scene.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, ctr.X, ctr.Y, 0));
            GameScene.Scene.OnMouseClick(new MouseEventArgs(MouseButtons.Left, 1, ctr.X, ctr.Y, 0));
            return hovered && pressed;
        }

        // 商城测试商品合成：ItemInfo.Image=1（Items[1] 图标 32×28），Date=Now（New 标签 7 天内）。
        static GameShopItem MakeShopItem(int gIndex, string name, ItemType type, string category, string cls,
            uint gold, uint credit, int stock, bool canGold, bool canCredit, bool top = false, bool deal = false)
        {
            var info = new ItemInfo
            {
                Index = gIndex,
                Name = name,
                Type = type,
                Shape = 0,
                Weight = 1,
                Image = 1,
                Durability = 1,
                StackSize = 1,
                Stats = new Stats(),
            };
            return new GameShopItem
            {
                ItemIndex = gIndex,
                GIndex = gIndex,
                Info = info,
                GoldPrice = gold,
                CreditPrice = credit,
                Count = 1,
                Class = cls,
                Category = category,
                Stock = stock,
                Deal = deal,
                TopItem = top,
                Date = DateTime.Now,
                CanBuyGold = canGold,
                CanBuyCredit = canCredit,
            };
        }

        // 迭代包8 英雄+宠物渲染探针：登录→进图后合成 UserHeroObject/Mount 测试数据，构建
        // HeroInventoryDialog/HeroBeltDialog/HeroInfoPanel/HeroBehaviourPanel/HeroManageDialog/
        // MountDialog/HeroMenuPanel 控制树 → 数据+像素断言 → 5 张 PNG。net-hero.ps1 编排。
        // 坐标基线：各对话框 Location=(20,20) 起平铺，子控件相对坐标源自逐字移植的 Layout。
        static void RenderHero()
        {
            // ProbeLang.Ensure(); // 中文语言包：推迟到全部任务完成后恢复（见任务 #18）
            UiText.Install();
            UiText.PreWarm(8);

            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "hero:prguse-missing"; return; }
            Libraries.Prguse = prguse;
            var prguse2 = SceneRender.EnsureMLibrary("Prguse2");
            if (prguse2 == null) { _uiFail = "hero:prguse2-missing"; return; }
            Libraries.Prguse2 = prguse2;
            var items = SceneRender.EnsureMLibrary("Items");
            if (items == null) { _uiFail = "hero:items-missing"; return; }
            Libraries.Items = items;
            var stateItems = SceneRender.EnsureMLibrary("Stateitem");
            if (stateItems == null) { _uiFail = "hero:stateitem-missing"; return; }
            Libraries.StateItems = stateItems;
            var title = SceneRender.EnsureMLibrary("Title");
            if (title == null) { _uiFail = "hero:title-missing"; return; }
            Libraries.Title = title;
            var ui = SceneRender.EnsureMLibrary("UI");
            if (ui == null) { _uiFail = "hero:ui-missing"; return; }
            Libraries.UI_32bit = ui;

            var user = MapObject.User;
            if (user == null) { _uiFail = "hero:no-user"; return; }
            GameScene.User = user;
            MapControl.User = user;
            user.Name = "Probe";
            user.Level = 15;
            // MountDialog 动画帧组开关（>=0 显示 StartIndex+MountType*20 帧组，旧客户端 UserObject 由装备 Shape 同步）。
            user.MountType = 0;

            // ---------- 测试物品：剑（Weapon）+ 药水（Potion，Count 3 角标） ----------
            var swordInfo = new ItemInfo { Index = 4001, Name = "ProbeSword", Type = ItemType.Weapon, Shape = 1, Weight = 5, Image = 1, Durability = 10, StackSize = 1, Stats = new Stats() };
            var sword = new UserItem(swordInfo) { UniqueID = 31, CurrentDura = 10, MaxDura = 10 };
            var potionInfo = new ItemInfo { Index = 4002, Name = "ProbePotion", Type = ItemType.Potion, Shape = 0, Weight = 1, Image = 1, Durability = 1, StackSize = 5, Stats = new Stats() };
            var potion = new UserItem(potionInfo) { UniqueID = 32, CurrentDura = 1, MaxDura = 1, Count = 3 };

            // 坐骑：5 槽 Mount 装备（SwitchType case 5 → 布局 167 + StartIndex 1330 + 5 格可见），
            // 格内放 Reins/Bells/Saddle/Ribbon/Mask 五种配饰（MountDialog.Grid 的 ItemArray 直接读 Slots）。
            var mountItem = new UserItem(new ItemInfo { Index = 4301, Name = "ProbeMount", Type = ItemType.Mount, Shape = 0, Weight = 5, Image = 1, Durability = 100, StackSize = 1, Stats = new Stats() })
            { UniqueID = 50, CurrentDura = 90, MaxDura = 100, Slots = new UserItem[5] };
            mountItem.Slots[(int)MountSlot.Reins] = new UserItem(new ItemInfo { Index = 4311, Name = "ProbeReins", Type = ItemType.Reins, Shape = 0, Weight = 1, Image = 1, Durability = 1, StackSize = 1, Stats = new Stats() }) { UniqueID = 51, CurrentDura = 1, MaxDura = 1 };
            mountItem.Slots[(int)MountSlot.Bells] = new UserItem(new ItemInfo { Index = 4312, Name = "ProbeBells", Type = ItemType.Bells, Shape = 0, Weight = 1, Image = 1, Durability = 1, StackSize = 1, Stats = new Stats() }) { UniqueID = 52, CurrentDura = 1, MaxDura = 1 };
            mountItem.Slots[(int)MountSlot.Saddle] = new UserItem(new ItemInfo { Index = 4313, Name = "ProbeSaddle", Type = ItemType.Saddle, Shape = 0, Weight = 1, Image = 1, Durability = 1, StackSize = 1, Stats = new Stats() }) { UniqueID = 53, CurrentDura = 1, MaxDura = 1 };
            mountItem.Slots[(int)MountSlot.Ribbon] = new UserItem(new ItemInfo { Index = 4314, Name = "ProbeRibbon", Type = ItemType.Ribbon, Shape = 0, Weight = 1, Image = 1, Durability = 1, StackSize = 1, Stats = new Stats() }) { UniqueID = 54, CurrentDura = 1, MaxDura = 1 };
            mountItem.Slots[(int)MountSlot.Mask] = new UserItem(new ItemInfo { Index = 4315, Name = "ProbeMask", Type = ItemType.Mask, Shape = 0, Weight = 1, Image = 1, Durability = 1, StackSize = 1, Stats = new Stats() }) { UniqueID = 55, CurrentDura = 1, MaxDura = 1 };
            user.Equipment[(int)EquipmentSlot.Mount] = mountItem;

            // 英雄对象（模拟 S.UserInformation 简化）：自动喝药开 + 快捷槽 + 背包。
            // 背包前 2 格（0/1）供 HeroBeltDialog（ItemSlot=0/1），2/3 供 HeroInventoryDialog（ItemSlot=2+idx）。
            var hero = new UserHeroObject(9001);
            hero.Name = "Hero";
            hero.Class = MirClass.Warrior;
            hero.Gender = MirGender.Male;
            hero.Level = 10;
            hero.HP = 100;
            hero.MP = 50;
            hero.Experience = 500;
            hero.MaxExperience = 1000;
            hero.Stats[Stat.HP] = 200;
            hero.Stats[Stat.MP] = 100;
            hero.Inventory = new UserItem[40];
            hero.Equipment = new UserItem[14];
            hero.AutoPot = true;
            hero.AutoHPPercent = 60;
            hero.AutoMPPercent = 40;
            hero.Inventory[0] = potion;
            hero.Inventory[1] = sword;
            hero.Inventory[2] = sword;
            hero.Inventory[3] = potion;
            hero.HPItem[0] = potion;
            hero.MPItem[0] = potion;
            GameScene.Hero = hero;
            MapObject.Hero = hero;
            GameScene.Scene.HeroSpawnState = HeroSpawnState.Summoned;
            GameScene.MaximumHeroCount = 3;
            GameScene.HeroStorage = new ClientHeroInformation[8];
            GameScene.HeroStorage[0] = new ClientHeroInformation { Index = 1, Name = "Hero1", Level = 10, Class = MirClass.Warrior, Gender = MirGender.Male };
            GameScene.HeroStorage[1] = new ClientHeroInformation { Index = 2, Name = "Hero2", Level = 12, Class = MirClass.Taoist, Gender = MirGender.Female };

            // HeroBeltDialog/HeroBehaviourPanel 构造读 Scene.MainDialog.Location，须先建 MainDialog。
            var main = new MainDialog { Parent = GameScene.Scene };
            GameScene.Scene.MainDialog = main;

            // ---------- 遍1：英雄背包（Prguse 1422 + Grid 物品 + AutoPot 按钮/预览区） ----------
            var heroInv = new HeroInventoryDialog { Parent = GameScene.Scene };
            GameScene.Scene.HeroInventoryDialog = heroInv;
            heroInv.Location = new MPoint(20, 20);
            heroInv.Visible = true;

            // ---------- 遍2：英雄状态（14）+ 腰带（1921）+ 行为（1840 组） ----------
            var heroInfo = new HeroInfoPanel { Parent = GameScene.Scene };
            heroInfo.Location = new MPoint(20, 20);
            heroInfo.Update();
            var heroBelt = new HeroBeltDialog { Parent = GameScene.Scene };
            heroBelt.Location = new MPoint(280, 20);
            var heroBeh = new HeroBehaviourPanel { Parent = GameScene.Scene };
            heroBeh.Location = new MPoint(20, 130);
            heroBeh.UpdateBehaviour(HeroBehaviour.Attack);

            // ---------- 遍3：英雄管理（1688 + 当前/可选头像） ----------
            var heroMan = new HeroManageDialog { Parent = GameScene.Scene };
            heroMan.Location = new MPoint(20, 20);
            heroMan.SetCurrentHero(GameScene.HeroStorage[0]);
            heroMan.RefreshInterface();

            // ---------- 遍4：坐骑（167 五槽布局 + 动画帧组 + 5 配饰格） ----------
            var mount = new MountDialog { Parent = GameScene.Scene };
            GameScene.Scene.MountDialog = mount;
            mount.Location = new MPoint(20, 20);

            // ---------- 遍5：英雄菜单（2179 三按钮） ----------
            var heroMenu = new HeroMenuPanel(GameScene.Scene);
            heroMenu.Location = new MPoint(20, 20);

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                var iFails = new System.Collections.Generic.List<string>();
                var sFails = new System.Collections.Generic.List<string>();
                var gFails = new System.Collections.Generic.List<string>();
                var mFails = new System.Collections.Generic.List<string>();
                var uFails = new System.Collections.Generic.List<string>();
                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
                Func<Color32, bool> bright = c => c.r + c.g + c.b > 60;
                // 8F 小字号文本抗锯齿下中心像素常低于 240，用宽松亮白（含描边内字芯）。
                Func<Color32, bool> nearWhite = c => c.r > 170 && c.g > 170 && c.b > 170;

                // ================= 遍1：英雄背包 =================
                UiText.WarmTree(heroInv);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                heroInv.Draw();
                CrystalSpriteBatch.End();

                var iRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                iRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                iRead.Apply();
                RenderTexture.active = null;
                var ipx = iRead.GetPixels32();

                // HeroInventoryDialog（Prguse 1422）位于 (20,20)：frame + Grid[0] 剑 (34,43) + Grid[1] 药水 (71,43)
                // + AutoPot 时 HPButton (78,184) + AutoHPPercentLabel "60%" (78,211)。
                int heroInvFramePx = CountRegion(ipx, 20, 20, 340, 224, lit);
                int heroInvIcon0Px = CountRegion(ipx, 34, 43, 36, 32, bright);
                int heroInvIcon1Px = CountRegion(ipx, 71, 43, 36, 32, bright);
                int autoPotBtnPx = CountRegion(ipx, 78, 184, 60, 25, bright);
                int autoPotLabelPx = CountRegion(ipx, 78, 211, 60, 25, nearWhite);

                if (heroInvFramePx < 600) iFails.Add("heroInvFrame=" + heroInvFramePx);
                if (heroInvIcon0Px < 40) iFails.Add("heroInvIcon0=" + heroInvIcon0Px);
                if (heroInvIcon1Px < 40) iFails.Add("heroInvIcon1=" + heroInvIcon1Px);
                if (autoPotBtnPx < 30) iFails.Add("autoPotBtn=" + autoPotBtnPx);
                if (autoPotLabelPx < 5) iFails.Add("autoPotLabel=" + autoPotLabelPx);

                // 遍1 渲染存档（调试/验收）。
                var i1Fl = new Color32[ipx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(ipx, (_rtH - 1 - y) * _rtW, i1Fl, y * _rtW, _rtW);
                iRead.SetPixels32(i1Fl);
                iRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-hero-pass1.png"), iRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(iRead);

                // ================= 遍2：英雄状态 + 腰带 + 行为 =================
                UiText.WarmTree(heroInfo);
                UiText.WarmTree(heroBelt);
                UiText.WarmTree(heroBeh);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                heroInfo.Draw();
                heroBelt.Draw();
                heroBeh.Draw();
                CrystalSpriteBatch.End();

                var sRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                sRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                sRead.Apply();
                RenderTexture.active = null;
                var spx = sRead.GetPixels32();

                // HeroInfoPanel（Prguse 14）位于 (20,20)：frame + Avatar 1400 (34,39) + NameLabel "Hero" (48,94)。
                // HeroBeltDialog（Prguse 1921）位于 (280,20)：frame + Grid[0] 药水 (292,23)。
                // HeroBehaviourPanel（1840 组）位于 (20,130)：四个行为按钮。
                int infoFramePx = CountRegion(spx, 20, 20, 200, 110, lit);
                int infoAvatarPx = CountRegion(spx, 34, 39, 40, 50, bright);
                int infoNamePx = CountRegion(spx, 48, 94, 97, 14, nearWhite);
                int beltFramePx = CountRegion(spx, 280, 20, 110, 55, lit);
                int beltCellPx = CountRegion(spx, 292, 23, 32, 32, bright);
                int behaviourPx = CountRegion(spx, 20, 130, 64, 17, bright);

                if (infoFramePx < 300) sFails.Add("infoFrame=" + infoFramePx);
                if (infoAvatarPx < 20) sFails.Add("infoAvatar=" + infoAvatarPx);
                if (infoNamePx < 5) sFails.Add("infoName=" + infoNamePx);
                if (beltFramePx < 150) sFails.Add("beltFrame=" + beltFramePx);
                if (beltCellPx < 30) sFails.Add("beltCell=" + beltCellPx);
                if (behaviourPx < 30) sFails.Add("behaviour=" + behaviourPx);

                // 遍2 渲染存档。
                var s2Fl = new Color32[spx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(spx, (_rtH - 1 - y) * _rtW, s2Fl, y * _rtW, _rtW);
                sRead.SetPixels32(s2Fl);
                sRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-hero-pass2.png"), sRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(sRead);

                // ================= 遍3：英雄管理 =================
                UiText.WarmTree(heroMan);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                heroMan.Draw();
                CrystalSpriteBatch.End();

                var gRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                gRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                gRead.Apply();
                RenderTexture.active = null;
                var gpx = gRead.GetPixels32();

                // HeroManageDialog（Prguse 1688）位于 (20,20)：frame + CurrentAvatar (40,86) + Avatars[0] (123,86)，
                // 头像 Index = HeroAvatar(Class,Gender)+370 = 1770。
                int manageFramePx = CountRegion(gpx, 20, 20, 260, 180, lit);
                int currentAvatarPx = CountRegion(gpx, 40, 86, 50, 60, bright);
                int slotAvatarPx = CountRegion(gpx, 123, 86, 50, 60, bright);

                if (manageFramePx < 400) gFails.Add("manageFrame=" + manageFramePx);
                if (currentAvatarPx < 20) gFails.Add("currentAvatar=" + currentAvatarPx);
                if (slotAvatarPx < 20) gFails.Add("slotAvatar=" + slotAvatarPx);

                // 遍3 渲染存档。
                var g3Fl = new Color32[gpx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(gpx, (_rtH - 1 - y) * _rtW, g3Fl, y * _rtW, _rtW);
                gRead.SetPixels32(g3Fl);
                gRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-hero-pass3.png"), gRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(gRead);

                // ================= 遍4：坐骑 =================
                UiText.WarmTree(mount);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                mount.Draw();
                CrystalSpriteBatch.End();

                var mRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                mRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                mRead.Apply();
                RenderTexture.active = null;
                var mpx = mRead.GetPixels32();

                // MountDialog（Prguse 167）位于 (20,20)：frame + 动画帧组 (20,90) + Reins 格 (56,343)
                // + Mask 格 (272,343) + MountName "ProbeMount" (50,30)。
                int mountFramePx = CountRegion(mpx, 20, 20, 340, 350, lit);
                int mountAnimPx = CountRegion(mpx, 20, 90, 150, 150, bright);
                int reinsPx = CountRegion(mpx, 56, 343, 34, 30, bright);
                int maskPx = CountRegion(mpx, 272, 343, 34, 30, bright);
                int mountNamePx = CountRegion(mpx, 50, 30, 260, 15, nearWhite);

                if (mountFramePx < 800) mFails.Add("mountFrame=" + mountFramePx);
                if (mountAnimPx < 30) mFails.Add("mountAnim=" + mountAnimPx);
                if (reinsPx < 30) mFails.Add("reins=" + reinsPx);
                if (maskPx < 30) mFails.Add("mask=" + maskPx);
                if (mountNamePx < 5) mFails.Add("mountName=" + mountNamePx);

                // 遍4 渲染存档。
                var m4Fl = new Color32[mpx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(mpx, (_rtH - 1 - y) * _rtW, m4Fl, y * _rtW, _rtW);
                mRead.SetPixels32(m4Fl);
                mRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-hero-pass4.png"), mRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(mRead);

                // ================= 遍5：英雄菜单 =================
                UiText.WarmTree(heroMenu);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                heroMenu.Draw();
                CrystalSpriteBatch.End();

                var uRead = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                uRead.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                uRead.Apply();
                RenderTexture.active = null;
                var upx = uRead.GetPixels32();

                // HeroMenuPanel（Prguse 2179）位于 (20,20) 24×61：frame + HeroMagicsButton (23,23)。
                int menuFramePx = CountRegion(upx, 20, 20, 24, 61, lit);
                int menuBtnPx = CountRegion(upx, 23, 23, 16, 16, bright);

                if (menuFramePx < 40) uFails.Add("menuFrame=" + menuFramePx);
                if (menuBtnPx < 10) uFails.Add("menuBtn=" + menuBtnPx);

                // 遍5 渲染存档。
                var u5Fl = new Color32[upx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(upx, (_rtH - 1 - y) * _rtW, u5Fl, y * _rtW, _rtW);
                uRead.SetPixels32(u5Fl);
                uRead.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)), "net-hero-pass5.png"), uRead.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(uRead);

                // 数据断言：Grid 绑定 / AutoPot 开关 / 管理头像 / 坐骑 5 槽布局。
                bool heroInvGrid0 = heroInv.Grid[0].Item != null && heroInv.Grid[0].Item.Info.Name == "ProbeSword";
                bool heroInvGrid1 = heroInv.Grid[1].Item != null && heroInv.Grid[1].Item.Info.Name == "ProbePotion";
                bool heroInvAutoPot = heroInv.HPButton.Visible && heroInv.MPButton.Visible;
                bool beltGrid0 = heroBelt.Grid[0].Item != null && heroBelt.Grid[0].Item.Info.Name == "ProbePotion";
                bool manageSlot0 = heroMan.Avatars[0].Info != null && heroMan.Avatars[0].Info.Name == "Hero1" && heroMan.Avatars[0].Index == 1770;
                bool mount5Slot = mount.Index == 167 && mount.Grid[(int)MountSlot.Reins].Item != null && mount.Grid[(int)MountSlot.Mask].Visible;
                if (heroInv.Grid.Length != 40) iFails.Add("heroInvGrid=" + heroInv.Grid.Length);
                if (!heroInvGrid0) iFails.Add("heroInvGrid0");
                if (!heroInvGrid1) iFails.Add("heroInvGrid1");
                if (!heroInvAutoPot) iFails.Add("autoPotOff");
                if (!beltGrid0) sFails.Add("beltGrid0");
                if (heroInfo.AvatarIndex != 1400) sFails.Add("avatarIdx=" + heroInfo.AvatarIndex);
                if (!manageSlot0) gFails.Add("manageSlot0");
                if (!mount5Slot) mFails.Add("mount5Slot");
                if (mount.MountName.Text != "ProbeMount") mFails.Add("mountName=" + mount.MountName.Text);

                _uiOk = iFails.Count == 0 && sFails.Count == 0 && gFails.Count == 0 && mFails.Count == 0 && uFails.Count == 0;
                _uiFail = $"inv={heroInv.Grid.Length} autoPot={heroInvAutoPot} avatar={heroInfo.AvatarIndex} avatars0={heroMan.Avatars[0].Info?.Name ?? "null"}/{heroMan.Avatars[0].Index} mountIdx={mount.Index} mountName={mount.MountName.Text} heroInvFrame={heroInvFramePx} heroInvIcon0={heroInvIcon0Px} heroInvIcon1={heroInvIcon1Px} autoPotBtn={autoPotBtnPx} autoPotLabel={autoPotLabelPx} infoFrame={infoFramePx} infoAvatar={infoAvatarPx} infoName={infoNamePx} beltFrame={beltFramePx} beltCell={beltCellPx} behaviour={behaviourPx} manageFrame={manageFramePx} currentAvatar={currentAvatarPx} slotAvatar={slotAvatarPx} mountFrame={mountFramePx} mountAnim={mountAnimPx} reins={reinsPx} mask={maskPx} mountNamePx={mountNamePx} menuFrame={menuFramePx} menuBtn={menuBtnPx}"
                    + (iFails.Count > 0 ? " FAIL:" + string.Join(",", iFails) : "")
                    + (sFails.Count > 0 ? " FAIL:" + string.Join(",", sFails) : "")
                    + (gFails.Count > 0 ? " FAIL:" + string.Join(",", gFails) : "")
                    + (mFails.Count > 0 ? " FAIL:" + string.Join(",", mFails) : "")
                    + (uFails.Count > 0 ? " FAIL:" + string.Join(",", uFails) : "");
                Console.WriteLine($"[netprobe] hero {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // 迭代包2 输入探针：登录→StartGame→合成鼠标事件驱动真实 MainDialog/ChatDialog/InventoryDialog
        // 控制树（hover→pressed→click 状态迁移 + ChatTextBox 光标）→ 数据+像素断言 → PNG。net-input.ps1 编排。
        // 点击链走 GameScene 鼠标入口（MirScene 分发语义）：OnMouseMove 命中→MouseControl=按钮，
        // OnMouseDown→ActiveControl=按钮（Index 转 pressed），OnMouseUp 已 Deactivate 清空 ActiveControl，
        // OnMouseClick 由入口的 MouseControl 兜底触发 Click→InventoryDialog.Show。
        static void RenderUiInput()
        {
            // ProbeLang.Ensure(); // 中文语言包：推迟到全部任务完成后恢复（见任务 #18）
            UiText.Install();
            UiText.PreWarm(8);

            var prguse = SceneRender.EnsureMLibrary("Prguse");
            if (prguse == null) { _uiFail = "input:prguse-missing"; return; }
            Libraries.Prguse = prguse;
            var prguse2 = SceneRender.EnsureMLibrary("Prguse2");
            if (prguse2 == null) { _uiFail = "input:prguse2-missing"; return; }
            Libraries.Prguse2 = prguse2;
            var items = SceneRender.EnsureMLibrary("Items");
            if (items == null) { _uiFail = "input:items-missing"; return; }
            Libraries.Items = items;
            var stateItems = SceneRender.EnsureMLibrary("Stateitem");
            if (stateItems == null) { _uiFail = "input:stateitem-missing"; return; }
            Libraries.StateItems = stateItems;
            var title = SceneRender.EnsureMLibrary("Title");
            if (title == null) { _uiFail = "input:title-missing"; return; }
            Libraries.Title = title;
            var ui = SceneRender.EnsureMLibrary("UI");
            if (ui == null) { _uiFail = "input:ui-missing"; return; }
            Libraries.UI_32bit = ui;

            var user = MapObject.User;
            if (user == null) { _uiFail = "input:no-user"; return; }
            // InventoryDialog.Process 走 GameScene.User（static），须与 MapObject.User 对齐。
            GameScene.User = user;
            user.HP = _userHp;
            user.MP = _userMp;
            user.Level = (ushort)_userLevel;
            user.Class = _userClass;
            user.Experience = _userExp;
            user.MaxExperience = Math.Max(_userMaxExp, 1);
            user.Stats[Stat.HP] = Math.Max(_userHp, 1);
            user.Stats[Stat.MP] = Math.Max(_userMp, 1);
            user.Stats[Stat.BagWeight] = 100;
            user.CurrentBagWeight = 12;

            // 背包第一格测试物品（Grid[0].ItemSlot=6 跳过 0-5 腰带槽）。
            var bagSwordInfo = new ItemInfo
            {
                Index = 2001,
                Name = "ProbeBagSword",
                Type = ItemType.Weapon,
                Shape = 1,
                Weight = 5,
                Image = 1,
                Durability = 10,
                StackSize = 1,
                Stats = new Stats(),
            };
            bagSwordInfo.Stats[Stat.MaxDC] = 15;
            bagSwordInfo.Stats[Stat.MinDC] = 5;
            user.Inventory[6] = new UserItem(bagSwordInfo) { UniqueID = 11, CurrentDura = 10, MaxDura = 10 };

            GameScene.Scene.ChatNoticeDialog = new ChatNoticeDialog();
            // 输入命中依赖控制树（Scene.OnMouseMove 遍历 Scene.Controls），MainDialog/ChatDialog 须挂 Scene。
            var main = new MainDialog { Parent = GameScene.Scene };
            GameScene.Scene.MainDialog = main;
            var chat = new ChatDialog { Parent = GameScene.Scene };
            var inv = new InventoryDialog { Parent = GameScene.Scene };   // ctor Visible=false，输入阶段初始关闭
            GameScene.Scene.InventoryDialog = inv;
            var chr = new CharacterDialog(MirGridType.Equipment, user) { Parent = GameScene.Scene };
            GameScene.Scene.CharacterDialog = chr;
            chr.Hide();   // 装备窗关闭，避免干扰 MainDialog 按钮 hit-test

            main.Process();
            chat.ReceiveChat("Welcome to Crystal, this is the announcement line", ChatType.Announcement);
            chat.ReceiveChat("System: server online and accepting connections", ChatType.System);
            chat.ReceiveChat("Shout test from the probe character", ChatType.Shout2);
            chat.ReceiveChat("Danger zone ahead, proceed with caution", ChatType.System2);
            chat.StartIndex = 0;
            chat.Update();
            inv.RefreshInventory();
            inv.Process();

            var fails = new System.Collections.Generic.List<string>();

            // ---------- Phase A：MainDialog.InventoryButton 点击链（hover→pressed→click→开背包） ----------
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            var btn = main.InventoryButton;
            // 按钮屏幕中心 = DisplayLocation + 基图 TrueSize/2（hover 前 Index=1903，与 hover 图同尺寸）。
            MPoint btnCenter = btn.DisplayLocation.Add(new MPoint(btn.TrueSize.Width / 2, btn.TrueSize.Height / 2));
            CMain.MPoint = btnCenter;
            GameScene.Scene.OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, btnCenter.X, btnCenter.Y, 0));
            if (MirControl.MouseControl != btn)
                fails.Add("hover-mc=" + (MirControl.MouseControl == null ? "null" : MirControl.MouseControl.GetType().Name));
            if (btn.Index != btn.HoverIndex)
                fails.Add("hover-idx=" + btn.Index);
            GameScene.Scene.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, btnCenter.X, btnCenter.Y, 0));
            if (MirControl.ActiveControl != btn)
                fails.Add("down-ac=" + (MirControl.ActiveControl == null ? "null" : MirControl.ActiveControl.GetType().Name));
            if (btn.Index != btn.PressedIndex)
                fails.Add("down-idx=" + btn.Index);
            GameScene.Scene.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, btnCenter.X, btnCenter.Y, 0));
            GameScene.Scene.OnMouseClick(new MouseEventArgs(MouseButtons.Left, 1, btnCenter.X, btnCenter.Y, 0));
            if (!inv.Visible)
                fails.Add("click-not-open");

            // ---------- Phase B：ChatTextBox 光标（Focused → DrawControl 画白竖线） ----------
            chat.ChatTextBox.Visible = true;
            chat.ChatTextBox.Text = "probe-input";
            chat.ChatTextBox.TextBox.SelectionLength = 0;
            chat.ChatTextBox.TextBox.SelectionStart = chat.ChatTextBox.Text.Length;
            chat.ChatTextBox.SetFocus();
            if (!chat.ChatTextBox.TextBox.Focused)
                fails.Add("caret-not-focused");

            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                UiText.WarmTree(main);
                UiText.WarmTree(chat);
                UiText.WarmTree(inv);
                // MirTextBox 非 MirLabel（WarmTree 不覆盖），ChatTextBox 字形须显式预热，否则 batch 内建图集透明。
                UiText.WarmText("probe-input", chat.ChatTextBox.Font);

                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                main.Draw();
                chat.Draw();
                inv.Draw();
                CrystalSpriteBatch.End();

                var read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                // 背景 Clear(0.1f)=RGB(25,25,25)。lit=区别于背景；strictWhite=光标白线/白字形。
                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;
                Func<Color32, bool> strictWhite = c => c.r > 230 && c.g > 230 && c.b > 230;

                // 背包已开（Title 196 @ (0,0)）+ 按钮 hover 态图（1904）+ ChatTextBox 光标白线
                //（ForeColour=Black 字形 + DarkGray 底，区域内唯一 strictWhite 即光标竖线）。
                int bagFramePx = CountRegion(px, 0, 0, 320, 260, lit);
                int btnHoverPx = CountRegion(px, btn.DisplayLocation.X, btn.DisplayLocation.Y, btn.TrueSize.Width, btn.TrueSize.Height, lit);
                int caretPx = CountRegion(px, chat.ChatTextBox.DisplayLocation.X, chat.ChatTextBox.DisplayLocation.Y, chat.ChatTextBox.Size.Width, chat.ChatTextBox.Size.Height, strictWhite);

                if (bagFramePx < 1000) fails.Add("bagFrame=" + bagFramePx);
                if (btnHoverPx < 100) fails.Add("btnHover=" + btnHoverPx);
                if (caretPx < 5) fails.Add("caret=" + caretPx);

                _uiOk = fails.Count == 0;
                _uiFail = $"mc={(MirControl.MouseControl == null ? "null" : MirControl.MouseControl.GetType().Name)} ac={(MirControl.ActiveControl == null ? "null" : MirControl.ActiveControl.GetType().Name)} bagOpen={inv.Visible} btnIdx={btn.Index} caretFocused={chat.ChatTextBox.TextBox.Focused} bagFrame={bagFramePx} btnHover={btnHoverPx} caret={caretPx}"
                    + (fails.Count > 0 ? " FAIL:" + string.Join(",", fails) : "");
                Console.WriteLine($"[netprobe] input {(_uiOk ? "ok" : "fail")} seq={string.Join(">", _seq)} {_uiFail}");

                // R3 行序：EncodeToPNG 输出翻转图，编码前按行翻转（RenderUi 同款）。
                var inPx = read.GetPixels32();
                var inFl = new Color32[inPx.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(inPx, (_rtH - 1 - y) * _rtW, inFl, y * _rtW, _rtW);
                read.SetPixels32(inFl);
                read.Apply();

                string fullOut = Path.GetFullPath(_outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
        // 探针单次渲染后进程即退，临时纹理不回收（无需 DestroyImmediate，跨 End() flush 安全）。
        // lvTex 在 batch Begin 之前由调用方构建（动态字体图集需非渲染上下文）。
        static void RenderHud(Texture2D lvTex)
        {
            var hpTex = new Texture2D(Mathf.Max(1, Mathf.Clamp(_userHp, 0, HudBarMax)), 12, TextureFormat.RGBA32, false);
            FillTex(hpTex, Color.white);
            CrystalSpriteBatch.Draw(hpTex, new Rect(0, 0, hpTex.width, hpTex.height), new Vector3(10, 10, 0), new Color(0.85f, 0.1f, 0.08f, 1f));

            var mpTex = new Texture2D(Mathf.Max(1, Mathf.Clamp(_userMp, 0, HudBarMax)), 12, TextureFormat.RGBA32, false);
            FillTex(mpTex, Color.white);
            CrystalSpriteBatch.Draw(mpTex, new Rect(0, 0, mpTex.width, mpTex.height), new Vector3(10, 26, 0), new Color(0.1f, 0.25f, 0.85f, 1f));

            if (lvTex != null)
                CrystalSpriteBatch.Draw(lvTex, new Rect(0, 0, lvTex.width, lvTex.height), new Vector3(10, 42, 0), Color.white);
        }

        static void FillTex(Texture2D tex, Color c)
        {
            var c32 = (Color32)c;
            var px = new Color32[tex.width * tex.height];
            for (int i = 0; i < px.Length; i++) px[i] = c32;
            tex.SetPixels32(px);
            tex.Apply();
        }

        // R8 文本栅格化：TextGenerator 字形 UV → 字型图集像素合成文本纹理。
        static Texture2D BuildTextTexture(string text, int size)
        {
            var font = Font.CreateDynamicFontFromOSFont("Arial", size);
            var gen = new TextGenerator();
            var settings = new TextGenerationSettings
            {
                font = font,
                fontSize = size,
                fontStyle = FontStyle.Bold,
                color = Color.white,
                textAnchor = TextAnchor.UpperLeft,
                richText = false,
                scaleFactor = 1f,
                lineSpacing = 1f,
                pivot = Vector2.zero,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                resizeTextForBestFit = false,
                updateBounds = false,
            };
            gen.Populate(text, settings);
            var verts = gen.verts;
            var fontTex = font.material.mainTexture as Texture2D;
            if (fontTex == null || verts.Count == 0) return null;

            int minU = int.MaxValue, minV = int.MaxValue, maxU = int.MinValue, maxV = int.MinValue;
            foreach (var v in verts)
            {
                int u = Mathf.FloorToInt(v.uv0.x * fontTex.width);
                int vv = Mathf.FloorToInt(v.uv0.y * fontTex.height);
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
                minV = Mathf.Min(minV, vv); maxV = Mathf.Max(maxV, vv);
            }
            int tw = maxU - minU + 1, th = maxV - minV + 1;
            if (tw <= 0 || th <= 0) return null;

            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            var atlasPx = fontTex.GetPixels32();
            var px = new Color32[tw * th];
            int maxA = 0;
            for (int y = minV; y <= maxV; y++)
                for (int x = minU; x <= maxU; x++)
                {
                    var src = atlasPx[y * fontTex.width + x];
                    maxA = Mathf.Max(maxA, src.a);
                    // 字型图集为灰度+alpha（RGB 常为黑）：强制白色字形，保留 alpha，供 alpha 混合渲染。
                    px[(y - minV) * tw + (x - minU)] = src.a > 32 ? new Color32(255, 255, 255, src.a) : new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(px);
            tex.Apply();
            Console.WriteLine($"[netprobe] hud-text: {tw}x{th} maxA={maxA}");
            return tex;
        }

        // 按像素数组行主序统计矩形区域内满足谓词的像素数。
        static int CountRegion(Color32[] arr, int x0, int y0, int w, int h, Func<Color32, bool> pred)
        {
            int n = 0;
            for (int y = y0; y < y0 + h && y < _rtH; y++)
                for (int x = x0; x < x0 + w && x < _rtW; x++)
                    if (pred(arr[y * _rtW + x])) n++;
            return n;
        }

        static void ProcessInteract()
        {
            if (_istepDeadline > 0 && CMain.Time >= _istepDeadline)
            {
                FailStep(_istep);
                return;
            }

            switch (_istep)
            {
                case InteractStep.Chat:
                case InteractStep.Bag:
                case InteractStep.Use:
                    break; // 等待回复封包
                case InteractStep.Pickup:
                    if (_pickupState == 0 && _dropSpawned)
                    {
                        if (_pickupLoc == _userLoc) { SendPickUp(); _pickupState = 2; }
                        else
                        {
                            Console.WriteLine($"[netprobe] interact pickup walk {_pickupLoc.X},{_pickupLoc.Y} from {_userLoc.X},{_userLoc.Y}");
                            Network.Enqueue(new C.Walk { Direction = DirTo(Math.Sign(_pickupLoc.X - _userLoc.X), Math.Sign(_pickupLoc.Y - _userLoc.Y)) });
                            _movePending = true;
                            _pickupState = 1;
                        }
                    }
                    else if (_pickupState == 1 && !_movePending)
                    {
                        if (_pickupLoc == _userLoc) { SendPickUp(); _pickupState = 2; }
                        else if (Math.Abs(_pickupLoc.X - _userLoc.X) + Math.Abs(_pickupLoc.Y - _userLoc.Y) <= 2)
                        {
                            Network.Enqueue(new C.Walk { Direction = DirTo(Math.Sign(_pickupLoc.X - _userLoc.X), Math.Sign(_pickupLoc.Y - _userLoc.Y)) });
                            _movePending = true;
                        }
                        else FailStep(InteractStep.Pickup);
                    }
                    break;
                case InteractStep.Npc:
                    if (_npcSendDeadline > 0 && CMain.Time >= _npcSendDeadline)
                    {
                        if (_npcTry >= _npcList.Count) FailStep(InteractStep.Npc);
                        else TryNpc();
                    }
                    break;
                case InteractStep.Combat:
                    ProcessCombat();
                    break;
            }
        }

        static void StartInteract()
        {
            _istep = InteractStep.Chat;
            _istepDeadline = CMain.Time + 10000;
            _seq.Add("InteractStart");
            Console.WriteLine("[netprobe] interact step=Chat send=C.Chat");
            Network.Enqueue(new C.Chat { Message = "probe-interact-1" });
        }

        static void NextStep()
        {
            switch (_istep)
            {
                case InteractStep.Chat: BeginBag(); break;
                case InteractStep.Bag: BeginNpc(); break;
                case InteractStep.Npc: BeginPickup(); break;
                case InteractStep.Pickup: BeginUse(); break;
                case InteractStep.Use:
                    if (_combatEnabled) BeginCombat();
                    else FinishInteract();
                    break;
                case InteractStep.Combat: FinishInteract(); break;
            }
        }

        static void BeginBag()
        {
            _istep = InteractStep.Bag;
            _istepDeadline = CMain.Time + 10000;
            var slots = _inv.Select(x => x.slot).OrderBy(s => s).ToList();
            if (slots.Count < 2) { FailStep(InteractStep.Bag); return; }
            _bagA = slots[0];
            _bagB = slots[1];
            _seq.Add($"BagSwap:{_bagA}:{_bagB}");
            Console.WriteLine($"[netprobe] interact step=Bag swap {_bagA}<->{_bagB}");
            Network.Enqueue(new C.MoveItem { Grid = MirGridType.Inventory, From = _bagA, To = _bagB });
        }

        static void BeginNpc()
        {
            _istep = InteractStep.Npc;
            _istepDeadline = CMain.Time + 15000;
            if (_npcList.Count == 0) { FailStep(InteractStep.Npc); return; }
            _npcTry = 0;
            TryNpc();
        }

        static void TryNpc()
        {
            if (_npcTry >= _npcList.Count) { FailStep(InteractStep.Npc); return; }
            var npc = _npcList[_npcTry];
            _npcTry++;
            _npcSendDeadline = CMain.Time + 3000;
            _seq.Add($"CallNPC:{npc.id}");
            Console.WriteLine($"[netprobe] interact step=Npc call {npc.id} ({npc.name})");
            Network.Enqueue(new C.CallNPC { ObjectID = npc.id, Key = "[@MAIN]" });
        }

        static void BeginPickup()
        {
            _istep = InteractStep.Pickup;
            _istepDeadline = CMain.Time + 10000;
            var potion = _inv.FirstOrDefault(x => x.idx == 1987 || x.idx == 1988);
            if (potion.uid == 0) potion = _inv.FirstOrDefault();
            if (potion.uid == 0)
            {
                Console.WriteLine("[netprobe] interact no item in inventory");
                FailStep(InteractStep.Pickup);
                return;
            }
            _potionUid = potion.uid;
            _dropSpawned = false;
            _dropObjId = 0;
            _pickupState = 0;
            _pickupSent = false;
            _seq.Add($"DropItem:{_potionUid}");
            Console.WriteLine($"[netprobe] interact step=Pickup drop {_potionUid}");
            Network.Enqueue(new C.DropItem { UniqueID = _potionUid, Count = 1, HeroInventory = false });
        }

        static void PickupComplete()
        {
            if (_pickupOk) return;
            _pickupOk = true;
            _seq.Add("PickupOk");
            Console.WriteLine("[netprobe] interact step=Pickup ok");
            NextStep();
        }

        static void SendPickUp()
        {
            if (_pickupSent) return;
            _pickupSent = true;
            _seq.Add("PickUp");
            Console.WriteLine($"[netprobe] interact pickup at {_userLoc.X},{_userLoc.Y}");
            Network.Enqueue(new C.PickUp());
        }

        static void BeginUse()
        {
            _istep = InteractStep.Use;
            _istepDeadline = CMain.Time + 10000;
            _seq.Add($"UseItem:{_potionUid}");
            Console.WriteLine($"[netprobe] interact step=Use item {_potionUid}");
            Network.Enqueue(new C.UseItem { UniqueID = _potionUid, Grid = MirGridType.Inventory });
        }

        static void BeginCombat()
        {
            _istep = InteractStep.Combat;
            _istepDeadline = CMain.Time + 60000;
            _combatOrder = null;
            _combatOrderIdx = 0;
            _walkTarget = MPoint.Empty;
            _stuckCount = 0;
            _seq.Add("CombatStart");
            Console.WriteLine("[netprobe] interact step=Combat start");
            PickCombatTarget();
        }

        static void PickCombatTarget()
        {
            if (_combatOrder == null || _combatOrderIdx >= _combatOrder.Count)
            {
                _combatOrder = _monList.OrderBy(m => Math.Abs(m.loc.X - _userLoc.X) + Math.Abs(m.loc.Y - _userLoc.Y)).ToList();
                _combatOrderIdx = 0;
            }
            for (; _combatOrderIdx < _combatOrder.Count; _combatOrderIdx++)
            {
                var t = _combatOrder[_combatOrderIdx];
                if (t.loc == _userLoc) continue; // 同一格不可选
                _combatTargetId = t.id;
                _combatTargetLoc = t.loc;
                _combatWalking = true;
                _combatAttacked = false;
                _attackAttempts = 0;
                _movePending = false;
                _walkTarget = MPoint.Empty;
                _stuckCount = 0;
                _combatStepDeadline = CMain.Time + 10000;
                _combatOrderIdx++;
                _seq.Add($"CombatTarget:{_combatTargetId}@{_combatTargetLoc.X},{_combatTargetLoc.Y}");
                Console.WriteLine($"[netprobe] interact combat target {_combatTargetId}@{_combatTargetLoc.X},{_combatTargetLoc.Y} (#{_combatOrderIdx}/{_combatOrder.Count})");
                return;
            }
            FailStep(InteractStep.Combat);
        }

        static void TrackMonster(uint id, MPoint loc)
        {
            if (_mode != Mode.Interact) return;
            for (int i = 0; i < _monList.Count; i++)
                if (_monList[i].id == id) { _monList[i] = (_monList[i].id, loc); break; }
            if (_istep == InteractStep.Combat && id == _combatTargetId)
            {
                _combatTargetLoc = loc;
                if (_combatAttacked && !_combatWalking)
                {
                    _combatAttacked = false;
                    _combatWalking = true;
                    _movePending = false;
                    _walkTarget = MPoint.Empty;
                    _combatStepDeadline = CMain.Time + 10000;
                }
            }
        }

        static void ProcessCombat()
        {
            if (_combatWalking)
            {
                if (CMain.Time >= _combatStepDeadline)
                {
                    _movePending = false;
                    PickCombatTarget(); // 走位超时 → 换目标
                    return;
                }
                int dx = _combatTargetLoc.X - _userLoc.X;
                int dy = _combatTargetLoc.Y - _userLoc.Y;
                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && (dx != 0 || dy != 0))
                {
                    _combatDir = DirTo(dx, dy);
                    _combatWalking = false;
                    _combatAttacked = true;
                    _movePending = false;
                    _combatStepDeadline = CMain.Time + 3000;
                    _seq.Add($"Attack:{_combatDir}");
                    Console.WriteLine($"[netprobe] interact combat attack dir={_combatDir} @{_userLoc.X},{_userLoc.Y}");
                    Network.Enqueue(new C.Attack { Direction = _combatDir, Spell = Spell.None });
                }
                else if (_movePending)
                {
                    if (CMain.Time >= _combatStepDeadline) _movePending = false;
                }
                else
                {
                    if (!_walkTarget.IsEmpty && _userLoc != _walkTarget)
                    {
                        if (++_stuckCount >= 3)
                        {
                            _stuckCount = 0;
                            _walkTarget = MPoint.Empty;
                            _movePending = false;
                            Console.WriteLine($"[netprobe] interact combat stuck at {_userLoc.X},{_userLoc.Y} -> retarget");
                            PickCombatTarget(); // 走不动 → 换目标
                            return;
                        }
                    }
                    var dir = DirTo(Math.Sign(dx), Math.Sign(dy));
                    _walkTarget = new MPoint(_userLoc.X + Math.Sign(dx), _userLoc.Y + Math.Sign(dy));
                    _movePending = true;
                    _combatStepDeadline = CMain.Time + 8000;
                    Console.WriteLine($"[netprobe] interact combat walk {dir} @{_userLoc.X},{_userLoc.Y}->{_combatTargetLoc.X},{_combatTargetLoc.Y}");
                    Network.Enqueue(new C.Walk { Direction = dir });
                }
            }
            else if (_combatAttacked)
            {
                if (CMain.Time >= _combatStepDeadline)
                {
                    int dx = _combatTargetLoc.X - _userLoc.X;
                    int dy = _combatTargetLoc.Y - _userLoc.Y;
                    if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && (dx != 0 || dy != 0))
                    {
                        _attackAttempts++;
                        if (_attackAttempts >= 6)
                        {
                            _attackAttempts = 0;
                            _movePending = false;
                            _walkTarget = MPoint.Empty;
                            PickCombatTarget(); // 6 次未命中 → 换目标
                            return;
                        }
                        _combatDir = DirTo(dx, dy);
                        _combatStepDeadline = CMain.Time + 3000;
                        _seq.Add($"ReAttack:{_combatDir}");
                        Console.WriteLine($"[netprobe] interact combat reattack {_combatDir} @{_userLoc.X},{_userLoc.Y} (try {_attackAttempts})");
                        Network.Enqueue(new C.Attack { Direction = _combatDir, Spell = Spell.None });
                    }
                    else
                    {
                        _combatAttacked = false;
                        _combatWalking = true;
                        _movePending = false;
                        _walkTarget = MPoint.Empty;
                        _combatStepDeadline = CMain.Time + 10000;
                    }
                }
            }
        }

        static void FinishInteract()
        {
            _istep = InteractStep.Done;
            _istepDeadline = -1;
            _ok = _chatOk && _bagOk && _npcOk && _pickupOk && _useOk && (!_combatEnabled || _combatOk);
            _fail = _ok ? null : $"chat={_chatOk} bag={_bagOk} npc={_npcOk} pickup={_pickupOk} use={_useOk} combat={_combatOk}";
            Console.WriteLine($"[netprobe] interact chat={_chatOk} bag={_bagOk} npc={_npcOk} pickup={_pickupOk} use={_useOk} combat={_combatOk}");
            Done(_ok, _fail);
        }

        static void FailStep(InteractStep step)
        {
            _seq.Add($"FailStep:{step}");
            Done(false, $"step-{step}");
        }

        static MirDirection DirTo(int dx, int dy)
        {
            if (dx == 0 && dy < 0) return MirDirection.Up;
            if (dx > 0 && dy < 0) return MirDirection.UpRight;
            if (dx > 0 && dy == 0) return MirDirection.Right;
            if (dx > 0 && dy > 0) return MirDirection.DownRight;
            if (dx == 0 && dy > 0) return MirDirection.Down;
            if (dx < 0 && dy > 0) return MirDirection.DownLeft;
            if (dx < 0 && dy == 0) return MirDirection.Left;
            return MirDirection.UpLeft;
        }

        static void StartGame(int index)
        {
            _charIndex = index;
            _seq.Add($"SendStartGame:{index}");
            Network.Enqueue(new C.StartGame { CharacterIndex = index });
        }

        // ---- 阶段6 补验：Edge 状态机（del/run/split/revive/recon/autopath/magic）----
        static void ProcessEdge()
        {
            if (_estepDeadline > 0 && CMain.Time >= _estepDeadline)
            {
                FailEdge($"step-timeout:{_estep}");
                return;
            }

            if (_edgeSub == "del" && _estep == EdgeStep.DelRecon && _reconPhase == 1 && CMain.Time >= _reconAt)
            {
                // IP block 窗口过后重连，重放登录状态机（软删持久化需断开重连后断言）
                _reconPhase = 2;
                ResetLoginState();
                _seq.Add("Reconnect");
                Console.WriteLine("[netprobe] edge del reconnect");
                Network.Connect();
            }
            else if (_edgeSub == "recon" && _estep == EdgeStep.ReconGo && _reconPhase == 1 && CMain.Time >= _reconAt)
            {
                _reconPhase = 2;
                ResetLoginState();
                _estep = EdgeStep.ReconReconn;
                _seq.Add("Reconnect");
                Console.WriteLine("[netprobe] edge recon reconnect");
                Network.Connect();
            }
        }

        static void ResetLoginState()
        {
            _didNewAccount = false;
            _seq.Add("LoginStateReset");
        }

        static void BeginEdge()
        {
            _estepDeadline = CMain.Time + 30000;
            switch (_edgeSub)
            {
                case "run":
                    BeginRunWalk();
                    break;
                case "split":
                    BeginSplit();
                    break;
                case "revive":
                    _estep = EdgeStep.ReviveDie;
                    _seq.Add("SendDie");
                    Console.WriteLine("[netprobe] edge revive send @die");
                    Network.Enqueue(new C.Chat { Message = "@die" });
                    break;
                case "recon":
                    _estep = EdgeStep.ReconGo;
                    _reconPhase = 1;
                    _reconAt = CMain.Time + 5500;
                    _seq.Add("HardDisconnect");
                    Console.WriteLine("[netprobe] edge recon hard disconnect TCP");
                    Network.Disconnect();
                    break;
                case "magic":
                    _estep = EdgeStep.MagicGive;
                    _seq.Add($"SendGiveskill:{_edgeSpell}");
                    Console.WriteLine($"[netprobe] edge magic @giveskill {_edgeSpell} 1");
                    Network.Enqueue(new C.Chat { Message = $"@giveskill {_edgeSpell} 1" });
                    break;
                case "fishing":
                    // BlueFishingRod（idx=794）reqType=Level reqAmt=20（实证：type=Weapon shape=49 reqClass=None）
                    // → fresh level-1 角色 CanEquipItem 拒绝（equip-rejected）。先 @LEVEL 20 再 @make；
                    // 服务器串行处理 Chat，@make 后 C.EquipItem 时 Level 已达标，无竞态。
                    _estep = EdgeStep.FishMake;
                    _estepDeadline = CMain.Time + 20000;
                    _seq.Add("SendLevel20");
                    _seq.Add("SendMake");
                    Console.WriteLine("[netprobe] edge fishing @LEVEL 20 then @make BlueFishingRod");
                    Network.Enqueue(new C.Chat { Message = "@LEVEL 20" });
                    Network.Enqueue(new C.Chat { Message = "@make BlueFishingRod" });
                    break;
                case "autopath":
                    BeginAutoPath();
                    break;
                default:
                    FailEdge("unknown-edge-sub:" + _edgeSub);
                    break;
            }
        }

        // ---- run：Walk 一步设服务器 _stepCounter → Run 两步 ----
        static void BeginRunWalk()
        {
            _runTries = 0;
            StartRunWalk();
        }

        static void StartRunWalk()
        {
            _estep = EdgeStep.RunWalk;
            _estepDeadline = CMain.Time + 15000;
            if (!PickRunDir(out _runDir))
            {
                FailEdge("no-walk-dir");
                return;
            }
            _seq.Add($"RunWalk:{_runDir}");
            Console.WriteLine($"[netprobe] edge run walk dir={_runDir} @{_userLoc.X},{_userLoc.Y}");
            Network.Enqueue(new C.Walk { Direction = _runDir });
        }

        static void OnRunUserLoc()
        {
            if (_estep == EdgeStep.RunWalk)
            {
                _runWalkLoc = _userLoc;
                _estep = EdgeStep.RunGo;
                _estepDeadline = CMain.Time + 15000;
                _seq.Add($"RunWalkOk:{_runWalkLoc.X},{_runWalkLoc.Y}");
                // 重验 2 步路径：fresh 出生区可能有客户端尚未生成的 Blocking 对象（服务端会拒跑）
                var d = DirDelta(_runDir);
                var mc = GameScene.Scene?.MapControl;
                bool clear = mc != null &&
                    mc.EmptyCell(new MPoint(_runWalkLoc.X + d.dx, _runWalkLoc.Y + d.dy)) &&
                    mc.EmptyCell(new MPoint(_runWalkLoc.X + 2 * d.dx, _runWalkLoc.Y + 2 * d.dy));
                if (!clear)
                {
                    _seq.Add("RunPathBlocked");
                    Console.WriteLine($"[netprobe] edge run path blocked @{_runWalkLoc.X},{_runWalkLoc.Y} dir={_runDir}, repick");
                    RetryRunWalk();
                    return;
                }
                Console.WriteLine($"[netprobe] edge run walk ok @{_runWalkLoc.X},{_runWalkLoc.Y}, send Run");
                Network.Enqueue(new C.Run { Direction = _runDir });
            }
            else if (_estep == EdgeStep.RunGo)
            {
                // Run 后位置应 = Walk 位置 + 2 格（HumanObject.Run 无坐骑/迅捷时 steps=2）
                var d = DirDelta(_runDir);
                var expect = new MPoint(_runWalkLoc.X + 2 * d.dx, _runWalkLoc.Y + 2 * d.dy);
                bool ok = _userLoc == expect;
                _seq.Add($"RunGo:{_userLoc.X},{_userLoc.Y} expect={expect.X},{expect.Y} ok={ok}");
                Console.WriteLine($"[netprobe] edge run ok={ok} @{_userLoc.X},{_userLoc.Y} expect {expect.X},{expect.Y}");
                if (ok)
                {
                    Done(true, null);
                    return;
                }
                // 服务端拒跑（阻塞对象未在客户端生成时被 EmptyCell 预见）：换方向重走重跑，最多 3 次
                if (_runTries < 3)
                {
                    _seq.Add("RunRejected");
                    RetryRunWalk();
                    return;
                }
                Done(false, "run-delta-mismatch");
            }
        }

        static void RetryRunWalk()
        {
            _runTries++;
            StartRunWalk();
        }

        static bool PickRunDir(out MirDirection dir)
        {
            var mc = GameScene.Scene?.MapControl;
            if (mc == null) { dir = MirDirection.Up; return false; }
            for (int d = 0; d < 8; d++)
            {
                var dd = DirDelta((MirDirection)d);
                var one = new MPoint(_userLoc.X + dd.dx, _userLoc.Y + dd.dy);
                var two = new MPoint(_userLoc.X + 2 * dd.dx, _userLoc.Y + 2 * dd.dy);
                if (mc.EmptyCell(one) && mc.EmptyCell(two)) { dir = (MirDirection)d; return true; }
            }
            dir = MirDirection.Up;
            return false;
        }

        static (int dx, int dy) DirDelta(MirDirection dir)
        {
            switch (dir)
            {
                case MirDirection.Up: return (0, -1);
                case MirDirection.UpRight: return (1, -1);
                case MirDirection.Right: return (1, 0);
                case MirDirection.DownRight: return (1, 1);
                case MirDirection.Down: return (0, 1);
                case MirDirection.DownLeft: return (-1, 1);
                case MirDirection.Left: return (-1, 0);
                default: return (-1, -1);
            }
        }

        // ---- split：找可叠放栈拆 1（fresh 起始物均 Count=1，需 @make 造栈）----
        // 候选 = 起始背包各 ItemIndex。@make {idx} 2 对可叠放物（StackSize>=2）合并进原槽
        // （原 UniqueID 存活、Count 增加），对不可叠放物给两个 Count=1 单件。S.GainedItem 的
        // Clone Count 即判据：>=2 可叠放 → 按原槽 UID 拆；==1 不可叠放 → 试下一候选。
        static void BeginSplit()
        {
            _estep = EdgeStep.SplitWait;
            _estepDeadline = CMain.Time + 20000;
            if (_edgeInv == null)
            {
                FailEdge("no-inventory");
                return;
            }
            for (int i = 0; i < _edgeInv.Length; i++)
            {
                var it = _edgeInv[i];
                if (it == null || it.Count < 2) continue;
                SplitById(it.UniqueID, it.ItemIndex, it.Count);
                return;
            }
            _splitTryIdx = 0;
            TrySplitMake();
        }

        static void TrySplitMake()
        {
            _estep = EdgeStep.SplitMake;
            _estepDeadline = CMain.Time + 10000;
            while (_splitTryIdx < _edgeInv.Length)
            {
                var it = _edgeInv[_splitTryIdx];
                if (it == null) { _splitTryIdx++; continue; }
                _seq.Add($"TryMake:{it.ItemIndex}");
                Console.WriteLine($"[netprobe] edge split @make {it.ItemIndex} 2 (try={_splitTryIdx})");
                Network.Enqueue(new C.Chat { Message = $"@make {it.ItemIndex} 2" });
                return;
            }
            FailEdge("no-stackable-candidate");
        }

        static void SplitById(ulong uid, int itemIndex, ushort count)
        {
            _estep = EdgeStep.SplitWait;
            _estepDeadline = CMain.Time + 15000;
            _seq.Add($"SplitStack:{itemIndex}@{uid}:count={count}");
            Console.WriteLine($"[netprobe] edge split {itemIndex}@{uid} count={count}");
            Network.Enqueue(new C.SplitItem { Grid = MirGridType.Inventory, UniqueID = uid, Count = 1 });
        }

        // ---- magic：NewMagic 确认技能后施放 ----
        static void SendMagicCast(Spell spell)
        {
            _estep = EdgeStep.MagicCast;
            _estepDeadline = CMain.Time + 15000;
            _seq.Add($"SendMagic:{spell}");
            Console.WriteLine($"[netprobe] edge magic cast {spell} @{_userLoc.X},{_userLoc.Y}");
            Network.Enqueue(new C.Magic
            {
                Spell = spell,
                Direction = _userDir,
                TargetID = 0,
                Location = new System.Drawing.Point(_userLoc.X, _userLoc.Y),
                ObjectID = _userObjId,
                SpellTargetLock = false,
            });
        }

        // ---- autopath：真实地图寻路 + 沿路径逐节点行走（AutoRun 路径跟随）----
        static void BeginAutoPath()
        {
            _estep = EdgeStep.AutoWalk;
            _estepDeadline = CMain.Time + 30000;
            var mc = GameScene.Scene?.MapControl;
            if (mc == null) { FailEdge("no-mapcontrol"); return; }
            var pf = new PathFinder(mc);
            _autoPf = pf;
            _autoStuck = 0;
            _pathTarget = FindPathTarget(mc, pf);
            if (_pathTarget.IsEmpty) { FailEdge("no-path-target"); return; }
            _path = pf.FindPath(_userLoc, _pathTarget);
            if (_path == null || _path.Count < 4)
            {
                FailEdge("path-not-found");
                return;
            }
            _pathIdx = 0; // RetracePath 不含 start，path[0] 即第一个待走格
            _lastLoc = _userLoc;
            _seq.Add($"Path:{_path.Count}->{_pathTarget.X},{_pathTarget.Y}");
            Console.WriteLine($"[netprobe] edge autopath path {_path.Count} nodes target {_pathTarget.X},{_pathTarget.Y}");
            WalkToPathNode();
        }

        static MPoint FindPathTarget(MapControl mc, PathFinder pf)
        {
            for (int dist = 10; dist <= 18; dist++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        if (x == 0 && y == 0) continue;
                        var t = new MPoint(_userLoc.X + x * dist, _userLoc.Y + y * dist);
                        if (t.X < 0 || t.Y < 0 || t.X >= mc.Width || t.Y >= mc.Height) continue;
                        if (!mc.EmptyCell(t)) continue;
                        var p = pf.FindPath(_userLoc, t);
                        if (p != null && p.Count >= 8) return t;
                    }
                }
            }
            return MPoint.Empty;
        }

        static void WalkToPathNode()
        {
            if (_pathIdx >= _path.Count)
            {
                bool ok = _userLoc == _pathTarget;
                _seq.Add($"AutoPathArrive:{_userLoc.X},{_userLoc.Y} target={_pathTarget.X},{_pathTarget.Y} ok={ok}");
                Console.WriteLine($"[netprobe] edge autopath arrived ok={ok} @{_userLoc.X},{_userLoc.Y} target {_pathTarget.X},{_pathTarget.Y}");
                Done(ok, ok ? null : "autopath-missed-target");
                return;
            }
            var node = _path[_pathIdx].Location;
            if (node == _userLoc)
            {
                _pathIdx++;
                WalkToPathNode();
                return;
            }
            var dir = DirTo(Math.Sign(node.X - _userLoc.X), Math.Sign(node.Y - _userLoc.Y));
            _seq.Add($"PathWalk:{_pathIdx}->{node.X},{node.Y}");
            Network.Enqueue(new C.Walk { Direction = dir });
        }

        static void OnAutoPathUserLoc()
        {
            if (_estep != EdgeStep.AutoWalk) return;
            if (_pathIdx >= _path.Count) { WalkToPathNode(); return; }
            var node = _path[_pathIdx].Location;
            if (_userLoc == node)
            {
                _autoStuck = 0;
                _pathIdx++;
                WalkToPathNode();
            }
            else if (_userLoc == _lastLoc)
            {
                // 位置未推进：服务器侧有客户端 EmptyCell 未知的阻塞（对象/门）。连续 5 次则
                // 把该格在 M2CellInfo 打上阻塞位（与 EmptyCell 0x20000000 一致）并重寻路绕行。
                _autoStuck++;
                if (_autoStuck >= 5)
                {
                    _seq.Add($"Blocked:{node.X},{node.Y}");
                    RerouteAutoPath(node);
                    return;
                }
                WalkToPathNode();
            }
            _lastLoc = _userLoc;
        }

        static void RerouteAutoPath(MPoint blocked)
        {
            var mc = GameScene.Scene?.MapControl;
            if (mc == null || mc.M2CellInfo == null) { FailEdge("reroute-no-map"); return; }
            if (blocked.X >= 0 && blocked.Y >= 0 && blocked.X < mc.Width && blocked.Y < mc.Height)
                mc.M2CellInfo[blocked.X, blocked.Y].BackImage |= 0x20000000;
            _path = _autoPf.FindPath(_userLoc, _pathTarget);
            if (_path == null || _path.Count < 2)
            {
                FailEdge("reroute-no-path");
                return;
            }
            _autoStuck = 0;
            _pathIdx = 0;
            _lastLoc = _userLoc;
            _seq.Add($"Reroute:{_path.Count}->{_pathTarget.X},{_pathTarget.Y}");
            Console.WriteLine($"[netprobe] edge autopath reroute {_path.Count} nodes after block {blocked.X},{blocked.Y}");
            WalkToPathNode();
        }

        static void FailEdge(string reason)
        {
            _seq.Add("EdgeFail:" + reason);
            Done(false, reason);
        }

        static void MaybeDone()
        {
            if (_mode == Mode.Select && _gameEntered && _mapFile.Length > 0 && _userObjId > 0 && _dumpDeadline < 0)
            {
                _dumpDeadline = CMain.Time + 3000;
                _seq.Add("GameEntered");
            }
            else if (_mode == Mode.Game && _gameEntered && _mapLoaded && _userObjId > 0 && _gameDeadline < 0)
            {
                _gameDeadline = CMain.Time + GetLong("CRYSTAL_GAME_MS", 6000);
                _seq.Add("GameEntered");
            }
            else if (_mode == Mode.Hud && _gameEntered && _mapLoaded && _userObjId > 0 && _gameDeadline < 0)
            {
                _gameDeadline = CMain.Time + GetLong("CRYSTAL_GAME_MS", 6000);
                _seq.Add("GameEntered");
            }
            else if (_mode == Mode.Ui && _gameEntered && _mapLoaded && _userObjId > 0 && _gameDeadline < 0)
            {
                _gameDeadline = CMain.Time + GetLong("CRYSTAL_GAME_MS", 6000);
                _seq.Add("GameEntered");
            }
            else if (_mode == Mode.Bag && _gameEntered && _mapLoaded && _userObjId > 0 && _gameDeadline < 0)
            {
                _gameDeadline = CMain.Time + GetLong("CRYSTAL_GAME_MS", 6000);
                _seq.Add("GameEntered");
            }
            else if (_mode == Mode.UiInput && _gameEntered && _mapLoaded && _userObjId > 0 && _gameDeadline < 0)
            {
                _gameDeadline = CMain.Time + GetLong("CRYSTAL_GAME_MS", 6000);
                _seq.Add("GameEntered");
            }
            else if ((_mode == Mode.Npc || _mode == Mode.Skill || _mode == Mode.Quest || _mode == Mode.Team || _mode == Mode.Market || _mode == Mode.Hero || _mode == Mode.Shop || _mode == Mode.Settings) && _gameEntered && _mapLoaded && _userObjId > 0 && _gameDeadline < 0)
            {
                _gameDeadline = CMain.Time + GetLong("CRYSTAL_GAME_MS", 6000);
                _seq.Add("GameEntered");
            }
            else if (_mode == Mode.Edge && _estep == EdgeStep.Init && _userObjId > 0 && _mapFile.Length > 0)
            {
                // del 子模式不进图（_userObjId 恒 0），其流程由 LoginSuccess/NewCharacterSuccess 钩子自驱动。
                BeginEdge();
            }
            else if (_mode == Mode.Interact && _gameEntered && _mapFile.Length > 0 && _userObjId > 0 && _istep == InteractStep.Init)
            {
                StartInteract();
            }
            else if (_mode == Mode.Logout && _gameEntered && _mapFile.Length > 0 && _userObjId > 0 && _logoutPhase == LogoutPhase.Entering)
            {
                _logoutPhase = LogoutPhase.WaitLogOut;
                _logoutDeadline = CMain.Time + 15000;
                _seq.Add("GameEntered");
                Network.Enqueue(new C.LogOut());
            }
            else if (_mode == Mode.DualOpen && _gameEntered && _mapFile.Length > 0 && _userObjId > 0 && !_dualStarted)
            {
                _dualStarted = true;
                _soakDeadline = _soakMs > 0 ? CMain.Time + _soakMs : 0;
                _settleDeadline = -1;
                _seq.Add("GameEntered");
                _bThread = new Thread(BThread) { IsBackground = true };
                _bThread.Start();
            }
        }

        static void ProcessDualOpen()
        {
            if (!_dualStarted) return;
            if (_bErr != null)
            {
                _bStop = true;
                Done(false, _bErr);
                return;
            }

            if (_soakMs <= 0)
            {
                if (_bDone)
                {
                    if (_settleDeadline < 0) _settleDeadline = CMain.Time + 2000;
                    if (CMain.Time >= _settleDeadline)
                    {
                        _bStop = true;
                        bool ok = _aSeenPlayerB && _aSeenWalkB && _bSawPlayerA;
                        Done(ok, ok ? null : $"aSeenB={_aSeenPlayerB} aWalkB={_aSeenWalkB} bSawA={_bSawPlayerA}");
                    }
                }
            }
            else if (CMain.Time >= _soakDeadline)
            {
                _bStop = true;
                bool ok = _bWalked && _aSeenPlayerB && _aSeenWalkB && Network.Connected && _aPktCount > 0;
                Done(ok, ok ? null : $"soak bWalked={_bWalked} aSeenB={_aSeenPlayerB} aWalkB={_aSeenWalkB} conn={Network.Connected} pkts={_aPktCount}");
            }
        }

        // B 客户端（双开第二连接）：脚本化 raw socket，走与 A 相同登录流（账号/角色自适应），进图后环走 + 间歇聊天。
        static void BThread()
        {
            try
            {
                // 服务端反滥用：每次 accept 登记同 IP 5s 封禁（MirConnection 构造 UpdateIPBlock），
                // B 与 A 同 IP 并发需等窗口过期否则 accept 被拒（收不到 S.Connected）。
                Thread.Sleep(5500);
                BLog("connecting after ban window");
                _bClient = new TcpClient { NoDelay = true };
                _bClient.Connect(Settings.IPAddress, Settings.Port);
                _bStream = _bClient.GetStream();
                _bRaw = new byte[0];
                BLog("connect");

                BSend(new C.ClientVersion { VersionHash = Array.Empty<byte>() });
                if (!BDrainUntil(ServerPacketIds.ClientVersion, 5000)) { _bErr = "b-version"; BLog("FAIL b-version"); return; }
                BLog("version ok");

                BSend(new C.NewAccount { AccountID = _bId, Password = _bPw, BirthDate = DateTime.Now });
                BDrain(800);
                BSend(new C.Login { AccountID = _bId, Password = _bPw });
                if (!BDrainUntil(ServerPacketIds.LoginSuccess, 6000)) { _bErr = "b-login"; BLog("FAIL b-login"); return; }
                BLog($"login ok chars={_bChars} idx={_bCharIdx}");

                if (_bChars == 0)
                {
                    BSend(new C.NewCharacter { Name = _bChar, Gender = MirGender.Male, Class = MirClass.Warrior });
                    if (!BDrainUntil(ServerPacketIds.NewCharacterSuccess, 6000)) { _bErr = "b-create"; BLog("FAIL b-create"); return; }
                    BLog($"char created idx={_bCharIdx}");
                }

                BSend(new C.StartGame { CharacterIndex = _bCharIdx });
                if (!BDrainUntil(ServerPacketIds.UserInformation, 8000)) { _bErr = "b-startgame"; BLog("FAIL b-startgame"); return; }
                _bEntered = true;
                BLog($"entered obj={_bObjId} loc={_bLoc.X},{_bLoc.Y} sawA={_bSawPlayerA}");
                BDrain(1500); // 留时间给网格 Add 分发：B 应收 A 的 ObjectPlayer
                BLog($"post-enter sawA={_bSawPlayerA}");

                // 主动找 A：A（probe）从不移动、位置恒定为出生点 (288,616)（服务器持久）。
                // B 的离线位置随每次 run 漂移，可能落到 A 视野外 → 贪心走回出生点进入互见范围；
                // 目标方向被挡时沿 8 方向试探一个可走的方向（B 可能停在上次离线的死角）。
                var probeDirs = new[] { MirDirection.Up, MirDirection.UpRight, MirDirection.Right, MirDirection.DownRight, MirDirection.Down, MirDirection.DownLeft, MirDirection.Left, MirDirection.UpLeft };
                int guard = 0;
                while (!_bSawPlayerA && guard < 80 && !_bStop)
                {
                    int dx = 288 - _bLoc.X, dy = 616 - _bLoc.Y;
                    if (Math.Abs(dx) + Math.Abs(dy) <= 10) break;
                    var prev = _bLoc;
                    BSend(new C.Walk { Direction = DirTo(Math.Sign(dx), Math.Sign(dy)) });
                    BDrain(300);
                    if (_bLoc.X == prev.X && _bLoc.Y == prev.Y)
                    {
                        foreach (var ad in probeDirs)
                        {
                            var ap = _bLoc;
                            BSend(new C.Walk { Direction = ad });
                            BDrain(300);
                            if (_bLoc.X != ap.X || _bLoc.Y != ap.Y) break;
                        }
                    }
                    guard++;
                    if (guard % 10 == 0)
                        BLog($"seekA@{guard} loc={_bLoc.X},{_bLoc.Y} sawA={_bSawPlayerA} wc={_bWalkCnt}");
                }
                BLog($"seekA guard={guard} loc={_bLoc.X},{_bLoc.Y} sawA={_bSawPlayerA} wc={_bWalkCnt}");

                var dirs = new[] { MirDirection.Up, MirDirection.Right, MirDirection.Down, MirDirection.Left };
                int k = 0;
                while (!_bStop)
                {
                    if (k % 40 == 0)
                    {
                        BSend(new C.Chat { Message = "probe2b-soak" });
                        BDrain(400);
                    }
                    BSend(new C.Walk { Direction = dirs[k % 4] });
                    BDrain(600);
                    k++;
                    if (k >= 4)
                    {
                        _bWalked = true;
                        if (_soakMs <= 0) { _bDone = true; BLog("cycle done"); return; } // 快模式：一个环即完成
                    }
                }
                _bDone = true;
            }
            catch (Exception ex) { _bErr = "b-ex:" + ex.GetType().Name + ":" + ex.Message; BLog("FAIL exception " + ex); }
        }

        static void BLog(string msg)
        {
            Console.WriteLine("[netprobe] b: " + msg);
        }

        static void BSend(Packet p)
        {
            var bytes = p.GetPacketBytes().ToArray();
            _bStream.Write(bytes, 0, bytes.Length);
        }

        static void BDrain(int waitMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
            var buf = new byte[65536];
            while (DateTime.UtcNow < deadline)
            {
                while (_bStream.DataAvailable)
                {
                    int n = _bStream.Read(buf, 0, buf.Length);
                    if (n <= 0) return;
                    BAppend(buf, n);
                }
                Thread.Sleep(10);
            }
        }

        static bool BDrainUntil(ServerPacketIds id, int waitMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
            var buf = new byte[65536];
            while (DateTime.UtcNow < deadline)
            {
                while (_bStream.DataAvailable)
                {
                    int n = _bStream.Read(buf, 0, buf.Length);
                    if (n <= 0) return false;
                    BAppend(buf, n);
                }
                if (BHas(id)) return true;
                Thread.Sleep(10);
            }
            return BHas(id);
        }

        static void BAppend(byte[] data, int count)
        {
            var tmp = _bRaw;
            _bRaw = new byte[tmp.Length + count];
            Buffer.BlockCopy(tmp, 0, _bRaw, 0, tmp.Length);
            Buffer.BlockCopy(data, 0, _bRaw, tmp.Length, count);
            Packet p;
            while ((p = Packet.ReceivePacket(_bRaw, out _bRaw)) != null)
            {
                _bParsed.Add(p);
                switch (p.Index)
                {
                    case (short)ServerPacketIds.LoginSuccess:
                        var ls = (S.LoginSuccess)p;
                        _bChars = ls.Characters.Count;
                        _bCharIdx = _bChars > 0 ? ls.Characters[0].Index : -1;
                        break;
                    case (short)ServerPacketIds.NewCharacterSuccess:
                        var ncs = (S.NewCharacterSuccess)p;
                        _bCharIdx = ncs.CharInfo.Index;
                        break;
                    case (short)ServerPacketIds.UserInformation:
                        var bui = (S.UserInformation)p;
                        _bObjId = bui.ObjectID;
                        _bLoc = new MPoint(bui.Location.X, bui.Location.Y);
                        break;
                    case (short)ServerPacketIds.ObjectPlayer:
                        var bop = (S.ObjectPlayer)p;
                        if (bop.ObjectID != _bObjId) _bSawPlayerA = true;
                        break;
                    case (short)ServerPacketIds.ObjectWalk:
                        _bWalkCnt++;
                        var bow = (S.ObjectWalk)p;
                        if (bow.ObjectID == _bObjId) _bLoc = new MPoint(bow.Location.X, bow.Location.Y);
                        break;
                }
            }
        }

        static bool BHas(ServerPacketIds id)
        {
            for (int i = 0; i < _bParsed.Count; i++)
                if (_bParsed[i].Index == (short)id) return true;
            return false;
        }

        static void ProcessLogout()
        {
            if (_logoutPhase == LogoutPhase.WaitLogOut || _logoutPhase == LogoutPhase.ReEntering)
            {
                if (CMain.Time >= _logoutDeadline)
                    Done(false, _logoutPhase == LogoutPhase.WaitLogOut ? "logout-timeout" : "reenter-timeout");
            }
        }

        static void Done(bool ok, string fail)
        {
            _ok = ok;
            _fail = fail;
            _done = true;
        }

        static string GetEnv(string key, string def) => Environment.GetEnvironmentVariable(key) ?? def;
        static int GetInt(string key, int def) => int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;
        static long GetLong(string key, long def) => long.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;
    }
}
