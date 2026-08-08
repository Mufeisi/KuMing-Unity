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
                       & DeterminismCase(Path.Combine(root, "det"), ref cases);
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

        static string Sha(string s) => Hex(SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(s)));

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("X2"));
            return sb.ToString();
        }

        // 极简本地静态 HTTP 服务器（TcpListener 单线程逐个服务；供探针端到端验证，非产品服务器）。
        sealed class MiniHttpServer : IDisposable
        {
            readonly TcpListener _listener;
            readonly string _root;
            readonly Task _loop;
            volatile bool _stop;

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
