using System;
using System.Collections.Generic;
using System.Linq;
using Client;
using Client.MirControls;
using Client.MirGraphics;
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
    // 阶段8 第4项 增量2 大地图触控纯逻辑验证（无服务器）：
    // GameSession.NewMapInfo → MapInfoList 记录构建（移动按钮 Parent=ViewPort、NPC 行 Parent=BigMapDialog）；
    // WorldMapSetup → WorldButton.Visible + TeleportToNPCCost；Show()→SetTargetMap(当前图) 无记录发
    // C.RequestMapInfo、有记录直接显示；视口点击（TouchInputAdapter 同链路 OnMouseClick）→ 寻路设定
    // CurrentPath+AutoPath；MobileAutoPath 逐格 C.Walk 驱动 + Cancel；移动按钮点击→SetTargetMap(目的地)；
    // NPC 行点击→SelectedNPC 选中+传送按钮使能（CanTeleportTo 门控）；传送 Gold 门控（<花费不发包）。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.MapVerify.Run -quit
    // 断言：全过输出 [mapverify] PASS exit 0。
    public static class MapVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[mapverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（MapInfoList/Objects/User/Scene/Gold/花费）+ 建 30x30 全空网格
        // + 玩家 + BigMapDialog（显式尺寸复现真实命中区）。MiniMap seam ImageSize 驱动视口
        // OnBeforeDraw 计算 ScaleX/Y（batchmode 空库 GetSize 返回 0x0，须显式模拟尺寸）。
        static MapControl NewScene()
        {
            GameScene.Scene = null;
            GameScene.User = null;
            GameScene.MapInfoList.Clear();
            GameScene.Gold = 500;
            GameScene.TeleportToNPCCost = 1000;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;

            var mc = new MapControl
            {
                Width = 30,
                Height = 30,
                M2CellInfo = new CellInfo[30, 30],
                Index = 0,
                BigMap = 5,
            };
            for (int x = 0; x < 30; x++)
                for (int y = 0; y < 30; y++)
                    mc.M2CellInfo[x, y] = new CellInfo();
            mc.PathFinder = new PathFinder(mc);

            var user = new UserObject(1)
            {
                Movement = new MPoint(1, 1),
                CurrentLocation = new MPoint(1, 1),
                OffSetMove = MPoint.Empty,
                Direction = MirDirection.Up,
                Name = "probe",
            };
            MapObject.User = user;
            MapControl.User = user;
            GameScene.User = user;

            var scene = new GameScene { MapControl = mc };
            GameScene.Scene = scene;

            var bigMap = new BigMapDialog { Parent = scene };
            bigMap.AutoSize = false;
            bigMap.Size = new Size(700, 450);
            bigMap.Location = new MPoint((1280 - 700) / 2, (720 - 450) / 2); // (290,135)
            bigMap.Visible = false;
            scene.BigMapDialog = bigMap;

            Libraries.MiniMap.ImageSize = new Size(200, 100); // 视口 200x100 → ScaleX=200/30
            return mc;
        }

        // 一张大地图记录数据源：1 个移动点（目的地 2）+ 2 个 NPC（可传送/不可传送）。
        static ClientMapInfo MapInfoOf(int mapIndex)
        {
            return new ClientMapInfo
            {
                Width = 30,
                Height = 30,
                BigMap = 5,
                Title = "测试地图",
                Movements = new List<ClientMovementInfo>
                {
                    new ClientMovementInfo { Destination = 2, Title = "移动点", Location = new System.Drawing.Point(10, 10), Icon = 200 },
                },
                NPCs = new List<ClientNPCInfo>
                {
                    new ClientNPCInfo { ObjectID = 7001, Name = "传送NPC", MapIndex = 0, Location = new System.Drawing.Point(15, 15), Icon = 300, CanTeleportTo = true },
                    new ClientNPCInfo { ObjectID = 7002, Name = "普通NPC", MapIndex = 0, Location = new System.Drawing.Point(18, 18), Icon = 301, CanTeleportTo = false },
                },
            };
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

        // 网络包捕获（Network.SentPackets 无条件记录 Enqueue，含未连接丢弃）。
        static void DrainPackets()
        {
            while (Network.SentPackets.TryDequeue(out _)) { }
        }

        static bool HasRequest(int mapIndex)
        {
            bool found = false;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.RequestMapInfo r && r.MapIndex == mapIndex) found = true;
            return found;
        }

        static bool HasTeleport(uint objectId)
        {
            bool found = false;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.TeleportToNPC t && t.ObjectID == objectId) found = true;
            return found;
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;
            CMain.Time = 10000;

            // ===== case1 S.NewMapInfo → MapInfoList 记录 + 按钮/行父级契约 =====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var info = MapInfoOf(0);
                GameSession.NewMapInfo(new S.NewMapInfo { MapIndex = 0, Info = info });
                Check(GameScene.MapInfoList.ContainsKey(0), "case1 record added");
                var rec = GameScene.MapInfoList[0];
                Check(rec.Index == 0 && rec.MapInfo == info, "case1 record fields");
                Check(rec.MovementButtons.Count == 1, "case1 movement button count");
                Check(rec.NPCButtons.Count == 2, "case1 npc row count");
                Check(rec.MovementButtons.Values.First().Parent == scene.BigMapDialog.ViewPort, "case1 button parent viewport");
                Check(rec.NPCButtons[0].Parent == scene.BigMapDialog, "case1 row parent dialog");
            }

            // ===== case2 S.WorldMapSetup → 世界地图按钮 + 传送花费；世界地图可打开 =====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var setup = new WorldMapSetup
                {
                    Enabled = true,
                    Icons = new List<WorldMapIcon> { new WorldMapIcon { ImageIndex = 400, Title = "主城", MapIndex = 3 } },
                };
                GameSession.WorldMapSetup(new S.WorldMapSetupInfo { Setup = setup, TeleportToNPCCost = 2000 });
                scene.BigMapDialog.Show(); // Visible getter 依赖 Parent.Visible → 对话框须先显示
                Check(scene.BigMapDialog.WorldButton.Visible, "case2 world button visible");
                Check(GameScene.TeleportToNPCCost == 2000, "case2 teleport cost");
                scene.BigMapDialog.WorldButton.InvokeMouseClick(null);
                Check(scene.BigMapDialog.WorldMap.Visible, "case2 world map opened");
            }

            // ===== case3 Show()→SetTargetMap(当前图)：无记录发请求 → NewMapInfo 回填 → 重开直接显示 =====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var dlg = scene.BigMapDialog;
                DrainPackets();
                dlg.Show(); // BigMap=5 → TargetMyLocation → SetTargetMap(0)，MapInfoList 空 → 发 C.RequestMapInfo
                Check(dlg.Visible, "case3 shown");
                Check(HasRequest(0), "case3 request current map");
                GameSession.NewMapInfo(new S.NewMapInfo { MapIndex = 0, Info = MapInfoOf(0) });
                DrainPackets();
                dlg.SetTargetMap(0);
                Check(dlg.CurrentRecord != null && dlg.CurrentRecord.Index == 0, "case3 record set");
                Check(!HasRequest(0), "case3 no re-request");
            }

            // ===== case4 视口点击 → 寻路设定 CurrentPath + AutoPath =====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var dlg = scene.BigMapDialog;
                GameSession.NewMapInfo(new S.NewMapInfo { MapIndex = 0, Info = MapInfoOf(0) });
                dlg.SetTargetMap(0);
                dlg.Visible = true;
                dlg.ViewPort.Draw(); // OnBeforeDraw：MiniMap.ImageSize seam → ScaleX/Y + 视口定位
                Tap(Center(dlg.ViewPort));
                Check(mc.AutoPath, "case4 autopath set");
                Check(mc.CurrentPath != null && mc.CurrentPath.Count > 0, "case4 path set");
            }

            // ===== case5 移动按钮点击 → SetTargetMap(目的地) + 发请求 =====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var dlg = scene.BigMapDialog;
                GameSession.NewMapInfo(new S.NewMapInfo { MapIndex = 0, Info = MapInfoOf(0) });
                dlg.SetTargetMap(0);
                dlg.Visible = true;
                dlg.ViewPort.Draw();
                var btn = GameScene.MapInfoList[0].MovementButtons.Values.First();
                btn.AutoSize = false;
                btn.Size = new Size(20, 20); // batchmode 空库 AutoSize 0x0，强制显式尺寸复现命中区
                DrainPackets();
                Tap(Center(btn));
                Check(dlg.TargetMapIndex == 2, "case5 movement to dest");
                Check(HasRequest(2), "case5 request dest");
            }

            // ===== case6 NPC 行点击 → SelectedNPC 切换 + 传送行为门控（CanTeleportTo=false 不发包）=====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var dlg = scene.BigMapDialog;
                GameSession.NewMapInfo(new S.NewMapInfo { MapIndex = 0, Info = MapInfoOf(0) });
                dlg.SetTargetMap(0);
                dlg.Visible = true;
                var rec = GameScene.MapInfoList[0];
                var row0 = rec.NPCButtons[0]; // CanTeleportTo=true
                Tap(Center(row0));
                Check(dlg.SelectedNPC == row0, "case6 row0 selected");
                var row1 = rec.NPCButtons[1]; // CanTeleportTo=false
                Tap(Center(row1));
                Check(dlg.SelectedNPC == row1, "case6 row1 selected");
                GameScene.Gold = 5000;
                GameScene.TeleportToNPCCost = 100;
                DrainPackets();
                dlg.TeleportToButton.InvokeMouseClick(null); // TeleportToNPC：CanTeleportTo=false → return
                Check(!HasTeleport(row1.Info.ObjectID), "case6 non-teleport npc blocked");
            }

            // ===== case7 传送 Gold 门控：<花费不发包，≥花费发包 =====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var dlg = scene.BigMapDialog;
                GameSession.NewMapInfo(new S.NewMapInfo { MapIndex = 0, Info = MapInfoOf(0) });
                dlg.SetTargetMap(0);
                dlg.Visible = true;
                var row0 = GameScene.MapInfoList[0].NPCButtons[0];
                Tap(Center(row0));
                GameScene.Gold = 100;
                GameScene.TeleportToNPCCost = 2000;
                DrainPackets();
                dlg.TeleportToButton.InvokeMouseClick(null);
                Check(!HasTeleport(row0.Info.ObjectID), "case7 gated by gold");
                GameScene.Gold = 5000;
                dlg.TeleportToButton.InvokeMouseClick(null);
                Check(HasTeleport(row0.Info.ObjectID), "case7 teleport sent");
            }

            // ===== case8 MobileAutoPath 逐格走位驱动 + Cancel =====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var dlg = scene.BigMapDialog;
                GameSession.NewMapInfo(new S.NewMapInfo { MapIndex = 0, Info = MapInfoOf(0) });
                dlg.SetTargetMap(0);
                dlg.Visible = true;
                dlg.ViewPort.Draw();
                Tap(Center(dlg.ViewPort));
                Check(mc.AutoPath && mc.CurrentPath != null && mc.CurrentPath.Count > 0, "case8 autopath set");

                var walks = new List<MirDirection>();
                var origSend = MobileAutoPath.SendWalk;
                var origNow = MobileAutoPath.Now;
                long now = 500; // 初始化到节流窗口内，首次 Tick 即走
                MobileAutoPath.Now = () => now;
                MobileAutoPath.SendWalk = d => walks.Add(d);
                var ap = new MobileAutoPath();
                ap.Tick();
                Check(walks.Count == 1, "case8 walked one step");
                Check(walks[0] == global::Client.MirObjects.Functions.DirectionFromPoint(MapObject.User.CurrentLocation, mc.CurrentPath[1].Location), "case8 walk direction");
                now += 500;
                ap.Tick();
                Check(walks.Count == 2, "case8 second step");
                ap.Cancel();
                Check(!mc.AutoPath, "case8 cancel clears autopath");
                MobileAutoPath.SendWalk = origSend;
                MobileAutoPath.Now = origNow;
            }

            // 还原全局 seam（防污染后续探针）。
            GameScene.MapInfoList.Clear();
            GameScene.Gold = 0;
            GameScene.TeleportToNPCCost = 1000;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.User = null;
            Libraries.MiniMap.ImageSize = Size.Empty;

            if (_fail == 0)
            {
                Console.WriteLine("[mapverify] PASS cases=8");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[mapverify] FAIL cases=8 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
