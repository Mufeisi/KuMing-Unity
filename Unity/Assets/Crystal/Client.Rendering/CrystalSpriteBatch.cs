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
        // G2 诊断：累计重建次数（跨帧缓存命中 → 静态场景每帧零重建）。
        public static int MeshRebuildCount;

        static RenderTexture _target;
        static int _screenW, _screenH;
        static bool _inBatch;
        static RenderTexture _prevRT;

        static readonly Dictionary<Texture2D, List<Quad>> _quads = new Dictionary<Texture2D, List<Quad>>();
        static readonly List<Texture2D> _keys = new List<Texture2D>();
        // 每纹理独立 Mesh（G2 实证：单一共享 Mesh + 连续 DrawMeshNow 无 CPU 间隔时 GPU 消费不及，
        // 上一条纹理的 buffer 被下一条覆写 → 非确定性错画；逐行 Flush 因行间大量 Draw 天然有间隔才稳定）。
        // 脏检查（quads 内容 + 屏幕尺寸未变）跳过重建 → 静态场景每帧只 SetPass+DrawMeshNow，零 buffer 上传。
        static readonly Dictionary<Texture2D, Mesh> _meshes = new Dictionary<Texture2D, Mesh>();
        static readonly Dictionary<Texture2D, List<Quad>> _meshSnap = new Dictionary<Texture2D, List<Quad>>();
        static int _meshScreenW, _meshScreenH;
        static Material _matAlpha, _matAdd, _matReplace, _matMultiply;

        static readonly int _mainTexId = Shader.PropertyToID("_MainTex");
        static readonly int _grayscaleId = Shader.PropertyToID("_Grayscale");

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
            RenderTexture.active = target;
            GL.Viewport(new Rect(0, 0, screenW, screenH));
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
                var mesh = GetMesh(tex, list);
                var mat = ReplaceBlend ? _matReplace
                        : BlendingMode == CrystalBlendMode.MULTIPLY ? _matMultiply
                        : (Blending ? _matAdd : _matAlpha);
                mat.SetFloat(_grayscaleId, GrayScale ? 1f : 0f);
                mat.SetTexture(_mainTexId, tex);
                mat.SetPass(0);
                Graphics.DrawMeshNow(mesh, Vector3.zero, Quaternion.identity);
                DPSCounter++;
                if (_debugFlush) Debug.Log($"[flush] k={k} tex={tex.name} quads={list.Count} meshVerts={mesh.vertexCount} mat={mat.name}");
            }
            _quads.Clear();
            _keys.Clear();
        }

        // 取（或构建）tex 的绘制 Mesh。每纹理独立 Mesh 实例（单一共享 Mesh 会 buffer 竞态错画）。
        // 缓存命中且内容未变 → 不重建（静态场景每帧零 buffer 上传，只 SetPass+DrawMeshNow）。
        static Mesh GetMesh(Texture2D tex, List<Quad> list)
        {
            bool have = _meshes.TryGetValue(tex, out var mesh);
            bool dirty = !have || _meshScreenW != _screenW || _meshScreenH != _screenH
                || !SameQuads(list, _meshSnap.TryGetValue(tex, out var snap) ? snap : null);
            if (have && !dirty) return mesh;
            if (!have)
            {
                mesh = new Mesh();
                mesh.name = "CrystalSpriteBatch";
                mesh.MarkDynamic();
                mesh.hideFlags = HideFlags.HideAndDontSave;
                _meshes[tex] = mesh;
            }
            BuildMesh(mesh, list, tex.width, tex.height);
            MeshRebuildCount++;
            _meshSnap[tex] = new List<Quad>(list);
            _meshScreenW = _screenW;
            _meshScreenH = _screenH;
            return mesh;
        }

        // 释放全部缓存的 per-texture Mesh（场景/图集切换时调用，防 GPU buffer 泄漏）。
        public static void ReleaseMeshes()
        {
            foreach (var kv in _meshes)
            {
                if (kv.Value != null) UnityEngine.Object.DestroyImmediate(kv.Value);
            }
            _meshes.Clear();
            _meshSnap.Clear();
        }

        static bool SameQuads(List<Quad> a, List<Quad> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                var q = a[i]; var r = b[i];
                if (q.pos.x != r.pos.x || q.pos.y != r.pos.y || q.size.x != r.size.x || q.size.y != r.size.y) return false;
                if (q.src.x != r.src.x || q.src.y != r.src.y || q.src.width != r.src.width || q.src.height != r.src.height) return false;
                if (q.color.r != r.color.r || q.color.g != r.color.g || q.color.b != r.color.b || q.color.a != r.color.a) return false;
            }
            return true;
        }

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
            RenderTexture.active = target;
            if (target != null) GL.Viewport(new Rect(0, 0, target.width, target.height));
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
        }

        static void BuildMesh(Mesh mesh, List<Quad> list, int tw, int th)
        {
            int n = list.Count;
            int needV = n * 4, needT = n * 6;
            // 精确大小数组（勿用共享 4096 长数组赋给 mesh：会把垃圾/残留顶点带进 buffer，
            // 且 per-texture mesh 独立时顶点数须 == quads*4 才与索引一致）。
            var verts = new Vector3[needV];
            var uvs = new Vector2[needV];
            var cols = new Color32[needV];
            var tris = new int[needT];

            int vi = 0, ti = 0;
            float invW = 1f / _screenW, invH = 1f / _screenH;
            float invTw = 1f / tw, invTh = 1f / th;
            for (int i = 0; i < n; i++)
            {
                var q = list[i];
                float x0 = q.pos.x, y0 = q.pos.y, x1 = q.pos.x + q.size.x, y1 = q.pos.y + q.size.y;
                // 屏幕 top-down → NDC y-up
                float nx0 = x0 * invW * 2f - 1f, ny0 = 1f - y0 * invH * 2f;
                float nx1 = x1 * invW * 2f - 1f, ny1 = 1f - y1 * invH * 2f;
                // 源区域 uv：v 轴反转使 quad 顶边 = PNG 顶行（匹配 golden top-down）
                float u0 = q.src.x * invTw, u1 = (q.src.x + q.src.width) * invTw;
                float vTop = 1f - q.src.y * invTh;
                float vBot = 1f - (q.src.y + q.src.height) * invTh;
                var c = (Color32)q.color;

                verts[vi] = new Vector3(nx0, ny0, 0f); uvs[vi] = new Vector2(u0, vTop); cols[vi] = c; vi++;
                verts[vi] = new Vector3(nx1, ny0, 0f); uvs[vi] = new Vector2(u1, vTop); cols[vi] = c; vi++;
                verts[vi] = new Vector3(nx0, ny1, 0f); uvs[vi] = new Vector2(u0, vBot); cols[vi] = c; vi++;
                verts[vi] = new Vector3(nx1, ny1, 0f); uvs[vi] = new Vector2(u1, vBot); cols[vi] = c; vi++;

                int b = i * 4;
                tris[ti++] = b; tris[ti++] = b + 1; tris[ti++] = b + 2;
                tris[ti++] = b + 1; tris[ti++] = b + 3; tris[ti++] = b + 2;
            }

            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = cols;
            mesh.triangles = tris;
        }
    }
}
