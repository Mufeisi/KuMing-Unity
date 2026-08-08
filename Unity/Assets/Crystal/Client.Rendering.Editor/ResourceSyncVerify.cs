using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // 阶段7 第 4 项探针（batchmode）：ResourceSync 版本校验 + 下载链路。
    // 场景1 diff：本地索引 vs 远端清单 → 需下载列表（变更 + 缺失，相同排除）。
    // 场景2 端到端：TcpListener 起本地静态 HTTP 资源服务器 → DownloadFile 拉取 +
    //   sha256 校验（正确 hash 落盘，错误 hash 拒绝且不残留脏文件）。
    // 阶段8 8-9-1：场景3 版本比对（IsVersionOutdated：无本地清单/版本不同 → 过期；
    //   版本一致 → 不过期；版本一致但文件被篡改 → PlanDiff 文件级兜底检出）。
    //   场景4 AssetCompiler manifest 确定性：dotnet 调 AssetCompiler.dll 两次（同输入同版本），
    //   Version/Files 完全一致（GeneratedUtc 忽略）；Version 字段正确写入。
    public static class ResourceSyncVerify
    {
        public static void Run()
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "resourcesync-verify-" + Guid.NewGuid().ToString("N"));
                int cases = 0;
                bool ok = DiffCase(Path.Combine(root, "diff"), ref cases)
                       & DownloadCase(Path.Combine(root, "dl"), ref cases)
                       & VersionCase(Path.Combine(root, "ver"), ref cases)
                       & DeterminismCase(Path.Combine(root, "det"), ref cases)
                       & SyncCase(Path.Combine(root, "sync"), ref cases)
                       & DeltaCase(Path.Combine(root, "delta"), ref cases);
                try { Directory.Delete(root, true); } catch { }
                Debug.Log($"[resourcesync] {(ok ? "PASS" : "FAIL")} cases={cases}");
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[resourcesync] exception {ex}");
                EditorApplication.Exit(1);
            }
        }

        // 场景1：a.bin 相同跳过；b.bin size/sha 变更；c.bin 缺失 → 需下载 [b.bin, c.bin]。
        static bool DiffCase(string root, ref int cases)
        {
            string local = Path.Combine(root, "local");
            Directory.CreateDirectory(local);
            File.WriteAllBytes(Path.Combine(local, "a.bin"), Encoding.ASCII.GetBytes("v1data"));
            File.WriteAllBytes(Path.Combine(local, "b.bin"), Encoding.ASCII.GetBytes("olddata"));

            var remote = new ResourceManifest
            {
                Files = new List<ResourceFileEntry>
                {
                    new ResourceFileEntry { Rel = "a.bin", Size = 6, Sha256 = Sha("v1data") },
                    new ResourceFileEntry { Rel = "b.bin", Size = 7, Sha256 = Sha("newdata") },
                    new ResourceFileEntry { Rel = "c.bin", Size = 2, Sha256 = Sha("cd") },
                },
            };
            var need = ResourceSync.PlanDiff(remote, ResourceSync.BuildLocalIndex(local));
            bool pass = need.Count == 2 && need[0] == "b.bin" && need[1] == "c.bin";
            Debug.Log($"[resourcesync] diff-case ok={pass} need=[{string.Join(",", need)}]");
            if (pass) cases++;
            return pass;
        }

        // 场景2：MiniHttpServer 托管 serverRoot；DownloadFile 正确 hash → 落盘且内容一致；
        // 错误 hash → 拒绝且 dest 无残留。
        static bool DownloadCase(string root, ref int cases)
        {
            string serverRoot = Path.Combine(root, "server");
            string dest = Path.Combine(root, "dest");
            Directory.CreateDirectory(serverRoot);
            File.WriteAllBytes(Path.Combine(serverRoot, "b.bin"), Encoding.ASCII.GetBytes("newdata"));
            File.WriteAllBytes(Path.Combine(serverRoot, "c.bin"), Encoding.ASCII.GetBytes("cd"));

            using var server = new MiniHttpServer(serverRoot);
            string baseUrl = $"http://127.0.0.1:{server.Port}/";

            bool ok = true;
            ok &= Check(ResourceSync.DownloadFile(baseUrl, "b.bin", dest, Sha("newdata"))
                && File.ReadAllBytes(Path.Combine(dest, "b.bin")).Length == 7
                && Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dest, "b.bin"))) == "newdata",
                "download ok+落盘一致");
            string cPath = Path.Combine(dest, "c.bin");
            ok &= Check(!ResourceSync.DownloadFile(baseUrl, "c.bin", dest, Sha("wrong")) && !File.Exists(cPath),
                "错误 hash 拒绝且无残留");
            Debug.Log($"[resourcesync] download-case ok={ok}");
            if (ok) cases++;
            return ok;
        }

        // 场景6（8-9-3）：增量更新端到端。AssetCompiler manifest-delta 生成正确性（Process 调 dll）
        // + 客户端 SyncDelta：本地 v1 → delta(BaseVersion=v1) 只下变化/新增 → MergeManifest 写回 v2；
        // 幂等（本地已一致跳过）；BaseVersion 不匹配 → 回退全量 SyncResources（文件级 diff 仍增量）。
        static bool DeltaCase(string root, ref int cases)
        {
            string dll = AssetCompilerDll();
            if (dll == null)
            {
                Debug.Log("[resourcesync] delta-case ok=False（AssetCompiler.dll 未找到）");
                return false;
            }
            string v1 = Path.Combine(root, "v1");
            string v2 = Path.Combine(root, "v2");
            Directory.CreateDirectory(Path.Combine(v1, "sub"));
            Directory.CreateDirectory(Path.Combine(v2, "sub"));
            File.WriteAllBytes(Path.Combine(v1, "a.bin"), Encoding.ASCII.GetBytes("aaa"));
            File.WriteAllBytes(Path.Combine(v1, "sub", "s.bin"), Encoding.ASCII.GetBytes("old"));
            File.WriteAllBytes(Path.Combine(v2, "a.bin"), Encoding.ASCII.GetBytes("aaa"));
            File.WriteAllBytes(Path.Combine(v2, "sub", "s.bin"), Encoding.ASCII.GetBytes("new!!"));
            File.WriteAllBytes(Path.Combine(v2, "c.bin"), Encoding.ASCII.GetBytes("newc"));

            string m1 = Path.Combine(root, "m1.json"), m2 = Path.Combine(root, "m2.json"), dJson = Path.Combine(root, "d.json");
            bool ok = RunAssetCompiler(dll, v1, m1, "1.0")
                   & RunAssetCompiler(dll, v2, m2, "2.0")
                   & RunAssetCompilerDelta(dll, m1, v2, dJson, "2.0");
            var delta = ok ? JsonUtility.FromJson<DeltaManifest>(File.ReadAllText(dJson)) : null;
            ok &= Check(delta != null && delta.BaseVersion == "1.0" && delta.Version == "2.0" && delta.Count == 2
                && delta.Files.Count == 2
                && delta.Files[0].Rel == "c.bin" && delta.Files[1].Rel == "sub/s.bin",
                "6a manifest-delta 生成：BaseVersion=1.0 Version=2.0 只含 [c.bin, sub/s.bin]（a.bin 相同排除）");

            // 服务器托管 v2 全量 + manifest + delta
            File.WriteAllText(Path.Combine(v2, ResourceSync.LocalManifestName), JsonUtility.ToJson(
                JsonUtility.FromJson<ResourceManifest>(File.ReadAllText(m2)), true));
            File.Copy(dJson, Path.Combine(v2, ResourceSync.DeltaManifestName), true);

            string dest = Path.Combine(root, "dest");
            Directory.CreateDirectory(dest);
            // 设备已有 v1 全量 + v1 本地清单
            File.WriteAllBytes(Path.Combine(dest, "a.bin"), Encoding.ASCII.GetBytes("aaa"));
            Directory.CreateDirectory(Path.Combine(dest, "sub"));
            File.WriteAllBytes(Path.Combine(dest, "sub", "s.bin"), Encoding.ASCII.GetBytes("old"));
            string manPath = Path.Combine(dest, ResourceSync.LocalManifestName);
            File.Copy(m1, manPath, true);

            using (var server = new MiniHttpServer(v2))
            {
                string baseUrl = $"http://127.0.0.1:{server.Port}/";
                var progress = new List<string>();

                // 6b 增量下载：只下 2 个变化文件，合并写回 v2
                var remote = ResourceSync.FetchManifest(baseUrl);
                var local = ResourceSync.LoadLocalManifest(manPath);
                var deltaFetched = ResourceSync.FetchDelta(baseUrl);
                ok &= Check(remote != null && remote.Version == "2.0", "6b 远端全量清单 v2");
                ok &= Check(ResourceSync.IsVersionOutdated(remote, manPath), "6b 版本 1.0≠2.0 → 过期");
                ok &= Check(ResourceSync.CanApplyDelta(local, deltaFetched), "6b delta BaseVersion=1.0 匹配本地 → 可应用");
                bool okB = ResourceSync.SyncDelta(baseUrl, deltaFetched, dest, manPath, local,
                    (i, n, rel) => progress.Add($"{i}/{n}:{rel}"));
                var merged = ResourceSync.LoadLocalManifest(manPath);
                ok &= Check(okB && progress.Count == 2 && progress[0] == "1/2:c.bin" && progress[1] == "2/2:sub/s.bin"
                    && Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dest, "sub", "s.bin"))) == "new!!"
                    && Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dest, "c.bin"))) == "newc"
                    && Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dest, "a.bin"))) == "aaa"
                    && merged != null && merged.Version == "2.0" && merged.Files.Count == 3,
                    "6b 增量下载 2/2（a.bin 未动）→ 合并清单 v2 共 3 文件");

                // 6c 幂等：再跑 → 本地已一致全跳过（零下载，仍 true）
                progress.Clear();
                bool okC = ResourceSync.SyncDelta(baseUrl, deltaFetched, dest, manPath,
                    ResourceSync.LoadLocalManifest(manPath), (i, n, rel) => progress.Add($"{i}/{n}:{rel}"));
                ok &= Check(okC && progress.Count == 2 && progress[1] == "2/2:sub/s.bin", "6c 幂等：本地已一致 → 跳过下载仍 true");

                // 6d BaseVersion 不匹配（设备 v0）→ CanApplyDelta false → 回退全量 SyncResources
                // （文件级 diff 只下缺失/变化：v0 设备只有 a.bin → 检出 [c.bin, sub/s.bin]）
                string destV0 = Path.Combine(root, "dest-v0");
                Directory.CreateDirectory(Path.Combine(destV0, "sub"));
                File.WriteAllBytes(Path.Combine(destV0, "a.bin"), Encoding.ASCII.GetBytes("aaa"));
                File.WriteAllBytes(Path.Combine(destV0, "sub", "s.bin"), Encoding.ASCII.GetBytes("old"));
                string v0Man = Path.Combine(destV0, ResourceSync.LocalManifestName);
                File.WriteAllText(v0Man, JsonUtility.ToJson(new ResourceManifest
                {
                    Version = "0.9",
                    Files = new List<ResourceFileEntry>
                    {
                        new ResourceFileEntry { Rel = "a.bin", Size = 3, Sha256 = Sha("aaa") },
                        new ResourceFileEntry { Rel = "sub/s.bin", Size = 3, Sha256 = Sha("old") },
                    },
                }, true));
                var localV0 = ResourceSync.LoadLocalManifest(v0Man);
                ok &= Check(!ResourceSync.CanApplyDelta(localV0, deltaFetched), "6d 本地 v0.9 ≠ delta base 1.0 → 不可应用");
                progress.Clear();
                bool okD = ResourceSync.SyncResources(baseUrl, remote, destV0, v0Man,
                    (i, n, rel) => progress.Add($"{i}/{n}:{rel}"));
                ok &= Check(okD && progress.Count == 2 && progress[0] == "1/2:c.bin" && progress[1] == "2/2:sub/s.bin",
                    "6d 回退全量：PlanDiff 只检出 [c.bin, sub/s.bin]（a.bin 相同排除）");
            }
            Debug.Log($"[resourcesync] delta-case ok={ok}");
            if (ok) cases++;
            return ok;
        }

        static bool RunAssetCompilerDelta(string dll, string oldManifest, string src, string outJson, string version)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add(dll);
                psi.ArgumentList.Add("manifest-delta");
                psi.ArgumentList.Add(oldManifest);
                psi.ArgumentList.Add(src);
                psi.ArgumentList.Add("--out");
                psi.ArgumentList.Add(outJson);
                psi.ArgumentList.Add("--version");
                psi.ArgumentList.Add(version);
                using var p = System.Diagnostics.Process.Start(psi);
                string outp = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 && File.Exists(outJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[resourcesync] AssetCompiler manifest-delta 调用失败 {ex.Message}");
                return false;
            }
        }

        static bool Check(bool cond, string label)
        {
            Debug.Log($"[resourcesync]   {label}: {(cond ? "ok" : "FAIL")}");
            return cond;
        }

        // 场景3（8-9-1）：版本比对。本地无 manifest → 过期；Version 相同 → 不过期；
        // Version 不同 → 过期；版本相同但文件被篡改 → 版本门放过但 PlanDiff 文件级兜底检出。
        static bool VersionCase(string root, ref int cases)
        {
            string localDir = Path.Combine(root, "local");
            Directory.CreateDirectory(localDir);
            string manPath = Path.Combine(localDir, ResourceSync.LocalManifestName);
            File.WriteAllBytes(Path.Combine(localDir, "a.bin"), Encoding.ASCII.GetBytes("v1data"));

            var remote = new ResourceManifest
            {
                Version = "1.2.3",
                Files = new List<ResourceFileEntry>
                {
                    new ResourceFileEntry { Rel = "a.bin", Size = 6, Sha256 = Sha("v1data") },
                    new ResourceFileEntry { Rel = "b.bin", Size = 7, Sha256 = Sha("newdata") },
                },
            };

            bool ok = true;
            // 3a 本地无 manifest（首启）→ 过期
            ok &= Check(ResourceSync.IsVersionOutdated(remote, manPath), "3a 本地无清单 → 过期");
            // 3b 本地 Version 相同 → 不过期
            WriteLocalManifest(manPath, "1.2.3");
            ok &= Check(!ResourceSync.IsVersionOutdated(remote, manPath), "3b 版本相同 → 不过期");
            // 3c 本地 Version 不同（版本升级）→ 过期
            WriteLocalManifest(manPath, "1.2.2");
            ok &= Check(ResourceSync.IsVersionOutdated(remote, manPath), "3c 版本不同 → 过期");
            // 3d 版本一致但文件被篡改 → 版本门放行，PlanDiff 检出篡改文件
            WriteLocalManifest(manPath, "1.2.3");
            File.WriteAllBytes(Path.Combine(localDir, "a.bin"), Encoding.ASCII.GetBytes("tampered!!"));
            var need = ResourceSync.PlanDiff(remote, ResourceSync.BuildLocalIndex(localDir));
            ok &= Check(!ResourceSync.IsVersionOutdated(remote, manPath)
                && need.Count == 2 && need[0] == "a.bin" && need[1] == "b.bin",
                "3d 版本同+文件篡改 → 版本门放行且 PlanDiff 检出 [a.bin,b.bin]");
            Debug.Log($"[resourcesync] version-case ok={ok}");
            if (ok) cases++;
            return ok;
        }

        // 场景4（8-9-1）：AssetCompiler manifest 确定性。dotnet 调 AssetCompiler.dll 两次
        // （同输入同 --version）→ Version/Count/TotalBytes/Files 完全一致（GeneratedUtc 忽略）。
        static bool DeterminismCase(string root, ref int cases)
        {
            string src = Path.Combine(root, "src");
            string sub = Path.Combine(src, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllBytes(Path.Combine(src, "a.bin"), Encoding.ASCII.GetBytes("abc"));
            File.WriteAllBytes(Path.Combine(sub, "b.bin"), Encoding.ASCII.GetBytes("x"));

            string dll = AssetCompilerDll();
            if (dll == null)
            {
                Debug.Log($"[resourcesync] determinism-case ok=False dataPath={Application.dataPath} dll 未找到");
                return false;
            }
            string m1 = Path.Combine(root, "m1.json");
            string m2 = Path.Combine(root, "m2.json");
            bool ok = RunAssetCompiler(dll, src, m1, "1.2.3")
                   & RunAssetCompiler(dll, src, m2, "1.2.3");
            ResourceManifest a = null, b = null;
            if (ok)
            {
                a = JsonUtility.FromJson<ResourceManifest>(File.ReadAllText(m1));
                b = JsonUtility.FromJson<ResourceManifest>(File.ReadAllText(m2));
            }
            ok &= Check(a != null && a.Version == "1.2.3", "4a 版本号写入 manifest.Version");
            ok &= Check(a != null && b != null && a.Version == b.Version && a.Count == b.Count
                && a.TotalBytes == b.TotalBytes && EntriesEqual(a.Files, b.Files),
                "4b 两次生成 Files/Version/Count/TotalBytes 一致（确定性）");
            Debug.Log($"[resourcesync] determinism-case ok={ok}");
            if (ok) cases++;
            return ok;
        }

        static bool EntriesEqual(List<ResourceFileEntry> x, List<ResourceFileEntry> y)
        {
            if (x == null || y == null || x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++)
                if (x[i].Rel != y[i].Rel || x[i].Size != y[i].Size
                    || !string.Equals(x[i].Sha256, y[i].Sha256, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        // 仓库根 = Unity/Assets 上溯两级；AssetCompiler 需已 dotnet build -c Release。
        static string AssetCompilerDll()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string dll = Path.Combine(root, "tools", "AssetCompiler", "bin", "Release", "net8.0", "AssetCompiler.dll");
            return File.Exists(dll) ? dll : null;
        }

        static bool RunAssetCompiler(string dll, string src, string outJson, string version)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add(dll);
                psi.ArgumentList.Add("manifest");
                psi.ArgumentList.Add(src);
                psi.ArgumentList.Add("--out");
                psi.ArgumentList.Add(outJson);
                psi.ArgumentList.Add("--version");
                psi.ArgumentList.Add(version);
                using var p = System.Diagnostics.Process.Start(psi);
                string outp = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 && File.Exists(outJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[resourcesync] AssetCompiler 调用失败 {ex.Message}");
                return false;
            }
        }

        static void WriteLocalManifest(string path, string version)
        {
            string json = JsonUtility.ToJson(new ResourceManifest
            {
                Version = version,
                Count = 0,
                Files = new List<ResourceFileEntry>(),
            });
            File.WriteAllText(path, json);
        }

        // 场景5（8-9-2）：SyncResources 批量同步端到端。服务器托管 manifest + 3 文件：
        //  5a 空 dest 全量下载落盘 + manifest 写回 + 进度序列 1/3,2/3,3/3
        //  5b 幂等：再跑无下载（need=0，进度零回调，仍返回 true）
        //  5c 篡改本地 b.bin → 只补 b.bin（进度 1/1）
        //  5d 重试：c.bin 首次 404（FailOnce）→ 重试成功
        //  5e 恒失败：d.bin 不存在 → SyncResources false + 本地 manifest 未写回（版本凭据保持旧值）
        static bool SyncCase(string root, ref int cases)
        {
            string serverRoot = Path.Combine(root, "server");
            string dest = Path.Combine(root, "dest");
            Directory.CreateDirectory(serverRoot);
            Directory.CreateDirectory(dest);
            File.WriteAllBytes(Path.Combine(serverRoot, "a.bin"), Encoding.ASCII.GetBytes("aaa"));
            File.WriteAllBytes(Path.Combine(serverRoot, "b.bin"), Encoding.ASCII.GetBytes("bbb"));
            File.WriteAllBytes(Path.Combine(serverRoot, "c.bin"), Encoding.ASCII.GetBytes("ccc"));
            string manPath = Path.Combine(dest, ResourceSync.LocalManifestName);
            var remote = new ResourceManifest
            {
                Version = "2.0.0",
                Files = new List<ResourceFileEntry>
                {
                    new ResourceFileEntry { Rel = "a.bin", Size = 3, Sha256 = Sha("aaa") },
                    new ResourceFileEntry { Rel = "b.bin", Size = 3, Sha256 = Sha("bbb") },
                    new ResourceFileEntry { Rel = "c.bin", Size = 3, Sha256 = Sha("ccc") },
                },
            };
            File.WriteAllText(Path.Combine(serverRoot, ResourceSync.LocalManifestName),
                JsonUtility.ToJson(remote, true));

            bool ok = true;
            using (var server = new MiniHttpServer(serverRoot))
            {
                string baseUrl = $"http://127.0.0.1:{server.Port}/";
                var progress = new List<string>();

                // 5a 全量
                bool okA = ResourceSync.SyncResources(baseUrl, remote, dest, manPath,
                    (i, n, rel) => progress.Add($"{i}/{n}:{rel}"));
                ok &= Check(okA && File.Exists(Path.Combine(dest, "a.bin")) && File.Exists(Path.Combine(dest, "b.bin"))
                    && File.Exists(Path.Combine(dest, "c.bin")) && File.Exists(manPath)
                    && !ResourceSync.IsVersionOutdated(remote, manPath)
                    && progress.Count == 3 && progress[0] == "1/3:a.bin" && progress[2] == "3/3:c.bin",
                    "5a 空目录全量下载落盘+manifest 写回+进度序列 1/3→3/3");

                // 5b 幂等
                progress.Clear();
                bool okB = ResourceSync.SyncResources(baseUrl, remote, dest, manPath,
                    (i, n, rel) => progress.Add($"{i}/{n}:{rel}"));
                ok &= Check(okB && progress.Count == 0, "5b 幂等：文件全齐 → 零下载仍返回 true");

                // 5c 篡改补差
                File.WriteAllBytes(Path.Combine(dest, "b.bin"), Encoding.ASCII.GetBytes("XXX"));
                progress.Clear();
                bool okC = ResourceSync.SyncResources(baseUrl, remote, dest, manPath,
                    (i, n, rel) => progress.Add($"{i}/{n}:{rel}"));
                ok &= Check(okC && progress.Count == 1 && progress[0] == "1/1:b.bin"
                    && Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dest, "b.bin"))) == "bbb",
                    "5c 篡改 b.bin → 仅补该文件");

                // 5d 瞬时失败重试
                server.FailOnce.Add("c.bin");
                bool okD = ResourceSync.SyncResources(baseUrl, remote, dest, manPath);
                ok &= Check(okD && Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dest, "c.bin"))) == "ccc",
                    "5d 首次 404 → 重试成功");

                // 5e 恒失败：远端清单含 d.bin（服务器无文件）→ 重试后仍失败 + manifest 不更新
                var remoteBad = new ResourceManifest
                {
                    Version = "3.0.0",
                    Files = new List<ResourceFileEntry>
                    {
                        new ResourceFileEntry { Rel = "d.bin", Size = 1, Sha256 = Sha("d") },
                    },
                };
                string oldLocal = File.ReadAllText(manPath);
                bool okE = ResourceSync.SyncResources(baseUrl, remoteBad, dest, manPath);
                ok &= Check(!okE && File.ReadAllText(manPath) == oldLocal, "5e 恒失败 → false 且本地 manifest 未写回");

                // 5f 路径穿越：rel=../evil.bin 必须被拒绝（防逃逸 destDir）
                var remoteEvil = new ResourceManifest
                {
                    Version = "4.0.0",
                    Files = new List<ResourceFileEntry>
                    {
                        new ResourceFileEntry { Rel = "../evil.bin", Size = 1, Sha256 = Sha("x") },
                    },
                };
                string evilParent = Path.Combine(dest, "..", "evil.bin");
                bool okF = ResourceSync.SyncResources(baseUrl, remoteEvil, dest, manPath);
                ok &= Check(!okF && !File.Exists(evilParent), "5f 路径穿越 ../evil.bin 拒绝且未写盘");

                // 5g FetchManifest：成功（服务器 manifest）+ 失败（无服务端口）→ null
                var fetched = ResourceSync.FetchManifest(baseUrl);
                ok &= Check(fetched != null && fetched.Version == "2.0.0", "5g fetch 远端清单成功（Version=2.0.0）");
                ok &= Check(ResourceSync.FetchManifest("http://127.0.0.1:1/") == null, "5g fetch 网络失败 → null");
            }
            Debug.Log($"[resourcesync] sync-case ok={ok}");
            if (ok) cases++;
            return ok;
        }

        static string Sha(string s) => Hex(SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(s)));

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("X2"));
            return sb.ToString();
        }

        // 极简本地静态 HTTP 服务器（TcpListener 单线程逐个服务；供探针端到端验证，非产品服务器）。
        // FailOnce：命中集合中的 rel 首次请求返回 404 并从集合移除（模拟瞬时故障 → 重试语义验证）。
        sealed class MiniHttpServer : IDisposable
        {
            readonly TcpListener _listener;
            readonly string _root;
            readonly Task _loop;
            volatile bool _stop;
            public readonly HashSet<string> FailOnce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public int Port { get; }

            public MiniHttpServer(string root)
            {
                _root = Path.GetFullPath(root);
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _loop = Task.Run(Loop);
            }

            void Loop()
            {
                while (!_stop)
                {
                    try
                    {
                        using var c = _listener.AcceptTcpClient();
                        Serve(c.GetStream());
                    }
                    catch { if (_stop) break; }
                }
            }

            void Serve(NetworkStream s)
            {
                var buf = new byte[4096];
                int n = s.Read(buf, 0, buf.Length);
                if (n <= 0) return;
                string head = Encoding.ASCII.GetString(buf, 0, n);
                string[] parts = head.Split(' '); // GET /path HTTP/1.1
                if (parts.Length < 2 || parts[0] != "GET") { Write(s, "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"); return; }
                string file = Path.GetFullPath(Path.Combine(_root, Uri.UnescapeDataString(parts[1]).TrimStart('/').Replace('/', '\\')));
                string rel = Uri.UnescapeDataString(parts[1]).TrimStart('/');
                if (FailOnce.Remove(rel)) { Write(s, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n"); return; }
                if (!file.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                { Write(s, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n"); return; }
                byte[] body = File.ReadAllBytes(file);
                Write(s, $"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {body.Length}\r\n\r\n");
                s.Write(body, 0, body.Length);
            }

            static void Write(NetworkStream s, string text)
            {
                byte[] b = Encoding.ASCII.GetBytes(text);
                s.Write(b, 0, b.Length);
            }

            public void Dispose()
            {
                _stop = true;
                try { _listener.Stop(); } catch { }
            }
        }
    }
}
