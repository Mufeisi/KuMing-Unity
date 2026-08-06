namespace Client
{
    // Crystal.Client.Core 的配置 seam（占位）：满足已移植文件的最小契约。
    // 真实 Settings 落地时替换，契约保持 SoundPath 等静态成员。
    public static class Settings
    {
        public const string DataPath = @".\Data\";
        public static string SoundPath = @".\Sound\";
        public static string FontName = "Arial";
        public static float FontSize = 8F; // 邮件/市场面板标题字号（MailDialogs/TrustMerchant，旧客户端同源默认）
        public static bool LogErrors = false;
        public static string IPAddress = "127.0.0.1";
        public static int Port = 7000;
        public const int TimeOut = 5000;
        public static bool Effect = true;
        public static bool LevelEffect = true;
        public static int ScreenWidth = 1024;
        public static int ScreenHeight = 768;
        public const long CleanDelay = 600000;
        public static int Resolution = 1024;      // 旧客户端取值 800/1024/1366，控件据此选图集序号
        public static bool HPView = true;          // HUD HP/MP 数值标签显示开关
        public static bool ModeView = false;       // 攻击/宠物/技能模式标签显示开关
        public static bool TargetDead = false;     // @TARGETDEAD 聊天开关
        public static bool SkillMode = false;      // 技能模式标签
        public static bool SkillBar = true;        // 快捷栏可见开关（SkillBarDialog Show/Hide）
        // 两条快捷栏的持久化位置（旧客户端 Settings.cs:163 同源默认值）。
        public static int[,] SkillbarLocation = new int[2, 2] { { 0, 0 }, { 216, 0 } };
        public static bool ExpandedBuffWindow = true; // Buff 窗展开状态（BuffDialog 委托注入目标）

        public static bool FilterNormalChat, FilterWhisperChat, FilterShoutChat, FilterSystemChat,
            FilterLoverChat, FilterMentorChat, FilterGroupChat, FilterGuildChat;
        // 透明聊天窗开关（迭代包10，ChatOptionDialog.UpdateTransparency 读写，旧客户端 Settings.cs 同源）。
        public static bool TransparentChat;

        public static void LoadTrackedQuests(string charName) { }
        public static void SaveTrackedQuests(string charName) { }

        // 追踪任务列表（QuestTrackingDialog.UpdateTrackedQuests 读写，旧客户端 Settings.cs:166 同源）。
        // 初始 0 由探针显式置 -1（无追踪标记）；真实 Save/Load 由 QuestTrackingReader 持久化，seam 空实现。
        public static int[] TrackedQuests = new int[5];
    }
}
