using System;
using Crystal.Client.Core.MirMath;
using Client.MirControls;

namespace Client.MirGraphics
{
    // GDI+ Graphics 的 Client.Core seam（占位）：仅作为 TextRenderer.MeasureText 的上下文标记。
    public sealed class Graphics
    {
    }

    // TextFormatFlags 的 Client.Core seam：数值对齐 System.Windows.Forms.TextFormatFlags。
    public enum TextFormatFlags
    {
        Default = 0,
        Left = 0x100,          // 与 WinForms 一致：Left 与 NoClipping 同值
        HorizontalCenter = 0x1,
        Right = 0x2,
        VerticalCenter = 0x4,
        Bottom = 0x8,
        WordBreak = 0x10,
        SingleLine = 0x20,
        ExpandTabs = 0x40,
        NoClipping = 0x100,
        NoPrefix = 0x800,
        TextBoxControl = 0x2000,
        RightToLeft = 0x20000,   // 商城价格标签（迭代包9）右对齐布局用，数值对齐 WinForms
    }

    // TextRenderer 的 Client.Core seam：测量为确定性占位（渲染层注入 MeasureImpl 后按动态字体度量），
    // 字形/背景/光标三类绘制全部走可注入委托，Client.Core 保持 UnityEngine 无关。
    public static class TextRenderer
    {
        public static Func<Graphics, string, Font, Size> MeasureImpl;

        public static Size MeasureText(Graphics g, string text, Font font)
        {
            if (MeasureImpl != null)
                return MeasureImpl(g, text, font);
            return new Size(text.Length * 7, 14);
        }

        // 带排版区的测量（ChatLink 定位用，TextBoxControl 语义由渲染层处理）。
        public static Func<Graphics, string, Font, Size, TextFormatFlags, Size> MeasureImpl5;
        public static Size MeasureText(Graphics g, string text, Font font, Size proposedSize, TextFormatFlags format)
        {
            if (MeasureImpl5 != null)
                return MeasureImpl5(g, text, font, proposedSize, format);
            return MeasureText(g, text, font);
        }

        // 文本绘制：rect 为控件显示坐标内文本排版区，format 控制对齐/换行。渲染层注入 R8 动态字体实现。
        public static Action<MirControl, string, Font, Rectangle, Color, TextFormatFlags> DrawTextImpl;
        public static void DrawText(MirControl control, string text, Font font, Rectangle rect, Color colour, TextFormatFlags format)
        {
            if (DrawTextImpl != null)
                DrawTextImpl(control, text, font, rect, colour, format);
        }

        // 实心背景矩形（MirLabel/MirTextBox 的 BackColour 填充）。
        public static Action<MirControl, Rectangle, Color> FillBackgroundImpl;
        public static void FillBackground(MirControl control, Rectangle rect, Color colour)
        {
            if (FillBackgroundImpl != null)
                FillBackgroundImpl(control, rect, colour);
        }

        // 文本输入光标竖线（MirTextBox 焦点态）。
        public static Action<MirControl, Rectangle> DrawCaretImpl;
        public static void DrawCaret(MirControl control, Rectangle rect)
        {
            if (DrawCaretImpl != null)
                DrawCaretImpl(control, rect);
        }
    }
}
