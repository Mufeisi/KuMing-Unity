using System;
using System.IO;
using Client.MirObjects;
using Client.MirScenes;
using Crystal.Client.Assets;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // GameRenderer 运行时渲染核心验证：与已证正确的 SceneRender 同场景 tile 层逐像素对照。
    // GameRenderer 是 SceneRender.DrawMapTiles/MapLibRel 的运行时抽取（去 UnityEditor），
    // 二者对同 map 同中心同 RT 的输出必须完全一致（diff==0）。
    // 用法（batchmode 经 Hub 会话）：
    //   CRYSTAL_MAP_DIR=<maps> CRYSTAL_MAP_ATLAS_DIR=<map atlas> CRYSTAL_MAP=0.map
    //   [CRYSTAL_CENTER=x,y] [CRYSTAL_RT_W=1152] [CRYSTAL_RT_H=640]
    //   Unity.exe -batchmode -quit -executeMethod Crystal.Rendering.Editor.GameRendererVerify.Run
    static class GameRendererVerify
    {
        const int CellWidth = MapControl.CellWidth;
        const int CellHeight = MapControl.CellHeight;

        public static void Run()
        {
            string mapDir = Environment.GetEnvironmentVariable("CRYSTAL_MAP_DIR");
            string mapAtlasDir = Environment.GetEnvironmentVariable("CRYSTAL_MAP_ATLAS_DIR");
            string map = Environment.GetEnvironmentVariable("CRYSTAL_MAP");
            if (string.IsNullOrEmpty(mapDir) || string.IsNullOrEmpty(mapAtlasDir) || string.IsNullOrEmpty(map))
            {
                Console.WriteLine("gamerenderer-verify: CRYSTAL_MAP_DIR / CRYSTAL_MAP_ATLAS_DIR / CRYSTAL_MAP not set");
                EditorApplication.Exit(2);
                return;
            }
            mapDir = Path.GetFullPath(mapDir);
            mapAtlasDir = Path.GetFullPath(mapAtlasDir);
            SceneRender._mapAtlasDir = mapAtlasDir;
            GameRenderer.MapAtlasDir = mapAtlasDir;
            GameRenderer.BatchFloor = true;

            string mapPath = Path.Combine(mapDir, map);
            if (!File.Exists(mapPath))
            {
                Console.WriteLine($"gamerenderer-verify: map missing {mapPath}");
                EditorApplication.Exit(2);
                return;
            }
            var mapReader = new MapReader(mapPath);
            var cells = mapReader.MapCells;

            int rtW = GetInt("CRYSTAL_RT_W", 1152);
            int rtH = GetInt("CRYSTAL_RT_H", 640);
            string center = Environment.GetEnvironmentVariable("CRYSTAL_CENTER");
            int cx, cy;
            if (!string.IsNullOrEmpty(center) && center.Contains(","))
            {
                var p = center.Split(',');
                cx = int.Parse(p[0]); cy = int.Parse(p[1]);
            }
            else { cx = mapReader.Width / 2; cy = mapReader.Height / 2; }

            int offX = rtW / 2 / CellWidth;
            int offY = rtH / 2 / CellHeight - 1;
            int rangeX = offX + 6, rangeY = offY + 6;

            Console.WriteLine($"gamerenderer-verify: {map} {mapReader.Width}x{mapReader.Height} center=({cx},{cy}) off=({offX},{offY})");

            // 预加载地图用到的全部图集（两路径一致，确保缺图集时行为同步）。
            int missing = 0;
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int y = 0; y < mapReader.Height; y++)
                for (int x = 0; x < mapReader.Width; x++)
                {
                    var c = cells[x, y];
                    if (c.BackIndex >= 0) seen.Add(c.BackIndex);
                    if (c.MiddleIndex >= 0) seen.Add(c.MiddleIndex);
                    if (c.FrontIndex >= 0) seen.Add(c.FrontIndex);
                }
            var sorted = new System.Collections.Generic.List<int>(seen); sorted.Sort();
            foreach (int li in sorted)
                if (SceneRender.MapLibRelLazy(li) == null) missing++;
            Console.WriteLine($"gamerenderer-verify: floorLibs={sorted.Count} unresolved={missing}");

            var libByIndex = GameRenderer.BuildLibIndex(0, cells, mapReader.Width, mapReader.Height);

            var rtA = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            var rtB = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                // A：SceneRender（参考实现，已逐像素验证正确）
                CrystalSpriteBatch.Begin(rtA, rtW, rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                var countA = SceneRender.DrawMapTiles(cells, mapReader, cx, cy, offX, offY, rangeX, rangeY, libByIndex);
                CrystalSpriteBatch.End();
                var pxA = ReadTopDown(rtA, rtW, rtH);

                // B：GameRenderer（运行时抽取实现）
                CrystalSpriteBatch.Begin(rtB, rtW, rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                var countB = GameRenderer.DrawMapTiles(cells, mapReader, cx, cy, offX, offY, rangeX, rangeY, libByIndex);
                CrystalSpriteBatch.End();
                var pxB = ReadTopDown(rtB, rtW, rtH);

                int diff = 0; int firstIdx = -1;
                for (int i = 0; i < pxA.Length; i++)
                {
                    if (pxA[i].r != pxB[i].r || pxA[i].g != pxB[i].g || pxA[i].b != pxB[i].b || pxA[i].a != pxB[i].a)
                    {
                        diff++; if (firstIdx < 0) firstIdx = i;
                    }
                }

                int nonBg = 0;
                for (int i = 0; i < pxA.Length; i++)
                    if (!(pxA[i].r == 26 && pxA[i].g == 26 && pxA[i].b == 26)) nonBg++;

                Console.WriteLine($"gamerenderer-verify: countsA=({countA[0]},{countA[1]},{countA[2]}) countsB=({countB[0]},{countB[1]},{countB[2]})");
                Console.WriteLine($"gamerenderer-verify: nonBg={nonBg} diff={diff}");

                bool ok = diff == 0 && nonBg > 0;
                if (!ok && firstIdx >= 0)
                {
                    int fx = firstIdx % rtW, fy = firstIdx / rtW;
                    Console.WriteLine($"  first diff at ({fx},{fy}): A={pxA[firstIdx]} B={pxB[firstIdx]}");
                }
                if (nonBg == 0) Console.WriteLine("  FAIL: both renders are blank (background only)");
                Console.WriteLine($"gamerenderer-verify: {(ok ? "PASS" : "FAIL")} diff={diff} nonBg={nonBg}");
                EditorApplication.Exit(ok ? 0 : 2);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rtA);
                RenderTexture.ReleaseTemporary(rtB);
            }
        }

        // top-down 回读（R1 实证 ReadPixels row0=RT 顶）。
        static Color32[] ReadTopDown(RenderTexture rt, int w, int h)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            var px = read.GetPixels32();
            UnityEngine.Object.DestroyImmediate(read);
            RenderTexture.active = prev;
            return px;
        }

        static int GetInt(string name, int def)
        {
            string s = Environment.GetEnvironmentVariable(name);
            return int.TryParse(s, out int v) ? v : def;
        }
    }
}
