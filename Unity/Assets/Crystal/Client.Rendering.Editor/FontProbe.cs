using System;
using UnityEngine;
using Crystal.Client.Rendering;

namespace Crystal.Rendering.Editor
{
    // 字体探针：决定性地回答"Unity 动态字体用 Arial 渲染中文字符时,TextGenerator 输出什么"。
    // 背景：Settings.FontName = "Arial"（旧客户端同源，忠实移植），Arial 无 CJK 字形，
    //   旧客户端靠 GDI+ 自动 fallback 到系统中文字体。Unity 侧依赖 dynamic font fallback，
    //   batchmode 下行为未知——本探针实测：每字符 quads 数（是否有字形）+ 单字符图集字形 ASCII。
    // 用法（batchmode 经 Hub 会话，无需 Server）：
    //   Unity.exe -batchmode -quit -executeMethod Crystal.Rendering.Editor.FontProbe.Run
    static class FontProbe
    {
        static TextGenerationSettings Settings(UnityEngine.Font font, int size)
        {
            return new TextGenerationSettings
            {
                font = font,
                fontSize = size,
                fontStyle = UnityEngine.FontStyle.Bold,
                color = Color.white,
                textAnchor = TextAnchor.UpperLeft,
                richText = false,
                scaleFactor = 1f,
                lineSpacing = 1f,
                pivot = Vector2.zero,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                resizeTextForBestFit = false,
                updateBounds = false,
            };
        }

        public static void Run()
        {
            string[] fonts = { "Arial", "Microsoft YaHei", "SimSun", "SimHei" };
            string[] samples = { "移", "动", "移动", "全部帮助", "F9", "4 / 45", "普通私聊喊话系统" };
            foreach (var fn in fonts)
            {
                var f = UnityEngine.Font.CreateDynamicFontFromOSFont(fn, 10);
                if (f == null) { Console.WriteLine($"[font] \"{fn}\" CreateDynamicFontFromOSFont -> null"); continue; }
                foreach (var s in samples)
                {
                    var gen = new TextGenerator();
                    gen.Populate(s, Settings(f, 10));
                    int quads = gen.verts.Count / 4;
                    Console.WriteLine($"[font] \"{fn}\" \"{s}\" chars={s.Length} quads={quads}");
                }
            }

            DumpGlyph("Arial", "移");
            DumpGlyph("Arial", "动");
            DumpGlyph("Arial", "A");
            DumpGlyph("Microsoft YaHei", "移");

            DumpQuads("4. Movements");
            DumpQuads("Movements");
            DumpQuads("4. ");
            DumpQuads("移动");

            DumpPerChar("移动");
            DumpPerChar("4. ");
            DumpPerChar("Movements");
            DumpPerChar("4. Movements");
            DumpPerChar("普通私聊喊话系统");
            DumpPerChar("  F9");
            DumpPerChar("X / 45");

            // 中文零渲染根因分离（net-settings 实测 build=6x14 px=0）：字号 8 vs 既有 Arial 实例（PreWarm）。
            DumpPerChar("背包开/关", 8);
            BuildCheck("背包开/关", 8, false);
            BuildCheck("背包开/关", 8, true);
            BuildCheck("背包开/关", 10, false);
            BuildCheck("背包开/关", 10, true);
            BuildCheck("移动", 8, true);

            RenderThenBuild();
        }

        // 逐字符合成探针 v2（字符驱动布局）：不再依赖整段 quad 列表（TextGenerator 会跳过部分字符——
        // "4. " 的 3 字符只产出 2 quad，中部跳字会让 quad↔text 索引错位）。
        // 方案：RequestCharactersInTexture 全字符入图集（此后不再增长）→ 逐字符单 Populate 取稳定 UV 字形
        // + 单字符 advance 累积游标排布 + 字形自身 y 包围盒对齐基线。无字距（1-2px，可接受）。
        static void DumpPerChar(string text, int size = 10)
        {
            var f = UnityEngine.Font.CreateDynamicFontFromOSFont("Arial", size);
            if (f == null) { Console.WriteLine($"[perchar] \"{text}\" font null"); return; }
            var s = Settings(f, size);
            s.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 0. 全字符入图集（保证单字符 Populate 不再触发图集重建，UV 稳定）。
            f.RequestCharactersInTexture(text, 0, UnityEngine.FontStyle.Bold);
            var fontTex = f.material != null ? f.material.mainTexture as Texture2D : null;
            if (fontTex == null || fontTex.width <= 0) { Console.WriteLine($"[perchar] \"{text}\" no atlas"); return; }
            int atlW = fontTex.width, atlH = fontTex.height;
            var atlasPx = fontTex.GetPixels32();

            // 1. 逐字符：单 Populate 取字形 UV 包围盒 + 字形 position 包围盒（advance=右缘）。
            float cursor = 0f;
            float gMinY = float.MaxValue, gMaxY = float.MinValue;
            var gs = new System.Collections.Generic.List<(float x, int minU, int minV, int maxU, int maxV, float botY, float topY)>();
            for (int ti = 0; ti < text.Length; ti++)
            {
                char c = text[ti];
                var gen2 = new TextGenerator();
                gen2.Populate(c.ToString(), s);
                int minU = int.MaxValue, minV = int.MaxValue, maxU = int.MinValue, maxV = int.MinValue;
                float botY = float.MaxValue, topY = float.MinValue, rightX = 0f;
                if (gen2.verts.Count >= 4)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        var v = gen2.verts[k];
                        int u = Mathf.FloorToInt(v.uv0.x * atlW);
                        int vv = Mathf.FloorToInt(v.uv0.y * atlH);
                        minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
                        minV = Mathf.Min(minV, vv); maxV = Mathf.Max(maxV, vv);
                        if (v.position.x > rightX) rightX = v.position.x;
                        if (v.position.y < botY) botY = v.position.y;
                        if (v.position.y > topY) topY = v.position.y;
                    }
                    gMinY = Mathf.Min(gMinY, botY); gMaxY = Mathf.Max(gMaxY, topY);
                }
                else
                {
                    // 无字形（空格等）：advance 走 GetCharacterInfo 兜底。
                    CharacterInfo ci;
                    rightX = f.GetCharacterInfo(c, out ci, 0, UnityEngine.FontStyle.Bold) ? ci.advance : 0f;
                }
                gs.Add((cursor, minU, minV, maxU, maxV, botY, topY));
                Console.WriteLine($"[perchar] '{c}' advance={rightX.ToString("F1")} u=[{minU},{maxU}] v=[{minV},{maxV}] botY={botY.ToString("F1")}");
                cursor += rightX;
            }
            int cw = Mathf.CeilToInt(cursor) + 1;
            int ch = Mathf.CeilToInt(gMaxY - gMinY) + 1;
            var canvas = new bool[cw * ch];
            Console.WriteLine($"[perchar] \"{text}\" size={size} chars={text.Length} canvas={cw}x{ch} atlas={atlW}x{atlH}");

            // 2. blit：字形行 r ↔ 文本空间 y=(botY + r)，映射到 canvas row=(botY - gMinY + r)；x 用游标。
            for (int ti = 0; ti < text.Length; ti++)
            {
                var it = gs[ti];
                int gw = it.maxU - it.minU + 1, gh = it.maxV - it.minV + 1;
                if (gw <= 0 || gh <= 0 || it.minU < 0 || it.minV < 0) continue;
                int ox = Mathf.FloorToInt(it.x);
                int oy = Mathf.FloorToInt(it.botY - gMinY);
                for (int r = 0; r < gh; r++)
                {
                    int row = oy + r;
                    if (row < 0 || row >= ch) continue;
                    for (int cc = 0; cc < gw; cc++)
                    {
                        int col = ox + cc;
                        if (col < 0 || col >= cw) continue;
                        var src = atlasPx[(it.minV + r) * atlW + (it.minU + cc)];
                        if (src.a > 64) canvas[row * cw + col] = true;
                    }
                }
            }
            for (int y = ch - 1; y >= 0; y--)
            {
                var line = "";
                for (int x = 0; x < cw; x++) line += canvas[y * cw + x] ? '#' : '.';
                if (line.Contains("#")) Console.WriteLine("[perchar] " + line);
            }
        }

        // 逐字符 quad 诊断：输出每个字符的 UV 与屏幕 position（TextGenerator 每字符 4 顶点）。
        // 关键：uv0 是左下角顶点。双 Populate 对照——第一次触发图集增长（UV 可能失效），第二次图集已定型。
        static void DumpQuads(string text)
        {
            var f = UnityEngine.Font.CreateDynamicFontFromOSFont("Arial", 10);
            if (f == null) { Console.WriteLine($"[quads] \"{text}\" font null"); return; }
            for (int pass = 1; pass <= 2; pass++)
            {
                var gen = new TextGenerator();
                gen.Populate(text, Settings(f, 10));
                Console.WriteLine($"[quads] \"{text}\" pass={pass} chars={text.Length} verts={gen.verts.Count}");
                for (int i = 0; i < gen.verts.Count; i += 4)
                {
                    var v0 = gen.verts[i];
                    var v2 = gen.verts[i + 2];
                    char ch = i / 4 < text.Length ? text[i / 4] : '?';
                    float wpx = (v2.uv0.x - v0.uv0.x) * f.material.mainTexture.width;
                    float hpx = (v2.uv0.y - v0.uv0.y) * f.material.mainTexture.height;
                    Console.WriteLine($"[quad] pass{pass} ch='{ch}' uv=({v0.uv0.x.ToString("F3")},{v0.uv0.y.ToString("F3")})->({v2.uv0.x.ToString("F3")},{v2.uv0.y.ToString("F3")}) glyph={wpx.ToString("F1")}x{hpx.ToString("F1")} pos=({v0.position.x.ToString("F1")},{v0.position.y.ToString("F1")})");
                }
            }
        }

        // 单字符字形：从 verts 取 quad 的 UV 包围盒，从字体图集读像素 → ASCII 轮廓。
        static void DumpGlyph(string fn, string ch)
        {
            var f = UnityEngine.Font.CreateDynamicFontFromOSFont(fn, 24);
            if (f == null) { Console.WriteLine($"[glyph] \"{fn}\" font null"); return; }
            var gen = new TextGenerator();
            gen.Populate(ch, Settings(f, 24));
            var tex = f.material != null ? f.material.mainTexture as Texture2D : null;
            if (tex == null) { Console.WriteLine($"[glyph] \"{fn}\" \"{ch}\" no atlas"); return; }
            if (gen.verts.Count < 4) { Console.WriteLine($"[glyph] \"{fn}\" \"{ch}\" NO VERTICES (quads={gen.verts.Count / 4})"); return; }

            var px = tex.GetPixels32();
            int atlW = tex.width, atlH = tex.height;
            int minU = int.MaxValue, minV = int.MaxValue, maxU = int.MinValue, maxV = int.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var v = gen.verts[i];
                int u = Mathf.FloorToInt(v.uv0.x * atlW);
                int vv = Mathf.FloorToInt(v.uv0.y * atlH);
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
                minV = Mathf.Min(minV, vv); maxV = Mathf.Max(maxV, vv);
            }
            int tw = maxU - minU + 1, th = maxV - minV + 1;
            Console.WriteLine($"[glyph] \"{fn}\" \"{ch}\" quad={tw}x{th} atlas={atlW}x{atlH} pos=({gen.verts[0].position.x.ToString("0.0")},{gen.verts[0].position.y.ToString("0.0")})");
            for (int y = minV; y <= maxV; y++)
            {
                var line = "";
                for (int x = minU; x <= maxU; x++)
                {
                    var c = px[y * atlW + x];
                    line += c.a > 64 ? '#' : '.';
                }
                if (line.Contains("#")) Console.WriteLine("[glyph] " + line);
            }
        }

        // 逐字符合成（TextGlyphBuilder.Build）对照：模拟 UiText.PreWarm（同尺寸先 Populate ASCII）与否，
        // 分离"既有 Arial 实例"与"字号"两个变量——net-settings 中文零渲染（build=6x14 px=0）根因定位。
        static void BuildCheck(string text, int size, bool prewarm)
        {
            if (prewarm)
            {
                var pw = UnityEngine.Font.CreateDynamicFontFromOSFont("Arial", size);
                var gen = new TextGenerator();
                gen.Populate("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", Settings(pw, size));
            }
            var tex = TextGlyphBuilder.Build(text, "Arial", size, true);
            if (tex == null) { Console.WriteLine($"[buildcheck] \"{text}\" size={size} prewarm={prewarm} -> null"); return; }
            var px = tex.GetPixels32();
            int n = 0;
            foreach (var c in px) if (c.a > 64) n++;
            Console.WriteLine($"[buildcheck] \"{text}\" size={size} prewarm={prewarm} -> {tex.width}x{tex.height} opaque={n}");
        }

        // 模拟 net-settings 的 RT 批渲染（每遍 Begin→Clear→Draw→End→ReadPixels），
        // 实证"渲染上下文后 Build 中文字形透明"（广义 R8 坑：批渲染后新字形 GetPixels32 失效）。
        static void RenderThenBuild()
        {
            var solid = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var sp = new Color32[64];
            for (int i = 0; i < 64; i++) sp[i] = new Color32(255, 0, 0, 255);
            solid.SetPixels32(sp);
            solid.Apply();
            for (int pass = 1; pass <= 4; pass++)
            {
                var rt = RenderTexture.GetTemporary(320, 200, 24, RenderTextureFormat.ARGB32);
                try
                {
                    CrystalSpriteBatch.Begin(rt, 320, 200);
                    CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                    CrystalSpriteBatch.Draw(solid, new Rect(0, 0, 8, 8), new Vector3(10, 10, 0f), Color.white);
                    CrystalSpriteBatch.End();
                    RenderTexture.active = rt;
                    var snap = new Texture2D(320, 200, TextureFormat.RGBA32, false);
                    snap.ReadPixels(new Rect(0, 0, 320, 200), 0, 0);
                    UnityEngine.Object.DestroyImmediate(snap);
                    RenderTexture.active = null;
                }
                finally { RenderTexture.ReleaseTemporary(rt); }
            }
            Console.WriteLine("[rtbatch] 4 RT batches done");
            var tex = TextGlyphBuilder.Build("背包开/关", "Arial", 8, true);
            if (tex == null) { Console.WriteLine("[rtbatch] build after 4 RT batches -> null"); return; }
            var px = tex.GetPixels32();
            int n = 0;
            foreach (var c in px) if (c.a > 64) n++;
            Console.WriteLine($"[rtbatch] build after 4 RT batches -> {tex.width}x{tex.height} opaque={n}");
        }
    }
}
