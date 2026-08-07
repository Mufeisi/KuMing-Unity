using System;
using System.Collections.Generic;
using System.IO;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirScenes;
using Crystal.Client.Assets;
using UnityEngine;
// Client.Core 的 Point 是 MirMath 类型（Ported 文件 via using Crystal.Client.Core.MirMath）；
// 显式别名避免与 UnityEngine.Vector 歧义。
using MPoint = Crystal.Client.Core.MirMath.Point;
using MColor = Crystal.Client.Core.MirMath.Color;

namespace Crystal.Client.Rendering
{
    // 运行时地图/场景渲染核心：从 Editor 探针 SceneRender/MapRender 抽取，去 UnityEditor 依赖，
    // 供 GameBootstrap（可运行 Player）与 Editor 验证共用同一条渲染路径。
    // 覆盖：MapLibs 段映射（MapLibRel）+ 图集缓存 + DrawFloor 三层 tile（Back/Middle/Front）+ 对象合成。
    public static class GameRenderer
    {
        // 图集根目录（AssetCompiler 产物）：对象库在 AtlasDir，地图库在 MapAtlasDir。
        public static string AtlasDir;
        public static string MapAtlasDir;
        // 合并批绘制（G2 实证：合并批 y-sort 安全且高性能，88 calls vs 逐行 612 calls）。
        public static bool BatchFloor = true;

        static readonly Dictionary<string, MLibraryUnity> _mlibCache = new Dictionary<string, MLibraryUnity>();
        static readonly Dictionary<string, AtlasLibrary> _libs = new Dictionary<string, AtlasLibrary>();
        static readonly Dictionary<string, AtlasLibrary> _mapLibs = new Dictionary<string, AtlasLibrary>();

        // MLibrary.cs MapLibs 段映射：0-99 WemadeMir2，100-199 ShandaMir2，200-299 WemadeMir3。
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
            if (idx >= 111 && idx <= 118) return "ShandaMir2/smTiles" + (idx - 109); // 源/产物命名 111→smTiles2 ... 118→smTiles9（非 SmTiles+idx-99，实证偏移）
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

        public static MLibraryUnity EnsureMLibrary(string rel)
        {
            if (_mlibCache.TryGetValue(rel, out var m)) return m;
            var lib = EnsureLib(rel);
            if (lib == null) return null;
            m = new MLibraryUnity(rel) { Atlas = lib, Frames = MLibraryUnity.BridgeFrames(lib.Manifest) };
            _mlibCache[rel] = m;
            return m;
        }

        public static AtlasLibrary EnsureLib(string rel)
        {
            if (_libs.TryGetValue(rel, out var lib)) return lib;
            if (string.IsNullOrEmpty(AtlasDir)) return null;
            string man = Path.Combine(AtlasDir, rel + ".json");
            if (!File.Exists(man))
            {
                Debug.LogWarning($"[gamerenderer] manifest missing {man}");
                _libs[rel] = null; // 负缓存：缺段不每帧重查+刷日志
                return null;
            }
            lib = AtlasLibrary.Load(man);
            _libs[rel] = lib;
            return lib;
        }

        public static AtlasLibrary EnsureMapLib(string rel)
        {
            if (_mapLibs.TryGetValue(rel, out var lib)) return lib;
            if (string.IsNullOrEmpty(MapAtlasDir)) return null;
            string man = Path.Combine(MapAtlasDir, rel + ".json");
            if (!File.Exists(man))
            {
                Debug.LogWarning($"[gamerenderer] map manifest missing {man}");
                _mapLibs[rel] = null; // 负缓存：缺段不每帧重查+刷日志
                return null;
            }
            lib = AtlasLibrary.Load(man);
            _mapLibs[rel] = lib;
            return lib;
        }

        // 预构建 libIndex→AtlasLibrary 数组（避免 DrawTile 热路径重复 MapLibRel 字符串拼接 + 字符串字典查找）。
        public static AtlasLibrary[] BuildLibIndex(int libCount, CellInfo[,] cells, int w, int h)
        {
            var arr = new AtlasLibrary[Math.Max(libCount, 300)];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = cells[x, y];
                    if (c.BackIndex >= 0 && c.BackIndex < arr.Length && arr[c.BackIndex] == null)
                        arr[c.BackIndex] = MapLibRelLazy(c.BackIndex);
                    if (c.MiddleIndex >= 0 && c.MiddleIndex < arr.Length && arr[c.MiddleIndex] == null)
                        arr[c.MiddleIndex] = MapLibRelLazy(c.MiddleIndex);
                    if (c.FrontIndex >= 0 && c.FrontIndex < arr.Length && arr[c.FrontIndex] == null)
                        arr[c.FrontIndex] = MapLibRelLazy(c.FrontIndex);
                }
            return arr;
        }

        public static AtlasLibrary MapLibRelLazy(int libIndex)
        {
            string rel = MapLibRel(libIndex);
            return rel == null ? null : EnsureMapLib(rel);
        }

        // 绘制地面三层（Back/Middle/Front），GameScene.DrawFloor 运行时复刻。
        // 复用：GameBootstrap 每帧 + Editor RenderVerify/SceneRender 对照。
        public static int[] DrawMapTiles(CellInfo[,] cells, MapReader mapReader, int cx, int cy, int offX, int offY, int rangeX, int rangeY, AtlasLibrary[] libByIndex = null)
        {
            int[] floorCount = { 0, 0, 0 };
            const int cellW = MapControl.CellWidth;
            const int cellH = MapControl.CellHeight;
            int startX = cx - rangeX, endX = cx + rangeX;
            int startY = cy - rangeY, endY = cy + rangeY + 5;
            for (int y = startY; y <= endY; y++)
            {
                if (y <= 0) continue;
                if (y >= mapReader.Height) break;
                int drawY = (y - cy + offY) * cellH;
                for (int x = startX; x <= endX; x++)
                {
                    if (x < 0) continue;
                    if (x >= mapReader.Width) break;
                    int drawX = (x - cx + offX) * cellW - offX;
                    var cell = cells[x, y];

                    if (y % 2 == 0 && x % 2 == 0 && cell.BackImage != 0 && cell.BackIndex != -1)
                    {
                        int index = (cell.BackImage & 0x1FFFFFFF) - 1;
                        if (DrawTile(cell.BackIndex, index, drawX, drawY, 0, 0, libByIndex)) floorCount[0]++;
                    }
                    int mid = cell.MiddleImage - 1;
                    if (mid >= 0 && cell.MiddleIndex != -1 && DrawTile(cell.MiddleIndex, mid, drawX, drawY, cellW, cellH, libByIndex)) floorCount[1]++;
                    int fi = (cell.FrontImage & 0x7FFF) - 1;
                    if (fi >= 0 && cell.FrontIndex != -1 && cell.FrontIndex != 200 && DrawTile(cell.FrontIndex, fi, drawX, drawY, cellW, cellH, libByIndex)) floorCount[2]++;
                }
                if (!BatchFloor) CrystalSpriteBatch.Flush();
            }
            return floorCount;
        }

        static bool DrawTile(int libIndex, int index, int drawX, int drawY, int reqW = 0, int reqH = 0, AtlasLibrary[] libByIndex = null)
        {
            AtlasLibrary lib;
            if (libByIndex != null)
            {
                if (libIndex < 0 || libIndex >= libByIndex.Length) return false;
                lib = libByIndex[libIndex];
            }
            else
            {
                string rel = MapLibRel(libIndex);
                lib = rel == null ? null : EnsureMapLib(rel);
            }
            if (lib == null || index < 0 || index >= lib.Frames.Length) return false;
            var f = lib.Frames[index];
            if (f.Empty) return false;
            if (reqW > 0 && (f.Width != reqW || f.Height != reqH)) return false;
            var tex = lib.GetPage(f.Page);
            if (tex == null) return false;
            CrystalSpriteBatch.Draw(tex, new Rect(f.X, f.Y, f.Width, f.Height), new Vector3(drawX, drawY, 0f), Color.white);
            return true;
        }

        // 场景/图集切换时清理全部缓存（防内存与 GPU buffer 泄漏）。
        public static void ReleaseAll()
        {
            _mlibCache.Clear();
            _libs.Clear();
            _mapLibs.Clear();
            CrystalSpriteBatch.ReleaseMeshes();
        }
    }
}
