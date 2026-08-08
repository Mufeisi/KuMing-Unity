using System;
using System.Collections.Generic;
using System.Linq;
using Client;
using Client.MirControls;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Client.MirScenes.Dialogs;
using Crystal.Client.Core.MirMath;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;
using C = ClientPackets;
using S = ServerPackets;
using MPoint = Crystal.Client.Core.MirMath.Point;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第3项 增量2 商店买卖触控纯逻辑验证（无服务器）：
    // S.NPCGoods → GetItemInfo 物品信息表解析 Info（未收录跳过）→ NPCGoodsDialog.NewGoods 渲染 + Show
    // （连带打开背包）；点格选中（mouse 链）→ BuyButton 购买 → C.BuyItem{ItemIndex=UniqueID, Count, Type}
    // （数量=最大可购：StackSize/金币/listing Count 封顶，对齐旧客户端 BuyItem 逻辑）；CloseButton 关闭；
    // 未选商品点购买不发包。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.ShopVerify.Run -quit
    // 断言：全过输出 [shopverify] PASS exit 0。
    public static class ShopVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[shopverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Scene/ItemInfoList/Gold/Objects/User），建空场景 + 背包/NPC 对话框
        // （NPCGoodsDialog 由 NpcGoods 懒建，同运行时兜底分支）+ 玩家（空背包 46 格 → GetMaxGain 早退）。
        static GameScene NewScene()
        {
            GameScene.Scene = null;
            GameScene.NPCID = 0; GameScene.NPCTime = 0; GameScene.NPCRate = 1f;
            GameScene.Gold = 0;
            GameScene.ItemInfoList.Clear();
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;

            var scene = new GameScene();
            GameScene.Scene = scene;
            scene.InventoryDialog = new InventoryDialog { Parent = scene, Visible = false };
            scene.NPCDialog = new NPCDialog { Parent = scene, Visible = false };

            var user = new UserObject(1) { Name = "probe" };
            MapObject.User = user;
            MapControl.User = user;
            return scene;
        }

        static ItemInfo InfoOf(int index, string name, uint price, ushort stack, ItemType type)
        {
            return new ItemInfo
            {
                Index = index,
                Name = name,
                Type = type,
                Shape = 0,
                Weight = 1,
                Image = 1,
                Durability = 0,
                Price = price,
                StackSize = stack,
                Stats = new Stats(),
            };
        }

        static UserItem GoodsItem(int index, ulong uid, ushort count, ItemInfo info)
        {
            return new UserItem(info) { UniqueID = uid, Count = count, IsShopItem = true };
        }

        // 点按分发（与 TouchInputAdapter 同链路：Move 更新 hover → Down 置 ActiveControl → Up+Click）。
        static void Tap(MPoint p)
        {
            var sc = GameScene.Scene;
            CMain.MPoint = p;
            sc.OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0));
            sc.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
            sc.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
            sc.OnMouseClick(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        }

        static MPoint Center(MirControl c)
        {
            var r = c.DisplayRectangle;
            return new MPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        // 购买走 BuyButton.Click → BuyItem → Network.Enqueue 直发（非 seam）：用 SentPackets 队列捕获断言。
        static void DrainPackets()
        {
            while (Network.SentPackets.TryDequeue(out _)) { }
        }

        static C.BuyItem LastBuyItem()
        {
            C.BuyItem result = null;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.BuyItem buy) result = buy;
            return result;
        }

        // 空库（Library null）下面板 AutoSize 取帧尺寸失败回退 0×0 → 显式尺寸供父矩形命中
        // （同 NpcVerify case6 对 NPCDialog 的处理；MirGoodsCell 构造器已设固定 205×32 无需处理）。
        static void PrepDialog(NPCGoodsDialog dlg)
        {
            dlg.AutoSize = false;
            dlg.Size = new Size(300, 400);
        }

        // 空库下按钮同 0×0 问题 → 显式尺寸。
        static void PrepButtons(NPCGoodsDialog dlg)
        {
            dlg.BuyButton.AutoSize = false;
            dlg.BuyButton.Size = new Size(120, 40);
            dlg.CloseButton.AutoSize = false;
            dlg.CloseButton.Size = new Size(30, 30);
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;

            // ===== case1 S.NPCGoods 分发：对话框创建+Visible+商品填充+Info 解析+NPCRate+背包连带 =====
            {
                var scene = NewScene();
                var potionInfo = InfoOf(101, "HP药水", 100, 100, ItemType.Potion);
                GameScene.ItemInfoList.Add(potionInfo);
                GameSession.NpcGoods(new S.NPCGoods
                {
                    List = new List<UserItem> { GoodsItem(101, 42, 1, potionInfo) },
                    Rate = 0.8f,
                    Type = PanelType.Buy,
                });
                var dlg = scene.NPCGoodsDialog;
                Check(dlg != null && dlg.Visible, "case1 dialog created+visible");
                Check(dlg.DisplayGoods.Count == 1 && dlg.DisplayGoods[0].Info == potionInfo, "case1 goods populated+Info resolved");
                Check(Math.Abs(GameScene.NPCRate - 0.8f) < 0.0001f, "case1 NPCRate set");
                Check(scene.InventoryDialog.Visible, "case1 Show opens inventory");
            }

            // ===== case2 未收录商品（物品信息表缺该 Index）：跳过不崩，其余正常 =====
            {
                var scene = NewScene();
                var potionInfo = InfoOf(101, "HP药水", 100, 100, ItemType.Potion);
                GameScene.ItemInfoList.Add(potionInfo);
                var unknown = GoodsItem(999, 2, 1, InfoOf(999, "dummy", 1, 1, ItemType.Potion));
                unknown.Info = null; // 模拟未解析状态（GetItemInfo 查不到 → 置 null → NpcGoods 跳过）
                GameSession.NpcGoods(new S.NPCGoods
                {
                    List = new List<UserItem> { unknown, GoodsItem(101, 42, 1, potionInfo) },
                    Rate = 1f,
                    Type = PanelType.Buy,
                });
                var dlg = scene.NPCGoodsDialog;
                Check(dlg != null && dlg.Visible, "case2 dialog ok");
                Check(dlg.DisplayGoods.Count == 1 && dlg.DisplayGoods[0].Info == potionInfo, "case2 unknown skipped, known kept");
            }

            // ===== case3 点格选中 + 切换 =====
            {
                var scene = NewScene();
                var potionInfo = InfoOf(101, "HP药水", 100, 100, ItemType.Potion);
                var swordInfo = InfoOf(102, "木剑", 50, 1, ItemType.Weapon);
                GameScene.ItemInfoList.Add(potionInfo);
                GameScene.ItemInfoList.Add(swordInfo);
                GameSession.NpcGoods(new S.NPCGoods
                {
                    List = new List<UserItem>
                    {
                        GoodsItem(101, 42, 1, potionInfo),
                        GoodsItem(102, 43, 1, swordInfo),
                    },
                    Rate = 1f,
                    Type = PanelType.Buy,
                });
                var dlg = scene.NPCGoodsDialog;
                PrepDialog(dlg);
                Check(dlg.DisplayGoods.Count == 2, "case3 two goods");
                Check(dlg.SelectedItem == null, "case3 no selection initially");
                CMain.Time += 10000;
                Tap(Center(dlg.Cells[0]));
                Check(dlg.SelectedItem == dlg.DisplayGoods[0], "case3 cell0 selected");
                CMain.Time += 1000; // 防双击判定（OnMouseClick 500ms 窗口）
                Tap(Center(dlg.Cells[1]));
                Check(dlg.SelectedItem == dlg.DisplayGoods[1], "case3 cell1 reselected");
            }

            // ===== case4 单件购买：StackSize=1 → Count=1 发包 =====
            {
                var scene = NewScene();
                var swordInfo = InfoOf(102, "木剑", 50, 1, ItemType.Weapon);
                GameScene.ItemInfoList.Add(swordInfo);
                GameScene.Gold = 10000;
                GameSession.NpcGoods(new S.NPCGoods
                {
                    List = new List<UserItem> { GoodsItem(102, 43, 1, swordInfo) },
                    Rate = 1f,
                    Type = PanelType.Buy,
                });
                var dlg = scene.NPCGoodsDialog;
                PrepDialog(dlg);
                PrepButtons(dlg);
                DrainPackets();
                CMain.Time += 10000;
                Tap(Center(dlg.Cells[0]));
                CMain.Time += 1000;
                Tap(Center(dlg.BuyButton));
                var buy = LastBuyItem();
                Check(buy != null && buy.ItemIndex == 43 && buy.Count == 1 && buy.Type == PanelType.Buy, "case4 single buy packet");
            }

            // ===== case5 叠放整组：listing Count=10, StackSize=100, 金币充足 → Count=10 =====
            {
                var scene = NewScene();
                var potionInfo = InfoOf(101, "HP药水", 1000, 100, ItemType.Potion);
                GameScene.ItemInfoList.Add(potionInfo);
                GameScene.Gold = 100000; // Price()=1000 < Gold → 不触发金币封顶
                GameSession.NpcGoods(new S.NPCGoods
                {
                    List = new List<UserItem> { GoodsItem(101, 42, 10, potionInfo) },
                    Rate = 1f,
                    Type = PanelType.Buy,
                });
                var dlg = scene.NPCGoodsDialog;
                PrepDialog(dlg);
                PrepButtons(dlg);
                DrainPackets();
                CMain.Time += 10000;
                Tap(Center(dlg.Cells[0]));
                CMain.Time += 1000;
                Tap(Center(dlg.BuyButton));
                var buy = LastBuyItem();
                Check(buy != null && buy.Count == 10, "case5 stack buy full listing count");
            }

            // ===== case6 金币封顶：Price()=单价×Count=1000×10=10000 > Gold=3000；单价=Price()/Count=1000
            //       → maxQuantity=3000/1000=3 =====
            {
                var scene = NewScene();
                var potionInfo = InfoOf(101, "HP药水", 1000, 100, ItemType.Potion);
                GameScene.ItemInfoList.Add(potionInfo);
                GameScene.Gold = 3000;
                GameSession.NpcGoods(new S.NPCGoods
                {
                    List = new List<UserItem> { GoodsItem(101, 42, 10, potionInfo) },
                    Rate = 1f,
                    Type = PanelType.Buy,
                });
                var dlg = scene.NPCGoodsDialog;
                PrepDialog(dlg);
                PrepButtons(dlg);
                DrainPackets();
                CMain.Time += 10000;
                Tap(Center(dlg.Cells[0]));
                CMain.Time += 1000;
                Tap(Center(dlg.BuyButton));
                var buy = LastBuyItem();
                Check(buy != null && buy.Count == 3, "case6 gold-capped count");
            }

            // ===== case7 StackSize 封顶：StackSize=5 < listing Count=10 → Count=5 =====
            {
                var scene = NewScene();
                var potionInfo = InfoOf(101, "HP药水", 1000, 5, ItemType.Potion);
                GameScene.ItemInfoList.Add(potionInfo);
                GameScene.Gold = 100000;
                GameSession.NpcGoods(new S.NPCGoods
                {
                    List = new List<UserItem> { GoodsItem(101, 42, 10, potionInfo) },
                    Rate = 1f,
                    Type = PanelType.Buy,
                });
                var dlg = scene.NPCGoodsDialog;
                PrepDialog(dlg);
                PrepButtons(dlg);
                DrainPackets();
                CMain.Time += 10000;
                Tap(Center(dlg.Cells[0]));
                CMain.Time += 1000;
                Tap(Center(dlg.BuyButton));
                var buy = LastBuyItem();
                Check(buy != null && buy.Count == 5, "case7 stacksize-capped count");
            }

            // ===== case8 未选商品点购买：SelectedItem null 守卫不发包 =====
            {
                var scene = NewScene();
                var potionInfo = InfoOf(101, "HP药水", 100, 100, ItemType.Potion);
                GameScene.ItemInfoList.Add(potionInfo);
                GameScene.Gold = 10000;
                GameSession.NpcGoods(new S.NPCGoods
                {
                    List = new List<UserItem> { GoodsItem(101, 42, 1, potionInfo) },
                    Rate = 1f,
                    Type = PanelType.Buy,
                });
                var dlg = scene.NPCGoodsDialog;
                PrepDialog(dlg);
                PrepButtons(dlg);
                DrainPackets();
                CMain.Time += 10000;
                Tap(Center(dlg.BuyButton));
                Check(LastBuyItem() == null, "case8 no-select no packet");
            }

            // ===== case9 关闭按钮：Hide =====
            {
                var scene = NewScene();
                var potionInfo = InfoOf(101, "HP药水", 100, 100, ItemType.Potion);
                GameScene.ItemInfoList.Add(potionInfo);
                GameSession.NpcGoods(new S.NPCGoods
                {
                    List = new List<UserItem> { GoodsItem(101, 42, 1, potionInfo) },
                    Rate = 1f,
                    Type = PanelType.Buy,
                });
                var dlg = scene.NPCGoodsDialog;
                PrepDialog(dlg);
                PrepButtons(dlg);
                Check(dlg.Visible, "case9 dialog visible");
                CMain.Time += 10000;
                Tap(Center(dlg.CloseButton));
                Check(!dlg.Visible, "case9 close hides");
            }

            // 还原全局 seam（防污染后续探针）。
            MobileNpc.SendCallNpc = p => global::Client.MirNetwork.Network.Enqueue(p);
            GameScene.NPCID = 0;
            GameScene.NPCTime = 0;
            GameScene.NPCRate = 1f;
            GameScene.ItemInfoList.Clear();
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.User = null;

            if (_fail == 0)
            {
                Console.WriteLine("[shopverify] PASS cases=9");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[shopverify] FAIL cases=9 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
