using System.Collections.Generic;
using Client;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using C = ClientPackets;
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Client.Rendering
{
    // 阶段8 第4项 增量2：大地图视口自动寻路走位控制器（纯逻辑层，仿 MobilePickup）。
    // 大地图视口点击（TouchInputAdapter 同链路 OnMouseClick）已设 MapControl.CurrentPath + AutoPath=true
    // （旧客户端 GameScene.OnMouseClick 语义），但 Unity 端无 PC Process 行走驱动 → 本控制器逐瓦片
    // C.Walk（WalkIntervalMs=500，与摇杆/战斗/拾取同频）。每帧：AutoPath 时按路径终点重寻路（路径可能
    // 因移动而陈旧）、跳过玩家已站节点、向首节点走一步；路径空/重寻路失败 → AutoPath=false 结束。
    // 与摇杆/拾取互斥：任何新触摸 Down 或摇杆移动即 Cancel（移动优先，MobileBootstrap 接线）。
    public sealed class MobileAutoPath
    {
        const long WalkIntervalMs = 500;

        // 时钟/动作注入（探针替换；默认 Network.Enqueue，未连接时静默丢弃）。
        public static System.Func<long> Now = () => CMain.Time;
        public static System.Action<MirDirection> SendWalk = d => Network.Enqueue(new C.Walk { Direction = d });

        long _lastWalkAt;

        // 激活判定：MapControl.AutoPath 置位（视口点击设定）。
        public bool Active => GameScene.Scene?.MapControl != null && GameScene.Scene.MapControl.AutoPath;

        // 每帧驱动：重寻路 → 跳已站节点 → 节流走一步。
        public void Tick()
        {
            var mc = GameScene.Scene?.MapControl;
            if (mc == null || MapObject.User == null) return;
            if (!mc.AutoPath || mc.CurrentPath == null || mc.CurrentPath.Count == 0)
            {
                mc.AutoPath = false;
                return;
            }
            var user = MapObject.User;

            // 按路径终点重寻路（对齐旧客户端：移动后原路径节点陈旧，需重算）。
            var finder = mc.PathFinder ?? new PathFinder(mc);
            var path = finder.FindPath(user.CurrentLocation, mc.CurrentPath[mc.CurrentPath.Count - 1].Location);
            if (path == null || path.Count == 0)
            {
                mc.AutoPath = false;
                return;
            }
            mc.CurrentPath = path;

            // 跳过玩家已站节点（对齐旧客户端 currentNode 修剪语义，简化：等值跳过）。
            int idx = 0;
            while (idx < mc.CurrentPath.Count && mc.CurrentPath[idx].Location == user.CurrentLocation)
                idx++;
            if (idx >= mc.CurrentPath.Count)
            {
                mc.AutoPath = false;
                return;
            }

            long now = Now();
            if (now - _lastWalkAt < WalkIntervalMs) return;
            _lastWalkAt = now;

            SendWalk(global::Client.MirObjects.Functions.DirectionFromPoint(user.CurrentLocation, mc.CurrentPath[idx].Location));
        }

        // 摇杆接管/新触摸等外部打断：清自动寻路（不移动）。
        public void Cancel()
        {
            var mc = GameScene.Scene?.MapControl;
            if (mc == null) return;
            mc.AutoPath = false;
            mc.CurrentPath = null;
        }
    }
}
