using Client.MirGraphics;
using Client.MirScenes;
using Client.MirSounds;
using Crystal.Client.Core.MirMath;

namespace Client.MirControls
{
    // 逐字移植（2026-08-06）：Client/MirControls/MirAmountBox.cs
    // 模态数字输入框（自动喝药百分比等）：标题 + 物品图 + 仅数字 MirTextBox + OK/Cancel/Close 按钮，
    // TextChanged 校验范围着色 + Enter/Esc 键盘路由。
    // 裁剪：Show/Dispose 里 Program.Form.Controls + MirTextBox.DialogChanged（WinForms 原生表单，
    // Unity 无对应）；Show 挂载 MirScene.ActiveScene → GameScene.Scene；DrawItem 的 Draw(int,int,int) →
    // Draw(int, Point, Color)（Unity MLibrary seam 重载）。
    public sealed class MirAmountBox : MirImageControl
    {
        public MirLabel TitleLabel, TextLabel;
        public MirButton OKButton, CancelButton, CloseButton;
        public MirTextBox InputTextBox;
        public MirControl ItemImage;
        public int ImageIndex;
        public uint Amount, MinAmount, MaxAmount;

        public MirAmountBox(string title, int image, uint max, uint min = 0, uint defaultAmount = 0)
        {
            ImageIndex = image;
            MaxAmount = max;
            MinAmount = min;
            Amount = max;
            Modal = true;
            Movable = false;

            Index = 238;
            Library = Libraries.Prguse;

            Location = new Point((Settings.ScreenWidth - Size.Width) / 2, (Settings.ScreenHeight - Size.Height) / 2);

            TitleLabel = new MirLabel
            {
                AutoSize = true,
                Location = new Point(19, 8),
                Parent = this,
                NotControl = true,
                Text = title
            };

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(180, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Dispose();

            ItemImage = new MirControl
            {
                Location = new Point(15, 34),
                Size = new Size(38, 34),
                Parent = this,
            };
            ItemImage.AfterDraw += (o, e) => DrawItem();

            OKButton = new MirButton
            {
                HoverIndex = 201,
                Index = 200,
                Library = Libraries.Title,
                Location = new Point(23, 76),
                Parent = this,
                PressedIndex = 202,
            };
            OKButton.Click += (o, e) => Dispose();

            CancelButton = new MirButton
            {
                HoverIndex = 204,
                Index = 203,
                Library = Libraries.Title,
                Location = new Point(110, 76),
                Parent = this,
                PressedIndex = 205,
            };
            CancelButton.Click += (o, e) => Dispose();

            InputTextBox = new MirTextBox
            {
                Parent = this,
                Border = true,
                BorderColour = Color.Lime,
                Location = new Point(58, 43),
                Size = new Size(132, 19),
            };
            InputTextBox.SetFocus();
            InputTextBox.TextBox.KeyPress += MirInputBox_KeyPress;
            InputTextBox.TextBox.TextChanged += TextBox_TextChanged;
            InputTextBox.Text = (defaultAmount > 0 && defaultAmount <= MaxAmount) ? defaultAmount.ToString() : MaxAmount.ToString();
            InputTextBox.TextBox.SelectionStart = 0;
            InputTextBox.TextBox.SelectionLength = InputTextBox.Text.Length;

        }
        public MirAmountBox(string title, int image, string message)
        {
            ImageIndex = image;

            Modal = true;
            Movable = false;

            Index = 238;
            Library = Libraries.Prguse;

            Location = new Point((800 - Size.Width) / 2, (600 - Size.Height) / 2);

            TitleLabel = new MirLabel
            {
                AutoSize = true,
                Location = new Point(19, 8),
                Parent = this,
                NotControl = true,
                Text = title
            };

            TextLabel = new MirLabel
            {
                AutoSize = true,
                Location = new Point(60, 43),
                ForeColour = Color.Yellow,
                Parent = this,
                NotControl = true,
                Text = message
            };

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(180, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Dispose();

            ItemImage = new MirControl
            {
                Location = new Point(15, 34),
                Size = new Size(38, 34),
                Parent = this,
            };
            ItemImage.AfterDraw += (o, e) => DrawItem();

            OKButton = new MirButton
            {
                HoverIndex = 201,
                Index = 200,
                Library = Libraries.Title,
                Location = new Point(23, 76),
                Parent = this,
                PressedIndex = 202,
            };
            OKButton.Click += (o, e) => Dispose();

            CancelButton = new MirButton
            {
                HoverIndex = 204,
                Index = 203,
                Library = Libraries.Title,
                Location = new Point(110, 76),
                Parent = this,
                PressedIndex = 205,
            };
            CancelButton.Click += (o, e) => Dispose();
        }

        void TextBox_TextChanged(object sender, EventArgs e)
        {
            if (uint.TryParse(InputTextBox.Text, out Amount) && Amount >= MinAmount)
            {
                InputTextBox.BorderColour = Color.Lime;

                OKButton.Visible = true;
                if (Amount > MaxAmount)
                {
                    Amount = MaxAmount;
                    InputTextBox.Text = MaxAmount.ToString();
                    InputTextBox.TextBox.SelectionStart = InputTextBox.Text.Length;
                }

                if (Amount == MaxAmount)
                    InputTextBox.BorderColour = Color.Orange;
            }
            else
            {
                InputTextBox.BorderColour = Color.Red;
                OKButton.Visible = false;
            }
        }

        void MirInputBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar)
                && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }

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

        void DrawItem()
        {
            int x = ItemImage.DisplayLocation.X, y = ItemImage.DisplayLocation.Y;

            Size s = Libraries.Items.GetTrueSize(ImageIndex);

            x += (ItemImage.Size.Width - s.Width) / 2;
            y += (ItemImage.Size.Height - s.Height) / 2;

            Libraries.Items.Draw(ImageIndex, new Point(x, y), Color.White);
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
                CancelButton.InvokeMouseClick(EventArgs.Empty);
            else if (e.KeyChar == (char)Keys.Enter)
                OKButton.InvokeMouseClick(EventArgs.Empty);
            e.Handled = true;
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
