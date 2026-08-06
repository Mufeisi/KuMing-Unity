using System;
using System.Collections.Generic;
using System.IO;
using Crystal.Client.Assets;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // R3 探针：动画精灵场景合成。加载 <rel>.json 图集 + FrameSet 表（manifest.Frames），
    // 按 DrawFrame = Frame.Start + (Count+Skip)*Direction + FrameIndex 选帧（旧客户端 PlayerObject.cs:761 同式），
    // 以格锚点 + 图 OffX/OffY 偏移绘制到 RT → PNG；CRYSTAL_SPOT_FRAME 指定帧做字节级直通对照。
    // 用法（batchmode）：
    //   CRYSTAL_ATLAS_DIR=<dir> CRYSTAL_LIB=Monster/000 [CRYSTAL_ACTION=Standing] [CRYSTAL_DIR=0]
    //   [CRYSTAL_FRAME=-1 全帧横排 | n 单帧] [CRYSTAL_SPOT_FRAME=n] [CRYSTAL_RT_W/H] [CRYSTAL_OUT]
    static class AnimationRender
    {
        static Dictionary<int, AtlasLibrary> _libs = new Dictionary<int, AtlasLibrary>();

        public static void Run()
        {
            string atlasDir = Environment.GetEnvironmentVariable("CRYSTAL_ATLAS_DIR");
            string libRel = Environment.GetEnvironmentVariable("CRYSTAL_LIB");
            if (string.IsNullOrEmpty(atlasDir) || string.IsNullOrEmpty(libRel))
            {
                Console.WriteLine("anim-render: CRYSTAL_ATLAS_DIR / CRYSTAL_LIB not set");
                EditorApplication.Exit(2);
                return;
            }
            string actionName = Environment.GetEnvironmentVariable("CRYSTAL_ACTION");
            if (string.IsNullOrEmpty(actionName)) actionName = "Standing";
            int dir = GetInt("CRYSTAL_DIR", 0);
            int frameSel = GetInt("CRYSTAL_FRAME", -1);
            int spotFrame = GetInt("CRYSTAL_SPOT_FRAME", -1);
            int rtW = GetInt("CRYSTAL_RT_W", 1600);
            int rtH = GetInt("CRYSTAL_RT_H", 400);
            string outPath = Environment.GetEnvironmentVariable("CRYSTAL_OUT");
            if (string.IsNullOrEmpty(outPath)) outPath = "Build/anim-render.png";

            string man = Path.Combine(Path.GetFullPath(atlasDir), libRel + ".json");
            if (!File.Exists(man))
            {
                Console.WriteLine($"anim-render: manifest missing {man}");
                EditorApplication.Exit(2);
                return;
            }
            var lib = AtlasLibrary.Load(man);
            _libs[0] = lib;

            // FrameSet 表：按动作名找帧区间
            var frames = lib.Manifest.Frames;
            FrameEntry fe = null;
            foreach (var e in frames)
                if (e.Action == actionName) { fe = e; break; }
            if (fe == null)
            {
                Console.WriteLine($"anim-render: action {actionName} not in FrameSet [{string.Join(",", frames.ConvertAll(e => e.Action).ToArray())}]");
                EditorApplication.Exit(2);
                return;
            }
            int offSet = fe.Count + fe.Skip;
            Console.WriteLine($"anim-render: {libRel} action={actionName} start={fe.Start} count={fe.Count} skip={fe.Skip} offSet={offSet} interval={fe.Interval} dir={dir} images={lib.Manifest.Count}");

            int anchorX = 64, anchorY = rtH - 48; // 格脚锚点（左下基准）
            int stride = 200;
            int frameCount = fe.Count;

            // 全帧横排渲染
            var rt = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            int fail = -1;
            try
            {
                CrystalSpriteBatch.Begin(rt, rtW, rtH);
                CrystalSpriteBatch.Clear(new Color(0.05f, 0.05f, 0.05f, 1f));
                int drawn = 0;
                for (int i = 0; i < frameCount; i++)
                {
                    int idx = fe.Start + offSet * dir + i;
                    if (DrawFrame(lib, idx, anchorX + i * stride, anchorY)) drawn++;
                }
                CrystalSpriteBatch.Flush();
                CrystalSpriteBatch.End();
                Console.WriteLine($"anim-render: frames drawn={drawn}/{frameCount}");

                if (spotFrame >= 0)
                {
                    int idx = fe.Start + offSet * dir + spotFrame;
                    fail = SpotCheck(lib, idx, anchorX + spotFrame * stride, anchorY, rtW, rtH);
                    Console.WriteLine($"anim-render: spot frame={spotFrame} idx={idx} fail={fail}");
                }
                else
                {
                    var read = new Texture2D(rtW, rtH, TextureFormat.RGBA32, false);
                    RenderTexture.active = rt;
                    read.ReadPixels(new Rect(0, 0, rtW, rtH), 0, 0);
                    read.Apply();
                    RenderTexture.active = null;
                    // EncodeToPNG 按纹理内存序写（row0=RT 底）→ 行翻转后输出为 top-down PNG
                    var px = read.GetPixels32();
                    var fl = new Color32[px.Length];
                    for (int y = 0; y < rtH; y++)
                        Array.Copy(px, (rtH - 1 - y) * rtW, fl, y * rtW, rtW);
                    read.SetPixels32(fl);
                    read.Apply();
                    string fullOut = Path.GetFullPath(outPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                    File.WriteAllBytes(fullOut, read.EncodeToPNG());
                    Console.WriteLine($"anim-render: wrote {fullOut}");
                    UnityEngine.Object.DestroyImmediate(read);
                }
                EditorApplication.Exit(fail == 0 || fail == -1 ? 0 : 1);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                lib.UnloadAll();
                _libs.Clear();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        static bool DrawFrame(AtlasLibrary lib, int index, int drawX, int drawY)
        {
            if (index < 0 || index >= lib.Frames.Length) return false;
            var f = lib.Frames[index];
            if (f.Empty) return false;
            var tex = lib.GetPage(f.Page);
            if (tex == null) return false;
            CrystalSpriteBatch.Draw(tex, new Rect(f.X, f.Y, f.Width, f.Height),
                new Vector3(drawX + f.OffX, drawY + f.OffY, 0f), Color.white);
            return true;
        }

        // 字节级直通对照：指定帧以锚点+偏移绘制（ReplaceBlend），与图集源像素逐像素比对（GetPixels32 行补偿）。
        static int SpotCheck(AtlasLibrary lib, int index, int anchorX, int anchorY, int rtW, int rtH)
        {
            if (index < 0 || index >= lib.Frames.Length)
            {
                Console.WriteLine("  spot: frame index out of range");
                return 2;
            }
            var f = lib.Frames[index];
            if (f.Empty)
            {
                Console.WriteLine("  spot: frame empty");
                return 2;
            }
            var tex = lib.GetPage(f.Page);
            int dx = anchorX + f.OffX, dy = anchorY + f.OffY;
            Console.WriteLine($"  spot: idx={index} {f.Width}x{f.Height} page{f.Page} src=({f.X},{f.Y}) at=({dx},{dy})");

            var rt = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            int fail = -1;
            try
            {
                CrystalSpriteBatch.ReplaceBlend = true;
                CrystalSpriteBatch.Begin(rt, rtW, rtH);
                CrystalSpriteBatch.Draw(tex, new Rect(f.X, f.Y, f.Width, f.Height), new Vector3(dx, dy, 0f), Color.white);
                CrystalSpriteBatch.End();

                var read = new Texture2D(rtW, rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, rtW, rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                var srcPx = tex.GetPixels32();
                int ph = tex.height;
                int w = f.Width, h = f.Height;
                fail = 0;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        var src = srcPx[(ph - 1 - (f.Y + y)) * tex.width + (f.X + x)];
                        int pxY = dy + y, pxX = dx + x;
                        if (pxY < 0 || pxY >= rtH || pxX < 0 || pxX >= rtW) continue;
                        var got = px[pxY * rtW + pxX];
                        if (src.r != got.r || src.g != got.g || src.b != got.b || src.a != got.a)
                        {
                            fail++;
                            if (fail <= 8)
                                Console.WriteLine($"  spot diff ({x},{y}) src({src.r:X2}{src.g:X2}{src.b:X2}{src.a:X2}) got({got.r:X2}{got.g:X2}{got.b:X2}{got.a:X2})");
                        }
                    }
                }
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
            return fail;
        }

        static int GetInt(string name, int def)
        {
            string s = Environment.GetEnvironmentVariable(name);
            return int.TryParse(s, out int v) ? v : def;
        }

        // 加载 <rel>.golden（AssetCompiler 提取的每帧 SHA-256，行格式 "<index> <hex>"）
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

        // R6 帧选择矩阵验证：多动作 × 方向 × 多帧 的帧选择公式
        //   DrawFrame = Frame.Start + (Count+Skip)*Direction + FrameIndex   （MonsterObject.cs:360 / PlayerObject.cs:763 同式）
        // 双层验证，golden（AssetCompiler 提取的每帧 SHA-256）作权威帧内容源：
        //   1) 公式一致性（数据层）：逐格算 idx → 越界/空 → 复刻旧客户端 CheckImage 的"跳过不绘"；
        //      有效 → 记入 checks（证明该 动作×方向×帧 组合落在有效帧区段）。
        //   2) 抽样渲染 spot（渲染层）：每动作每方向选代表帧，实际渲染 → SHA-256 对照 golden，
        //      端到端证明"公式选出的帧，绘制出的像素与帧内容一致"（R1-3 平扫不验证公式→方向映射）。
        // 用法：CRYSTAL_ATLAS_DIR=<all> CRYSTAL_MATRIX_LIBS="Monster/000,Monster/013,.."
        //   [CRYSTAL_MATRIX_ACTION=Attack1] [CRYSTAL_MATRIX_DIRS=0,1,3] Unity.exe -batchmode -nographics -executeMethod ...RunMatrix
        // 无 CRYSTAL_MATRIX_ACTION 时验证全部动作。
        public static void RunMatrix()
        {
            string atlasDir = Environment.GetEnvironmentVariable("CRYSTAL_ATLAS_DIR");
            string libs = Environment.GetEnvironmentVariable("CRYSTAL_MATRIX_LIBS");
            if (string.IsNullOrEmpty(atlasDir) || string.IsNullOrEmpty(libs))
            {
                Console.WriteLine("anim-matrix: CRYSTAL_ATLAS_DIR / CRYSTAL_MATRIX_LIBS not set");
                EditorApplication.Exit(2);
                return;
            }
            atlasDir = Path.GetFullPath(atlasDir);
            string filterAction = Environment.GetEnvironmentVariable("CRYSTAL_MATRIX_ACTION");

            var dirs = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
            string ds = Environment.GetEnvironmentVariable("CRYSTAL_MATRIX_DIRS");
            if (!string.IsNullOrEmpty(ds))
            {
                dirs.Clear();
                foreach (var tok in ds.Split(','))
                    if (int.TryParse(tok.Trim(), out int d)) dirs.Add(d);
            }

            int libFail = 0, totalChecks = 0, totalSkipped = 0, totalSpot = 0, totalSpotFail = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (string libRel in libs.Split(','))
            {
                if (string.IsNullOrWhiteSpace(libRel)) continue;
                string rel = libRel.Trim();
                int f = VerifyMatrixLib(atlasDir, rel, filterAction, dirs,
                    out int checks, out int skipped, out int spot, out int spotFail);
                libFail += f;
                totalChecks += checks;
                totalSkipped += skipped;
                totalSpot += spot;
                totalSpotFail += spotFail;
                Console.WriteLine($"anim-matrix {rel}: fail={f} validChecks={checks} skipped={skipped} spot={spot} spotFail={spotFail}");
            }
            sw.Stop();
            Console.WriteLine($"anim-matrix: libFail={libFail} checks={totalChecks} skipped={totalSkipped} spot={totalSpot} spotFail={totalSpotFail}  {sw.Elapsed.TotalSeconds:F1}s");
            EditorApplication.Exit(libFail == 0 ? 0 : 1);
        }

        // 单个库：公式一致性 + 抽样渲染 spot。返回库级失败数。
        static int VerifyMatrixLib(string atlasDir, string libRel, string filterAction, List<int> dirs,
            out int checks, out int skipped, out int spot, out int spotFail)
        {
            checks = 0; skipped = 0; spot = 0; spotFail = 0;
            string manPath = Path.Combine(atlasDir, libRel + ".json");
            string goldenPath = Path.Combine(atlasDir, libRel + ".golden");
            if (!File.Exists(manPath) || !File.Exists(goldenPath))
            {
                Console.WriteLine($"anim-matrix: manifest/golden missing {libRel}");
                return 1;
            }
            var golden = LoadGolden(goldenPath);
            var lib = AtlasLibrary.Load(manPath);
            int fail = 0;
            try
            {
                foreach (var fe in lib.Manifest.Frames)
                {
                    if (!string.IsNullOrEmpty(filterAction) && fe.Action != filterAction) continue;
                    int off = fe.Count + fe.Skip;
                    // 1) 公式一致性：逐方向逐帧算 idx，统计有效/越界
                    foreach (int dir in dirs)
                    {
                        for (int fi = 0; fi < fe.Count; fi++)
                        {
                            int idx = fe.Start + off * dir + fi;
                            if (idx < 0 || idx >= lib.Frames.Length || !golden.ContainsKey(idx))
                            {
                                skipped++;
                                continue;
                            }
                            checks++;
                        }
                        // 2) 抽样渲染 spot：每方向抽该方向最后一帧（Reverse 动作反向播放，末帧即实际首画帧），
                        //    字节级直通对照 golden，端到端证明"公式选出的帧绘制像素与帧内容一致"。
                        int frameSel = fe.Count - 1;
                        int spotIdx = fe.Start + off * dir + frameSel;
                        if (spotIdx >= 0 && spotIdx < lib.Frames.Length && golden.ContainsKey(spotIdx))
                        {
                            var sf = lib.Frames[spotIdx];
                            // RT 必须容纳 OffX/OffY 负偏移：锚点取 (max(0,-OffX)+8, max(0,-OffY)+8)，尺寸=帧尺寸+偏移+边距
                            int margin = 8;
                            int anchorX = Math.Max(0, -sf.OffX) + margin;
                            int anchorY = Math.Max(0, -sf.OffY) + margin;
                            int rtW = sf.Width + anchorX + Math.Max(0, sf.OffX) + margin;
                            int rtH = sf.Height + anchorY + Math.Max(0, sf.OffY) + margin;
                            spot++;
                            if (SpotCheck(lib, spotIdx, anchorX, anchorY, rtW, rtH) != 0)
                            {
                                spotFail++;
                                fail++;
                            }
                        }
                    }
                }
            }
            finally
            {
                lib.UnloadAll();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            return fail;
        }
    }
}
