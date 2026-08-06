using Crystal.Client.Core.MirMath;
using Client.MirGraphics;
using Client.MirScenes;

namespace Client.MirControls
{
    // 逐字移植（2026-08-05）：Client/MirControls/MirGoodsCell.cs 渲染核心
    // NPC 商店格子（迭代包3）。名称/数量/价格标签 + New 标记 + 物品图标绘制，逐字保留。
    // 裁剪的扩展（属后续迭代/对话框）：UsePearls 珍珠币、Recipe 配方价、BorderInfo 折线绘制
    //（Unity MirControl 无 BorderInfo 虚属性）、MultipleAvailable 多数量标记。
    public sealed class MirGoodsCell : MirControl
    {
        public UserItem Item;

        public MirLabel NameLabel, PriceLabel, CountLabel;
        public MirImageControl NewIcon;

        public MirGoodsCell()
        {
            Size = new Size(205, 32);
            BorderColour = Color.Lime;

            NameLabel = new MirLabel
            {
                AutoSize = true,
                Parent = this,
                NotControl = true,
                Location = new Point(44, 0),
            };

            CountLabel = new MirLabel
            {
                AutoSize = true,
                Parent = this,
                NotControl = true,
                DrawControlTexture = true,
                Location = new Point(23, 17),
                ForeColour = Color.Yellow,
            };

            PriceLabel = new MirLabel
            {
                AutoSize = true,
                Parent = this,
                NotControl = true,
                Location = new Point(44, 14),
            };

            NewIcon = new MirImageControl
            {
                Index = 550,
                Library = Libraries.Prguse,
                Parent = this,
                Location = new Point(190, 5),
                NotControl = true,
                Visible = false
            };

            BeforeDraw += (o, e) => Update();
            AfterDraw += (o, e) => DrawItem();
        }

        private void Update()
        {
            NewIcon.Visible = false;

            if (Item == null || Item.Info == null) return;
            NameLabel.Text = Item.Info.FriendlyName;
            CountLabel.Text = (Item.Count <= 1) ? "" : Item.Count.ToString();

            NewIcon.Visible = !Item.IsShopItem;
            PriceLabel.Text = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.PriceGold), (uint)(Item.Price() * GameScene.NPCRate));
        }

        protected override void OnMouseEnter()
        {
            base.OnMouseEnter();
            GameScene.Scene.CreateItemLabel(Item);
        }

        protected override void OnMouseLeave()
        {
            base.OnMouseLeave();
            GameScene.Scene.DisposeItemLabel();
            GameScene.HoverItem = null;
        }

        private void DrawItem()
        {
            if (Item == null || Item.Info == null) return;

            Size size = Libraries.Items.GetTrueSize(Item.Image);
            Point offSet = new Point((40 - size.Width) / 2, (32 - size.Height) / 2);
            Libraries.Items.Draw(Item.Image, DisplayLocation.Add(offSet), Color.White, false, 1F);

            CountLabel.Draw();
        }
    }
}
