namespace Crystal.Client.Core.MirMath
{
    // System.Drawing.Font/FontStyle 的平台无关替换：对象模型仅用 FontName + 字号 + 样式。
    // 真实字体渲染层落地时替换。FontStyle 位标志对齐 System.Drawing（Bold=1, Italic=2）。
    public enum FontStyle
    {
        Regular = 0,
        Bold = 1,
        Italic = 2
    }

    public class Font
    {
        public string Name { get; }
        public float Size { get; }
        public FontStyle Style { get; }

        public Font(string name, float size) : this(name, size, FontStyle.Regular) { }

        public Font(string name, float size, FontStyle style)
        {
            Name = name;
            Size = size;
            Style = style;
        }
    }
}
