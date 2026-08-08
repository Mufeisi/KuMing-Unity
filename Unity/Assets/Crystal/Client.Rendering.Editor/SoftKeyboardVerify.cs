using System;
using Client.MirControls;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // P2 sanduan 软键盘桥（CMain OnGUI + TouchScreenKeyboard）方法参考的确定性验证：
    // SoftKeyboardBridge 纯逻辑驱动核心 + ISoftKeyboard seam（Fake 键盘注入），不经 IMGUI。
    // 断言：绑定开键盘（初始文本/密码/最大长度透传）、轮询文本同步、Enter 提交→KeyPress(Enter)
    // 进控件树+解绑、取消/解绑关键盘、重复绑定先解旧。batchmode：-executeMethod ...Run -quit
    // 全过输出 [softkeyboardverify] PASS exit 0。
    static class SoftKeyboardVerify
    {
        class FakeKeyboard : ISoftKeyboard
        {
            public string Text { get; set; } = string.Empty;
            public bool Active { get; set; }
            public bool Submitted { get; set; }
            public bool Canceled { get; set; }
            public string OpenedText; public int OpenedMax; public bool OpenedPassword;
            public int OpenCount, CloseCount;

            public void Open(string text, int maxLength, bool password)
            {
                OpenedText = text; OpenedMax = maxLength; OpenedPassword = password;
                OpenCount++; Active = true; Submitted = Canceled = false;
            }
            public void Close() { CloseCount++; Active = false; }
        }

        static int _fail;

        static void Check(bool cond, string name)
        {
            if (cond) { Console.WriteLine($"  ok  {name}"); }
            else { _fail++; Console.WriteLine($"  FAIL {name}"); }
        }

        static MirTextBox NewBox(string text, int maxLength = 32767, bool password = false)
        {
            var box = new MirTextBox { Visible = true, Enabled = true };
            box.MaxLength = maxLength;
            box.Password = password;
            box.Text = text;
            return box;
        }

        public static void Run()
        {
            var fake = new FakeKeyboard();
            SoftKeyboardBridge.Keyboard = fake;
            SoftKeyboardBridge.Unfocus();
            try
            {
                int cases = 0;

                // 1. 绑定开键盘：初始文本/密码/最大长度透传 + ActiveBox 记录
                cases++;
                var box1 = NewBox("abc", 10);
                SoftKeyboardBridge.Focus(box1);
                Check(fake.OpenCount == 1 && fake.OpenedText == "abc" && fake.OpenedMax == 10
                      && !fake.OpenedPassword && SoftKeyboardBridge.ActiveBox == box1, "focus opens keyboard with box state");

                // 2. 轮询文本同步：软键盘文本 → 输入模型
                cases++;
                fake.Text = "abcxyz";
                SoftKeyboardBridge.Poll();
                Check(box1.Text == "abcxyz", "poll syncs text to input model");

                // 3. 密码框：password 透传
                cases++;
                var boxPw = NewBox("", 8, true);
                SoftKeyboardBridge.Focus(boxPw);
                Check(fake.OpenedPassword && fake.OpenedMax == 8, "password box opens secure keyboard");

                // 4. Enter 提交：KeyPress(Enter) 进控件树 + 文本保留 + 解绑
                cases++;
                char captured = '\0';
                boxPw.TextBox.KeyPress += (s, e) => captured = e.KeyChar;
                fake.Text = "hunter2";
                fake.Submitted = true;
                SoftKeyboardBridge.Poll();
                Check(captured == (char)Keys.Enter && boxPw.Text == "hunter2"
                      && SoftKeyboardBridge.ActiveBox == null && fake.CloseCount > 0, "submit raises KeyPress(Enter) and unbinds");

                // 5. 取消：关闭解绑不提交
                cases++;
                int closeBefore = fake.CloseCount;
                SoftKeyboardBridge.Focus(box1);
                fake.Canceled = true;
                SoftKeyboardBridge.Poll();
                Check(SoftKeyboardBridge.ActiveBox == null && fake.CloseCount == closeBefore + 1, "cancel closes and unbinds");

                // 6. 显式解绑
                cases++;
                closeBefore = fake.CloseCount;
                SoftKeyboardBridge.Focus(box1);
                SoftKeyboardBridge.Unfocus();
                Check(SoftKeyboardBridge.ActiveBox == null && fake.CloseCount == closeBefore + 1, "unfocus closes keyboard");

                // 7. 重复绑定：先解旧再绑新
                cases++;
                var box2 = NewBox("second", 20);
                SoftKeyboardBridge.Focus(box1);
                int opens = fake.OpenCount;
                SoftKeyboardBridge.Focus(box2);
                Check(fake.OpenCount == opens + 1 && fake.OpenedText == "second" && fake.OpenedMax == 20
                      && SoftKeyboardBridge.ActiveBox == box2, "refocus unbinds old and binds new");

                // 8. null 框安全
                cases++;
                SoftKeyboardBridge.Focus(null);
                Check(SoftKeyboardBridge.ActiveBox == box2, "null box is ignored");

                Console.WriteLine($"softkeyboard-verify: cases={cases} fail={_fail}");
                if (_fail == 0)
                {
                    Console.WriteLine("[softkeyboardverify] PASS cases=" + cases);
                    EditorApplication.Exit(0);
                }
                else
                {
                    Console.WriteLine($"[softkeyboardverify] FAIL cases={cases} fail={_fail}");
                    EditorApplication.Exit(1);
                }
            }
            finally
            {
                SoftKeyboardBridge.Unfocus();
                SoftKeyboardBridge.Keyboard = null;
            }
        }
    }
}
