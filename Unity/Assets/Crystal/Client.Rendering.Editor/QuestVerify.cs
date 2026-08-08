using System;
using System.Collections.Generic;
using System.Linq;
using Client;
using Client.MirControls;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Client.MirScenes.Dialogs;
using Crystal.Client.Core.MirMath;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;
using C = ClientPackets;
using S = ServerPackets;
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第4项 任务四窗触控纯逻辑验证（无服务器）：
    // S.NewQuestInfo → QuestInfoList（NPCObject.Load 按 NPCIndex 关联）；NpcResponse 有任务 NPC 连带
    // 弹 QuestListDialog（无任务不弹、NPC 对话保留）；S.ChangeQuest Add/Update/Remove 双引用同步
    // （GameScene.User + MapObject.User）+ TrackQuest 追踪 + Remove 摘追踪；QuestDiaryDialog.Show 分组
    // + 点行开详情（QuestSingleQuestItem 点行=_questLabel）+ 追踪按钮 toggle + 5 条上限。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.QuestVerify.Run -quit
    // 断言：全过输出 [questverify] PASS exit 0。
    public static class QuestVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[questverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Scene/User/NPCID/QuestInfoList/Objects/TrackedQuests + hover 静态态），
        // 建空场景 + 背包（NPCDialog.Show 读 InventoryDialog）+ 玩家（双 User 引用同实例）+ 四窗
        // （顺序契约：NPCDialog → QuestTracking → QuestDiary → QuestList → QuestDetail）。对话框
        // Library null 下 AutoSize 回退 0×0 → 显式尺寸供子控件 hover 命中。
        static GameScene NewScene()
        {
            GameScene.Scene = null;
            GameScene.User = null;
            GameScene.NPCID = 0;
            GameScene.QuestInfoList.Clear();
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            Settings.TrackedQuests = new int[5];

            var scene = new GameScene();
            GameScene.Scene = scene;

            var inv = new InventoryDialog { Parent = scene, Visible = false };
            inv.AutoSize = false;
            inv.Size = new Size(340, 240);
            scene.InventoryDialog = inv;

            var npc = new NPCDialog { Parent = scene, Visible = false };
            npc.AutoSize = false;
            npc.Size = new Size(300, 400); // QuestListDialog ctor 读 Size.Width 定 Location
            scene.NPCDialog = npc;

            scene.QuestTrackingDialog = new QuestTrackingDialog { Parent = scene, Visible = false };

            var qDiary = new QuestDiaryDialog { Parent = scene, Visible = false };
            qDiary.AutoSize = false;
            qDiary.Size = new Size(300, 400);
            scene.QuestDiaryDialog = qDiary;

            var qList = new QuestListDialog { Parent = scene, Visible = false };
            qList.AutoSize = false;
            qList.Size = new Size(320, 470);
            scene.QuestListDialog = qList;

            var qDetail = new QuestDetailDialog { Parent = scene, Visible = false };
            qDetail.AutoSize = false;
            qDetail.Size = new Size(320, 470);
            scene.QuestDetailDialog = qDetail;

            var user = new UserObject(1) { Name = "probe", Level = 30 };
            GameScene.User = user;
            MapObject.User = user;
            MapControl.User = user;
            return scene;
        }

        static ClientQuestInfo QuestInfoOf(int index, uint npcIndex, string name, string group)
        {
            return new ClientQuestInfo
            {
                Index = index,
                NPCIndex = npcIndex,
                Name = name,
                Group = group,
                MinLevelNeeded = 5,
                Description = new List<string> { "描述文本" },
                TaskDescription = new List<string> { "击杀 5 只怪物" },
                ReturnDescription = new List<string> { "找任务发布者交还" },
                CompletionDescription = new List<string> { "任务完成描述" },
                RewardExp = 100,
            };
        }

        static ClientQuestProgress QuestOf(int id, ClientQuestInfo info)
        {
            return new ClientQuestProgress { Id = id, QuestInfo = info, Taken = true, TaskList = new List<string> { "进度 0/5" } };
        }

        // NPCObject ctor 自动注册 Objects+ObjectsList；Quests 手动赋（模拟 Load 的 QuestInfoList 关联）。
        static NPCObject SpawnNpc(uint id, ClientQuestInfo info)
        {
            return new NPCObject(id) { Quests = info != null ? new List<ClientQuestInfo> { info } : new List<ClientQuestInfo>() };
        }

        // 点按分发（与 TouchInputAdapter 同链路：Move 更新 hover → Down 置 ActiveControl → Up+Click）。
        static void Tap(MPoint p)
        {
            var sc = GameScene.Scene;
            CMain.MPoint = p;
            sc.OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0));
            sc.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
            sc.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
            sc.OnMouseClick(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        }

        static MPoint Center(MirControl c)
        {
            var r = c.DisplayRectangle;
            return new MPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        // 探针夹具：batchmode 空库下 AutoSize 失真（MirImageControl.Size→GetTrueSize 返回 0×0；
        // MirLabel 文本测量回退把状态文本撑到 93px 盖住 (236,0) 按钮）。真机库 GetTrueSize=16×14、
        // 状态文本 ~3 CJK 字符，故强制显式尺寸复现真实命中区。
        static void PrepDiaryForTap(QuestDiaryDialog diary)
        {
            foreach (var g in diary.TaskGroups)
                foreach (var c in g.Controls)
                    if (c is QuestSingleQuestItem s)
                        foreach (var ch in s.Controls)
                            if (ch is MirButton b)
                            {
                                b.AutoSize = false;
                                b.Size = new Size(16, 14);
                            }
                            else if (ch is MirLabel l && l.Location.X == 185)
                            {
                                l.AutoSize = false;
                                l.Size = new Size(30, 16);
                            }
        }

        // 日记里按任务 Id 定位单行控件（TaskGroups 公开，单行在 group.Controls 里）。
        static QuestSingleQuestItem RowOf(QuestDiaryDialog diary, int questId)
        {
            foreach (var g in diary.TaskGroups)
                foreach (var c in g.Controls)
                    if (c is QuestSingleQuestItem s && s.Quest != null && s.Quest.Id == questId)
                        return s;
            return null;
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;
            CMain.Time = 10000;

            // ===== case1 S.NewQuestInfo → QuestInfoList + NPC 关联契约 =====
            {
                var scene = NewScene();
                var info = QuestInfoOf(101, 7001, "新手任务", "主线");
                GameSession.NewQuestInfo(new S.NewQuestInfo { Info = info });
                Check(GameScene.QuestInfoList.Count == 1 && GameScene.QuestInfoList[0] == info, "case1 quest info list");
                var npc = new NPCObject(7001) { Quests = GameScene.QuestInfoList.Where(c => c.NPCIndex == 7001).ToList() };
                Check(npc.Quests.Count == 1 && npc.Quests[0] == info, "case1 npc association");
            }

            // ===== case2 NpcResponse 门控：有任务 NPC → QuestListDialog.Show + NPC 对话保留 =====
            {
                var scene = NewScene();
                var info = QuestInfoOf(102, 7001, "接取任务", "主线");
                GameSession.NewQuestInfo(new S.NewQuestInfo { Info = info });
                GameScene.NPCID = 7001;
                SpawnNpc(7001, info);
                GameSession.NpcResponse(new S.NPCResponse { Page = new List<string> { "你好，勇士。" } });
                Check(scene.NPCDialog.Visible, "case2 npc dialog shown");
                var qList = scene.QuestListDialog;
                Check(qList.Visible, "case2 quest list shown");
                Check(qList.Rows[0] != null && qList.SelectedQuest != null, "case2 row0 auto-selected");
                Check(qList.SelectedQuest.QuestInfo == info, "case2 selected quest info");
            }

            // ===== case3 无任务 NPC → 列表不弹、对话保留 =====
            {
                var scene = NewScene();
                GameScene.NPCID = 7002;
                SpawnNpc(7002, null); // 空 Quests 列表（Load 无匹配时的形态）
                GameSession.NpcResponse(new S.NPCResponse { Page = new List<string> { "这里没有任务。" } });
                Check(scene.NPCDialog.Visible, "case3 npc dialog kept");
                Check(!scene.QuestListDialog.Visible, "case3 list not shown");
            }

            // ===== case4 ChangeQuest Add 双引用 + TrackQuest 追踪 + Settings 落盘 =====
            {
                var scene = NewScene();
                var info = QuestInfoOf(104, 7001, "追踪任务", "主线");
                var q = QuestOf(104, info);
                GameSession.ChangeQuest(new S.ChangeQuest { Quest = q, QuestState = QuestState.Add, TrackQuest = true });
                Check(GameScene.User.CurrentQuests.Count == 1 && GameScene.User.CurrentQuests[0] == q, "case4 dual ref add A");
                Check(MapObject.User.CurrentQuests.Count == 1 && MapObject.User.CurrentQuests[0] == q, "case4 dual ref add B");
                var tracking = scene.QuestTrackingDialog;
                Check(tracking.TrackedQuestsIds.Contains(104), "case4 tracked id");
                Check(tracking.Visible, "case4 tracking shown");
                Check(Settings.TrackedQuests[0] == 104 && Settings.TrackedQuests[1] == -1, "case4 settings persisted");
            }

            // ===== case5 ChangeQuest Add 无 TrackQuest → 只入册不追踪 =====
            {
                var scene = NewScene();
                var q = QuestOf(105, QuestInfoOf(105, 7001, "静默任务", "主线"));
                GameSession.ChangeQuest(new S.ChangeQuest { Quest = q, QuestState = QuestState.Add, TrackQuest = false });
                Check(GameScene.User.CurrentQuests.Count == 1, "case5 added without track");
                Check(!scene.QuestTrackingDialog.TrackedQuestsIds.Contains(105), "case5 not tracked");
            }

            // ===== case6 ChangeQuest Update/Remove 双引用 + Remove 摘追踪 =====
            {
                var scene = NewScene();
                var q1 = QuestOf(106, QuestInfoOf(106, 7001, "进度任务", "主线"));
                GameSession.ChangeQuest(new S.ChangeQuest { Quest = q1, QuestState = QuestState.Add, TrackQuest = true });
                var q2 = QuestOf(106, QuestInfoOf(106, 7001, "进度任务", "主线"));
                q2.TaskList = new List<string> { "进度 5/5" };
                GameSession.ChangeQuest(new S.ChangeQuest { Quest = q2, QuestState = QuestState.Update, TrackQuest = false });
                Check(GameScene.User.CurrentQuests.Count == 1 && GameScene.User.CurrentQuests[0] == q2, "case6 update replaced");
                Check(MapObject.User.CurrentQuests[0] == q2, "case6 update dual ref");
                GameSession.ChangeQuest(new S.ChangeQuest { Quest = q2, QuestState = QuestState.Remove, TrackQuest = false });
                Check(GameScene.User.CurrentQuests.Count == 0 && MapObject.User.CurrentQuests.Count == 0, "case6 remove dual ref");
                Check(!scene.QuestTrackingDialog.TrackedQuestsIds.Contains(106), "case6 remove untracks");
                Check(Settings.TrackedQuests[0] == -1, "case6 settings cleared");
            }

            // ===== case7 日记分组 + 点行开详情 =====
            {
                var scene = NewScene();
                var q1 = QuestOf(107, QuestInfoOf(107, 7001, "任务一", "主线"));
                var q2 = QuestOf(108, QuestInfoOf(108, 7001, "任务二", "主线"));
                var q3 = QuestOf(109, QuestInfoOf(109, 7001, "任务三", "支线"));
                GameScene.User.CurrentQuests.AddRange(new[] { q1, q2, q3 });
                var qDiary = scene.QuestDiaryDialog;
                qDiary.Show();
                Check(qDiary.Visible && qDiary.TaskGroups.Count == 2, "case7 diary grouped");
                Check(qDiary.TaskGroups[0].Quests.Count == 2 && qDiary.TaskGroups[1].Quests.Count == 1, "case7 group sizes");
                CMain.Time += 10000;
                Tap(new MPoint(378, 122)); // 主线组第一行 _questLabel（diary(320,60)+group(15,40)+row(18,15) 中心偏左）
                var qDetail = scene.QuestDetailDialog;
                Check(qDetail.Visible && qDetail.Quest == q1, "case7 detail opened");
                var row1 = RowOf(qDiary, 107);
                var row2 = RowOf(qDiary, 108);
                Check(row1 != null && row1.Selected, "case7 row1 selected");
                Check(row2 != null && !row2.Selected, "case7 row2 not selected");
            }

            // ===== case8 追踪按钮 toggle（开 + 关）=====
            {
                var scene = NewScene();
                var q1 = QuestOf(110, QuestInfoOf(110, 7001, "追踪一", "主线"));
                var q2 = QuestOf(111, QuestInfoOf(111, 7001, "追踪二", "主线"));
                GameScene.User.CurrentQuests.AddRange(new[] { q1, q2 });
                scene.QuestTrackingDialog.TrackedQuestsIds = new List<int> { 110 };
                var qDiary = scene.QuestDiaryDialog;
                qDiary.Show();
                PrepDiaryForTap(qDiary);
                var row1 = RowOf(qDiary, 110);
                Check(row1 != null && row1.TrackQuest, "case8 row1 initially tracked");
                CMain.Time += 10000;
                Tap(new MPoint(597, 122)); // 第一行 _trackButton（(589,115) 中心）
                Check(!scene.QuestTrackingDialog.TrackedQuestsIds.Contains(110) && RowOf(qDiary, 110).TrackQuest == false, "case8 untracked");
                CMain.Time += 1000;
                Tap(new MPoint(597, 137)); // 第二行 _trackButton（(589,130) 中心）
                Check(scene.QuestTrackingDialog.TrackedQuestsIds.Contains(111) && RowOf(qDiary, 111).TrackQuest, "case8 tracked");
                Check(Settings.TrackedQuests[0] == 111, "case8 settings updated");
            }

            // ===== case9 追踪 5 条上限：第 6 条按钮点击不生效 =====
            {
                var scene = NewScene();
                var quests = new List<ClientQuestProgress>();
                for (int i = 0; i < 6; i++)
                    quests.Add(QuestOf(120 + i, QuestInfoOf(120 + i, 7001, "上限" + i, "主线")));
                GameScene.User.CurrentQuests.AddRange(quests);
                scene.QuestTrackingDialog.TrackedQuestsIds = new List<int> { 120, 121, 122, 123, 124 };
                var qDiary = scene.QuestDiaryDialog;
                qDiary.Show();
                PrepDiaryForTap(qDiary);
                var row6 = RowOf(qDiary, 125);
                Check(row6 != null && !row6.TrackQuest, "case9 row6 untracked");
                CMain.Time += 10000;
                Tap(new MPoint(597, 197)); // 第 6 行 _trackButton（(589,190) 中心）
                Check(scene.QuestTrackingDialog.TrackedQuestsIds.Count == 5, "case9 cap held");
                Check(RowOf(qDiary, 125).TrackQuest == false, "case9 row6 still untracked");
            }

            // 还原全局 seam（防污染后续探针）。
            GameScene.NPCID = 0;
            GameScene.QuestInfoList.Clear();
            GameScene.SelectedCell = null;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.User = null;
            Settings.TrackedQuests = new int[5];

            if (_fail == 0)
            {
                Console.WriteLine("[questverify] PASS cases=9");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[questverify] FAIL cases=9 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
