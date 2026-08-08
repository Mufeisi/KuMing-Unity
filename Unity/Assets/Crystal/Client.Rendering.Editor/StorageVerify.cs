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
    // 阶段8 第3项 增量3 仓库存取触控纯逻辑验证（无服务器）：
    // S.UserStorage → GameScene.Storage 填格 + StorageDialog.Show（连带打开背包）；S.NPCStorage → 关 NPC 对话
    // + 弹仓库；选中背包格 → 点空仓库格 → C.StoreItem{From=背包格,To=仓库格}（双格 Locked 防重复双击）；
    // 目标仓库格被占 → 静默不发包（MouseDown 快照修复：MirItemCell.OnMouseClick 会改写 SelectedCell，
    // 否则"选背包格点已占仓库格"被误判成取出）；点有物品仓库格 → C.TakeBackItem{From=仓库格,To=背包首空格}；
    // StoreItem/TakeBackItem 回声成功 → 本地交换 + 解锁；Storage1/2 分页可见性；CloseButton 关闭。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.StorageVerify.Run -quit
    // 断言：全过输出 [storageverify] PASS exit 0。
    public static class StorageVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[storageverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Scene/User/SelectedCell/Storage/ItemInfoList/Objects），建空场景 +
        // 背包（显式尺寸供父矩形命中）/NPC 对话框 + 玩家（GameScene.User 守卫 StorageDialog.Show，
        // MapObject.User 供 OnGridClick 取背包；BeltIdx=6 起扫空格）。
        static GameScene NewScene()
        {
            GameScene.Scene = null;
            GameScene.User = null;
            GameScene.SelectedCell = null;
            GameScene.Storage = new UserItem[Globals.StorageGridSize];
            GameScene.ItemInfoList.Clear();
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;

            var scene = new GameScene();
            GameScene.Scene = scene;
            var inv = new InventoryDialog { Parent = scene, Visible = false };
            inv.AutoSize = false;
            inv.Size = new Size(340, 240); // 空库下面板 AutoSize 回退 0×0 → 显式尺寸供格子 hover 命中
            scene.InventoryDialog = inv;
            scene.NPCDialog = new NPCDialog { Parent = scene, Visible = false };

            var user = new UserObject(1) { Name = "probe" };
            GameScene.User = user;
            MapObject.User = user;
            MapControl.User = user;
            return scene;
        }

        static ItemInfo InfoOf(int index, string name)
        {
            return new ItemInfo
            {
                Index = index,
                Name = name,
                Type = ItemType.Potion,
                Shape = 0,
                Weight = 1,
                Image = 1,
                Durability = 0,
                Price = 10,
                StackSize = 100,
                Stats = new Stats(),
            };
        }

        static UserItem MakeItem(int index, ulong uid, ushort count = 1)
        {
            var info = InfoOf(index, "it" + index);
            GameScene.ItemInfoList.Add(info);
            return new UserItem(info) { UniqueID = uid, Count = count };
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

        // 存取走 OnGridClick → Network.Enqueue 直发（非 seam）：用 SentPackets 队列捕获断言。
        static void DrainPackets()
        {
            while (Network.SentPackets.TryDequeue(out _)) { }
        }

        static C.StoreItem LastStoreItem()
        {
            C.StoreItem result = null;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.StoreItem s) result = s;
            return result;
        }

        static C.TakeBackItem LastTakeBackItem()
        {
            C.TakeBackItem result = null;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.TakeBackItem t) result = t;
            return result;
        }

        // 空库（Library null）下对话框/按钮 AutoSize 回退 0×0 → 显式尺寸供父矩形命中。
        static void PrepStorage(StorageDialog dlg)
        {
            dlg.AutoSize = false;
            dlg.Size = new Size(400, 350);
            dlg.Storage1Button.AutoSize = false;
            dlg.Storage1Button.Size = new Size(60, 23);
            dlg.Storage2Button.AutoSize = false;
            dlg.Storage2Button.Size = new Size(60, 23);
            dlg.CloseButton.AutoSize = false;
            dlg.CloseButton.Size = new Size(30, 30);
        }

        // 打开仓库（UserStorage 分发，StorageDialog 懒建+Show），随后 Prep 供交互。
        static StorageDialog OpenStorage(GameScene scene, UserItem[] storage = null)
        {
            if (storage != null)
            {
                for (int i = 0; i < storage.Length && i < GameScene.Storage.Length; i++)
                    GameScene.Storage[i] = storage[i];
            }
            GameSession.UserStorage(new S.UserStorage { Storage = new UserItem[0] });
            var dlg = scene.StorageDialog;
            PrepStorage(dlg);
            // StorageDialog.Show 在 Size=0×0 时把背包摆到 (5,0)；撑大仓库面板后两者重叠会截获背包点击 →
            // 把背包右移到仓库右侧。
            scene.InventoryDialog.Location = new MPoint(dlg.Size.Width + 5, 0);
            return dlg;
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;

            // ===== case1 S.UserStorage 分发：填格 + 弹仓库框 + 背包连带 =====
            {
                var scene = NewScene();
                var item = MakeItem(201, 11);
                GameSession.UserStorage(new S.UserStorage { Storage = new[] { item } });
                Check(GameScene.Storage[0] == item, "case1 storage filled");
                var dlg = scene.StorageDialog;
                Check(dlg != null && dlg.Visible, "case1 dialog created+visible");
                Check(scene.InventoryDialog.Visible, "case1 Show opens inventory");
            }

            // ===== case2 S.NPCStorage：关 NPC 对话 + 弹仓库 =====
            {
                var scene = NewScene();
                scene.NPCDialog.Visible = true;
                GameSession.NpcStorage();
                Check(!scene.NPCDialog.Visible, "case2 npc dialog hidden");
                var dlg = scene.StorageDialog;
                Check(dlg != null && dlg.Visible, "case2 storage dialog shown");
            }

            // ===== case3 选中背包格 → 点空仓库格 → C.StoreItem{From=6,To=0} + 双格 Locked =====
            {
                var scene = NewScene();
                var sword = MakeItem(202, 12);
                MapObject.User.Inventory[6] = sword; // InventoryDialog.Grid[0].ItemSlot=6
                var dlg = OpenStorage(scene);
                DrainPackets();
                CMain.Time += 10000;
                Tap(Center(scene.InventoryDialog.Grid[0])); // ItemSlot=6
                Check(GameScene.SelectedCell == scene.InventoryDialog.Grid[0], "case3 bag cell selected");
                CMain.Time += 1000;
                Tap(Center(dlg.Grid[0])); // 空仓库格
                var store = LastStoreItem();
                Check(store != null && store.From == 6 && store.To == 0, "case3 deposit packet");
                Check(scene.InventoryDialog.Grid[0].Locked && dlg.Grid[0].Locked, "case3 both locked");
            }

            // ===== case4 目标仓库格被占 → 静默不发包（MouseDown 快照修复） =====
            {
                var scene = NewScene();
                var sword = MakeItem(203, 13);
                MapObject.User.Inventory[6] = sword;
                var occupied = MakeItem(204, 14);
                var dlg = OpenStorage(scene, new[] { occupied }); // 仓库格0被占
                DrainPackets();
                CMain.Time += 10000;
                Tap(Center(scene.InventoryDialog.Grid[0])); // 选背包格
                CMain.Time += 1000;
                Tap(Center(dlg.Grid[0])); // 点已占仓库格
                Check(LastStoreItem() == null, "case4 no deposit packet");
                Check(LastTakeBackItem() == null, "case4 no withdraw packet");
            }

            // ===== case5 点有物品仓库格（无背包选中）→ C.TakeBackItem{From=0,To=6} =====
            {
                var scene = NewScene();
                var stored = MakeItem(205, 15);
                var dlg = OpenStorage(scene, new[] { stored });
                DrainPackets();
                CMain.Time += 10000;
                Tap(Center(dlg.Grid[0])); // 仓库格0有物品，背包空 → 找首空格（BeltIdx=6）
                var take = LastTakeBackItem();
                Check(take != null && take.From == 0 && take.To == 6, "case5 withdraw packet");
                Check(dlg.Grid[0].Locked, "case5 storage cell locked");
            }

            // ===== case6 StoreItem 回声成功：本地交换 + 解锁 =====
            {
                var scene = NewScene();
                var sword = MakeItem(206, 16);
                MapObject.User.Inventory[6] = sword;
                var dlg = OpenStorage(scene);
                scene.InventoryDialog.Grid[0].Locked = true;
                dlg.Grid[0].Locked = true;
                GameSession.StoreItem(new S.StoreItem { From = 6, To = 0, Success = true });
                Check(GameScene.Storage[0] == sword && MapObject.User.Inventory[6] == null, "case6 deposit echo swapped");
                Check(!scene.InventoryDialog.Grid[0].Locked && !dlg.Grid[0].Locked, "case6 both unlocked");
            }

            // ===== case7 TakeBackItem 回声成功：本地交换 + 解锁 =====
            {
                var scene = NewScene();
                var stored = MakeItem(207, 17);
                var dlg = OpenStorage(scene, new[] { stored });
                scene.InventoryDialog.Grid[0].Locked = true; // 槽位6 → Grid[0]
                dlg.Grid[0].Locked = true;
                GameSession.TakeBackItem(new S.TakeBackItem { From = 0, To = 6, Success = true });
                Check(MapObject.User.Inventory[6] == stored && GameScene.Storage[0] == null, "case7 withdraw echo swapped");
                Check(!scene.InventoryDialog.Grid[0].Locked && !dlg.Grid[0].Locked, "case7 both unlocked");
            }

            // ===== case8 分页：Storage1/2 切换网格可见性与按钮态 =====
            {
                var scene = NewScene();
                var dlg = OpenStorage(scene);
                Check(dlg.Grid[0].Visible && !dlg.Grid[Globals.StorageGridSize].Visible, "case8 page1 grid visibility");
                Check(dlg.Storage1Button.Index == 743, "case8 page1 btn state");
                CMain.Time += 10000;
                Tap(Center(dlg.Storage2Button)); // RefreshStorage2：无扩展租赁 → 全部格隐藏 + RentalLabel
                Check(dlg.Storage1Button.Index == 744 && dlg.Storage2Button.Index == 745, "case8 page2 btn state");
                Check(!dlg.Grid[0].Visible && dlg.RentalLabel.Visible, "case8 page2 locked grid");
                CMain.Time += 1000;
                Tap(Center(dlg.Storage1Button)); // RefreshStorage1 回页1
                Check(dlg.Grid[0].Visible && dlg.Storage1Button.Index == 743, "case8 page1 restored");
            }

            // ===== case9 CloseButton → Hide =====
            {
                var scene = NewScene();
                var dlg = OpenStorage(scene);
                Check(dlg.Visible, "case9 dialog visible");
                CMain.Time += 10000;
                Tap(Center(dlg.CloseButton));
                Check(!dlg.Visible, "case9 close hides");
            }

            // 还原全局 seam（防污染后续探针）。
            GameScene.SelectedCell = null;
            GameScene.Storage = new UserItem[Globals.StorageGridSize];
            GameScene.ItemInfoList.Clear();
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.User = null;

            if (_fail == 0)
            {
                Console.WriteLine("[storageverify] PASS cases=9");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[storageverify] FAIL cases=9 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
