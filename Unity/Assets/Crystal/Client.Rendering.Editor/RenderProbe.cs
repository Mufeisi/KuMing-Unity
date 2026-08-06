using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // R1-0 探针：验证 -batchmode（无 -nographics）能否渲染 RenderTexture 并 ReadPixels 回读。
    // 同时探测垂直朝向：纹理上半红下半蓝，回读检查行序。
    // 用法：Unity.exe -batchmode -quit -executeMethod Crystal.Rendering.Editor.RenderProbe.Run -logFile <path>
    static class RenderProbe
    {
        public static void Run()
        {
            int w = 64, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
            {
                int row = i / w;
                px[i] = row < h / 2 ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255);
            }
            tex.SetPixels32(px);
            tex.Apply();

            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);

            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = null;

            var got = read.GetPixels32();
            Color32 top = got[1];      // readback 行 1
            Color32 mid = got[h / 2 * w + 1];
            Color32 bottom = got[(h - 1) * w + 1];
            Debug.Log($"RENDERPROBE top={top} mid={mid} bottom={bottom}");
            Debug.Log($"RENDERPROBE topIsRed={top.r == 255 && top.b == 0} bottomIsBlue={bottom.b == 255 && bottom.r == 0}");

            RenderTexture.ReleaseTemporary(rt);
            bool topIsRed = top.r == 255 && top.b == 0;
            bool bottomIsBlue = bottom.b == 255 && bottom.r == 0;
            EditorApplication.Exit(topIsRed && bottomIsBlue ? 0 : 1);
        }
    }
}
