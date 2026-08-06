using System;
using System.Collections.Generic;
using Client.MirControls;
using Crystal.Client.Rendering;
using UnityEngine;
// 显式别名：Client.Core 的 MirMath 类型 + seam Graphics/TextFormatFlags/MirControl，
// 避免与 UnityEngine.FontStyle/Graphics/Color 歧义（NetProbe 同款别名策略）。
using G = Client.MirGraphics.Graphics;
using TFF = Client.MirGraphics.TextFormatFlags;
using TR = Client.MirGraphics.TextRenderer;
using MCtrl = Client.MirControls.MirControl;
using MColor = Crystal.Client.Core.MirMath.Color;
using MRect = Crystal.Client.Core.MirMath.Rectangle;
using MSize = Crystal.Client.Core.MirMath.Size;
using MFont = Crystal.Client.Core.MirMath.Font;

namespace Crystal.Rendering.Editor
{
    // TextRenderer 渲染桥：把 Client.Core 的 TextRenderer seam 静态委托接到
    // Unity 动态字体（TextGenerator CPU 字形）+ CrystalSpriteBatch（R8 管线）。
    // 供 UiProbe（真实 MainDialog/ChatDialog 控制树）安装，batchmode 渲染上下文内工作。
    // 关键坑（net-hud 实证）：batch 内首建动态字体图集 → glyph UV 有效但 GetPixels32 返回透明。
    // 对策：所有字形合成都在 CrystalSpriteBatch.Begin() 之前完成——
    //   PreWarm(字号) 预热图集 glyph 集 + WarmTree(控制树) 为每个 MirLabel.Text 提前合成字形纹理，
    //   batch 内的 DrawText 只命中 _textTex 缓存，绝不触碰字体图集。实心背景/光标无此限制（纯纹理）。
    public static class UiText
    {
        static readonly Dictionary<string, Texture2D> _textTex = new Dictionary<string, Texture2D>();
        static readonly Dictionary<int, Texture2D> _solid = new Dictionary<int, Texture2D>();

        public static void Install()
        {
            TR.MeasureImpl = Measure;
            TR.MeasureImpl5 = Measure5;
            TR.DrawTextImpl = DrawText;
            TR.FillBackgroundImpl = FillBackground;
            TR.DrawCaretImpl = DrawCaret;
        }

        // 中文预热诊断：返回 _textTex 缓存中 CJK 文本 key（size|text）列表，用于核对预热覆盖。
        public static string DumpCjkKeys()
        {
            var list = new List<string>();
            foreach (var kv in _textTex)
            {
                int idx = kv.Key.IndexOf('|');
                string text = idx >= 0 ? kv.Key.Substring(idx + 1) : kv.Key;
                if (text.Length > 0 && text[0] > 127) list.Add(kv.Key);
            }
            list.Sort();
            return string.Join(" ", list);
        }

        // CJK 纹理有效性诊断：打印缓存中每个 CJK 纹理的实际不透明像素数（Build 成功 or 透明）。
        public static string DumpCjkOpaque()
        {
            var parts = new List<string>();
            foreach (var kv in _textTex)
            {
                int idx = kv.Key.IndexOf('|');
                string text = idx >= 0 ? kv.Key.Substring(idx + 1) : kv.Key;
                if (text.Length == 0 || text[0] <= 127) continue;
                var px = kv.Value.GetPixels32();
                int n = 0;
                for (int i = 0; i < px.Length; i++) if (px[i].a > 64) n++;
                parts.Add($"{kv.Key}={kv.Value.width}x{kv.Value.height}op{n}");
            }
            parts.Sort();
            return string.Join(" ", parts);
        }

        public static void Reset()
        {
            foreach (var kv in _textTex) UnityEngine.Object.DestroyImmediate(kv.Value);
            foreach (var kv in _solid) UnityEngine.Object.DestroyImmediate(kv.Value);
            _textTex.Clear();
            _solid.Clear();
        }

        // 批前预暖：为每个字号 Populate 全 ASCII，预热字体图集 glyph 集（后续 Populate 不触发图集重建）。
        public static void PreWarm(params int[] sizes)
        {
            const string warmText = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 "
                + "HPMP/:%-@!#$&()[].,<>~-_'\"+*=Level ";
            foreach (int sz in sizes)
            {
                var f = UnityEngine.Font.CreateDynamicFontFromOSFont("Arial", sz);
                var gen = new TextGenerator();
                gen.Populate(warmText, Settings(f, sz));
                var fontTex = f.material != null ? f.material.mainTexture as Texture2D : null;
                if (fontTex == null || fontTex.width <= 0 || fontTex.height <= 0) continue;
            }
        }

        // 批前预构建：递归控制树，为每个 MirLabel 的 Text 提前合成字形纹理。
        // 必须在 CrystalSpriteBatch.Begin() 之前调用；batch 内 DrawText 只命中 _textTex 缓存。
        public static void WarmTree(MCtrl root)
        {
            if (root == null) return;
            if (root is MirLabel label && !string.IsNullOrEmpty(label.Text))
                GetTextTexture(label.Text, label.Font);
            if (root.Controls != null)
                for (int i = 0; i < root.Controls.Count; i++)
                    WarmTree(root.Controls[i]);
        }

        // 批前预构建单段文本（MirTextBox 非 MirLabel，WarmTree 不覆盖；ChatTextBox 光标帧需预热）。
        public static void WarmText(string text, MFont font)
        {
            if (string.IsNullOrEmpty(text)) return;
            GetTextTexture(text, font);
        }

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

        // ---- 度量 ----
        static MSize Measure(G g, string text, MFont font) => MeasureText(text, font, MSize.Empty, TFF.Default);
        static MSize Measure5(G g, string text, MFont font, MSize proposed, TFF format) => MeasureText(text, font, proposed, format);

        static MSize MeasureText(string text, MFont font, MSize proposed, TFF format)
        {
            if (string.IsNullOrEmpty(text)) return MSize.Empty;
            int size = font.Size < 1 ? 8 : (int)font.Size;
            var f = UnityEngine.Font.CreateDynamicFontFromOSFont(font.Name, size);
            bool wrap = (format & TFF.WordBreak) != 0 && proposed.Width > 0;
            var s = Settings(f, size);
            s.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            var gen = new TextGenerator();
            gen.Populate(text, s);
            var verts = gen.verts;
            if (verts.Count == 0) return MSize.Empty;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in verts)
            {
                if (v.position.x < minX) minX = v.position.x;
                if (v.position.x > maxX) maxX = v.position.x;
                if (v.position.y < minY) minY = v.position.y;
                if (v.position.y > maxY) maxY = v.position.y;
            }
            return new MSize(Mathf.CeilToInt(maxX - minX), Mathf.CeilToInt(maxY - minY));
        }

        // ---- 字形绘制 ----
        static void DrawText(MCtrl control, string text, MFont font, MRect rect, MColor colour, TFF format)
        {
            if (string.IsNullOrEmpty(text)) return;
            var tex = GetTextTexture(text, font);
            if (tex == null) return;
            int x = rect.X, y = rect.Y;
            int w = tex.width, h = tex.height;
            if ((format & TFF.HorizontalCenter) != 0) x = rect.X + (rect.Width - w) / 2;
            if ((format & TFF.Right) != 0) x = rect.Right - w;
            if ((format & TFF.VerticalCenter) != 0) y = rect.Y + (rect.Height - h) / 2;
            if ((format & TFF.Bottom) != 0) y = rect.Bottom - h;
            CrystalSpriteBatch.Draw(tex, new Rect(0, 0, w, h), new Vector3(x, y, 0f), ToUnityColour(colour));
        }

        // 字形纹理：TextGlyphBuilder 逐字符合成（position 布局 + 单字符稳定 UV，绕开整段 TextGenerator
        // 多字形 UV 失效/跳字 bug）→ 强制白字形（图集 RGB=黑+alpha，R8 坑）→ 缓存。
        // 必须在 batch 外合成（经 WarmTree 预构建），batch 内 DrawText 只命中缓存。
        static Texture2D GetTextTexture(string text, MFont font)
        {
            int size = font.Size < 1 ? 8 : (int)font.Size;
            string key = size + "|" + text;
            if (_textTex.TryGetValue(key, out var hit)) return hit;

            var tex = TextGlyphBuilder.Build(text, font.Name, size, true);
            if (tex != null) _textTex[key] = tex;
            return tex;
        }

        // ---- 实心背景（MirControl.DrawControl BackColour 填充 / 聊天行底色）----
        static void FillBackground(MCtrl control, MRect rect, MColor colour)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            var tex = GetSolid(rect.Width, rect.Height, colour);
            CrystalSpriteBatch.Draw(tex, new Rect(0, 0, rect.Width, rect.Height), new Vector3(rect.X, rect.Y, 0f), Color.white);
        }

        // ---- 文本输入光标竖线（ChatTextBox 焦点态）----
        static void DrawCaret(MCtrl control, MRect rect)
        {
            if (rect.Height <= 0) return;
            var tex = GetSolid(Mathf.Max(1, rect.Width), rect.Height, MColor.White);
            CrystalSpriteBatch.Draw(tex, new Rect(0, 0, tex.width, tex.height), new Vector3(rect.X, rect.Y, 0f), Color.white);
        }

        static Texture2D GetSolid(int w, int h, MColor colour)
        {
            int argb = colour.ToArgb();
            int key = ((w * 7919 + h) * 131 + argb);
            if (_solid.TryGetValue(key, out var hit)) return hit;
            var c32 = new Color32(colour.R, colour.G, colour.B, colour.A);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = c32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            _solid[key] = tex;
            return tex;
        }

        static Color ToUnityColour(MColor c) => new Color(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
    }
}
