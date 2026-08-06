using System;
using System.Collections.Generic;
using System.IO;
using Client.MirObjects;
using Crystal.Client.Assets;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // R2 探针：MapReader 解析 .map + MapLibs 图集 → CrystalSpriteBatch 绘制可见地面 tile 到 RT → 保存 PNG。
    // 复刻 GameScene.DrawFloor 的 Back/Middle/Front 三层（动画帧取 0，门取关态），供视觉验证 tile 管线。
    // 用法（batchmode 经 Hub 会话）：
    //   CRYSTAL_MAP_DIR=<maps> CRYSTAL_ATLAS_DIR=<map atlas> CRYSTAL_MAP=0.map
    //   [CRYSTAL_CENTER=x,y] [CRYSTAL_RT_W=1152] [CRYSTAL_RT_H=640]
    //   [CRYSTAL_LAYER=all|back|middle|front] [CRYSTAL_OUT=out.png]
    //   [CRYSTAL_SPOT=x,y] —— 仅 back 层 + ReplaceBlend 直通：把该格 back tile 逐像素与图集源比对（字节级 spot-check）。
    //   Unity.exe -batchmode -quit -executeMethod ...MapRender.Run
    static class MapRender
    {
        const int CellWidth = 48;
        const int CellHeight = 32;

        // MLibrary.cs MapLibs 段映射：0-99 WemadeMir2，100-199 ShandaMir2，200-299 WemadeMir3。
        // 返回图集相对路径（<rel>.json）。
        static readonly string[] Mir3Names =
        {
            "Tilesc", "Tiles30c", "Tiles5c", "Smtilesc", "Housesc", "Cliffsc",
            "Dungeonsc", "Innersc", "Furnituresc", "Wallsc", "smObjectsc", "Animationsc", "Object1c", "Object2c"
        };
        static readonly string[] Mir3Subs = { "", "Wood/", "Sand/", "Snow/", "Forest/" };

        public static string MapLibRel(int idx)
        {
            if (idx == 0) return "WemadeMir2/Tiles";
            if (idx == 1) return "WemadeMir2/SmTiles";
            if (idx == 2) return "WemadeMir2/Objects";
            if (idx >= 3 && idx <= 27) return "WemadeMir2/Objects" + (idx - 1); // 3→Objects2 ... 27→Objects26
            if (idx == 90) return "WemadeMir2/Objects_32bit";
            if (idx == 100) return "ShandaMir2/Tiles";
            if (idx >= 101 && idx <= 109) return "ShandaMir2/Tiles" + (idx - 99); // 101→Tiles2 ... 109→Tiles10
            if (idx == 110) return "ShandaMir2/SmTiles";
            if (idx >= 111 && idx <= 119) return "ShandaMir2/SmTiles" + (idx - 99);
            if (idx == 120) return "ShandaMir2/Objects";
            if (idx >= 121 && idx <= 150) return "ShandaMir2/Objects" + (idx - 119); // 121→Objects2 ... 150→Objects31
            if (idx == 190) return "ShandaMir2/AniTiles1";
            // WemadeMir3：组 i=0..4（空/Wood/Sand/Snow/Forest），组内 14 个段，base=200+i*15
            if (idx >= 200 && idx <= 273)
            {
                int i = (idx - 200) / 15;
                int off = (idx - 200) % 15;
                if (off < Mir3Names.Length) return "WemadeMir3/" + Mir3Subs[i] + Mir3Names[off];
            }
            return null;
        }

        static readonly Dictionary<int, AtlasLibrary> _libs = new Dictionary<int, AtlasLibrary>();
        static string _atlasDir;

        public static void Run()
        {
            string mapDir = Environment.GetEnvironmentVariable("CRYSTAL_MAP_DIR");
            string atlasDir = Environment.GetEnvironmentVariable("CRYSTAL_ATLAS_DIR");
            string map = Environment.GetEnvironmentVariable("CRYSTAL_MAP");
            if (string.IsNullOrEmpty(mapDir) || string.IsNullOrEmpty(atlasDir) || string.IsNullOrEmpty(map))
            {
                Console.WriteLine("map-render: CRYSTAL_MAP_DIR / CRYSTAL_ATLAS_DIR / CRYSTAL_MAP not set");
                EditorApplication.Exit(2);
                return;
            }
            _atlasDir = Path.GetFullPath(atlasDir);
            mapDir = Path.GetFullPath(mapDir);

            string mapPath = Path.Combine(mapDir, map);
            if (!File.Exists(mapPath))
            {
                Console.WriteLine($"map-render: map missing {mapPath}");
                EditorApplication.Exit(2);
                return;
            }

            var mapReader = new MapReader(mapPath);
            var cells = mapReader.MapCells;
            Console.WriteLine($"map-render: {map} {mapReader.Width}x{mapReader.Height} cells={cells.Length}");

            int rtW = GetInt("CRYSTAL_RT_W", 1152);
            int rtH = GetInt("CRYSTAL_RT_H", 640);
            string center = Environment.GetEnvironmentVariable("CRYSTAL_CENTER");
            int cx, cy;
            if (!string.IsNullOrEmpty(center) && center.Contains(","))
            {
                var p = center.Split(',');
                cx = int.Parse(p[0]); cy = int.Parse(p[1]);
            }
            else
            {
                cx = mapReader.Width / 2; cy = mapReader.Height / 2;
            }
            string layerFilter = Environment.GetEnvironmentVariable("CRYSTAL_LAYER");
            if (string.IsNullOrEmpty(layerFilter)) layerFilter = "all";
            string outPath = Environment.GetEnvironmentVariable("CRYSTAL_OUT");
            if (string.IsNullOrEmpty(outPath)) outPath = "Build/map-render.png";
            string spot = Environment.GetEnvironmentVariable("CRYSTAL_SPOT");

            // 扫描地图用到的 lib 段（诊断 + 决定加载哪些图集）
            var usedLibs = new HashSet<int>();
            for (int y = 0; y < mapReader.Height; y++)
                for (int x = 0; x < mapReader.Width; x++)
                {
                    var c = cells[x, y];
                    if (c.BackIndex >= 0) usedLibs.Add(c.BackIndex);
                    if (c.MiddleIndex >= 0) usedLibs.Add(c.MiddleIndex);
                    if (c.FrontIndex >= 0) usedLibs.Add(c.FrontIndex);
                }
            var sortedLibs = new List<int>(usedLibs); sortedLibs.Sort();
            int missing = 0;
            foreach (int li in sortedLibs)
            {
                string rel = MapLibRel(li);
                if (rel != null && EnsureLib(li, rel) == null) missing++;
                else if (rel == null) missing++;
            }
            Console.WriteLine($"map-render: usedLibs={sortedLibs.Count} unresolved={missing}");

            // 诊断：对每个 Mir3 段（idx>=200）输出首个可解析 back/front 引用单元格，便于挑选 Mir3 图元 spot-check 中心。
            foreach (int li in sortedLibs)
            {
                if (li < 200) continue;
                var lib = _libs.TryGetValue(li, out var l) ? l : null;
                int fl = lib == null ? 0 : lib.Frames.Length;
                for (int y = 0; y < mapReader.Height; y++)
                    for (int x = 0; x < mapReader.Width; x++)
                    {
                        var c = cells[x, y];
                        int bi = (c.BackImage & 0x1FFFFFFF) - 1;
                        if (c.BackIndex == li && c.BackImage != 0 && bi >= 0 && bi < fl && !lib.Frames[bi].Empty)
                        {
                            Console.WriteLine($"  mir3-back lib={li} first=({x},{y}) rel={MapLibRel(li)}");
                            goto next;
                        }
                    }
                Console.WriteLine($"  mir3-back lib={li} first=<none> rel={MapLibRel(li)}");
                next: ;
            }
            foreach (int li in sortedLibs)
            {
                if (li < 200) continue;
                var lib = _libs.TryGetValue(li, out var l) ? l : null;
                int fl = lib == null ? 0 : lib.Frames.Length;
                for (int y = 0; y < mapReader.Height; y++)
                    for (int x = 0; x < mapReader.Width; x++)
                    {
                        var c = cells[x, y];
                        int fi = (c.FrontImage & 0x7FFF) - 1;
                        if (c.FrontIndex == li && c.FrontIndex != 200 && fi >= 0 && fi < fl && !lib.Frames[fi].Empty)
                        {
                            Console.WriteLine($"  mir3-front lib={li} first=({x},{y}) rel={MapLibRel(li)}");
                            goto next2;
                        }
                    }
                Console.WriteLine($"  mir3-front lib={li} first=<none> rel={MapLibRel(li)}");
                next2: ;
            }

            int offX = rtW / 2 / CellWidth;
            int offY = rtH / 2 / CellHeight - 1;
            int rangeX = offX + 6;
            int rangeY = offY + 6;

            var rt = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                CrystalSpriteBatch.ReplaceBlend = !string.IsNullOrEmpty(spot); // spot 模式直通
                CrystalSpriteBatch.Begin(rt, rtW, rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));

                int[] count = { 0, 0, 0 };
                int midCells = 0, frontCells = 0;
                var midSizes = new Dictionary<string, int>();
                var midLibs = new HashSet<int>();
                int startX = cx - rangeX, endX = cx + rangeX;
                int startY = cy - rangeY, endY = cy + rangeY + 5;
                for (int y = startY; y <= endY; y++)
                {
                    if (y <= 0) continue;
                    if (y >= mapReader.Height) break;
                    int drawY = (y - cy + offY) * CellHeight;
                    for (int x = startX; x <= endX; x++)
                    {
                        if (x < 0) continue;
                        if (x >= mapReader.Width) break;
                        int drawX = (x - cx + offX) * CellWidth - offX;
                        var cell = cells[x, y];

                        // Back：仅偶数格提交，index=(BackImage&0x1FFFFFFF)-1
                        if ((layerFilter == "all" || layerFilter == "back") && y % 2 == 0 && x % 2 == 0)
                        {
                            if (cell.BackImage != 0 && cell.BackIndex != -1)
                            {
                                int index = (cell.BackImage & 0x1FFFFFFF) - 1;
                                if (DrawFrame(cell.BackIndex, index, drawX, drawY)) count[0]++;
                            }
                        }

                        // Middle：MiddleImage-1，仅 48x32 或 96x64
                        if (layerFilter == "all" || layerFilter == "middle")
                        {
                            int mid = cell.MiddleImage - 1;
                            if (mid >= 0 && cell.MiddleIndex != -1)
                            {
                                midCells++;
                                if (cell.MiddleIndex >= 0) midLibs.Add(cell.MiddleIndex);
                                var f = GetFrame(cell.MiddleIndex, mid);
                                if (f.HasValue && !f.Value.Empty)
                                {
                                    string key = $"{f.Value.Width}x{f.Value.Height}";
                                    midSizes.TryGetValue(key, out int n);
                                    midSizes[key] = n + 1;
                                }
                                if (f.HasValue && !f.Value.Empty &&
                                    ((f.Value.Width == CellWidth && f.Value.Height == CellHeight) ||
                                     (f.Value.Width == CellWidth * 2 && f.Value.Height == CellHeight * 2)))
                                {
                                    if (DrawFrame(cell.MiddleIndex, mid, drawX, drawY)) count[1]++;
                                }
                            }
                        }

                        // Front：(FrontImage&0x7FFF)-1，门取关态、动画取 0
                        if (layerFilter == "all" || layerFilter == "front")
                        {
                            int fi = (cell.FrontImage & 0x7FFF) - 1;
                            if (fi >= 0 && cell.FrontIndex != -1 && cell.FrontIndex != 200)
                            {
                                frontCells++;
                                var f = GetFrame(cell.FrontIndex, fi);
                                if (f.HasValue && !f.Value.Empty &&
                                    ((f.Value.Width == CellWidth && f.Value.Height == CellHeight) ||
                                     (f.Value.Width == CellWidth * 2 && f.Value.Height == CellHeight * 2)))
                                {
                                    if (DrawFrame(cell.FrontIndex, fi, drawX, drawY)) count[2]++;
                                }
                            }
                        }
                    }
                    if (layerFilter != "back") CrystalSpriteBatch.Flush();
                }
                CrystalSpriteBatch.End();

                var midSizeStr = "";
                foreach (var kv in midSizes) midSizeStr += $"{kv.Key}={kv.Value} ";
                Console.WriteLine($"map-render: back={count[0]} middle={count[1]}/{midCells} front={count[2]}/{frontCells} center=({cx},{cy}) off=({offX},{offY}) range=({rangeX},{rangeY})");
                Console.WriteLine($"map-render: midSizes=[{midSizeStr}] midLibs=[{string.Join(",", new List<int>(midLibs).ConvertAll(i => i.ToString()).ToArray())}]");

                int fail = -1;
                if (!string.IsNullOrEmpty(spot))
                {
                    var p = spot.Split(',');
                    bool front = p[0].StartsWith("f:");
                    if (front) p[0] = p[0].Substring(2);
                    fail = SpotCheck(mapReader, int.Parse(p[0]), int.Parse(p[1]), cx, cy, rtW, rtH, front);
                    Console.WriteLine($"map-render: spot ({p[0]},{p[1]}) {(front ? "front" : "back")} fail={fail}");
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
                    Console.WriteLine($"map-render: wrote {fullOut}");
                    UnityEngine.Object.DestroyImmediate(read);
                }
                EditorApplication.Exit(fail == 0 || fail == -1 ? 0 : 1);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                foreach (var kv in _libs) kv.Value.UnloadAll();
                _libs.Clear();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // 字节级 spot-check：渲染指定格 tile（直通模式），与图集源像素逐像素比对。front=true 验 front 层，否则验 back 层。
        static int SpotCheck(MapReader mr, int sx, int sy, int camX, int camY, int rtW, int rtH, bool front)
        {
            var cell = mr.MapCells[sx, sy];
            int libIndex, index;
            if (front)
            {
                if (cell.FrontIndex == -1 || cell.FrontIndex == 200) { Console.WriteLine("  spot: cell has no front image"); return 2; }
                index = (cell.FrontImage & 0x7FFF) - 1;
                libIndex = cell.FrontIndex;
            }
            else
            {
                if (cell.BackImage == 0 || cell.BackIndex == -1) { Console.WriteLine("  spot: cell has no back image"); return 2; }
                index = (cell.BackImage & 0x1FFFFFFF) - 1;
                libIndex = cell.BackIndex;
            }
            var lib = _libs.TryGetValue(libIndex, out var l) ? l : null;
            if (lib == null || index < 0 || index >= lib.Frames.Length)
            {
                Console.WriteLine($"  spot: {(front ? "front" : "back")} lib/frame unresolvable");
                return 2;
            }
            var f = lib.Frames[index];
            if (f.Empty)
            {
                Console.WriteLine($"  spot: {(front ? "front" : "back")} frame empty");
                return 2;
            }
            var tex = lib.GetPage(f.Page);

            int offX = rtW / 2 / CellWidth;
            int offY = rtH / 2 / CellHeight - 1;
            int dx = (sx - camX + offX) * CellWidth - offX;
            int dy = (sy - camY + offY) * CellHeight;
            Console.WriteLine($"  spot: idx={index} lib={libIndex} {f.Width}x{f.Height} page{f.Page} src=({f.X},{f.Y}) at=({dx},{dy})");

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

                // 图集源：GetPixels32 垂直翻转（row 0=底），行补偿 (ph-1-(f.Y+y))
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

        static AtlasLibrary EnsureLib(int libIndex, string rel)
        {
            if (_libs.TryGetValue(libIndex, out var lib)) return lib;
            string man = Path.Combine(_atlasDir, rel + ".json");
            if (!File.Exists(man))
            {
                Console.WriteLine($"  map-render: WARN manifest missing {man}");
                return null;
            }
            lib = AtlasLibrary.Load(man);
            _libs[libIndex] = lib;
            return lib;
        }

        static SpriteFrame? GetFrame(int libIndex, int index)
        {
            var lib = _libs.TryGetValue(libIndex, out var l) ? l : null;
            if (lib == null || index < 0 || index >= lib.Frames.Length) return null;
            return lib.Frames[index];
        }

        static bool DrawFrame(int libIndex, int index, int drawX, int drawY)
        {
            var lib = _libs.TryGetValue(libIndex, out var l) ? l : null;
            if (lib == null || index < 0 || index >= lib.Frames.Length) return false;
            var f = lib.Frames[index];
            if (f.Empty) return false;
            var tex = lib.GetPage(f.Page);
            if (tex == null) return false;
            CrystalSpriteBatch.Draw(tex, new Rect(f.X, f.Y, f.Width, f.Height),
                new Vector3(drawX, drawY, 0f), Color.white);
            return true;
        }
    }
}
