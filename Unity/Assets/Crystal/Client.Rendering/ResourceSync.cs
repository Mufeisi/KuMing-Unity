using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

namespace Crystal.Client.Rendering
{
    // 阶段7 第 4 项（移动资源包、下载和版本校验）：资源分发清单 + 版本比对 + HTTP 下载骨架；
    // 阶段8 8-9-1（Manifest 版本系统）：远端清单带 Version，IsVersionOutdated 版本比对
    //   （本地无清单/解析失败/版本不匹配 → 过期触发下载），文件级 PlanDiff 兜底。
    // 8-9-2（下载系统）：FetchManifest/SyncResources 批量同步（重试+校验落盘+写回版本凭据）。
    // 8-9-3（增量更新）：FetchDelta/CanApplyDelta/SyncDelta——本地版本 == delta.BaseVersion 时
    //   只下变化/新增文件并 MergeManifest 合并写回；不匹配回退全量 SyncResources。
    // 数据源：AssetCompiler manifest 子命令（rel/size/sha256 + Version，PascalCase 与 JsonUtility 精确匹配）+ manifest-delta。
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

    // 增量清单（8-9-3）：AssetCompiler manifest-delta 产物。BaseVersion=客户端须持有的旧版本，
    // Version=新版本号；Files 只含变化/新增文件。客户端本地版本 == BaseVersion 才可应用（否则回退全量）。
    [Serializable]
    public sealed class DeltaManifest
    {
        public int Format;
        public string BaseVersion;
        public string Version;
        public string GeneratedUtc;
        public int Count;
        public List<ResourceFileEntry> Files;
    }

    public static class ResourceSync
    {
        // 本地已落地清单的默认文件名（与下载目录同层，镜像远端 resource.manifest.json）。
        public const string LocalManifestName = "resource.manifest.json";
        // 远端增量清单文件名（与全量 manifest 同层；AssetCompiler manifest-delta 默认输出名）。
        public const string DeltaManifestName = "resource.manifest.delta.json";

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

        // 拉取远端清单：GET baseUrl/resource.manifest.json → 解析。失败（网络/404/JSON 坏）返回 null。
        public static ResourceManifest FetchManifest(string baseUrl)
        {
            byte[] data = GetBytes(baseUrl, LocalManifestName);
            if (data == null) return null;
            try
            {
                var m = JsonUtility.FromJson<ResourceManifest>(System.Text.Encoding.UTF8.GetString(data));
                return m != null && m.Files != null ? m : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[resource-sync] remote manifest parse fail: {ex.Message}");
                return null;
            }
        }

        // 写回本地清单（下载完成后的版本凭据：下次启动 IsVersionOutdated 比对它）。
        // 原子写：先写 .tmp 再 rename（崩溃不留下半截清单，避免下次启动 parse fail 误判）。
        public static void WriteLocalManifest(string localManifestPath, ResourceManifest remote)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localManifestPath));
                string tmp = localManifestPath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(remote, true));
                File.Delete(localManifestPath);
                File.Move(tmp, localManifestPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[resource-sync] write local manifest fail {localManifestPath}: {ex.Message}");
            }
        }

        // 读本地已落地清单；无文件/解析失败 → null（首启或损坏）。
        public static ResourceManifest LoadLocalManifest(string localManifestPath)
        {
            if (!File.Exists(localManifestPath)) return null;
            try
            {
                var m = JsonUtility.FromJson<ResourceManifest>(File.ReadAllText(localManifestPath));
                return m != null && m.Files != null ? m : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[resource-sync] local manifest parse fail {localManifestPath}: {ex.Message}");
                return null;
            }
        }

        // 增量清单（8-9-3）：GET baseUrl/resource.manifest.delta.json → 解析。失败返回 null。
        public static DeltaManifest FetchDelta(string baseUrl)
        {
            byte[] data = GetBytes(baseUrl, DeltaManifestName);
            if (data == null) return null;
            try
            {
                var d = JsonUtility.FromJson<DeltaManifest>(System.Text.Encoding.UTF8.GetString(data));
                return d != null && d.Files != null ? d : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[resource-sync] delta manifest parse fail: {ex.Message}");
                return null;
            }
        }

        // 增量可应用判定：本地版本 == delta.BaseVersion（相邻版本升级）且 delta 有内容。
        public static bool CanApplyDelta(ResourceManifest local, DeltaManifest delta)
        {
            return local != null && delta != null && delta.Files != null && delta.Files.Count > 0
                && !string.IsNullOrEmpty(local.Version)
                && string.Equals(local.Version, delta.BaseVersion, StringComparison.Ordinal);
        }

        // 合并本地旧清单 + 增量 → 新全量清单（不变文件保留，增量覆盖/新增），Version 提升为 delta.Version。
        public static ResourceManifest MergeManifest(ResourceManifest local, DeltaManifest delta)
        {
            var merged = new ResourceManifest
            {
                Format = delta.Format,
                Version = delta.Version,
                GeneratedUtc = delta.GeneratedUtc,
                Files = new List<ResourceFileEntry>(),
            };
            if (local != null && local.Files != null)
                foreach (var f in local.Files)
                    merged.Files.Add(new ResourceFileEntry { Rel = f.Rel, Size = f.Size, Sha256 = f.Sha256 });
            foreach (var e in delta.Files)
            {
                int i = merged.Files.FindIndex(f => string.Equals(f.Rel, e.Rel, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) merged.Files[i] = new ResourceFileEntry { Rel = e.Rel, Size = e.Size, Sha256 = e.Sha256 };
                else merged.Files.Add(new ResourceFileEntry { Rel = e.Rel, Size = e.Size, Sha256 = e.Sha256 });
            }
            merged.Count = merged.Files.Count;
            return merged;
        }

        // 增量同步（8-9-3 核心）：按 delta.Files 逐个下载（本地已一致的跳过），全部成功
        // 合并写回本地清单返回 true；失败不写回（下次启动回退全量 SyncResources 重试）。
        public static bool SyncDelta(string baseUrl, DeltaManifest delta, string destDir,
            string localManifestPath, ResourceManifest local, Action<int, int, string> progress = null, int retries = 2)
        {
            if (delta == null || delta.Files == null || delta.Files.Count == 0) return false;
            var idx = BuildLocalIndex(destDir);
            int total = delta.Files.Count;
            int done = 0;
            foreach (var e in delta.Files)
            {
                if (idx.TryGetValue(e.Rel, out var cur) && cur.Size == e.Size
                    && string.Equals(cur.Sha256, e.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    done++; // 本地已是最新（如增量重复应用）→ 跳过下载
                    progress?.Invoke(done, total, e.Rel);
                    continue;
                }
                bool ok = false;
                for (int attempt = 0; attempt <= retries; attempt++)
                {
                    if (DownloadFile(baseUrl, e.Rel, destDir, e.Sha256, e.Size)) { ok = true; break; }
                    if (attempt < retries)
                    {
                        Debug.LogWarning($"[resource-sync] delta retry {e.Rel} attempt={attempt + 1}/{retries}");
                        System.Threading.Thread.Sleep(300);
                    }
                }
                if (!ok)
                {
                    Debug.LogError($"[resource-sync] delta fail rel={e.Rel}（已重试 {retries} 次）");
                    return false;
                }
                done++;
                progress?.Invoke(done, total, e.Rel);
            }
            WriteLocalManifest(localManifestPath, MergeManifest(local, delta));
            return true;
        }

        // 批量同步（8-9-2 下载系统核心）：PlanDiff（版本门 + 文件级兜底）→ 逐个 DownloadFile，
        // 单文件失败重试 retries 次（重试间 300ms 退避）→ 全部成功写回本地清单返回 true；
        // 任一文件重试后仍失败返回 false（不写清单，下次启动版本仍过期重试）。
        // progress(i, total, rel) 每成功一个文件回调（i 从 1 起）。远端清单为空 = 无可同步 → 写回即成功。
        public static bool SyncResources(string baseUrl, ResourceManifest remote, string destDir,
            string localManifestPath, Action<int, int, string> progress = null, int retries = 2)
        {
            if (remote == null || remote.Files == null) return false;
            if (remote.Files.Count == 0) { WriteLocalManifest(localManifestPath, remote); return true; }
            var need = PlanDiff(remote, BuildLocalIndex(destDir));
            int total = need.Count;
            int done = 0;
            foreach (var rel in need)
            {
                bool ok = false;
                for (int attempt = 0; attempt <= retries; attempt++)
                {
                    if (DownloadFile(baseUrl, rel, destDir, ShaOf(remote, rel), SizeOf(remote, rel))) { ok = true; break; }
                    if (attempt < retries)
                    {
                        Debug.LogWarning($"[resource-sync] retry {rel} attempt={attempt + 1}/{retries}");
                        System.Threading.Thread.Sleep(300); // 退避：瞬时闪断快速重试大概率再败
                    }
                }
                if (!ok)
                {
                    Debug.LogError($"[resource-sync] sync fail rel={rel}（已重试 {retries} 次）");
                    return false;
                }
                done++;
                progress?.Invoke(done, total, rel);
            }
            WriteLocalManifest(localManifestPath, remote);
            return true;
        }

        static string ShaOf(ResourceManifest remote, string rel)
        {
            foreach (var e in remote.Files)
                if (string.Equals(e.Rel, rel, StringComparison.OrdinalIgnoreCase)) return e.Sha256;
            return null;
        }

        static long SizeOf(ResourceManifest remote, string rel)
        {
            foreach (var e in remote.Files)
                if (string.Equals(e.Rel, rel, StringComparison.OrdinalIgnoreCase)) return e.Size;
            return -1;
        }

        static byte[] GetBytes(string baseUrl, string rel)
        {
            string url = baseUrl.TrimEnd('/') + "/" + Uri.EscapeUriString(rel);
            using var req = UnityWebRequest.Get(url);
            req.timeout = 30; // 同上：防 fetch 无限挂死
            req.SendWebRequest();
            while (!req.isDone) System.Threading.Thread.Sleep(10);
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[resource-sync] get fail rel={rel} result={req.result} err={req.error}");
                return null;
            }
            return req.downloadHandler.data;
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

        // GET baseUrl/<rel> → 断点续传落盘 <destDir>/<rel> → sha256 校验（8-9-4）。
        // 用 HttpWebRequest 流式读（UnityWebRequest 失败时拿不到部分数据，无法断点）：ResponseStream
        // 中途异常（断网/超时）→ 已写 .part 保留（下次带 Range bytes=existing- 续传）；
        // 本地 .part 已 ≥ expectedSize → 坏 .part 丢弃重下；sha 校验失败删 .part（坏包不留残留）；
        // 成功 rename .part → rel。服务器不支持 Range（回 200）→ 检测 Content-Range 决定 Create 重写。
        // rel 来自远端清单（不可信）：IsSafeRel 拒绝路径穿越（.. 段/绝对路径/盘符）防逃逸 destDir。
        public static bool DownloadFile(string baseUrl, string rel, string destDir, string expectedSha, long expectedSize = -1)
        {
            if (!IsSafeRel(rel))
            {
                Debug.LogWarning($"[resource-sync] unsafe rel rejected {rel}");
                return false;
            }
            string dest = Path.Combine(destDir, rel);
            string part = dest + ".part";
            long existing = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (expectedSize > 0 && existing >= expectedSize)
            {
                File.Delete(part); // 坏 .part（大小已达期望但 sha 未过）→ 整体重下
                existing = 0;
            }
            string url = baseUrl.TrimEnd('/') + "/" + Uri.EscapeUriString(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 30000;        // 连接超时（默认 0=永不，防启动卡死+ANR）
                req.ReadWriteTimeout = 30000; // 读超时：弱网半开不无限挂
                if (existing > 0) req.AddRange(existing); // Range: bytes=existing-
                using var resp = (HttpWebResponse)req.GetResponse();
                using var stream = resp.GetResponseStream();
                bool ranged = existing > 0 && resp.Headers["Content-Range"] != null;
                using (var fs = new FileStream(part, ranged ? FileMode.Append : FileMode.Create))
                {
                    var buf = new byte[64 * 1024];
                    int r;
                    while ((r = stream.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, r);
                }
                // 完整性检查：.NET HttpWebRequest 对 Content-Length 短读不抛异常（读循环正常退出），
                // 须按权威期望大小（manifest size）判定截断——截断则保留 .part 供下次 Range 续传。
                long got = new FileInfo(part).Length;
                if (expectedSize > 0 && got != expectedSize)
                {
                    Debug.LogWarning($"[resource-sync] truncated rel={rel} got={got} want={expectedSize} part 保留待续传");
                    return false;
                }
            }
            catch (Exception ex)
            {
                // 网络中断/超时：保留 .part（已写部分）供下次 Range 续传（失败恢复自愈）
                Debug.LogWarning($"[resource-sync] download fail rel={rel} {ex.GetType().Name} part={existing}B err={ex.Message}");
                return false;
            }
            string actual = Sha256File(part);
            if (!string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part); // 坏包不留残留（含 .part）
                Debug.LogWarning($"[resource-sync] hash mismatch rel={rel} got={actual.Substring(0, Math.Min(8, actual.Length))}…");
                return false;
            }
            File.Delete(dest);
            File.Move(part, dest);
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

        // rel 安全校验（远端清单不可信）：非空、无盘符/协议冒号、非绝对路径、无 .. 段（含 \ 归一）。
        static bool IsSafeRel(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return false;
            if (rel.IndexOf(':') >= 0) return false;
            if (rel[0] == '/' || rel[0] == '\\') return false;
            foreach (var seg in rel.Replace('\\', '/').Split('/'))
                if (seg == "..") return false;
            return true;
        }
    }
}
