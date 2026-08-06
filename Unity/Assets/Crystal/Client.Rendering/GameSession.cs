using System;
using System.Collections.Generic;
using System.IO;
using Client;
using Client.MirGraphics;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using UnityEngine;
using C = ClientPackets;
using S = ServerPackets;
using MPoint = Crystal.Client.Core.MirMath.Point;

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
        static int _charIndex;

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

        public static void SelectCharacter(int index)
        {
            if (State != GameSessionState.Select && State != GameSessionState.Creating) return;
            _charIndex = index;
            State = GameSessionState.Entering;
            Network.Enqueue(new C.StartGame { CharacterIndex = index });
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
                    SelectCharacter(ncs.CharInfo.Index);
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
                case (short)ServerPacketIds.Disconnect:
                    State = GameSessionState.Disconnected;
                    OnDisconnected?.Invoke();
                    break;
            }
        }

        static void ObjectMonster(S.ObjectMonster p)
        {
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
            GameScene.Scene = new GameScene { MapControl = mc };
            GameScene.CanMove = true;
            MapReader = reader;
            MapFileName = fileName;
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
            };
            MapObject.User = user;
            User = user;
        }

        static void Error(string msg)
        {
            State = GameSessionState.Error;
            Debug.LogError($"[gamesession] {msg}");
            OnError?.Invoke(msg);
        }
    }
}
