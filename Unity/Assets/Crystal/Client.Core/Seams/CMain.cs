using Crystal.Client.Core.MirMath;
using Client.MirControls;

namespace Client
{
    // Crystal.Client.Core 的 CMain seam（占位）：满足已移植文件的时钟/错误日志/绘制上下文/输入修饰键契约。
    // 真实 CMain 落地时替换。
    public static class CMain
    {
        public static long Time;
        public static long BytesReceived, BytesSent;
        public static System.Random Random = new System.Random();
        public static DateTime StartTime = DateTime.Now;
        public static DateTime Now { get { return StartTime.AddMilliseconds(Time); } }
        public static MirGraphics.Graphics Graphics;
        public static Point MPoint;
        public static bool Shift, Ctrl, Alt, Tilde;
        // 按键绑定表（迭代包10，KeyBindSettings.cs seam）：KeyboardLayoutDialog 读写，旧客户端 CMain.InputKeys 同源。
        public static KeyBindSettings InputKeys = new KeyBindSettings();
        public static void SaveError(string msg) { }

        // 日志 seam：旧客户端的 Console/Debug 输出统一走此钩子，渲染层注入 LogImpl=Debug.Log 还原。
        public static System.Action<string> LogImpl;
        public static void Log(string msg) { if (LogImpl != null) LogImpl(msg); }

        // 渲染层输入接管将 OS 事件转成控件事件后，在此汇聚全局键/鼠钩子（旧客户端 CMain_KeyDown 等）。
        public static void CMain_KeyUp(object sender, KeyEventArgs e) { }
        public static void CMain_KeyDown(object sender, KeyEventArgs e) { }
        public static void CMain_MouseMove(object sender, MouseEventArgs e) { }
    }
}
