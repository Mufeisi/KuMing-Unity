using Crystal.Client.Core.MirMath;
using Client.MirGraphics;
using Client.MirSounds;

namespace Client.MirControls
{
    // 逐字移植（2026-08-06）：Client/MirControls/MirCheckBox.cs
    // 勾选框：Checked 切换 TickedIndex/UnTickedIndex 两态图标 + LabelText 文本。
    public class MirCheckBox : MirButton
    {

        #region TickedIndex
        private int _tickedIndex;
        public int TickedIndex
        {
            get { return _tickedIndex; }
            set { _tickedIndex = value; }
        }
        #endregion

        #region UnTickedIndex
        private int _untickedIndex;
        public int UnTickedIndex
        {
            get { return _untickedIndex; }
            set { _untickedIndex = value; }
        }
        #endregion

        #region Checked
        private bool _checked;
        public bool Checked
        {
            get { return _checked; }
            set { _checked = value; Index = value ? TickedIndex : UnTickedIndex; Redraw(); }
        }
        #endregion

        #region Label
        private new MirLabel _label;
        #endregion

        #region CenterText
        private new bool _center;
        public bool CenterLabelText
        {
            get
            {
                return _center;
            }
            set
            {
                _center = value;
                if (_center)
                {
                    _label.Size = Size;
                    _label.DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
                }
                else
                    _label.AutoSize = true;
            }
        }
        #endregion

        #region LabelText
        public string LabelText
        {
            set
            {
                if (_label == null || _label.IsDisposed)
                    return;
                _label.Text = value;
                _label.Visible = !string.IsNullOrEmpty(value);
            }
        }
        #endregion

        public MirCheckBox()
        {
            TickedIndex = -1;
            UnTickedIndex = -1;
            Index = -1;
            HoverIndex = -1;
            PressedIndex = -1;
            DisabledIndex = -1;
            Sound = SoundList.ButtonB;
            Click += MirCheckBox_Click;

            _label = new MirLabel
            {
                AutoSize = true,
                NotControl = true,
                Location = new Point(15, -2),
                Parent = this
            };
        }

        private void MirCheckBox_Click(object sender, EventArgs e)
        {
            Checked = !Checked;
            if (Checked) Index = TickedIndex;
            else Index = UnTickedIndex;
            Redraw();
        }
    }
}
