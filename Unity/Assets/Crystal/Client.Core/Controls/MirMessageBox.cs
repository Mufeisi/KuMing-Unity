using Client.MirGraphics;
using Client.MirScenes;
using Crystal.Client.Core.MirMath;

namespace Client.MirControls
{
    // 逐字移植（2026-08-06）：Client/MirControls/MirMessageBox.cs
    // 模态消息框：MirMessageBoxButtons 五档按钮布局（OK/OKCancel/YesNo/YesNoCancel/Cancel）+
    // Esc/Enter 键盘路由 + 静态 Show 便捷入口。
    // 裁剪：Show/Dispose 里 Program.Form.Controls + MirTextBox.DialogChanged（WinForms 原生表单，
    // Unity 无对应）；Show 挂载 MirScene.ActiveScene → GameScene.Scene；静态 Show 的 close 参数（关宿主）空操作。
    public enum MirMessageBoxButtons { OK, OKCancel, YesNo, YesNoCancel, Cancel }

    public sealed class MirMessageBox : MirImageControl
    {
        public MirLabel Label;
        public MirButton OKButton, CancelButton, NoButton, YesButton;
        public MirMessageBoxButtons Buttons;
        public bool AllowKeyPress = true;

        public MirMessageBox(string message, MirMessageBoxButtons b = MirMessageBoxButtons.OK, bool allowKeys = true)
        {
            DrawImage = true;
            ForeColour = Color.White;
            Buttons = b;
            Modal = true;
            Movable = false;
            AllowKeyPress = allowKeys;

            Index = 360;
            Library = Libraries.Prguse;

            Location = new Point((Settings.ScreenWidth - Size.Width) / 2, (Settings.ScreenHeight - Size.Height) / 2);

            Label = new MirLabel
            {
                AutoSize = false,
                Location = new Point(35, 35),
                Size = new Size(390, 110),
                Parent = this,
                Text = message
            };

            switch (Buttons)
            {
                case MirMessageBoxButtons.OK:
                    OKButton = new MirButton
                    {
                        HoverIndex = 201,
                        Index = 200,
                        Library = Libraries.Title,
                        Location = new Point(360, 157),
                        Parent = this,
                        PressedIndex = 202,
                    };
                    OKButton.Click += (o, e) => Dispose();
                    break;
                case MirMessageBoxButtons.OKCancel:
                    OKButton = new MirButton
                    {
                        HoverIndex = 201,
                        Index = 200,
                        Library = Libraries.Title,
                        Location = new Point(260, 157),
                        Parent = this,
                        PressedIndex = 202,
                    };
                    OKButton.Click += (o, e) => Dispose();
                    CancelButton = new MirButton
                    {
                        HoverIndex = 204,
                        Index = 203,
                        Library = Libraries.Title,
                        Location = new Point(360, 157),
                        Parent = this,
                        PressedIndex = 205,
                    };
                    CancelButton.Click += (o, e) => Dispose();
                    break;
                case MirMessageBoxButtons.YesNo:
                    YesButton = new MirButton
                    {
                        HoverIndex = 207,
                        Index = 206,
                        Library = Libraries.Title,
                        Location = new Point(260, 157),
                        Parent = this,
                        PressedIndex = 208,
                    };
                    YesButton.Click += (o, e) => Dispose();
                    NoButton = new MirButton
                    {
                        HoverIndex = 211,
                        Index = 210,
                        Library = Libraries.Title,
                        Location = new Point(360, 157),
                        Parent = this,
                        PressedIndex = 212,
                    };
                    NoButton.Click += (o, e) => Dispose();
                    break;
                case MirMessageBoxButtons.YesNoCancel:
                    YesButton = new MirButton
                    {
                        HoverIndex = 207,
                        Index = 206,
                        Library = Libraries.Title,
                        Location = new Point(160, 157),
                        Parent = this,
                        PressedIndex = 208,
                    };
                    YesButton.Click += (o, e) => Dispose();
                    NoButton = new MirButton
                    {
                        HoverIndex = 211,
                        Index = 210,
                        Library = Libraries.Title,
                        Location = new Point(260, 157),
                        Parent = this,
                        PressedIndex = 212,
                    };
                    NoButton.Click += (o, e) => Dispose();
                    CancelButton = new MirButton
                    {
                        HoverIndex = 204,
                        Index = 203,
                        Library = Libraries.Title,
                        Location = new Point(360, 157),
                        Parent = this,
                        PressedIndex = 205,
                    };
                    CancelButton.Click += (o, e) => Dispose();
                    break;
                case MirMessageBoxButtons.Cancel:
                    CancelButton = new MirButton
                    {
                        HoverIndex = 204,
                        Index = 203,
                        Library = Libraries.Title,
                        Location = new Point(360, 157),
                        Parent = this,
                        PressedIndex = 205,
                    };
                    CancelButton.Click += (o, e) => Dispose();
                    break;
            }
        }

        public override void Show()
        {
            if (Parent != null) return;

            Parent = GameScene.Scene;

            Highlight();

            // 裁剪：旧客户端同步 WinForms TextBox 输入状态（Program.Form.Controls + DialogChanged），Unity 无原生表单。
        }

        public override void OnKeyDown(KeyEventArgs e)
        {
            if (AllowKeyPress)
            {
                base.OnKeyDown(e);
                e.Handled = true;
            }
        }
        public override void OnKeyUp(KeyEventArgs e)
        {
            if (AllowKeyPress)
            {
                base.OnKeyUp(e);
                e.Handled = true;
            }
        }
        public override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);

            if (AllowKeyPress)
            {
                if (e.KeyChar == (char)Keys.Escape)
                {
                    switch (Buttons)
                    {
                        case MirMessageBoxButtons.OK:
                            if (OKButton != null && !OKButton.IsDisposed) OKButton.InvokeMouseClick(null);
                            break;
                        case MirMessageBoxButtons.OKCancel:
                        case MirMessageBoxButtons.YesNoCancel:
                            if (CancelButton != null && !CancelButton.IsDisposed) CancelButton.InvokeMouseClick(null);
                            break;
                        case MirMessageBoxButtons.YesNo:
                            if (NoButton != null && !NoButton.IsDisposed) NoButton.InvokeMouseClick(null);
                            break;
                    }
                }

                else if (e.KeyChar == (char)Keys.Enter)
                {
                    switch (Buttons)
                    {
                        case MirMessageBoxButtons.OK:
                        case MirMessageBoxButtons.OKCancel:
                            if (OKButton != null && !OKButton.IsDisposed) OKButton.InvokeMouseClick(null);
                            break;
                        case MirMessageBoxButtons.YesNoCancel:
                        case MirMessageBoxButtons.YesNo:
                            if (YesButton != null && !YesButton.IsDisposed) YesButton.InvokeMouseClick(null);
                            break;

                    }
                }
                e.Handled = true;
            }
        }

        public static void Show(string message, bool close = false)
        {
            MirMessageBox box = new MirMessageBox(message);

            // 裁剪：close 时旧客户端 Program.Form.Close()（Unity 无原生表单），探针无需关闭宿主。

            box.Show();
        }

        #region Disposable

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing) return;

            Label = null;
            OKButton = null;
            CancelButton = null;
            NoButton = null;
            YesButton = null;
            Buttons = 0;

            // 裁剪：旧客户端同步 WinForms TextBox 输入状态（Program.Form.Controls + DialogChanged），Unity 无原生表单。
        }

        #endregion
    }
}
