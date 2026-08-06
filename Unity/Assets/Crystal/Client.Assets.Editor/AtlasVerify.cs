using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Crystal.Client.Assets;
using UnityEditor;
using UnityEngine;

namespace Crystal.Assets.Editor
{
    // 阶段 3 门禁（G2 前驱）：Unity 运行时读取图集 vs golden（AssetCompiler 从图集页提取的 SHA-256）逐像素对照。
    // 用法：CRYSTAL_ATLAS_DIR=<assetcompile/all> Unity.exe -batchmode -nographics -executeMethod Crystal.Assets.Editor.AtlasVerify.Run
    // 覆盖全部 *.json 清单；逐库处理并释放纹理；最后 EditorApplication.Exit(0/1)。
    static class AtlasVerify
    {
        public static void Run()
        {
            string dir = Environment.GetEnvironmentVariable("CRYSTAL_ATLAS_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                Console.WriteLine("atlas-verify: CRYSTAL_ATLAS_DIR not set");
                EditorApplication.Exit(2);
                return;
            }
            dir = Path.GetFullPath(dir);
            var mans = new List<string>(Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories));
            mans.Sort(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"atlas-verify: {mans.Count} manifests from {dir}");

            int ok = 0, fail = 0, missing = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var man in mans)
            {
                string rel = Path.GetFileNameWithoutExtension(man);
                string goldenPath = Path.Combine(Path.GetDirectoryName(man), rel + ".golden");
                if (!File.Exists(goldenPath))
                {
                    missing++;
                    Console.WriteLine($"  MISSING golden: {rel}");
                    continue;
                }
                if (VerifyLib(man, goldenPath, out int bad, out int checked_))
                {
                    ok++;
                    Console.WriteLine($"  {rel}: verify OK ({checked_} images)");
                }
                else
                {
                    fail++;
                    Console.WriteLine($"  {rel}: FAIL {bad}/{checked_} mismatches");
                }
            }
            sw.Stop();
            Console.WriteLine($"atlas-verify ok={ok} fail={fail} missing={missing}  {sw.Elapsed.TotalSeconds:F1}s");
            EditorApplication.Exit(fail == 0 && missing == 0 ? 0 : 1);
        }

        static bool VerifyLib(string manifestPath, string goldenPath, out int bad, out int checked_)
        {
            bad = 0;
            checked_ = 0;
            try
            {
                var golden = LoadGolden(goldenPath);
                var lib = AtlasLibrary.Load(manifestPath);
                var pagePx = new Dictionary<int, Color32[]>();
                using var sha = SHA256.Create();
                int maxPageW = 0;
                foreach (var p in lib.Manifest.Pages) maxPageW = Math.Max(maxPageW, p.W);
                var rowBuf = new byte[maxPageW * 4];
                try
                {
                    for (int i = 0; i < lib.Frames.Length; i++)
                    {
                        var f = lib.Frames[i];
                        if (f.Empty) continue;
                        if (!golden.TryGetValue(i, out string want))
                        {
                            bad++;
                            if (bad <= 10) Console.WriteLine($"  MISSING golden idx {i}");
                            continue;
                        }
                        checked_++;
                        string got = HashFrame(lib, f, pagePx, sha, rowBuf);
                        if (got != want)
                        {
                            bad++;
                            if (bad <= 10) Console.WriteLine($"  MISMATCH idx {i} want {want} got {got}");
                        }
                    }
                }
                finally
                {
                    lib.UnloadAll();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                return bad == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {Path.GetFileNameWithoutExtension(manifestPath)}: {ex.GetType().Name} {ex.Message}");
                bad = -1;
                return false;
            }
        }

        // 从图集页 rect 提取 RGBA → SHA-256 hex（与 golden 生成一致：非预乘，top-down 行序）。
        // 注意：Unity GetPixels32 返回垂直翻转纹理（row 0 = 图下缘），故用 tex.height-1 补偿。
        // 性能：每页像素数组只取一次缓存复用；SHA256 与行缓冲跨帧复用，避免 224 万帧的逐帧分配。
        static string HashFrame(AtlasLibrary lib, SpriteFrame f, Dictionary<int, Color32[]> pagePx,
            SHA256 sha, byte[] rowBuf)
        {
            var tex = lib.GetPage(f.Page);
            if (!pagePx.TryGetValue(f.Page, out var px))
            {
                px = tex.GetPixels32();
                pagePx[f.Page] = px;
            }
            sha.Initialize();
            int stride = tex.width;
            int rowBytes = f.Width * 4;
            for (int y = 0; y < f.Height; y++)
            {
                int row = (tex.height - 1 - (f.Y + y)) * stride + f.X;
                for (int x = 0; x < f.Width; x++)
                {
                    Color32 c = px[row + x];
                    rowBuf[x * 4] = c.r;
                    rowBuf[x * 4 + 1] = c.g;
                    rowBuf[x * 4 + 2] = c.b;
                    rowBuf[x * 4 + 3] = c.a;
                }
                sha.TransformBlock(rowBuf, 0, rowBytes, null, 0);
            }
            sha.TransformFinalBlock(rowBuf, 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "");
        }

        static Dictionary<int, string> LoadGolden(string path)
        {
            var d = new Dictionary<int, string>();
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0) continue;
                int sp = line.IndexOf(' ');
                if (sp <= 0) continue;
                d[int.Parse(line.Substring(0, sp))] = line.Substring(sp + 1).Trim();
            }
            return d;
        }
    }
}
