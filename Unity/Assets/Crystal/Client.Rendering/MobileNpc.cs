using Client;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Crystal.Client.Core.MirMath;
using C = ClientPackets;
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Client.Rendering
{
    // 阶段8 第3项 增量1：NPC 对话触控控制器（纯逻辑层，仿 MobilePickup）。
    // 地图 tap（ui 空间）→ 屏转格（MobilePickup.ScreenToTile 同源）→ 最近 NPCObject（≤TapRadius）
    // 命中：置 GameScene.NPCID（运行时无他处设值，NPCDialog.ButtonClicked 选项点击依赖它）+ 发
    // C.CallNPC{ObjectID, Key="[@Main]"} 拉对话首页（对齐旧客户端 GameScene NPC 点击链）。
    // 对照决策：点击节流用独立 _lastCallAt（CallCooldownMs=5000），不共享 GameScene.NPCTime——
    // 旧客户端共享计时会在开框后吞掉首个选项点击（quirk），触控版让选项（ButtonClicked 走 NPCTime）即点即响。
    // 对话框已开（Visible）不重弹，对齐旧客户端 `Dialog.Visible 时点击 NPC 无效`。
    // 与 Unity Input/渲染解耦（依赖静态 seam：MapControl.ObjectsList/MapObject.User/GameScene.Scene/
    // GameScene.NPCID/Network/CMain.Time），探针（NpcVerify）构造虚拟 NPC+网格断言各态。
    public sealed class MobileNpc
    {
        public const int TapRadius = 1;      // tap 命中半径（格，tap 落格与 NPC 格距离）
        const long CallCooldownMs = 5000;    // 拉首页节流（对齐旧客户端 NPCTime+5000；独立计时）

        // 动作注入（探针替换捕获 C.CallNPC，网络层未连接时 Enqueue 静默丢弃无法断言）。
        public static System.Action<C.CallNPC> SendCallNpc = p => Network.Enqueue(p);

        // long.MinValue 哨兵：首点永不节流（CMain.Time 启动值低于 5000 时不被误吞）；
        // 用 `== long.MinValue` 短路，避免 MinValue 参与减法溢出为负。
        long _lastCallAt = long.MinValue;

        // 地图 tap（ui 空间）：屏→格找最近 NPCObject（≤TapRadius）；命中发 C.CallNPC[@Main]
        // 返回 true（消费，拾取让位）；否则 false（无 NPC/对话框已开，落回拾取）。
        public bool TapAt(MapControl mc, MPoint ui)
        {
            if (mc == null || MapObject.User == null) return false;
            var scene = GameScene.Scene;
            if (scene != null && scene.NPCDialog != null && scene.NPCDialog.Visible) return false;

            MPoint tile = MobilePickup.ScreenToTile(mc, ui);
            NPCObject best = null;
            int bestDist = int.MaxValue;
            foreach (var ob in MapControl.ObjectsList)
            {
                if (ob is not NPCObject npc) continue;
                int d = global::Client.MirObjects.Functions.MaxDistance(npc.CurrentLocation, tile);
                if (d > TapRadius) continue;
                if (d < bestDist) { best = npc; bestDist = d; }
            }
            if (best == null) return false;

            long now = CMain.Time;
            if (_lastCallAt == long.MinValue || now - _lastCallAt >= CallCooldownMs)
            {
                _lastCallAt = now;
                GameScene.NPCID = best.ObjectID; // 选项点击（NPCDialog.ButtonClicked）以它为对象
                SendCallNpc(new C.CallNPC { ObjectID = best.ObjectID, Key = "[@Main]" });
            }
            return true; // 命中 NPC 即消费：即便在节流内也不落回拾取（对齐旧客户端点击 NPC 不触发他事）
        }
    }
}
