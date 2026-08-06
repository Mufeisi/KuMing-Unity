using System;
using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirSounds;
using C = ClientPackets;
using Client.MirNetwork;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-05）：Client/MirScenes/Dialogs/NPCDialogs.cs StorageDialog 仓库对话框。
    // 仓库网格（160 个 MirItemCell，GridType=Storage 绑 GameScene.Storage）+ Storage1/Storage2
    // 分页 + RentButton 扩展租赁 + ProtectButton 密码 + RentalLabel/LockedPage/StoragePasswordLabel
    // 状态标签 + CloseButton。RefreshStorage1/2 切分页显隐。
    // 裁剪的扩展（属后续迭代/依赖对话框）：仓库密码全套（MirInputBox/MirMessageBox/C.SetStoragePassword/
    // C.UnlockStorage/S.StorageUnlockResult/S.StoragePasswordResult/Handle*）、RentButton 点击
    //（MirMessageBox + C.Chat @ADDSTORAGE）、GetCell（无调用方）。
    public sealed class StorageDialog : MirImageControl
    {
        public MirItemCell[] Grid;
        public MirButton Storage1Button, Storage2Button, RentButton, ProtectButton, CloseButton;
        public MirImageControl LockedPage;
        public MirLabel RentalLabel, StoragePasswordLabel;

        public StorageDialog()
        {
            Index = 586;
            Library = Libraries.Prguse;
            Location = new Point(0, 0);
            Sort = true;

            MirImageControl TitleLabel = new MirImageControl
            {
                Index = 0,
                Library = Libraries.Title,
                Location = new Point(18, 8),
                Parent = this
            };

            LockedPage = new MirImageControl
            {
                Index = 2443,
                Library = Libraries.Prguse,
                Location = new Point(8, 59),
                Parent = this,
                Visible = false
            };

            Storage1Button = new MirButton
            {
                HoverIndex = 743,
                Index = 743,
                Location = new Point(8, 36),
                Library = Libraries.Title,
                Parent = this,
                PressedIndex = 744,
                Sound = SoundList.ButtonA,
            };
            Storage1Button.Click += (o, e) =>
            {
                RefreshStorage1();
            };

            Storage2Button = new MirButton
            {
                HoverIndex = 746,
                Index = 746,
                Location = new Point(80, 36),
                Library = Libraries.Title,
                Parent = this,
                PressedIndex = 746,
                Sound = SoundList.ButtonA,
                Visible = true
            };
            Storage2Button.Click += (o, e) =>
            {
                RefreshStorage2();
            };
            RentButton = new MirButton
            {
                Index = 483,
                HoverIndex = 484,
                PressedIndex = 485,
                Library = Libraries.Title,
                Location = new Point(283, 33),
                Parent = this,
                Sound = SoundList.ButtonA,
                Visible = true,
            };

            ProtectButton = new MirButton
            {
                HoverIndex = 114,
                Index = 113,
                Location = new Point(328, 33),
                Library = Libraries.Title,
                Parent = this,
                PressedIndex = 115,
                Sound = SoundList.ButtonA,
                Visible = true
            };
            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(363, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Hide();

            RentalLabel = new MirLabel
            {
                Parent = this,
                Location = new Point(40, 322),
                AutoSize = true,
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                NotControl = true,
                Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ExpandedStorageLocked),
                ForeColour = Color.Red
            };

            StoragePasswordLabel = new MirLabel
            {
                Parent = this,
                Location = new Point(40, 304),
                AutoSize = true,
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                NotControl = true,
                ForeColour = Color.White,
                Visible = false
            };

            Grid = new MirItemCell[10 * 16];

            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    int idx = 10 * y + x;

                    Grid[idx] = new MirItemCell
                    {
                        ItemSlot = idx,
                        GridType = MirGridType.Storage,
                        Library = Libraries.Items,
                        Parent = this,
                        Location = new Point(x * 36 + 9 + x, y % 8 * 32 + 60 + y % 8),
                    };

                    if (idx >= Globals.StorageGridSize)
                        Grid[idx].Visible = false;
                }
            }
        }

        public override void Show()
        {
            if (GameScene.User == null) return;

            GameScene.Scene.InventoryDialog.Location = new Point(Size.Width + 5, Location.Y);
            GameScene.Scene.InventoryDialog.Show();
            RefreshStorage1();

            Visible = true;
        }

        public void RefreshStorage1()
        {
            if (GameScene.User == null) return;

            Storage1Button.Index = 743;
            Storage1Button.HoverIndex = 743;
            Storage2Button.Index = 746;
            Storage2Button.HoverIndex = 746;

            foreach (var grid in Grid)
            {
                if (grid.ItemSlot < Globals.StorageGridSize)
                    grid.Visible = true;
                else
                    grid.Visible = false;
            }

            RentButton.Visible = false;
            LockedPage.Visible = false;
            RentalLabel.Visible = false;
            StoragePasswordLabel.Visible = false;
        }

        public void RefreshStorage2()
        {
            if (GameScene.User == null) return;

            Storage1Button.Index = 744;
            Storage1Button.HoverIndex = 744;
            Storage2Button.Index = 745;
            Storage2Button.HoverIndex = 745;

            RentalLabel.Visible = true;

            if (GameScene.User.HasExpandedStorage)
            {
                RentButton.Visible = true;
                LockedPage.Visible = false;
                RentalLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ExpandedStorageExpiresOn) + GameScene.User.ExpandedStorageExpiryTime.ToString();
                RentalLabel.ForeColour = Color.White;
            }
            else
            {
                RentalLabel.Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.ExpandedStorageLocked);
                RentalLabel.ForeColour = Color.Red;
                RentButton.Visible = true;
                LockedPage.Visible = true;
            }

            foreach (var grid in Grid)
            {
                if (grid.ItemSlot < Globals.StorageGridSize || !GameScene.User.HasExpandedStorage)
                    grid.Visible = false;
                else
                    grid.Visible = true;
            }
            StoragePasswordLabel.Visible = false;
        }
    }
}
