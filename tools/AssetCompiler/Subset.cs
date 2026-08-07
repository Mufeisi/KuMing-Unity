using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PageFile = Crystal.AssetCompiler.Program.PageFile;
using LibManifest = Crystal.AssetCompiler.Program.LibManifest;
using ImageEntry = Crystal.AssetCompiler.Program.ImageEntry;
using ImgMeta = Crystal.AssetCompiler.Program.ImgMeta;
using MaskEntry = Crystal.AssetCompiler.Program.MaskEntry;

namespace Crystal.AssetCompiler;

// subset 子命令：已编译地图图集 → 按 .map 出生区域引用帧裁剪，重打包小图集。
// 动机：Android 模拟器 2GB RAM，nn0 全图引用帧 338MB RGBA 必然 OOM；中心区域子集 ~58MB 可行。
// 关键兼容：运行时 AtlasLibrary.Load 按 Images 数组位置 = 原图 index 索引进 Frames[i]（忽略 I 字段），
// 故新 json 的 Images 数组长度必须 = 原段 Count；选中帧保留 Page/X/Y（指向重打包页），
// 未选中项 Empty=true, Page=-1（不占纹理）。Frames 段原样保留（DrawTile 按原 index 索引进图）。
// 地图段无 mask 页（实测 _mask.png count=0）→ 不复制 mask，MaskPages 置空。
// self-verify：选中帧新图集 PNG 解码 vs 原图集逐字节比对。
static class Subset
{
    internal static int Run(string[] args)
    {
        string map = Path.GetFullPath(Program.Arg(args, "--map") ?? "");
        string atlas = Path.GetFullPath(Program.Arg(args, "--atlas") ?? "");
        string outDir = Path.GetFullPath(Program.Arg(args, "--out") ?? "");
        string center = Program.Arg(args, "--center") ?? "0,0";
        int radius = int.Parse(Program.Arg(args, "--radius") ?? "60");
        int page = int.Parse(Program.Arg(args, "--page") ?? "4096");
        if (!File.Exists(map)) { Console.WriteLine($"FAIL: map missing {map}"); return 2; }
        if (!Directory.Exists(atlas)) { Console.WriteLine($"FAIL: atlas dir missing {atlas}"); return 2; }
        var (cx, cy) = ParseCenter(center);

        // 1. 解析 .map，收集中心区域 (libIndex → 帧 index 集)；同帧多重引用合并。
        var refs = CollectReferences(map, cx, cy, radius);
        int total = refs.Values.Sum(s => s.Count);
        Console.WriteLine($"map={map} center={cx},{cy} radius={radius} -> {refs.Count} libs, {total} frame refs");

        // 2. 逐 rel 裁剪
        int okLibs = 0, badLibs = 0, selected = 0, selectedRgba = 0;
        foreach (var kv in refs.OrderBy(k => k.Key))
        {
            string rel = MapLibRel(kv.Key);
            if (rel == null) { Console.WriteLine($"  skip libIndex {kv.Key}: unmapped"); badLibs++; continue; }
            var res = SubsetLib(rel, kv.Value, atlas, outDir, page);
            if (res.ok) { okLibs++; selected += res.frames; selectedRgba += res.rgba; }
            else badLibs++;
        }
        Console.WriteLine($"subset ok={okLibs} fail={badLibs} frames={selected} rgbaMB={selectedRgba / 1048576.0:0.0}");
        return badLibs == 0 ? 0 : 1;
    }

    // ---------- 地图引用收集（Type100 主格式，8 字节头 + W*H*26） ----------
    // 复刻 GameRenderer.DrawMapTiles 可见性条件与 index 计算，得到区域渲染所需的最小帧集。
    static Dictionary<int, HashSet<int>> CollectReferences(string mapPath, int cx, int cy, int radius)
    {
        byte[] b = File.ReadAllBytes(mapPath);
        if (b.Length < 8 || b[0] != 1 || b[1] != 0)
        { Console.WriteLine($"FAIL: {mapPath} not Type100 v1"); return new Dictionary<int, HashSet<int>>(); }
        int w = BitConverter.ToInt16(b, 4);
        int h = BitConverter.ToInt16(b, 6);
        if (b.Length < 8 + (long)w * h * 26) { Console.WriteLine($"FAIL: {mapPath} truncated"); return new Dictionary<int, HashSet<int>>(); }
        Console.WriteLine($"  map {w}x{h}");

        var refs = new Dictionary<int, HashSet<int>>();
        void Add(int libIndex, int frameIndex)
        {
            if (libIndex < 0 || frameIndex < 0) return;
            if (!refs.TryGetValue(libIndex, out var set)) { set = new HashSet<int>(); refs[libIndex] = set; }
            set.Add(frameIndex);
        }

        long off = 8;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (Math.Abs(x - cx) > radius || Math.Abs(y - cy) > radius) { off += 26; continue; }
                short backIdx = BitConverter.ToInt16(b, (int)off);
                int backImg = BitConverter.ToInt32(b, (int)off + 2);
                short midIdx = BitConverter.ToInt16(b, (int)off + 6);
                short midImg = BitConverter.ToInt16(b, (int)off + 8);
                short frontIdx = BitConverter.ToInt16(b, (int)off + 10);
                short frontImg = BitConverter.ToInt16(b, (int)off + 12);
                off += 26;

                if (y % 2 == 0 && x % 2 == 0 && backImg != 0 && backIdx != -1)
                    Add(backIdx, (backImg & 0x1FFFFFFF) - 1);
                int mid = midImg - 1;
                if (mid >= 0 && midIdx != -1) Add(midIdx, mid);
                int fi = (frontImg & 0x7FFF) - 1;
                if (fi >= 0 && frontIdx != -1 && frontIdx != 200) Add(frontIdx, fi);
            }
        return refs;
    }

    // ---------- 单段裁剪 ----------
    static (bool ok, int frames, int rgba) SubsetLib(string rel, HashSet<int> want, string atlas, string outDir, int page)
    {
        string srcPath = Path.Combine(atlas, rel + ".json");
        if (!File.Exists(srcPath)) { Console.WriteLine($"  SKIP {rel}: manifest missing {srcPath}"); return (true, 0, 0); }
        var src = Program.LoadManifest(srcPath);
        int count = src.Images.Count;
        want.RemoveWhere(i => i < 0 || i >= count || src.Images[i].Empty);
        if (want.Count == 0) { Console.WriteLine($"  SKIP {rel}: 0 selected"); return (true, 0, 0); }

        // 提取选中帧 rgba（源页缓存按页索引；页文件与清单同目录）
        string srcDir = Path.Combine(atlas, Path.GetDirectoryName(rel));
        var pageCache = new Dictionary<int, (int w, int h, byte[] rgba)>();
        (int w, int h, byte[] rgba) LoadPage(int pi)
        {
            if (pageCache.TryGetValue(pi, out var v)) return v;
            var pg = src.Pages[pi];
            v = Program.ReadPng(File.ReadAllBytes(Path.Combine(srcDir, pg.Name)));
            pageCache[pi] = v;
            return v;
        }
        var rgba = new Dictionary<int, byte[]>();
        foreach (int i in want)
        {
            var e = src.Images[i];
            var (pw, _, pg) = LoadPage(e.Page);
            var pix = new byte[e.W * e.H * 4];
            for (int row = 0; row < e.H; row++)
                Buffer.BlockCopy(pg, ((e.Y + row) * pw + e.X) * 4, pix, row * e.W * 4, e.W * 4);
            rgba[i] = pix;
        }
        pageCache.Clear(); // 释放源页（超限图页可达 4800×3200）

        // 重打包：未选中帧 W=0 → Pack 跳过
        var metas = new ImgMeta[count];
        for (int i = 0; i < count; i++) metas[i] = new ImgMeta();
        foreach (int i in want) metas[i] = new ImgMeta { W = src.Images[i].W, H = src.Images[i].H };
        var anomalies = new List<string>();
        var (pages, rects) = Program.Pack(metas, page, anomalies);

        // 写新页
        string outRelDir = Path.Combine(outDir, Path.GetDirectoryName(rel));
        Directory.CreateDirectory(outRelDir);
        var pageFiles = new List<PageFile>();
        for (int p = 0; p < pages.Count; p++)
        {
            var pg = pages[p];
            var buf = new byte[pg.W * pg.H * 4];
            foreach (var (i, x, y) in pg.Items) Program.Blit(buf, pg.W, x, y, metas[i].W, metas[i].H, rgba[i]);
            string png = $"{Path.GetFileName(rel)}_p{p}.png";
            Program.WritePng(Path.Combine(outRelDir, png), pg.W, pg.H, buf);
            pageFiles.Add(new PageFile(png, pg.W, pg.H));
        }

        // 新清单：Images 长度 = 原 Count，位置 = 原 index
        var man = new LibManifest
        {
            Lib = rel,
            Version = src.Version,
            Count = count,
            PageSize = page,
            Pages = pageFiles,
            MaskPages = new List<PageFile>(),
            Images = Enumerable.Range(0, count).Select(i =>
            {
                if (!want.Contains(i)) return new ImageEntry { I = i, Empty = true, Page = -1 };
                var e = src.Images[i];
                var r = rects[i];
                return new ImageEntry
                {
                    I = i, Empty = false,
                    W = e.W, H = e.H, OX = e.OX, OY = e.OY, SX = e.SX, SY = e.SY, Shadow = e.Shadow,
                    Page = r.Page, X = r.X, Y = r.Y,
                };
            }).ToList(),
            Frames = src.Frames,
        };
        string outJson = Path.Combine(outDir, rel + ".json");
        File.WriteAllText(outJson, JsonSerializer.Serialize(man, new JsonSerializerOptions { WriteIndented = true }));

        // self-verify：选中帧新图集 PNG 解码 vs 源图集逐字节比对
        int bad = 0;
        var newPageCache = new Dictionary<int, (int w, int h, byte[] rgba)>();
        foreach (int i in want)
        {
            var e = man.Images[i];
            if (!newPageCache.TryGetValue(e.Page, out var np)) { np = Program.ReadPng(File.ReadAllBytes(Path.Combine(outRelDir, pageFiles[e.Page].Name))); newPageCache[e.Page] = np; }
            var got = new byte[e.W * e.H * 4];
            for (int row = 0; row < e.H; row++)
                Buffer.BlockCopy(np.rgba, ((e.Y + row) * np.w + e.X) * 4, got, row * e.W * 4, e.W * 4);
            if (!rgba[i].AsSpan().SequenceEqual(got)) { bad++; if (bad <= 5) Console.WriteLine($"  MISMATCH {rel} img {i}"); }
        }
        Console.WriteLine($"  {rel}: {want.Count}/{count} frames, {pages.Count} pages, {pageFiles.Sum(f => (long)f.W * f.H * 4) / 1048576.0:0.0}MB" + (bad > 0 ? $"  VERIFY FAIL x{bad}" : ""));
        return (bad == 0, want.Count, pageFiles.Sum(f => f.W * f.H * 4));
    }

    static (int, int) ParseCenter(string s)
    {
        var p = s.Split(',');
        return (int.Parse(p[0].Trim()), int.Parse(p[1].Trim()));
    }

    // ---------- libIndex → rel 段映射（GameRenderer.MapLibRel 移植） ----------
    static readonly string[] Mir3Names =
    {
        "Tilesc", "Tiles30c", "Tiles5c", "Smtilesc", "Housesc", "Cliffsc",
        "Dungeonsc", "Innersc", "Furnituresc", "Wallsc", "smObjectsc", "Animationsc", "Object1c", "Object2c"
    };
    static readonly string[] Mir3Subs = { "", "Wood/", "Sand/", "Snow/", "Forest/" };

    internal static string MapLibRel(int idx)
    {
        if (idx == 0) return "WemadeMir2/Tiles";
        if (idx == 1) return "WemadeMir2/SmTiles";
        if (idx == 2) return "WemadeMir2/Objects";
        if (idx >= 3 && idx <= 27) return "WemadeMir2/Objects" + (idx - 1); // 3→Objects2 ... 27→Objects26
        if (idx == 90) return "WemadeMir2/Objects_32bit";
        if (idx == 100) return "ShandaMir2/Tiles";
        if (idx >= 101 && idx <= 109) return "ShandaMir2/Tiles" + (idx - 99); // 101→Tiles2 ... 109→Tiles10
        if (idx == 110) return "ShandaMir2/SmTiles";
        if (idx >= 111 && idx <= 118) return "ShandaMir2/smTiles" + (idx - 109); // 源/产物命名 111→smTiles2 ... 118→smTiles9
        if (idx == 120) return "ShandaMir2/Objects";
        if (idx >= 121 && idx <= 150) return "ShandaMir2/Objects" + (idx - 119); // 121→Objects2 ... 150→Objects31
        if (idx == 190) return "ShandaMir2/AniTiles1";
        if (idx >= 200 && idx <= 273)
        {
            int i = (idx - 200) / 15;
            int off = (idx - 200) % 15;
            if (off < Mir3Names.Length) return "WemadeMir3/" + Mir3Subs[i] + Mir3Names[off];
        }
        return null;
    }
}
