using Crystal.Client.Core.MirMath;

namespace Client.MirObjects
{
    // Shared.Functions 的点类型适配层：Shared 编译于 netstandard（Point=System.Drawing.Point），
    // 而 Client.Core 全局别名 Point=Crystal.Client.Core.MirMath.Point。
    // 本类置于 Client.MirObjects 命名空间，使对象模型逐字代码中的 Functions.X 解析到此，
    // 内部委托回 Shared 的全局 Functions（本地类不在全局命名空间，故不遮蔽之）。
    // 仅覆盖对象模型实际用到的 Point 相关成员；其余 Functions 成员按需补充。
    public static class Functions
    {
        public static int MaxDistance(Point p1, Point p2)
        {
            return global::Functions.MaxDistance(new System.Drawing.Point(p1.X, p1.Y), new System.Drawing.Point(p2.X, p2.Y));
        }

        public static MirDirection DirectionFromPoint(Point source, Point dest)
        {
            return global::Functions.DirectionFromPoint(new System.Drawing.Point(source.X, source.Y), new System.Drawing.Point(dest.X, dest.Y));
        }

        public static Point PointMove(Point p, MirDirection d, int i)
        {
            var r = global::Functions.PointMove(new System.Drawing.Point(p.X, p.Y), d, i);
            return new Point(r.X, r.Y);
        }

        public static MirDirection ReverseDirection(MirDirection dir)
        {
            return global::Functions.ReverseDirection(dir);
        }

        public static ItemInfo GetRealItem(ItemInfo Origin, ushort Level, MirClass job, List<ItemInfo> ItemList)
        {
            return global::Functions.GetRealItem(Origin, Level, job, ItemList);
        }
    }
}
