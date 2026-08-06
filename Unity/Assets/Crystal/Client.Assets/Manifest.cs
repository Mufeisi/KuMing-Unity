using System;
using System.Collections.Generic;

namespace Crystal.Client.Assets
{
    // Crystal.AssetCompiler 产出的 <rel>.json 清单的运行时镜像。
    // 字段名与 JSON 键严格一致（JsonUtility 按名映射），保持 [Serializable] 纯 C# 数据类。

    [Serializable]
    public sealed class LibManifest
    {
        public string Lib;
        public int Version;
        public int Count;
        public int PageSize;
        public List<PageFile> Pages;
        public List<PageFile> MaskPages;
        public List<ImageEntry> Images;
        public List<FrameEntry> Frames;
    }

    [Serializable]
    public sealed class PageFile
    {
        public string Name;
        public int W;
        public int H;
    }

    [Serializable]
    public sealed class ImageEntry
    {
        public int I;
        public bool Empty;
        public int W;
        public int H;
        public int OX;
        public int OY;
        public int SX;
        public int SY;
        public int Shadow;
        public int Page = -1;
        public int X;
        public int Y;
        public MaskEntry Mask;
    }

    [Serializable]
    public sealed class MaskEntry
    {
        public int Page;
        public int X;
        public int Y;
        public int W;
        public int H;
        public int MX;
        public int MY;
    }

    [Serializable]
    public sealed class FrameEntry
    {
        public string Action;
        public int ActionId;
        public int Start;
        public int Count;
        public int Skip;
        public int Interval;
        public int EffectStart;
        public int EffectCount;
        public int EffectSkip;
        public int EffectInterval;
        public bool Reverse;
        public bool Blend;
    }
}
