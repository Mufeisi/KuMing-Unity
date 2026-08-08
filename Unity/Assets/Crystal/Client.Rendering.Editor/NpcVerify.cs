using System;
using System.Collections.Generic;
using System.Linq;
using Client;
using Client.MirControls;
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
    // 阶段8 第3项 增量1 NPC 对话触控纯逻辑验证（无服务器）：
    // MobileNpc.TapAt 地图 tap→屏转格→最近 NPCObject（≤TapRadius）命中置 GameScene.NPCID +
    // C.CallNPC{ObjectID,Key="[@Main]"}；无 NPC/超半径拒绝（落回拾取）；独立节流 5s 节流期内仍消费
    // （不重发不落拾取）；对话框 Visible 不重弹。GameSession.NpcResponse：S.NPCResponse → NPCDialog
    // NewText 渲染选项 + Show；选项点击（TouchInputAdapter 同链路 OnMouseClick）→ C.CallNPC[动作]；
    // 选项节流走 GameScene.NPCTime；@Exit 关闭。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.NpcVerify.Run -quit
    // 断言：全过输出 [npcverify] PASS exit 0。
    public static class NpcVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[npcverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Objects/ObjectsList/User/Scene/OffSet），建 30x30 全空网格 + 玩家。
        static MapControl NewMap(int px, int py, out UserObject user)
        {
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            GameScene.Scene = null;
            MapControl.OffSetX = 0;
            MapControl.OffSetY = 0;

            var mc = new MapControl { Width = 30, Height = 30, M2CellInfo = new CellInfo[30, 30] };
            for (int x = 0; x < 30; x++)
                for (int y = 0; y < 30; y++)
                    mc.M2CellInfo[x, y] = new CellInfo();
            mc.PathFinder = new PathFinder(mc);

            user = new UserObject(1)
            {
                Movement = new MPoint(px, py),
                CurrentLocation = new MPoint(px, py),
                OffSetMove = MPoint.Empty,
                Direction = MirDirection.Up,
                Name = "probe",
            };
            MapObject.User = user;
            MapControl.User = user;
            GameScene.Scene = new GameScene { MapControl = mc };
            GameSession.State = GameSessionState.InGame;
            return mc;
        }

        // NPCObject ctor 自动注册 Objects+ObjectsList；仅需补位置字段。
        static NPCObject SpawnNpc(uint id, int x, int y)
        {
            return new NPCObject(id)
            {
                Movement = new MPoint(x, y),
                CurrentLocation = new MPoint(x, y),
                MapLocation = new MPoint(x, y),
            };
        }

        // 格 → 屏（ui 空间）：屏→格逆变换（tileX = ui.X/CellWidth - OffSetX + user.Movement.X）。
        static MPoint UiOf(MapControl mc, UserObject user, MPoint tile)
        {
            return new MPoint(
                (tile.X - user.Movement.X + MapControl.OffSetX) * MapControl.CellWidth,
                (tile.Y - user.Movement.Y + MapControl.OffSetY) * MapControl.CellHeight);
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

        // 选项点击走 NPCDialog.ButtonClicked → Network.Enqueue 直发（非 MobileNpc.SendCallNpc seam）：
        // 用 SentPackets 队列捕获断言（同 UseItemVerify）。
        static void DrainPackets()
        {
            while (Network.SentPackets.TryDequeue(out _)) { }
        }

        static C.CallNPC LastCallNpc()
        {
            C.CallNPC result = null;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.CallNPC call) result = call;
            return result;
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;
            var calls = new List<C.CallNPC>();
            MobileNpc.SendCallNpc = p => calls.Add(p);

            // ===== case1 命中 NPC：发 C.CallNPC[@Main] + NPCID 置位 =====
            {
                var mc = NewMap(10, 10, out var user);
                SpawnNpc(7001, 11, 10);
                CMain.Time = 10000; GameScene.NPCID = 0; calls.Clear();
                var npc = new MobileNpc();
                Check(npc.TapAt(mc, UiOf(mc, user, new MPoint(11, 10))), "case1 tap hit npc");
                Check(calls.Count == 1 && calls[0].ObjectID == 7001 && calls[0].Key == "[@Main]", "case1 CallNPC [@Main]");
                Check(GameScene.NPCID == 7001, "case1 NPCID set");
            }

            // ===== case2 无 NPC：拒绝 + 不发包（落回拾取）=====
            {
                var mc = NewMap(10, 10, out var user);
                calls.Clear();
                var npc = new MobileNpc();
                Check(!npc.TapAt(mc, UiOf(mc, user, new MPoint(12, 10))), "case2 no npc rejected");
                Check(calls.Count == 0, "case2 no packet");
            }

            // ===== case3 超 tap 半径：拒绝 =====
            {
                var mc = NewMap(10, 10, out var user);
                SpawnNpc(7003, 11, 10);
                calls.Clear();
                var npc = new MobileNpc();
                Check(!npc.TapAt(mc, UiOf(mc, user, new MPoint(13, 10))), "case3 out-of-radius rejected");
                Check(calls.Count == 0, "case3 no packet");
            }

            // ===== case4 独立节流 5000ms：期内消费不重发，期后重发 =====
            {
                var mc = NewMap(10, 10, out var user);
                SpawnNpc(7004, 11, 10);
                var npc = new MobileNpc();
                CMain.Time = 10000; calls.Clear();
                npc.TapAt(mc, UiOf(mc, user, new MPoint(11, 10)));
                Check(calls.Count == 1, "case4 first call");
                CMain.Time = 10400; calls.Clear();
                Check(npc.TapAt(mc, UiOf(mc, user, new MPoint(11, 10))), "case4 throttled still consumes");
                Check(calls.Count == 0, "case4 throttled no packet");
                CMain.Time = 15000; calls.Clear();
                npc.TapAt(mc, UiOf(mc, user, new MPoint(11, 10)));
                Check(calls.Count == 1, "case4 re-send after cooldown");
            }

            // ===== case5 对话框已开：不重弹（对齐旧客户端 Dialog.Visible 时点击 NPC 无效）=====
            {
                var mc = NewMap(10, 10, out var user);
                SpawnNpc(7005, 11, 10);
                calls.Clear();
                var sc = GameScene.Scene;
                sc.NPCDialog = new NPCDialog { Parent = sc, Visible = false };
                sc.NPCDialog.Visible = true;
                var npc = new MobileNpc();
                Check(!npc.TapAt(mc, UiOf(mc, user, new MPoint(11, 10))), "case5 dialog-open blocks");
                Check(calls.Count == 0, "case5 no packet");
                sc.NPCDialog.Visible = false;
            }

            // ===== case6 S.NPCResponse → 对话渲染 + 选项点击发包 + 节流 + @Exit 关闭 =====
            {
                GameScene.Scene = new GameScene();
                var inv = new InventoryDialog { Parent = GameScene.Scene };
                GameScene.Scene.InventoryDialog = inv;
                var npcDlg = new NPCDialog { Parent = GameScene.Scene, Visible = false };
                GameScene.Scene.NPCDialog = npcDlg;
                GameScene.NPCID = 42;
                GameScene.NPCTime = 0;

                var page = new List<string> { "欢迎来到我的商店", "<购买药品/@buy>", "<出售物品/@sell>", "<离开/@Exit>" };
                GameSession.NpcResponse(new S.NPCResponse { Page = page });
                Check(npcDlg.Visible, "case6 dialog visible");
                Check(npcDlg.CurrentLines.SequenceEqual(page), "case6 current lines");
                Check(npcDlg.TextButtons.Count == 3, "case6 option buttons count");
                // NewText 里 `Size = TrueSize` 在空库下把面板缩为 0×0（真机库帧正常）→ 探针重设
                // 尺寸保证父矩形可命中（同 baginteract 探针模式）。
                npcDlg.AutoSize = false;
                npcDlg.Size = new global::Crystal.Client.Core.MirMath.Size(450, 200);

                calls.Clear();
                CMain.Time = 10000;
                DrainPackets();
                Tap(Center(npcDlg.TextButtons[0]));
                var sent1 = LastCallNpc();
                // regex group3 含 @：Key = "[@buy]"（对齐旧客户端 ButtonClicked `$"[{action}]"`）。
                Check(sent1 != null && sent1.Key == "[@buy]" && sent1.ObjectID == 42, "case6 option click CallNPC");

                CMain.Time = 11000; // 距上击 1000ms 防双击判定；仍 ≤ NPCTime(+5000) → 选项节流吞
                DrainPackets();
                Tap(Center(npcDlg.TextButtons[1]));
                Check(LastCallNpc() == null, "case6 option throttle");

                CMain.Time = 12000;
                Tap(Center(npcDlg.TextButtons[2])); // @Exit 不节流，直接关闭
                Check(!npcDlg.Visible, "case6 @Exit closes");
            }

            // 还原静态委托 + 全局 seam（防污染后续探针）。
            MobileNpc.SendCallNpc = p => global::Client.MirNetwork.Network.Enqueue(p);
            GameScene.NPCID = 0;
            GameScene.NPCTime = 0;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.User = null;

            if (_fail == 0)
            {
                Console.WriteLine("[npcverify] PASS cases=6");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[npcverify] FAIL cases=6 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
