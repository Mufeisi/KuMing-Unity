using System;
using System.Collections.Generic;
using System.IO;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // R5 探针：灯光管线复刻（GameScene.DrawLights + DXManager.CreateLights 语义）。
    // 旧客户端合成：①lightRT 清为暗色（Night 黑/map 色、Evening/Dawn (50,50,50)、Day 白）；
    //   ②additive（SrcAlpha,One）画光源径向渐变图（LightSizes 11 档，tint lightColour 染色）；
    //   ③整幅 Zero/SourceColor（multiply，dest=dest*src）乘回场景（夜/黄昏调暗）。
    // 光源图（旧客户端 GDI+ PathGradientBrush，DXManager.cs:152-216）在 Unity 侧用 CPU 椭圆射线
    //   径向渐变（同一色标 stops [White,(210),(160),(70),(40),transparent]@[0,.2,.4,.6,.8,1.0]，
    //   椭圆边界 t = sqrt((ry*dx)^2+(rx*dy)^2)/(rx*ry)）。GDI+ 按路径三角网格 Gouraud 插值，
    //   逐像素精确复刻留待 golden baseline（阶段 4），本探针验证混合语义字节级。
    // CPU 期望：lightTex = clamp(dark + Σ grad.rgb*tint.rgb*grad.a/65025)；final = scene*lightTex/255。
    // 用法（batchmode 经 Hub 会话）：
    //   CRYSTAL_LIGHTS="<cx>,<cy>,<sizeIdx>[,<tintR,tintG,tintB>];..."（分号分隔多灯）
    //   [CRYSTAL_RT_W=320] [CRYSTAL_RT_H=200] [CRYSTAL_DARKNESS="r,g,b"] [CRYSTAL_OUT]
    static class LightRender
    {
        // LightSizes（DXManager.cs:43-56）：idx1..10，Lights[0] 不存在
        static readonly int[] LightW = { 0, 205, 285, 365, 445, 525, 605, 685, 765, 845, 925 };
        static readonly int[] LightH = { 0, 156, 217, 277, 338, 399, 460, 521, 581, 642, 703 };
        // 色标（t → r,g,b,a）
        static readonly float[] T = { 0f, .2f, .4f, .6f, .8f, 1f };
        static readonly byte[,] Stops = {
            {255,255,255,255}, {255,210,210,210}, {255,160,160,160},
            {255,70,70,70}, {255,40,40,40}, {0,0,0,0} };

        class Light
        {
            public int Cx, Cy, SizeIdx, Tr = 255, Tg = 255, Tb = 255;
            public int X0, Y0, W, H;
        }

        static readonly Dictionary<int, Texture2D> _glowCache = new Dictionary<int, Texture2D>();
        static readonly Dictionary<int, Color32[]> _glowPx = new Dictionary<int, Color32[]>();

        public static void Run()
        {
            int rtW = GetInt("CRYSTAL_RT_W", 320);
            int rtH = GetInt("CRYSTAL_RT_H", 200);
            int darkR = 20, darkG = 20, darkB = 20;
            string darkSpec = Environment.GetEnvironmentVariable("CRYSTAL_DARKNESS");
            if (!string.IsNullOrEmpty(darkSpec))
            {
                var p = darkSpec.Split(',');
                darkR = int.Parse(p[0]); darkG = int.Parse(p[1]); darkB = int.Parse(p[2]);
            }
            string lightSpec = Environment.GetEnvironmentVariable("CRYSTAL_LIGHTS");
            if (string.IsNullOrEmpty(lightSpec))
            {
                Console.WriteLine("light-render: CRYSTAL_LIGHTS not set");
                EditorApplication.Exit(2);
                return;
            }
            string outPath = Environment.GetEnvironmentVariable("CRYSTAL_OUT");
            if (string.IsNullOrEmpty(outPath)) outPath = "Build/light-render.png";

            // sanduan Light.shader 脉冲模式：CRYSTAL_TIME=<秒> 时，每灯 tint 经 LightPulse.Modulate 调制，
            // CPU 期望同走脉冲公式 → 字节级验证闪烁语义。未设时保持 R5 静态行为。
            float timeSec = float.NaN;
            string timeSpec = Environment.GetEnvironmentVariable("CRYSTAL_TIME");
            bool timeSet = !string.IsNullOrEmpty(timeSpec) && float.TryParse(timeSpec, out timeSec);
            if (timeSet)
                Console.WriteLine($"light-render: pulse t={timeSec}s brightness={LightPulse.Brightness(timeSec)} alpha={LightPulse.Alpha(timeSec)}");

            var lights = new List<Light>();
            foreach (string tok in lightSpec.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(tok)) continue;
                var p = tok.Split(',');
                if (p.Length != 3 && p.Length != 6)
                {
                    Console.WriteLine($"light-render: bad light [{tok}] (cx,cy,sizeIdx[,tintR,tintG,tintB])");
                    EditorApplication.Exit(2);
                    return;
                }
                var l = new Light { Cx = int.Parse(p[0]), Cy = int.Parse(p[1]), SizeIdx = int.Parse(p[2]) };
                if (l.SizeIdx < 1 || l.SizeIdx >= LightW.Length)
                {
                    Console.WriteLine($"light-render: sizeIdx {l.SizeIdx} out of 1..{LightW.Length - 1}");
                    EditorApplication.Exit(2);
                    return;
                }
                l.W = LightW[l.SizeIdx]; l.H = LightH[l.SizeIdx];
                if (p.Length == 6) { l.Tr = int.Parse(p[3]); l.Tg = int.Parse(p[4]); l.Tb = int.Parse(p[5]); }
                l.X0 = l.Cx - l.W / 2; l.Y0 = l.Cy - l.H / 2;
                lights.Add(l);
            }
            Console.WriteLine($"light-render: {rtW}x{rtH} dark=({darkR},{darkG},{darkB}) lights={lights.Count}");
            foreach (var l in lights)
                Console.WriteLine($"  light cx={l.Cx} cy={l.Cy} size={l.W}x{l.H} tint=({l.Tr},{l.Tg},{l.Tb}) topleft=({l.X0},{l.Y0})");

            var sceneTex = MakeScene(rtW, rtH);
            var sceneRT = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            var lightRT = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            int fail = -1;
            try
            {
                // ①场景：整幅画 checkerboard（white 纹理全屏，Point 过滤无缩放走样）
                CrystalSpriteBatch.Begin(sceneRT, rtW, rtH);
                CrystalSpriteBatch.Clear(new Color(0f, 0f, 0f, 1f));
                CrystalSpriteBatch.Draw(sceneTex, new Rect(0, 0, rtW, rtH), Vector3.zero, Color.white);
                CrystalSpriteBatch.Flush();
                CrystalSpriteBatch.End();
                var sceneBefore = ReadRT(sceneRT, rtW, rtH);

                // ②灯光：暗色 clear + additive 光源
                CrystalSpriteBatch.Begin(lightRT, rtW, rtH);
                CrystalSpriteBatch.Clear(new Color(darkR / 255f, darkG / 255f, darkB / 255f, 1f));
                CrystalSpriteBatch.SetBlend(true, 1f, CrystalBlendMode.NORMAL);
                foreach (var l in lights)
                {
                    var glow = Glow(l.SizeIdx);
                    var tint = new Color(l.Tr / 255f, l.Tg / 255f, l.Tb / 255f, 1f);
                    if (timeSet) tint = LightPulse.Modulate(tint, timeSec);
                    CrystalSpriteBatch.Draw(glow, new Rect(0, 0, l.W, l.H), new Vector3(l.X0, l.Y0, 0f), tint);
                }
                CrystalSpriteBatch.Flush();
                CrystalSpriteBatch.SetBlend(false);
                CrystalSpriteBatch.End();
                var lightTex = ReadRT(lightRT, rtW, rtH);

                // ③合成：整幅 multiply 乘回场景
                var lightTex2D = MakeTexFromPixels(lightTex, rtW, rtH);
                CrystalSpriteBatch.Begin(sceneRT, rtW, rtH);
                CrystalSpriteBatch.SetBlend(true, 1f, CrystalBlendMode.MULTIPLY);
                CrystalSpriteBatch.Draw(lightTex2D, new Rect(0, 0, rtW, rtH), Vector3.zero, Color.white);
                CrystalSpriteBatch.Flush();
                CrystalSpriteBatch.SetBlend(false);
                CrystalSpriteBatch.End();
                var sceneAfter = ReadRT(sceneRT, rtW, rtH);

                // CPU 期望
                fail = 0;
                // 场景字节级（Point 过滤全屏，应逐像素相等）
                for (int y = 0; y < rtH && fail < 100; y++)
                    for (int x = 0; x < rtW; x++)
                    {
                        var c = sceneTex.GetPixels32()[(rtH - 1 - y) * rtW + x];
                        var got = sceneBefore[y * rtW + x];
                        if (got.r != c.r || got.g != c.g || got.b != c.b || got.a != c.a)
                        {
                            Console.WriteLine($"  scene diff ({x},{y}) src({c.r:X2}{c.g:X2}{c.b:X2}) got({got.r:X2}{got.g:X2}{got.b:X2})");
                            fail++;
                        }
                    }
                Console.WriteLine($"light-render: scene bytes fail={fail}");
                int f1 = fail;

                // lightTex CPU 期望：按 GPU 点过滤采样模型（fragment 中心 → texel → 读纹理存储像素）精确复刻
                for (int y = 0; y < rtH && fail < 200; y++)
                    for (int x = 0; x < rtW; x++)
                    {
                        int er = darkR, eg = darkG, eb = darkB;
                        foreach (var l in lights)
                        {
                            if (x < l.X0 || x >= l.X0 + l.W || y < l.Y0 || y >= l.Y0 + l.H) continue;
                            int col = (int)Math.Round((x + 0.5 - l.X0) * (l.W - 1) / (double)l.W);
                            int row = (int)Math.Round((1 - (y + 0.5 - l.Y0) / (double)l.H) * (l.H - 1));
                            var g = _glowPx[l.SizeIdx][row * l.W + col];
                            // 脉冲：tint.rgb×brightness，src.a=grad.a×alpha（与 GPU 绘制色 Modulate 一致）
                            double br = l.Tr, bg = l.Tg, bb = l.Tb, ba = 1.0;
                            if (timeSet)
                            {
                                br *= LightPulse.Brightness(timeSec);
                                bg *= LightPulse.Brightness(timeSec);
                                bb *= LightPulse.Brightness(timeSec);
                                ba *= LightPulse.Alpha(timeSec);
                            }
                            er = Math.Min(255, er + (int)Math.Round(g.r * br * (g.a * ba) / 65025.0));
                            eg = Math.Min(255, eg + (int)Math.Round(g.g * bg * (g.a * ba) / 65025.0));
                            eb = Math.Min(255, eb + (int)Math.Round(g.b * bb * (g.a * ba) / 65025.0));
                        }
                        var got = lightTex[y * rtW + x];
                        if (Math.Abs(got.r - er) > 2 || Math.Abs(got.g - eg) > 2 || Math.Abs(got.b - eb) > 2)
                        {
                            Console.WriteLine($"  light diff ({x},{y}) exp({er},{eg},{eb}) got({got.r},{got.g},{got.b})");
                            fail++;
                        }
                    }
                Console.WriteLine($"light-render: lightTex CPU fail={fail - f1}");
                int f2 = fail;

                // 合成 CPU 期望：final = scene*light/255
                for (int y = 0; y < rtH && fail < 200; y++)
                    for (int x = 0; x < rtW; x++)
                    {
                        var s = sceneBefore[y * rtW + x];
                        var lt = lightTex[y * rtW + x];
                        int er = (int)Math.Round(s.r * lt.r / 255.0), eg = (int)Math.Round(s.g * lt.g / 255.0), eb = (int)Math.Round(s.b * lt.b / 255.0);
                        var got = sceneAfter[y * rtW + x];
                        if (Math.Abs(got.r - er) > 2 || Math.Abs(got.g - eg) > 2 || Math.Abs(got.b - eb) > 2)
                        {
                            Console.WriteLine($"  composite diff ({x},{y}) exp({er},{eg},{eb}) got({got.r},{got.g},{got.b})");
                            fail++;
                        }
                    }
                Console.WriteLine($"light-render: composite CPU fail={fail - f2}");

                // 正立 PNG（EncodeToPNG 反行序 → 先翻转）
                WritePng(lightTex, rtW, rtH, Path.ChangeExtension(outPath, null) + "-light.png");
                WritePng(sceneAfter, rtW, rtH, outPath);
                Console.WriteLine($"light-render: wrote {Path.GetFullPath(outPath)} fail={fail}");
                if (timeSet)
                    Console.WriteLine(fail == 0 ? "[lightpulseverify] PASS" : $"[lightpulseverify] FAIL fail={fail}");
                EditorApplication.Exit(fail == 0 ? 0 : 1);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(sceneRT);
                RenderTexture.ReleaseTemporary(lightRT);
                UnityEngine.Object.DestroyImmediate(sceneTex);
                foreach (var kv in _glowCache) UnityEngine.Object.DestroyImmediate(kv.Value);
                _glowCache.Clear();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // checkerboard 4 色场景纹理（RGBA32，SetPixels32 row0=底部，全屏绘制时 batcher quad 顶边 v=1=顶行 → 正立）
        static Texture2D MakeScene(int rtW, int rtH)
        {
            const int cell = 40;
            var px = new Color32[rtW * rtH];
            var cols = new[] {
                new Color32(180, 90, 40, 255), new Color32(60, 160, 80, 255),
                new Color32(40, 90, 200, 255), new Color32(150, 150, 60, 255) };
            for (int ty = 0; ty < rtH; ty++)
            {
                int sy = rtH - 1 - ty;
                int j = sy / cell;
                for (int tx = 0; tx < rtW; tx++)
                {
                    int i = tx / cell;
                    px[ty * rtW + tx] = cols[(i % 2) + 2 * (j % 2)];
                }
            }
            var t = new Texture2D(rtW, rtH, TextureFormat.RGBA32, false);
            t.filterMode = FilterMode.Point;
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // CPU 椭圆射线径向渐变纹理（同色标）
        static Texture2D Glow(int sizeIdx)
        {
            if (_glowCache.TryGetValue(sizeIdx, out var cached)) return cached;
            int w = LightW[sizeIdx], h = LightH[sizeIdx];
            var px = new Color32[w * h];
            for (int ty = 0; ty < h; ty++)
                for (int tx = 0; tx < w; tx++)
                {
                    // 屏幕一致偏移：texel (tx,ty) → 屏幕 (tx, h-1-ty)，像素中心距光心 (w/2,h/2)
                    Grad(w, h, tx - w / 2.0 + 0.5, (h - 1 - ty) - h / 2.0 + 0.5, out byte r, out byte g, out byte b, out byte a);
                    px[ty * w + tx] = new Color32(r, g, b, a);
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.filterMode = FilterMode.Point;
            t.SetPixels32(px);
            t.Apply();
            _glowCache[sizeIdx] = t;
            _glowPx[sizeIdx] = px;
            return t;
        }

        // 渐变值：像素中心距光心 (w/2,h/2) 的偏移 (dx,dy)（含 +0.5 像素中心校正），t 椭圆边界线性化
        static void Grad(int w, int h, double dx, double dy, out byte r, out byte g, out byte b, out byte a)
        {
            double rx = w / 2.0, ry = h / 2.0;
            double t = Math.Sqrt(ry * dx * (ry * dx) + rx * dy * (rx * dy)) / (rx * ry);
            if (t < 0) t = 0; if (t > 1) t = 1;
            int i = 0;
            while (i < 5 && t > T[i + 1]) i++;
            double f = (t - T[i]) / (T[i + 1] - T[i]);
            r = (byte)Math.Round(Stops[i, 0] + (Stops[i + 1, 0] - Stops[i, 0]) * f);
            g = (byte)Math.Round(Stops[i, 1] + (Stops[i + 1, 1] - Stops[i, 1]) * f);
            b = (byte)Math.Round(Stops[i, 2] + (Stops[i + 1, 2] - Stops[i, 2]) * f);
            a = (byte)Math.Round(Stops[i, 3] + (Stops[i + 1, 3] - Stops[i, 3]) * f);
        }

        static Color32[] ReadRT(RenderTexture rt, int rtW, int rtH)
        {
            var read = new Texture2D(rtW, rtH, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rtW, rtH), 0, 0);
            read.Apply();
            RenderTexture.active = null;
            var px = read.GetPixels32();
            UnityEngine.Object.DestroyImmediate(read);
            return px;
        }

        // top-down 回读像素 → 纹理（SetPixels32 row0=底部）：须翻转，否则 batcher 采样 V 轴反向
        static Texture2D MakeTexFromPixels(Color32[] px, int w, int h)
        {
            var fl = new Color32[px.Length];
            for (int y = 0; y < h; y++)
                Array.Copy(px, y * w, fl, (h - 1 - y) * w, w);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.filterMode = FilterMode.Point;
            t.SetPixels32(fl);
            t.Apply();
            return t;
        }

        static void WritePng(Color32[] px, int rtW, int rtH, string outPath)
        {
            var read = new Texture2D(rtW, rtH, TextureFormat.RGBA32, false);
            var fl = new Color32[px.Length];
            for (int y = 0; y < rtH; y++)
                Array.Copy(px, (rtH - 1 - y) * rtW, fl, y * rtW, rtW);
            read.SetPixels32(fl);
            read.Apply();
            string fullOut = Path.GetFullPath(outPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
            File.WriteAllBytes(fullOut, read.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(read);
        }

        static int GetInt(string name, int def)
        {
            string s = Environment.GetEnvironmentVariable(name);
            return int.TryParse(s, out int v) ? v : def;
        }
    }
}
