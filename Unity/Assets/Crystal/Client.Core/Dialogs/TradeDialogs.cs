using System;
using System.Collections.Generic;
using System.Linq;
using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirSounds;
using C = ClientPackets;
using Client.MirNetwork;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-06）：Client/MirScenes/Dialogs/TradeDialogs.cs 交易对话框控制树。
    // TradeDialog 本方交易面板（Prguse 389，5×2 MirItemCell + 名称/金币标签 + Title 520 确认
    // + Prguse2 360 关闭）/ GuestTradeDialog 对方交易面板（Prguse 390，5×2 GuestGrid）。
    // 阶段8 第7项 接回：GoldLabel 点弹 MirAmountBox 输入金币（8-5-1 软键盘桥可用，旧客户端逐字）；
    // 网络发包（C.TradeConfirm/C.TradeCancel/C.TradeGold）保留（Network 已可用），放入/取回
    // 走 MirItemCell.OnMouseClick 两段式触控（选中源格→点空目标格发 C.DepositTradeItem/RetrieveTradeItem）。
    public sealed class TradeDialog : MirImageControl
    {
        public MirItemCell[] Grid;
        public MirLabel NameLabel, GoldLabel;
        public MirButton ConfirmButton, CloseButton;

        public TradeDialog()
        {
            Index = 389;
            Library = Libraries.Prguse;
            Movable = true;
            Size = new Size(204, 152);
            Location = new Point((Settings.ScreenWidth / 2) - Size.Width - 10, Settings.ScreenHeight - 350);
            Sort = true;

            #region Buttons
            ConfirmButton = new MirButton
            {
                Index = 520,
                HoverIndex = 521,
                Location = new Point(135, 120),
                Size = new Size(48, 25),
                Library = Libraries.Title,
                Parent = this,
                PressedIndex = 522,
                Sound = SoundList.ButtonA,
            };
            ConfirmButton.Click += (o, e) =>
            {
                ChangeLockState(!GameScene.User.TradeLocked);
                Network.Enqueue(new C.TradeConfirm { Locked = GameScene.User.TradeLocked });
            };

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(Size.Width - 23, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) =>
            {
                Hide();
                GameScene.Scene.GuestTradeDialog.Hide();
                TradeCancel();
            };

            #endregion

            #region Host labels
            NameLabel = new MirLabel
            {
                Parent = this,
                Location = new Point(20, 10),
                Size = new Size(150, 14),
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                NotControl = true,
            };

            GoldLabel = new MirLabel
            {
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                Font = new Font(Settings.FontName, 8F),
                Location = new Point(35, 123),
                Parent = this,
                Size = new Size(90, 15),
                Sound = SoundList.Gold,
            };
            // 金币输入（8-7-1）：点金币标签弹 MirAmountBox 输入金额（旧客户端 TradeDialogs 逐字移植，
            // 8-5-1 软键盘桥已可用）。背包选中格时不可输入（对齐旧客户端 SelectedCell==null 守卫）；
            // OK 且 Amount>0 → TradeGoldAmount 累加 + C.TradeGold{Amount}（服务器权威校验上限）。
            GoldLabel.Click += (o, e) =>
            {
                if (GameScene.SelectedCell == null && GameScene.Gold > 0)
                {
                    var amountBox = new MirAmountBox(
                        GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.TradeAmount),
                        116, GameScene.Gold);

                    amountBox.OKButton.Click += (c, a) =>
                    {
                        if (amountBox.Amount > 0)
                        {
                            GameScene.User.TradeGoldAmount += amountBox.Amount;
                            Network.Enqueue(new C.TradeGold { Amount = amountBox.Amount });
                            RefreshInterface();
                        }
                    };

                    amountBox.Show();
                    GameScene.PickedUpGold = false;
                }
            };
            #endregion

            #region Grids
            Grid = new MirItemCell[5 * 2];

            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    Grid[2 * x + y] = new MirItemCell
                    {
                        ItemSlot = 2 * x + y,
                        GridType = MirGridType.Trade,
                        Parent = this,
                        Location = new Point(x * 36 + 10 + x, y * 32 + 39 + y),
                    };
                }
            }
            #endregion
        }

        public void ChangeLockState(bool lockState, bool cancelled = false)
        {
            GameScene.User.TradeLocked = lockState;

            if (GameScene.User.TradeLocked)
            {
                ConfirmButton.Index = 521;
            }
            else
            {
                ConfirmButton.Index = 520;
            }
        }

        public void RefreshInterface()
        {
            NameLabel.Text = GameScene.User.Name;
            GoldLabel.Text = GameScene.User.TradeGoldAmount.ToString("###,###,##0");

            GameScene.Scene.GuestTradeDialog.RefreshInterface();

            Redraw();
        }

        public void TradeAccept()
        {
            GameScene.Scene.InventoryDialog.Location = new Point(Settings.ScreenWidth - GameScene.Scene.InventoryDialog.Size.Width, 0);
            GameScene.Scene.InventoryDialog.Show();

            RefreshInterface();

            Show();
            GameScene.Scene.GuestTradeDialog.Show();
        }

        public void TradeReset()
        {
            GameScene.Scene.GuestTradeDialog.TradeReset();

            for (int i = 0; i < GameScene.User.Trade.Length; i++)
                GameScene.User.Trade[i] = null;

            GameScene.User.TradeGoldAmount = 0;
            ChangeLockState(false, true);

            RefreshInterface();

            Hide();
            GameScene.Scene.GuestTradeDialog.Hide();
        }

        public void TradeCancel()
        {
            Network.Enqueue(new C.TradeCancel());
        }

        public MirItemCell GetCell(ulong id)
        {
            for (int i = 0; i < Grid.Length; i++)
            {
                if (Grid[i].Item == null || Grid[i].Item.UniqueID != id) continue;
                return Grid[i];
            }
            return null;
        }
    }
    public sealed class GuestTradeDialog : MirImageControl
    {
        public MirItemCell[] GuestGrid;
        public static UserItem[] GuestItems = new UserItem[10];
        public string GuestName;
        public uint GuestGold;

        public MirLabel GuestNameLabel, GuestGoldLabel;

        public MirButton ConfirmButton;

        public GuestTradeDialog()
        {
            Index = 390;
            Library = Libraries.Prguse;
            Movable = true;
            Size = new Size(204, 152);
            Location = new Point((Settings.ScreenWidth / 2) + 10, Settings.ScreenHeight - 350);
            Sort = true;

            #region Host labels
            GuestNameLabel = new MirLabel
            {
                Parent = this,
                Location = new Point(0, 10),
                Size = new Size(204, 14),
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                NotControl = true,
            };

            GuestGoldLabel = new MirLabel
            {
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                Font = new Font(Settings.FontName, 8F),
                Location = new Point(35, 123),
                Parent = this,
                Size = new Size(90, 15),
                Sound = SoundList.Gold,
                NotControl = true,
            };
            #endregion

            #region Grids
            GuestGrid = new MirItemCell[5 * 2];

            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    GuestGrid[2 * x + y] = new MirItemCell
                    {
                        ItemSlot = 2 * x + y,
                        GridType = MirGridType.GuestTrade,
                        Parent = this,
                        Location = new Point(x * 36 + 10 + x, y * 32 + 39 + y),
                    };
                }
            }
            #endregion
        }

        public void RefreshInterface()
        {
            GuestNameLabel.Text = GuestName;
            GuestGoldLabel.Text = string.Format("{0:###,###,##0}", GuestGold);

            for (int i = 0; i < GuestItems.Length; i++)
            {
                if (GuestItems[i] == null) continue;
                GameScene.Bind(GuestItems[i]);
            }

            Redraw();
        }

        public void TradeReset()
        {
            for (int i = 0; i < GuestItems.Length; i++)
                GuestItems[i] = null;

            GuestName = string.Empty;
            GuestGold = 0;

            Hide();
        }
    }
}
