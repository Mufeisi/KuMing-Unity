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
    // 阶段8 第2项 增量5：地面拾取触控控制器（纯逻辑层，仿 MobileCombat）。
    // 地图 tap（ui 空间）→ 屏转格 → 最近 ItemObject（≤TapRadius）设为目标；目标格==玩家格 →
    // C.PickUp（节流 PickupCooldownMs，对齐旧客户端 PickUpTime+200 同源）；否则 PathFinder 逐格
    // C.Walk 走位（物品格非 Blocking 可直达），到格即拾取。目标被拾取移除（S.ObjectRemove）→ 自动清目标。
    // 与摇杆/战斗互斥（点击 vs 移动判定）：摇杆任何移动即 Cancel 拾取目标（移动优先）；
    // 拾取目标激活时战斗自动索敌让位（MobileBootstrap 挂载时二选一）。
    // 与 Unity Input/渲染解耦（依赖静态 seam：MapControl.ObjectsList/MapObject.User/GameScene.Scene/
    // Network/CMain.Time），探针（PickupVerify）构造虚拟物品+网格断言各态。
    public sealed class MobilePickup
    {
        public const int TapRadius = 1;      // tap 命中半径（格，tap 落格与物品格距离）
        public const int PickupRadius = 3;   // 拾取半径（格，玩家与物品距离；超此拒绝——防误触远走）
        const long WalkIntervalMs = 500;     // 走格节流（与摇杆/战斗同频）
        const long PickupCooldownMs = 200;   // 拾取节流（旧客户端 PickUpTime+200 同源）
        const int ReplanEveryTicks = 20;     // 每 N 帧重寻路（目标路径阻塞兜底）

        // 时钟注入（探针替换）：默认 CMain.Time（毫秒）。
        public static System.Func<long> Now = () => CMain.Time;
        // 动作注入（探针替换捕获 C.Walk/C.PickUp，网络层未连接时 Enqueue 静默丢弃无法断言）。
        public static System.Action<MirDirection> SendWalk = d => Network.Enqueue(new C.Walk { Direction = d });
        public static System.Action SendPickUp = () => Network.Enqueue(new C.PickUp());

        MapObject _target;
        uint _targetId;
        List<Node> _path;
        int _pathIdx;
        int _ticks;
        long _lastWalkAt;
        long _lastPickupAt;

        public uint TargetId => _targetId;
        public bool Active => _target != null && MapControl.Objects.ContainsKey(_target.ObjectID);

        // 目标失效（被拾取移除/换图）判定。
        bool TargetValid => _target != null && MapControl.Objects.ContainsKey(_target.ObjectID);

        // 地图 tap（ui 空间）：屏→格找最近 ItemObject（≤TapRadius）且距玩家 ≤PickupRadius；
        // 命中设目标返回 true，否则 false（无物品/距离外，不发包）。
        public bool TapAt(MapControl mc, MPoint ui)
        {
            if (mc == null || MapObject.User == null) return false;
            MPoint tile = ScreenToTile(mc, ui);
            var user = MapObject.User;

            MapObject best = null;
            int bestDist = int.MaxValue;
            foreach (var ob in MapControl.ObjectsList)
            {
                if (ob is not ItemObject io) continue;
                int tap = global::Client.MirObjects.Functions.MaxDistance(io.CurrentLocation, tile);
                if (tap > TapRadius) continue;
                int reach = global::Client.MirObjects.Functions.MaxDistance(io.CurrentLocation, user.CurrentLocation);
                if (reach > PickupRadius) continue;
                if (tap < bestDist) { best = io; bestDist = tap; }
            }
            if (best == null) return false;
            _target = best;
            _targetId = best.ObjectID;
            _path = null; _pathIdx = 0;
            return true;
        }

        // 每帧驱动：目标在玩家格 → C.PickUp（节流）；否则寻路走位。
        public void Tick()
        {
            if (MapObject.User == null) return;
            if (!TargetValid) { Clear(); return; }
            var user = MapObject.User;

            _ticks++;
            if (_target.CurrentLocation == user.CurrentLocation)
            {
                _path = null; _pathIdx = 0;
                long now = Now();
                if (now - _lastPickupAt < PickupCooldownMs) return;
                _lastPickupAt = now;
                SendPickUp();
                return;
            }
            WalkToTarget(user);
        }

        // 走位：目标格非 Blocking（物品不占格）直接作终点寻路，逐格 C.Walk。
        void WalkToTarget(UserObject user)
        {
            var mc = GameScene.Scene != null ? GameScene.Scene.MapControl : null;
            if (mc == null) return;

            bool replan = _path == null || _pathIdx >= _path.Count || (_ticks % ReplanEveryTicks) == 0;
            if (replan)
            {
                var finder = mc.PathFinder ?? new PathFinder(mc);
                _path = finder.FindPath(user.CurrentLocation, _target.CurrentLocation);
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

        // 摇杆接管等外部打断：清目标（不移动）。
        public void Cancel() => Clear();

        void Clear()
        {
            _target = null;
            _targetId = 0;
            _path = null;
            _pathIdx = 0;
        }

        // 屏（ui 空间）→ 格：ItemObject.Process 世界→屏幕逆变换（忽略格内居中/偏移，TapRadius 内足够）。
        static MPoint ScreenToTile(MapControl mc, MPoint ui)
        {
            var user = MapObject.User;
            return new MPoint(
                ui.X / MapControl.CellWidth - MapControl.OffSetX + user.Movement.X,
                ui.Y / MapControl.CellHeight - MapControl.OffSetY + user.Movement.Y);
        }
    }
}
