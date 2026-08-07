using System.Collections.Generic;
using Client;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Crystal.Client.Core.MirMath;
using C = ClientPackets;
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Client.Rendering
{
    // 阶段8 第1项（战斗触控 HUD）增量2：自动战斗控制器（纯逻辑层）。
    // 进图后自动索敌（半径 TargetRadius 内最近非死亡/非骷髅怪物）→ 不相邻 PathFinder 寻路逐格 C.Walk 追击
    // → 相邻（InRange 1）C.Attack 普攻（节流 GameScene.AttackTime）。目标死亡/消失自动重选。
    // 与 Unity Input/渲染解耦（依赖静态 seam：MapControl.ObjectsList/MapObject.User/GameScene.Scene/
    // Network/CMain.Time），探针（MobileCombatVerify）构造虚拟怪物+网格断言四态。
    public sealed class MobileCombat
    {
        public const int TargetRadius = 10;    // 索敌半径（格）
        const long WalkIntervalMs = 500;       // 走格节流（与摇杆同频）
        const long AttackCooldownMs = 800;     // 攻击冷却（服务器 Player.Attack 有 retry 节流，此为 UX 层）
        const int ReplanEveryTicks = 20;       // 每 N 帧重寻路（目标移动/路径阻塞兜底）

        // 时钟注入（探针替换）：默认 CMain.Time（毫秒）。
        public static System.Func<long> Now = () => CMain.Time;
        // 动作注入（探针替换捕获 C.Walk/C.Attack，网络层未连接时 Enqueue 静默丢弃无法断言）。
        public static System.Action<MirDirection> SendWalk = d => Network.Enqueue(new C.Walk { Direction = d });
        public static System.Action<MirDirection> SendAttack = d => Network.Enqueue(new C.Attack { Direction = d, Spell = Spell.None });

        MapObject _target;
        uint _targetId;
        List<Node> _path;
        int _pathIdx;
        int _ticks;
        long _lastWalkAt;

        public uint TargetId => _targetId;
        public MapObject Target => _target;
        public bool HasPath => _path != null && _pathIdx < _path.Count;

        // 目标失效（死亡/被移除/换图）判定。
        bool TargetValid => _target != null && !_target.Dead && MapControl.Objects.ContainsKey(_target.ObjectID);

        // 每帧驱动：目标失效重选，相邻攻击，否则寻路追击。
        public void Tick()
        {
            if (GameSession.State != GameSessionState.InGame || MapObject.User == null) return;
            var mc = GameScene.Scene?.MapControl;
            if (mc == null) return;
            var user = MapObject.User;

            if (!TargetValid)
            {
                _target = null; _targetId = 0; _path = null; _pathIdx = 0;
                if (!AcquireTarget(mc)) return; // 无怪可打
            }

            _ticks++;
            if (global::Client.MirObjects.Functions.InRange(_target.CurrentLocation, user.CurrentLocation, 1))
            {
                _path = null; _pathIdx = 0;
                AttackTarget(user);
                return;
            }
            ChaseTarget(mc, user);
        }

        // 索敌：半径内最近非死亡怪物（Skeleton=已死尸体，跳过）。
        public bool AcquireTarget(MapControl mc)
        {
            MapObject best = null;
            int bestDist = int.MaxValue;
            foreach (var ob in MapControl.ObjectsList)
            {
                if (ob is not MonsterObject mo || mo.Dead || mo.Skeleton) continue;
                int d = global::Client.MirObjects.Functions.MaxDistance(mo.CurrentLocation, MapObject.User.CurrentLocation);
                if (d <= TargetRadius && d < bestDist) { best = mo; bestDist = d; }
            }
            if (best == null) return false;
            _target = best;
            _targetId = best.ObjectID;
            return true;
        }

        // 追击：目标 8 邻域最近可走格作终点寻路，逐格 C.Walk（下一格不可达自动重选）。
        void ChaseTarget(MapControl mc, UserObject user)
        {
            bool replan = _path == null || _pathIdx >= _path.Count || (_ticks % ReplanEveryTicks) == 0;
            if (replan)
            {
                MPoint dest = FindApproachCell(mc, _target.CurrentLocation);
                if (dest.IsEmpty)
                {
                    _target = null; _targetId = 0; _path = null; _pathIdx = 0; // 贴墙不可及，换目标
                    return;
                }
                var finder = mc.PathFinder ?? new PathFinder(mc);
                _path = finder.FindPath(user.CurrentLocation, dest);
                _pathIdx = _path != null ? 1 : 0; // 0=起点，从下一节点走
            }

            while (_path != null && _pathIdx < _path.Count && _path[_pathIdx].Location == user.CurrentLocation)
                _pathIdx++; // 已站在下一节点则推进

            if (_path == null || _pathIdx >= _path.Count) return;

            long now = Now();
            if (now - _lastWalkAt < WalkIntervalMs) return;
            _lastWalkAt = now;

            SendWalk(global::Client.MirObjects.Functions.DirectionFromPoint(user.CurrentLocation, _path[_pathIdx].Location));
            _pathIdx++;
        }

        // 目标相邻（1 格）最近可走格：目标本身 Blocking 不可直达，A* 终点须在邻域。
        MPoint FindApproachCell(MapControl mc, MPoint target)
        {
            var user = MapObject.User;
            MPoint best = MPoint.Empty;
            int bestDist = int.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                MPoint pt = global::Client.MirObjects.Functions.PointMove(target, (MirDirection)i, 1);
                if (!mc.EmptyCell(pt)) continue;
                int dist = global::Client.MirObjects.Functions.MaxDistance(pt, user.CurrentLocation);
                if (dist < bestDist) { best = pt; bestDist = dist; }
            }
            return best;
        }

        // 近距普攻（对齐 old client 近距攻击1：C.Attack + AttackTime 节流）。
        void AttackTarget(UserObject user)
        {
            long now = Now();
            if (now < GameScene.AttackTime) return;
            SendAttack(global::Client.MirObjects.Functions.DirectionFromPoint(user.CurrentLocation, _target.CurrentLocation));
            GameScene.AttackTime = now + AttackCooldownMs;
        }
    }
}
