using System;
using Crystal.Client.Core.MirMath;
using Client.MirGraphics;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Client.MirScenes.Dialogs;
using C = ClientPackets;

namespace Client.MirControls
{
    // 逐字移植（2026-08-05）：Client/MirControls/MirItemCell.cs 渲染核心
    // 背包/装备/Tooltip 场景（迭代包2）。数据绑定（Item/ItemArray/GridType/ItemSlot）+ DrawControl
    // （格子背景/物品图标/SealedInfo 特效/数量角标）+ 悬停 Tooltip 触发，逐字保留。
    // 交互（增量2 点格选中；增量3 双击穿脱）：OnMouseClick 选中 / OnMouseDoubleClick→UseItem
    // （背包格可穿戴→C.EquipItem，装备格→卸下）/CanWearItem 资格校验。拖拽（MoveItem 2639 行主体）、
    // 药品/卷轴使用（C.UseItem）属后续增量；未移植对话框分支（DropPanel/TrustMerchant/Renting/Craft/
    // Storage/Trade/Mount 等）在 Item/ItemArray 中按 Scope Gate 剔除或抛 NotImplementedException。
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

        // 阶段8 第2项 增量2：点格选中（触控/鼠标统一入口）。有物品→SelectedCell=this（边框高亮+图标置灰，
        // Border/DrawControl 既有渲染消费）；空格→清除本网格选中（跨网格守卫，装备/仓库等不误清）。
        // 拖拽移动/删除（旧客户端 OnMouseClick 2639 行主体）属后续增量。
        public override void OnMouseClick(MouseEventArgs e)
        {
            if (Item != null)
                GameScene.SelectedCell = this;
            else if (GameScene.SelectedCell != null && GameScene.SelectedCell.GridType == GridType)
                GameScene.SelectedCell = null;
            base.OnMouseClick(e);
        }

        // 阶段8 第2项 增量3：双击使用/卸下（旧客户端 OnMouseDoubleClick 298-310 行同源）。
        // 先清选中（防高亮残留），再 UseItem；Locked（在途包未回）拒绝。
        public override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (Locked) return;
            base.OnMouseClick(e);
            Redraw();
            GameScene.SelectedCell = null;
            UseItem();
        }

        // 使用物品（8-2-3 裁剪）：背包格可穿戴类型→C.EquipItem（锁目标槽+本格防重复双击）；
        // 装备格→卸下（RemoveItem）。药品/卷轴/书（C.UseItem）属 8-2-4。右/左手槽序对齐旧客户端。
        public void UseItem()
        {
            if (Locked) return;

            if (GridType == MirGridType.Equipment)
            {
                RemoveItem();
                return;
            }

            if (GridType != MirGridType.Inventory || Item == null) return;

            var scene = GameScene.Scene;
            var dialog = scene != null ? scene.CharacterDialog : null;
            if (dialog == null) return;
            var actor = GameScene.User;

            bool TryEquip(EquipmentSlot slot)
            {
                var cell = dialog.Grid[(int)slot];
                if (cell.CanWearItem(actor, Item))
                {
                    Network.Enqueue(new C.EquipItem { Grid = GridType, UniqueID = Item.UniqueID, To = (int)slot });
                    cell.Locked = true;
                    Locked = true;
                    return true;
                }
                return false;
            }

            switch (Item.Info.Type)
            {
                case ItemType.Weapon: TryEquip(EquipmentSlot.Weapon); break;
                case ItemType.Armour: TryEquip(EquipmentSlot.Armour); break;
                case ItemType.Helmet: TryEquip(EquipmentSlot.Helmet); break;
                case ItemType.Necklace: TryEquip(EquipmentSlot.Necklace); break;
                case ItemType.Bracelet:
                    // 右手空或占位 Amulet 可让位→优先右手，否则落左手（旧客户端同序）。
                    if ((dialog.Grid[(int)EquipmentSlot.BraceletR].Item == null
                        || dialog.Grid[(int)EquipmentSlot.BraceletR].Item.Info.Type == ItemType.Amulet)
                        && dialog.Grid[(int)EquipmentSlot.BraceletR].CanWearItem(actor, Item))
                    {
                        Network.Enqueue(new C.EquipItem { Grid = GridType, UniqueID = Item.UniqueID, To = (int)EquipmentSlot.BraceletR });
                        dialog.Grid[(int)EquipmentSlot.BraceletR].Locked = true;
                        Locked = true;
                    }
                    else TryEquip(EquipmentSlot.BraceletL);
                    break;
                case ItemType.Ring:
                    if (dialog.Grid[(int)EquipmentSlot.RingR].Item == null && dialog.Grid[(int)EquipmentSlot.RingR].CanWearItem(actor, Item))
                    {
                        Network.Enqueue(new C.EquipItem { Grid = GridType, UniqueID = Item.UniqueID, To = (int)EquipmentSlot.RingR });
                        dialog.Grid[(int)EquipmentSlot.RingR].Locked = true;
                        Locked = true;
                    }
                    else TryEquip(EquipmentSlot.RingL);
                    break;
                case ItemType.Amulet: TryEquip(EquipmentSlot.Amulet); break;
                case ItemType.Belt: TryEquip(EquipmentSlot.Belt); break;
                case ItemType.Boots: TryEquip(EquipmentSlot.Boots); break;
                case ItemType.Stone: TryEquip(EquipmentSlot.Stone); break;
                case ItemType.Torch: TryEquip(EquipmentSlot.Torch); break;
                case ItemType.Mount: TryEquip(EquipmentSlot.Mount); break;
            }
        }

        // 卸下（8-2-3）：装备格双击→找背包首个空格发 C.RemoveItem。腰带区（0..BeltIdx-1）目标无
        // 腰带格，从 BeltIdx 起扫；无空格不发包（静默，服务器权威拒绝）。
        public void RemoveItem()
        {
            if (Locked || Item == null) return;

            for (int i = MapObject.User.BeltIdx; i < MapObject.User.Inventory.Length; i++)
            {
                if (MapObject.User.Inventory[i] != null) continue;
                Network.Enqueue(new C.RemoveItem { Grid = MirGridType.Inventory, UniqueID = Item.UniqueID, To = i });
                Locked = true;
                return;
            }
        }

        // 穿戴资格（8-2-3 裁剪）：性别/职业/需求类型/负重校验（旧客户端 CanWearItem 2287 行同源）。
        // 失败提示（ChatDialog 未移植）裁剪为静默拒绝——服务器仍权威校验，拒绝时 S.EquipItem
        // Success=false，客户端照常解锁双格。
        private bool CanWearItem(UserObject actor, UserItem i)
        {
            if (i == null || actor == null) return false;

            switch (actor.Gender)
            {
                case MirGender.Male:
                    if (!i.Info.RequiredGender.HasFlag(RequiredGender.Male)) return false;
                    break;
                case MirGender.Female:
                    if (!i.Info.RequiredGender.HasFlag(RequiredGender.Female)) return false;
                    break;
            }

            switch (actor.Class)
            {
                case MirClass.Warrior:
                    if (!i.Info.RequiredClass.HasFlag(RequiredClass.Warrior)) return false;
                    break;
                case MirClass.Wizard:
                    if (!i.Info.RequiredClass.HasFlag(RequiredClass.Wizard)) return false;
                    break;
                case MirClass.Taoist:
                    if (!i.Info.RequiredClass.HasFlag(RequiredClass.Taoist)) return false;
                    break;
                case MirClass.Assassin:
                    if (!i.Info.RequiredClass.HasFlag(RequiredClass.Assassin)) return false;
                    break;
                case MirClass.Archer:
                    if (!i.Info.RequiredClass.HasFlag(RequiredClass.Archer)) return false;
                    break;
            }

            switch (i.Info.RequiredType)
            {
                case RequiredType.Level:
                    if (actor.Level < i.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MaxAC:
                    if (actor.Stats[Stat.MaxAC] < i.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MaxMAC:
                    if (actor.Stats[Stat.MaxMAC] < i.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MaxDC:
                    if (actor.Stats[Stat.MaxDC] < i.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MaxMC:
                    if (actor.Stats[Stat.MaxMC] < i.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MaxSC:
                    if (actor.Stats[Stat.MaxSC] < i.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MaxLevel:
                    if (actor.Level > i.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MinAC:
                    if (actor.Stats[Stat.MinAC] < i.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MinMAC:
                    if (actor.Stats[Stat.MinMAC] < i.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MinDC:
                    if (actor.Stats[Stat.MinDC] < i.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MinMC:
                    if (actor.Stats[Stat.MinMC] < i.Info.RequiredAmount) return false;
                    break;
                case RequiredType.MinSC:
                    if (actor.Stats[Stat.MinSC] < i.Info.RequiredAmount) return false;
                    break;
            }

            // 负重（旧客户端同源）：武器/火把占手部负重，其余占穿戴负重。
            int weightDelta = i.Weight - (Item != null ? Item.Weight : 0);
            if (i.Info.Type == ItemType.Weapon || i.Info.Type == ItemType.Torch)
            {
                if (weightDelta + actor.CurrentHandWeight > actor.Stats[Stat.HandWeight]) return false;
            }
            else
            {
                if (weightDelta + actor.CurrentWearWeight > actor.Stats[Stat.WearWeight]) return false;
            }

            return true;
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
