using System;
using Crystal.Client.Assets;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // R1-4：CrystalSpriteBatch 的 NORMAL/ADDITIVE 混合、Opacity、Grayscale、Transform 缩放
    // 语义 vs CPU 计算的期望值逐像素对照（±2 容差吸收 GPU 舍入）。
    // 这是 DXManager.Draw seam 的 470 调用点实际走的路（ReplaceBlend 仅是 golden 验证钩子）。
    // 用法：Unity.exe -batchmode -quit -executeMethod ...BlendVerify.Run
    static class BlendVerify
    {
        // 16 个各异 RGBA 纹素，覆盖不透明/半透明/全透明/纯色/渐变
        static readonly Color32[] Texels =
        {
            new Color32(255, 0, 0, 255),   new Color32(0, 255, 0, 128),
            new Color32(0, 0, 255, 64),    new Color32(255, 255, 255, 255),
            new Color32(200, 100, 50, 200), new Color32(100, 200, 100, 0),
            new Color32(50, 50, 200, 100), new Color32(255, 128, 64, 255),
            new Color32(10, 20, 30, 160),  new Color32(240, 240, 240, 32),
            new Color32(128, 0, 128, 255), new Color32(0, 128, 0, 96),
            new Color32(64, 64, 64, 255),  new Color32(255, 200, 0, 0),
            new Color32(0, 255, 255, 128), new Color32(30, 40, 50, 255),
        };
        static readonly Color32 Bg = new Color32(40, 80, 120, 255);

        static int _fail;

        public static void Run()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            tex.SetPixels32(Texels);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();

            CheckCase("normal", tex, 4, 4, 1f, false, false, 1);
            CheckCase("opacity", tex, 4, 4, 0.5f, false, false, 1);
            CheckCase("additive", tex, 4, 4, 1f, true, false, 1);
            CheckCase("grayscale", tex, 4, 4, 1f, false, true, 1);
            CheckCase("transform2x", tex, 8, 8, 1f, false, false, 2);

            UnityEngine.Object.DestroyImmediate(tex);
            Console.WriteLine($"blend-verify: fail={_fail}");
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        static void CheckCase(string name, Texture2D tex, int w, int h, float opacity,
            bool additive, bool grayscale, int scale)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            try
            {
                CrystalSpriteBatch.ReplaceBlend = false;
                CrystalSpriteBatch.Begin(rt, w, h);
                CrystalSpriteBatch.Clear(Bg);
                CrystalSpriteBatch.SetBlend(additive, 1f, CrystalBlendMode.NORMAL);
                CrystalSpriteBatch.SetGrayscale(grayscale);
                CrystalSpriteBatch.SetOpacity(opacity);
                CrystalSpriteBatch.Transform = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                CrystalSpriteBatch.Draw(tex, new Rect(0, 0, 4, 4), Vector3.zero, Color.white);
                CrystalSpriteBatch.End();
                CrystalSpriteBatch.Transform = Matrix4x4.identity;

                var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                // CPU 期望：逐像素对照（点采样 1:1 或 scale 2x 块）
                int bad = 0;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int tx = x / scale, ty = y / scale;
                        var expect = Blend(tex, tx, ty, opacity, additive, grayscale);
                        var got = px[y * w + x];
                        if (Diff(expect, got))
                        {
                            bad++;
                            if (bad <= 8) Console.WriteLine($"  {name} diff ({x},{y}) exp{expect.r:X2}{expect.g:X2}{expect.b:X2}{expect.a:X2} got{got.r:X2}{got.g:X2}{got.b:X2}{got.a:X2}");
                        }
                    }
                }
                Console.WriteLine($"{name}: bad={bad}/{w * h}");
                if (bad > 0) _fail++;
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static Color32 Blend(Texture2D tex, int tx, int ty, float opacity, bool additive, bool grayscale)
        {
            // SetPixels32 行 0 = 纹理底部（v=0）；batcher quad 顶边采样 v=1=纹理顶行 → 取反行序
            var t = Texels[(tex.height - 1 - ty) * 4 + tx];
            // 顶点色：白色 * opacity → Color32 舍入（Unity Color→Color32 用 round-to-even）
            int oa = Mathf.RoundToInt(opacity * 255f);
            float sr = t.r / 255f, sg = t.g / 255f, sb = t.b / 255f;
            float sa = (t.a / 255f) * (oa / 255f);

            if (grayscale)
            {
                float luma = sr * 0.30f + sg * 0.59f + sb * 0.11f;
                sr = sg = sb = luma;
            }

            float dr = Bg.r / 255f, dg = Bg.g / 255f, db = Bg.b / 255f, da = Bg.a / 255f;
            float or_, og, ob, oa_;
            if (additive)
            {
                or_ = sr * sa + dr; og = sg * sa + dg; ob = sb * sa + db; oa_ = sa * sa + da;
            }
            else
            {
                float om = 1f - sa;
                or_ = sr * sa + dr * om; og = sg * sa + dg * om; ob = sb * sa + db * om; oa_ = sa * sa + da * om;
            }
            return new Color32(ClampB(or_), ClampB(og), ClampB(ob), ClampB(oa_));
        }

        static byte ClampB(float f)
        {
            if (f < 0f) f = 0f;
            if (f > 1f) f = 1f;
            return (byte)Mathf.RoundToInt(f * 255f);
        }

        static bool Diff(Color32 a, Color32 b)
        {
            return Math.Abs(a.r - b.r) > 2 || Math.Abs(a.g - b.g) > 2 ||
                   Math.Abs(a.b - b.b) > 2 || Math.Abs(a.a - b.a) > 2;
        }
    }
}
