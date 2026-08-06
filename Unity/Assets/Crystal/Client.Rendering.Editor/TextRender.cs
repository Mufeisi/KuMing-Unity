using System;
using System.IO;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // R8 探针：Unity 动态字体文本栅格化 —— TextGenerator（CPU 字形生成）提取字形像素 → 合成文本纹理
    // → CrystalSpriteBatch 画到 RT → PNG。实证 batchmode 下 Unity 动态字体可用性 + 字形像素确定性
    // （MirLabel seam 落地关键）。
    // TextGenerator 语义：Populate(text, settings) → verts 每字形 4 顶点（position=屏幕坐标，uv0=字体图集 UV）。
    // 字形像素在 font.material.mainTexture（CPU 图集），UV (0,0)=左下 → GetPixels32(0,0) 同序直接映射。
    // 用法（batchmode 经 Hub 会话）：
    //   [CRYSTAL_TEXT="Hello"] [CRYSTAL_FONT_NAME=Arial] [CRYSTAL_FONT_SIZE=8]
    //   [CRYSTAL_OUT=text.png] [CRYSTAL_RT_W=256] [CRYSTAL_RT_H=128]
    //   Unity.exe -batchmode -quit -executeMethod Crystal.Rendering.Editor.TextRender.Run
    static class TextRender
    {
        static string LogPath = "Build/TextVerify/probe.log";
        static void Log(string s) { try { File.AppendAllText(LogPath, s + "\n"); } catch { } }

        static string Env(string key, string def = "") { return Environment.GetEnvironmentVariable(key) ?? def; }
        static int EnvI(string key, int def) { var s = Env(key); return string.IsNullOrEmpty(s) ? def : int.Parse(s); }

        public static void Run()
        {
            Directory.CreateDirectory("Build/TextVerify");
            File.WriteAllText(LogPath, "");
            string text = Env("CRYSTAL_TEXT", "Hello");
            string fontName = Env("CRYSTAL_FONT_NAME", "Arial");
            int size = EnvI("CRYSTAL_FONT_SIZE", 8);
            string outPng = Env("CRYSTAL_OUT", "text.png");
            int rtW = EnvI("CRYSTAL_RT_W", 256);
            int rtH = EnvI("CRYSTAL_RT_H", 128);

            // 1. 动态字体。
            var font = Font.CreateDynamicFontFromOSFont(fontName, size);
            var fontTex = font.material != null ? font.material.mainTexture as Texture2D : null;
            Log($"text-render: font={font.name} size={size} text=\"{text}\" fontTex={fontTex?.width}x{fontTex?.height}");

            // 2. 逐字符合成：position 布局 + 单字符稳定 UV（绕开整段 TextGenerator 多字形 UV 失效/跳字）。
            var tex = TextGlyphBuilder.Build(text, fontName, size, false);
            if (tex == null)
            {
                Log("text-render: no glyph texture, FAIL");
                EditorApplication.Exit(1);
                return;
            }

            // 3. 断言：合成文本纹理非空（CPU 字形提取有效）。
            int texOpaque = 0, texMaxA = 0;
            var tx = tex.GetPixels32();
            for (int i = 0; i < tx.Length; i++)
            {
                if (tx[i].a > 0) texOpaque++;
                if (tx[i].a > texMaxA) texMaxA = tx[i].a;
            }
            Log($"text-render: textTex {tex.width}x{tex.height} opaque={texOpaque} maxA={texMaxA}");

            // 4. 连续 Begin→Clear→Draw→End 画到 RT → PNG（真实 MirLabel 用法；期间不可插读 RT）。
            var rt = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            CrystalSpriteBatch.ReplaceBlend = true;
            CrystalSpriteBatch.Begin(rt, rtW, rtH);
            CrystalSpriteBatch.Clear(new Color(0, 0, 0, 0));
            CrystalSpriteBatch.Draw(tex, new Rect(0, 0, tex.width, tex.height), new Vector3(10, 10, 0), Color.white);
            CrystalSpriteBatch.End();
            Log($"text-render: after-draw opaque={CountOpaque(rt, rtW, rtH)}");

            Directory.CreateDirectory("Build/TextVerify");
            string full = Path.Combine("Build/TextVerify", outPng);
            WritePng(rt, full, rtW, rtH);

            // 5. 断言：PNG 解码非空。
            bool pngExists = File.Exists(full);
            int pngOpaque = pngExists ? CountPngOpaque(full) : 0;
            Log($"text-render: pngExists={pngExists} pngOpaque={pngOpaque}");
            UnityEngine.Object.DestroyImmediate(tex);

            RenderTexture.ReleaseTemporary(rt);
            bool ok = texOpaque > 0 && pngOpaque > 0;
            Log(ok ? "text-render: PASS" : "text-render: FAIL");
            EditorApplication.Exit(ok ? 0 : 1);
        }

        // 分段定位用：从 RT 回读统计非透明像素（top-down，同 ReadPixels 语义）。
        static int CountOpaque(RenderTexture rt, int w, int h)
        {
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = null;
            var px = read.GetPixels32();
            int n = 0;
            for (int i = 0; i < px.Length; i++)
                if (px[i].a > 0) n++;
            UnityEngine.Object.DestroyImmediate(read);
            return n;
        }

        // 从 PNG 文件解码并统计非透明像素（正确解码 PNG 滤波，行序 top-down）。
        static int CountPngOpaque(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            bool ok = t.LoadImage(File.ReadAllBytes(path));
            var px = t.GetPixels32();
            int n = 0;
            for (int i = 0; i < px.Length; i++)
                if (px[i].a > 0) n++;
            UnityEngine.Object.DestroyImmediate(t);
            return ok ? n : -1;
        }

        // 同款 WritePng：ReadPixels 回读 top-down，EncodeToPNG 输出需先按行翻转（R3 OrientProbe 实证）。
        static void WritePng(RenderTexture rt, string path, int w, int h)
        {
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = null;

            var px = read.GetPixels32();
            var fl = new Color32[w * h];
            for (int y = 0; y < h; y++)
                Array.Copy(px, (h - 1 - y) * w, fl, y * w, w);
            read.SetPixels32(fl);
            read.Apply();
            File.WriteAllBytes(path, read.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(read);
        }
    }
}
