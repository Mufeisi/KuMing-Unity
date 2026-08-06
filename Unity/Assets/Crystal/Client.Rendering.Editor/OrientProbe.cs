using System;
using System.IO;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // R3 方向探针：定死 渲染→ReadPixels 回读 的行序，并产出 PNG 供外部判读 EncodeToPNG 行序。
    // 画一个左上角(20,20)的 20x20 纯红块。回读若行 30 红 → RT top-down（row0=顶）；行 80 红 → RT 底行在 row0。
    static class OrientProbe
    {
        public static void Run()
        {
            int rtW = 200, rtH = 100;
            var red = new Texture2D(20, 20, TextureFormat.RGBA32, false);
            var cols = new Color32[20 * 20];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 0, 0, 255);
            red.SetPixels32(cols);
            red.Apply();

            var rt = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                CrystalSpriteBatch.Begin(rt, rtW, rtH);
                CrystalSpriteBatch.Clear(new Color(0, 0, 0, 1));
                CrystalSpriteBatch.Draw(red, new Rect(0, 0, 20, 20), new Vector3(20, 20, 0f), Color.white);
                CrystalSpriteBatch.End();

                var read = new Texture2D(rtW, rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, rtW, rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;

                var px = read.GetPixels32();
                bool topRed = px[30 * rtW + 30].r == 255;
                bool botRed = px[80 * rtW + 30].r == 255;
                Console.WriteLine($"orient: readback top(row30)red={topRed} bottom(row80)red={botRed}");

                // 与 MapRender/AnimationRender 相同：EncodeToPNG 反行序 → 先翻转再编码
                var fl = new Color32[px.Length];
                for (int y = 0; y < rtH; y++)
                    Array.Copy(px, (rtH - 1 - y) * rtW, fl, y * rtW, rtW);
                read.SetPixels32(fl);
                read.Apply();

                string pngPath = Path.GetFullPath("Build/orient-probe.png");
                Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
                File.WriteAllBytes(pngPath, read.EncodeToPNG());
                Console.WriteLine($"orient: wrote {pngPath}");
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(red);
            }
            EditorApplication.Exit(0);
        }
    }
}
