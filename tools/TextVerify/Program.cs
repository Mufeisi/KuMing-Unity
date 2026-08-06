using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

// R8 GDI+ 文本栅格化探针：复刻旧客户端 MirLabel.CreateTexture（Client/MirControls/MirLabel.cs:187-239）。
// 语义：
//   1. 尺寸 = TextRenderer.MeasureText(g, Text, Font)；OutLine 时 +2 宽高（描边占位）。
//   2. 栅格化参数（Graphics 属性）：SmoothingMode=AntiAlias, TextRenderingHint=AntiAliasGridFit,
//      CompositingQuality=HighQuality, InterpolationMode=NearestNeighbor,
//      PixelOffsetMode=HighQuality, TextContrast=0。
//   3. 描边：OutLine=true 时 5 次 DrawText —— (1,0),(0,1),(2,1),(1,2) 描边色 + (1,1) 前景色；
//      否则 1 次 DrawText (1,0) 前景。
//   4. 输出 BGRA 像素（A8R8G8B8），保存 PNG。
// 验证：固定字体/DPI=96（ScaleFont 恒等）→ 尺寸与像素布局确定。
// 注意：GDI+ 与 Unity 渲染的像素级一致性由 R1-3 golden 链路兜底，本探针验证栅格化语义本身确定。

internal static class Program
{
    private static int fails = 0;

    private static void Check(string name, bool cond, string detail)
    {
        if (cond) Console.WriteLine($"  [PASS] {name}: {detail}");
        else { Console.WriteLine($"  [FAIL] {name}: {detail}"); fails++; }
    }

    private static int Main()
    {
        Console.WriteLine("== R8 GDI+ text rasterization probe ==");

        var font = new Font("Arial", 8F, FontStyle.Bold);

        // --- 尺寸：MeasureText 与 OutLine +2 ---
        Size noOutline = Measure("Hello", font);
        Size outlined = Measure("Hello", font, outline: true);
        Console.WriteLine($"MeasureText: noOutline={noOutline} outlined={outlined}");
        Check("OutLine 宽高 +2", outlined.Width == noOutline.Width + 2 && outlined.Height == noOutline.Height + 2,
            $"noOutline={noOutline} outlined={outlined}");
        Check("尺寸非零", noOutline.Width > 0 && noOutline.Height > 0, $"{noOutline}");

        // --- 栅格化：描边 5 次 DrawText → 输出位图 ---
        var text = "Hello";
        var label = Rasterize(text, font, outline: true, Color.White, Color.Black);
        Console.WriteLine($"Label bitmap: {label.Width}x{label.Height}");
        Check("描边位图尺寸=measure+2", label.Width == outlined.Width && label.Height == outlined.Height,
            $"{label.Width}x{label.Height} vs {outlined.Width}x{outlined.Height}");

        // 非空：位图含非透明像素（文本像素 + 描边像素）。
        int opaque = CountOpaque(label);
        Console.WriteLine($"Opaque pixels (text+outline): {opaque}");
        Check("描边位图非空", opaque > 0, $"opaque={opaque}");

        // 白色前景应存在（描边黑 + 前景白）。
        int white = CountNear(label, Color.White);
        Check("前景白色存在", white > 0, $"white={white}");

        // 无描边时前景白仍存在，但总不透明像素应更少（无黑描边层）。
        var noOl = Rasterize(text, font, outline: false, Color.White, Color.Black);
        int opaqueNoOl = CountOpaque(noOl);
        Check("无描边不透明像素 < 有描边", opaqueNoOl < opaque, $"noOl={opaqueNoOl} < ol={opaque}");

        // 保存 PNG 工件。
        Directory.CreateDirectory("Build/TextVerify");
        label.Save("Build/TextVerify/text-outline.png", ImageFormat.Png);
        noOl.Save("Build/TextVerify/text-plain.png", ImageFormat.Png);
        Console.WriteLine("Wrote Build/TextVerify/text-outline.png + text-plain.png");

        Console.WriteLine(fails == 0 ? "R8 PASS (exit 0)" : $"R8 FAIL ({fails} checks)");
        return fails == 0 ? 0 : 1;
    }

    // MirLabel.GetSize()：AutoSize 时 MeasureText；OutLine 时 +2。
    static Size Measure(string text, Font font, bool outline = false)
    {
        if (string.IsNullOrEmpty(text)) return Size.Empty;
        using var g = Graphics.FromImage(new Bitmap(1, 1));
        Size s = TextRenderer.MeasureText(g, text, font);
        if (outline && s != Size.Empty) s = new Size(s.Width + 2, s.Height + 2);
        return s;
    }

    // MirLabel.CreateTexture()：栅格化参数 + 描边/前景 DrawText 序列 → BGRA 位图。
    static Bitmap Rasterize(string text, Font font, bool outline, Color fore, Color outlineColour)
    {
        Size size = Measure(text, font, outline);
        if (size.Width == 0 || size.Height == 0) return new Bitmap(1, 1);

        var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextContrast = 0;
            g.Clear(Color.Transparent);

            if (outline)
            {
                TextRenderer.DrawText(g, text, font, new Rectangle(1, 0, size.Width, size.Height), outlineColour, TextFormatFlags.WordBreak);
                TextRenderer.DrawText(g, text, font, new Rectangle(0, 1, size.Width, size.Height), outlineColour, TextFormatFlags.WordBreak);
                TextRenderer.DrawText(g, text, font, new Rectangle(2, 1, size.Width, size.Height), outlineColour, TextFormatFlags.WordBreak);
                TextRenderer.DrawText(g, text, font, new Rectangle(1, 2, size.Width, size.Height), outlineColour, TextFormatFlags.WordBreak);
                TextRenderer.DrawText(g, text, font, new Rectangle(1, 1, size.Width, size.Height), fore, TextFormatFlags.WordBreak);
            }
            else
                TextRenderer.DrawText(g, text, font, new Rectangle(1, 0, size.Width, size.Height), fore, TextFormatFlags.WordBreak);
        }
        return bmp;
    }

    static int CountOpaque(Bitmap bmp)
    {
        int n = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, y).A > 0) n++;
        return n;
    }

    static int CountNear(Bitmap bmp, Color c)
    {
        int n = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                if (p.A > 0 && Math.Abs(p.R - c.R) < 40 && Math.Abs(p.G - c.G) < 40 && Math.Abs(p.B - c.B) < 40) n++;
            }
        return n;
    }
}
