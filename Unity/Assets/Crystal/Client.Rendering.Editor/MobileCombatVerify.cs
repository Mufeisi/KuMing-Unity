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
    // 阶段8 第1项（战斗触控 HUD）增量2 自动战斗控制器纯逻辑验证：
    // 喂虚拟地图/怪物/时钟断言 MobileCombat 索敌/半径过滤/死亡跳过/相邻攻击+冷却/寻路追击+走格节流/目标死亡重选。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.MobileCombatVerify.Run -quit
    // 断言：全过输出 [combatverify] PASS exit 0。
    public static class MobileCombatVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[combatverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Objects/ObjectsList/User/Scene），建 30x30 全空网格 + 玩家。
        static MapControl NewMap(int px, int py, out UserObject user)
        {
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            GameScene.Scene = null;

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

        static MonsterObject Spawn(MapControl mc, uint id, int x, int y)
        {
            var mo = new MonsterObject(id)
            {
                Movement = new MPoint(x, y),
                CurrentLocation = new MPoint(x, y),
                MapLocation = new MPoint(x, y),
            };
            MapControl.Objects[id] = mo;
            MapControl.ObjectsList.Add(mo);
            return mo;
        }

        public static void Run()
        {
            var clock = new FakeClock();

            // ===== case1 索敌选最近 =====
            {
                var mc = NewMap(10, 10, out var _);
                var a = Spawn(mc, 101, 11, 10); // dist 1
                Spawn(mc, 102, 13, 10);         // dist 3
                var combat = new MobileCombat();
                Check(combat.AcquireTarget(mc), "case1 acquire ok");
                Check(combat.TargetId == 101, "case1 nearest target");
            }

            // ===== case2 超半径不选 =====
            {
                var mc = NewMap(10, 10, out var _);
                Spawn(mc, 201, 21, 10); // dist 11 > 10
                var combat = new MobileCombat();
                Check(!combat.AcquireTarget(mc), "case2 out-of-range rejected");
            }

            // ===== case3 死亡/骷髅跳过 =====
            {
                var mc = NewMap(10, 10, out var _);
                var dead = Spawn(mc, 301, 11, 10); dead.Dead = true;
                var live = Spawn(mc, 302, 13, 10);
                var combat = new MobileCombat();
                Check(combat.AcquireTarget(mc), "case3 acquire ok");
                Check(combat.TargetId == 302, "case3 dead skipped");
                live.Skeleton = true;
                var combat2 = new MobileCombat();
                Check(!combat2.AcquireTarget(mc), "case3 skeleton skipped");
            }

            // ===== case4 相邻攻击 + 冷却 =====
            {
                var mc = NewMap(10, 10, out var _);
                Spawn(mc, 401, 11, 10); // 相邻
                var combat = new MobileCombat();
                var attacks = new List<MirDirection>();
                MobileCombat.SendAttack = d => attacks.Add(d);
                MobileCombat.Now = () => clock.Now;
                clock.Now = 0; GameScene.AttackTime = 0;
                combat.AcquireTarget(mc);
                combat.Tick();
                Check(attacks.Count == 1, "case4 attack sent");
                Check(attacks[0] == MirDirection.Right, "case4 attack dir right");
                combat.Tick(); // 同刻冷却中
                Check(attacks.Count == 1, "case4 cooldown blocks second");
                clock.Now = 700; combat.Tick(); // < 800ms 冷却
                Check(attacks.Count == 1, "case4 cooldown blocks early");
                clock.Now = 1000; combat.Tick();
                Check(attacks.Count == 2, "case4 cooldown expires");
            }

            // ===== case5 不相邻追击 + 走格节流 =====
            {
                var mc = NewMap(5, 5, out var _);
                Spawn(mc, 501, 8, 8);
                var combat = new MobileCombat();
                var walks = new List<MirDirection>();
                MobileCombat.SendWalk = d => walks.Add(d);
                MobileCombat.Now = () => clock.Now;
                clock.Now = 1000;
                combat.AcquireTarget(mc);
                combat.Tick();
                Check(walks.Count == 1, "case5 first walk sent");
                combat.Tick(); // 同刻节流 500ms
                Check(walks.Count == 1, "case5 walk throttle same-tick");
                clock.Now = 1600; combat.Tick();
                Check(walks.Count == 2, "case5 second walk after interval");
            }

            // ===== case6 目标死亡 → 重选/清空 =====
            {
                var mc = NewMap(10, 10, out var _);
                var a = Spawn(mc, 601, 11, 10);
                var b = Spawn(mc, 602, 12, 10);
                var combat = new MobileCombat();
                var attacks = new List<MirDirection>();
                MobileCombat.SendAttack = d => attacks.Add(d);
                MobileCombat.Now = () => clock.Now;
                clock.Now = 0; GameScene.AttackTime = 0;
                combat.AcquireTarget(mc);
                Check(combat.TargetId == 601, "case6 lock nearest");
                a.Dead = true;
                combat.Tick(); // 目标死亡 → 重选 b
                Check(combat.TargetId == 602, "case6 retarget after death");
                b.Dead = true;
                combat.Tick(); // 全死 → 清空
                Check(combat.TargetId == 0 && combat.Target == null, "case6 clear when none left");
            }

            // 还原静态委托（防污染后续测试）。global:: 规避 Crystal.Client 遮蔽全局 Client 命名空间。
            MobileCombat.Now = () => CMain.Time;
            MobileCombat.SendWalk = d => global::Client.MirNetwork.Network.Enqueue(new ClientPackets.Walk { Direction = d });
            MobileCombat.SendAttack = d => global::Client.MirNetwork.Network.Enqueue(new ClientPackets.Attack { Direction = d, Spell = Spell.None });

            if (_fail == 0)
            {
                Console.WriteLine("[combatverify] PASS cases=6");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[combatverify] FAIL cases=6 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }

        class FakeClock
        {
            public long Now;
        }
    }
}
