namespace Crystal.Client.Rendering
{
    // Android 连接配置（生成源）：BuildAndroid.Run 每次构建按 env 重写（CRYSTAL_NET_HOST/PORT/LOGIN_ID/LOGIN_PW），
    // env 缺省时回落到此提交值（= androidverify 默认：模拟器 10.0.2.2 → 宿主服务端）。
    // 静态字段的初始化值在 Player 构建时编译固化，Editor 运行时赋值不进产物，故走生成源注入。
    static class MobileConfig
    {
        public const string NetHost = "10.0.2.2";
        public const int NetPort = 7000;
        public const string LoginId = "pcplayer";
        public const string LoginPw = "pcplayer";
    }
}
