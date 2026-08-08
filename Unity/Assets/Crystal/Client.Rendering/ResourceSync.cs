using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

namespace Crystal.Client.Rendering
{
    // 阶段7 第 4 项（移动资源包、下载和版本校验）：资源分发清单 + 版本比对 + HTTP 下载骨架；
    // 阶段8 8-9-1（Manifest 版本系统）：远端清单带 Version，IsVersionOutdated 版本比对
    //   （本地无清单/解析失败/版本不匹配 → 过期触发下载），文件级 PlanDiff 兜底。
    // 数据源：AssetCompiler manifest 子命令（rel/size/sha256 + Version，PascalCase 与 JsonUtility 精确匹配）。
    // 链路：BuildLocalIndex（本地资源目录 sha256 索引）→ IsVersionOutdated（版本判定）
    //   → PlanDiff（远端清单 vs 本地，得需下载列表）→ DownloadFile（UnityWebRequest GET → 落盘
    //   → sha256 校验，失败删脏文件）。
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
        public string Version;
        public string GeneratedUtc;
        public int Count;
        public long TotalBytes;
        public List<ResourceFileEntry> Files;
    }

    public static class ResourceSync
    {
        // 本地已落地清单的默认文件名（与下载目录同层，镜像远端 resource.manifest.json）。
        public const string LocalManifestName = "resource.manifest.json";

        // 版本比对：远端清单 vs 本地已落地清单 → 是否过期（需触发下载）。
        // 本地无清单文件 / JSON 解析失败 / Version 不一致 → true（首启或版本升级/降级）。
        // Version 一致 → false；文件级差异由 PlanDiff 兜底（版本未变但文件被篡改仍会检出）。
        public static bool IsVersionOutdated(ResourceManifest remote, string localManifestPath)
        {
            if (remote == null || string.IsNullOrEmpty(remote.Version)) return true;
            if (!File.Exists(localManifestPath)) return true;
            ResourceManifest local;
            try
            {
                local = JsonUtility.FromJson<ResourceManifest>(File.ReadAllText(localManifestPath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[resource-sync] local manifest parse fail {localManifestPath}: {ex.Message}");
                return true;
            }
            if (local == null || string.IsNullOrEmpty(local.Version)) return true;
            return !string.Equals(remote.Version, local.Version, StringComparison.Ordinal);
        }

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
