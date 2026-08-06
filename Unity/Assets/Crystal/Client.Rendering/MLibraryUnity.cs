using System;
using System.Collections.Generic;
using Client.MirObjects;
using Crystal.Client.Assets;
using Crystal.Client.Rendering;
using UnityEngine;
// Client.Core 的 Point/Color 是 MirMath 类型（Ported 文件 via using Crystal.Client.Core.MirMath）；
// Client.Rendering 无全局别名，显式引入避免与 UnityEngine.Color 歧义。
using MPoint = Crystal.Client.Core.MirMath.Point;
using MColor = Crystal.Client.Core.MirMath.Color;
using MRect = Crystal.Client.Core.MirMath.Rectangle;
using MSize = Crystal.Client.Core.MirMath.Size;

namespace Client.MirGraphics
{
    // renderable MLibrary：把 Client.Core 的 MLibrary seam 调用接驳到 AtlasLibrary + CrystalSpriteBatch。
    // 不触碰 Client.Core；继承 seam（未密封、Draw/DrawBlend virtual）。
    // 注意：seam 的 GetOffSet/GetSize/GetTrueSize/VisiblePixel 已改 virtual（C# 派发到派生类），
    // 尺寸/偏移/像素命中均由 Atlas.Frames 驱动（HUD 布局 + PixelDetect 控件命中）。
    // seam 的 4 参 Draw(int,Point,Color,bool) 也非 virtual → 探针显式调用 DrawIndex 渲染内核；
    // 生产接入时把 seam 4 参改 virtual 并在子类覆写即可无缝。
    public class MLibraryUnity : MLibrary
    {
        public AtlasLibrary Atlas;

        // 页面像素缓存：VisiblePixel 命中检测逐帧读像素，缓存页数组避免每次 GetPixels32（图集只读）。
        readonly Dictionary<Texture2D, Color32[]> _pagePx = new Dictionary<Texture2D, Color32[]>();

        public MLibraryUnity(string fileName) : base(fileName) { }

        // 尺寸/偏移查询覆写：seam 的 GetSize/GetTrueSize/GetOffSet 已改 virtual（HUD 控件布局依赖），
        // 由 Atlas.Frames 驱动真实图元尺寸/锚点偏移（空帧/越界回退 0）。
        public override MSize GetSize(int index)
        {
            return FrameSize(index);
        }
        public override MSize GetTrueSize(int index)
        {
            return FrameSize(index);
        }
        public override MPoint GetOffSet(int index)
        {
            if (Atlas == null || index < 0 || index >= Atlas.Frames.Length) return new MPoint(0, 0);
            var f = Atlas.Frames[index];
            return new MPoint(f.OffX, f.OffY);
        }

        MSize FrameSize(int index)
        {
            if (Atlas == null || index < 0 || index >= Atlas.Frames.Length) return MSize.Empty;
            var f = Atlas.Frames[index];
            if (f.Empty) return MSize.Empty;
            return new MSize(f.Width, f.Height);
        }

        // 像素级命中（MirImageControl.PixelDetect=true 的控件，如 MainDialog）：location 为帧内相对坐标，
        // useOffSet=true 时叠加锚点偏移。帧在图集页内 (f.X,f.Y) 为 PNG 顶左坐标，Unity 纹理 row0=底，
        // 故像素行需翻转：texRow = tex.height - (f.Y + y) - 1。alpha>0 即命中（旧客户端同语义）。
        public override bool VisiblePixel(int index, MPoint location, bool useOffSet)
        {
            if (Atlas == null || index < 0 || index >= Atlas.Frames.Length) return false;
            var f = Atlas.Frames[index];
            if (f.Empty || f.Width <= 0 || f.Height <= 0) return false;
            var tex = Atlas.GetPage(f.Page);
            if (tex == null) return false;
            int x = location.X + (useOffSet ? f.OffX : 0);
            int y = location.Y + (useOffSet ? f.OffY : 0);
            if (x < 0 || y < 0 || x >= f.Width || y >= f.Height) return false;
            var px = GetPagePx(tex);
            int texRow = tex.height - (f.Y + y) - 1;
            int texCol = f.X + x;
            return px[texRow * tex.width + texCol].a > 0;
        }

        Color32[] GetPagePx(Texture2D tex)
        {
            if (_pagePx.TryGetValue(tex, out var px)) return px;
            px = tex.GetPixels32();
            _pagePx[tex] = px;
            return px;
        }

        // 渲染内核：所有 4 参/5 参 Draw/DrawBlend 都进这里。offSet=true 时用 Frame.OffX/OffY 锚点偏移。
        // 顶点 v 轴方向：AtlasLibrary.LoadTexture 的图集是 Unity 纹理（row0=底，点过滤），CrystalSpriteBatch
        // quad UV 顶边采样 v=1（=PNG 顶行），与旧客户端 .Lib 行 0=图顶一致（R1 实证），直接按 UV 绘制即可。
        public void DrawIndex(int index, MPoint point, MColor colour, bool offSet, float opacity)
        {
            if (Atlas == null || index < 0 || index >= Atlas.Frames.Length) return;
            var f = Atlas.Frames[index];
            if (f.Empty) return;
            var tex = Atlas.GetPage(f.Page);
            if (tex == null) return;
            int sx = point.X + (offSet ? f.OffX : 0);
            int sy = point.Y + (offSet ? f.OffY : 0);
            float o = opacity < 0 ? 1f : opacity;
            // 顶点色只携带图元色（含其自身 alpha），opacity 单次应用：
            // DrawOpaque → DrawInternal → quad.color.a = color.a * opacity（旧 DXManager.DrawOpaque(Color4, opacity) 语义）。
            // 修复前 opacity 同时写进 color.a 与 DrawOpaque 参数 → 双重应用（实际 alpha=o²），
            // 天气 Fog（BlendRate=0.4）实测 got≈o²·src 暴露；BlendVerify 走 Draw+SetOpacity 单次路径未覆盖此分支。
            var c = new UnityEngine.Color(colour.R / 255f, colour.G / 255f, colour.B / 255f, colour.A / 255f);
            var rect = new Rect(f.X, f.Y, f.Width, f.Height);
            if (colour.A < 255 || o < 1f)
                CrystalSpriteBatch.DrawOpaque(tex, rect, new Vector3(sx, sy, 0f), c, o);
            else
                CrystalSpriteBatch.Draw(tex, rect, new Vector3(sx, sy, 0f), c);
        }

        public override void Draw(int index, MPoint point, MColor colour, bool offSet, float opacity)
            => DrawIndex(index, point, colour, offSet, opacity);

        public override void DrawBlend(int index, MPoint point, MColor colour, bool offSet, float rate)
            => DrawIndex(index, point, colour, offSet, rate);

        // source-rect 裁剪绘制（HUD orb/exp 条，MainDialog.BeforeDraw 走此路径）：
        // section 为源图坐标系内的裁剪矩形，映射到图集页 (f.X+section.X, f.Y+section.Y, W, H)，
        // 在 point（旧客户端已算好的裁剪区左上角）处整幅绘制。offSet=true 时叠加图元锚点偏移。
        public override void Draw(int index, MRect section, MPoint point, MColor colour, bool offSet)
            => DrawSection(index, section, point, colour, offSet, 1f);

        public override void Draw(int index, MRect section, MPoint point, MColor colour, float opacity)
            => DrawSection(index, section, point, colour, false, opacity);

        void DrawSection(int index, MRect section, MPoint point, MColor colour, bool offSet, float opacity)
        {
            if (Atlas == null || index < 0 || index >= Atlas.Frames.Length) return;
            var f = Atlas.Frames[index];
            if (f.Empty || section.Width <= 0 || section.Height <= 0) return;
            var tex = Atlas.GetPage(f.Page);
            if (tex == null) return;
            int sx = point.X + (offSet ? f.OffX : 0);
            int sy = point.Y + (offSet ? f.OffY : 0);
            float o = opacity < 0 ? 1f : opacity;
            var c = new UnityEngine.Color(colour.R / 255f, colour.G / 255f, colour.B / 255f, (colour.A / 255f) * o);
            var rect = new Rect(f.X + section.X, f.Y + section.Y, section.Width, section.Height);
            if (colour.A < 255 || o < 1f)
                CrystalSpriteBatch.DrawOpaque(tex, rect, new Vector3(sx, sy, 0f), c, o);
            else
                CrystalSpriteBatch.Draw(tex, rect, new Vector3(sx, sy, 0f), c);
        }

        // FrameEntry(ActionId==MirAction 数值，实证 Monster/000：Standing=0, Walking=1, Attack1=9, Struck=18)
        // → FrameSet（Dictionary<MirAction,Frame>）。只对怪物（玩家用 FrameSet.Player 硬编码表不改）。
        public static FrameSet BridgeFrames(LibManifest m)
        {
            var set = new FrameSet();
            if (m.Frames == null) return set;
            foreach (var e in m.Frames)
            {
                if (e.ActionId < 0 || e.ActionId > 49) continue;
                var a = (MirAction)e.ActionId;
                if (!Enum.IsDefined(typeof(MirAction), a)) continue;
                set[a] = new Frame(e.Start, e.Count, e.Skip, e.Interval,
                                   e.EffectStart, e.EffectCount, e.EffectSkip, e.EffectInterval)
                { Reverse = e.Reverse, Blend = e.Blend };
            }
            return set;
        }
    }
}
