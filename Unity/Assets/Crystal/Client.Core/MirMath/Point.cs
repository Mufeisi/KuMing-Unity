namespace Crystal.Client.Core.MirMath
{
    // System.Drawing.Point 的纯 C# 等价物，API 对齐以便旧代码零改动迁移。
    public struct Point
    {
        public static readonly Point Empty;

        public int X { get; set; }
        public int Y { get; set; }

        public bool IsEmpty { get { return X == 0 && Y == 0; } }

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public void Offset(int dx, int dy)
        {
            X += dx;
            Y += dy;
        }

        public void Offset(Point p)
        {
            X += p.X;
            Y += p.Y;
        }

        public Point Add(Point p)
        {
            return new Point(X + p.X, Y + p.Y);
        }

        public Point Subtract(Point p)
        {
            return new Point(X - p.X, Y - p.Y);
        }

        public static bool operator ==(Point a, Point b) { return a.X == b.X && a.Y == b.Y; }
        public static bool operator !=(Point a, Point b) { return !(a == b); }

        public override bool Equals(object obj)
        {
            if (!(obj is Point)) return false;
            return this == (Point)obj;
        }

        public override int GetHashCode() { return X ^ Y; }

        public override string ToString() { return string.Format("{{X={0},Y={1}}}", X, Y); }
    }
}
