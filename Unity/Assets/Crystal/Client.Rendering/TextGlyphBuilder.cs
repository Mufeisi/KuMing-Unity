using System.Collections.Generic;
using UnityEngine;

namespace Crystal.Client.Rendering
{
    // 文本字形合成器：修复 TextGenerator 多字形 UV 失效（≥6 字形时整段 Populate 的 verts UV 塌缩到窄列、
    // 出现负宽度 quad，且会跳过部分字符——"4. Movements" 12 字符只出 11 quad，丢 's'）。
    // 算法（FontProbe.DumpPerChar 引擎级验证）：
    //   1. RequestCharactersInTexture 全字符入图集（此后单字符 Populate 不再触发图集重建，UV 稳定）；
    //   2. 逐字符单 Populate → 稳定 UV 包围盒 → 从图集提取字形像素；advance=quad 右缘（无字形字符如空格
    //      用 GetCharacterInfo 兜底）；
    //   3. 游标累积排布 + 字形自身 y 包围盒对齐基线，逐字形 blit 到文本位图。
    //   无字距（1-2px，探针可接受）。位图 bottom-up（row0=文本底），同 GetPixels32/UV 语义，直接给 SpriteBatch 画。
    public static class TextGlyphBuilder
    {
        public static Texture2D Build(string text, string fontName, int size, bool forceWhite)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var f = UnityEngine.Font.CreateDynamicFontFromOSFont(fontName, size);
            if (f == null) return null;
            var s = Settings(f, size);

            // 1. 全字符入图集（图集可能增长/重建）。
            f.RequestCharactersInTexture(text, 0, FontStyle.Bold);
            var fontTex = f.material != null ? f.material.mainTexture as Texture2D : null;
            if (fontTex == null || fontTex.width <= 0 || fontTex.height <= 0) return null;
            int atlW = fontTex.width, atlH = fontTex.height;

            // 2. 逐字符：字形 UV 包围盒 + advance（quad 右缘）+ 字形 y 包围盒。
            //    注意：GetPixels32 之后同字体再 Populate 时 fallback 字形几何失效（渲染后触发，
            //    实测 net-settings post-pass4 build=6x14 px=0；ASCII 因预载入图集幸免）——
            //    故必须先收集几何，再读像素 blit。
            float cursor = 0f;
            float gMinY = float.MaxValue, gMaxY = float.MinValue;
            var glyphs = new List<(float x, int minU, int minV, int maxU, int maxV, float botY)>();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                var gen = new TextGenerator();
                gen.Populate(c.ToString(), s);
                int minU = int.MaxValue, minV = int.MaxValue, maxU = int.MinValue, maxV = int.MinValue;
                float botY = 0f, topY = 0f, rightX = 0f;
                if (gen.verts.Count >= 4)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        var v = gen.verts[k];
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
                    // 无字形（空格等）：advance 走 GetCharacterInfo 兜底；字形占位 0/-1 让 blit 跳过。
                    CharacterInfo ci;
                    rightX = f.GetCharacterInfo(c, out ci, 0, FontStyle.Bold) ? ci.advance : 0f;
                    minU = 0; minV = 0; maxU = -1; maxV = -1;
                }
                glyphs.Add((cursor, minU, minV, maxU, maxV, botY));
                cursor += rightX;
            }

            int tw = Mathf.CeilToInt(cursor) + 1;
            int th = gMinY == float.MaxValue ? 0 : Mathf.CeilToInt(gMaxY - gMinY) + 1;
            if (tw <= 0 || th <= 0) return null;

            // 3. 几何收集完毕，图集定型后读像素（blit 源）。
            fontTex = f.material != null ? f.material.mainTexture as Texture2D : null;
            if (fontTex == null || fontTex.width <= 0 || fontTex.height <= 0) return null;
            atlW = fontTex.width; atlH = fontTex.height;
            var atlasPx = fontTex.GetPixels32();

            // 4. blit：字形行 r ↔ 文本空间 y=(botY + r) → canvas row=(botY - gMinY + r)；x 用游标。
            var px = new Color32[tw * th];
            for (int i = 0; i < text.Length; i++)
            {
                var g = glyphs[i];
                int gw = g.maxU - g.minU + 1, gh = g.maxV - g.minV + 1;
                if (gw <= 0 || gh <= 0 || g.minU < 0 || g.minV < 0 || g.maxU >= atlW || g.maxV >= atlH) continue;
                int ox = Mathf.FloorToInt(g.x);
                int oy = Mathf.FloorToInt(g.botY - gMinY);
                for (int r = 0; r < gh; r++)
                {
                    int row = oy + r;
                    if (row < 0 || row >= th) continue;
                    for (int cc = 0; cc < gw; cc++)
                    {
                        int col = ox + cc;
                        if (col < 0 || col >= tw) continue;
                        var src = atlasPx[(g.minV + r) * atlW + (g.minU + cc)];
                        if (src.a == 0) continue;
                        px[row * tw + col] = forceWhite
                            ? (src.a > 32 ? new Color32(255, 255, 255, src.a) : new Color32(0, 0, 0, 0))
                            : src;
                    }
                }
            }
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static TextGenerationSettings Settings(UnityEngine.Font font, int size)
        {
            return new TextGenerationSettings
            {
                font = font,
                fontSize = size,
                fontStyle = FontStyle.Bold,
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
    }
}
