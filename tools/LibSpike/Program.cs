using System.IO.Compression;
using System.Text;

namespace LibSpike;

// R0 Spike：校验 .Lib v3 解析逻辑与真实资源一致（风险 #1 去险）
// 结构参照 Client/MirGraphics/MLibrary.cs：version/count/frameSeek/indexList/FrameTable，
// MImage = [6×short + byte Shadow + int Length]=17B 基础头 + [4×short + int MaskLength]=12B mask 头。
// 校验：layer1 GZip 解压后 == Width*Height*4（A8R8G8B8 = BGRA 4Bpp）；
// mask 分支实证 CreateTexture 读 Length 字节 vs 读 MaskLength 字节哪个正确。

static class Program
{
    private sealed class Stats
    {
        public long Libs, LibsBad;
        public long V2, V3;
        public long Images, Masked, Frames;
        public long LayerOk, LayerBad;
        public long MaskLenEq, MaskLenDiff, MaskByMaskLen, MaskByLength, MaskDecodeBad;
        public long BoundaryOk, BoundaryBad;
        public long OffsetOutOfRange;
        public readonly Dictionary<long, long> DiffDist = new(); // decLen - expected -> count
        public readonly HashSet<string> PadFiles = new(); // 含尾部填充的文件
        public readonly List<string> Anomalies = new();
    }

    static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "inspect" && args.Length >= 3)
            return Inspect(args[1], int.Parse(args[2]));

        var root = args.Length > 0 ? args[0] : ".";
        var libs = Directory.EnumerateFiles(root, "*.Lib", SearchOption.AllDirectories).ToArray();
        Console.WriteLine($"scanning {libs.Length} libs under {root}");

        var total = new Stats();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Parallel.ForEach(libs,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file => { var s = new Stats(); Analyze(file, s); lock (total) Merge(total, s); });
        sw.Stop();

        Report(total, sw.Elapsed);
        return 0;
    }

    // 单图诊断：打印头部字段 + 解码尺寸与候选解释
    private static int Inspect(string file, int index)
    {
        var raw = File.ReadAllBytes(file);
        using var ms = new MemoryStream(raw);
        using var br = new BinaryReader(ms);
        int version = br.ReadInt32();
        int count = br.ReadInt32();
        int frameSeek = version >= 3 ? br.ReadInt32() : 0;
        var idx = new long[count];
        for (int i = 0; i < count; i++) idx[i] = br.ReadInt32();
        if (index < 0 || index >= count) { Console.WriteLine($"index {index} out of range [0,{count})"); return 2; }
        long start = idx[index];
        long end = index + 1 < count ? idx[index + 1] : (frameSeek > 0 ? frameSeek : raw.Length);
        ms.Position = start;
        short W = br.ReadInt16(), H = br.ReadInt16();
        short X = br.ReadInt16(), Y = br.ReadInt16();
        short SX = br.ReadInt16(), SY = br.ReadInt16();
        byte Shadow = br.ReadByte();
        int Length = br.ReadInt32();
        bool hasMask = (Shadow >> 7) == 1;
        Console.WriteLine($"version={version} count={count} frameSeek={frameSeek} index[{index}]={start} end={end}");
        Console.WriteLine($"W={W} H={H} X={X} Y={Y} SX={SX} SY={SY} Shadow=0x{Shadow:X2} hasMask={hasMask} Length={Length}");
        if (hasMask && start + 17 + Length + 12 <= end)
        {
            ms.Position = start + 17 + Length;
            short MW = br.ReadInt16(), MH = br.ReadInt16();
            short MX = br.ReadInt16(), MY = br.ReadInt16();
            int MaskLength = br.ReadInt32();
            Console.WriteLine($"MaskW={MW} MaskH={MH} MaskX={MX} MaskY={MY} MaskLength={MaskLength}");
        }
        var dec = Decompress(raw, (int)(start + 17), Length);
        long exp = (long)W * H * 4;
        Console.WriteLine($"decLen={dec?.Length ?? -1} expected(W*H*4)={exp} diff={((dec?.Length ?? 0) - exp)}");
        if (dec != null)
        {
            long pw = ((W + 3) / 4) * 4, ph = ((H + 3) / 4) * 4;
            var cands = new Dictionary<string, long>
            {
                ["W*H*4"] = (long)W * H * 4,
                ["paddedW*H*4"] = pw * H * 4,
                ["W*paddedH*4"] = W * ph * 4,
                ["W*H*3"] = (long)W * H * 3,
                ["W*H*4+10"] = (long)W * H * 4 + 10,
            };
            var matches = cands.Where(kv => kv.Value == dec.Length).Select(kv => kv.Key).ToArray();
            Console.WriteLine("  decLen matches: " + (matches.Length > 0 ? string.Join(", ", matches) : "(none)"));
            Console.WriteLine("  first16: " + BitConverter.ToString(dec, 0, Math.Min(16, dec.Length)));
            Console.WriteLine("  last16:  " + BitConverter.ToString(dec, Math.Max(0, dec.Length - 16), Math.Min(16, dec.Length)));
        }
        return 0;
    }

    private static void Merge(Stats t, Stats s)
    {
        t.Libs += s.Libs; t.LibsBad += s.LibsBad; t.V2 += s.V2; t.V3 += s.V3;
        t.Images += s.Images; t.Masked += s.Masked; t.Frames += s.Frames;
        t.LayerOk += s.LayerOk; t.LayerBad += s.LayerBad;
        t.MaskLenEq += s.MaskLenEq; t.MaskLenDiff += s.MaskLenDiff;
        t.MaskByMaskLen += s.MaskByMaskLen; t.MaskByLength += s.MaskByLength; t.MaskDecodeBad += s.MaskDecodeBad;
        t.BoundaryOk += s.BoundaryOk; t.BoundaryBad += s.BoundaryBad;
        t.OffsetOutOfRange += s.OffsetOutOfRange;
        foreach (var kv in s.DiffDist) { t.DiffDist.TryGetValue(kv.Key, out var v); t.DiffDist[kv.Key] = v + kv.Value; }
        foreach (var f in s.PadFiles) t.PadFiles.Add(f);
        t.Anomalies.AddRange(s.Anomalies);
    }

    private static void Analyze(string file, Stats s)
    {
        s.Libs++;
        byte[] raw;
        try { raw = File.ReadAllBytes(file); }
        catch (Exception ex) { s.LibsBad++; s.Anomalies.Add($"{file}: read fail {ex.Message}"); return; }

        try
        {
            using var ms = new MemoryStream(raw);
            using var br = new BinaryReader(ms);
            int version = br.ReadInt32();
            if (version < 2) { s.LibsBad++; s.Anomalies.Add($"{file}: version {version} < 2"); return; }
            if (version == 2) s.V2++; else if (version == 3) s.V3++; else s.V2++;

            int count = br.ReadInt32();
            if (count == 0) { s.Libs++; s.V3++; return; } // 合法空库（无图无帧表）
            if (count < 0 || count > 1_000_000) { s.LibsBad++; s.Anomalies.Add($"{file}: count {count} implausible"); return; }
            int frameSeek = 0;
            if (version >= 3) frameSeek = br.ReadInt32();

            var index = new long[count];
            for (int i = 0; i < count; i++) index[i] = br.ReadInt32();

            long frameCount = 0;
            if (version >= 3 && frameSeek > 0 && frameSeek < raw.Length)
            {
                ms.Position = frameSeek;
                frameCount = br.ReadInt32();
                if (frameCount < 0 || frameCount > 1_000_000) { s.LibsBad++; s.Anomalies.Add($"{file}: frameCount {frameCount} implausible"); return; }
                if (frameCount > 0)
                {
                    if (frameSeek + 4 + frameCount * 35L > raw.Length) { s.LibsBad++; s.Anomalies.Add($"{file}: frame table out of range"); return; }
                    ms.Position = frameSeek + 4;
                    for (long f = 0; f < frameCount; f++) { br.ReadByte(); br.ReadBytes(34); }
                    s.Frames += frameCount;
                }
            }

            for (int i = 0; i < count; i++)
            {
                long start = index[i];
                long end = i + 1 < count ? index[i + 1] : (frameSeek > 0 && frameSeek < raw.Length ? frameSeek : raw.Length);
                if (start < 0 || start + 17 > end || end > raw.Length) { s.OffsetOutOfRange++; s.Anomalies.Add($"{file}: idx {i} offset {start}->{end} OOR"); continue; }

                ms.Position = start;
                short W = br.ReadInt16(), H = br.ReadInt16();
                br.ReadInt16(); br.ReadInt16(); br.ReadInt16(); br.ReadInt16(); // X,Y,ShadowX,ShadowY
                byte Shadow = br.ReadByte();
                int Length = br.ReadInt32();
                bool hasMask = (Shadow >> 7) == 1;
                if (W == 0 || H == 0) { s.Images++; continue; } // 合法空占位图（无像素数据）
                if (W < 0 || H < 0 || Length <= 0 || start + 17 + Length > end) { s.LayerBad++; s.Anomalies.Add($"{file}: idx {i} dims {W}x{H} len {Length} oob"); continue; }

                var dec1 = Decompress(raw, (int)(start + 17), Length);
                long exp1 = (long)W * H * 4;
                if (dec1 == null || dec1.Length < exp1)
                {
                    s.LayerBad++;
                    s.DiffDist.TryGetValue((dec1?.Length ?? -1) - exp1, out var c);
                    s.DiffDist[(dec1?.Length ?? -1) - exp1] = c + 1;
                    s.Anomalies.Add($"{file}: idx {i} layer1 {dec1?.Length ?? -1} < {exp1} 截断");
                }
                else
                {
                    s.LayerOk++;
                    if (dec1.Length > exp1) // 写者多压的尾部零填充，原客户端读 W*H*4 忽略
                    {
                        s.DiffDist.TryGetValue(dec1.Length - exp1, out var c);
                        s.DiffDist[dec1.Length - exp1] = c + 1;
                        s.PadFiles.Add(file);
                    }
                }
                s.Images++;

                if (hasMask)
                {
                    s.Masked++;
                    long maskHdrPos = start + 17 + Length;
                    if (maskHdrPos + 12 > end) { s.MaskDecodeBad++; s.Anomalies.Add($"{file}: idx {i} mask header oob"); continue; }
                    ms.Position = maskHdrPos;
                    br.ReadInt16(); br.ReadInt16(); br.ReadInt16(); br.ReadInt16(); // MaskW/H/X/Y
                    int MaskLength = br.ReadInt32();
                    long maskDataStart = maskHdrPos + 12;
                    long maskAvail = end - maskDataStart;

                    // 实证：CreateTexture 用 Length 字节读 mask；构造函数跳过 MaskLength 字节
                    // 分类：MaskLength 解码成功 → 写者按 MaskLength 布局；Length 解码成功 → 写者按 Length 布局
                    bool lenEq = MaskLength == Length;
                    if (lenEq) s.MaskLenEq++; else s.MaskLenDiff++;

                    long target = (long)W * H * 4;
                    bool byMaskLen = false, byLength = false;
                    if (MaskLength > 0 && MaskLength <= maskAvail)
                    {
                        var d = Decompress(raw, (int)maskDataStart, MaskLength);
                        if (d != null && d.Length == target) byMaskLen = true;
                    }
                    if (!byMaskLen && Length > 0 && Length <= maskAvail)
                    {
                        var d = Decompress(raw, (int)maskDataStart, Length);
                        if (d != null && d.Length == target) byLength = true;
                    }
                    if (byMaskLen) s.MaskByMaskLen++;
                    else if (byLength) s.MaskByLength++;
                    else { s.MaskDecodeBad++; s.Anomalies.Add($"{file}: idx {i} mask len={MaskLength} avail={maskAvail} eq={lenEq} undecodable"); }

                    // 顺序布局校验：masked 图应占 17 + Length + 12 + MaskLength
                    if (start + 17 + Length + 12L + MaskLength == end) { s.BoundaryOk++; }
                    else { s.BoundaryBad++; s.Anomalies.Add($"{file}: idx {i} consumed {start + 17 + Length + 12L + MaskLength} != end {end}"); }
                }
                else
                {
                    // 非 masked：边界应为 17 + Length
                    if (start + 17L + Length == end) { s.BoundaryOk++; }
                    else { s.BoundaryBad++; s.Anomalies.Add($"{file}: idx {i} consumed {start + 17L + Length} != end {end}"); }
                }
            }
        }
        catch (Exception ex) { s.LibsBad++; s.Anomalies.Add($"{file}: {ex.GetType().Name} {ex.Message}"); }
    }

    private static byte[] Decompress(byte[] src, int offset, int len)
    {
        try
        {
            using var gz = new GZipStream(new MemoryStream(src, offset, len), CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            gz.CopyTo(outMs);
            return outMs.ToArray();
        }
        catch { return null; }
    }

    private static void Report(Stats s, TimeSpan elapsed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== .Lib Spike 报告 ({elapsed.TotalSeconds:F1}s) ===");
        sb.AppendLine($"libs={s.Libs}  bad_libs={s.LibsBad}  v2={s.V2}  v3={s.V3}");
        sb.AppendLine($"images={s.Images}  masked={s.Masked}  frames={s.Frames}");
        sb.AppendLine($"layer1: ok={s.LayerOk}  bad={s.LayerBad}");
        sb.AppendLine($"mask:   len==length={s.MaskLenEq}  len!=length={s.MaskLenDiff}  by_MaskLen={s.MaskByMaskLen}  by_Length={s.MaskByLength}  bad={s.MaskDecodeBad}");
        sb.AppendLine($"boundary: ok={s.BoundaryOk}  bad={s.BoundaryBad}  offset_oob={s.OffsetOutOfRange}");
        sb.AppendLine("diff(decLen-expected): " + string.Join(", ", s.DiffDist.OrderByDescending(kv => kv.Value).Take(10).Select(kv => $"{kv.Key}={kv.Value}")));
        if (s.PadFiles.Count > 0)
            sb.AppendLine($"padding 文件数: {s.PadFiles.Count}  例: {string.Join(", ", s.PadFiles.Take(5).Select(Path.GetFileName))} ...");
        sb.AppendLine($"anomalies={s.Anomalies.Count}");
        int shown = 0;
        foreach (var a in s.Anomalies)
        {
            if (shown >= 25) { sb.AppendLine($"... 及另外 {s.Anomalies.Count - 25} 条"); break; }
            sb.AppendLine("  " + a); shown++;
        }
        var outPath = Path.Combine(Path.GetTempPath(), "libspike-report.txt");
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine(sb.ToString());
        Console.WriteLine($"report -> {outPath}");
    }
}
