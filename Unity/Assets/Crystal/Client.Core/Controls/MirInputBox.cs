using Client.MirGraphics;
using Client.MirScenes;
using Crystal.Client.Core.MirMath;

namespace Client.MirControls
{
    // 逐字移植（2026-08-07）：Client/MirControls/MirInputBox.cs
    // 模态文本输入框：Prguse 660 框 + 标题 + 单行 MirTextBox + OK/Cancel 按钮。
    // OKButton.Click 由调用方挂动作（如组队 Add/Del 发包），Enter/Esc 键盘路由；
    // 移动端经 TouchInputAdapter 鼠标链点击 + 软键盘桥（SoftKeyboardBridge）输入/提交。
    // 裁剪：Show/Dispose 里 Program.Form.Controls + MirTextBox.DialogChanged（WinForms 原生表单，
    // Unity 无对应）；Show 挂载 MirScene.ActiveScene → GameScene.Scene（同 MirMessageBox 移植）。
    public sealed class MirInputBox : MirImageControl
    {
        public readonly MirLabel CaptionLabel;
        public readonly MirButton OKButton, CancelButton;
        public readonly MirTextBox InputTextBox;

        public MirInputBox(string message)
        {
            Modal = true;
            Movable = false;

            Index = 660;
            Library = Libraries.Prguse;

            Location = new Point((Settings.ScreenWidth - Size.Width) / 2, (Settings.ScreenHeight - Size.Height) / 2);

            CaptionLabel = new MirLabel
            {
                DrawFormat = TextFormatFlags.WordBreak,
                Location = new Point(25, 25),
                Size = new Size(235, 40),
                Parent = this,
                Text = message,
            };

            InputTextBox = new MirTextBox
            {
                Parent = this,
                Border = true,
                BorderColour = Color.Lime,
                Location = new Point(23, 86),
                Size = new Size(240, 19),
                MaxLength = 50,
            };
            InputTextBox.SetFocus();
            InputTextBox.TextBox.KeyPress += MirInputBox_KeyPress;

            OKButton = new MirButton
            {
                HoverIndex = 201,
                Index = 200,
                Library = Libraries.Title,
                Location = new Point(60, 123),
                Parent = this,
                PressedIndex = 202,
            };

            CancelButton = new MirButton
            {
                HoverIndex = 204,
                Index = 203,
                Library = Libraries.Title,
                Location = new Point(160, 123),
                Parent = this,
                PressedIndex = 205,
            };
            CancelButton.Click += DisposeDialog;
        }

        void MirInputBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                if (OKButton != null && !OKButton.IsDisposed)
                    OKButton.InvokeMouseClick(EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.KeyChar == (char)Keys.Escape)
            {
                if (CancelButton != null && !CancelButton.IsDisposed)
                    CancelButton.InvokeMouseClick(EventArgs.Empty);
                e.Handled = true;
            }
        }
        void DisposeDialog(object sender, EventArgs e)
        {
            Dispose();
        }

        public override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            e.Handled = true;
        }
        public override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            e.Handled = true;
        }
        public override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);

            if (e.KeyChar == (char)Keys.Escape)
            {
                if (CancelButton != null && !CancelButton.IsDisposed)
                    CancelButton.InvokeMouseClick(EventArgs.Empty);
            }
            else if (e.KeyChar == (char)Keys.Enter)
            {
                if (OKButton != null && !OKButton.IsDisposed)
                    OKButton.InvokeMouseClick(EventArgs.Empty);
            }
            e.Handled = true;
        }

        public override void Show()
        {
            if (Parent != null) return;

            Parent = GameScene.Scene;

            Highlight();
        }

        #region Disposable

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing) return;

            // 裁剪：旧客户端同步 WinForms TextBox 输入状态（Program.Form.Controls + DialogChanged），Unity 无原生表单。
        }

        #endregion
    }
}
