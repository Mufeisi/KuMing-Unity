using System;
using Crystal.Client.Core.MirMath;
using Client.MirGraphics;

namespace Client.MirControls
{
    // 逐字移植（2026-08-05）：Client/MirControls/MirLabel.cs
    // 去 GDI+/SlimDX offscreen RT：CreateTexture（BackColour 清屏 + 5×DrawText）改为
    // DrawControl 直接经 TextRenderer seam 下发（同一 5 次偏移语义：描边 1,0/0,1/2,1/1,2 + 前景 1,1；
    // 无描边仍偏移 (1,0)，与旧代码 Rectangle(1,0,..) 对齐）。真实字形栅格化由渲染层
    // TextRenderer.DrawTextImpl 注入（R8 动态字体管线）。
    public class MirLabel : MirControl
    {
        #region Auto Size
        private bool _autoSize;
        public bool AutoSize
        {
            get { return _autoSize; }
            set
            {
                if (_autoSize == value)
                    return;
                _autoSize = value;
                OnAutoSizeChanged(EventArgs.Empty);
            }
        }
        public event EventHandler AutoSizeChanged;
        private void OnAutoSizeChanged(EventArgs e)
        {
            TextureValid = false;
            GetSize();
            if (AutoSizeChanged != null)
                AutoSizeChanged.Invoke(this, e);
        }
        #endregion

        #region DrawFormat
        private TextFormatFlags _drawFormat;
        public TextFormatFlags DrawFormat
        {
            get { return _drawFormat; }
            set
            {
                _drawFormat = value;
                OnDrawFormatChanged(EventArgs.Empty);
            }
        }
        public event EventHandler DrawFormatChanged;
        private void OnDrawFormatChanged(EventArgs e)
        {
            TextureValid = false;

            if (DrawFormatChanged != null)
                DrawFormatChanged.Invoke(this, e);
        }
        #endregion

        #region Font
        private Font _font;
        public Font Font
        {
            get { return _font; }
            set
            {
                _font = ScaleFont(value);
                OnFontChanged(EventArgs.Empty);
            }
        }
        public event EventHandler FontChanged;
        private void OnFontChanged(EventArgs e)
        {
            TextureValid = false;

            GetSize();

            if (FontChanged != null)
                FontChanged.Invoke(this, e);
        }
        #endregion

        #region Out Line
        private bool _outLine;
        public bool OutLine
        {
            get { return _outLine; }
            set
            {
                if (_outLine == value)
                    return;
                _outLine = value;
                OnOutLineChanged(EventArgs.Empty);
            }
        }
        public event EventHandler OutLineChanged;
        private void OnOutLineChanged(EventArgs e)
        {
            TextureValid = false;
            GetSize();

            if (OutLineChanged != null)
                OutLineChanged.Invoke(this, e);
        }
        #endregion

        #region Out Line Colour
        private Color _outLineColour;
        public Color OutLineColour
        {
            get { return _outLineColour; }
            set
            {
                if (_outLineColour == value)
                    return;
                _outLineColour = value;
                OnOutLineColourChanged();
            }
        }
        public event EventHandler OutLineColourChanged;
        private void OnOutLineColourChanged()
        {
            TextureValid = false;

            if (OutLineColourChanged != null)
                OutLineColourChanged.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Size
        private void GetSize()
        {
            if (!AutoSize)
                return;

            if (string.IsNullOrEmpty(_text))
                Size = Size.Empty;
            else
            {
                Size = TextRenderer.MeasureText(CMain.Graphics, Text, Font);

                if (OutLine && Size != Size.Empty)
                    Size = new Size(Size.Width + 2, Size.Height + 2);
            }
        }
        #endregion

        #region Label
        private string _text;
        public string Text
        {
            get { return _text; }
            set
            {
                if (_text == value)
                    return;

                _text = value;
                OnTextChanged(EventArgs.Empty);
            }
        }
        public event EventHandler TextChanged;
        private void OnTextChanged(EventArgs e)
        {
            DrawControlTexture = !string.IsNullOrEmpty(Text);
            TextureValid = false;
            Redraw();

            GetSize();

            if (TextChanged != null)
                TextChanged.Invoke(this, e);
        }
        #endregion

        public MirLabel()
        {
            DrawControlTexture = true;
            _drawFormat = TextFormatFlags.WordBreak;

            _font = ScaleFont(new Font(Settings.FontName, 8F));
            _outLine = true;
            _outLineColour = Color.Black;
            _text = string.Empty;
        }

        protected internal override void DrawControl()
        {
            base.DrawControl();

            if (string.IsNullOrEmpty(Text))
                return;

            int x = DisplayLocation.X;
            int y = DisplayLocation.Y;
            int w = Size.Width;
            int h = Size.Height;

            if (OutLine)
            {
                TextRenderer.DrawText(this, Text, Font, new Rectangle(x + 1, y + 0, w, h), OutLineColour, DrawFormat);
                TextRenderer.DrawText(this, Text, Font, new Rectangle(x + 0, y + 1, w, h), OutLineColour, DrawFormat);
                TextRenderer.DrawText(this, Text, Font, new Rectangle(x + 2, y + 1, w, h), OutLineColour, DrawFormat);
                TextRenderer.DrawText(this, Text, Font, new Rectangle(x + 1, y + 2, w, h), OutLineColour, DrawFormat);
                TextRenderer.DrawText(this, Text, Font, new Rectangle(x + 1, y + 1, w, h), ForeColour, DrawFormat);
            }
            else
                TextRenderer.DrawText(this, Text, Font, new Rectangle(x + 1, y + 0, w, h), ForeColour, DrawFormat);
        }

        #region Disposable
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing) return;

            AutoSizeChanged = null;
            _autoSize = false;

            DrawFormatChanged = null;
            _drawFormat = 0;

            FontChanged = null;
            _font = null;

            OutLineChanged = null;
            _outLine = false;

            OutLineColourChanged = null;
            _outLineColour = Color.Empty;

            TextChanged = null;
            _text = null;
        }
        #endregion
    }
}
