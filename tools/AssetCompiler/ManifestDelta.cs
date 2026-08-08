using System.Security.Cryptography;
using System.Text.Json;

namespace Crystal.AssetCompiler;

// manifest-delta <old-manifest.json> <dir> --out <delta.json> [--version <v>]
// 打包侧增量发布（8-9-3）：读上一版本全量清单，扫新资源目录，输出仅"新增或内容变化"文件的
// 增量清单（BaseVersion=旧清单 Version，Version=新版本号）。客户端本地版本与 BaseVersion 匹配时
// 只按增量清单下载（小版本只下增量），不匹配回退全量 PlanDiff。
// 确定性：同输入同版本重复运行 Files/Version/BaseVersion 完全一致（GeneratedUtc 不参与比较）。
static class ManifestDelta
{
    internal sealed class DeltaEntry { public string Rel { get; set; } public long Size { get; set; } public string Sha256 { get; set; } }
    internal sealed class DeltaOutput { public int Format { get; set; } public string BaseVersion { get; set; } public string Version { get; set; } public string GeneratedUtc { get; set; } public int Count { get; set; } public List<DeltaEntry> Files { get; set; } }

    internal static int Run(string[] args)
    {
        if (args.Length < 3) { Program.Usage("manifest-delta"); return 2; }
        string oldMan = Path.GetFullPath(args[1]);
        string dir = Path.GetFullPath(args[2]);
        string outAbs = Path.GetFullPath(Program.Arg(args, "--out") ?? Path.Combine(dir, "resource.manifest.delta.json"));
        string version = Program.Arg(args, "--version") ?? "0";
        if (!File.Exists(oldMan)) { Console.Error.WriteLine($"old manifest missing: {oldMan}"); return 1; }

        // 旧清单索引：rel → (size, sha256)
        using (var doc = JsonDocument.Parse(File.ReadAllText(oldMan)))
        {
            var oldIdx = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
            string baseVersion = "0";
            if (doc.RootElement.TryGetProperty("Version", out var v)) baseVersion = v.GetString() ?? "0";
            if (doc.RootElement.TryGetProperty("Files", out var files) && files.ValueKind == JsonValueKind.Array)
                foreach (var f in files.EnumerateArray())
                {
                    string rel = f.GetProperty("Rel").GetString();
                    long size = f.GetProperty("Size").GetInt64();
                    string sha = f.GetProperty("Sha256").GetString();
                    if (rel != null) oldIdx[rel] = (size, sha);
                }

            var outAbsFull = Path.GetFullPath(outAbs);
            var entries = new List<DeltaEntry>();
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetFullPath(f).Equals(outAbsFull, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
                long size = new FileInfo(f).Length;
                string sha = HashFile(f);
                if (oldIdx.TryGetValue(rel, out var cur) && cur.Item1 == size && string.Equals(cur.Item2, sha, StringComparison.OrdinalIgnoreCase))
                    continue; // 与旧版一致 → 不进增量
                entries.Add(new DeltaEntry { Rel = rel, Size = size, Sha256 = sha });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outAbs));
            var output = new DeltaOutput
            {
                Format = 1,
                BaseVersion = baseVersion,
                Version = version,
                GeneratedUtc = DateTime.UtcNow.ToString("o"),
                Count = entries.Count,
                Files = entries,
            };
            File.WriteAllText(outAbs, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"manifest-delta base={baseVersion} version={version} changed={entries.Count} -> {outAbs}");
        }
        return 0;
    }

    static string HashFile(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs));
    }
}
