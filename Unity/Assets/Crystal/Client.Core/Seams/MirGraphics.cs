using Crystal.Client.Core.MirMath;
using Client.MirObjects;

namespace Client.MirGraphics
{
    // MLibrary 的 Client.Core seam（占位）：签名对齐旧客户端（Client/MirGraphics/MLibrary.cs），空实现。
    // 真实渲染接驳（CrystalSpriteBatch + 图集）时替换空体，调用点不变。
    public class MLibrary
    {
        public MLibrary(string fileName) { }

        public FrameSet Frames;

        // 测试/验证可配置的模拟尺寸：GetSize 返回它（默认 Empty=0x0）。
        // 真实渲染接驳时由 AtlasLibrary 元数据驱动，本字段仅服务确定性探针。
        public Size ImageSize { get; set; } = Size.Empty;

        // 尺寸/偏移查询（HUD 控件布局依赖：MirImageControl.AutoSize→GetTrueSize、DisplayLocation→GetOffSet）。
        // 必须 virtual：MLibraryUnity 覆写为 Atlas.Frames 驱动（真实图元尺寸/偏移）。
        public virtual Point GetOffSet(int index) { return new Point(0, 0); }
        public virtual Size GetSize(int index) { return ImageSize; }
        public virtual Size GetTrueSize(int index) { return new Size(0, 0); }
        // 必须 virtual：MLibraryUnity 覆写为图集像素 alpha 检测（PixelDetect 控件命中测试依赖）。
        public virtual bool VisiblePixel(int index, Point location, bool useOffSet) { return false; }

        public void Draw(int index, int x, int y) { }
        public void Draw(int index, Point point, Color colour, bool offSet = false) { }
        public virtual void Draw(int index, Point point, Color colour, bool offSet, float opacity) { }
        public virtual void DrawBlend(int index, Point point, Color colour, bool offSet = false, float rate = 1) { }
        public void DrawTinted(int index, Point point, Color colour, Color tint, bool offSet = false) { }
        public void Draw(int index, Point point, Size size, Color colour) { }
        // source-rect 裁剪重载：HUD orb / exp 条（MainDialog.BeforeDraw）走此路径，
        // 必须 virtual 供 MLibraryUnity 覆写真实裁剪绘制（图集源矩形 → CrystalSpriteBatch）。
        public virtual void Draw(int index, Rectangle section, Point point, Color colour, bool offSet) { }
        public virtual void Draw(int index, Rectangle section, Point point, Color colour, float opacity) { }
    }

    // 库门面 seam：仅覆盖对象模型已引用的库，其余库在对应对象模型移植时按需加入。
    public static class Libraries
    {
        public static MLibrary
            Prguse = new MLibrary(Settings.DataPath + "Prguse"),
            Prguse2 = new MLibrary(Settings.DataPath + "Prguse2"),
            // 打孔镶嵌对话框（迭代包9，SocketDialog）：图集产物为 Prguse3（.Lib 原文件名）。
            Prguse3 = new MLibrary(Settings.DataPath + "Prguse3"),
            Magic = new MLibrary(Settings.DataPath + "Magic"),
            Magic2 = new MLibrary(Settings.DataPath + "Magic2"),
            Magic3 = new MLibrary(Settings.DataPath + "Magic3"),
            MagicC = new MLibrary(Settings.DataPath + "MagicC"),
            Dragon = new MLibrary(Settings.DataPath + "Dragon"),
            Effect = new MLibrary(Settings.DataPath + "Effect"),
            Weather = new MLibrary(Settings.DataPath + "Weather"),
            // 背包/装备/Tooltip 依赖库（迭代包2）。路径字符串须匹配 Build/assetcompile/all 图集文件名：
            // 图集产物为 Stateitem（.Lib 原文件名，单数 t），字段名沿用旧客户端 StateItems。
            Items = new MLibrary(Settings.DataPath + "Items"),
            StateItems = new MLibrary(Settings.DataPath + "Stateitem"),
            Title = new MLibrary(Settings.DataPath + "Title"),
            // 帮助窗口（迭代包10，HelpDialog）：图集产物为 Help（Build/assetcompile/all 已有）。
            Help = new MLibrary(Settings.DataPath + "Help"),
            // 负重条中高段素材（WeightBar_BeforeDraw）：图集产物为 UI（.Lib 原文件名），字段名沿用旧客户端 UI_32bit。
            UI_32bit = new MLibrary(Settings.DataPath + "UI"),
            // 技能/快捷栏/Buff 依赖库（迭代包4）：图集产物为 MagIcon/MagIcon2/BuffIcon，
            // 字段名与旧客户端 Libraries 一致（快捷栏图标 / 技能页大图标 / Buff 状态图标）。
            MagIcon = new MLibrary(Settings.DataPath + "MagIcon"),
            MagIcon2 = new MLibrary(Settings.DataPath + "MagIcon2"),
            BuffIcon = new MLibrary(Settings.DataPath + "BuffIcon"),
            // 大地图/小地图依赖库（迭代包5）：图集产物为 mmap（小地图瓦片图源）/MapLinkIcon（世界地图图标）。
            MiniMap = new MLibrary(Settings.DataPath + "mmap"),
            MapLinkIcon = new MLibrary(Settings.DataPath + "MapLinkIcon");

        // 玩家/英雄形象库数组：旧客户端在 static ctor 中 InitLibrary 填充，seam 保留空数组契约。
        public static MLibrary[] CArmours = new MLibrary[2048];
        public static MLibrary[] CWeapons = new MLibrary[2048];
        public static MLibrary[] CWeaponEffect = new MLibrary[2048];
        public static MLibrary[] CHair = new MLibrary[2048];
        public static MLibrary[] CHumEffect = new MLibrary[2048];
        public static MLibrary[] AArmours = new MLibrary[2048];
        public static MLibrary[] AWeaponsL = new MLibrary[2048];
        public static MLibrary[] AWeaponsR = new MLibrary[2048];
        public static MLibrary[] AHair = new MLibrary[2048];
        public static MLibrary[] AHumEffect = new MLibrary[2048];
        public static MLibrary[] ARArmours = new MLibrary[2048];
        public static MLibrary[] ARWeapons = new MLibrary[2048];
        public static MLibrary[] ARWeaponsS = new MLibrary[2048];
        public static MLibrary[] ARHair = new MLibrary[2048];
        public static MLibrary[] ARHumEffect = new MLibrary[2048];
        public static MLibrary[] Monsters = new MLibrary[2048];
        public static MLibrary[] Flags = new MLibrary[2048];
        public static MLibrary[] NPCs = new MLibrary[2048];
        public static MLibrary[] Gates = new MLibrary[256];
        public static MLibrary[] Siege = new MLibrary[256];
        public static MLibrary[] Mounts = new MLibrary[256];
        public static MLibrary[] Fishing = new MLibrary[256];
        public static MLibrary[] Pets = new MLibrary[256];
        public static MLibrary[] Transform = new MLibrary[2048];
        public static MLibrary[] TransformMounts = new MLibrary[2048];
        public static MLibrary[] TransformEffect = new MLibrary[2048];
    }
}
