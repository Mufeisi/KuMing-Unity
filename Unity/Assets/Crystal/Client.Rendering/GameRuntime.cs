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
        public static int LastObjectDraws; // 探针断言：本帧实际绘制对象数
        public static int FramesRendered;  // 渲染完成帧数（render-ready 判据：首帧完成即渲染就绪）

        // 探针路径：逻辑 + 渲染到显式 RT（batchmode 无相机，不存在相机清屏覆盖问题）。
        public static void Tick(RenderTexture target)
        {
            TickLogic();
            Render(target);
        }

        // Player 壳路径：每帧逻辑推进（Update 调）；渲染走 RenderScreen（OnPostRender，相机渲染后才轮到，否则被相机清屏覆盖）。
        public static void TickLogic()
        {
            if (GameSession.State == GameSessionState.Idle || GameSession.State == GameSessionState.Error) return;
            CMain.Time = _clock.ElapsedMilliseconds;
            GameSession.Process();
            if (GameSession.User == null || GameSession.MapReader == null) return;

            // OffSet 为屏幕尺寸派生常量，先于对象 Process 设置（Monster/NPC DrawLocation 依赖 OffSetX/Y）。
            MapControl.OffSetX = ScreenW / 2 / MapControl.CellWidth;
            MapControl.OffSetY = ScreenH / 2 / MapControl.CellHeight - 1;
            ProcessObjects();
        }

        // Player 壳路径：屏幕渲染（相机 OnPostRender 调用，target=null 直渲后缓冲）。
        public static void RenderScreen()
        {
            if (GameSession.User == null || GameSession.MapReader == null) return;
            Render(null);
        }

        static void ProcessObjects()
        {
            foreach (var o in MapControl.ObjectsList)
                if (o != MapObject.User) o.Process();
        }

        static void Render(RenderTexture target)
        {
            var user = GameSession.User;
            int cx = user.Movement.X, cy = user.Movement.Y;
            int offX = MapControl.OffSetX, offY = MapControl.OffSetY;
            int rangeX = offX + 6, rangeY = offY + 6;

            var cells = GameSession.MapReader.MapCells;
            var libByIndex = GetLibIndex(cells);

            CrystalSpriteBatch.Begin(target, ScreenW, ScreenH);
            CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
            GameRenderer.DrawMapTiles(cells, GameSession.MapReader, cx, cy, offX, offY, rangeX, rangeY, libByIndex);

            int drawn = 0;
            foreach (var o in MapControl.ObjectsList.OrderBy(o => o.MapLocation.Y).ThenBy(o => o.MapLocation.X))
            {
                if (o == user || o.Dead) continue;
                var lib = o.BodyLibrary as MLibraryUnity;
                if (lib == null) continue;
                lib.DrawIndex(o.DrawFrame, o.DrawLocation, o.DrawColour, true, 1f);
                drawn++;
            }
            LastObjectDraws = drawn;
            CrystalSpriteBatch.End();
            FramesRendered++;
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
