using System.Security.Cryptography;
using System.Text;

namespace Crystal.AssetCompiler;

// golden-dir <outDir>：遍历 outDir 下所有 manifest，从图集页提取每图 RGBA，
// 写 SHA-256 侧车 <rel>.golden（行格式 "<index> <hex>"，跳过 Empty）。
// 复用一个图集解码路径（非 .Lib GZip 直解），产出供 Unity 运行时读取对照的独立 ground truth。
static class Golden
{
    internal static int Run(string[] args)
    {
        string outDir = Path.GetFullPath(args.Length > 1 ? args[1] : ".");
        var mans = Directory.EnumerateFiles(outDir, "*.json", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        Console.WriteLine($"golden {mans.Length} manifests from {outDir}");

        int ok = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var man in mans)
            if (WriteGolden(man)) ok++;
        sw.Stop();
        Console.WriteLine($"golden ok={ok} fail={mans.Length - ok}  {sw.Elapsed.TotalSeconds:F1}s");
        return ok == mans.Length ? 0 : 1;
    }

    // png-scan <png> <x0> <y0> <x1> <y1> [max]: 扫 (x0,y0)-(x1,y1) 区域找非零像素，输出前 max 个 (x,y) RGBA 十六进制。
    internal static int PngScan(string[] args)
    {
        if (args.Length < 6) { Console.WriteLine("usage: png-scan <png> <x0> <y0> <x1> <y1> [max]"); return 2; }
        var (w, h, rgba) = Program.ReadPng(File.ReadAllBytes(Path.GetFullPath(args[1])));
        int x0 = int.Parse(args[2]), y0 = int.Parse(args[3]), x1 = int.Parse(args[4]), y1 = int.Parse(args[5]);
        int max = args.Length > 6 ? int.Parse(args[6]) : 8;
        var sb = new StringBuilder($"png {w}x{h} region({x0},{y0})-({x1},{y1}) nonzero[");
        int found = 0;
        for (int y = y0; y < y1 && y < h; y++)
            for (int x = x0; x < x1 && x < w; x++)
            {
                int p = (y * w + x) * 4;
                if (rgba[p] == 0 && rgba[p + 1] == 0 && rgba[p + 2] == 0) continue;
                sb.Append($"({x},{y})={rgba[p]:X2}{rgba[p + 1]:X2}{rgba[p + 2]:X2}{rgba[p + 3]:X2},");
                if (++found >= max) break;
            }
        sb.Append(']');
        Console.WriteLine(sb);
        return 0;
    }

    // png-dump <png> <x> <y> <n>：读图集页，dump 区域左上 n×n 像素 RGBA 十六进制，供与 Unity GetPixels32 对照。
    internal static int PngDump(string[] args)
    {
        if (args.Length < 5) { Console.WriteLine("usage: png-dump <png> <x> <y> <n>"); return 2; }
        var (w, h, rgba) = Program.ReadPng(File.ReadAllBytes(Path.GetFullPath(args[1])));
        int x = int.Parse(args[2]), y = int.Parse(args[3]), n = int.Parse(args[4]);
        var sb = new StringBuilder($"png {w}x{h} region({x},{y}) [");
        for (int row = y; row < y + n && row < h; row++)
            for (int col = x; col < x + n && col < w; col++)
            {
                int p = (row * w + col) * 4;
                sb.Append($"{rgba[p]:X2}{rgba[p + 1]:X2}{rgba[p + 2]:X2}{rgba[p + 3]:X2},");
            }
        sb.Append(']');
        Console.WriteLine(sb);
        return 0;
    }

    static bool WriteGolden(string manPath)
    {
        string rel = Path.GetFileNameWithoutExtension(manPath);
        string dir = Path.GetDirectoryName(manPath);
        string outPath = Path.Combine(dir, rel + ".golden");
        try
        {
            var man = Program.LoadManifest(manPath);
            var pngCache = new Dictionary<string, (int w, int h, byte[] rgba)>();

            (int w, int h, byte[] rgba) Load(int pageIdx)
            {
                var page = man.Pages[pageIdx];
                if (!pngCache.TryGetValue(page.Name, out var v))
                {
                    v = Program.ReadPng(File.ReadAllBytes(Path.Combine(dir, page.Name)));
                    pngCache[page.Name] = v;
                }
                return v;
            }

            var sb = new StringBuilder(man.Count * 72);
            int n = 0;
            for (int i = 0; i < man.Images.Count; i++)
            {
                var e = man.Images[i];
                if (e.Empty) continue;
                var (pw, _, pg) = Load(e.Page);
                var rgba = new byte[e.W * e.H * 4];
                for (int row = 0; row < e.H; row++)
                    Buffer.BlockCopy(pg, ((e.Y + row) * pw + e.X) * 4, rgba, row * e.W * 4, e.W * 4);
                sb.Append(i).Append(' ').AppendLine(Convert.ToHexString(SHA256.HashData(rgba)));
                n++;
            }
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"  {rel}: {n} goldens");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {rel}: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }
}
