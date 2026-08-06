namespace Crystal.Client.Core.MirMath
{
    // System.Drawing.Size 的纯 C# 等价物：对象模型仅访问 Width/Height。
    public struct Size
    {
        public static readonly Size Empty;

        public int Width { get; set; }
        public int Height { get; set; }

        public Size(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public static bool operator ==(Size a, Size b) { return a.Width == b.Width && a.Height == b.Height; }
        public static bool operator !=(Size a, Size b) { return !(a == b); }
        public override bool Equals(object obj) { return obj is Size s && this == s; }
        public override int GetHashCode() { return (Width * 397) ^ Height; }
    }
}
