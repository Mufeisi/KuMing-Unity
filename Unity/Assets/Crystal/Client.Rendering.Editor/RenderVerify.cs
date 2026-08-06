using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Crystal.Client.Assets;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // R1-3 门禁：CrystalSpriteBatch 逐帧渲染到 RT 回读 → SHA-256 vs golden 逐像素对照。
    // 验证纹理加载、UV 方向（V 轴反转）、颜色/透明度透传、合批管线的正确性。
    // 单库：CRYSTAL_ATLAS_DIR=<dir> CRYSTAL_VERIFY_LIB=<lib> Unity.exe -batchmode -quit -executeMethod ...Run
    // 批量：CRYSTAL_VERIFY_LIBS="libA,Monster/000,AArmour/00" 同参数 ...RunBatch
    static class RenderVerify
    {
        public static void Run()
        {
            string dir = Environment.GetEnvironmentVariable("CRYSTAL_ATLAS_DIR");
            string lib = Environment.GetEnvironmentVariable("CRYSTAL_VERIFY_LIB");
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(lib))
            {
                Console.WriteLine("render-verify: CRYSTAL_ATLAS_DIR / CRYSTAL_VERIFY_LIB not set");
                EditorApplication.Exit(2);
                return;
            }
            int fail = VerifyLib(Path.GetFullPath(dir), lib);
            EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        public static void RunBatch()
        {
            string dir = Environment.GetEnvironmentVariable("CRYSTAL_ATLAS_DIR");
            string libs = Environment.GetEnvironmentVariable("CRYSTAL_VERIFY_LIBS");
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(libs))
            {
                Console.WriteLine("render-verify: CRYSTAL_ATLAS_DIR / CRYSTAL_VERIFY_LIBS not set");
                EditorApplication.Exit(2);
                return;
            }
            dir = Path.GetFullPath(dir);
            int totalFail = 0;
            foreach (string lib in libs.Split(','))
            {
                if (string.IsNullOrWhiteSpace(lib)) continue;
                totalFail += VerifyLib(dir, lib.Trim());
            }
            Console.WriteLine($"render-verify-batch: totalFail={totalFail}");
            EditorApplication.Exit(totalFail == 0 ? 0 : 1);
        }

        static int VerifyLib(string dir, string lib)
        {
            string manPath = Path.Combine(dir, lib + ".json");
            string goldenPath = Path.Combine(dir, lib + ".golden");
            if (!File.Exists(manPath) || !File.Exists(goldenPath))
            {
                Console.WriteLine($"render-verify: manifest/golden missing for {lib}");
                return 1;
            }

            var golden = LoadGolden(goldenPath);
            var atlas = AtlasLibrary.Load(manPath);

            int maxW = 0;
            for (int i = 0; i < atlas.Frames.Length; i++)
                maxW = Math.Max(maxW, atlas.Frames[i].Width);

            int ok = 0, fail = 0, checked_ = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var sha = SHA256.Create();
            var rowBuf = new byte[Math.Max(1, maxW * 4)];
            try
            {
                for (int i = 0; i < atlas.Frames.Length; i++)
                {
                    var f = atlas.Frames[i];
                    if (f.Empty) continue;
                    if (!golden.TryGetValue(i, out string want))
                    {
                        fail++;
                        Console.WriteLine($"  MISSING golden idx {i}");
                        continue;
                    }
                    checked_++;
                    string got = RenderFrameHash(atlas, f, sha, rowBuf);
                    if (got != want)
                    {
                        fail++;
                        if (fail <= 200) Console.WriteLine($"  MISMATCH idx {i} {f.Width}x{f.Height} pg{f.Page} ({f.X},{f.Y}) want {want.Substring(0, 12)} got {got.Substring(0, 12)}");
                    }
                    else ok++;
                }
            }
            finally
            {
                atlas.UnloadAll();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            sw.Stop();
            Console.WriteLine($"render-verify {lib}: ok={ok} fail={fail} checked={checked_}  {sw.Elapsed.TotalSeconds:F1}s");
            return fail;
        }

        static string RenderFrameHash(AtlasLibrary atlas, SpriteFrame f, SHA256 sha, byte[] rowBuf)
        {
            var tex = atlas.GetPage(f.Page);
            int w = f.Width, h = f.Height;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            try
            {
                CrystalSpriteBatch.ReplaceBlend = true;
                CrystalSpriteBatch.Begin(rt, w, h);
                CrystalSpriteBatch.Draw(tex, new Rect(f.X, f.Y, w, h), Vector3.zero, Color.white);
                CrystalSpriteBatch.End();

                var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                sha.Initialize();
                int rowBytes = w * 4;
                for (int y = 0; y < h; y++)
                {
                    // ReadPixels 行序即 top-down（RenderDump 实证：px 行序 hash == golden，无需翻转）。
                    int bi = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        Color32 c = px[bi + x];
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
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
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
