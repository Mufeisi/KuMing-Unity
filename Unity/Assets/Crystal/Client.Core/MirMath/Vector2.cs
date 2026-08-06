namespace Crystal.Client.Core.MirMath
{
    // SlimDX.Vector2 的纯 C# 等价物：粒子系统用浮点位置/速度。
    public struct Vector2
    {
        public float X;
        public float Y;

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vector2 Zero = new Vector2(0f, 0f);

        public static bool operator ==(Vector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);

        public override bool Equals(object obj) => obj is Vector2 o && this == o;
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2);
        public override string ToString() => $"({X},{Y})";
    }
}
