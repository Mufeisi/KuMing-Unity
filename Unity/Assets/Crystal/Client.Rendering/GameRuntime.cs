using System.Diagnostics;
using System.Linq;
using Client;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirScenes;
using Crystal.Client.Assets;
using UnityEngine;

namespace Crystal.Client.Rendering
{
    // PC Player 运行时主循环（C3）：CMain.Time 真实时钟 + GameSession 网络推进 + 对象 Process + 屏幕/RT 渲染。
    // 可测试性：静态 Tick 供 batchmode 探针直接调用（渲染目标=RT）；GameBootstrap 每帧调用（渲染目标=null 屏幕）。
    // 对象渲染沿用 NetProbe 实证路径：跳过 User（裸 UserObject 无装备图集），其余对象 y-sort + BodyLibrary.DrawIndex。
    public static class GameRuntime
    {
        static readonly Stopwatch _clock = Stopwatch.StartNew();
        static string _libCacheMap;      // BuildLibIndex 跨帧缓存键（当前地图文件，G2 性能教训：静态场景每帧零重建）
        static AtlasLibrary[] _libCache; // 当前地图 libIndex→AtlasLibrary 数组

        public static int ScreenW = 1280, ScreenH = 720;
        // 8-10 性能分级：RenderScale=内部渲染分辨率缩放（Begin 传缩放后尺寸 → EmitQuad 归一化 NDC 放大）；
        // DrawDistanceScale=远处对象更新/绘制距离系数（按视野半径缩放过滤，减 CPU 与 draw call）。
        public static float RenderScale = 1f;
        public static float DrawDistanceScale = 1f;
        public static int LastObjectDraws; // 探针断言：本帧实际绘制对象数
        public static int FramesRendered;  // 渲染完成帧数（render-ready 判据：首帧完成即渲染就绪）
        // 9-4 性能剖析：TickLogic/Render 每帧耗时累计（GameBootstrap.MaybeFpsLog 每 5s 输出均值）。
        static double _logicMs, _renderMs, _sessionMs, _objectsMs;
        static int _perfFrames;
        public static double AvgLogicMs => _perfFrames > 0 ? _logicMs / _perfFrames : 0;
        public static double AvgRenderMs => _perfFrames > 0 ? _renderMs / _perfFrames : 0;
        public static double AvgSessionMs => _perfFrames > 0 ? _sessionMs / _perfFrames : 0;
        public static double AvgObjectsMs => _perfFrames > 0 ? _objectsMs / _perfFrames : 0;

        // 探针路径：逻辑 + 渲染到显式 RT（batchmode 无相机，不存在相机清屏覆盖问题）。
        public static void Tick(RenderTexture target)
        {
            TickLogic();
            Render(target);
        }

        // Player 壳路径：每帧逻辑推进（Update 调）；渲染走 RenderScreen（OnPostRender，相机渲染后才轮到，否则被相机清屏覆盖）。
        public static void TickLogic()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (GameSession.State == GameSessionState.Idle || GameSession.State == GameSessionState.Error) return;
            CMain.Time = _clock.ElapsedMilliseconds;
            GameSession.Process();
            double sessionMs = sw.Elapsed.TotalMilliseconds;
            if (GameSession.User == null || GameSession.MapReader == null) return;

            // OffSet 为屏幕尺寸派生常量，先于对象 Process 设置（Monster/NPC DrawLocation 依赖 OffSetX/Y）。
            MapControl.OffSetX = ScreenW / 2 / MapControl.CellWidth;
            MapControl.OffSetY = ScreenH / 2 / MapControl.CellHeight - 1;
            ProcessObjects();
            _sessionMs += sessionMs;
            _objectsMs += sw.Elapsed.TotalMilliseconds - sessionMs;
            _logicMs += sw.Elapsed.TotalMilliseconds;
            _perfFrames++;
        }

        // Player 壳路径：屏幕渲染（相机 OnPostRender 调用，target=null 直渲后缓冲）。
        public static void RenderScreen()
        {
            if (GameSession.User == null || GameSession.MapReader == null) return;
            Render(null);
        }

        static void ProcessObjects()
        {
            int visX = MapControl.OffSetX + 6, visY = MapControl.OffSetY + 6;
            foreach (var o in MapControl.ObjectsList)
                if (o != MapObject.User && InDrawRange(o, visX, visY)) o.Process();
        }

        // 8-10 距离裁剪：DrawDistanceScale<1 时按视野半径缩放，远处对象跳过 Process/Draw（CPU+draw call 双减）。
        static bool InDrawRange(MapObject o, int visX, int visY)
        {
            if (DrawDistanceScale >= 0.999f) return true;
            var u = GameSession.User;
            int dx = o.MapLocation.X - u.Movement.X, dy = o.MapLocation.Y - u.Movement.Y;
            int limitX = Mathf.Max(4, (int)(visX * DrawDistanceScale));
            int limitY = Mathf.Max(4, (int)(visY * DrawDistanceScale));
            return Mathf.Abs(dx) <= limitX && Mathf.Abs(dy) <= limitY;
        }

        static void Render(RenderTexture target)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var user = GameSession.User;
            int cx = user.Movement.X, cy = user.Movement.Y;
            int offX = MapControl.OffSetX, offY = MapControl.OffSetY;
            int rangeX = offX + 6, rangeY = offY + 6;

            var cells = GameSession.MapReader.MapCells;
            var libByIndex = GetLibIndex(cells);

            CrystalSpriteBatch.Begin(target, (int)(ScreenW * RenderScale), (int)(ScreenH * RenderScale));
            CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
            GameRenderer.DrawMapTiles(cells, GameSession.MapReader, cx, cy, offX, offY, rangeX, rangeY, libByIndex);

            int drawn = 0;
            int visX = offX + 6, visY = offY + 6;
            foreach (var o in MapControl.ObjectsList.OrderBy(o => o.MapLocation.Y).ThenBy(o => o.MapLocation.X))
            {
                if (o == user || o.Dead) continue;
                if (!InDrawRange(o, visX, visY)) continue; // 8-10 远处对象不绘制
                var lib = o.BodyLibrary as MLibraryUnity;
                if (lib == null) continue;
                lib.DrawIndex(o.DrawFrame, o.DrawLocation, o.DrawColour, true, 1f);
                drawn++;
            }
            LastObjectDraws = drawn;
            CrystalSpriteBatch.End();
            FramesRendered++;
            _renderMs += sw.Elapsed.TotalMilliseconds;
        }

        // 跨帧缓存 libIndex 数组：同地图 cells 不变，避免每帧重建（G2 性能教训）。
        static AtlasLibrary[] GetLibIndex(CellInfo[,] cells)
        {
            if (_libCacheMap != GameSession.MapFileName || _libCache == null)
            {
                _libCache = GameRenderer.BuildLibIndex(0, cells, GameSession.MapReader.Width, GameSession.MapReader.Height);
                _libCacheMap = GameSession.MapFileName;
            }
            return _libCache;
        }

        public static void ReleaseAll()
        {
            _libCache = null;
            _libCacheMap = null;
            GameRenderer.ReleaseAll();
        }
    }
}
