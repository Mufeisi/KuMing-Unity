using System.Security.Cryptography;
using System.Text.Json;

namespace Crystal.AssetCompiler;

// manifest <dir> --out <file.json> [--version <v>]：递归扫 dir 下所有文件，生成资源分发清单
// （每项 rel/size/sha256 + 汇总 Version/count/totalBytes）。阶段7 第 4 项"版本校验"数据源：
// 客户端本地索引与之 PlanDiff，得需下载文件列表（缺失或 size/sha256 不一致）；
// 阶段8 8-9-1 起带版本号：--version 写入顶层 Version，客户端 IsVersionOutdated 比对，
// 版本不匹配即触发下载。
// 确定性：按 rel OrdinalIgnoreCase 排序，同输入同版本重复运行 Files/Version 字节级一致
// （GeneratedUtc 为生成时刻，不参与内容比较）。
static class Manifest
{
    internal sealed class Entry { public string Rel { get; set; } public long Size { get; set; } public string Sha256 { get; set; } }
    internal sealed class Output { public int Format { get; set; } public string Version { get; set; } public string GeneratedUtc { get; set; } public int Count { get; set; } public long TotalBytes { get; set; } public List<Entry> Files { get; set; } }

    internal static int Run(string[] args)
    {
        string dir = Path.GetFullPath(args.Length > 1 ? args[1] : ".");
        string outAbs = Path.GetFullPath(Arg(args, "--out") ?? Path.Combine(dir, "resource.manifest.json"));
        string version = Arg(args, "--version") ?? "0";

        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFullPath(f).Equals(outAbs, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var entries = new List<Entry>(files.Length);
        long total = 0;
        foreach (var f in files)
        {
            string rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
            long size = new FileInfo(f).Length;
            entries.Add(new Entry { Rel = rel, Size = size, Sha256 = HashFile(f) });
            total += size;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outAbs));
        var output = new Output
        {
            Format = 1,
            Version = version,
            GeneratedUtc = DateTime.UtcNow.ToString("o"),
            Count = entries.Count,
            TotalBytes = total,
            Files = entries,
        };
        File.WriteAllText(outAbs, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"manifest dir={dir} version={version} files={entries.Count} bytes={total} -> {outAbs}");
        return 0;
    }

    static string Arg(string[] args, string name, string def = null)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return def;
    }

    static string HashFile(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs));
    }
}
