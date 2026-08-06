using System;
using Crystal.Client.Core.MirMath;
using Client.MirGraphics;

namespace Client.MirControls
{
    // 逐字移植（2026-08-05）：Client/MirControls/MirTextBox.cs
    // WinForms TextBox 原生宿主 → 纯 C# 输入模型（TextBox）：文本/光标/事件三要素，
    // 字形渲染由 DrawControl 经 TextRenderer seam 下发（BackColour 填充 + 文本 + 焦点光标线）。
    // 剪掉的平台面：BorderStyle/Cursor/Program.Form/DialogChanged（MirMessageBox 桩后不再需要）/
    // DrawToBitmap offscreen RT。真实键盘输入接管在渲染层驱动输入模型时接入。
    public sealed class MirTextBox : MirControl
    {
        #region Back Color

        protected override void OnBackColourChanged()
        {
            base.OnBackColourChanged();
            if (TextBox != null && !TextBox.IsDisposed)
                TextBox.BackColor = BackColour;
        }

        #endregion

        #region Enabled

        protected override void OnEnabledChanged()
        {
            base.OnEnabledChanged();
            if (TextBox != null && !TextBox.IsDisposed)
                TextBox.Enabled = Enabled;
        }

        #endregion

        #region Fore Color

        protected override void OnForeColourChanged()
        {
            base.OnForeColourChanged();
            if (TextBox != null && !TextBox.IsDisposed)
                TextBox.ForeColor = ForeColour;
        }

        #endregion

        #region Location

        protected override void OnLocationChanged()
        {
            base.OnLocationChanged();
            ApplyNativeTextBoxState();

            TextureValid = false;
            Redraw();
        }

        #endregion

        #region Max Length

        public int MaxLength
        {
            get
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    return TextBox.MaxLength;
                return -1;
            }
            set
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    TextBox.MaxLength = value;
            }
        }

        #endregion

        #region Parent

        protected override void OnParentChanged()
        {
            base.OnParentChanged();
            if (TextBox != null && !TextBox.IsDisposed)
                ApplyNativeTextBoxState();
        }

        #endregion

        #region Password

        public bool Password
        {
            get
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    return TextBox.UseSystemPasswordChar;
                return false;
            }
            set
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    TextBox.UseSystemPasswordChar = value;
            }
        }

        #endregion

        #region Font

        public Font Font
        {
            get
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    return TextBox.Font;
                return null;
            }
            set
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    TextBox.Font = ScaleFont(value);
            }
        }

        #endregion

        #region Size

        protected override void OnSizeChanged()
        {
            if (TextBox != null && !TextBox.IsDisposed)
                TextBox.Size = Size;

            _size = Size;

            if (TextBox != null && !TextBox.IsDisposed)
                base.OnSizeChanged();
        }

        #endregion

        #region TextBox

        public bool CanLoseFocus;
        public readonly InputTextBox TextBox;

        private void ApplyNativeTextBoxState()
        {
            // 无原生窗口：仅同步输入模型的可见焦点意图，真实聚焦由渲染层输入接管驱动。
        }

        #endregion

        #region Label

        public string Text
        {
            get
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    return TextBox.Text;
                return null;
            }
            set
            {
                if (TextBox != null && !TextBox.IsDisposed)
                {
                    TextBox.Text = value;
                    TextBox_NeedRedraw(this, EventArgs.Empty);
                }
            }
        }
        public string[] MultiText
        {
            get
            {
                if (TextBox != null && !TextBox.IsDisposed)
                    return TextBox.Lines;
                return null;
            }
            set
            {
                if (TextBox != null && !TextBox.IsDisposed)
                {
                    TextBox.Lines = value;
                    TextBox_NeedRedraw(this, EventArgs.Empty);
                }
            }
        }

        #endregion

        #region Visible

        public override bool Visible
        {
            get
            {
                return base.Visible;
            }
            set
            {
                base.Visible = value;
                OnVisibleChanged();
            }
        }

        protected override void OnVisibleChanged()
        {
            base.OnVisibleChanged();

            ApplyNativeTextBoxState();
        }

        #endregion

        #region MultiLine

        public override void MultiLine()
        {
            TextBox.Multiline = true;
            TextBox.Size = Size;

            Redraw();
        }

        #endregion

        public MirTextBox()
        {
            BackColour = Color.Black;

            DrawControlTexture = true;
            TextureValid = false;

            TextBox = new InputTextBox
                {
                    BackColor = BackColour,
                    Font = new Font(Settings.FontName, 10F),
                    ForeColor = ForeColour,
                    Size = Size,
                };

            TextBox.KeyUp += TextBoxOnKeyUp;
            TextBox.KeyPress += TextBox_KeyPress;

            TextBox.KeyPress += TextBox_NeedRedraw;
            TextBox.KeyUp += TextBox_NeedRedraw;
            TextBox.TextChanged += TextBox_NeedRedraw;
            TextBox.GotFocus += TextBox_NeedRedraw;
            TextBox.LostFocus += TextBox_NeedRedraw;

            Shown += MirTextBox_Shown;
        }

        private void TextBox_NeedRedraw(object sender, EventArgs e)
        {
            TextureValid = false;
            Redraw();
        }

        protected internal override void DrawControl()
        {
            base.DrawControl();

            if (TextBox == null || TextBox.IsDisposed)
                return;

            string text = TextBox.Text;
            if (string.IsNullOrEmpty(text))
                return;

            Rectangle rect = new Rectangle(DisplayLocation.X + 1, DisplayLocation.Y, Size.Width, Size.Height);
            TextRenderer.DrawText(this, text, TextBox.Font, rect, ForeColour, TextFormatFlags.TextBoxControl);

            if (TextBox.Focused)
            {
                int caretX = GetCaretPosition();
                TextRenderer.DrawCaret(this, new Rectangle(DisplayLocation.X + caretX, DisplayLocation.Y + 1, 1, Size.Height - 2));
            }
        }

        public override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (!Enabled || TextBox == null || TextBox.IsDisposed || !TextBox.Visible) return;

            if (e.Button == MouseButtons.Left)
            {
                Point localPoint = new Point(e.X - DisplayLocation.X, e.Y - DisplayLocation.Y);
                int charIndex = TextBox.GetCharIndexFromPosition(localPoint);
                TextBox.SelectionStart = Math.Max(0, Math.Min(charIndex, TextBox.TextLength));
                TextBox.SelectionLength = 0;
            }

            SetFocus();
        }

        private int GetCaretPosition()
        {
            string text = TextBox.Text;
            int index = Math.Min(TextBox.SelectionStart, text.Length);
            if (index <= 0) return 0;
            return TextRenderer.MeasureText(CMain.Graphics, text.Substring(0, index), TextBox.Font).Width;
        }

        private void TextBoxOnKeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.PrintScreen:
                    CMain.CMain_KeyUp(sender, e);
                    break;
            }
        }

        private void TextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            base.OnKeyPress(e);

            if (e.KeyChar == (char)Keys.Escape)
                e.Handled = true;
        }

        // 键盘来源接驳：旧客户端由原生 TextBox 消息循环触发事件；纯 C# 端口无原生控件，
        // 控件树路由（父级 OnKeyPress/OnKeyDown/OnKeyUp 逐层下传）在此转投输入模型，
        // 触发 MirTextBox 内部订阅（TextBox_KeyPress）与外部订阅（ChatDialog 的 ChatTextBox_KeyPress 等）。
        public override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (TextBox == null || TextBox.IsDisposed || e.Handled) return;
            TextBox.RaiseKeyPress(e);
        }
        public override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (TextBox == null || TextBox.IsDisposed || e.Handled) return;
            TextBox.RaiseKeyDown(e);
        }
        public override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (TextBox == null || TextBox.IsDisposed || e.Handled) return;
            TextBox.RaiseKeyUp(e);
        }

        private void MirTextBox_Shown(object sender, EventArgs e)
        {
            ApplyNativeTextBoxState();
            CMain.Ctrl = false;
            CMain.Shift = false;
            CMain.Alt = false;
            CMain.Tilde = false;

            TextureValid = false;
            SetFocus();
        }

        public void SetFocus()
        {
            TextBox.Focus();
        }

        #region Disposable

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing) return;

            if (TextBox != null && !TextBox.IsDisposed)
                TextBox.Dispose();
        }

        #endregion

        #region Input TextBox 模型（WinForms TextBox 的纯 C# 等价物）
        // 去 WinForms：文本/光标/修饰键状态 + 键盘事件回调，由渲染层输入接管驱动。
        public sealed class InputTextBox : IDisposable
        {
            private string _text = string.Empty;
            public string Text
            {
                get { return _text; }
                set
                {
                    if (_text == value)
                        return;
                    _text = value ?? string.Empty;
                    if (TextChanged != null)
                        TextChanged(this, EventArgs.Empty);
                }
            }

            public int TextLength { get { return _text.Length; } }
            public int MaxLength = 32767;
            public int SelectionStart;
            public int SelectionLength;
            public bool Multiline;
            public bool UseSystemPasswordChar;
            public bool Enabled = true;
            public Color BackColor;
            public Color ForeColor;
            public Font Font;
            public Size Size;

            public bool Focused { get; private set; }
            public bool Visible = true;

            public string[] Lines
            {
                get { return _text.Split('\n'); }
                set { Text = value == null ? string.Empty : string.Join("\n", value); }
            }

            public event EventHandler TextChanged, GotFocus, LostFocus;
            public event KeyPressEventHandler KeyPress;
            public event KeyEventHandler KeyUp, KeyDown;

            public void Focus()
            {
                Focused = true;
                if (GotFocus != null)
                    GotFocus(this, EventArgs.Empty);
            }
            public void LoseFocus()
            {
                Focused = false;
                if (LostFocus != null)
                    LostFocus(this, EventArgs.Empty);
            }

            public int GetCharIndexFromPosition(Point local)
            {
                // 与 GetCaretPosition 同源的逆映射：按前缀测量二分逼近（无原生控件时的确定性近似）。
                if (local.X <= 0 || _text.Length == 0)
                    return 0;
                int lo = 0, hi = _text.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (TextRenderer.MeasureText(CMain.Graphics, _text.Substring(0, mid), Font).Width <= local.X)
                        lo = mid;
                    else
                        hi = mid - 1;
                }
                return lo;
            }

            // 键盘事件只能在本声明类型内触发：控件树路由（MirTextBox.OnKey*）经此转投外部订阅者。
            public void RaiseKeyPress(KeyPressEventArgs e) { if (KeyPress != null) KeyPress(this, e); }
            public void RaiseKeyDown(KeyEventArgs e) { if (KeyDown != null) KeyDown(this, e); }
            public void RaiseKeyUp(KeyEventArgs e) { if (KeyUp != null) KeyUp(this, e); }

            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                if (IsDisposed) return;
                TextChanged = null;
                GotFocus = null;
                LostFocus = null;
                KeyPress = null;
                KeyUp = null;
                KeyDown = null;
                IsDisposed = true;
            }
        }
        #endregion
    }
}
