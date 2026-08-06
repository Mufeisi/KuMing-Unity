using System;
using System.IO;
using System.Security.Cryptography;
using Crystal.Client.Assets;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // 诊断：渲染帧 0 → 回读两种行序哈希 vs 从图集页提取的"预期"像素（AtlasVerify 已验证的翻转补偿）。
    // 用法：CRYSTAL_ATLAS_DIR=<dir> CRYSTAL_VERIFY_LIB=<lib> Unity.exe -batchmode -quit -executeMethod ...RenderDump.Run
    static class RenderDump
    {
        public static void Run()
        {
            string dir = Path.GetFullPath(Environment.GetEnvironmentVariable("CRYSTAL_ATLAS_DIR"));
            string lib = Environment.GetEnvironmentVariable("CRYSTAL_VERIFY_LIB");
            string idxStr = Environment.GetEnvironmentVariable("CRYSTAL_VERIFY_IDX");
            var atlas = AtlasLibrary.Load(Path.Combine(dir, lib + ".json"));

            // 找一个有内容的帧（或用 CRYSTAL_VERIFY_IDX 指定）
            int idx = idxStr != null ? int.Parse(idxStr) : -1;
            if (idx < 0)
            {
                for (int i = 0; i < atlas.Frames.Length; i++)
                    if (!atlas.Frames[i].Empty && atlas.Frames[i].Width > 2 && atlas.Frames[i].Height > 2) { idx = i; break; }
                if (idx < 0) { Console.WriteLine("no suitable frame"); EditorApplication.Exit(2); return; }
            }
            if (idx >= atlas.Frames.Length) { Console.WriteLine($"idx {idx} out of range"); EditorApplication.Exit(2); return; }

            var f = atlas.Frames[idx];
            Console.WriteLine($"frame {idx} {f.Width}x{f.Height} page={f.Page} at ({f.X},{f.Y})");

            // 预期像素：从图集页 GetPixels32 提取（row = tex.height-1-(f.Y+y)，即 PNG top-down）
            var pageTex = atlas.GetPage(f.Page);
            var pagePx = pageTex.GetPixels32();
            int pw = pageTex.width, ph = pageTex.height;
            var expected = new byte[f.Width * f.Height * 4];
            for (int y = 0; y < f.Height; y++)
            {
                int srcRow = (ph - 1 - (f.Y + y)) * pw + f.X;
                for (int x = 0; x < f.Width; x++)
                {
                    var c = pagePx[srcRow + x];
                    int p = (y * f.Width + x) * 4;
                    expected[p] = c.r; expected[p + 1] = c.g; expected[p + 2] = c.b; expected[p + 3] = c.a;
                }
            }

            // 渲染回读
            var rt = RenderTexture.GetTemporary(f.Width, f.Height, 24, RenderTextureFormat.ARGB32);
            CrystalSpriteBatch.ReplaceBlend = true;
            CrystalSpriteBatch.Begin(rt, f.Width, f.Height);
            CrystalSpriteBatch.Draw(pageTex, new Rect(f.X, f.Y, f.Width, f.Height), Vector3.zero, Color.white);
            CrystalSpriteBatch.End();
            var read = new Texture2D(f.Width, f.Height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, f.Width, f.Height), 0, 0);
            read.Apply();
            var px = read.GetPixels32();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            // 回读两种行序哈希
            string hashFlip = HashRows(px, f.Width, f.Height, true);    // y -> h-1-y
            string hashNoFlip = HashRows(px, f.Width, f.Height, false); // y -> y
            string hashExp = HashBytes(expected);
            Console.WriteLine($"expected   {hashExp}");
            Console.WriteLine($"got(flip)  {hashFlip}  match={hashFlip == hashExp}");
            Console.WriteLine($"got(noflip){hashNoFlip}  match={hashNoFlip == hashExp}");

            // 像素行对比：首行 + 末行 前 8 像素
            Console.WriteLine("row0 expected vs got(noflip):");
            for (int x = 0; x < Math.Min(8, f.Width); x++)
            {
                int pe = x * 4;
                int pg = 0 * f.Width + x;
                Console.Write($" exp{expected[pe]:X2}{expected[pe+1]:X2}{expected[pe+2]:X2}{expected[pe+3]:X2}" +
                              $" got{px[pg].r:X2}{px[pg].g:X2}{px[pg].b:X2}{px[pg].a:X2}  ");
            }
            Console.WriteLine();
            Console.WriteLine("rowLast expected vs got(noflip):");
            for (int x = 0; x < Math.Min(8, f.Width); x++)
            {
                int y = f.Height - 1;
                int pe = (y * f.Width + x) * 4;
                int pg = y * f.Width + x;
                Console.Write($" exp{expected[pe]:X2}{expected[pe+1]:X2}{expected[pe+2]:X2}{expected[pe+3]:X2}" +
                              $" got{px[pg].r:X2}{px[pg].g:X2}{px[pg].b:X2}{px[pg].a:X2}  ");
            }
            Console.WriteLine();

            // 间隔 10% 的行采样：expected vs got(noflip) 全行字节对比，找首个差异行
            int diffRow = -1;
            for (int y = 0; y < f.Height && diffRow < 0; y++)
            {
                for (int x = 0; x < f.Width; x++)
                {
                    int pe = (y * f.Width + x) * 4;
                    var c = px[y * f.Width + x];
                    if (expected[pe] != c.r || expected[pe + 1] != c.g || expected[pe + 2] != c.b || expected[pe + 3] != c.a)
                    {
                        diffRow = y;
                        break;
                    }
                }
            }
            Console.WriteLine($"firstDiffRow={diffRow}");

            // 垂直移位互相关：expected[y] vs px[y+k]，统计最佳 k
            int bestK = 0, bestCnt = -1;
            for (int k = -40; k <= 40; k++)
            {
                int cnt = 0, tot = 0;
                for (int y = 0; y < f.Height; y++)
                {
                    int sy = y + k;
                    if (sy < 0 || sy >= f.Height) continue;
                    for (int x = 0; x < f.Width; x++)
                    {
                        int pe = (y * f.Width + x) * 4;
                        var c = px[sy * f.Width + x];
                        if (expected[pe] == c.r && expected[pe + 1] == c.g && expected[pe + 2] == c.b && expected[pe + 3] == c.a) cnt++;
                        tot++;
                    }
                }
                if (cnt > bestCnt) { bestCnt = cnt; bestK = k; }
                if (k == 0) Console.WriteLine($"shift0 match {cnt}/{tot}");
            }
            Console.WriteLine($"bestShiftK={bestK} match {bestCnt}/{(f.Width * f.Height)}");

            // expected 顶部/底部透明行数
            int topTransparent = 0;
            while (topTransparent < f.Height)
            {
                int pe = (topTransparent * f.Width) * 4;
                if (expected[pe + 3] != 0) break;
                topTransparent++;
            }
            int botTransparent = 0;
            while (botTransparent < f.Height)
            {
                int pe = ((f.Height - 1 - botTransparent) * f.Width) * 4;
                if (expected[pe + 3] != 0) break;
                botTransparent++;
            }
            Console.WriteLine($"topTransparentRows={topTransparent} botTransparentRows={botTransparent}");

            // 差异像素坐标分布：列出首个 20 个 + 行列范围
            int diffCnt = 0, minR = f.Height, maxR = -1, minC = f.Width, maxC = -1;
            for (int y = 0; y < f.Height; y++)
            {
                for (int x = 0; x < f.Width; x++)
                {
                    int pe = (y * f.Width + x) * 4;
                    var c = px[y * f.Width + x];
                    if (expected[pe] != c.r || expected[pe + 1] != c.g || expected[pe + 2] != c.b || expected[pe + 3] != c.a)
                    {
                        diffCnt++;
                        if (diffCnt <= 20)
                            Console.WriteLine($"  diff ({y},{x}) exp{expected[pe]:X2}{expected[pe+1]:X2}{expected[pe+2]:X2}{expected[pe+3]:X2} got{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}");
                        minR = Math.Min(minR, y); maxR = Math.Max(maxR, y);
                        minC = Math.Min(minC, x); maxC = Math.Max(maxC, x);
                    }
                }
            }
            Console.WriteLine($"diffPx={diffCnt} rowRange=[{minR},{maxR}] colRange=[{minC},{maxC}]");

            atlas.UnloadAll();
            EditorApplication.Exit(0);
        }

        static string HashRows(Color32[] px, int w, int h, bool flip)
        {
            using var sha = SHA256.Create();
            var buf = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                int srcRow = (flip ? h - 1 - y : y) * w;
                for (int x = 0; x < w; x++)
                {
                    var c = px[srcRow + x];
                    buf[x * 4] = c.r; buf[x * 4 + 1] = c.g; buf[x * 4 + 2] = c.b; buf[x * 4 + 3] = c.a;
                }
                sha.TransformBlock(buf, 0, w * 4, null, 0);
            }
            sha.TransformFinalBlock(buf, 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "");
        }

        static string HashBytes(byte[] b)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "");
        }
    }
}
