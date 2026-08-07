using System;
using System.Linq;
using Crystal.Client.Core.MirMath;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirSounds;

namespace Client.MirScenes.Dialogs
{
    // 逐字移植（2026-08-05）：Client/MirScenes/Dialogs/InventoryDialog.cs 渲染核心
    // 背包窗口（迭代包2）。控件树/格子布局（Grid 8x10=80 格 ItemSlot=6+idx 跳过腰带）/负重条
    // WeightBar_BeforeDraw/切页可见性（Reset/RefreshInventory/RefreshInventory2）逐字保留。
    // 裁剪的交互（属后续迭代）：删除模式（ToggleDeleteMode/PromptDelete/SendDeleteItem）、
    // 扩包 AddButton 消息框/网络、BeltDialog 腰带栏（不在迭代包2 范围）。
    // 增量2（2026-08-08）：点格选中生命周期——MirItemCell.OnMouseClick 设 SelectedCell；切页/关闭
    // （Reset/Hide）清选中 + Tooltip（DisposeItemLabel + HoverItem）。
    public sealed class InventoryDialog : MirImageControl
    {
        public MirImageControl WeightBar;
        public MirImageControl[] LockBar = new MirImageControl[10];
        public MirItemCell[] Grid;
        public MirItemCell[] QuestGrid;

        public MirButton CloseButton, ItemButton, ItemButton2, QuestButton, AddButton, DelItemButton;
        public MirLabel GoldLabel, WeightLabel;

        public InventoryDialog()
        {
            Index = 196;
            Library = Libraries.Title;
            Movable = true;
            Sort = true;
            Visible = false;

            WeightBar = new MirImageControl
            {
                Index = 24,
                Library = Libraries.Prguse,
                Location = new Point(182, 217),
                Parent = this,
                DrawImage = false,
                NotControl = true,
            };

            ItemButton = new MirButton
            {
                Index = 197,
                Library = Libraries.Title,
                Location = new Point(6, 7),
                Parent = this,
                Size = new Size(72, 23),
                Sound = SoundList.ButtonA,
            };

            ItemButton2 = new MirButton
            {
                Index = 738,
                Library = Libraries.Title,
                Location = new Point(76, 7),
                Parent = this,
                Size = new Size(72, 23),
                Sound = SoundList.ButtonA,
            };

            QuestButton = new MirButton
            {
                Index = 739,
                Library = Libraries.Title,
                Location = new Point(146, 7),
                Parent = this,
                Size = new Size(72, 23),
                Sound = SoundList.ButtonA,
            };

            AddButton = new MirButton
            {
                Index = 483,
                HoverIndex = 484,
                PressedIndex = 485,
                Library = Libraries.Title,
                Location = new Point(235, 5),
                Parent = this,
                Size = new Size(72, 23),
                Sound = SoundList.ButtonA,
                Visible = false,
            };

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(289, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };

            DelItemButton = new MirButton
            {
                Index = 366,
                HoverIndex = 367,
                PressedIndex = 368,
                Location = new Point(291, 212),
                Library = Libraries.Prguse2,
                Parent = this,
                Sound = SoundList.ButtonA,
            };

            GoldLabel = new MirLabel
            {
                Parent = this,
                Location = new Point(40, 212),
                Size = new Size(111, 14),
                Sound = SoundList.Gold,
            };

            Grid = new MirItemCell[8 * 10];

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    int idx = 8 * y + x;
                    Grid[idx] = new MirItemCell
                    {
                        ItemSlot = 6 + idx,
                        GridType = MirGridType.Inventory,
                        Library = Libraries.Items,
                        Parent = this,
                        Location = new Point(x * 36 + 9 + x, y % 5 * 32 + 37 + y % 5),
                    };

                    if (idx >= 40)
                        Grid[idx].Visible = false;
                }
            }

            QuestGrid = new MirItemCell[8 * 5];

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 5; y++)
                {
                    QuestGrid[8 * y + x] = new MirItemCell
                    {
                        ItemSlot = 8 * y + x,
                        GridType = MirGridType.QuestInventory,
                        Library = Libraries.Items,
                        Parent = this,
                        Location = new Point(x * 36 + 9 + x, y * 32 + 37 + y),
                        Visible = false
                    };
                }
            }

            WeightLabel = new MirLabel
            {
                Parent = this,
                Location = new Point(268, 212),
                Size = new Size(26, 14)
            };
            WeightBar.BeforeDraw += WeightBar_BeforeDraw;

            for (int i = 0; i < LockBar.Length; i++)
            {
                LockBar[i] = new MirImageControl
                {
                    Index = 307,
                    Library = Libraries.Prguse2,
                    Location = new Point(9 + i % 2 * 148, 37 + i / 2 * 33),
                    Parent = this,
                    DrawImage = true,
                    NotControl = true,
                    Visible = false,
                };
            }

            // 按钮交互（迭代包2）：切页（背包/物品/任务）+ 关闭。裁剪（属后续迭代）：
            // Ctrl+拖拽移格、扩包 AddButton 消息框/网络、删除模式（ToggleDeleteMode/PromptDelete）。
            ItemButton.Click += Button_Click;
            ItemButton2.Click += Button_Click;
            QuestButton.Click += Button_Click;
            CloseButton.Click += (o, e) => Hide();
        }

        void Button_Click(object sender, EventArgs e)
        {
            if (sender == ItemButton)
            {
                RefreshInventory();
            }
            else if (sender == ItemButton2)
            {
                RefreshInventory2();
            }
            else if (sender == QuestButton)
            {
                Reset();

                ItemButton.Index = 737;
                ItemButton2.Index = 738;
                QuestButton.Index = 198;

                if (GameScene.User.Inventory.Length == 46)
                {
                    ItemButton2.Index = 169;
                }

                foreach (var grid in QuestGrid)
                {
                    grid.Visible = true;
                }
            }
        }

        void Reset()
        {
            foreach (MirItemCell grid in QuestGrid)
            {
                grid.Visible = false;
            }

            foreach (MirItemCell grid in Grid)
            {
                grid.Visible = false;
            }

            for (int i = 0; i < LockBar.Length; i++)
            {
                LockBar[i].Visible = false;
            }

            AddButton.Visible = false;

            ClearSelection();
        }

        // 阶段8 第2项 增量2 选中生命周期：切页/关闭清选中 + Tooltip（防跨页残留）。
        void ClearSelection()
        {
            GameScene.SelectedCell = null;
            if (GameScene.Scene != null) GameScene.Scene.DisposeItemLabel();
            GameScene.HoverItem = null;
        }

        public override void Hide()
        {
            ClearSelection();
            base.Hide();
        }

        public void RefreshInventory()
        {
            Reset();

            ItemButton.Index = 197;
            ItemButton2.Index = 738;
            QuestButton.Index = 739;

            if (GameScene.User.Inventory.Length == 46)
            {
                ItemButton2.Index = 169;
            }

            foreach (var grid in Grid)
            {
                if (grid.ItemSlot < 46)
                    grid.Visible = true;
                else
                    grid.Visible = false;
            }
        }

        public void RefreshInventory2()
        {
            Reset();

            ItemButton.Index = 737;
            ItemButton2.Index = 168;
            QuestButton.Index = 739;

            foreach (var grid in Grid)
            {
                if (grid.ItemSlot < 46 || grid.ItemSlot >= GameScene.User.Inventory.Length)
                    grid.Visible = false;
                else
                    grid.Visible = true;
            }

            int openLevel = (GameScene.User.Inventory.Length - 46) / 4;
            for (int i = 0; i < LockBar.Length; i++)
            {
                LockBar[i].Visible = (i < openLevel) ? false : true;
            }

            AddButton.Visible = openLevel >= 10 ? false : true;
        }

        public void Process()
        {
            WeightLabel.Text = GameScene.User.Inventory.Count(t => t == null).ToString();
            GoldLabel.Text = GameScene.Gold.ToString("###,###,##0");
        }

        private void WeightBar_BeforeDraw(object sender, EventArgs e)
        {
            if (WeightBar.Library == null) return;

            double percent = MapObject.User.CurrentBagWeight / (double)MapObject.User.Stats[Stat.BagWeight];
            if (percent > 1) percent = 1;
            if (percent <= 0) return;

            // Weight bar art based on fill
            if (percent <= 0.50)
            {
                WeightBar.Library = Libraries.Prguse;
                WeightBar.Index = 24;
            }
            else if (percent <= 0.75)
            {
                WeightBar.Library = Libraries.UI_32bit;
                WeightBar.Index = 471;
            }
            else
            {
                WeightBar.Library = Libraries.UI_32bit;
                WeightBar.Index = 470;
            }

            Rectangle section = new Rectangle
            {
                Size = new Size((int)((WeightBar.Size.Width - 3) * percent), WeightBar.Size.Height)
            };

            WeightBar.Library.Draw(WeightBar.Index, section, WeightBar.DisplayLocation, Color.White, false);
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

        public MirItemCell GetQuestCell(ulong id)
        {
            return QuestGrid.FirstOrDefault(t => t.Item != null && t.Item.UniqueID == id);
        }
    }
}
