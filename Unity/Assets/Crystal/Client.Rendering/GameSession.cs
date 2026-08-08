using System;
using System.Collections.Generic;
using System.IO;
using Client;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Client.MirScenes.Dialogs;
using Client.MirSounds;
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
    // （ObjectMonster/Npc/Spell/Item/Gold/Turn/Walk/Run/Remove/UserLocation）。UI 与主循环经事件/状态订阅。
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
                case (short)ServerPacketIds.ObjectSpell:
                    ObjectSpell((S.ObjectSpell)p);
                    break;
                case (short)ServerPacketIds.ObjectItem:
                    ObjectItem((S.ObjectItem)p);
                    break;
                case (short)ServerPacketIds.ObjectGold:
                    ObjectGold((S.ObjectGold)p);
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
                case (short)ServerPacketIds.NPCResponse:
                    NpcResponse((S.NPCResponse)p);
                    break;
                case (short)ServerPacketIds.EquipItem:
                    EquipItem((S.EquipItem)p);
                    break;
                case (short)ServerPacketIds.RemoveItem:
                    RemoveItem((S.RemoveItem)p);
                    break;
                case (short)ServerPacketIds.UseItem:
                    UseItem((S.UseItem)p);
                    break;
                case (short)ServerPacketIds.NewItemInfo:
                    // 商店商品 Info 数据源（8-3-2）：S.NewItemInfo 单条物品信息入库（登录/进图服务端批量推送）。
                    var nii = (S.NewItemInfo)p;
                    if (nii.Info != null) GameScene.ItemInfoList.Add(nii.Info);
                    break;
                case (short)ServerPacketIds.NPCGoods:
                    NpcGoods((S.NPCGoods)p);
                    break;
                case (short)ServerPacketIds.StoreItem:
                    StoreItem((S.StoreItem)p);
                    break;
                case (short)ServerPacketIds.TakeBackItem:
                    TakeBackItem((S.TakeBackItem)p);
                    break;
                case (short)ServerPacketIds.UserStorage:
                    UserStorage((S.UserStorage)p);
                    break;
                case (short)ServerPacketIds.NPCStorage:
                    NpcStorage();
                    break;
                case (short)ServerPacketIds.NewQuestInfo:
                    NewQuestInfo((S.NewQuestInfo)p);
                    break;
                case (short)ServerPacketIds.ChangeQuest:
                    ChangeQuest((S.ChangeQuest)p);
                    break;
                case (short)ServerPacketIds.CompleteQuest:
                    CompleteQuest((S.CompleteQuest)p);
                    break;
                case (short)ServerPacketIds.ShareQuest:
                    ShareQuest((S.ShareQuest)p);
                    break;
                case (short)ServerPacketIds.NewMapInfo:
                    NewMapInfo((S.NewMapInfo)p);
                    break;
                case (short)ServerPacketIds.WorldMapSetup:
                    WorldMapSetup((S.WorldMapSetupInfo)p);
                    break;
                case (short)ServerPacketIds.SwitchGroup:
                    GroupSwitch((S.SwitchGroup)p);
                    break;
                case (short)ServerPacketIds.DeleteGroup:
                    GroupDelete();
                    break;
                case (short)ServerPacketIds.DeleteMember:
                    GroupDeleteMember((S.DeleteMember)p);
                    break;
                case (short)ServerPacketIds.GroupInvite:
                    GroupInvite((S.GroupInvite)p);
                    break;
                case (short)ServerPacketIds.AddMember:
                    GroupAddMember((S.AddMember)p);
                    break;
                case (short)ServerPacketIds.GroupMembersMap:
                    GroupMembersMap((S.GroupMembersMap)p);
                    break;
                case (short)ServerPacketIds.SendMemberLocation:
                    GroupMemberLocation((S.SendMemberLocation)p);
                    break;
                case (short)ServerPacketIds.FriendUpdate:
                    FriendUpdate((S.FriendUpdate)p);
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

        // NPC 对话回流（8-3-1）：S.NPCResponse 对话页 → NPCDialog.NewText 渲染选项 + Show。
        // NPCDialog 由 InitInGameDialogs 常驻创建（Visible=false，防 MapObject.cs NPC 移除 NRE +
        // UiHitTest 污染）；此处兜底懒建。internal：NpcVerify 探针直调测全链。
        internal static void NpcResponse(S.NPCResponse p)
        {
            var scene = GameScene.Scene;
            if (scene == null) return;
            if (scene.NPCDialog == null)
                scene.NPCDialog = new NPCDialog { Parent = scene, Visible = false };
            scene.NPCDialog.NewText(p.Page);
            if (scene.InventoryDialog != null) scene.NPCDialog.Show();
            else scene.NPCDialog.Visible = true;

            // 任务列表（8-4-1）：当前 NPC 有可接任务 → 连带打开任务列表（对齐旧客户端点 NPC 弹列表）。
            // 无任务不弹：QuestListDialog.Show→DisplayInfo→GetAvailableQuests 失败会 Hide()（连带关
            // NPC 对话），此处先查 NPCObject.Quests 门控，避免无任务 NPC 把对话关掉。
            if (scene.QuestListDialog == null)
                scene.QuestListDialog = new QuestListDialog { Parent = scene, Visible = false };
            if (MapControl.GetObject(GameScene.NPCID) is NPCObject npc && npc.Quests != null && npc.Quests.Count > 0)
                scene.QuestListDialog.Show();
        }

        // 商店商品回流（8-3-2）：S.NPCGoods 商品列表 + 价格倍率 → NPCGoodsDialog.NewGoods 渲染 + Show
        // （Show 连带打开背包，对齐旧客户端语义）。UserItem 反序列化后 Info 为空，逐条按 ItemIndex
        // 从物品信息表（GameScene.ItemInfoList，S.NewItemInfo 填充）解析；未收录（表缺失该 Index）
        // 跳过不渲染——AddGoods 会对 null Info 商品解引用 NRE，跳过与 MirGoodsCell null-Info 早退同源。
        // NPCDialog/InventoryDialog 由 InitInGameDialogs 常驻，此处兜底懒建（探针直调）。internal：
        // ShopVerify 探针直调测全链。
        internal static void NpcGoods(S.NPCGoods p)
        {
            var scene = GameScene.Scene;
            if (scene == null) return;
            if (scene.NPCDialog == null)
                scene.NPCDialog = new NPCDialog { Parent = scene, Visible = false };
            if (scene.NPCGoodsDialog == null)
                scene.NPCGoodsDialog = new NPCGoodsDialog(p.Type) { Parent = scene, Visible = false };

            var goodsList = new List<UserItem>();
            foreach (var g in p.List)
            {
                var info = GetItemInfo(g.ItemIndex);
                if (info == null) continue;
                g.Info = info;
                goodsList.Add(g);
            }

            GameScene.NPCRate = p.Rate;
            scene.NPCGoodsDialog.NewGoods(goodsList);
            scene.NPCGoodsDialog.Show();
        }

        // 物品信息表按 Index 查找（旧客户端 GameScene.GetItemInfo 同源）。
        static ItemInfo GetItemInfo(int index)
        {
            foreach (var info in GameScene.ItemInfoList)
                if (info.Index == index) return info;
            return null;
        }

        // ===== 仓库（8-3-3）=====
        // S.UserStorage：仓库物品全量 → GameScene.Storage（本地镜像快照，StorageDialog 网格绑定源）+
        // 开仓库框。服务端 SendStorage 可能因密码未解锁早退（仅发 S.NPCStorage），届时仅开框不填数。
        internal static void UserStorage(S.UserStorage p)
        {
            var scene = GameScene.Scene;
            if (scene == null || p.Storage == null) return;
            for (int i = 0; i < GameScene.Storage.Length && i < p.Storage.Length; i++)
                GameScene.Storage[i] = p.Storage[i];
            if (scene.StorageDialog == null)
                scene.StorageDialog = new StorageDialog { Parent = scene, Visible = false };
            scene.StorageDialog.Show();
        }

        // S.NPCStorage：仓库 NPC 应答（关对话 + 开仓库框）。@STORAGE 时服务端先发 S.UserStorage 再发本包，
        // 密码未解锁时仅本包 → 仓库框仍开（页面1 基础格，LockedPage 在页面2 展示，密码流程未移植）。
        internal static void NpcStorage()
        {
            var scene = GameScene.Scene;
            if (scene == null) return;
            if (scene.NPCDialog != null) scene.NPCDialog.Hide();
            if (scene.StorageDialog == null)
                scene.StorageDialog = new StorageDialog { Parent = scene, Visible = false };
            scene.StorageDialog.Show();
        }

        // S.StoreItem 回声：存（From=背包格, To=仓库格）。Success → 本地交换（服务器权威）+ 解锁双格。
        internal static void StoreItem(S.StoreItem p)
        {
            ApplyStorageSwap(p.From, p.To, p.Success, isDeposit: true);
        }

        // S.TakeBackItem 回声：取（From=仓库格, To=背包格）。Success → 本地交换 + 解锁双格。
        internal static void TakeBackItem(S.TakeBackItem p)
        {
            ApplyStorageSwap(p.From, p.To, p.Success, isDeposit: false);
        }

        // 仓库交换本地应用（回声成功后对齐服务端真实格子）+ 解锁（成功失败都解，防死锁）。
        static void ApplyStorageSwap(int from, int to, bool success, bool isDeposit)
        {
            var scene = GameScene.Scene;
            var user = MapObject.User;
            if (scene == null || user == null) return;

            var inv = user.Inventory;
            var storage = GameScene.Storage;
            bool okFrom = from >= 0 && from < inv.Length;
            bool okTo = to >= 0 && to < storage.Length;
            if (success && okFrom && okTo)
            {
                if (isDeposit) { storage[to] = inv[from]; inv[from] = null; }
                else { inv[to] = storage[from]; storage[from] = null; }
                try { user.RefreshStats(); }
                catch (Exception ex) { Debug.LogError($"[gamesession] storage-swap stats {ex.GetType().Name}: {ex.Message}"); }
            }

            // 存储槽位按方向取：存=To(仓库格)、取=From(仓库格)；背包槽位反之。Grid[slot] 下标即槽位。
            int storeSlot = isDeposit ? to : from;
            bool okStore = isDeposit ? okTo : okFrom;
            if (scene.StorageDialog != null && okStore)
                scene.StorageDialog.Grid[storeSlot].Locked = false;
            if (scene.InventoryDialog?.Grid != null)
            {
                // Grid 下标≠物品槽位（Grid[0].ItemSlot=6）：按 ItemSlot 扫描定位真实被锁格。
                int slot = isDeposit ? from : to;
                for (int i = 0; i < scene.InventoryDialog.Grid.Length; i++)
                {
                    if (scene.InventoryDialog.Grid[i].ItemSlot != slot) continue;
                    scene.InventoryDialog.Grid[i].Locked = false;
                    break;
                }
            }
        }

        // ===== 任务（8-4-1）=====
        // S.NewQuestInfo：任务模板全量 → QuestInfoList（NPCObject.Load 按 NPCIndex==ObjectID 关联可接任务）。
        internal static void NewQuestInfo(S.NewQuestInfo p)
        {
            if (p?.Info == null) return;
            GameScene.QuestInfoList.Add(p.Info);
        }

        // S.ChangeQuest：任务进度增/改/删。CurrentQuests 双引用同步——QuestDiaryDialog.DisplayQuests 读
        // GameScene.User、QuestTrackingDialog.DisplayQuests 读 MapObject.User，须两处都维护。TrackQuest
        // → 追踪栏（TrackedQuestsIds 单一事实源，AddQuest 内自驱 Show + 写 Settings）。打开中的任务窗就地刷新。
        internal static void ChangeQuest(S.ChangeQuest p)
        {
            var q = p.Quest;
            if (q == null) return;
            var users = new List<UserObject>();
            if (GameScene.User != null) users.Add(GameScene.User);
            if (MapObject.User != null && !users.Contains(MapObject.User)) users.Add(MapObject.User);
            foreach (var user in users)
            {
                int idx = user.CurrentQuests.FindIndex(x => x.Id == q.Id);
                switch (p.QuestState)
                {
                    case QuestState.Add:
                        if (idx < 0) user.CurrentQuests.Add(q);
                        break;
                    case QuestState.Update:
                        if (idx >= 0) user.CurrentQuests[idx] = q;
                        else user.CurrentQuests.Add(q);
                        break;
                    case QuestState.Remove:
                        if (idx >= 0) user.CurrentQuests.RemoveAt(idx);
                        break;
                }
            }

            var scene = GameScene.Scene;
            if (scene == null) return;

            var tracking = scene.QuestTrackingDialog;
            if (p.QuestState == QuestState.Remove && tracking != null && tracking.TrackedQuestsIds.Contains(q.Id))
                tracking.RemoveQuest(q); // 废弃任务同步摘追踪（对齐旧客户端 ChangeQuest），否则追踪 ID 残留 Settings
            if (p.TrackQuest && tracking != null && !tracking.TrackedQuestsIds.Contains(q.Id))
                tracking.AddQuest(q);
            if (tracking != null && MapObject.User != null && tracking.TrackedQuestsIds.Count > 0)
                tracking.DisplayQuests();
            if (scene.QuestDiaryDialog != null && scene.QuestDiaryDialog.Visible)
                scene.QuestDiaryDialog.DisplayQuests();
        }

        // S.CompleteQuest：已完成任务 Id 列表 → 双引用移除 + 打开中的日记刷新。
        internal static void CompleteQuest(S.CompleteQuest p)
        {
            if (p.CompletedQuests == null) return;
            var users = new List<UserObject>();
            if (GameScene.User != null) users.Add(GameScene.User);
            if (MapObject.User != null && !users.Contains(MapObject.User)) users.Add(MapObject.User);
            foreach (var user in users)
                user.CurrentQuests.RemoveAll(x => p.CompletedQuests.Contains(x.Id));

            var scene = GameScene.Scene;
            if (scene != null && scene.QuestDiaryDialog != null && scene.QuestDiaryDialog.Visible)
                scene.QuestDiaryDialog.DisplayQuests();
        }

        // S.ShareQuest：组队分享任务提示（旧客户端经 ChatDialog 广播，未移植）→ 空体保留契约。
        internal static void ShareQuest(S.ShareQuest p) { }

        // ===== 大地图（8-4-2）=====
        // S.NewMapInfo：单张地图大地图记录（旧客户端 NewMapInfo+CreateBigMapButtons 移植）。构建
        // BigMapRecord + 移动按钮（Parent=ViewPort，Click→SetTargetMap(目的地)）+ NPC 行（Parent=
        // BigMapDialog）入 MapInfoList。旧客户端 Add 重复会抛（KeyAlreadyExists），改索引赋值幂等重建。
        // internal：MapVerify 探针直调测全链。
        internal static void NewMapInfo(S.NewMapInfo info)
        {
            var scene = GameScene.Scene;
            if (scene == null || scene.BigMapDialog == null) return;
            var record = new BigMapRecord { Index = info.MapIndex, MapInfo = info.Info };
            CreateBigMapButtons(record);
            GameScene.MapInfoList[info.MapIndex] = record;
        }

        // 移动按钮/NPC 行创建（旧客户端 GameScene.CreateBigMapButtons 逐字移植）。移动按钮 MouseEnter
        // 更新坐标标签（ClientNPCInfo/ClientMovementInfo.Location 为 Shared System.Drawing.Point，
        // 转换到 MirMath.Point）；Click→SetTargetMap（视口 OnBeforeDraw 按 ScaleX/Y 重定位按钮）。
        static void CreateBigMapButtons(BigMapRecord record)
        {
            var dlg = GameScene.Scene.BigMapDialog;
            record.MovementButtons.Clear();
            record.NPCButtons.Clear();
            foreach (var mInfo in record.MapInfo.Movements)
            {
                var button = new MirButton
                {
                    Library = Libraries.MapLinkIcon,
                    Index = mInfo.Icon,
                    PressedIndex = mInfo.Icon,
                    Sound = SoundList.ButtonA,
                    Parent = dlg.ViewPort,
                    Location = new MPoint(20, 38),
                    Hint = mInfo.Title,
                    Visible = false,
                };
                button.MouseEnter += (o, e) => dlg.MouseLocation = new MPoint(mInfo.Location.X, mInfo.Location.Y);
                button.Click += (o, e) => dlg.SetTargetMap(mInfo.Destination);
                record.MovementButtons.Add(mInfo, button);
            }
            foreach (var npcInfo in record.MapInfo.NPCs)
                record.NPCButtons.Add(new BigMapNPCRow(npcInfo) { Parent = dlg });
        }

        // S.WorldMapSetup：世界地图开关 + NPC 传送花费（旧客户端 WorldMapSetup 移植）。
        internal static void WorldMapSetup(S.WorldMapSetupInfo info)
        {
            var scene = GameScene.Scene;
            if (scene == null || scene.BigMapDialog == null) return;
            scene.BigMapDialog.WorldMapSetup(info.Setup);
            GameScene.TeleportToNPCCost = (uint)info.TeleportToNPCCost;
        }

        // ===== 组队（8-6-1）=====
        // 旧客户端 GameScene 组队包处理逐字移植（SwitchGroup/DeleteGroup/DeleteMember/GroupInvite/
        // AddMember/GroupMembersMap/SendMemberLocation）。分发刷新 GroupDialog 静态数据
        // （AllowGroup/GroupList/GroupMembersMap）+ 大地图成员雷达点（BigMapViewPort.PlayerLocations，
        // 8-4-2 未接雷达渲染，仅维护字典供后续）。系统提示走 ChatDialog.ReceiveChat（常驻，缺文本回退键名）。
        // S.SwitchGroup：允许组队开关回声。关组队且已在队伍 → 本地同步清（服务器 LeaveGroup 已推 DeleteGroup）。
        internal static void GroupSwitch(S.SwitchGroup p)
        {
            var scene = GameScene.Scene;
            if (scene == null || scene.GroupDialog == null) return;
            GroupDialog.AllowGroup = p.AllowGroup;
            if (!p.AllowGroup && GroupDialog.GroupList.Count > 0)
                GroupDelete();
        }

        internal static void GroupDelete()
        {
            GroupDialog.GroupList.Clear();
            GroupDialog.GroupMembersMap.Clear();
            BigMapViewPort.PlayerLocations.Clear();
            var chat = GameScene.Scene?.ChatDialog;
            if (chat != null)
                chat.ReceiveChat(GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.YouHaveLeftGroup), ChatType.Group);
        }

        internal static void GroupDeleteMember(S.DeleteMember p)
        {
            GroupDialog.GroupList.Remove(p.Name);
            GroupDialog.GroupMembersMap.Remove(p.Name);
            BigMapViewPort.PlayerLocations.Remove(p.Name);
            var chat = GameScene.Scene?.ChatDialog;
            if (chat != null)
                chat.ReceiveChat(GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.PlayerHasLeftGroup, p.Name), ChatType.Group);
        }

        // S.GroupInvite：被邀弹 MirMessageBox YesNo（旧客户端 GroupInvite 逐字移植）。接受 →
        // C.GroupInvite{true} + 开组队窗（GroupDialog.Show 带 Visible 守卫）；拒绝 → C.GroupInvite{false}。
        internal static void GroupInvite(S.GroupInvite p)
        {
            var box = new MirMessageBox(
                GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.DoYouWantGroupWithPlayer, p.Name),
                MirMessageBoxButtons.YesNo);
            box.YesButton.Click += (o, e) =>
            {
                Network.Enqueue(new C.GroupInvite { AcceptInvite = true });
                var dlg = GameScene.Scene?.GroupDialog;
                if (dlg != null) dlg.Show();
            };
            box.NoButton.Click += (o, e) => Network.Enqueue(new C.GroupInvite { AcceptInvite = false });
            box.Show();
        }

        internal static void GroupAddMember(S.AddMember p)
        {
            if (!GroupDialog.GroupList.Contains(p.Name)) GroupDialog.GroupList.Add(p.Name);
            var chat = GameScene.Scene?.ChatDialog;
            if (chat != null)
                chat.ReceiveChat(GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.PlayerHasJoinedGroup, p.Name), ChatType.Group);
        }

        internal static void GroupMembersMap(S.GroupMembersMap p)
        {
            if (!GroupDialog.GroupMembersMap.ContainsKey(p.PlayerName))
                GroupDialog.GroupMembersMap.Add(p.PlayerName, p.PlayerMap);
            else
            {
                GroupDialog.GroupMembersMap.Remove(p.PlayerName);
                GroupDialog.GroupMembersMap.Add(p.PlayerName, p.PlayerMap);
            }
        }

        internal static void GroupMemberLocation(S.SendMemberLocation p)
        {
            var loc = new MPoint(p.MemberLocation.X, p.MemberLocation.Y); // 封包 Point 转 MPoint（同 ObjectUserInformation 范式）
            if (!BigMapViewPort.PlayerLocations.ContainsKey(p.MemberName))
                BigMapViewPort.PlayerLocations.Add(p.MemberName, loc);
            else
            {
                BigMapViewPort.PlayerLocations.Remove(p.MemberName);
                BigMapViewPort.PlayerLocations.Add(p.MemberName, loc);
            }
        }

        // 好友整表回声（8-6-2）：C.RefreshFriends/AddFriend/RemoveFriend/AddMemo 后服务端全量回 S.FriendUpdate
        // （无增量包），填 FriendDialog.Friends；面板开着才 Update(false)（保选中，旧客户端同款）。
        internal static void FriendUpdate(S.FriendUpdate p)
        {
            var dlg = GameScene.Scene?.FriendDialog;
            if (dlg == null) return;
            dlg.Friends = p.Friends;
            if (dlg.Visible) dlg.Update(false);
        }

        internal static void ObjectSpell(S.ObjectSpell p)
        {
            if (MapControl.Objects.TryGetValue(p.ObjectID, out var ob) && ob is SpellObject spo)
            {
                spo.Load(p);
                return;
            }

            spo = new SpellObject(p.ObjectID);
            spo.Load(p);
        }

        // 地面物品/金币共用 ItemObject（金币走 Load(S.ObjectGold) 分级帧）。
        // FloorItems 图集数据当前不在仓库（EnsureFloorItems null）→ 不建对象优雅跳过；
        // 数据就位后补图集即可，代码路径不变。
        internal static void ObjectItem(S.ObjectItem p)
        {
            if (EnsureFloorItems() == null) return;
            if (MapControl.Objects.TryGetValue(p.ObjectID, out var ob) && ob is ItemObject io)
            {
                io.Load(p);
                return;
            }

            io = new ItemObject(p.ObjectID);
            io.Load(p);
        }

        internal static void ObjectGold(S.ObjectGold p)
        {
            if (EnsureFloorItems() == null) return;
            if (MapControl.Objects.TryGetValue(p.ObjectID, out var ob) && ob is ItemObject ig)
            {
                ig.Load(p);
                return;
            }

            ig = new ItemObject(p.ObjectID);
            ig.Load(p);
        }

        static MLibrary EnsureFloorItems()
        {
            var m = GameRenderer.EnsureMLibrary("FloorItems");
            if (m != null) Libraries.FloorItems = m;
            return m;
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

        // 使用回流（8-2-4）：S.UseItem 成功确认→解锁来源背包格 + 数量-1/清格 + RefreshStats。
        // HP/MP 恢复走独立 S.HealthChanged（服务器权威封顶，客户端不本地补血防溢出）。
        // internal：useitemverify 探针（Crystal.Client.Rendering.Editor）直接调用测全链。
        internal static void UseItem(S.UseItem p)
        {
            if (MapObject.User == null) return;
            var scene = GameScene.Scene;
            if (scene == null) return;

            var cell = scene.InventoryDialog != null ? scene.InventoryDialog.GetCell(p.UniqueID) : null;
            if (cell == null) return;
            cell.Locked = false;

            if (!p.Success) return;
            if (cell.Item.Count > 1) cell.Item.Count--;
            else cell.Item = null;
            MapObject.User.RefreshStats();
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
            ScreenMetrics.Set(GameRuntime.ScreenW, GameRuntime.ScreenH); // P2 分辨率统一：对话框布局基准对齐渲染真值
            if (Libraries.Prguse == null) Libraries.Prguse = GameRenderer.EnsureMLibrary("Prguse");
            if (Libraries.Prguse2 == null) Libraries.Prguse2 = GameRenderer.EnsureMLibrary("Prguse2");
            if (Libraries.Items == null) Libraries.Items = GameRenderer.EnsureMLibrary("Items");
            if (Libraries.Title == null) Libraries.Title = GameRenderer.EnsureMLibrary("Title");
            if (Libraries.UI_32bit == null) Libraries.UI_32bit = GameRenderer.EnsureMLibrary("UI");
            try
            {
                var main = new MainDialog { Parent = scene };
                scene.MainDialog = main;
                // 聊天窗（8-5-2）：常驻创建（旧客户端 MainDialog ctor 直接建底部聊天窗，Unity 端此前裁剪——
                // 聊天输入未接，ChatDialog 从未实例化）。ChatDialog ctor 读 Scene.MainDialog.Location →
                // 须在 main 之后（NetProbe 顺序契约同款）。常驻可见（底部聊天窗，点聊天按钮开输入框弹软键盘）。
                var chat = new ChatDialog { Parent = scene };
                scene.ChatDialog = chat;
                var inv = new InventoryDialog { Parent = scene };
                scene.InventoryDialog = inv;
                // NPC 对话框（8-3-1）：常驻创建默认隐藏。不设为 Visible=false 会污染 UiHitTest
                // （MirControl.Visible 默认 true），且 MapObject.cs NPC 移除会直接 Hide 它（NRE 兜底）。
                var npc = new NPCDialog { Parent = scene, Visible = false };
                scene.NPCDialog = npc;
                // 商店对话框（8-3-2）：常驻创建默认隐藏（同 NPCDialog 模式）。运行时商店均为 Buy
                // 面板（Craft/BuySub 裁剪未支持），PType 固定 Buy；NpcGoods 懒建兜底才用 p.Type。
                var goods = new NPCGoodsDialog(PanelType.Buy) { Parent = scene, Visible = false };
                scene.NPCGoodsDialog = goods;
                // 仓库对话框（8-3-3）：常驻创建默认隐藏（同 NPCDialog 模式）。@STORAGE 时
                // S.UserStorage/S.NPCStorage 派发 Show；Runtime 图集 Prguse 已就位，StorageDialog 尺寸正常。
                var storage = new StorageDialog { Parent = scene, Visible = false };
                scene.StorageDialog = storage;
                // 任务四窗（8-4-1）：常驻创建默认隐藏（同 NPCDialog 模式）。顺序契约：QuestListDialog
                // ctor 读 NPCDialog.Size（上方已建）；QuestSingleQuestItem 读 QuestTrackingDialog（先建）。
                // 追踪栏由 Add/RemoveQuest→DisplayQuests 自驱显隐（有追踪项才 Show）。
                var qTracking = new QuestTrackingDialog { Parent = scene, Visible = false };
                scene.QuestTrackingDialog = qTracking;
                var qDiary = new QuestDiaryDialog { Parent = scene, Visible = false };
                scene.QuestDiaryDialog = qDiary;
                var qList = new QuestListDialog { Parent = scene, Visible = false };
                scene.QuestListDialog = qList;
                var qDetail = new QuestDetailDialog { Parent = scene, Visible = false };
                scene.QuestDetailDialog = qDetail;
                // 大地图（8-4-2）：常驻创建默认隐藏（同 NPCDialog 模式）。移动端地图按钮 Show 打开，
                // Show→TargetMyLocation→SetTargetMap 按需发 C.RequestMapInfo；S.NewMapInfo 回填记录。
                var bigMap = new BigMapDialog { Parent = scene, Visible = false };
                scene.BigMapDialog = bigMap;
                // 小地图（8-4-3）：常驻创建（HUD 右上角，旧客户端 GameScene ctor 直接建，Visible 默认 true）。
                // 档位切换/大地图按钮走 MirButton.Click（TouchInputAdapter 鼠标链）；坐标/地图名每帧 Process。
                var mini = new MiniMapDialog { Parent = scene };
                scene.MiniMapDialog = mini;
                // 组队面板（8-6-1）：常驻创建默认隐藏（同 NPCDialog 模式）。移动端组队按钮 Toggle
                // Show/Hide；S.SwitchGroup/AddMember/DeleteMember/DeleteGroup 分发刷新静态数据；
                // S.GroupInvite 弹 MirMessageBox YesNo（接受 → C.GroupInvite{true}）。
                var group = new GroupDialog { Parent = scene, Visible = false };
                scene.GroupDialog = group;
                // 好友/黑名单（8-6-2）：常驻创建默认隐藏（同 NPCDialog 模式）。FriendDialog.Hide 连带
                // Hide MemoDialog（实例引用）→ MemoDialog 须先建。移动端好友按钮 Toggle Show/Hide；
                // Show 发 C.RefreshFriends → S.FriendUpdate 回声刷新整表。Whisper seam 由 MobileBootstrap 接。
                var memo = new MemoDialog { Parent = scene, Visible = false };
                scene.MemoDialog = memo;
                var friend = new FriendDialog { Parent = scene, Visible = false };
                scene.FriendDialog = friend;
                // DuraStatusPanel 为旧客户端 DuraStatusDialog seam 占位（Unity 未渲染耐久条），
                // MiniMapDialog Toggle/档位自适应 SetSmallMode/SetBigMode 引用其 Location → 空控件防 NRE。
                if (scene.DuraStatusPanel == null)
                    scene.DuraStatusPanel = new MirImageControl { Parent = scene, Visible = false };
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
