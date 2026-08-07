using System;
using System.Collections.Generic;
using System.IO;
using Client;
using Client.MirGraphics;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Client.MirScenes.Dialogs;
using UnityEngine;
using C = ClientPackets;
using S = ServerPackets;
using MPoint = Crystal.Client.Core.MirMath.Point;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Crystal.Client.Rendering.Editor")]

namespace Crystal.Client.Rendering
{
    public enum GameSessionState
    {
        Idle, Connecting, LoginWait, NewAccountWait, Select, Creating, Entering, InGame, Disconnected, Error
    }

    // 运行时网络游戏会话：从 Editor 探针 NetProbe 抽取核心协议（去断言/编排 mode 分支）。
    // 覆盖：登录状态机（ClientVersion→Login→自适应 NewAccount→LoginSuccess→NewCharacter→StartGame）、
    // 进图（MapInformation→LoadMap，UserInformation→EnsureUser）、对象分发
    // （ObjectMonster/Npc/Turn/Walk/Run/Remove/UserLocation）。UI 与主循环经事件/状态订阅。
    public static class GameSession
    {
        public static event Action OnSelectReady;      // 登录成功，角色列表就绪
        public static event Action OnEnterGame;        // 进图成功（S.StartGame Result=4）
        public static event Action<string> OnError;
        public static event Action OnDisconnected;

        public static GameSessionState State = GameSessionState.Idle;
        public static List<SelectInfo> Characters = new List<SelectInfo>();
        public static UserObject User;
        // 当前地图（渲染层读取：GameRenderer.DrawMapTiles 需要 MapReader + CellInfo[,]）。
        public static MapReader MapReader;
        public static string MapFileName;
        public static string MapDir;   // .map 目录（发布数据，如 Build/Server/publish/Maps）

        static string _account = string.Empty, _password = string.Empty;
        static bool _didNewAccount;

        public static void Connect(string ip, int port)
        {
            Settings.IPAddress = ip;
            Settings.Port = port;
            Network.OnPacket = ProcessPacket;
            Network.Connected = false;
            _didNewAccount = false;
            State = GameSessionState.Connecting;
            Network.Connect();
        }

        // 保存账号凭据；已握手（Network.Connected）则立即发 C.Login，否则等 S.ClientVersion(1) 流程。
        public static void Login(string account, string password)
        {
            _account = account ?? string.Empty;
            _password = password ?? string.Empty;
            if (State == GameSessionState.Connecting || State == GameSessionState.LoginWait)
                State = GameSessionState.LoginWait;
            if (Network.Connected && _account.Length > 0)
                Network.Enqueue(new C.Login { AccountID = _account, Password = _password });
        }

        // 按列表位置选号（OnSelectReady 回调用）：服务端按角色真实 Index 匹配，须经 Characters[index].Index 解析
        // （列表位置≠角色 Index，DB 主键从 1 起；直接传位置会 startgame-rejected:2）。
        public static void SelectCharacter(int index)
        {
            if (State != GameSessionState.Select && State != GameSessionState.Creating) return;
            if (index < 0 || index >= Characters.Count) return;
            StartGameByIndex(Characters[index].Index);
        }

        // 建号成功路径：新角色 Index 仅来自 S.NewCharacterSuccess.CharInfo（服务端不再重发 LoginSuccess）。
        static void StartGameByIndex(int characterIndex)
        {
            State = GameSessionState.Entering;
            Network.Enqueue(new C.StartGame { CharacterIndex = characterIndex });
        }

        public static void CreateCharacter(string name, MirGender gender, MirClass cls)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            State = GameSessionState.Creating;
            Network.Enqueue(new C.NewCharacter { Name = name, Gender = gender, Class = cls });
        }

        // 主循环每帧调用（Network.Process 驱动收发 + keepalive，真实时间）。
        public static void Process()
        {
            Network.Process();
        }

        static void ProcessPacket(Packet p)
        {
            switch (p.Index)
            {
                case (short)ServerPacketIds.Connected:
                    Network.Connected = true;
                    Network.Enqueue(new C.ClientVersion { VersionHash = Array.Empty<byte>() });
                    break;
                case (short)ServerPacketIds.ClientVersion:
                    var cv = (S.ClientVersion)p;
                    if (cv.Result == 1)
                    {
                        if (State == GameSessionState.LoginWait && _account.Length > 0)
                            Network.Enqueue(new C.Login { AccountID = _account, Password = _password });
                    }
                    else Error("version-rejected");
                    break;
                case (short)ServerPacketIds.NewAccount:
                    var na = (S.NewAccount)p;
                    // 8=成功 7=已存在 → 发登录
                    if (na.Result == 8 || na.Result == 7)
                        Network.Enqueue(new C.Login { AccountID = _account, Password = _password });
                    else Error($"account-create-rejected:{na.Result}");
                    break;
                case (short)ServerPacketIds.Login:
                    var lg = (S.Login)p;
                    // Result=3 账号不存在 → 自适应建号（每连接仅一次，防 Envir AccountsMade 计数 24h IP ban）
                    if (lg.Result == 3 && !_didNewAccount)
                    {
                        _didNewAccount = true;
                        State = GameSessionState.NewAccountWait;
                        Network.Enqueue(new C.NewAccount
                        {
                            AccountID = _account,
                            Password = _password,
                            BirthDate = DateTime.Now
                        });
                    }
                    else if (lg.Result != 0)
                        Error($"login-rejected:{lg.Result}");
                    break;
                case (short)ServerPacketIds.LoginSuccess:
                    var ls = (S.LoginSuccess)p;
                    Characters = ls.Characters;
                    State = GameSessionState.Select;
                    OnSelectReady?.Invoke();
                    break;
                case (short)ServerPacketIds.NewCharacter:
                    var nc = (S.NewCharacter)p;
                    if (nc.Result != 0) Error($"character-create-rejected:{nc.Result}");
                    break;
                case (short)ServerPacketIds.NewCharacterSuccess:
                    var ncs = (S.NewCharacterSuccess)p;
                    StartGameByIndex(ncs.CharInfo.Index);
                    break;
                case (short)ServerPacketIds.StartGame:
                    var sg = (S.StartGame)p;
                    if (sg.Result == 4)
                    {
                        State = GameSessionState.InGame;
                        OnEnterGame?.Invoke();
                    }
                    else Error($"startgame-rejected:{sg.Result}");
                    break;
                case (short)ServerPacketIds.MapInformation:
                    var mi = (S.MapInformation)p;
                    if (State == GameSessionState.InGame && mi.FileName.Length > 0)
                        LoadMap(mi.FileName);
                    break;
                case (short)ServerPacketIds.UserInformation:
                    var ui = (S.UserInformation)p;
                    if (MapObject.User == null)
                        EnsureUser(ui);
                    break;
                case (short)ServerPacketIds.ObjectMonster:
                    ObjectMonster((S.ObjectMonster)p);
                    break;
                case (short)ServerPacketIds.ObjectNpc:
                    ObjectNpc((S.ObjectNPC)p);
                    break;
                case (short)ServerPacketIds.ObjectTurn:
                    ObjectMove((S.ObjectTurn)p, MirAction.Standing);
                    break;
                case (short)ServerPacketIds.ObjectWalk:
                    ObjectMove((S.ObjectWalk)p, MirAction.Walking);
                    break;
                case (short)ServerPacketIds.ObjectRun:
                    ObjectMove((S.ObjectRun)p, MirAction.Running);
                    break;
                case (short)ServerPacketIds.ObjectRemove:
                    ObjectRemove((S.ObjectRemove)p);
                    break;
                case (short)ServerPacketIds.UserLocation:
                    UserLocation((S.UserLocation)p);
                    break;
                case (short)ServerPacketIds.HealthChanged:
                    var hc = (S.HealthChanged)p;
                    if (MapObject.User != null)
                    {
                        MapObject.User.HP = hc.HP;
                        MapObject.User.MP = hc.MP;
                    }
                    break;
                case (short)ServerPacketIds.ObjectHealth:
                    var oh = (S.ObjectHealth)p;
                    if (MapControl.Objects.TryGetValue(oh.ObjectID, out var objH))
                    {
                        objH.PercentHealth = oh.Percent;
                        objH.HealthTime = CMain.Time + oh.Expire * 1000;
                    }
                    break;
                case (short)ServerPacketIds.ObjectStruck:
                    ObjectStruck((S.ObjectStruck)p);
                    break;
                case (short)ServerPacketIds.ObjectDied:
                    ObjectDied((S.ObjectDied)p);
                    break;
                case (short)ServerPacketIds.EquipItem:
                    EquipItem((S.EquipItem)p);
                    break;
                case (short)ServerPacketIds.RemoveItem:
                    RemoveItem((S.RemoveItem)p);
                    break;
                case (short)ServerPacketIds.Disconnect:
                    State = GameSessionState.Disconnected;
                    OnDisconnected?.Invoke();
                    break;
            }
        }

        static void ObjectMonster(S.ObjectMonster p)
        {
            ushort img = (ushort)p.Image;
            EnsureObjectLib(img, Libraries.Monsters, $"Monster/{img:D3}");
            // 无图集（区域子集裁剪未含该怪物段，Android 常见）：跳过渲染，不抛 NRE 刷屏
            if (img >= Libraries.Monsters.Length || Libraries.Monsters[img] == null) return;
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
            EnsureObjectLib(p.Image, Libraries.NPCs, $"NPC/{p.Image:D2}");
            if (p.Image >= Libraries.NPCs.Length || Libraries.NPCs[p.Image] == null) return;
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
            var m = GameRenderer.EnsureMLibrary(key);
            if (m != null) slot[img] = m;
        }

        static void ObjectMove(object p, MirAction action)
        {
            uint id = 0; int x = 0, y = 0; MirDirection dir = 0;
            if (p is S.ObjectTurn t) { id = t.ObjectID; x = t.Location.X; y = t.Location.Y; dir = t.Direction; }
            else if (p is S.ObjectWalk w) { id = w.ObjectID; x = w.Location.X; y = w.Location.Y; dir = w.Direction; }
            else if (p is S.ObjectRun r) { id = r.ObjectID; x = r.Location.X; y = r.Location.Y; dir = r.Direction; }

            if (MapControl.Objects.TryGetValue(id, out var ob))
                ob.ActionFeed.Add(new QueuedAction { Action = action, Direction = dir, Location = new MPoint(x, y) });
        }

        static void ObjectRemove(S.ObjectRemove p)
        {
            if (MapControl.Objects.TryGetValue(p.ObjectID, out var ob))
                ob.Remove();
        }

        // 受击动画（对齐 old client GameScene.ObjectStruck：被击动作入队，自身跳过；无特效/Buff 简化）。
        static void ObjectStruck(S.ObjectStruck p)
        {
            if (MapObject.User != null && p.ObjectID == MapObject.User.ObjectID) return;
            if (!MapControl.Objects.TryGetValue(p.ObjectID, out var ob)) return;
            if (ob.SkipFrames) return;
            if (ob.ActionFeed.Count > 0 && ob.ActionFeed[ob.ActionFeed.Count - 1].Action == MirAction.Struck) return;
            ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Struck, Direction = p.Direction, Location = new MPoint(p.Location.X, p.Location.Y) });
        }

        // 死亡（对齐 old client GameScene.ObjectDied：Type=0 死亡动作+Dead 留尸，1/2 直接移除——无 Magic2 特效库不播）。
        static void ObjectDied(S.ObjectDied p)
        {
            if (MapObject.User != null && p.ObjectID == MapObject.User.ObjectID) return;
            if (!MapControl.Objects.TryGetValue(p.ObjectID, out var ob)) return;
            if (p.Type == 0)
            {
                ob.ActionFeed.Add(new QueuedAction { Action = MirAction.Die, Direction = p.Direction, Location = new MPoint(p.Location.X, p.Location.Y) });
                ob.Dead = true;
            }
            else
            {
                ob.Remove();
            }
        }

        // 装备穿戴回流（8-2-3）：S.EquipItem 成功确认→解锁来源背包格（交换前按 UniqueID 定位）
        // + 目标装备格（按槽位），随后 ApplyEquip 镜像交换数组（RefreshStats 重算外观/属性）。
        // internal：equipverify 探针（Crystal.Client.Rendering.Editor）直接调用测全链。
        internal static void EquipItem(S.EquipItem p)
        {
            if (MapObject.User == null) return;
            var scene = GameScene.Scene;
            if (scene == null) return;

            var fromCell = scene.InventoryDialog != null ? scene.InventoryDialog.GetCell(p.UniqueID) : null;
            if (p.To >= 0 && p.To < MapObject.User.Equipment.Length && scene.CharacterDialog != null)
                scene.CharacterDialog.Grid[p.To].Locked = false;
            if (fromCell != null) fromCell.Locked = false;

            if (!p.Success) return;
            MapObject.User.ApplyEquip(p);
        }

        // 卸下回流（8-2-3）：S.RemoveItem 成功确认→解锁来源装备格，随后 ApplyRemove 迁回背包。
        // internal：equipverify 探针直接调用测全链。
        internal static void RemoveItem(S.RemoveItem p)
        {
            if (MapObject.User == null) return;
            var scene = GameScene.Scene;
            if (scene == null) return;

            var fromCell = scene.CharacterDialog != null ? scene.CharacterDialog.GetCell(p.UniqueID) : null;
            if (fromCell != null) fromCell.Locked = false;

            if (!p.Success) return;
            MapObject.User.ApplyRemove(p);
        }

        static void UserLocation(S.UserLocation p)
        {
            if (MapObject.User == null) return;
            MapObject.User.Movement = new MPoint(p.Location.X, p.Location.Y);
            MapObject.User.CurrentLocation = new MPoint(p.Location.X, p.Location.Y);
            MapObject.User.Direction = p.Direction;
        }

        static void LoadMap(string fileName)
        {
            string mapPath = Path.Combine(MapDir, fileName);
            if (!File.Exists(mapPath)) mapPath = Path.Combine(MapDir, fileName + ".Map");
            if (!File.Exists(mapPath))
            {
                Error($"map missing {mapPath}");
                return;
            }

            var reader = new MapReader(mapPath);
            var mc = new MapControl
            {
                M2CellInfo = reader.MapCells,
                Width = reader.Width,
                Height = reader.Height,
            };
            mc.PathFinder = new PathFinder(mc); // A* 依赖 mc.EmptyCell（Node.Walkable），须在 M2CellInfo 赋值后构造
            GameScene.Scene = new GameScene { MapControl = mc };
            GameScene.CanMove = true;
            MapReader = reader;
            MapFileName = fileName;
            InitInGameDialogs();
        }

        // 进图实例化 HUD 状态条 + 背包对话框（阶段8 第2项 增量1）：挂 GameScene.Scene。
        // 顺序契约：Settings 屏幕尺寸先同步（MainDialog.Location 依赖）；Libraries.* 换
        // atlas-backed MLibraryUnity（对话框 ctor 的 AutoSize 尺寸/锚点来自真实帧）；缺库
        // （Android 区域裁剪不含 UI 段时）负缓存 null → 对话框 ctor 安全（控件 Library null
        // 不绘制、Size 回退默认），try/catch 防异常传播卡死包处理主循环。
        static void InitInGameDialogs()
        {
            var scene = GameScene.Scene;
            if (scene == null) return;
            Settings.ScreenWidth = GameRuntime.ScreenW;
            Settings.ScreenHeight = GameRuntime.ScreenH;
            if (Libraries.Prguse == null) Libraries.Prguse = GameRenderer.EnsureMLibrary("Prguse");
            if (Libraries.Prguse2 == null) Libraries.Prguse2 = GameRenderer.EnsureMLibrary("Prguse2");
            if (Libraries.Items == null) Libraries.Items = GameRenderer.EnsureMLibrary("Items");
            if (Libraries.Title == null) Libraries.Title = GameRenderer.EnsureMLibrary("Title");
            if (Libraries.UI_32bit == null) Libraries.UI_32bit = GameRenderer.EnsureMLibrary("UI");
            try
            {
                var main = new MainDialog { Parent = scene };
                scene.MainDialog = main;
                var inv = new InventoryDialog { Parent = scene };
                scene.InventoryDialog = inv;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[gamesession] dialogs {ex.GetType().Name}: {ex.Message}");
            }
        }

        static void EnsureUser(S.UserInformation ui)
        {
            var user = new UserObject(ui.ObjectID)
            {
                Movement = new MPoint(ui.Location.X, ui.Location.Y),
                CurrentLocation = new MPoint(ui.Location.X, ui.Location.Y),
                OffSetMove = MPoint.Empty,
                Direction = ui.Direction,
                Name = ui.Name,
                HP = ui.HP,
                MP = ui.MP,
                Class = ui.Class,
                Gender = ui.Gender,
                Level = ui.Level,
            };
            MapObject.User = user;
            User = user;
            if (GameScene.Scene != null) GameScene.User = user; // InventoryDialog.Process/RefreshInventory 数据源
            // 装备窗口（8-2-3）：CharacterDialog ctor 注入 Actor（私有仅 ctor 可设）→ 须在 User 到齐后
            // 创建（InitInGameDialogs 时 MapObject.User 尚空）。默认隐藏（MirControl.Visible 默认 true），
            // MobileBootstrap 装备按钮 ShowCharacterPage 打开；CreateItemLabel 依赖其存在。
            // Parent=Scene 必须显式挂（旧客户端 GameScene 同款）：Mir 鼠标链 hit 走 Scene.Controls
            // 子树递归，不挂父则装备格收不到双击/悬停（卸下无法触发），绘制也脱离场景树。
            if (GameScene.Scene != null && GameScene.Scene.CharacterDialog == null)
            {
                try
                {
                    GameScene.Scene.CharacterDialog = new CharacterDialog(MirGridType.Equipment, user)
                    {
                        Parent = GameScene.Scene,
                        Visible = false,
                    };
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[gamesession] char-dialog {ex.GetType().Name}: {ex.Message}");
                }
            }
            // 补旧客户端 Load() 的 RefreshStats：UserInformation 仅含当前 HP/MP，最大血量须从
            // 等级/装备/技能计算（Stats[Stat.HP]），供 HUD 血条分母；进图时 Scene 已由 MapInformation 建立。
            // try/catch 防 RefreshStats 内部（SetLibraries 图集等）异常传播卡死包处理主循环。
            try
            {
                if (GameScene.Scene != null) user.RefreshStats();
                else Debug.LogWarning("[gamesession] user-stats skipped (scene null)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[gamesession] user-stats {ex.GetType().Name}: {ex.Message}");
            }
        }

        static void Error(string msg)
        {
            State = GameSessionState.Error;
            Debug.LogError($"[gamesession] {msg}");
            OnError?.Invoke(msg);
        }
    }
}
