namespace Crystal.Client.Assets
{
    // MImage 的运行时等价物：描述单张图片在图集中的位置与绘制语义。
    // 由 AtlasLibrary 从 LibManifest.ImageEntry 构建，供渲染层按页 + rect 采样。
    public struct SpriteFrame
    {
        public int Index;
        public bool Empty;

        public int Width;
        public int Height;

        // 绘制偏移：图锚点相对绘制位置（原 .Lib 头 X/Y）
        public int OffX;
        public int OffY;

        // 原 .Lib SX/SY（阴影相关，透传保留）
        public int SX;
        public int SY;
        public int Shadow;

        // 主图：图集页索引 + rect 左上角
        public int Page;
        public int X;
        public int Y;

        // mask：平行 mask 页同 rect，另附 MX/MY 相对偏移（无 mask 时 HasMask=false）
        public bool HasMask;
        public int MaskX;
        public int MaskY;
    }
}
