using Client.MirControls;

namespace Crystal.Client.Rendering
{
    // P2 sanduan CMain.cs OnGUI + TouchScreenKeyboard 软键盘桥（方法参考，RT 直绘替代 IMGUI）：
    //   sanduan 用 GUILayout.TextField + GUI.FocusControl 路由 → Keyboard.text 回写；
    //   本项目 ADR 禁 IMGUI，改用 MirTextBox.InputTextBox 纯 C# 输入模型 + 控件树 KeyPress 路由。
    // 本类为纯逻辑驱动核心（与 Unity TouchScreenKeyboard 经 ISoftKeyboard seam 解耦，可确定性探针）。
    // 用法：UI 代码在 MirTextBox 聚焦时调 SoftKeyboardBridge.Focus(box)，渲染层每帧调 Poll()。
    // 密码框 secure=true（TouchScreenKeyboard 原生掩码），Enter 提交 → ActiveBox.OnKeyPress(Enter)
    // → 控件树 KeyPress 订阅（ChatDialog/登录按钮等同链触发），Back/取消 → Unfocus。
    public interface ISoftKeyboard
    {
        string Text { get; }
        bool Active { get; }
        bool Submitted { get; }   // Enter/Done：提交本次文本
        bool Canceled { get; }    // 用户取消关闭
        void Open(string text, int maxLength, bool password);
        void Close();
    }

    public static class SoftKeyboardBridge
    {
        // 键盘实现 seam：默认 Unity 包装（TouchScreenKeyboard），探针注入 Fake 确定性驱动。
        public static ISoftKeyboard Keyboard;
        public static MirTextBox ActiveBox { get; private set; }

        // 绑定：打开软键盘（初始文本=框内文本，password/maxLength 走框属性）。已绑定时先解绑。
        public static void Focus(MirTextBox box)
        {
            if (box == null) return;
            Unfocus();
            ActiveBox = box;
            Keyboard?.Open(box.Text ?? string.Empty, box.MaxLength, box.Password);
        }

        // 每帧轮询（渲染层 Update 调用）：文本同步 → 提交/取消判定。
        public static void Poll()
        {
            if (ActiveBox == null || Keyboard == null) return;
            if (Keyboard.Canceled)
            {
                Unfocus();
                return;
            }
            if (Keyboard.Submitted)
            {
                SyncText();
                RaiseSubmit();
                Unfocus();
                return;
            }
            SyncText();
        }

        // 解绑：关软键盘、清活跃框（对话框 Hide / 焦点转移 / 返回键时调用）。
        public static void Unfocus()
        {
            if (ActiveBox == null) return;
            Keyboard?.Close();
            ActiveBox = null;
        }

        // 软键盘文本 → 输入模型（TextChanged → MirTextBox 重绘）。
        static void SyncText()
        {
            if (ActiveBox.Text != Keyboard.Text)
                ActiveBox.Text = Keyboard.Text;
        }

        // Enter 提交：投 KeyPress(Enter) 进控件树（触发 MirTextBox.OnKeyPress → 外部订阅）。
        static void RaiseSubmit()
        {
            ActiveBox.OnKeyPress(new KeyPressEventArgs((char)Keys.Enter));
        }
    }

    // Unity 默认实现：TouchScreenKeyboard 原生软键盘包装（secure 掩码走 TouchScreenKeyboard 的 secure 参数）。
    public class UnitySoftKeyboard : ISoftKeyboard
    {
        UnityEngine.TouchScreenKeyboard _kb;

        public string Text => _kb?.text ?? string.Empty;
        public bool Active => _kb?.active ?? false;
        public bool Submitted => _kb != null && _kb.status == UnityEngine.TouchScreenKeyboard.Status.Done;
        public bool Canceled => _kb != null && _kb.status == UnityEngine.TouchScreenKeyboard.Status.Canceled;

        public void Open(string text, int maxLength, bool password)
        {
            if (_kb != null && _kb.active) _kb.active = false;
            _kb = UnityEngine.TouchScreenKeyboard.Open(text,
                UnityEngine.TouchScreenKeyboardType.Default,
                !password, false, password, false, string.Empty);
        }

        public void Close()
        {
            if (_kb != null && _kb.active) _kb.active = false;
            _kb = null;
        }
    }
}
