using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Crystal.Client.Assets
{
    // 运行时图集门面：从 <rel>.json 清单构建 SpriteFrame 表，按需加载页 Texture2D。
    // 渲染层直接消费 GetFrame/GetPage；页纹理由调用方持有生命周期。
    public sealed class AtlasLibrary
    {
        public string Dir { get; private set; }
        public LibManifest Manifest { get; private set; }
        public SpriteFrame[] Frames { get; private set; }
        public int NonEmptyCount { get; private set; }

        readonly Dictionary<int, Texture2D> _pageCache = new Dictionary<int, Texture2D>();

        AtlasLibrary() { }

        public static AtlasLibrary Load(string manifestPath)
        {
            string json = File.ReadAllText(manifestPath);
            var man = JsonUtility.FromJson<LibManifest>(json);
            if (man == null || man.Images == null)
                throw new InvalidDataException($"manifest parse failed: {manifestPath}");

            var lib = new AtlasLibrary
            {
                Dir = Path.GetDirectoryName(manifestPath),
                Manifest = man,
                Frames = new SpriteFrame[man.Images.Count],
            };

            int nonEmpty = 0;
            for (int i = 0; i < man.Images.Count; i++)
            {
                var e = man.Images[i];
                var f = new SpriteFrame
                {
                    Index = i,
                    Empty = e.Empty,
                    Width = e.W,
                    Height = e.H,
                    OffX = e.OX,
                    OffY = e.OY,
                    SX = e.SX,
                    SY = e.SY,
                    Shadow = e.Shadow,
                    Page = e.Page,
                    X = e.X,
                    Y = e.Y,
                };
                if (e.Mask != null)
                {
                    f.HasMask = true;
                    f.MaskX = e.Mask.MX;
                    f.MaskY = e.Mask.MY;
                }
                lib.Frames[i] = f;
                if (!e.Empty) nonEmpty++;
            }
            lib.NonEmptyCount = nonEmpty;
            return lib;
        }

        public SpriteFrame GetFrame(int index) => Frames[index];

        // 懒加载页纹理（主图页；mask 页用 GetMaskPage）。非可读页在 Texture2D.LoadImage 后默认可读。
        public Texture2D GetPage(int pageIdx)
        {
            if (_pageCache.TryGetValue(pageIdx, out var tex)) return tex;
            var page = Manifest.Pages[pageIdx];
            tex = LoadTexture(Path.Combine(Dir, page.Name));
            _pageCache[pageIdx] = tex;
            return tex;
        }

        public Texture2D GetMaskPage(int pageIdx)
        {
            if (Manifest.MaskPages == null || pageIdx >= Manifest.MaskPages.Count) return null;
            int key = ~pageIdx;
            if (_pageCache.TryGetValue(key, out var tex)) return tex;
            var page = Manifest.MaskPages[pageIdx];
            tex = LoadTexture(Path.Combine(Dir, page.Name));
            _pageCache[key] = tex;
            return tex;
        }

        public void UnloadAll()
        {
            // Destroy 延迟到帧末，-batchmode/-nographics 无帧循环时原生纹理永不释放（跨库累积可 OOM）。
            foreach (var kv in _pageCache)
                if (kv.Value != null) UnityEngine.Object.DestroyImmediate(kv.Value);
            _pageCache.Clear();
        }

        static Texture2D LoadTexture(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
                throw new InvalidDataException($"texture load failed: {path}");
            // 点过滤：镜像旧客户端像素画（DX9 点采样）。golden 为精确纹素，双线性会在非整分
            // UV 处插值出 ±1 残差（RenderDump 实证 675 高帧 39 像素差），点过滤可逐字节复现。
            tex.filterMode = FilterMode.Point;
            return tex;
        }
    }
}
