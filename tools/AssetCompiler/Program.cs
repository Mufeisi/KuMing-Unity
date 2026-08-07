using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Shared;

namespace Crystal.AssetCompiler;

// .Lib v3 → 纹理图集 + JSON 元数据 + 帧表（阶段 3 资源管线入口）
// 解析逻辑与 R0 Spike 一致（Client/MirGraphics/MLibrary.cs 实证）：
//   MImage = [6×short + byte Shadow + int Length]=17B 基础头
//          + [4×short + int MaskLength]=12B mask 头（Shadow bit7=HasMask）
//   layer1/mask 数据均为 GZip(BGRA 4Bpp)，解压后截断到 W*H*4（尾部零填充忽略）
//   mask 布局按 MaskLength 排布（MLibrary.cs:928 读 Length 是原客户端 bug）
// 输出：每 .Lib 一至多个无损 PNG（RGBA，无预乘；常规 ≤pageSize²，超限图独占自然尺寸页如 mmap 4800×3200）+ <rel>.json 清单。
// compile 自带 self-verify：把图集 PNG 解码回来与直解逐字节比对，全等才退出 0。

static class Program
{
    // ---------- 子命令分发 ----------
    static int Main(string[] args)
    {
        if (args.Length < 1) { Usage(); return 2; }
        return args[0] switch
        {
            "compile" => Compile(args),
            "compile-dir" => CompileDir(args),
            "verify" => Verify(args),
            "verify-dir" => VerifyDir(args),
            "golden-dir" => Golden.Run(args),
            "png-dump" => Golden.PngDump(args),
            "png-scan" => Golden.PngScan(args),
            "manifest" => Manifest.Run(args),
            "subset" => Subset.Run(args),
            _ => Usage(args[0]),
        };
    }

    static int Usage(string unknown = null)
    {
        Console.WriteLine(unknown != null ? $"unknown command: {unknown}" : "usage:");
        Console.WriteLine("  AssetCompiler compile <lib.Lib> --out <dir> [--page 4096]");
        Console.WriteLine("  AssetCompiler compile-dir <dataRoot> --out <dir> [--page 4096] [--max N]");
        Console.WriteLine("  AssetCompiler verify <outDir>/<rel>.json <lib.Lib>");
        Console.WriteLine("  AssetCompiler verify-dir <dataRoot> <outDir>   // 全量审计：图集 vs 直解逐字节比对；lib 缺失=残留");
        Console.WriteLine("  AssetCompiler golden-dir <outDir>              // 从图集页提取每图 RGBA 写 SHA-256 侧车 <rel>.golden");
        Console.WriteLine("  AssetCompiler png-dump <png> <x> <y> <n>        // dump 区域 n×n 像素 RGBA 十六进制（诊断对照）");
        Console.WriteLine("  AssetCompiler png-scan <png> <x0> <y0> <x1> <y1> [max] // 扫区域找非零像素（诊断对照）");
        Console.WriteLine("  AssetCompiler manifest <dir> --out <file.json>       // 递归 sha256 资源分发清单（版本校验数据源）");
        return 2;
    }

    internal static string Arg(string[] args, string name, string def = null)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return def;
    }

    // ---------- 入口 ----------
    static int Compile(string[] args)
    {
        if (args.Length < 2) return Usage();
        string lib = Path.GetFullPath(args[1]);
        string outDir = Path.GetFullPath(Arg(args, "--out") ?? ".");
        int page = int.Parse(Arg(args, "--page") ?? "4096");
        string rel = Path.GetFileNameWithoutExtension(lib);
        string manPath = Path.Combine(outDir, rel + ".json");
        return CompileOne(lib, rel, outDir, page) && VerifyOne(manPath, lib) ? 0 : 1;
    }

    static int Verify(string[] args)
    {
        if (args.Length < 3) return Usage();
        return VerifyOne(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])) ? 0 : 1;
    }

    // 全量审计：遍历 outDir 下所有 manifest，按其 rel 定位数据源 .Lib（不存在=残留），
    // 对每库做图集 PNG 解码 vs 直解逐字节比对。返回 fail==0 ? 0 : 1。
    static int VerifyDir(string[] args)
    {
        if (args.Length < 3) return Usage();
        string dataRoot = Path.GetFullPath(args[1]);
        string outDir = Path.GetFullPath(args[2]);
        var mans = Directory.EnumerateFiles(outDir, "*.json", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        Console.WriteLine($"verifying {mans.Length} manifests from {outDir}");

        int ok = 0, fail = 0, missing = 0;
        foreach (var man in mans)
        {
            string rel = Path.GetRelativePath(outDir, man)[..^".json".Length];
            string lib = Path.Combine(dataRoot, rel + ".Lib");
            if (!File.Exists(lib)) { missing++; Console.WriteLine($"  MISSING lib: {rel}"); continue; }
            if (VerifyOne(man, lib)) ok++; else fail++;
        }
        Console.WriteLine($"verify ok={ok} fail={fail} missing={missing}");
        return fail == 0 ? 0 : 1;
    }

    static int CompileDir(string[] args)
    {
        string root = Path.GetFullPath(args.Length > 1 ? args[1] : ".");
        string outDir = Path.GetFullPath(Arg(args, "--out") ?? ".");
        int page = int.Parse(Arg(args, "--page") ?? "4096");
        int max = int.Parse(Arg(args, "--max") ?? "0");

        var libs = Directory.EnumerateFiles(root, "*.Lib", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        if (max > 0) libs = libs.Take(max).ToArray();
        Console.WriteLine($"compiling {libs.Length} libs from {root}");

        int ok = 0, fail = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long images = 0, pages = 0;
        foreach (var f in libs)
        {
            string rel = Path.GetRelativePath(root, f);
            rel = rel[..^Path.GetExtension(rel).Length]; // 去 .Lib，保留相对子路径
            if (CompileOne(f, rel, outDir, page))
            {
                ok++;
                var m = LoadManifest(Path.Combine(outDir, rel + ".json"));
                images += m.Images.Count(i => !i.Empty);
                pages += m.Pages.Count;
            }
            else fail++;
        }
        sw.Stop();
        Console.WriteLine($"ok={ok} fail={fail} images={images} pages={pages}  {sw.Elapsed.TotalSeconds:F1}s");
        return fail == 0 ? 0 : 1;
    }

    // ---------- 单个 .Lib 编译 ----------
    static bool CompileOne(string lib, string rel, string outDir, int pageSize)
    {
        byte[] raw;
        try { raw = File.ReadAllBytes(lib); }
        catch (Exception ex) { Console.WriteLine($"  {rel}: read fail {ex.Message}"); return false; }

        var anomalies = new List<string>();
        try
        {
            using var ms = new MemoryStream(raw);
            using var br = new BinaryReader(ms);

            int version = br.ReadInt32();
            if (version < 2) { Console.WriteLine($"  {rel}: version {version} < 2"); return false; }
            int count = br.ReadInt32();
            long frameSeek = 0;
            if (version >= 3) frameSeek = br.ReadInt32();
            var idx = new long[count];
            for (int i = 0; i < count; i++) idx[i] = br.ReadInt32();

            // 帧表（v3）——先读出来，compile 后写进清单
            var frames = new List<(byte action, FrameEntry fe)>();
            if (version >= 3 && frameSeek > 0 && frameSeek < raw.Length)
            {
                ms.Position = frameSeek;
                int fc = br.ReadInt32();
                for (int i = 0; i < fc; i++)
                {
                    byte action = br.ReadByte();
                    var fe = new FrameEntry
                    {
                        Start = br.ReadInt32(), Count = br.ReadInt32(), Skip = br.ReadInt32(), Interval = br.ReadInt32(),
                        EffectStart = br.ReadInt32(), EffectCount = br.ReadInt32(), EffectSkip = br.ReadInt32(), EffectInterval = br.ReadInt32(),
                        Reverse = br.ReadBoolean(), Blend = br.ReadBoolean(),
                    };
                    frames.Add((action, fe));
                }
            }

            // Pass 1：只读头，得尺寸/偏移（不解码），供打包
            var metas = new ImgMeta[count];
            for (int i = 0; i < count; i++)
            {
                long start = idx[i];
                long end = i + 1 < count ? idx[i + 1] : (frameSeek > 0 && frameSeek < raw.Length ? frameSeek : raw.Length);
                if (start < 0 || start + 17 > end || end > raw.Length)
                {
                    anomalies.Add($"idx {i} offset {start}->{end} OOR");
                    metas[i] = default;
                    continue;
                }
                ms.Position = start;
                var m = new ImgMeta { W = br.ReadInt16(), H = br.ReadInt16(), X = br.ReadInt16(), Y = br.ReadInt16(), SX = br.ReadInt16(), SY = br.ReadInt16() };
                m.Shadow = br.ReadByte();
                m.Length = br.ReadInt32();
                m.HasMask = (m.Shadow >> 7) == 1;
                m.DataStart = start + 17;
                if (m.HasMask)
                {
                    long mp = m.DataStart + m.Length;
                    if (mp + 12 > end) { anomalies.Add($"idx {i} mask header OOR"); m.HasMask = false; }
                    else
                    {
                        ms.Position = mp;
                        m.MW = br.ReadInt16(); m.MH = br.ReadInt16(); m.MX = br.ReadInt16(); m.MY = br.ReadInt16();
                        m.MaskLength = br.ReadInt32();
                        m.MaskDataStart = mp + 12;
                    }
                }
                metas[i] = m;
            }

            // 打包
            var (pages, rects) = Pack(metas, pageSize, anomalies);

            // 写图集页（主图页 + 平行 mask 页，均按下标 p 索引）
            // rel 已含相对子路径（如 "AWeapon/41 R"），父目录需补建
            var pageFiles = new List<PageFile>();
            var maskFiles = new List<PageFile>();
            string jsonPath = Path.Combine(outDir, rel + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));
            for (int p = 0; p < pages.Count; p++)
            {
                var pg = pages[p];
                var buf = new byte[pg.W * pg.H * 4];
                foreach (var (i, x, y) in pg.Items)
                {
                    var rgba = DecodeImage(raw, metas[i]);
                    Blit(buf, pg.W, x, y, metas[i].W, metas[i].H, rgba);
                }
                string png = $"{rel}_p{p}.png";
                WritePng(Path.Combine(outDir, png), pg.W, pg.H, buf);
                pageFiles.Add(new PageFile(Path.GetFileName(png), pg.W, pg.H));

                bool anyMask = pg.Items.Any(it => metas[it.i].HasMask && metas[it.i].MaskOk);
                if (anyMask)
                {
                    var mBuf = new byte[pg.W * pg.H * 4];
                    foreach (var (i, x, y) in pg.Items)
                        if (metas[i].HasMask && metas[i].MaskOk)
                            Blit(mBuf, pg.W, x, y, metas[i].W, metas[i].H, metas[i].MaskRgba);
                    string mpng = $"{rel}_p{p}_mask.png";
                    WritePng(Path.Combine(outDir, mpng), pg.W, pg.H, mBuf);
                    maskFiles.Add(new PageFile(Path.GetFileName(mpng), pg.W, pg.H));
                }
            }

            // 清单
            var man = new LibManifest
            {
                Lib = rel,
                Version = version,
                Count = count,
                PageSize = pageSize,
                Pages = pageFiles,
                MaskPages = maskFiles,
                Images = Enumerable.Range(0, count).Select(i =>
                {
                    var m = metas[i];
                    var e = new ImageEntry
                    {
                        I = i,
                        Empty = m.W == 0 || m.H == 0,
                        W = m.W, H = m.H, OX = m.X, OY = m.Y, SX = m.SX, SY = m.SY,
                        Shadow = m.Shadow,
                    };
                    if (rects[i].Page >= 0)
                    {
                        e.Page = rects[i].Page;
                        e.X = rects[i].X; e.Y = rects[i].Y;
                    }
                    if (m.HasMask && m.MaskOk)
                        e.Mask = new MaskEntry { Page = rects[i].Page, X = rects[i].X, Y = rects[i].Y, W = m.W, H = m.H, MX = m.MX, MY = m.MY };
                    return e;
                }).ToList(),
                Frames = frames.Select(f => { f.fe.Action = ((MirAction)f.action).ToString(); f.fe.ActionId = f.action; return f.fe; }).ToList(),
            };
            var json = JsonSerializer.Serialize(man, _jsonOpts);
            File.WriteAllText(jsonPath, json);

            if (anomalies.Count > 0)
                Console.WriteLine($"  {rel}: {anomalies.Count} anomalies (first: {anomalies[0]})");
            int masked = metas.Count(m => m.MaskOk);
            Console.WriteLine($"  {rel}: v{version} count={count} pages={pages.Count} masked={masked}{(masked > 0 ? $"  [{rel}]" : "")}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {rel}: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    // ---------- 打包（shelf，高先降序，超页图片独占一页） ----------
    internal static (List<PageInfo>, PackedRect[]) Pack(ImgMeta[] metas, int pageSize, List<string> anomalies)
    {
        int n = metas.Length;
        var rects = new PackedRect[n];
        var pages = new List<PageInfo>();
        var order = new List<int>();
        for (int i = 0; i < n; i++) if (metas[i].W > 0 && metas[i].H > 0) order.Add(i);
        order.Sort((a, b) =>
        {
            int c = metas[b].H.CompareTo(metas[a].H);
            return c != 0 ? c : metas[b].W.CompareTo(metas[a].W);
        });

        var cur = new List<(int i, int x, int y)>();
        int px = 0, py = 0, shelfH = 0;
        void Flush()
        {
            if (cur.Count == 0) return;
            int pw = 0, ph = 0;
            foreach (var (i, x, y) in cur)
            {
                pw = Math.Max(pw, x + metas[i].W);
                ph = Math.Max(ph, y + metas[i].H);
            }
            pages.Add(new PageInfo(pw, ph, cur));
            cur = new List<(int, int, int)>();
            px = py = shelfH = 0;
        }

        foreach (int i in order)
        {
            int w = metas[i].W, h = metas[i].H;
            if (w > pageSize || h > pageSize)
            {
                Flush();
                cur.Add((i, 0, 0));
                rects[i] = new PackedRect(pages.Count, 0, 0);
                Flush();
                anomalies.Add($"img {i} {w}x{h} > page {pageSize}（独占一页）");
                continue;
            }
            if (px + w > pageSize) { px = 0; py += shelfH; shelfH = 0; }
            if (py + h > pageSize) Flush();
            if (px + w > pageSize) { px = 0; py += shelfH; shelfH = 0; }
            cur.Add((i, px, py));
            rects[i] = new PackedRect(pages.Count, px, py);
            px += w;
            shelfH = Math.Max(shelfH, h);
        }
        Flush();
        return (pages, rects);
    }

    // ---------- 解码 ----------
    // 返回主图层 RGBA（W*H*4）。MaskRgba 在 metas 上缓存。
    static byte[] DecodeImage(byte[] raw, ImgMeta m)
    {
        var rgba = DecompressBgra(raw, (int)m.DataStart, m.Length, m.W, m.H);
        if (m.HasMask)
        {
            var mrgba = DecompressBgra(raw, (int)m.MaskDataStart, m.MaskLength, m.W, m.H);
            if (mrgba != null) { m.MaskRgba = mrgba; m.MaskOk = true; }
            else if (m.MaskLength == 0) { m.MaskOk = false; } // 无数据
        }
        return rgba;
    }

    // GZip(BGRA W*H*4) → RGBA，尾部零填充截断
    static byte[] DecompressBgra(byte[] src, int offset, int len, int w, int h)
    {
        if (len <= 0 || w <= 0 || h <= 0) return null;
        if (offset < 0 || offset + len > src.Length) return null;
        try
        {
            using var gz = new GZipStream(new MemoryStream(src, offset, len), CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            gz.CopyTo(outMs);
            var bgra = outMs.GetBuffer();
            int target = w * h * 4;
            if (outMs.Length < target) return null;
            var rgba = new byte[target];
            for (int p = 0; p < target; p += 4)
            {
                rgba[p] = bgra[p + 2];     // R
                rgba[p + 1] = bgra[p + 1]; // G
                rgba[p + 2] = bgra[p];     // B
                rgba[p + 3] = bgra[p + 3]; // A
            }
            return rgba;
        }
        catch { return null; }
    }

    internal static void Blit(byte[] page, int pageW, int x, int y, int w, int h, byte[] rgba)
    {
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(rgba, row * w * 4, page, ((y + row) * pageW + x) * 4, w * 4);
    }

    // ---------- verify：图集 PNG 解码 vs 直解逐字节比对 ----------
    static bool VerifyOne(string manPath, string lib)
    {
        string outDir = Path.GetDirectoryName(manPath);
        string rel = Path.GetFileNameWithoutExtension(manPath);
        var man = LoadManifest(manPath);
        var pngCache = new Dictionary<string, (int w, int h, byte[] rgba)>();

        (int w, int h, byte[] rgba) Load(int pageIdx, bool mask)
        {
            var page = mask ? man.MaskPages[pageIdx] : man.Pages[pageIdx];
            if (!pngCache.TryGetValue(page.Name, out var v))
            {
                var (pw, ph, prgba) = ReadPng(File.ReadAllBytes(Path.Combine(outDir, page.Name)));
                if (pw * ph * 4 != prgba.Length) throw new InvalidDataException($"{page.Name}: pixel {prgba.Length} != {pw}*{ph}*4");
                v = (pw, ph, prgba);
                pngCache[page.Name] = v;
            }
            return v;
        }

        byte[] raw = File.ReadAllBytes(lib);
        int bad = 0, checked_ = 0;
        for (int i = 0; i < man.Images.Count; i++)
        {
            var e = man.Images[i];
            if (e.Empty) continue;
            int w = e.W, h = e.H;
            var rgba = DecodeRaw(raw, man, i);
            var (pw, _, pg) = Load(e.Page, mask: false);
            var got = new byte[w * h * 4];
            for (int row = 0; row < h; row++)
                Buffer.BlockCopy(pg, ((e.Y + row) * pw + e.X) * 4, got, row * w * 4, w * 4);
            checked_++;
            if (!rgba.AsSpan().SequenceEqual(got))
            {
                bad++;
                if (bad <= 10) Console.WriteLine($"  MISMATCH img {i}");
            }
            if (e.Mask != null)
            {
                var (mpw, _, mg) = Load(e.Mask.Page, mask: true);
                var mgot = new byte[w * h * 4];
                for (int row = 0; row < h; row++)
                    Buffer.BlockCopy(mg, ((e.Mask.Y + row) * mpw + e.Mask.X) * 4, mgot, row * w * 4, w * 4);
                var mdec = DecodeRaw(raw, man, i, mask: true);
                if (mdec.Length != w * h * 4 || !mdec.AsSpan().SequenceEqual(mgot))
                {
                    bad++;
                    if (bad <= 10) Console.WriteLine($"  MISMATCH mask {i}");
                }
            }
        }
        if (bad > 0) { Console.WriteLine($"  {rel}: FAIL {bad}/{checked_} mismatches"); return false; }
        Console.WriteLine($"  {rel}: verify OK ({checked_} images, {man.Images.Count(x => x.Mask != null)} masks)");
        return true;
    }

    // 从 .Lib 直解某图（不含图集）
    static byte[] DecodeRaw(byte[] raw, LibManifest man, int i, bool mask = false)
    {
        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);
        int version = br.ReadInt32();
        int count = br.ReadInt32();
        long frameSeek = version >= 3 ? br.ReadInt32() : 0;
        var idx = new long[count];
        for (int k = 0; k < count; k++) idx[k] = br.ReadInt32();
        long start = idx[i];
        long end = i + 1 < count ? idx[i + 1] : (frameSeek > 0 && frameSeek < raw.Length ? frameSeek : raw.Length);
        ms.Position = start;
        var m = new ImgMeta
        {
            W = br.ReadInt16(), H = br.ReadInt16(), X = br.ReadInt16(), Y = br.ReadInt16(),
            SX = br.ReadInt16(), SY = br.ReadInt16(), Shadow = br.ReadByte(), Length = br.ReadInt32(),
            HasMask = false, DataStart = start + 17,
        };
        if (m.Shadow >> 7 == 1)
        {
            ms.Position = m.DataStart + m.Length;
            m.MW = br.ReadInt16(); m.MH = br.ReadInt16(); m.MX = br.ReadInt16(); m.MY = br.ReadInt16();
            m.MaskLength = br.ReadInt32();
            m.MaskDataStart = m.DataStart + m.Length + 12;
            m.HasMask = true;
        }
        if (mask)
        {
            if (!m.HasMask) return Array.Empty<byte>();
            var d = DecompressBgra(raw, (int)m.MaskDataStart, m.MaskLength, m.W, m.H);
            return d ?? Array.Empty<byte>();
        }
        return DecompressBgra(raw, (int)m.DataStart, m.Length, m.W, m.H) ?? Array.Empty<byte>();
    }

    internal static LibManifest LoadManifest(string path)
    {
        using var fs = File.OpenRead(path);
        return JsonSerializer.Deserialize<LibManifest>(fs, _jsonOpts);
    }

    // ---------- 最小 PNG 编解码（filter 0 扫描线；RGBA8） ----------
    static readonly byte[] _sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    static readonly uint[] _crc = BuildCrc();

    static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    static void WriteBE(BinaryWriter bw, uint v)
    {
        bw.Write((byte)(v >> 24)); bw.Write((byte)(v >> 16)); bw.Write((byte)(v >> 8)); bw.Write((byte)v);
    }

    static void WriteChunk(BinaryWriter bw, string type, byte[] data)
    {
        var t = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) t[i] = (byte)type[i];
        Array.Copy(data, 0, t, 4, data.Length);
        uint crc = 0xFFFFFFFF;
        foreach (var b in t) crc = _crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
        WriteBE(bw, (uint)data.Length);
        bw.Write(t);
        WriteBE(bw, crc ^ 0xFFFFFFFF);
    }

    internal static void WritePng(string path, int w, int h, byte[] rgba)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(_sig);
        var ihdr = new byte[13];
        uint u = (uint)w; ihdr[0] = (byte)(u >> 24); ihdr[1] = (byte)(u >> 16); ihdr[2] = (byte)(u >> 8); ihdr[3] = (byte)u;
        u = (uint)h; ihdr[4] = (byte)(u >> 24); ihdr[5] = (byte)(u >> 16); ihdr[6] = (byte)(u >> 8); ihdr[7] = (byte)u;
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // color type RGBA
        ihdr[10] = 0;  // compression
        ihdr[11] = 0;  // filter
        ihdr[12] = 0;  // interlace
        WriteChunk(bw, "IHDR", ihdr);

        int rowBytes = w * 4;
        var raw = new byte[(rowBytes + 1) * h];
        for (int y = 0; y < h; y++)
        {
            raw[y * (rowBytes + 1)] = 0; // filter None
            Buffer.BlockCopy(rgba, y * rowBytes, raw, y * (rowBytes + 1) + 1, rowBytes);
        }
        byte[] idat;
        using (var msz = new MemoryStream())
        {
            using (var z = new ZLibStream(msz, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
            idat = msz.ToArray();
        }
        WriteChunk(bw, "IDAT", idat);
        WriteChunk(bw, "IEND", Array.Empty<byte>());
    }

    // 仅支持我们自产的 PNG（filter 0）。遇其它 filter 报错。
    internal static (int w, int h, byte[] rgba) ReadPng(byte[] png)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(_sig)) throw new InvalidDataException("bad PNG sig");
        int pos = 8, w = 0, h = 0;
        var idat = new MemoryStream();
        bool ihdrSeen = false;
        while (pos + 8 <= png.Length)
        {
            int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            int data = pos + 8;
            if (type == "IHDR")
            {
                w = (png[data] << 24) | (png[data + 1] << 16) | (png[data + 2] << 8) | png[data + 3];
                h = (png[data + 4] << 24) | (png[data + 5] << 16) | (png[data + 6] << 8) | png[data + 7];
                if (png[data + 8] != 8 || png[data + 9] != 6) throw new InvalidDataException("unsupported PNG (want 8-bit RGBA)");
                ihdrSeen = true;
            }
            else if (type == "IDAT") idat.Write(png, data, len);
            else if (type == "IEND") break;
            pos = data + len + 4;
        }
        if (!ihdrSeen) throw new InvalidDataException("no IHDR");

        var raw = idat.ToArray();
        byte[] dec;
        using (var ms = new MemoryStream(raw))
        using (var z = new ZLibStream(ms, CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            z.CopyTo(outMs);
            dec = outMs.ToArray();
        }
        int rowBytes = w * 4;
        int expected = (rowBytes + 1) * h;
        if (dec.Length < expected) throw new InvalidDataException($"IDAT too short {dec.Length}<{expected}");
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            byte filter = dec[y * (rowBytes + 1)];
            if (filter != 0) throw new InvalidDataException($"unexpected PNG filter {filter} (only 0 written)");
            Buffer.BlockCopy(dec, y * (rowBytes + 1) + 1, rgba, y * rowBytes, rowBytes);
        }
        return (w, h, rgba);
    }

    // ---------- 数据模型 ----------
    internal sealed class ImgMeta
    {
        public int W, H, X, Y, SX, SY;
        public byte Shadow;
        public int Length;
        public bool HasMask;
        public long DataStart;
        public int MW, MH, MX, MY, MaskLength;
        public long MaskDataStart;
        public bool MaskOk;
        public byte[] MaskRgba;
    }

    internal readonly struct PackedRect
    {
        public readonly int Page, X, Y;
        public PackedRect(int page, int x, int y) { Page = page; X = x; Y = y; }
    }

    internal sealed class PageInfo
    {
        public int W, H;
        public List<(int i, int x, int y)> Items;
        public PageInfo(int w, int h, List<(int i, int x, int y)> items) { W = w; H = h; Items = items; }
    }

    internal sealed class PageFile
    {
        public string Name { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public PageFile() { }
        public PageFile(string name, int w, int h) { Name = name; W = w; H = h; }
    }

    internal sealed class LibManifest
    {
        public string Lib { get; set; }
        public int Version { get; set; }
        public int Count { get; set; }
        public int PageSize { get; set; }
        public List<PageFile> Pages { get; set; }
        public List<PageFile> MaskPages { get; set; }
        public List<ImageEntry> Images { get; set; }
        public List<FrameEntry> Frames { get; set; }
    }

    internal sealed class ImageEntry
    {
        public int I { get; set; }
        public bool Empty { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public int OX { get; set; }
        public int OY { get; set; }
        public int SX { get; set; }
        public int SY { get; set; }
        public int Shadow { get; set; }
        public int Page { get; set; } = -1;
        public int X { get; set; }
        public int Y { get; set; }
        public MaskEntry Mask { get; set; }
    }

    internal sealed class MaskEntry
    {
        public int Page { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public int MX { get; set; }
        public int MY { get; set; }
    }

    internal sealed class FrameEntry
    {
        public string Action { get; set; }
        public byte ActionId { get; set; }
        public int Start { get; set; }
        public int Count { get; set; }
        public int Skip { get; set; }
        public int Interval { get; set; }
        public int EffectStart { get; set; }
        public int EffectCount { get; set; }
        public int EffectSkip { get; set; }
        public int EffectInterval { get; set; }
        public bool Reverse { get; set; }
        public bool Blend { get; set; }
    }

    static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
}
