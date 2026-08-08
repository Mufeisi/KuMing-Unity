using System;
using System.Collections.Generic;
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
    // 阶段8 第4项 增量3 小地图触控纯逻辑验证（无服务器）：
    // MiniMapDialog 常驻控制树（初始大档 Index=2090）；ToggleButton → Toggle 大/小档切换
    // （2090↔2091 + SetSmallMode/SetBigMode，DuraStatusPanel seam 占位防 NRE）；Process 每帧刷
    // 地图名/坐标；BigMapButton → BigMapDialog.Toggle；MiniMap_BeforeDraw 档位自适应
    // （map.MiniMap=0 强切小档、>0 保持大档）。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.MiniMapVerify.Run -quit
    // 断言：全过输出 [minimapverify] PASS exit 0。
    public static class MiniMapVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[minimapverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Scene/User/MapInfoList/Objects）+ 建 30x30 全空网格 + 玩家 +
        // MainDialog（Process 刷 SModeLabel 依赖）+ MiniMapDialog + DuraStatusPanel 占位 + BigMapDialog。
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
                MiniMap = 1,
                BigMap = 5,
                Title = "测试地图",
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

            var main = new MainDialog { Parent = scene };
            scene.MainDialog = main;

            var mini = new MiniMapDialog { Parent = scene };
            scene.MiniMapDialog = mini;

            // DuraStatusPanel seam 占位（旧客户端 DuraStatusDialog 未移植；Toggle/档位自适应读 Location）。
            scene.DuraStatusPanel = new MirImageControl { Parent = scene, Visible = false };

            var bigMap = new BigMapDialog { Parent = scene, Visible = false };
            scene.BigMapDialog = bigMap;

            return mc;
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;
            CMain.Time = 10000;

            // ===== case1 常驻创建：初始大档 + 档位/大地图按钮存在 =====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var mini = scene.MiniMapDialog;
                Check(mini != null, "case1 minimap created");
                Check(mini.Index == 2090, "case1 big mode default");
                Check(mini.ToggleButton != null && mini.BigMapButton != null, "case1 buttons exist");
                Check(scene.DuraStatusPanel != null, "case1 durastatus seam filled");
            }

            // ===== case2 ToggleButton → 大/小档切换（2090↔2091）双向 =====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var mini = scene.MiniMapDialog;
                mini.ToggleButton.InvokeMouseClick(null); // Toggle → _fade 1→0 → SetSmallMode
                Check(mini.Index == 2091, "case2 toggle to small");
                mini.ToggleButton.InvokeMouseClick(null); // _fade==0 → SetBigMode
                Check(mini.Index == 2090, "case2 toggle back to big");
            }

            // ===== case3 Process 每帧刷地图名 + 坐标 =====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var mini = scene.MiniMapDialog;
                mini.Process();
                Check(mini.MapNameLabel.Text == "测试地图", "case3 map name");
                Check(mini.LocationLabel.Text == "1, 1", "case3 location");
            }

            // ===== case4 BigMapButton → BigMapDialog.Toggle（隐藏→显示）=====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var mini = scene.MiniMapDialog;
                Check(!scene.BigMapDialog.Visible, "case4 bigmap hidden");
                mini.BigMapButton.InvokeMouseClick(null);
                Check(scene.BigMapDialog.Visible, "case4 bigmap opened");
                mini.BigMapButton.InvokeMouseClick(null);
                Check(!scene.BigMapDialog.Visible, "case4 bigmap closed");
            }

            // ===== case5 MiniMap_BeforeDraw 档位自适应：MiniMap=0 强切小档、>0 保持大档 =====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var mini = scene.MiniMapDialog;
                mc.MiniMap = 0; // 地图无小地图 → 强制小档
                mini.Draw();    // BeforeDraw → SetSmallMode
                Check(mini.Index == 2091, "case5 no-minimap force small");
                mc.MiniMap = 1; // 有图集 + _bigMode=true + 当前小档帧 → 档位校正回大档（SetBigMode）
                mini.Draw();
                Check(mini.Index == 2090, "case5 minimap adapt to big intent");
            }

            // ===== case6 大档下 Process + 渲染路径不崩（Index=2090 走 mmap 视口计算）=====
            {
                var mc = NewScene();
                var scene = GameScene.Scene;
                var mini = scene.MiniMapDialog;
                mini.Process();
                mini.Draw(); // 大档渲染路径：GetSize 空库 0x0 → scale 0，viewRect 裁剪，不抛
                Check(mini.Index == 2090, "case6 big mode draw stable");
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

            if (_fail == 0)
            {
                Console.WriteLine("[minimapverify] PASS cases=6");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[minimapverify] FAIL cases=6 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
