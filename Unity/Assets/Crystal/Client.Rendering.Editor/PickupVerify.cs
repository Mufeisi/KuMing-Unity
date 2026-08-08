using System;
using System.Collections.Generic;
using Client;
using Client.MirObjects;
using Client.MirScenes;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第2项 增量5 地面拾取控制器纯逻辑验证（无服务器）：
    // 地图 tap 屏→格找最近 ItemObject 设目标（命中/邻格命中/无物品/超拾取半径拒绝）；
    // 相邻即 C.PickUp（节流 200ms，冷却后可重发）；目标被拾取移除（S.ObjectRemove → item.Remove()）→ 自动清目标；
    // 不相邻 PathFinder 逐格 C.Walk 走位到格后拾取。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.PickupVerify.Run -quit
    // 断言：全过输出 [pickupverify] PASS exit 0。
    public static class PickupVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[pickupverify] FAIL {what}"); }
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

        // ItemObject ctor 自动注册 Objects+ObjectsList；仅需补位置字段。
        static ItemObject SpawnItem(uint id, int x, int y)
        {
            return new ItemObject(id)
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

        public static void Run()
        {
            var clock = new FakeClock();
            var walks = new List<MirDirection>();
            int pickups = 0;
            MobilePickup.Now = () => clock.Now;
            MobilePickup.SendWalk = d => walks.Add(d);
            MobilePickup.SendPickUp = () => pickups++;

            // ===== case1 地面物品命中（精确格 + 邻格 tap）=====
            {
                var mc = NewMap(10, 10, out var user);
                SpawnItem(101, 11, 10); // 玩家右侧 1 格
                var pickup = new MobilePickup();
                var ui = UiOf(mc, user, new MPoint(11, 10));
                Check(pickup.TapAt(mc, ui), "case1 tap hit item");
                Check(pickup.TargetId == 101, "case1 target id");
                Check(pickup.Active, "case1 active");

                // 邻格 tap（物品斜下格，tap 距=1 ≤ TapRadius）也命中。
                var pickup2 = new MobilePickup();
                var ui2 = UiOf(mc, user, new MPoint(11, 11));
                Check(pickup2.TapAt(mc, ui2), "case1 adjacent tap hits");
                Check(pickup2.TargetId == 101, "case1 adjacent target id");
            }

            // ===== case2 无物品：不设目标不发包 =====
            {
                var mc = NewMap(10, 10, out var user);
                var pickup = new MobilePickup();
                Check(!pickup.TapAt(mc, UiOf(mc, user, new MPoint(12, 10))), "case2 no item rejected");
                Check(pickup.TargetId == 0, "case2 target stays clear");
            }

            // ===== case3 超拾取半径拒绝（防误触远走）=====
            {
                var mc = NewMap(10, 10, out var user);
                SpawnItem(301, 15, 10); // reach=5 > PickupRadius=3
                var pickup = new MobilePickup();
                Check(!pickup.TapAt(mc, UiOf(mc, user, new MPoint(15, 10))), "case3 out-of-reach rejected");
                Check(pickup.TargetId == 0, "case3 target stays clear");
            }

            // ===== case4 相邻即拾取 + 节流 + 冷却后可重发 =====
            {
                var mc = NewMap(10, 10, out var user);
                SpawnItem(401, 11, 10);
                var pickup = new MobilePickup();
                clock.Now = 1000; walks.Clear(); pickups = 0; // 时钟从 1000 起：_lastWalkAt/_lastPickupAt=0 哨兵不被首步节流误吞
                pickup.TapAt(mc, UiOf(mc, user, new MPoint(11, 10)));
                pickup.Tick(); // 目标格 != 玩家格 → 走一格
                Check(walks.Count == 1 && walks[0] == MirDirection.Right, "case4 walk to item tile");
                user.Movement = new MPoint(11, 10);
                user.CurrentLocation = new MPoint(11, 10);
                pickup.Tick(); // 到格 → C.PickUp
                Check(pickups == 1, "case4 pickup sent on tile");
                pickup.Tick(); // 同刻节流 200ms
                Check(pickups == 1, "case4 pickup throttle same-tick");
                clock.Now = 1300; pickup.Tick(); // 冷却过期（>200ms），目标仍在（无回流）→ 重发
                Check(pickups == 2, "case4 pickup re-send after cooldown");
            }

            // ===== case5 目标被拾取移除（S.ObjectRemove）→ 自动清目标 =====
            {
                var mc = NewMap(10, 10, out var user);
                var item = SpawnItem(501, 11, 10);
                var pickup = new MobilePickup();
                clock.Now = 0; walks.Clear(); pickups = 0;
                pickup.TapAt(mc, UiOf(mc, user, new MPoint(11, 10)));
                item.Remove(); // 服务器确认拾取 → 从地图移除
                pickup.Tick();
                Check(pickup.TargetId == 0 && !pickup.Active, "case5 target cleared on removal");
                Check(walks.Count == 0 && pickups == 0, "case5 no walk/pickup after clear");
            }

            // ===== case6 不相邻寻路走位到格后拾取（两格）+ 走格节流 =====
            {
                var mc = NewMap(10, 10, out var user);
                SpawnItem(601, 12, 10); // 玩家右侧 2 格
                var pickup = new MobilePickup();
                clock.Now = 1000; walks.Clear(); pickups = 0;
                pickup.TapAt(mc, UiOf(mc, user, new MPoint(12, 10)));
                pickup.Tick(); // 首步
                Check(walks.Count == 1 && walks[0] == MirDirection.Right, "case6 first walk");
                pickup.Tick(); // 同刻节流 500ms
                Check(walks.Count == 1, "case6 walk throttle same-tick");
                clock.Now = 1500;
                user.Movement = new MPoint(11, 10); // 服务器确认到位
                user.CurrentLocation = new MPoint(11, 10);
                pickup.Tick(); // 推进到下一节点（12,10）
                Check(walks.Count == 2 && walks[1] == MirDirection.Right, "case6 second walk");
                clock.Now = 2000;
                user.Movement = new MPoint(12, 10);
                user.CurrentLocation = new MPoint(12, 10);
                pickup.Tick(); // 到格 → C.PickUp
                Check(pickups == 1, "case6 pickup on arrival");
                Check(walks.Count == 2, "case6 no extra walk on tile");
            }

            // 还原静态委托 + 全局 seam（防污染后续探针）。
            MobilePickup.Now = () => CMain.Time;
            MobilePickup.SendWalk = d => global::Client.MirNetwork.Network.Enqueue(new ClientPackets.Walk { Direction = d });
            MobilePickup.SendPickUp = () => global::Client.MirNetwork.Network.Enqueue(new ClientPackets.PickUp());
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.User = null;

            if (_fail == 0)
            {
                Console.WriteLine("[pickupverify] PASS cases=6");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[pickupverify] FAIL cases=6 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }

        class FakeClock
        {
            public long Now;
        }
    }
}
