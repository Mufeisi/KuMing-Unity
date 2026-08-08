using System.Collections.Generic;
using UnityEngine;

namespace Crystal.Client.Rendering
{
    public enum CrystalBlendMode
    {
        NORMAL,
        INVLIGHT,
        MULTIPLY   // Zero/SourceColor：dest = dest * src（DrawLights 灯光贴图乘回场景）
    }

    // 渲染批处理器：复刻旧客户端 DXManager.Draw 咽喉面（保留同名语义），
    // 使移植的 MLibrary/GameScene ~470 处 Libraries.X.Draw 调用点无需改动。
    // 顶点在 CPU 侧烘焙为 NDC（屏幕 top-down → NDC y-up），shader 直通，无投影矩阵依赖。
    // V 轴：图集页纹理 v=0=PNG 末行（GetPixels32 实证垂直翻转），故 quad 顶边 uv v=1（PNG 顶行），
    // 输出与 golden（PNG top-down）一致。
    public static class CrystalSpriteBatch
    {
        // ---- 状态（镜像 DXManager） ----
        public static bool GrayScale;
        public static bool OutlineEnabled;
        public static Color OutlineColour = Color.red;
        public static bool Blending;
        public static float BlendingRate = 1f;
        public static CrystalBlendMode BlendingMode;
        public static float Opacity = 1f;
        public static int DPSCounter;
        // 镜像 DXManager.Sprite.Transform（仅缩放用，MLibrary.Draw(index,point,size) 会设缩放矩阵）。
        public static Matrix4x4 Transform = Matrix4x4.identity;
        // 验证钩子：true 时用 Blend Off 材质直写源像素（golden 对照用，游戏画面不使用）。
        public static bool ReplaceBlend;
        // G2 诊断：打印每次 Flush 提交（纹理/quad 数），batchmode 下经 Debug.Log 进 Editor.log。
        public static bool _debugFlush;

        static RenderTexture _target;
        static int _screenW, _screenH;
        static bool _inBatch;
        static RenderTexture _prevRT;

        static readonly Dictionary<Texture2D, List<Quad>> _quads = new Dictionary<Texture2D, List<Quad>>();
        static readonly List<Texture2D> _keys = new List<Texture2D>();
        static Material _matAlpha, _matAdd, _matReplace, _matMultiply, _matOutline;

        static readonly int _mainTexId = Shader.PropertyToID("_MainTex");
        static readonly int _grayscaleId = Shader.PropertyToID("_Grayscale");
        static readonly int _outlineColourId = Shader.PropertyToID("_OutlineColor");

        struct Quad
        {
            public Vector2 pos;   // 屏幕左上角（px）
            public Vector2 size;  // 目标尺寸（px，已含 Transform 缩放）
            public Rect src;      // 图集页内源区域（px，top-down，src.y=顶行）
            public Color color;
        }

        public static void Begin(RenderTexture target, int screenW, int screenH)
        {
            _target = target;
            _screenW = screenW;
            _screenH = screenH;
            _prevRT = RenderTexture.active;
            // 屏幕模式（target=null）：不触碰 RenderTexture.active/GL.Viewport——OnPostRender 相机上下文里
            // 手动置 active=null 会使后续 GL 立即模式绘制丢失（实证：诊断 red quad 不经 Begin 成功，Flush 经 Begin 全黑）。
            if (target != null)
            {
                RenderTexture.active = target;
                GL.Viewport(new Rect(0, 0, screenW, screenH));
            }
            GL.PushMatrix();
            GL.LoadProjectionMatrix(Matrix4x4.identity);
            GL.LoadIdentity();
            _inBatch = true;
        }

        public static void End()
        {
            if (!_inBatch) return;
            Flush();
            GL.PopMatrix();
            RenderTexture.active = _prevRT;
            _inBatch = false;
            _target = null;
        }

        // 对应 DXManager.Draw(Texture, Rectangle?, Vector3?, Color4)
        public static void Draw(Texture2D tex, Rect sourceRect, Vector3 position, Color color)
        {
            DrawInternal(tex, sourceRect, position, color, Opacity);
        }

        // 对应 DXManager.DrawOpaque(Texture, Rectangle?, Vector3?, Color4, float opacity)
        public static void DrawOpaque(Texture2D tex, Rect sourceRect, Vector3 position, Color color, float opacity)
        {
            DrawInternal(tex, sourceRect, position, color, opacity);
        }

        static void DrawInternal(Texture2D tex, Rect src, Vector3 pos, Color color, float opacity)
        {
            if (!_inBatch || tex == null) return;

            float sx = 1f, sy = 1f;
            if (Transform != Matrix4x4.identity)
            {
                sx = Transform.m00;
                sy = Transform.m11;
            }

            var q = new Quad
            {
                pos = new Vector2(pos.x * sx, pos.y * sy),
                size = new Vector2(src.width * sx, src.height * sy),
                src = src,
                color = new Color(color.r, color.g, color.b, color.a * opacity),
            };

            if (!_quads.TryGetValue(tex, out var list))
            {
                list = new List<Quad>(64);
                _quads[tex] = list;
                _keys.Add(tex);
            }
            list.Add(q);
        }

        public static void Flush()
        {
            if (_quads.Count == 0) return;
            EnsureResources();
            for (int k = 0; k < _keys.Count; k++)
            {
                var tex = _keys[k];
                var list = _quads[tex];
                if (list.Count == 0) continue;
                var mat = OutlineEnabled ? _matOutline
                        : ReplaceBlend ? _matReplace
                        : BlendingMode == CrystalBlendMode.MULTIPLY ? _matMultiply
                        : (Blending ? _matAdd : _matAlpha);
                mat.SetFloat(_grayscaleId, GrayScale ? 1f : 0f);
                if (OutlineEnabled) mat.SetColor(_outlineColourId, OutlineColour);
                mat.SetTexture(_mainTexId, tex);
                mat.SetPass(0);
                // GL 立即模式提交：顶点已 CPU 烘焙 NDC，Begin 已设 GL 矩阵栈 identity → shader vert 直通 v.vertex.xy。
                // 弃用 DrawMeshNow：实证其在相机 OnPostRender 上下文不输出（PC 屏幕全黑 vs RT 探针正常），
                // GL 立即模式 RT/屏幕双路径均验证 OK（红色测试 quad）。
                // Android 用 TRIANGLES：GL.QUADS 在该后端（默认 Vulkan）无此原语。
                GL.Begin(GL.TRIANGLES);
                for (int i = 0; i < list.Count; i++) EmitQuad(list[i], tex.width, tex.height);
                GL.End();
                DPSCounter++;
                if (_debugFlush) Debug.Log($"[flush] k={k} tex={tex.name} quads={list.Count} mat={mat.name}");
            }
            _quads.Clear();
            _keys.Clear();
        }

        // 单 quad 提交：屏幕 top-down → NDC y-up（与原 BuildMesh 同款顶点烘焙），v 轴翻转匹配 PNG top-down。
        static void EmitQuad(Quad q, int tw, int th)
        {
            float invW = 1f / _screenW, invH = 1f / _screenH;
            float x0 = q.pos.x, y0 = q.pos.y, x1 = q.pos.x + q.size.x, y1 = q.pos.y + q.size.y;
            float nx0 = x0 * invW * 2f - 1f, ny0 = 1f - y0 * invH * 2f;
            float nx1 = x1 * invW * 2f - 1f, ny1 = 1f - y1 * invH * 2f;
            float invTw = 1f / tw, invTh = 1f / th;
            float u0 = q.src.x * invTw, u1 = (q.src.x + q.src.width) * invTw;
            float vTop = 1f - q.src.y * invTh;
            float vBot = 1f - (q.src.y + q.src.height) * invTh;
            GL.Color(q.color);
            // 每 quad 两个三角形须共享同一条对角线（TL-BR，即 v0v1v2+v0v2v3 规范拆分）。
            // 实证 TL,TR,BL / BL,BR,TL 两三角形斜边为两条交叉对角线（\ 与 /），quad 中央
            // 留下约 25% 菱形空洞未覆盖 → 透出 clear color → 地图规律黑三角/钻石花屏（PC/Android 均有）。
            GL.TexCoord2(u0, vTop); GL.Vertex3(nx0, ny0, 0f);   // TL
            GL.TexCoord2(u1, vTop); GL.Vertex3(nx1, ny0, 0f);   // TR
            GL.TexCoord2(u1, vBot); GL.Vertex3(nx1, ny1, 0f);   // BR
            GL.TexCoord2(u1, vBot); GL.Vertex3(nx1, ny1, 0f);   // BR
            GL.TexCoord2(u0, vBot); GL.Vertex3(nx0, ny1, 0f);   // BL
            GL.TexCoord2(u0, vTop); GL.Vertex3(nx0, ny0, 0f);   // TL
        }

        // 释放资源（原 per-texture Mesh 已随 DrawMeshNow 移除，保留 API 兼容：GameRenderer.ReleaseAll 调用）。
        public static void ReleaseMeshes() { }

        // 对应 DXManager.SetBlend(bool, float, BlendMode) —— 换态前 flush（与旧 Sprite.Flush 同义）。
        public static void SetBlend(bool value, float rate = 1f, CrystalBlendMode mode = CrystalBlendMode.NORMAL)
        {
            if (value == Blending && rate == BlendingRate && mode == BlendingMode) return;
            Flush();
            Blending = value;
            BlendingRate = rate;
            BlendingMode = mode;
        }

        // 对应 DXManager.SetGrayscale(bool)。
        public static void SetGrayscale(bool value)
        {
            if (value == GrayScale) return;
            Flush();
            GrayScale = value;
        }

        // sanduan OutLine.shader 描边状态：开启后 Flush 用 Crystal/SpriteOutline 材质（平涂描边色 + 阴影例外）。
        public static void SetOutline(bool value)
        {
            if (value == OutlineEnabled) return;
            Flush();
            OutlineEnabled = value;
        }

        public static void SetOutlineColour(Color value)
        {
            if (value == OutlineColour) return;
            Flush();
            OutlineColour = value;
        }

        // sanduan OutLine.shader 描边（图集兼容实现）：4 向 ±thickness px 平涂描边色副本压后 + 原图压顶，
        // 得轮廓外 1px 描边光环（与 MirLabel 文本描边 4 向重绘同款）。atlas 批处理中 UV 邻域采样会串读
        // 相邻帧，故不能在 frag 里按 sanduan 原版做邻域描边——用偏移副本等价复刻效果语义。
        public static void DrawOutline(Texture2D tex, Rect src, Vector3 pos, Color colour, Color outlineColour, float thickness = 1f)
        {
            SetOutlineColour(outlineColour);
            SetOutline(true);
            Draw(tex, src, pos + new Vector3(-thickness, 0f, 0f), Color.white);
            Draw(tex, src, pos + new Vector3(0f, -thickness, 0f), Color.white);
            Draw(tex, src, pos + new Vector3(thickness, 0f, 0f), Color.white);
            Draw(tex, src, pos + new Vector3(0f, thickness, 0f), Color.white);
            SetOutline(false);
            Draw(tex, src, pos, colour);
        }

        // 对应 DXManager.SetOpacity(float) —— 烘焙进后续 quad 的顶点 alpha。
        public static void SetOpacity(float value)
        {
            if (value == Opacity) return;
            Flush();
            Opacity = value;
        }

        // 对应 DXManager.SetSurface(Surface) —— 切换渲染目标。
        public static void SetSurface(RenderTexture target)
        {
            if (target == _target) return;
            Flush();
            _prevRT = RenderTexture.active;
            _target = target;
            if (target != null)
            {
                RenderTexture.active = target;
                GL.Viewport(new Rect(0, 0, target.width, target.height));
            }
        }

        public static void Clear(Color color)
        {
            Flush();
            if (RenderTexture.active != null)
                GL.Clear(true, true, color);
        }

        static void EnsureResources()
        {
            if (_matAlpha == null)
            {
                var s = Shader.Find("Crystal/Sprite");
                _matAlpha = new Material(s);
                _matAlpha.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_matAdd == null)
            {
                var s2 = Shader.Find("Crystal/SpriteAdditive");
                _matAdd = new Material(s2);
                _matAdd.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_matReplace == null)
            {
                var s3 = Shader.Find("Crystal/SpriteReplace");
                _matReplace = new Material(s3);
                _matReplace.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_matMultiply == null)
            {
                var s4 = Shader.Find("Crystal/SpriteMultiply");
                _matMultiply = new Material(s4);
                _matMultiply.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_matOutline == null)
            {
                var s5 = Shader.Find("Crystal/SpriteOutline");
                _matOutline = new Material(s5);
                _matOutline.hideFlags = HideFlags.HideAndDontSave;
            }
        }

    }
}
