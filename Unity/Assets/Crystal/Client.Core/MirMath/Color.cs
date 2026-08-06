namespace Crystal.Client.Core.MirMath
{
    // System.Drawing.Color 的平台无关替换：仅覆盖对象模型用到的静态具名色 + FromArgb。
    // 真实渲染层落地时按此契约转换。
    public struct Color
    {
        public static readonly Color Empty = new Color(0);

        public static Color White => new Color(255, 255, 255, 255);
        public static Color Black => new Color(255, 0, 0, 0);
        public static Color Gray => new Color(255, 128, 128, 128);
        public static Color Transparent => new Color(0, 255, 255, 255);
        public static Color Yellow => new Color(255, 255, 255, 0);
        public static Color Red => new Color(255, 255, 0, 0);
        public static Color Purple => new Color(255, 128, 0, 128);
        public static Color MediumVioletRed => new Color(255, 199, 21, 133);
        public static Color Green => new Color(255, 0, 128, 0);
        public static Color DarkRed => new Color(255, 139, 0, 0);
        public static Color DarkGreen => new Color(255, 0, 100, 0);
        public static Color Blue => new Color(255, 0, 0, 255);
        public static Color Orange => new Color(255, 255, 165, 0);
        public static Color DarkSeaGreen => new Color(255, 143, 188, 143);
        public static Color Firebrick => new Color(255, 178, 34, 34);
        public static Color Goldenrod => new Color(255, 218, 165, 32);
        public static Color DeepSkyBlue => new Color(255, 0, 191, 255);
        public static Color DarkGray => new Color(255, 64, 64, 64);
        public static Color Brown => new Color(255, 165, 42, 42);
        public static Color CornflowerBlue => new Color(255, 100, 149, 237);
        public static Color DarkBlue => new Color(255, 0, 0, 139);
        public static Color HotPink => new Color(255, 255, 105, 180);
        public static Color LimeGreen => new Color(255, 50, 205, 50);
        public static Color Lime => new Color(255, 0, 255, 0);
        public static Color LawnGreen => new Color(255, 124, 252, 0);
        public static Color Gold => new Color(255, 255, 215, 0);
        public static Color DimGray => new Color(255, 105, 105, 105);

        public byte A { get; }
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public int ToArgb() => (A << 24) | (R << 16) | (G << 8) | B;

        public Color(int argb)
        {
            A = (byte)((argb >> 24) & 0xFF);
            R = (byte)((argb >> 16) & 0xFF);
            G = (byte)((argb >> 8) & 0xFF);
            B = (byte)(argb & 0xFF);
        }

        public Color(int a, int r, int g, int b)
        {
            A = (byte)a;
            R = (byte)r;
            G = (byte)g;
            B = (byte)b;
        }

        public static Color FromArgb(int argb) => new Color(argb);
        public static Color FromArgb(int r, int g, int b) => new Color(255, r, g, b);
        public static Color FromArgb(int a, int r, int g, int b) => new Color(a, r, g, b);

        public static bool operator ==(Color left, Color right) => left.ToArgb() == right.ToArgb();
        public static bool operator !=(Color left, Color right) => !(left == right);

        public override bool Equals(object obj) => obj is Color c && this == c;
        public override int GetHashCode() => ToArgb();
        public override string ToString() => $"Color [A={A}, R={R}, G={G}, B={B}]";
    }
}
