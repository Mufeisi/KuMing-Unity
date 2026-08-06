using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

namespace Crystal.Client.Rendering
{
    // 阶段7 第 4 项（移动资源包、下载和版本校验）：资源分发清单 + 版本比对 + HTTP 下载骨架。
    // 数据源：AssetCompiler manifest 子命令（rel/size/sha256 递归清单，PascalCase 与 JsonUtility 精确匹配）。
    // 链路：BuildLocalIndex（本地资源目录 sha256 索引）→ PlanDiff（远端清单 vs 本地，得需下载列表）
    //   → DownloadFile（UnityWebRequest GET → 落盘 → sha256 校验，失败删脏文件）。
    // 本地资源目录约定镜像 assetcompile 布局：<destDir>/<rel>，图集 manifest 与页 PNG 同目录
    //   （AtlasLibrary.Load 要求），下载后即可加载。全量资源打包/增量清单属阶段8，本骨架验证机制。

    [Serializable]
    public sealed class ResourceFileEntry
    {
        public string Rel;
        public long Size;
        public string Sha256;
    }

    [Serializable]
    public sealed class ResourceManifest
    {
        public int Format;
        public string GeneratedUtc;
        public int Count;
        public long TotalBytes;
        public List<ResourceFileEntry> Files;
    }

    public static class ResourceSync
    {
        // 本地目录 → rel→(size, sha256) 索引（与远端清单同语义，rel 正斜杠）。目录不存在返回空。
        public static Dictionary<string, (long Size, string Sha256)> BuildLocalIndex(string dir)
        {
            var idx = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(dir)) return idx;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
                idx[rel] = (new FileInfo(f).Length, Sha256File(f));
            }
            return idx;
        }

        // 远端清单 vs 本地索引 → 需下载 rel 列表（本地缺失，或 size/sha256 任一不一致），按清单顺序。
        public static List<string> PlanDiff(ResourceManifest remote, Dictionary<string, (long Size, string Sha256)> local)
        {
            var need = new List<string>();
            foreach (var e in remote.Files)
            {
                if (!local.TryGetValue(e.Rel, out var cur)
                    || cur.Size != e.Size
                    || !string.Equals(cur.Sha256, e.Sha256, StringComparison.OrdinalIgnoreCase))
                    need.Add(e.Rel);
            }
            return need;
        }

        // GET baseUrl/<rel> → 落盘 <destDir>/<rel> → sha256 校验；不匹配删文件返回 false（不留下脏状态）。
        public static bool DownloadFile(string baseUrl, string rel, string destDir, string expectedSha)
        {
            string url = baseUrl.TrimEnd('/') + "/" + Uri.EscapeUriString(rel);
            using var req = UnityWebRequest.Get(url);
            req.SendWebRequest();
            while (!req.isDone) System.Threading.Thread.Sleep(10);
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[resource-sync] download fail rel={rel} result={req.result} err={req.error}");
                return false;
            }
            string dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.WriteAllBytes(dest, req.downloadHandler.data);
            string actual = Sha256File(dest);
            if (!string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(dest);
                Debug.LogWarning($"[resource-sync] hash mismatch rel={rel} got={actual.Substring(0, Math.Min(8, actual.Length))}…");
                return false;
            }
            return true;
        }

        static string Sha256File(string path)
        {
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Hex(sha.ComputeHash(fs));
        }

        static string Hex(byte[] b)
        {
            var sb = new System.Text.StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("X2"));
            return sb.ToString();
        }
    }
}
