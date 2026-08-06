using System;
using System.Collections.Generic;
using System.IO;
using Client;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirScenes;
using Crystal.Client.Assets;
using Crystal.Client.Rendering;
using S = ServerPackets;
using MPoint = Crystal.Client.Core.MirMath.Point;
using MColor = Crystal.Client.Core.MirMath.Color;
using SDPoint = System.Drawing.Point;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // R4 探针：场景合成 = 地面（GameScene.DrawFloor 三层）+ 地图对象精灵（FrameSet 帧选择）。
    // 对象按 (Y,X) 行主序 y-sort 绘制（复刻 GameScene.DrawObjects 逐行先远后近），对象锚点公式
    // DrawLocation=((x-camX+offX)*48,(y-camY+offY)*32) 无地面那 -OffSetX 像素校正（MonsterObject.cs:435）。
    // 验证：①每个对象首个不透明局部像素 RT==图集源（锚点钉死）；②两对象重叠区近者（大Y，后画）在上、
    //   远者（小Y，先画）独占区仍可见（y-sort 遮挡）。
    // RunPerf（G2 门禁）：同一 DrawScene 连续 N 帧 1080p 全量绘制，测每帧耗时 → P50/P95/平均 FPS。
    // 用法（batchmode 经 Hub 会话）：
    //   CRYSTAL_MAP_DIR=<maps> CRYSTAL_ATLAS_DIR=<对象图集 all> CRYSTAL_MAP=0.map [CRYSTAL_CENTER=x,y]
    //   [CRYSTAL_MAP_ATLAS_DIR=<地图图集 map>] [CRYSTAL_RT_W=1152] [CRYSTAL_RT_H=640] [CRYSTAL_OUT]
    //   CRYSTAL_OBJECTS="<rel>:<action>:<dir>:<frame>:<x>:<y>;<rel>:..." —— 分号分隔，冒号分段
    //   玩家对象：rel 为 "p" 时 10 段 = p:<action>:<dir>:<frame>:<x>:<y>:<armour>:<hair>:<weapon>:<gender>
    //     （R9 复刻 PlayerObject.SetLibraries C 系列映射：Body=CArmours[Armour]、Hair=CHair[Hair]、
    //       Weapon=CWeapons[Weapon]，帧选择 FrameSet.Player，Gender offset 男0/女808/808/416）
    //   RunPerf 另加：CRYSTAL_FRAMES=<N> [CRYSTAL_WARMUP=<M>] [CRYSTAL_PERF_OUT=<json>]
    static class SceneRender
    {
        const int CellWidth = 48;
        const int CellHeight = 32;

        internal static string _atlasDir;      // 对象图集根（Monster/* 等，Build/assetcompile/all）
        internal static string _mapAtlasDir;   // 地图图集根（WemadeMir2/* 等，Build/assetcompile/map）
        static readonly Dictionary<string, AtlasLibrary> _libs = new Dictionary<string, AtlasLibrary>();
        static readonly Dictionary<string, AtlasLibrary> _mapLibs = new Dictionary<string, AtlasLibrary>();
        // R11：真实对象状态机的可渲染 MLibrary（AtlasLibrary + BridgeFrames 帧表）缓存，写回 Libraries 供 Load 命中。
        static readonly Dictionary<string, MLibraryUnity> _mlibCache = new Dictionary<string, MLibraryUnity>();
        // G2 诊断：CRYSTAL_BATCH=0 → 地面逐行 Flush（旧实现，612 draw calls，遮挡正确但 <60FPS）。
        // 默认合并（绘制序=插入序，SetBlend 切换才分段）→ 48 draw calls，正确性 fail=0 + >60FPS。
        static bool _batchFloor = true;

        class ObjLayer
        {
            public AtlasLibrary Lib;
            public SpriteFrame F;
            public int SpriteX, SpriteY; // 精灵左上 = DrawLocation + (OffX, OffY)
            public Color32[] Src;
            public int TW, TH;
        }

        class Obj
        {
            public string Rel, Action;
            public int Dir, Frame, X, Y;
            public bool IsPlayer;
            public int Armour, Hair, Weapon, Gender; // 玩家对象参数
            public List<ObjLayer> Layers = new List<ObjLayer>();
            public AtlasLibrary Lib;
            public FrameEntry Fe;
            public int Idx;
            public SpriteFrame F;
            public int DrawX, DrawY;   // DrawLocation（格锚点）
            public int SpriteX, SpriteY; // 精灵左上 = DrawLocation + (OffX, OffY)
            public Color32[] Src;
            public int TW, TH;
            public object Real; // 真实对象引用（PlayerObject/MonsterObject），与 objs 排序同步
        }

        // R11 CRYSTAL_OBJSPEC 段：m:<image>:<action>:<dir>:<frame>:<x>:<y> / p:<action>:<dir>:<frame>:<x>:<y>:<armour>:<hair>:<weapon>:<gender>:<class>
        class ObjSpec
        {
            public char Kind;
            public int Image;
            public string Action;
            public int Dir, Frame, X, Y;
            public int Armour, Hair, Weapon, Gender, Cls;
        }

        public static void Run()
        {
            string mapDir = Environment.GetEnvironmentVariable("CRYSTAL_MAP_DIR");
            string atlasDir = Environment.GetEnvironmentVariable("CRYSTAL_ATLAS_DIR");
            string map = Environment.GetEnvironmentVariable("CRYSTAL_MAP");
            string objSpec = Environment.GetEnvironmentVariable("CRYSTAL_OBJECTS");
            if (string.IsNullOrEmpty(mapDir) || string.IsNullOrEmpty(atlasDir) || string.IsNullOrEmpty(map) || string.IsNullOrEmpty(objSpec))
            {
                Console.WriteLine("scene-render: CRYSTAL_MAP_DIR / CRYSTAL_ATLAS_DIR / CRYSTAL_MAP / CRYSTAL_OBJECTS not set");
                EditorApplication.Exit(2);
                return;
            }
            _atlasDir = Path.GetFullPath(atlasDir);
            string mapAtlasDir = Environment.GetEnvironmentVariable("CRYSTAL_MAP_ATLAS_DIR");
            if (string.IsNullOrEmpty(mapAtlasDir)) mapAtlasDir = _atlasDir;
            _mapAtlasDir = Path.GetFullPath(mapAtlasDir);
            mapDir = Path.GetFullPath(mapDir);
            _batchFloor = Environment.GetEnvironmentVariable("CRYSTAL_BATCH") != "0";
            CrystalSpriteBatch._debugFlush = Environment.GetEnvironmentVariable("CRYSTAL_FLUSH_DEBUG") == "1";
            CrystalSpriteBatch.MeshRebuildCount = 0;

            string mapPath = Path.Combine(mapDir, map);
            if (!File.Exists(mapPath))
            {
                Console.WriteLine($"scene-render: map missing {mapPath}");
                EditorApplication.Exit(2);
                return;
            }
            var mapReader = new MapReader(mapPath);
            var cells = mapReader.MapCells;
            Console.WriteLine($"scene-render: {map} {mapReader.Width}x{mapReader.Height}");

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
            string outPath = Environment.GetEnvironmentVariable("CRYSTAL_OUT");
            if (string.IsNullOrEmpty(outPath)) outPath = "Build/scene-render.png";

            int offX = rtW / 2 / CellWidth;
            int offY = rtH / 2 / CellHeight - 1;
            int rangeX = offX + 6, rangeY = offY + 6;

            // 解析对象表
            var objs = new List<Obj>();
            foreach (string token in objSpec.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                var p = token.Split(':');
                if (p[0] == "p")
                {
                    // 玩家对象：p:<action>:<dir>:<frame>:<x>:<y>:<armour>:<hair>:<weapon>:<gender>
                    if (p.Length != 10)
                    {
                        Console.WriteLine($"scene-render: bad player spec [{token}]");
                        EditorApplication.Exit(2);
                        return;
                    }
                    var po = new Obj
                    {
                        IsPlayer = true,
                        Action = p[1],
                        Dir = int.Parse(p[2]), Frame = int.Parse(p[3]),
                        X = int.Parse(p[4]), Y = int.Parse(p[5]),
                        Armour = int.Parse(p[6]), Hair = int.Parse(p[7]),
                        Weapon = int.Parse(p[8]), Gender = int.Parse(p[9]),
                    };
                    if (!ResolvePlayer(po, cx, cy, offX, offY)) return;
                    objs.Add(po);
                }
                else
                {
                    if (p.Length != 6)
                    {
                        Console.WriteLine($"scene-render: bad object spec [{token}]");
                        EditorApplication.Exit(2);
                        return;
                    }
                    var o = new Obj
                    {
                        Rel = p[0], Action = p[1],
                        Dir = int.Parse(p[2]), Frame = int.Parse(p[3]),
                        X = int.Parse(p[4]), Y = int.Parse(p[5])
                    };
                    if (!ResolveObject(o, cx, cy, offX, offY)) return;
                    objs.Add(o);
                }
            }
            if (objs.Count < 1)
            {
                Console.WriteLine("scene-render: no objects parsed");
                EditorApplication.Exit(2);
                return;
            }

            // 加载地面用到的图集（地图图集根）
            int missing = 0;
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
            foreach (int li in sortedLibs)
            {
                string rel = MapRender.MapLibRel(li);
                if (rel == null || EnsureMapLib(rel) == null) missing++;
            }
            Console.WriteLine($"scene-render: floorLibs={sortedLibs.Count} unresolved={missing} objects={objs.Count} center=({cx},{cy}) off=({offX},{offY})");

            // y-sort：行主序 (Y,X)，先远（小Y）后近（大Y）
            objs.Sort(delegate (Obj a, Obj b)
            {
                int c = a.Y.CompareTo(b.Y);
                return c != 0 ? c : a.X.CompareTo(b.X);
            });

            var rt = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            int fail = -1;
            try
            {
                var libByIndex = BuildLibIndex(sortedLibs.Count, cells, mapReader.Width, mapReader.Height);
                CrystalSpriteBatch.Begin(rt, rtW, rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                int[] floorCount = DrawScene(cells, mapReader, cx, cy, offX, offY, rangeX, rangeY, objs, libByIndex);
                CrystalSpriteBatch.End();
                Console.WriteLine($"scene-render: floor back={floorCount[0]} middle={floorCount[1]} front={floorCount[2]}");

                var read = new Texture2D(rtW, rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, rtW, rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                fail = 0;
                // ①每个对象锚点钉死 + 源像素存在（跳过其后续对象 bbox，避免被遮挡像素误报）
                for (int i = 0; i < objs.Count; i++)
                {
                    var later = new List<Obj>();
                    for (int k = i + 1; k < objs.Count; k++) later.Add(objs[k]);
                    int d = VerifyPresence(objs[i], later, px, rtW, rtH);
                    Console.WriteLine($"  obj {objs[i].Rel}/{objs[i].Action} dir={objs[i].Dir} f={objs[i].Frame} idx={objs[i].Idx} at cell({objs[i].X},{objs[i].Y}) " +
                        $"draw=({objs[i].DrawX},{objs[i].DrawY}) sprite=({objs[i].SpriteX},{objs[i].SpriteY}) {objs[i].F.Width}x{objs[i].F.Height} fail={d}");
                    fail += d;
                }
                // ②y-sort 遮挡：末对象 vs 其前所有重叠者
                for (int i = 1; i < objs.Count; i++)
                {
                    for (int j = 0; j < i; j++)
                    {
                        int d = VerifyOcclusion(objs[j], objs[i], px, rtW, rtH);
                        Console.WriteLine($"  occlusion far=({objs[j].X},{objs[j].Y}) near=({objs[i].X},{objs[i].Y}) fail={d}");
                        fail += d;
                    }
                }

                // 正立 PNG（EncodeToPNG 反行序 → 先翻转）
                var fl = new Color32[px.Length];
                for (int y = 0; y < rtH; y++)
                    Array.Copy(px, (rtH - 1 - y) * rtW, fl, y * rtW, rtW);
                read.SetPixels32(fl);
                read.Apply();
                string fullOut = Path.GetFullPath(outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                Console.WriteLine($"scene-render: wrote {fullOut} fail={fail}");
                UnityEngine.Object.DestroyImmediate(read);
                EditorApplication.Exit(fail == 0 ? 0 : 1);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                foreach (var kv in _libs) kv.Value.UnloadAll();
                _libs.Clear();
                foreach (var kv in _mapLibs) kv.Value.UnloadAll();
                _mapLibs.Clear();
                CrystalSpriteBatch.ReleaseMeshes();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // R11 探针：真实对象状态机驱动场景渲染（MonsterObject/PlayerObject Load→Process→Draw 接入 SceneRender），
        // 取代 R10 手工 spec（CRYSTAL_OBJECTS）。验证：
        //  ①数据级：真实 DrawFrame == R10 公式 Fe.Start+(Fe.Count+Fe.Skip)*Dir+Frame（逐字节一致）
        //  ②像素级：真实对象经 MLibraryUnity.DrawIndex 渲染的 RT 像素（VerifyPresence/VerifyOcclusion）
        // 确定性：GameScene.CanMove=false 全程（冻结 Walking 帧推进）；NextMotion=long.MaxValue 冻结 Standing/Attack；
        //   MonsterObject.Load 的随机 Standing 帧与 PlayerObject.SetAction 的 Stance 陷阱（Time=0）均在 Land 覆写。
        // 用法：同 Run 环境变量 + CRYSTAL_OBJSPEC（分号分隔、冒号分段）：
        //   m:<image>:<action>:<dir>:<frame>:<x>:<y>                       怪物（image=Monster enum 数值，0=Guard 走 Monsters 数组）
        //   p:<action>:<dir>:<frame>:<x>:<y>:<armour>:<hair>:<weapon>:<gender>:<class>  玩家（R11 仅验证 Standing）
        public static void RunObjects()
        {
            string mapDir = Environment.GetEnvironmentVariable("CRYSTAL_MAP_DIR");
            string atlasDir = Environment.GetEnvironmentVariable("CRYSTAL_ATLAS_DIR");
            string map = Environment.GetEnvironmentVariable("CRYSTAL_MAP");
            string objSpec = Environment.GetEnvironmentVariable("CRYSTAL_OBJSPEC");
            if (string.IsNullOrEmpty(mapDir) || string.IsNullOrEmpty(atlasDir) || string.IsNullOrEmpty(map) || string.IsNullOrEmpty(objSpec))
            {
                Console.WriteLine("scene-objects: CRYSTAL_MAP_DIR / CRYSTAL_ATLAS_DIR / CRYSTAL_MAP / CRYSTAL_OBJSPEC not set");
                EditorApplication.Exit(2);
                return;
            }
            _atlasDir = Path.GetFullPath(atlasDir);
            string mapAtlasDir = Environment.GetEnvironmentVariable("CRYSTAL_MAP_ATLAS_DIR");
            if (string.IsNullOrEmpty(mapAtlasDir)) mapAtlasDir = _atlasDir;
            _mapAtlasDir = Path.GetFullPath(mapAtlasDir);
            mapDir = Path.GetFullPath(mapDir);
            _batchFloor = Environment.GetEnvironmentVariable("CRYSTAL_BATCH") != "0";

            string mapPath = Path.Combine(mapDir, map);
            if (!File.Exists(mapPath)) { Console.WriteLine($"scene-objects: map missing {mapPath}"); EditorApplication.Exit(2); return; }
            var mapReader = new MapReader(mapPath);
            var cells = mapReader.MapCells;

            int rtW = GetInt("CRYSTAL_RT_W", 1152);
            int rtH = GetInt("CRYSTAL_RT_H", 640);
            string center = Environment.GetEnvironmentVariable("CRYSTAL_CENTER");
            int cx, cy;
            if (!string.IsNullOrEmpty(center) && center.Contains(",")) { var p = center.Split(','); cx = int.Parse(p[0]); cy = int.Parse(p[1]); }
            else { cx = mapReader.Width / 2; cy = mapReader.Height / 2; }
            string outPath = Environment.GetEnvironmentVariable("CRYSTAL_OUT");
            if (string.IsNullOrEmpty(outPath)) outPath = "Build/scene-objects.png";

            int offX = rtW / 2 / CellWidth;
            int offY = rtH / 2 / CellHeight - 1;
            int rangeX = offX + 6, rangeY = offY + 6;

            var specs = new List<ObjSpec>();
            foreach (string token in objSpec.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                var p = token.Split(':');
                if (p[0] == "p")
                {
                    if (p.Length != 11) { Console.WriteLine($"scene-objects: bad player spec [{token}] (expect 11)"); EditorApplication.Exit(2); return; }
                    specs.Add(new ObjSpec
                    {
                        Kind = 'p', Action = p[1], Dir = int.Parse(p[2]), Frame = int.Parse(p[3]),
                        X = int.Parse(p[4]), Y = int.Parse(p[5]), Armour = int.Parse(p[6]), Hair = int.Parse(p[7]),
                        Weapon = int.Parse(p[8]), Gender = int.Parse(p[9]), Cls = int.Parse(p[10]),
                    });
                }
                else
                {
                    if (p.Length != 7) { Console.WriteLine($"scene-objects: bad monster spec [{token}] (expect 7)"); EditorApplication.Exit(2); return; }
                    specs.Add(new ObjSpec
                    {
                        Kind = 'm', Image = int.Parse(p[1]), Action = p[2], Dir = int.Parse(p[3]),
                        Frame = int.Parse(p[4]), X = int.Parse(p[5]), Y = int.Parse(p[6]),
                    });
                }
            }
            if (specs.Count < 1) { Console.WriteLine("scene-objects: no specs parsed"); EditorApplication.Exit(2); return; }

            int missing = 0;
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
            foreach (int li in sortedLibs)
            {
                string rel = MapRender.MapLibRel(li);
                if (rel == null || EnsureMapLib(rel) == null) missing++;
            }

            // 相机 + 场景静态（DrawLocation 公式依赖 MapObject.User / MapControl.OffSetX/Y）
            var user = new UserObject(999999)
            {
                Movement = new MPoint(cx, cy),
                CurrentLocation = new MPoint(cx, cy),
                OffSetMove = MPoint.Empty,
                Name = "probe",
            };
            MapObject.User = user;
            GameScene.Scene = new GameScene { MapControl = new MapControl() };
            GameScene.CanMove = false;
            MapControl.OffSetX = offX;
            MapControl.OffSetY = offY;

            // 预 EnsureMLibrary 并写回 Libraries（真实对象 Load 时命中）
            foreach (var s in specs)
            {
                if (s.Kind == 'm')
                {
                    var m = EnsureMLibrary($"Monster/{s.Image:D3}");
                    if (m == null) { Console.WriteLine($"scene-objects: Monster/{s.Image:D3} missing"); EditorApplication.Exit(2); return; }
                    Libraries.Monsters[s.Image] = m;
                }
                else
                {
                    var b = EnsureMLibrary($"CArmour/{s.Armour:D2}");
                    if (b == null) { Console.WriteLine($"scene-objects: CArmour/{s.Armour:D2} missing"); EditorApplication.Exit(2); return; }
                    Libraries.CArmours[s.Armour] = b;
                    var h = EnsureMLibrary($"CHair/{s.Hair:D2}");
                    if (h == null) { Console.WriteLine($"scene-objects: CHair/{s.Hair:D2} missing"); EditorApplication.Exit(2); return; }
                    Libraries.CHair[s.Hair] = h;
                    if (s.Weapon >= 0)
                    {
                        var w = EnsureMLibrary($"CWeapon/{s.Weapon:D2}");
                        if (w == null) { Console.WriteLine($"scene-objects: CWeapon/{s.Weapon:D2} missing"); EditorApplication.Exit(2); return; }
                        Libraries.CWeapons[s.Weapon] = w;
                    }
                }
            }
            Console.WriteLine($"scene-objects: {map} {mapReader.Width}x{mapReader.Height} floorLibs={sortedLibs.Count} unresolved={missing} objects={specs.Count} center=({cx},{cy}) off=({offX},{offY})");

            var objs = new List<Obj>();
            var realObjs = new List<MapObject>();
            int oid = 1000;
            foreach (var s in specs)
            {
                var o = new Obj
                {
                    Rel = s.Kind == 'm' ? $"m:{s.Image}" : "p", Action = s.Action, Dir = s.Dir, Frame = s.Frame,
                    X = s.X, Y = s.Y, IsPlayer = s.Kind == 'p', Armour = s.Armour, Hair = s.Hair, Weapon = s.Weapon, Gender = s.Gender,
                };
                if (s.Kind == 'm')
                {
                    var mo = new MonsterObject((uint)oid++);
                    mo.Load(new S.ObjectMonster
                    {
                        ObjectID = mo.ObjectID,
                        Image = (Monster)s.Image,
                        Direction = (MirDirection)s.Dir,
                        Location = new SDPoint(s.X, s.Y),
                        Buffs = new List<BuffType>(),
                    });
                    LandMonster(mo, s.Action, s.Dir, s.Frame);
                    realObjs.Add(mo);
                    o.Real = mo;
                }
                else
                {
                    var po = new PlayerObject((uint)oid++);
                    po.Load(new S.ObjectPlayer
                    {
                        ObjectID = po.ObjectID,
                        Class = (MirClass)s.Cls,
                        Gender = (MirGender)s.Gender,
                        Level = 1,
                        Direction = (MirDirection)s.Dir,
                        Location = new SDPoint(s.X, s.Y),
                        Hair = (byte)s.Hair,
                        Weapon = (short)s.Weapon,
                        Armour = (short)s.Armour,
                        TransformType = -1, // 默认 0 会误入 Transform 分支（BodyLibrary=Transform[0]=null）
                        Buffs = new List<BuffType>(),
                    });
                    LandPlayer(po, s.Action, s.Dir, s.Frame);
                    realObjs.Add(po);
                    o.Real = po;
                }
                objs.Add(o);
            }

            // 逐帧 Process（CanMove=false 冻结帧推进，DrawFrame/DrawLocation 每帧重算）
            CMain.Time = 0;
            for (int f = 0; f < 3; f++)
            {
                foreach (var mo in realObjs) mo.Process();
                CMain.Time += 100;
            }

            int fail = 0;
            for (int i = 0; i < objs.Count; i++)
            {
                int d = objs[i].IsPlayer
                    ? FillObjFromPlayer(objs[i], (PlayerObject)objs[i].Real, specs[i])
                    : FillObjFromMonster(objs[i], (MonsterObject)objs[i].Real, specs[i]);
                if (d < 0) { EditorApplication.Exit(2); return; }
                fail += d;
            }

            objs.Sort(delegate (Obj a, Obj b) { int c = a.Y.CompareTo(b.Y); return c != 0 ? c : a.X.CompareTo(b.X); });

            var rt = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                CrystalSpriteBatch.Begin(rt, rtW, rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                var libByIndex = BuildLibIndex(sortedLibs.Count, cells, mapReader.Width, mapReader.Height);
                int[] floorCount = DrawMapTiles(cells, mapReader, cx, cy, offX, offY, rangeX, rangeY, libByIndex);
                Console.WriteLine($"scene-objects: floor back={floorCount[0]} middle={floorCount[1]} front={floorCount[2]}");
                for (int i = 0; i < objs.Count; i++)
                {
                    var o = objs[i];
                    if (o.IsPlayer)
                    {
                        var po = (PlayerObject)o.Real;
                        var body = po.BodyLibrary as MLibraryUnity;
                        if (body != null) body.DrawIndex(po.DrawFrame + po.ArmourOffSet, po.DrawLocation, po.DrawColour, true, 1f);
                        var hair = po.HairLibrary as MLibraryUnity;
                        if (hair != null) hair.DrawIndex(po.DrawFrame + po.HairOffSet, po.DrawLocation, po.DrawColour, true, 1f);
                        var weapon = po.WeaponLibrary1 as MLibraryUnity;
                        if (weapon != null) weapon.DrawIndex(po.DrawFrame + po.WeaponOffSet, po.DrawLocation, po.DrawColour, true, 1f);
                    }
                    else
                    {
                        var mb = (MonsterObject)o.Real;
                        var lib = mb.BodyLibrary as MLibraryUnity;
                        if (lib != null) lib.DrawIndex(mb.DrawFrame, mb.DrawLocation, mb.DrawColour, true, 1f);
                    }
                }
                CrystalSpriteBatch.End();

                var read = new Texture2D(rtW, rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, rtW, rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                for (int i = 0; i < objs.Count; i++)
                {
                    var later = new List<Obj>();
                    for (int k = i + 1; k < objs.Count; k++) later.Add(objs[k]);
                    int d = VerifyPresence(objs[i], later, px, rtW, rtH);
                    Console.WriteLine($"  obj {objs[i].Rel}/{objs[i].Action} dir={objs[i].Dir} f={objs[i].Frame} idx={objs[i].Idx} at cell({objs[i].X},{objs[i].Y}) " +
                        $"draw=({objs[i].DrawX},{objs[i].DrawY}) sprite=({objs[i].SpriteX},{objs[i].SpriteY}) {objs[i].F.Width}x{objs[i].F.Height} fail={d}");
                    fail += d;
                }
                for (int i = 1; i < objs.Count; i++)
                    for (int j = 0; j < i; j++)
                    {
                        int d = VerifyOcclusion(objs[j], objs[i], px, rtW, rtH);
                        Console.WriteLine($"  occlusion far=({objs[j].X},{objs[j].Y}) near=({objs[i].X},{objs[i].Y}) fail={d}");
                        fail += d;
                    }

                var fl = new Color32[px.Length];
                for (int y = 0; y < rtH; y++)
                    Array.Copy(px, (rtH - 1 - y) * rtW, fl, y * rtW, rtW);
                read.SetPixels32(fl);
                read.Apply();
                string fullOut = Path.GetFullPath(outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                Console.WriteLine($"scene-objects: wrote {fullOut} fail={fail}");
                UnityEngine.Object.DestroyImmediate(read);
                EditorApplication.Exit(fail == 0 ? 0 : 1);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                MapControl.Objects.Clear();
                MapControl.ObjectsList.Clear();
                MapControl.Effects.Clear();
                MapObject.User = null;
                GameScene.Scene = null;
                GameScene.User = null;
                MapControl.OffSetX = 0;
                MapControl.OffSetY = 0;
                for (int i = 0; i < Libraries.Monsters.Length; i++) Libraries.Monsters[i] = null;
                for (int i = 0; i < Libraries.CArmours.Length; i++) Libraries.CArmours[i] = null;
                for (int i = 0; i < Libraries.CHair.Length; i++) Libraries.CHair[i] = null;
                for (int i = 0; i < Libraries.CWeapons.Length; i++) Libraries.CWeapons[i] = null;
                foreach (var kv in _libs) kv.Value.UnloadAll();
                _libs.Clear();
                foreach (var kv in _mapLibs) kv.Value.UnloadAll();
                _mapLibs.Clear();
                _mlibCache.Clear();
                CrystalSpriteBatch.ReleaseMeshes();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // 真实怪物对象落地到确定性 (action,dir,frame)：Load 已 SetAction→Standing，此处覆写。
        // CanMove=false + NextMotion=long.MaxValue 冻结 ProcessFrames 帧推进（Walking 走 CanMove 门控）。
        static void LandMonster(MonsterObject mo, string action, int dir, int frame)
        {
            var a = (MirAction)Enum.Parse(typeof(MirAction), action);
            mo.Direction = (MirDirection)dir;
            mo.CurrentAction = a;
            mo.Frames.TryGetValue(a, out var f);
            mo.Frame = f;
            mo.FrameIndex = frame;
            mo.NextMotion = long.MaxValue;
        }

        // 真实玩家对象落地：Load 后 CurrentAction=Stance（Time=0 陷阱），覆写 Standing；R11 仅验证 Standing。
        static void LandPlayer(PlayerObject po, string action, int dir, int frame)
        {
            po.Direction = (MirDirection)dir;
            po.CurrentAction = MirAction.Standing;
            po.Frames.TryGetValue(MirAction.Standing, out var f);
            po.Frame = f;
            po.FrameIndex = frame;
            po.NextMotion = long.MaxValue;
        }

        // 回填 Obj（真实 DrawFrame/DrawLocation/锚点）+ 数据级验证：真实 DrawFrame == R10 公式。
        // 返回 -1 致命（spec/图集错误）/ 0 数据级一致 / 1 数据级不一致。
        static int FillObjFromMonster(Obj o, MonsterObject mo, ObjSpec s)
        {
            o.Lib = EnsureLib($"Monster/{s.Image:D3}");
            if (o.Lib == null) { Console.WriteLine($"  obj: manifest missing Monster/{s.Image:D3}"); return -1; }
            FrameEntry fe = null;
            foreach (var e in o.Lib.Manifest.Frames)
                if (e.Action == s.Action) { fe = e; break; }
            if (fe == null) { Console.WriteLine($"  obj: action {s.Action} not in Monster/{s.Image:D3}"); return -1; }
            o.Fe = fe;
            int expected = fe.Start + (fe.Count + fe.Skip) * s.Dir + s.Frame;
            o.Idx = expected;
            int realIdx = mo.DrawFrame;
            if (realIdx < 0 || realIdx >= o.Lib.Frames.Length)
            {
                Console.WriteLine($"  obj: realDrawFrame {realIdx} out of range [{o.Lib.Frames.Length}]");
                return -1;
            }
            o.F = o.Lib.Frames[realIdx];
            if (o.F.Empty) { Console.WriteLine($"  obj: frame empty realDrawFrame={realIdx}"); return -1; }
            o.DrawX = mo.DrawLocation.X; o.DrawY = mo.DrawLocation.Y;
            o.SpriteX = o.DrawX + o.F.OffX; o.SpriteY = o.DrawY + o.F.OffY;
            var tex = o.Lib.GetPage(o.F.Page);
            o.TW = tex.width; o.TH = tex.height; o.Src = tex.GetPixels32();
            o.Layers.Clear();
            AddLayer(o, o.Lib, o.F, o.DrawX, o.DrawY);
            bool ok = realIdx == expected;
            Console.WriteLine($"  obj m:{s.Image} {s.Action} dir={s.Dir} f={s.Frame} realDrawFrame={realIdx} expected={expected} dataMatch={ok}");
            return ok ? 0 : 1;
        }

        static int FillObjFromPlayer(Obj o, PlayerObject po, ObjSpec s)
        {
            if (!FrameSet.Player.TryGetValue(MirAction.Standing, out var frm))
            { Console.WriteLine($"  obj: Standing not in FrameSet.Player"); return -1; }
            int expected = frm.Start + (frm.Count + frm.Skip) * s.Dir + s.Frame;
            o.Idx = expected;
            int realIdx = po.DrawFrame;
            o.Lib = EnsureLib($"CArmour/{s.Armour:D2}");
            if (o.Lib == null) { Console.WriteLine($"  obj: CArmour/{s.Armour:D2} missing"); return -1; }
            int bodyIdx = realIdx + po.ArmourOffSet;
            if (bodyIdx < 0 || bodyIdx >= o.Lib.Frames.Length)
            {
                Console.WriteLine($"  obj: body idx {bodyIdx} out of range [{o.Lib.Frames.Length}]");
                return -1;
            }
            o.F = o.Lib.Frames[bodyIdx];
            if (o.F.Empty) { Console.WriteLine($"  obj: body frame empty bodyIdx={bodyIdx}"); return -1; }
            o.DrawX = po.DrawLocation.X; o.DrawY = po.DrawLocation.Y;
            o.SpriteX = o.DrawX + o.F.OffX; o.SpriteY = o.DrawY + o.F.OffY;
            var tex = o.Lib.GetPage(o.F.Page);
            o.TW = tex.width; o.TH = tex.height; o.Src = tex.GetPixels32();
            o.Layers.Clear();
            AddLayer(o, o.Lib, o.F, o.DrawX, o.DrawY);
            var hLib = po.HairLibrary as MLibraryUnity;
            if (hLib != null)
            {
                int hIdx = realIdx + po.HairOffSet;
                if (hIdx >= 0 && hIdx < hLib.Atlas.Frames.Length)
                {
                    var hf = hLib.Atlas.Frames[hIdx];
                    if (!hf.Empty) AddLayer(o, hLib.Atlas, hf, o.DrawX, o.DrawY);
                }
            }
            var wLib = po.WeaponLibrary1 as MLibraryUnity;
            if (wLib != null)
            {
                int wIdx = realIdx + po.WeaponOffSet;
                if (wIdx >= 0 && wIdx < wLib.Atlas.Frames.Length)
                {
                    var wf = wLib.Atlas.Frames[wIdx];
                    if (!wf.Empty) AddLayer(o, wLib.Atlas, wf, o.DrawX, o.DrawY);
                }
            }
            bool ok = realIdx == expected;
            Console.WriteLine($"  obj p {s.Action} dir={s.Dir} f={s.Frame} realDrawFrame={realIdx} expected={expected} dataMatch={ok} layers={o.Layers.Count}");
            return ok ? 0 : 1;
        }

        // 构造 MLibraryUnity（AtlasLibrary + BridgeFrames 帧表），缓存复用。
        internal static MLibraryUnity EnsureMLibrary(string rel)
        {
            if (_mlibCache.TryGetValue(rel, out var m)) return m;
            var lib = EnsureLib(rel);
            if (lib == null) return null;
            m = new MLibraryUnity(rel) { Atlas = lib, Frames = MLibraryUnity.BridgeFrames(lib.Manifest) };
            _mlibCache[rel] = m;
            return m;
        }

        // 绘制地面三层 + 对象（y-sort 顺序）。复用：Run（验证）+ RunPerf（性能）。
        // 地图 tile 绘制（Back/Middle/Front），R10 DrawScene 与 R11 RunObjects 共用
        internal static int[] DrawMapTiles(CellInfo[,] cells, MapReader mapReader, int cx, int cy, int offX, int offY, int rangeX, int rangeY, AtlasLibrary[] libByIndex = null)
        {
            int[] floorCount = { 0, 0, 0 };
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

                    if (y % 2 == 0 && x % 2 == 0 && cell.BackImage != 0 && cell.BackIndex != -1)
                    {
                        int index = (cell.BackImage & 0x1FFFFFFF) - 1;
                        if (DrawTile(cell.BackIndex, index, drawX, drawY, 0, 0, libByIndex)) floorCount[0]++;
                    }
                    int mid = cell.MiddleImage - 1;
                    if (mid >= 0 && cell.MiddleIndex != -1 && DrawTile(cell.MiddleIndex, mid, drawX, drawY, CellWidth, CellHeight, libByIndex)) floorCount[1]++;
                    int fi = (cell.FrontImage & 0x7FFF) - 1;
                    if (fi >= 0 && cell.FrontIndex != -1 && cell.FrontIndex != 200 && DrawTile(cell.FrontIndex, fi, drawX, drawY, CellWidth, CellHeight, libByIndex)) floorCount[2]++;
                }
                if (!_batchFloor) CrystalSpriteBatch.Flush();
            }
            return floorCount;
        }

        static int[] DrawScene(CellInfo[,] cells, MapReader mapReader, int cx, int cy, int offX, int offY, int rangeX, int rangeY, List<Obj> objs, AtlasLibrary[] libByIndex = null)
        {
            int[] floorCount = DrawMapTiles(cells, mapReader, cx, cy, offX, offY, rangeX, rangeY, libByIndex);

            // 对象（y-sort 顺序）；玩家对象按 Layer 顺序画（Body→Hair→Weapon，复刻 DrawBody/Head/Weapon）
            foreach (var o in objs)
            {
                if (o.IsPlayer)
                {
                    foreach (var layer in o.Layers)
                    {
                        var tex = layer.Lib.GetPage(layer.F.Page);
                        CrystalSpriteBatch.Draw(tex, new Rect(layer.F.X, layer.F.Y, layer.F.Width, layer.F.Height),
                            new Vector3(layer.SpriteX, layer.SpriteY, 0f), Color.white);
                    }
                }
                else
                {
                    var tex = o.Lib.GetPage(o.F.Page);
                    CrystalSpriteBatch.Draw(tex, new Rect(o.F.X, o.F.Y, o.F.Width, o.F.Height),
                        new Vector3(o.SpriteX, o.SpriteY, 0f), Color.white);
                }
            }
            CrystalSpriteBatch.Flush();
            return floorCount;
        }

        // G2 性能门禁：1080p 代表场景渲染 FPS 基线。连续 N 帧全量绘制（每帧独立 Clear+Draw+End），
        // 测每帧耗时（含 CPU 提交 + GPU 同步），输出 P50/P95/平均 FPS。目标 1080p 60FPS（P95 ≤ 16.6ms）。
        // 用法：同 Run 环境变量 + CRYSTAL_FRAMES=<N>（默认 120）[CRYSTAL_WARMUP=<M>]（默认 10）。
        //   CRYSTAL_PERF_OUT=<json 路径>（可选，写 P50/P95/avg ms）。
        public static void RunPerf()
        {
            string mapDir = Environment.GetEnvironmentVariable("CRYSTAL_MAP_DIR");
            string atlasDir = Environment.GetEnvironmentVariable("CRYSTAL_ATLAS_DIR");
            string map = Environment.GetEnvironmentVariable("CRYSTAL_MAP");
            string objSpec = Environment.GetEnvironmentVariable("CRYSTAL_OBJECTS");
            if (string.IsNullOrEmpty(mapDir) || string.IsNullOrEmpty(atlasDir) || string.IsNullOrEmpty(map) || string.IsNullOrEmpty(objSpec))
            {
                Console.WriteLine("scene-perf: CRYSTAL_MAP_DIR / CRYSTAL_ATLAS_DIR / CRYSTAL_MAP / CRYSTAL_OBJECTS not set");
                EditorApplication.Exit(2);
                return;
            }
            _atlasDir = Path.GetFullPath(atlasDir);
            string mapAtlasDir = Environment.GetEnvironmentVariable("CRYSTAL_MAP_ATLAS_DIR");
            if (string.IsNullOrEmpty(mapAtlasDir)) mapAtlasDir = _atlasDir;
            _mapAtlasDir = Path.GetFullPath(mapAtlasDir);
            mapDir = Path.GetFullPath(mapDir);
            _batchFloor = Environment.GetEnvironmentVariable("CRYSTAL_BATCH") != "0";

            string mapPath = Path.Combine(mapDir, map);
            if (!File.Exists(mapPath)) { Console.WriteLine($"scene-perf: map missing {mapPath}"); EditorApplication.Exit(2); return; }
            var mapReader = new MapReader(mapPath);
            var cells = mapReader.MapCells;

            int rtW = GetInt("CRYSTAL_RT_W", 1920);
            int rtH = GetInt("CRYSTAL_RT_H", 1080);
            int frames = GetInt("CRYSTAL_FRAMES", 120);
            int warmup = GetInt("CRYSTAL_WARMUP", 10);
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

            var objs = new List<Obj>();
            foreach (string token in objSpec.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                var p = token.Split(':');
                if (p[0] == "p")
                {
                    if (p.Length != 10) { Console.WriteLine($"scene-perf: bad player spec [{token}]"); EditorApplication.Exit(2); return; }
                    var po = new Obj
                    {
                        IsPlayer = true,
                        Action = p[1],
                        Dir = int.Parse(p[2]), Frame = int.Parse(p[3]),
                        X = int.Parse(p[4]), Y = int.Parse(p[5]),
                        Armour = int.Parse(p[6]), Hair = int.Parse(p[7]),
                        Weapon = int.Parse(p[8]), Gender = int.Parse(p[9]),
                    };
                    if (!ResolvePlayer(po, cx, cy, offX, offY)) return;
                    objs.Add(po);
                }
                else
                {
                    if (p.Length != 6) { Console.WriteLine($"scene-perf: bad object spec [{token}]"); EditorApplication.Exit(2); return; }
                    var o = new Obj
                    {
                        Rel = p[0], Action = p[1],
                        Dir = int.Parse(p[2]), Frame = int.Parse(p[3]),
                        X = int.Parse(p[4]), Y = int.Parse(p[5])
                    };
                    if (!ResolveObject(o, cx, cy, offX, offY)) return;
                    objs.Add(o);
                }
            }
            if (objs.Count < 1) { Console.WriteLine("scene-perf: no objects parsed"); EditorApplication.Exit(2); return; }

            // 预加载地图库
            int missing = 0;
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
            foreach (int li in sortedLibs)
            {
                string rel = MapRender.MapLibRel(li);
                if (rel == null || EnsureMapLib(rel) == null) missing++;
            }
            Console.WriteLine($"scene-perf: {map} {mapReader.Width}x{mapReader.Height} floorLibs={sortedLibs.Count} unresolved={missing} objects={objs.Count} rt={rtW}x{rtH} frames={frames} warmup={warmup}");

            objs.Sort(delegate (Obj a, Obj b)
            {
                int c = a.Y.CompareTo(b.Y);
                return c != 0 ? c : a.X.CompareTo(b.X);
            });

            var rt = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                var libByIndex = BuildLibIndex(sortedLibs.Count, cells, mapReader.Width, mapReader.Height);
                var times = new double[frames];
                var drawMs = new double[frames];
                var flushMs = new double[frames];
                int quads = 0;
                for (int i = 0; i < frames + warmup; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    CrystalSpriteBatch.Begin(rt, rtW, rtH);
                    CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                    var swd = System.Diagnostics.Stopwatch.StartNew();
                    DrawScene(cells, mapReader, cx, cy, offX, offY, rangeX, rangeY, objs, libByIndex);
                    swd.Stop();
                    CrystalSpriteBatch.End();
                    sw.Stop();
                    if (i >= warmup)
                    {
                        times[i - warmup] = sw.Elapsed.TotalMilliseconds;
                        drawMs[i - warmup] = swd.Elapsed.TotalMilliseconds;
                        flushMs[i - warmup] = sw.Elapsed.TotalMilliseconds - swd.Elapsed.TotalMilliseconds;
                    }
                    if (i == warmup) quads = CrystalSpriteBatch.DPSCounter;
                }

                Array.Sort(times);
                Array.Sort(drawMs);
                Array.Sort(flushMs);
                double avg = 0; foreach (var t in times) avg += t; avg /= frames;
                double p50 = times[frames / 2];
                double p95 = times[(int)(frames * 0.95)];
                double fps = 1000.0 / avg;
                Console.WriteLine($"scene-perf: P50={p50:F2}ms P95={p95:F2}ms avg={avg:F2}ms FPS={fps:F1}");
                Console.WriteLine($"scene-perf: drawP50={drawMs[frames / 2]:F2}ms flushP50={flushMs[frames / 2]:F2}ms drawP95={drawMs[(int)(frames * 0.95)]:F2}ms flushP95={flushMs[(int)(frames * 0.95)]:F2}ms");
                Console.WriteLine($"scene-perf: flushCountPerFrame={quads}");
                Console.WriteLine($"scene-perf: meshRebuilds={CrystalSpriteBatch.MeshRebuildCount} (frames={frames})");
                Console.WriteLine(fps >= 60.0 && p95 <= 16.6 ? "scene-perf: G2-PASS (1080p ≥60FPS)" : "scene-perf: G2-FAIL (below 60FPS)");

                string fpsStr = fps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                string perfOut = Environment.GetEnvironmentVariable("CRYSTAL_PERF_OUT");
                if (!string.IsNullOrEmpty(perfOut))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(perfOut)));
                    File.WriteAllText(perfOut,
                        $"{{\"rt\":\"{rtW}x{rtH}\",\"frames\":{frames},\"avg_ms\":{avg:F2},\"p50_ms\":{p50:F2},\"p95_ms\":{p95:F2},\"fps\":{fpsStr}}}\n");
                }
                EditorApplication.Exit(fps >= 60.0 ? 0 : 1);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                foreach (var kv in _libs) kv.Value.UnloadAll();
                _libs.Clear();
                foreach (var kv in _mapLibs) kv.Value.UnloadAll();
                _mapLibs.Clear();
                CrystalSpriteBatch.ReleaseMeshes();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // 解析玩家对象（R9 复刻 PlayerObject.SetLibraries C 系列）：
        //   Body=CArmours[Armour]、Hair=CHair[Hair]、Weapon=CWeapons[Weapon]
        //   帧选择 DrawFrame=Frame.Start+OffSet*Dir+FrameIndex（OffSet=Count+Skip，FrameSet.Player 硬编码表）
        //   层叠 offset：男 0 / 女 808/808/416（PlayerObject.cs:586-588）
        // 验证代表帧 = Body 层（锚点层，DrawLocation+BodyLibrary.GetOffSet）；Hair/Weapon 层叠已在 R9 独立验证。
        static bool ResolvePlayer(Obj o, int cx, int cy, int offX, int offY)
        {
            string actionKey = o.Action switch
            {
                "Standing" => "Standing", "Walking" => "Walking", "Running" => "Running",
                "Attack1" => "Attack1", "Attack2" => "Attack2", "Attack3" => "Attack3",
                "Attack4" => "Attack4", "Spell" => "Spell", "Struck" => "Struck",
                "Die" => "Die", "Dead" => "Dead", "Revive" => "Revive",
                "Stance" => "Stance", "Stance2" => "Stance2", "Harvest" => "Harvest",
                "Mine" => "Mine", "Lunge" => "Lunge",
                _ => null,
            };
            if (actionKey == null)
            {
                Console.WriteLine($"  player: unsupported action {o.Action}");
                EditorApplication.Exit(2);
                return false;
            }
            var action = (MirAction)Enum.Parse(typeof(MirAction), actionKey);
            if (!FrameSet.Player.TryGetValue(action, out var frm))
            {
                Console.WriteLine($"  player: action {o.Action} not in FrameSet.Player");
                EditorApplication.Exit(2);
                return false;
            }
            var bodyLib = EnsureLib($"CArmour/{o.Armour:D2}");
            if (bodyLib == null)
            {
                Console.WriteLine($"  player: CArmour/{o.Armour:D2} missing");
                EditorApplication.Exit(2);
                return false;
            }
            var hLib = o.Hair >= 0 ? EnsureLib($"CHair/{o.Hair:D2}") : null;
            var wLib = o.Weapon >= 0 ? EnsureLib($"CWeapon/{o.Weapon:D2}") : null;

            int offSet = frm.Count + frm.Skip;
            int drawFrame = frm.Start + offSet * o.Dir + o.Frame;
            int armOff = o.Gender == 0 ? 0 : 808;
            int hairOff = o.Gender == 0 ? 0 : 808;
            int wepOff = o.Gender == 0 ? 0 : 416;

            o.Lib = bodyLib;
            o.Idx = drawFrame + armOff;
            o.DrawX = (o.X - cx + offX) * CellWidth;
            o.DrawY = (o.Y - cy + offY) * CellHeight;

            var bodyF = bodyLib.Frames[drawFrame + armOff];
            if (bodyF.Empty)
            {
                Console.WriteLine($"  player: body frame empty idx={o.Idx}");
                EditorApplication.Exit(2);
                return false;
            }
            o.F = bodyF;
            o.SpriteX = o.DrawX + bodyF.OffX;
            o.SpriteY = o.DrawY + bodyF.OffY;
            var bodyTex = bodyLib.GetPage(bodyF.Page);
            o.TW = bodyTex.width; o.TH = bodyTex.height;
            o.Src = bodyTex.GetPixels32();

            AddLayer(o, bodyLib, bodyF, o.DrawX, o.DrawY);
            if (hLib != null)
            {
                var hf = hLib.Frames[drawFrame + hairOff];
                if (!hf.Empty) AddLayer(o, hLib, hf, o.DrawX, o.DrawY);
            }
            if (wLib != null)
            {
                var wf = wLib.Frames[drawFrame + wepOff];
                if (!wf.Empty) AddLayer(o, wLib, wf, o.DrawX, o.DrawY);
            }
            Console.WriteLine($"  player: armour={o.Armour} hair={o.Hair} weapon={o.Weapon} gender={o.Gender} action={o.Action} dir={o.Dir} f={o.Frame} " +
                $"drawFrame={drawFrame} off=({armOff},{hairOff},{wepOff}) layers={o.Layers.Count} anchor body idx={o.Idx}");
            return true;
        }

        static void AddLayer(Obj o, AtlasLibrary lib, SpriteFrame f, int drawX, int drawY)
        {
            var tex = lib.GetPage(f.Page);
            o.Layers.Add(new ObjLayer
            {
                Lib = lib,
                F = f,
                SpriteX = drawX + f.OffX,
                SpriteY = drawY + f.OffY,
                TW = tex.width,
                TH = tex.height,
                Src = tex.GetPixels32(),
            });
        }

        // 对象在屏幕点 (sx,sy) 是否有任一层 bbox 覆盖（用于 presence 跳过被遮挡像素）
        static bool OccludesAt(Obj o, int sx, int sy)
        {
            if (o.IsPlayer)
            {
                foreach (var layer in o.Layers)
                    if (sx >= layer.SpriteX && sx < layer.SpriteX + layer.F.Width &&
                        sy >= layer.SpriteY && sy < layer.SpriteY + layer.F.Height) return true;
                return false;
            }
            return sx >= o.SpriteX && sx < o.SpriteX + o.F.Width && sy >= o.SpriteY && sy < o.SpriteY + o.F.Height;
        }

        // 对象在屏幕点 (sx,sy) 的最顶层渲染色；不透明返回 true（玩家逐层从顶往下找）
        static bool RenderColorAt(Obj o, int sx, int sy, out Color32 col)
        {
            col = default;
            if (o.IsPlayer)
            {
                for (int li = o.Layers.Count - 1; li >= 0; li--)
                {
                    var layer = o.Layers[li];
                    int lx = sx - layer.SpriteX, ly = sy - layer.SpriteY;
                    if (lx < 0 || lx >= layer.F.Width || ly < 0 || ly >= layer.F.Height) continue;
                    var c = layer.Src[(layer.TH - 1 - (layer.F.Y + ly)) * layer.TW + (layer.F.X + lx)];
                    if (c.a == 255) { col = c; return true; }
                }
                return false;
            }
            int lxn = sx - o.SpriteX, lyn = sy - o.SpriteY;
            if (lxn < 0 || lxn >= o.F.Width || lyn < 0 || lyn >= o.F.Height) return false;
            var s = o.Src[(o.TH - 1 - (o.F.Y + lyn)) * o.TW + (o.F.X + lxn)];
            if (s.a != 255) return false;
            col = s;
            return true;
        }

        // 解析对象的 FrameEntry/帧/锚点并缓存源像素
        static bool ResolveObject(Obj o, int cx, int cy, int offX, int offY)
        {
            o.Lib = EnsureLib(o.Rel);
            if (o.Lib == null)
            {
                Console.WriteLine($"  obj: manifest missing {o.Rel}");
                EditorApplication.Exit(2);
                return false;
            }
            o.Fe = null;
            foreach (var e in o.Lib.Manifest.Frames)
                if (e.Action == o.Action) { o.Fe = e; break; }
            if (o.Fe == null)
            {
                Console.WriteLine($"  obj: action {o.Action} not in {o.Rel} FrameSet");
                EditorApplication.Exit(2);
                return false;
            }
            o.Idx = o.Fe.Start + (o.Fe.Count + o.Fe.Skip) * o.Dir + o.Frame;
            if (o.Idx < 0 || o.Idx >= o.Lib.Frames.Length)
            {
                Console.WriteLine($"  obj: idx {o.Idx} out of range [{o.Lib.Frames.Length}]");
                EditorApplication.Exit(2);
                return false;
            }
            o.F = o.Lib.Frames[o.Idx];
            if (o.F.Empty)
            {
                Console.WriteLine($"  obj: frame empty");
                EditorApplication.Exit(2);
                return false;
            }
            // 对象锚点：无 -OffSetX（MonsterObject.cs:435）
            o.DrawX = (o.X - cx + offX) * CellWidth;
            o.DrawY = (o.Y - cy + offY) * CellHeight;
            o.SpriteX = o.DrawX + o.F.OffX;
            o.SpriteY = o.DrawY + o.F.OffY;
            var tex = o.Lib.GetPage(o.F.Page);
            o.TW = tex.width; o.TH = tex.height;
            o.Src = tex.GetPixels32();
            return true;
        }

        // ①锚点钉死：扫描精灵局部 (lx,ly) 首个不透明像素，RT 屏上对应点须 == 图集源。
        // 跳过：后续对象 bbox（later objs）+ 玩家对象自身更上层（hair/weapon 覆盖 body）。
        static int VerifyPresence(Obj o, List<Obj> occluders, Color32[] px, int rtW, int rtH)
        {
            int fail = 0;
            for (int ly = 0; ly < o.F.Height && fail == 0; ly++)
                for (int lx = 0; lx < o.F.Width && fail == 0; lx++)
                {
                    var src = o.Src[(o.TH - 1 - (o.F.Y + ly)) * o.TW + (o.F.X + lx)];
                    if (src.a != 255) continue;
                    int sx = o.SpriteX + lx, sy = o.SpriteY + ly;
                    if (sx < 0 || sx >= rtW || sy < 0 || sy >= rtH) continue;
                    bool occluded = false;
                    // 玩家自身更上层（同对象后画层）可遮挡 body
                    if (o.IsPlayer)
                    {
                        foreach (var layer in o.Layers)
                        {
                            if (layer == o.Layers[0]) continue;
                            if (sx >= layer.SpriteX && sx < layer.SpriteX + layer.F.Width &&
                                sy >= layer.SpriteY && sy < layer.SpriteY + layer.F.Height) { occluded = true; break; }
                        }
                    }
                    if (!occluded)
                        foreach (var c in occluders)
                            if (OccludesAt(c, sx, sy)) { occluded = true; break; }
                    if (occluded) continue;
                    var got = px[sy * rtW + sx];
                    if (got.r != src.r || got.g != src.g || got.b != src.b || got.a != src.a)
                    {
                        Console.WriteLine($"  presence diff local({lx},{ly}) screen({sx},{sy}) src({src.r:X2}{src.g:X2}{src.b:X2}{src.a:X2}) got({got.r:X2}{got.g:X2}{got.b:X2}{got.a:X2})");
                        fail++;
                    }
                }
            return fail;
        }

        // ②y-sort 遮挡：far 先画（小Y）、near 后画（大Y）。重叠区须显示 near 色（near 在上）；
        // far 独占区须仍为 far 色（far 未被覆盖）。两者重叠像素取两源都不透明且颜色不同者。
        static int VerifyOcclusion(Obj far, Obj near, Color32[] px, int rtW, int rtH)
        {
            // 统一用代表帧 bbox 求交集（玩家代表帧=Body，为最大层）
            int x0 = Math.Max(far.SpriteX, near.SpriteX), x1 = Math.Min(far.SpriteX + far.F.Width, near.SpriteX + near.F.Width);
            int y0 = Math.Max(far.SpriteY, near.SpriteY), y1 = Math.Min(far.SpriteY + far.F.Height, near.SpriteY + near.F.Height);
            if (x0 >= x1 || y0 >= y1)
            {
                Console.WriteLine($"  overlap none bbox far=[{far.SpriteX},{far.SpriteX + far.F.Width - 1}]x[{far.SpriteY},{far.SpriteY + far.F.Height - 1}] near=[{near.SpriteX},{near.SpriteX + near.F.Width - 1}]x[{near.SpriteY},{near.SpriteY + near.F.Height - 1}]");
                return 0;
            }
            int fail = 0;
            // 重叠区：near 色须覆盖（玩家 near 用 RenderColorAt 取顶层色）
            bool overlapOk = false;
            for (int sy = y0; sy < y1 && !overlapOk; sy++)
                for (int sx = x0; sx < x1 && !overlapOk; sx++)
                {
                    if (!RenderColorAt(near, sx, sy, out var sN)) continue;
                    if (!RenderColorAt(far, sx, sy, out var sF)) continue;
                    if (sN.r == sF.r && sN.g == sF.g && sN.b == sF.b) continue;
                    var got = px[sy * rtW + sx];
                    if (got.r != sN.r || got.g != sN.g || got.b != sN.b)
                    {
                        Console.WriteLine($"  overlap diff screen({sx},{sy}) near({sN.r:X2}{sN.g:X2}{sN.b:X2}) got({got.r:X2}{got.g:X2}{got.b:X2})");
                        fail++;
                    }
                    else overlapOk = true;
                }
            if (!overlapOk) Console.WriteLine("  overlap: no distinguishable opaque intersection pixel");

            // far 独占区：仍在（跳过 near 任何层 bbox）
            // 玩家 far 的实际渲染色 = 顶层自身层色（RenderColorAt），非代表帧 Body 色
            // （far 自身 hair/weapon 层可覆盖 Body 像素，R10 批量验证实证 false fail 根因）。
            bool farOk = false;
            for (int ly = 0; ly < far.F.Height && !farOk; ly++)
                for (int lx = 0; lx < far.F.Width && !farOk; lx++)
                {
                    int sx = far.SpriteX + lx, sy = far.SpriteY + ly;
                    if (sx < 0 || sx >= rtW || sy < 0 || sy >= rtH) continue;
                    if (OccludesAt(near, sx, sy)) continue;
                    if (!RenderColorAt(far, sx, sy, out var sF)) continue;
                    if (sF.a != 255) continue;
                    var got = px[sy * rtW + sx];
                    if (got.r != sF.r || got.g != sF.g || got.b != sF.b)
                    {
                        Console.WriteLine($"  far-only diff screen({sx},{sy}) far({sF.r:X2}{sF.g:X2}{sF.b:X2}) got({got.r:X2}{got.g:X2}{got.b:X2})");
                        fail++;
                    }
                    else farOk = true;
                }
            if (!farOk) Console.WriteLine("  far-only: no opaque exclusive pixel");
            return fail;
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
                string rel = MapRender.MapLibRel(libIndex);
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

        internal static AtlasLibrary EnsureLib(string rel)
        {
            if (_libs.TryGetValue(rel, out var lib)) return lib;
            string man = Path.Combine(_atlasDir, rel + ".json");
            if (!File.Exists(man))
            {
                Console.WriteLine($"  scene-render: WARN manifest missing {man}");
                return null;
            }
            lib = AtlasLibrary.Load(man);
            _libs[rel] = lib;
            return lib;
        }

        internal static AtlasLibrary EnsureMapLib(string rel)
        {
            if (_mapLibs.TryGetValue(rel, out var lib)) return lib;
            string man = Path.Combine(_mapAtlasDir, rel + ".json");
            if (!File.Exists(man))
            {
                Console.WriteLine($"  scene-render: WARN map manifest missing {man}");
                return null;
            }
            lib = AtlasLibrary.Load(man);
            _mapLibs[rel] = lib;
            return lib;
        }

        // 预构建 libIndex→AtlasLibrary 数组（避免 DrawTile 热路径重复 MapLibRel 字符串拼接 + 字符串字典查找）。
        internal static AtlasLibrary[] BuildLibIndex(int libCount, CellInfo[,] cells, int w, int h)
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

        internal static AtlasLibrary MapLibRelLazy(int libIndex)
        {
            string rel = MapRender.MapLibRel(libIndex);
            return rel == null ? null : EnsureMapLib(rel);
        }

        static int GetInt(string name, int def)
        {
            string s = Environment.GetEnvironmentVariable(name);
            return int.TryParse(s, out int v) ? v : def;
        }
    }
}
