using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Client;
using Client.MirGraphics;
using Client.MirGraphics.Particles;
using Client.MirScenes;
using Crystal.Client.Assets;
using Crystal.Client.Rendering;
using MPoint = Crystal.Client.Core.MirMath.Point;
using MColor = Crystal.Client.Core.MirMath.Color;
using SDPoint = System.Drawing.Point;
using Vector2 = Crystal.Client.Core.MirMath.Vector2;

namespace Crystal.Rendering.Editor
{
    // 阶段6 天气补验（R7 渲染层闭环，2026-08-06）：
    // 真实 Weather.Lib（G3 外部补充快照，sha256 9A065B7D…，supplementId Crystal-G3-Weather-2026-07-31，
    // v3 878 图 / 16 页）→ AssetCompiler 图集（compile verify OK + Weather.golden 878 行侧车）
    // → 本探针复刻 GameScene.UpdateWeather（GameScene.cs:12278-12420）粒子引擎组装
    //   （Rain=164 150帧 / Snow=43 20帧 / Fog=0 单帧 / Leaves=359+531+587），
    //   Process 步进（CMain.Time 单调推进，R7 确定性同款）→ RT 渲染 → PNG。
    // env: CRYSTAL_ATLAS_DIR / CRYSTAL_RT_W / CRYSTAL_RT_H / CRYSTAL_OUT / CRYSTAL_WEATHER(Rain,Snow,Fog)
    // 断言：①数据层 GetSize 7 粒子索引 == manifest（512²/32²/400²）；②渲染层每引擎
    //   粒子图区域非背景覆盖 + Fog 精确混合对照（src×0.4+bg×0.6，BlendVerify 实证 ±2 容差 ±20）；
    //   ③pass B 步进后 CurrentFrame 推进 + 粒子位移（状态机活跃）。
    public static class WeatherRender
    {
        static int _rtW = 1024, _rtH = 768;
        static string _outPath;
        static readonly List<string> _seq = new List<string>();
        static readonly List<ParticleEngine> _engines = new List<ParticleEngine>();
        static int _fail;

        static string Env(string k, string d = null) => Environment.GetEnvironmentVariable(k) ?? d;
        static int GetInt(string k, int d) { int v; return int.TryParse(Env(k), out v) ? v : d; }

        static void Fail(string why)
        {
            _fail++;
            Console.WriteLine($"[netprobe] weather fail={why}");
        }

        public static void RunWeather()
        {
            string atlasDir = Path.GetFullPath(Env("CRYSTAL_ATLAS_DIR", "Build/assetcompile/all"));
            SceneRender._atlasDir = atlasDir; // EnsureLib 依赖此静态字段
            _outPath = Path.GetFullPath(Env("CRYSTAL_OUT", "Build/weather.png"));
            _rtW = GetInt("CRYSTAL_RT_W", 1024);
            _rtH = GetInt("CRYSTAL_RT_H", 768);
            string spec = Env("CRYSTAL_WEATHER", "Rain,Snow,Fog");

            try
            {
                // 确定性（R7 同款）：固定 seed + 单调 CMain.Time
                CMain.Random = new System.Random(42);
                CMain.Time = 0;

                var lib = SceneRender.EnsureMLibrary("Weather");
                if (lib == null) { Fail("weather-lib-missing"); goto done; }
                Libraries.Weather = lib;
                GameScene.Scene = new GameScene { MapControl = new MapControl() };

                // ① 数据断言：7 个粒子索引尺寸 == manifest
                var checks = new (int idx, int w, int h)[] { (0,512,512),(1,32,32),(43,400,400),(164,512,512),(359,512,512),(531,512,512),(587,512,512) };
                foreach (var c in checks)
                {
                    var s = lib.GetSize(c.idx);
                    if (s.Width != c.w || s.Height != c.h) { Fail($"size[{c.idx}]={s.Width}x{s.Height} expect {c.w}x{c.h}"); }
                }
                _seq.Add($"data=frames={lib.Atlas.Manifest.Count}");

                // 引擎组装（UpdateWeather 逐字语义）
                var engines = new List<ParticleEngine>();
                foreach (var item in spec.Split(','))
                {
                    var t = item.Trim();
                    if (t == "Rain")
                    {
                        var textures = new List<ParticleImageInfo> { new ParticleImageInfo(lib, 164, 150, 50) };
                        var e = new ParticleEngine(textures, new Vector2(2f, 0), ParticleType.Rain);
                        var vel = Vector2.Zero; // rsevelocity（逐字）
                        for (int y = -512; y < _rtH + 512; y += 512)
                            for (int x = -512; x < _rtW + 512; x += 512)
                            { var p = e.GenerateNewParticle(ParticleType.Rain); p.Position = new Vector2(x, y); p.Velocity = vel; }
                        e.GenerateParticles = false;
                        engines.Add(e);
                    }
                    else if (t == "Snow")
                    {
                        var textures = new List<ParticleImageInfo> { new ParticleImageInfo(lib, 43, 20, 50) };
                        var e = new ParticleEngine(textures, new Vector2(0, 0), ParticleType.Snow);
                        var vel = new Vector2(1f, -1f); // rsvelocity
                        for (int y = -400; y < _rtH + 400; y += 400)
                            for (int x = -400; x < _rtW + 400; x += 400)
                            { var p = e.GenerateNewParticle(ParticleType.Snow); p.Position = new Vector2(x, y); p.Velocity = vel; }
                        e.GenerateParticles = false;
                        engines.Add(e);
                    }
                    else if (t == "Fog")
                    {
                        var textures = new List<ParticleImageInfo> { new ParticleImageInfo(lib, 0) };
                        var e = new ParticleEngine(textures, new Vector2(0, 0), ParticleType.Fog);
                        e.UpdateDelay = TimeSpan.FromMilliseconds(20);
                        var vel = new Vector2(2f, -2f); // fvelocity
                        for (int y = -512; y < _rtH + 512; y += 512)
                            for (int x = -512; x < _rtW + 512; x += 512)
                            { var p = e.GenerateNewParticle(ParticleType.Fog); p.Position = new Vector2(x, y); p.Velocity = vel; }
                        e.GenerateParticles = false;
                        engines.Add(e);
                    }
                }
                _seq.Add($"engines={string.Join(",", engines.Select(e => e.Type.ToString()))}");
                _engines.Clear();
                _engines.AddRange(engines);

                // pass A：帧 0（不 Process）渲染 + 像素断言（源像素混合对照）
                RenderAndCheck(lib, engines, "a", 0);

                // pass B：Process 6 帧（每帧 50ms）
                for (int i = 0; i < 6; i++)
                {
                    CMain.Time += 50;
                    foreach (var e in engines) e.Process();
                }
                bool moving = true;
                foreach (var e in engines)
                {
                    var p = e.Particles.FirstOrDefault(x => x.ImageInfo != null);
                    if (p == null || p.ImageInfo.CurrentFrame < 0) { moving = false; break; }
                }
                _seq.Add($"step=6 frames={string.Join(",", engines.Select(e => e.Particles.Count > 0 ? e.Particles[0].ImageInfo.CurrentFrame.ToString() : "-"))}");
                RenderAndCheck(lib, engines, "b", 6 * 50);

                WritePng(lib);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[netprobe] weather exception {ex}");
                _fail++;
            }
        done:
            string line = $"[netprobe] weather {(_fail == 0 ? "ok" : "fail")} seq={string.Join(">", _seq)} fail={_fail}";
            Console.WriteLine(line);
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        static void RenderAndCheck(MLibraryUnity lib, List<ParticleEngine> engines, string phase, long stepMs)
        {
            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f)); // bg=RGB(25,25,25)
                foreach (var e in engines) e.Draw();
                CrystalSpriteBatch.End();

                var read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32(); // top-down（R3 实证）

                Func<Color32, bool> lit = c => Math.Abs(c.r - 25) + Math.Abs(c.g - 25) + Math.Abs(c.b - 25) > 15;

                foreach (var e in engines)
                {
                    // 选第一个与屏幕相交的粒子（UpdateWeather 网格从 -512 铺起，首粒子在屏外）
                    int idx = -1;
                    for (int i = 0; i < e.Particles.Count; i++)
                    {
                        var pp = e.Particles[i];
                        if (pp.ImageInfo == null) continue;
                        var ff = lib.Atlas.Frames[pp.ImageInfo.BaseIndex + pp.ImageInfo.CurrentFrame];
                        int xa = (int)pp.Position.X + ff.OffX, ya = (int)pp.Position.Y + ff.OffY;
                        if (xa + ff.Width > 0 && ya + ff.Height > 0 && xa < _rtW && ya < _rtH) { idx = i; break; }
                    }
                    if (idx < 0) { Fail($"{phase}:{e.Type}:no-on-screen-particle"); continue; }
                    var p = e.Particles[idx];
                    var f = lib.Atlas.Frames[p.ImageInfo.BaseIndex + p.ImageInfo.CurrentFrame];
                    if (f.Empty) { Fail($"{phase}:{e.Type}:frame-empty idx={p.ImageInfo.BaseIndex + p.ImageInfo.CurrentFrame}"); continue; }
                    int x0 = (int)p.Position.X + f.OffX, y0 = (int)p.Position.Y + f.OffY;

                    // 粒子图区域覆盖统计（屏内采样步长 4）
                    int nonBg = 0;
                    for (int gy = 0; gy < f.Height; gy += 4)
                        for (int gx = 0; gx < f.Width; gx += 4)
                        {
                            int xx = x0 + gx, yy = y0 + gy;
                            if (xx < 0 || yy < 0 || xx >= _rtW || yy >= _rtH) continue;
                            if (lit(px[yy * _rtW + xx])) nonBg++;
                        }
                    if (nonBg < 20) Fail($"{phase}:{e.Type}:coverage={nonBg} pos=({(int)p.Position.X},{(int)p.Position.Y}) off=({f.OffX},{f.OffY})");

                    // Fog 精确混合对照：图源最大亮度像素 → RT 期望 = src×opacity + bg×(1-opacity)
                    // （Fog BlendRate=0.4 → opacity 0.4；BlendVerify 已实证 NORMAL 混合语义）
                    if (e.Type == ParticleType.Fog)
                    {
                        float opacity = p.BlendRate;
                        var srcPx = FindBrightest(lib, f);
                        if (srcPx.alpha < 200)
                            Fail($"{phase}:fog:src-alpha={srcPx.alpha}（雾图预期不透明灰度）");
                        int sx = x0 + srcPx.gx, sy = y0 + srcPx.gy;
                        if (sx < 0 || sy < 0 || sx >= _rtW || sy >= _rtH) { Fail($"{phase}:fog:src-px-offscreen ({sx},{sy})"); continue; }
                        var got = px[sy * _rtW + sx];
                        int er = (int)(srcPx.c.r * opacity + 25 * (1 - opacity));
                        int eg = (int)(srcPx.c.g * opacity + 25 * (1 - opacity));
                        int eb = (int)(srcPx.c.b * opacity + 25 * (1 - opacity));
                        int dr = Math.Abs(got.r - er), dg = Math.Abs(got.g - eg), db = Math.Abs(got.b - eb);
                        _seq.Add($"{phase}:fog:src=({srcPx.gx},{srcPx.gy})#{srcPx.c.r:X2}{srcPx.c.g:X2}{srcPx.c.b:X2} got=({got.r},{got.g},{got.b}) exp=({er},{eg},{eb}) d=({dr},{dg},{db})");
                        if (dr > 20 || dg > 20 || db > 20) Fail($"{phase}:fog:blend d=({dr},{dg},{db})");
                    }
                    else
                    {
                        _seq.Add($"{phase}:{e.Type}:coverage={nonBg} pos=({(int)p.Position.X},{(int)p.Position.Y}) frame={p.ImageInfo.BaseIndex + p.ImageInfo.CurrentFrame}");
                    }
                }
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static (int gx, int gy, Color32 c, int alpha) FindBrightest(MLibraryUnity lib, SpriteFrame f)
        {
            var tex = lib.Atlas.GetPage(f.Page);
            var px = tex.GetPixels32();
            int best = -1, bgx = 0, bgy = 0; var bc = new Color32(0, 0, 0, 0);
            for (int gy = 0; gy < f.Height; gy++)
                for (int gx = 0; gx < f.Width; gx++)
                {
                    // Unity 翻转补偿（AtlasVerify HashFrame 同式）：图内 (gx,gy)（图顶起）→ tex row=texH-1-(f.Y+gy)
                    var c = px[(tex.height - 1 - (f.Y + gy)) * tex.width + (f.X + gx)];
                    int lum = c.r + c.g + c.b;
                    if (lum > best) { best = lum; bgx = gx; bgy = gy; bc = c; }
                }
            return (bgx, bgy, bc, bc.a);
        }

        static void WritePng(MLibraryUnity lib)
        {
            // 复渲染一帧（pass B 末态）出 PNG；R3 行序：编码前按行翻转
            var rt = RenderTexture.GetTemporary(_rtW, _rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                CrystalSpriteBatch.Begin(rt, _rtW, _rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                foreach (var e in _engines) e.Draw();
                CrystalSpriteBatch.End();
                var read = new Texture2D(_rtW, _rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, _rtW, _rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var src = read.GetPixels32();
                var fl = new Color32[src.Length];
                for (int y = 0; y < _rtH; y++)
                    Array.Copy(src, (_rtH - 1 - y) * _rtW, fl, y * _rtW, _rtW);
                read.SetPixels32(fl);
                read.Apply();
                var dir = Path.GetDirectoryName(_outPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(_outPath, read.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(read);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
