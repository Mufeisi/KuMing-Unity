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
    // 逐字移植（2026-08-05）：Client/MirScenes/Dialogs/NPCDialogs.cs NPCGoodsDialog 商店对话框。
    // NPC 商店商品列表（8 个 MirGoodsCell 格）+ BuyButton 购买 + BuyLabel 标题 + Up/Down/PositionBar
    // 滚动 + 关闭。NewGoods/AddGoods 填商品，Update 分页/选中描边，BuyItem 发 C.BuyItem。
    // 裁剪的扩展（属后续迭代/依赖对话框）：PanelType.Craft 分支、NPCSubGoodsDialog 多数量子选择、
    // UsePearls 珍珠币、MirAmountBox 数量输入框（StackSize>1 直接整组购买）、DoubleClick、滚轮、
    // PositionBar 拖拽移动。
    public sealed class NPCGoodsDialog : MirImageControl
    {
        public PanelType PType;
        public bool UsePearls;

        public int StartIndex;
        public UserItem SelectedItem;

        public List<UserItem> Goods = new List<UserItem>();
        public List<UserItem> DisplayGoods = new List<UserItem>();
        public MirGoodsCell[] Cells;
        public MirButton BuyButton, CloseButton;
        public MirImageControl BuyLabel;

        public MirButton UpButton, DownButton, PositionBar;

        public NPCGoodsDialog(PanelType type)
        {
            PType = type;

            Index = 1000;
            Library = Libraries.Prguse;
            Location = new Point(0, 224);
            Cells = new MirGoodsCell[8];
            Sort = true;

            for (int i = 0; i < Cells.Length; i++)
            {
                Cells[i] = new MirGoodsCell
                {
                    Parent = this,
                    Location = new Point(10, 34 + i * 33),
                };
                Cells[i].Click += (o, e) =>
                {
                    SelectedItem = ((MirGoodsCell)o).Item;
                    Update();
                };
            }

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(217, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Hide();

            BuyButton = new MirButton
            {
                HoverIndex = 313,
                Index = 312,
                Location = new Point(77, 304),
                Library = Libraries.Title,
                Parent = this,
                PressedIndex = 314,
                Sound = SoundList.ButtonA,
            };
            BuyButton.Click += (o, e) => BuyItem();

            BuyLabel = new MirImageControl
            {
                Index = 27,
                Library = Libraries.Title,
                Parent = this,
                Location = new Point(20, 9),
            };

            UpButton = new MirButton
            {
                Index = 197,
                HoverIndex = 198,
                Library = Libraries.Prguse2,
                Location = new Point(219, 35),
                Parent = this,
                PressedIndex = 199,
                Sound = SoundList.ButtonA
            };
            UpButton.Click += (o, e) =>
            {
                if (StartIndex == 0) return;
                StartIndex--;
                Update();
            };

            DownButton = new MirButton
            {
                Index = 207,
                HoverIndex = 208,
                Library = Libraries.Prguse2,
                Location = new Point(219, 284),
                Parent = this,
                PressedIndex = 209,
                Sound = SoundList.ButtonA
            };
            DownButton.Click += (o, e) =>
            {
                if (DisplayGoods.Count <= 8) return;

                if (StartIndex == DisplayGoods.Count - 8) return;
                StartIndex++;
                Update();
            };

            PositionBar = new MirButton
            {
                Index = 205,
                HoverIndex = 206,
                Library = Libraries.Prguse2,
                Location = new Point(219, 49),
                Parent = this,
                PressedIndex = 206,
                Sound = SoundList.None
            };
        }

        private void BuyItem()
        {
            if (SelectedItem == null) return;

            if (SelectedItem.Info.StackSize > 1)
            {
                ushort tempCount = SelectedItem.Count;
                ushort maxQuantity = SelectedItem.Info.StackSize;

                if (SelectedItem.Price() > GameScene.Gold)
                {
                    maxQuantity = Math.Min(ushort.MaxValue, (ushort)(GameScene.Gold / (SelectedItem.Price() / SelectedItem.Count)));
                    if (maxQuantity == 0)
                    {
                        GameScene.Scene.ChatDialog.ReceiveChat(GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.LowGold), ChatType.System);
                        return;
                    }
                }

                MapObject.User.GetMaxGain(SelectedItem);

                if (SelectedItem.Count == 0)
                {
                    SelectedItem.Count = tempCount;
                    GameScene.Scene.ChatDialog.ReceiveChat(GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.YouNoBagSpace), ChatType.System);
                    return;
                }

                if (SelectedItem.Count < maxQuantity)
                {
                    maxQuantity = SelectedItem.Count;
                }

                if (SelectedItem.Count > tempCount)
                {
                    SelectedItem.Count = tempCount;
                }

                Network.Enqueue(new C.BuyItem { ItemIndex = SelectedItem.UniqueID, Count = maxQuantity, Type = PType });
            }
            else
            {
                if (SelectedItem.Info.Price > GameScene.Gold)
                {
                    GameScene.Scene.ChatDialog.ReceiveChat(GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.LowGold), ChatType.System);
                    return;
                }

                for (int i = 0; i < MapObject.User.Inventory.Length; i++)
                {
                    if (MapObject.User.Inventory[i] == null) break;
                    if (i == MapObject.User.Inventory.Length - 1)
                    {
                        GameScene.Scene.ChatDialog.ReceiveChat(GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.YouNoBagSpace), ChatType.System);
                        return;
                    }
                }

                Network.Enqueue(new C.BuyItem { ItemIndex = SelectedItem.UniqueID, Count = 1, Type = PType });
            }
        }

        private void Update()
        {
            if (StartIndex > DisplayGoods.Count - 8) StartIndex = DisplayGoods.Count - 8;
            if (StartIndex <= 0) StartIndex = 0;

            if (DisplayGoods.Count > 8)
            {
                PositionBar.Visible = true;
                int h = 233 - PositionBar.Size.Height;
                h = (int)((h / (float)(DisplayGoods.Count - 8)) * StartIndex);
                PositionBar.Location = new Point(219, 49 + h);
            }
            else
                PositionBar.Visible = false;

            for (int i = 0; i < 8; i++)
            {
                if (i + StartIndex >= DisplayGoods.Count)
                {
                    Cells[i].Visible = false;
                    continue;
                }
                Cells[i].Visible = true;

                Cells[i].Item = DisplayGoods[i + StartIndex];
                Cells[i].Border = SelectedItem != null && Cells[i].Item == SelectedItem;
            }
        }

        public void NewGoods(IEnumerable<UserItem> list)
        {
            Goods.Clear();
            DisplayGoods.Clear();

            AddGoods(list);
        }

        public void AddGoods(IEnumerable<UserItem> list)
        {
            foreach (UserItem item in list)
            {
                // 普通商店每类只显示一个（MirGoodsCell 无 UsePearls，全按 Gold 价）
                if (PType == PanelType.Buy)
                {
                    Goods.Add(item);

                    if (DisplayGoods.Any(x => x.Info.Index == item.Info.Index)) continue;
                }

                DisplayGoods.Add(item);
            }

            Update();
        }

        public override void Show()
        {
            Location = new Point(Location.X, GameScene.Scene.NPCDialog.Size.Height);
            Visible = true;

            GameScene.Scene.InventoryDialog.Show();
        }
    }
}
