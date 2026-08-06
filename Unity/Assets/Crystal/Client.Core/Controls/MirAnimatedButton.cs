using System;
using Client.MirSounds;

namespace Client.MirControls
{
    // 逐字移植（2026-08-06）：Client/MirControls/MirAnimatedButton.cs 动画按钮
    // （FishingStatusDialog FishButton 用）。动画逻辑复用 MirAnimatedControl
    // （Animated/AnimationCount/AnimationDelay/Loop/OffSet），叠加 MirButton 的
    // Hover/Pressed/Disabled Index 态。非动画态（pressed/hover/disabled）直接用
    // 固定帧；动画态走 MirAnimatedControl.Index（MirImageControl.Index + OffSet）。
    public class MirAnimatedButton : MirAnimatedControl
    {
        #region Hover Index
        private int _hoverIndex;
        public int HoverIndex
        {
            get { return _hoverIndex; }
            set
            {
                if (_hoverIndex == value) return;
                _hoverIndex = value;
                OnHoverIndexChanged();
            }
        }
        public event EventHandler HoverIndexChanged;
        private void OnHoverIndexChanged()
        {
            if (HoverIndexChanged != null)
                HoverIndexChanged.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Pressed Index
        private int _pressedIndex;
        public int PressedIndex
        {
            get { return _pressedIndex; }
            set
            {
                if (_pressedIndex == value) return;
                _pressedIndex = value;
                OnPressedIndexChanged();
            }
        }
        public event EventHandler PressedIndexChanged;
        private void OnPressedIndexChanged()
        {
            if (PressedIndexChanged != null)
                PressedIndexChanged.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Disabled Index
        private int _disabledIndex;
        public int DisabledIndex
        {
            get { return _disabledIndex; }
            set
            {
                if (_disabledIndex == value) return;
                _disabledIndex = value;
                OnDisabledIndexChanged();
            }
        }
        public event EventHandler DisabledIndexChanged;
        private void OnDisabledIndexChanged()
        {
            if (DisabledIndexChanged != null)
                DisabledIndexChanged.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Index
        public override int Index
        {
            get
            {
                if (!Enabled)
                    return _disabledIndex >= 0 ? _disabledIndex : base.Index;

                if (_pressedIndex >= 0 && ActiveControl == this && MouseControl == this)
                    return _pressedIndex;

                if (_hoverIndex >= 0 && MouseControl == this)
                    return _hoverIndex;

                return base.Index;
            }
            set { base.Index = value; }
        }
        #endregion

        public MirAnimatedButton()
        {
            HoverIndex = -1;
            PressedIndex = -1;
            DisabledIndex = -1;
            Sound = SoundList.ButtonB;
        }

        #region Disposable
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing) return;

            HoverIndexChanged = null;
            _hoverIndex = 0;

            PressedIndexChanged = null;
            _pressedIndex = 0;

            DisabledIndexChanged = null;
            _disabledIndex = 0;
        }
        #endregion
    }
}
