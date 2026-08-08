using Client;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Crystal.Client.Core.MirMath;
using C = ClientPackets;
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Client.Rendering
{
    // 阶段8 第7项：交易请求触控控制器（纯逻辑层，仿 MobileNpc）。
    // 地图 tap（ui 空间）→ 屏转格（MobilePickup.ScreenToTile 同源）→ 最近存活 PlayerObject
    // （≤TapRadius，非自己）命中 → 发 C.TradeRequest（节流 TradeCooldownMs，防误触连发）。
    // C.TradeRequest 无目标字段（服务器按玩家朝向解目标），客户端 tap 命中即视为朝对方发起。
    // 与 Unity Input/渲染解耦（依赖静态 seam：MapControl.ObjectsList/MapObject.User/Network/
    // CMain.Time），探针（TradeVerify）构造虚拟玩家+网格断言各态。
    public sealed class MobileTrade
    {
        public const int TapRadius = 1;      // tap 命中半径（格，同 MobileNpc）
        const long TradeCooldownMs = 3000;   // 交易请求节流（防 tap 连发刷屏）

        // 动作注入（探针替换捕获 C.TradeRequest，网络层未连接时 Enqueue 静默丢弃无法断言）。
        public static System.Action SendTradeRequest = () => Network.Enqueue(new C.TradeRequest());

        long _lastRequestAt = long.MinValue;

        // 地图 tap（ui 空间）：屏→格找最近存活非自己 PlayerObject（≤TapRadius）；命中发 C.TradeRequest
        // 返回 true（消费，拾取让位）；否则 false（无玩家，落回拾取）。
        public bool TapAt(MapControl mc, MPoint ui)
        {
            if (mc == null || MapObject.User == null) return false;

            MPoint tile = MobilePickup.ScreenToTile(mc, ui);
            uint self = MapObject.User.ObjectID;
            PlayerObject best = null;
            int bestDist = int.MaxValue;
            foreach (var ob in MapControl.ObjectsList)
            {
                if (ob is not PlayerObject po) continue;
                if (po.ObjectID == self || po.Dead) continue;
                int d = global::Client.MirObjects.Functions.MaxDistance(po.CurrentLocation, tile);
                if (d > TapRadius) continue;
                if (d < bestDist) { best = po; bestDist = d; }
            }
            if (best == null) return false;

            long now = CMain.Time;
            if (_lastRequestAt == long.MinValue || now - _lastRequestAt >= TradeCooldownMs)
            {
                _lastRequestAt = now;
                SendTradeRequest();
            }
            return true; // 命中玩家即消费：即便在节流内也不落回拾取（对齐 MobileNpc 同语义）
        }
    }
}
