namespace Crystal.Client.Core.MirMath
{
    // System.Drawing.Rectangle 的纯 C# 等价物：对象模型用到的 X/Y/Width/Height/Location/Contains + 两种 ctor。
    public struct Rectangle
    {
        public static readonly Rectangle Empty;

        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public Point Location
        {
            get { return new Point(X, Y); }
            set { X = value.X; Y = value.Y; }
        }

        public Size Size
        {
            get { return new Size(Width, Height); }
            set { Width = value.Width; Height = value.Height; }
        }

        public Rectangle(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public Rectangle(Point location, Size size)
        {
            X = location.X;
            Y = location.Y;
            Width = size.Width;
            Height = size.Height;
        }

        public bool Contains(Point p)
        {
            return p.X >= X && p.X < X + Width && p.Y >= Y && p.Y < Y + Height;
        }

        public int Left { get { return X; } }
        public int Top { get { return Y; } }
        public int Right { get { return X + Width; } }
        public int Bottom { get { return Y + Height; } }
    }
}
