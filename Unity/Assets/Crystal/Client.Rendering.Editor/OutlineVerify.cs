using System;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // P1 sanduan OutLine.shader 描边语义复刻验证（确定性，无服务器）：
    // CrystalSpriteOutline shader + CrystalSpriteBatch.DrawOutline（4 向 ±thickness px 平涂描边色副本
    // + 原图压顶 = 轮廓外描边光环，图集兼容实现）。期望逐像素对照（±2 容差吸收 GPU 舍入）：
    //   src 不透明像素 → 原色（含阴影例外 (16,8,8)/r<0.01 原样绘制）；
    //   src 透明像素 → 若 4 邻域（距离 t）存在"描边资格"像素（a≥0.5 且非阴影例外）→ 描边色混合背景，否则背景。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.OutlineVerify.Run -quit
    // 断言：全过输出 [outlineverify] PASS exit 0。
    static class OutlineVerify
    {
        static readonly Color32 Bg = new Color32(40, 80, 120, 255);
        static readonly Color32[] Texels = new Color32[12 * 12];

        static int _fail;

        static bool Eligible(int tx, int ty)
        {
            if (tx < 0 || ty < 0 || tx >= 12 || ty >= 12) return false;
            var t = Texels[ty * 12 + tx];
            if (t.a < 128) return false;                                    // a<0.5 视为透明
            if (t.r == 16 && t.g == 8 && t.b == 8) return false;            // 阴影例外 (16/8/8)
            if (t.r < 3) return false;                                      // r<0.01 近黑例外
            return true;
        }

        // 纹理空间期望：src 透明 → 4 邻域(距离 t)任一资格像素 ⇒ 描边色混合背景。
        static Color32 Expect(int tx, int ty, float t, Color outline)
        {
            if (tx < 0 || ty < 0 || tx >= 12 || ty >= 12) return Bg; // 纹理外：无原图压顶；邻域资格由 Eligible 越界保护
            var src = Texels[ty * 12 + tx];
            if (src.a >= 128)
                return src; // 原图压顶：含阴影/近黑原样（halo 模式不替换轮廓内像素）

            bool painted = Eligible(tx + (int)t, ty) || Eligible(tx - (int)t, ty) ||
                           Eligible(tx, ty + (int)t) || Eligible(tx, ty - (int)t);
            if (!painted) return Bg;

            // SrcAlpha/OneMinusSrcAlpha：dest = src*srcA + dst*(1-srcA)
            float sa = outline.a;
            byte r = ClampB(outline.r * sa + Bg.r / 255f * (1f - sa));
            byte g = ClampB(outline.g * sa + Bg.g / 255f * (1f - sa));
            byte b = ClampB(outline.b * sa + Bg.b / 255f * (1f - sa));
            byte a = ClampB(sa * sa + Bg.a / 255f * (1f - sa)); // BlendVerify 同款 alpha 模型
            return new Color32(r, g, b, a);
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

        public static void Run()
        {
            for (int i = 0; i < Texels.Length; i++) Texels[i] = new Color32(0, 0, 0, 0);
            // 白色方块（tx 4..7, ty 4..7）；阴影例外块（tx 1..2, ty 1..2）；近黑像素（tx 10, ty 1）
            for (int ty = 4; ty <= 7; ty++)
                for (int tx = 4; tx <= 7; tx++)
                    Texels[ty * 12 + tx] = new Color32(255, 255, 255, 255);
            for (int ty = 1; ty <= 2; ty++)
                for (int tx = 1; tx <= 2; tx++)
                    Texels[ty * 12 + tx] = new Color32(16, 8, 8, 255);
            Texels[1 * 12 + 10] = new Color32(2, 2, 2, 255);

            var tex = new Texture2D(12, 12, TextureFormat.RGBA32, false);
            tex.SetPixels32(Texels);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();

            RunCase("outline t1 alpha1", tex, 1f, new Color(1f, 0f, 0f, 1f));
            RunCase("outline t2 alpha1", tex, 2f, new Color(1f, 0f, 0f, 1f));
            RunCase("outline t1 alpha0.5", tex, 1f, new Color(1f, 0f, 0f, 0.5f));

            UnityEngine.Object.DestroyImmediate(tex);
            Console.WriteLine($"outline-verify: fail={_fail}");
            if (_fail == 0)
            {
                Console.WriteLine("[outlineverify] PASS cases=3");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[outlineverify] FAIL cases=3 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }

        static void RunCase(string name, Texture2D tex, float t, Color outline)
        {
            const int w = 16, h = 16, ox = 2, oy = 2;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            try
            {
                CrystalSpriteBatch.ReplaceBlend = false;
                CrystalSpriteBatch.Begin(rt, w, h);
                CrystalSpriteBatch.Clear(Bg);
                CrystalSpriteBatch.DrawOutline(tex, new Rect(0, 0, 12, 12), new Vector3(ox, oy, 0f), Color.white, outline, t);
                CrystalSpriteBatch.End();

                var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                int bad = 0;
                for (int sy = 0; sy < h; sy++)
                {
                    for (int sx = 0; sx < w; sx++)
                    {
                        int tx = sx - ox;                                    // 屏幕 x 无翻转
                        int ty = (12 - 1) - (sy - oy);                       // v 翻转：纹理底行=屏幕顶
                        var expect = Expect(tx, ty, t, outline);
                        var got = px[sy * w + sx];
                        if (Diff(expect, got))
                        {
                            bad++;
                            if (bad <= 8) Console.WriteLine($"  {name} diff ({sx},{sy}) exp{expect.r:X2}{expect.g:X2}{expect.b:X2}{expect.a:X2} got{got.r:X2}{got.g:X2}{got.b:X2}{got.a:X2}");
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
    }
}
