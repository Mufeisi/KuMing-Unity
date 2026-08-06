using Client.MirObjects;
using Crystal.Client.Core.MirMath;

namespace Client.MirScenes
{
    // Crystal.Client.Core 的 MapControl seam（占位）：满足 PathFinder/MapObject/Effect 的最小契约。
    // 真实 MapControl 落地时替换，静态成员（Objects/ObjectsList/CellWidth 等）契约保持。
    public class MapControl
    {
        public static Dictionary<uint, MapObject> Objects = new Dictionary<uint, MapObject>();
        public static List<MapObject> ObjectsList = new List<MapObject>();
        public static List<Effect> Effects = new List<Effect>();

        public const int CellWidth = 48;
        public const int CellHeight = 32;

        public static int OffSetX;
        public static int OffSetY;

        public static Point MapLocation;

        public int Width, Height;
        public bool TextureValid;
        public bool FloorValid;

        public CellInfo[,] M2CellInfo;

        // 大地图/小地图/寻路字段（BigMapDialog/MiniMapDialog/PathFinder seam，旧客户端 MapControl 同源）。
        public static UserObject User;
        public int Index;
        public int MiniMap;
        public int BigMap;
        public string Title;
        public PathFinder PathFinder;
        public List<Node> CurrentPath;
        public bool AutoPath;

        public static long InputDelay;
        public static long NextAction;

        // 忠实移植旧客户端 GameScene.EmptyCell（GameScene.cs:12074）：BackImage 0x20000000 / FrontImage 0x8000
        // 阻塞位 + 占格 Blocking 对象。PathFinder.Node.Walkable 唯一调用方（真实地图寻路依赖）。加边界/空守卫防越界。
        public bool EmptyCell(Point p)
        {
            if (M2CellInfo == null || !InBounds(p)) return true;
            if ((M2CellInfo[p.X, p.Y].BackImage & 0x20000000) != 0 || (M2CellInfo[p.X, p.Y].FrontImage & 0x8000) != 0)
                return false;
            foreach (var ob in Objects.Values)
                if (ob.CurrentLocation == p && ob.Blocking)
                    return false;
            return true;
        }

        public bool ValidPoint(Point p) { return true; }
        public bool HasTarget(Point p) { return false; }
        public bool CanHalfMoon(Point p, MirDirection d) { return false; }
        public bool CanCrossHalfMoon(Point p) { return false; }

        public void RemoveObject(MapObject ob)
        {
            if (M2CellInfo == null || !InBounds(ob.MapLocation)) return;
            M2CellInfo[ob.MapLocation.X, ob.MapLocation.Y].RemoveObject(ob);
        }
        public void AddObject(MapObject ob)
        {
            if (M2CellInfo == null || !InBounds(ob.MapLocation)) return;
            M2CellInfo[ob.MapLocation.X, ob.MapLocation.Y].AddObject(ob);
        }
        public void SortObject(MapObject ob)
        {
            if (M2CellInfo == null || !InBounds(ob.MapLocation)) return;
            M2CellInfo[ob.MapLocation.X, ob.MapLocation.Y].Sort();
        }

        bool InBounds(Point p)
        {
            return p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
        }

        public static MapObject GetObject(uint objectID)
        {
            return Objects.TryGetValue(objectID, out var obj) ? obj : null;
        }

        public static int Direction16(Point source, Point destination) { return 0; }
    }
}
