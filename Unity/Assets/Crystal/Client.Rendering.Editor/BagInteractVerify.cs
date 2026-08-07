using System;
using Client;
using Client.MirControls;
using Client.MirObjects;
using Client.MirScenes;
using Client.MirScenes.Dialogs;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;
using MPoint = Crystal.Client.Core.MirMath.Point;
using Size = Crystal.Client.Core.MirMath.Size;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第2项 增量2 背包面板触控交互纯逻辑验证（无服务器）：
    // 喂虚拟坐标断言 MirItemCell 点格选中/空格取消/越界忽略/切页状态/切页清选中/关闭清选中/任务页。
    // 命中定位走 8-0 适配层 UiHitTest（DialogRoot→GameScene.Scene 真实树），点按分发走 Mir 鼠标链
    // （GameScene.OnMouseMove/Down/Up/Click，与 TouchInputAdapter 同链路）。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.BagInteractVerify.Run -quit
    // 断言：全过输出 [baginteractverify] PASS exit 0。
    public static class BagInteractVerify
    {
        static int _fail;
        static UserObject _user;
        static UserItem _sword;
        static InventoryDialog _inv;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[baginteractverify] FAIL {what}"); }
        }

        static UserItem MakeItem(uint id, string name)
        {
            var info = new ItemInfo
            {
                Index = 2001 + (int)id,
                Name = name,
                Type = ItemType.Weapon,
                Shape = 1,
                Weight = 5,
                Image = 1,
                Durability = 10,
                StackSize = 1,
                Stats = new Stats(),
            };
            info.Stats[Stat.MaxDC] = 15;
            info.Stats[Stat.MinDC] = 5;
            return new UserItem(info) { UniqueID = id, CurrentDura = 10, MaxDura = 10 };
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

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;
            GameScene.Scene = new GameScene();
            _user = new UserObject();
            GameScene.User = _user;
            MapObject.User = _user;
            GameScene.Gold = 12345;
            GameScene.SelectedCell = null;
            GameScene.HoverItem = null;

            _sword = MakeItem(11, "ProbeBagSword");
            _user.Inventory[6] = _sword; // Grid[0].ItemSlot=6

            _inv = new InventoryDialog { Parent = GameScene.Scene };
            GameScene.Scene.InventoryDialog = _inv;
            // batchmode 无库数据：MirImageControl.Size getter 在 AutoSize&&Library!=null&&Index>=0 时
            // 返回 Library.GetTrueSize(Index)=(0,0)，吞掉 ctor 显式尺寸（面板/按钮 Size 全 0 → IsMouseOver
            // 永不命中）。探针统一关 AutoSize 回落 base.Size（面板 340x240、按钮 72x23）。
            _inv.AutoSize = false;
            _inv.ItemButton.AutoSize = false;
            _inv.ItemButton2.AutoSize = false;
            _inv.QuestButton.AutoSize = false;
            _inv.CloseButton.AutoSize = false;
            _inv.AddButton.AutoSize = false;
            _inv.Size = new Size(340, 240); // 面板加宽覆盖 CloseButton 全宽（289..329，300 宽会被截出面板外）
            _inv.CloseButton.Size = new Size(40, 40); // CloseButton ctor 无显式尺寸（依赖库帧），探针显式设
            _inv.Visible = true;
            _inv.RefreshInventory();
            _inv.Process();

            // ===== case1 点格命中（有物品）→ SelectedCell + Tooltip =====
            {
                MPoint c = Center(_inv.Grid[0]);
                Check(MobileUiAdapter.UiHitTest(c), "case1 adapter hits cell area");
                Tap(c);
                Check(GameScene.SelectedCell == _inv.Grid[0], "case1 select item cell");
                Check(GameScene.HoverItem == _sword && GameScene.Scene.ItemLabel != null && !GameScene.Scene.ItemLabel.IsDisposed, "case1 tooltip created");
            }

            // ===== case2 空格取消选中（跨网格守卫：仅清本网格） =====
            {
                MPoint c = Center(_inv.Grid[1]); // ItemSlot=7 空
                Check(GameScene.SelectedCell == _inv.Grid[0], "case2 still selected before");
                Tap(c);
                Check(GameScene.SelectedCell == null, "case2 empty cell clears selection");
            }

            // ===== case3 越界忽略：面板外点按不改选中、不崩 =====
            {
                Tap(Center(_inv.Grid[0]));
                Check(GameScene.SelectedCell == _inv.Grid[0], "case3 reselected");
                Tap(new MPoint(1000, 600)); // 面板外
                Check(GameScene.SelectedCell == _inv.Grid[0], "case3 outside tap ignored");
                Check(!MobileUiAdapter.UiHitTest(new MPoint(1000, 600)), "case3 adapter misses outside");
            }

            // ===== case4 切页状态（物品2 页）：扩包后 46.. 槽格显、首页格隐 =====
            {
                _user.Inventory = new UserItem[56];
                _user.Inventory[6] = _sword;          // 首页保格（数组替换会丢 _sword，case5/6 重选需要）
                _user.Inventory[46] = MakeItem(12, "ProbeBagSword2"); // Grid[40].ItemSlot=46
                Tap(Center(_inv.ItemButton2));
                Check(!_inv.Grid[0].Visible, "case4 page1 hidden");
                Check(_inv.Grid[40].Visible, "case4 page2 grid40 visible");
                Check(_inv.Grid[40].Item == _user.Inventory[46], "case4 grid40 binds slot46");
                Tap(Center(_inv.Grid[40]));
                Check(GameScene.SelectedCell == _inv.Grid[40], "case4 select page2 cell");
            }

            // ===== case5 切页清选中：回首页重选 → 切页 → 选中清 =====
            {
                Tap(Center(_inv.ItemButton));
                Check(_inv.Grid[0].Visible, "case5 page1 restored");
                Tap(Center(_inv.Grid[0]));
                Check(GameScene.SelectedCell == _inv.Grid[0], "case5 selected page1");
                Tap(Center(_inv.ItemButton2)); // 切页
                Check(GameScene.SelectedCell == null, "case5 page switch clears selection");
                Tap(Center(_inv.ItemButton)); // 回首页
            }

            // ===== case6 关闭清选中 + Tooltip 释放 =====
            {
                Tap(Center(_inv.Grid[0]));
                Check(GameScene.SelectedCell == _inv.Grid[0], "case6 selected before close");
                Tap(Center(_inv.CloseButton));
                Check(!_inv.Visible, "case6 dialog hidden");
                Check(GameScene.SelectedCell == null, "case6 close clears selection");
                Check(GameScene.Scene.ItemLabel == null || GameScene.Scene.ItemLabel.IsDisposed, "case6 close disposes tooltip");
                Check(GameScene.HoverItem == null, "case6 hover cleared");
            }

            // ===== case7 任务页：QuestButton 切任务格 =====
            {
                _inv.Visible = true;
                _inv.RefreshInventory();
                Tap(Center(_inv.QuestButton));
                Check(!_inv.Grid[0].Visible, "case7 bag grid hidden");
                Check(_inv.QuestGrid[0].Visible, "case7 quest grid visible");
            }

            // 还原静态（防污染后续探针）。
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            GameScene.SelectedCell = null;
            GameScene.HoverItem = null;
            MirControl.MouseControl = null;
            MirControl.ActiveControl = null;

            if (_fail == 0)
            {
                Console.WriteLine("[baginteractverify] PASS cases=7");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[baginteractverify] FAIL cases=7 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
