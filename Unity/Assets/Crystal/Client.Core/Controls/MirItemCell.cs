using System;
using Crystal.Client.Core.MirMath;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirScenes;
using Client.MirScenes.Dialogs;

namespace Client.MirControls
{
    // 逐字移植（2026-08-05）：Client/MirControls/MirItemCell.cs 渲染核心
    // 背包/装备/Tooltip 场景（迭代包2）。数据绑定（Item/ItemArray/GridType/ItemSlot）+ DrawControl
    // （格子背景/物品图标/SealedInfo 特效/数量角标）+ 悬停 Tooltip 触发，逐字保留。
    // 裁剪的交互（属后续迭代）：OnMouseClick/MoveItem/UseItem/RemoveItem 拖拽/使用（2639 行主体），
    // 未移植对话框分支（DropPanel/TrustMerchant/Renting/Craft/Storage/Trade/Mount 等）在
    // Item/ItemArray 中按 Scope Gate 剔除或抛 NotImplementedException。
    public sealed class MirItemCell : MirImageControl
    {
        public UserItem Item
        {
            get
            {
                if (GridType == MirGridType.TrustMerchant)
                    return TrustMerchantDialog.SellItemSlot;

                if (ItemArray != null && _itemSlot >= 0 && _itemSlot < ItemArray.Length)
                    return ItemArray[_itemSlot];
                return null;
            }
            set
            {
                if (GridType == MirGridType.TrustMerchant)
                    TrustMerchantDialog.SellItemSlot = value;
                else if (ItemArray != null && _itemSlot >= 0 && _itemSlot < ItemArray.Length)
                    ItemArray[_itemSlot] = value;

                SetEffect();
                Redraw();
            }
        }

        public UserItem ShadowItem
        {
            get { return null; } // Craft 对话框影子物品（未移植），迭代包2 恒空
        }

        public UserItem[] ItemArray
        {
            get
            {
                switch (GridType)
                {
                    case MirGridType.Inventory:
                        return MapObject.User.Inventory;
                    case MirGridType.Equipment:
                        return MapObject.User.Equipment;
                    case MirGridType.Storage:
                        return GameScene.Storage;
                    case MirGridType.QuestInventory:
                        return MapObject.User.QuestInventory;
                    case MirGridType.Trade:
                        return GameScene.User.Trade;
                    case MirGridType.GuestTrade:
                        return GuestTradeDialog.GuestItems;
                    case MirGridType.Mail:
                        return MailComposeParcelDialog.Items;
                    case MirGridType.Mount:
                        return MapObject.User.Equipment[(int)EquipmentSlot.Mount].Slots;
                    case MirGridType.Fishing:
                        // 渔具槽 = 武器（鱼竿）的 Slots（旧客户端 MirItemCell.cs:84 逐字语义；
                        // 鱼竿 UserItem.Slots 存 Hook/Float/Bait/Finder/Reel 五件子物品）。
                        return MapObject.User.Equipment[(int)EquipmentSlot.Weapon]?.Slots;
                    case MirGridType.HeroEquipment:
                        return MapObject.Hero.Equipment;
                    case MirGridType.HeroInventory:
                        return MapObject.Hero.Inventory;
                    case MirGridType.HeroHPItem:
                        return MapObject.Hero.HPItem;
                    case MirGridType.HeroMPItem:
                        return MapObject.Hero.MPItem;
                    case MirGridType.Socket:
                        // 打孔镶嵌对话框（迭代包9）：槽数组 = 选中物品的 Slots。
                        return GameScene.SelectedItem?.Slots;
                    default:
                        // 其余 GridType（Mount/Craft/Socket/Hero* 等）依赖未移植对话框，后续迭代接入
                        throw new NotImplementedException();
                }
            }
        }

        public override bool Border
        {
            get { return (GameScene.SelectedCell == this || MouseControl == this || Locked); }
        }

        private bool _locked;

        public bool Locked
        {
            get { return _locked; }
            set
            {
                if (_locked == value) return;
                _locked = value;
                Redraw();
            }
        }

        #region GridType

        private MirGridType _gridType;
        public event EventHandler GridTypeChanged;
        public MirGridType GridType
        {
            get { return _gridType; }
            set
            {
                if (_gridType == value) return;
                _gridType = value;
                OnGridTypeChanged();
            }
        }

        private void OnGridTypeChanged()
        {
            if (GridTypeChanged != null)
                GridTypeChanged.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region ItemSlot

        private int _itemSlot;
        public event EventHandler ItemSlotChanged;
        public int ItemSlot
        {
            get { return _itemSlot; }
            set
            {
                if (_itemSlot == value) return;
                _itemSlot = value;
                OnItemSlotChanged();
            }
        }

        private void OnItemSlotChanged()
        {
            if (ItemSlotChanged != null)
                ItemSlotChanged.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Count Label

        private MirLabel CountLabel { get; set; }

        #endregion

        public MirItemCell()
        {
            Size = new Size(36, 32);
            GridType = MirGridType.None;
            DrawImage = false;

            BorderColour = Color.Lime;

            BackColour = Color.FromArgb(255, 255, 125, 125);
            Opacity = 0.5F;
            DrawControlTexture = true;
            Library = Libraries.Items;
        }

        public void SetEffect()
        {
            // 特效（旧客户端空实现占位，逐字保留）
        }

        protected internal override void DrawControl()
        {
            if (Item != null && GameScene.SelectedCell != this && Locked != true)
            {
                CreateDisposeLabel();

                if (Library != null)
                {
                    ushort image = Item.Image;

                    Size imgSize = Library.GetTrueSize(image);

                    Point offSet = new Point((Size.Width - imgSize.Width) / 2, (Size.Height - imgSize.Height) / 2);

                    Library.Draw(image, DisplayLocation.Add(offSet), ForeColour, UseOffSet, 1F);

                    if (Item.SealedInfo != null && Item.SealedInfo.ExpiryDate > CMain.Now)
                    {
                        Libraries.StateItems.Draw(3590, DisplayLocation.Add(new Point(2, 2)), Color.White, UseOffSet, 1F);
                    }
                }
            }
            else if (Item != null && (GameScene.SelectedCell == this || Locked))
            {
                CreateDisposeLabel();

                if (Library != null)
                {
                    ushort image = Item.Image;

                    Size imgSize = Library.GetTrueSize(image);

                    Point offSet = new Point((Size.Width - imgSize.Width) / 2, (Size.Height - imgSize.Height) / 2);

                    Library.Draw(image, DisplayLocation.Add(offSet), Color.DimGray, UseOffSet, 0.8F);
                }
            }
            else if (ShadowItem != null)
            {
                CreateDisposeLabel();

                if (Library != null)
                {
                    ushort image = ShadowItem.Info.Image;

                    Size imgSize = Library.GetTrueSize(image);

                    Point offSet = new Point((Size.Width - imgSize.Width) / 2, (Size.Height - imgSize.Height) / 2);

                    Library.Draw(image, DisplayLocation.Add(offSet), Color.DimGray, UseOffSet, 0.8F);
                }
            }
            else
                DisposeCountLabel();
        }

        protected override void OnMouseEnter()
        {
            base.OnMouseEnter();
            // ShadowItem（Craft 影子物品）恒 null，3 参 CreateItemLabel 重载不引用
            if (Item != null)
                GameScene.Scene.CreateItemLabel(Item);
        }
        protected override void OnMouseLeave()
        {
            base.OnMouseLeave();
            GameScene.Scene.DisposeItemLabel();
            GameScene.HoverItem = null;
        }

        private void CreateDisposeLabel()
        {
            if (Item == null && ShadowItem == null)
                return;

            if (Item != null && ShadowItem == null && Item.Info.StackSize <= 1)
            {
                DisposeCountLabel();
                return;
            }

            if (CountLabel == null || CountLabel.IsDisposed)
            {
                CountLabel = new MirLabel
                {
                    AutoSize = true,
                    ForeColour = Color.Yellow,
                    NotControl = true,
                    OutLine = false,
                    Parent = this,
                };
            }

            if (ShadowItem != null)
            {
                CountLabel.ForeColour = (Item == null || ShadowItem.Count > Item.Count) ? Color.Red : Color.LimeGreen;
                CountLabel.Text = string.Format("{0}/{1}", Item == null ? 0 : Item.Count, ShadowItem.Count);
            }
            else
            {
                CountLabel.Text = Item.Count.ToString("###0");
            }

            CountLabel.Location = new Point(Size.Width - CountLabel.Size.Width, Size.Height - CountLabel.Size.Height);
        }
        private void DisposeCountLabel()
        {
            if (CountLabel != null && !CountLabel.IsDisposed)
                CountLabel.Dispose();
            CountLabel = null;
        }
    }
}
