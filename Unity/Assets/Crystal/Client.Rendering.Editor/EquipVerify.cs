using System;
using System.Linq;
using Client;
using Client.MirControls;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using Client.MirScenes.Dialogs;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;
using C = ClientPackets;
using S = ServerPackets;
using MPoint = Crystal.Client.Core.MirMath.Point;
using Size = Crystal.Client.Core.MirMath.Size;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第2项 增量3 装备穿戴纯逻辑验证（无服务器）：
    // 双击背包格→C.EquipItem（可穿戴 + 锁双格）；不可穿戴（等级不足）→不发包；S.EquipItem
    // 回流→数组交换 + 属性重算 + 双格解锁；双击装备格→C.RemoveItem；S.RemoveItem 回流→迁回背包。
    // 双击走 GameScene 双击分发（Mir 鼠标链 + 500ms 窗口），与真机/PC 双击同链路。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.EquipVerify.Run -quit
    // 断言：全过输出 [equipverify] PASS exit 0。
    public static class EquipVerify
    {
        static int _fail;
        static UserObject _user;
        static InventoryDialog _inv;
        static CharacterDialog _chr;
        static UserItem _sword, _swordHigh;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[equipverify] FAIL {what}"); }
        }

        static ItemInfo MakeInfo(uint id, string name, ItemType type, RequiredType reqType, byte reqAmount)
        {
            var info = new ItemInfo
            {
                Index = 3001 + (int)id,
                Name = name,
                Type = type,
                Shape = 1,
                Weight = 5,
                Image = 1,
                Durability = 10,
                StackSize = 1,
                RequiredGender = RequiredGender.Male,
                RequiredClass = RequiredClass.Warrior,
                RequiredType = reqType,
                RequiredAmount = reqAmount,
                Stats = new Stats(),
            };
            info.Stats[Stat.MaxDC] = 15;
            info.Stats[Stat.MinDC] = 5;
            return info;
        }

        static UserItem MakeItem(uint id, string name, ItemType type, RequiredType reqType = RequiredType.Level, byte reqAmount = 0)
        {
            return new UserItem(MakeInfo(id, name, type, reqType, reqAmount)) { UniqueID = id, CurrentDura = 10, MaxDura = 10 };
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

        // 双击：两次点击间隔 200ms < DoubleClickTimeMs=500。时钟单调递增（CMain.Time 静态可设）：
        // 若每次重置回 1000，时间倒退会命中上一次双点残留的 _lastClickTime 把首击误判成双击，
        // 窗口被清空后次击反而落单击——连续用例间双击链就断了。
        static long _clock;
        static void DoubleTap(MPoint p)
        {
            _clock += 1000;
            CMain.Time = _clock;
            Tap(p);
            _clock += 200;
            CMain.Time = _clock;
            Tap(p);
        }

        static MPoint Center(MirControl c)
        {
            var r = c.DisplayRectangle;
            return new MPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        static void DrainPackets()
        {
            while (Network.SentPackets.TryDequeue(out _)) { }
        }

        // 最近一次 C.EquipItem（有则取，无则 null）。
        static C.EquipItem LastEquip()
        {
            C.EquipItem result = null;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.EquipItem eq) result = eq;
            return result;
        }

        static C.RemoveItem LastRemove()
        {
            C.RemoveItem result = null;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.RemoveItem rm) result = rm;
            return result;
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;
            CMain.Time = 0;
            _clock = 0;
            GameScene.Scene = new GameScene();
            _user = new UserObject(100) { Class = MirClass.Warrior, Gender = MirGender.Male, Level = 1 };
            GameScene.User = _user;
            MapObject.User = _user;
            GameScene.Gold = 12345;
            GameScene.SelectedCell = null;
            GameScene.HoverItem = null;
            GameScene.Scene.BuffsDialog = new BuffDialog(); // RefreshStats 的 RefreshBuffs 依赖（空 Buffs）

            _sword = MakeItem(21, "ProbeEquipSword", ItemType.Weapon);
            _swordHigh = MakeItem(22, "ProbeEquipSwordHigh", ItemType.Weapon, RequiredType.Level, 10);
            _user.Inventory[6] = _sword;       // Grid[0].ItemSlot=6
            _user.Inventory[7] = _swordHigh;   // Grid[1].ItemSlot=7

            // 背包窗口（AutoSize 陷阱同 baginteractverify：关 AutoSize 回落显式尺寸）。
            _inv = new InventoryDialog { Parent = GameScene.Scene };
            GameScene.Scene.InventoryDialog = _inv;
            _inv.AutoSize = false;
            _inv.ItemButton.AutoSize = false;
            _inv.ItemButton2.AutoSize = false;
            _inv.QuestButton.AutoSize = false;
            _inv.CloseButton.AutoSize = false;
            _inv.AddButton.AutoSize = false;
            _inv.Size = new Size(340, 240);
            _inv.CloseButton.Size = new Size(40, 40);
            _inv.Visible = true;
            _inv.RefreshInventory();
            _inv.Process();

            // 装备窗口（8-2-3）：ctor 注入 Actor；隐藏创建（MirControl.Visible 默认 true）。
            // Parent=Scene 显式挂：Mir 鼠标链 hit 走 Scene.Controls 子树，不挂父则装备格收不到双击。
            _chr = new CharacterDialog(MirGridType.Equipment, _user)
            {
                Parent = GameScene.Scene,
                Visible = false,
            };
            GameScene.Scene.CharacterDialog = _chr;
            _chr.AutoSize = false;
            _chr.Size = new Size(264, 270);
            // CharacterPage 显式尺寸：batchmode Prguse 库 null → 页面 AutoSize 退化 (0,0)，
            // IsMouseOver 假使装备格子树不可达（鼠标链停在 Dialog 本级）。给覆盖全部槽位的尺寸。
            _chr.CharacterPage.AutoSize = false;
            _chr.CharacterPage.Size = new Size(240, 260);

            // 初始化 Stats（HandWeight=12/WearWeight=15 来自 CoreStats Warrior 基值）：
            // CanWearItem 负重校验依赖，且 ApplyEquip 内 RefreshStats 重算属性。
            _user.RefreshStats();
            DrainPackets();

            // ===== case1 可装备：双击背包武器格 → C.EquipItem + 锁双格 =====
            {
                var cell = _inv.Grid[0];
                DoubleTap(Center(cell));
                var eq = LastEquip();
                Check(eq != null, "case1 equip packet sent");
                Check(eq != null && eq.UniqueID == _sword.UniqueID, "case1 equip uniqueID");
                Check(eq != null && eq.To == (int)EquipmentSlot.Weapon, "case1 equip to weapon slot");
                Check(eq != null && eq.Grid == MirGridType.Inventory, "case1 equip from inventory");
                Check(cell.Locked, "case1 source cell locked");
                Check(_chr.Grid[(int)EquipmentSlot.Weapon].Locked, "case1 target cell locked");
                Check(GameScene.SelectedCell == null, "case1 double-click clears selection");
                Check(_sword.Info.Type == ItemType.Weapon && _user.CurrentHandWeight == 0, "case1 not yet equipped (no response applied)");
            }

            // ===== case2 不可装备：等级不足 → 不发包、不锁格 =====
            {
                DrainPackets();
                DoubleTap(Center(_inv.Grid[1])); // _swordHigh RequiredAmount=10 > Level 1
                Check(LastEquip() == null, "case2 reject sends no equip packet");
                Check(Network.SentPackets.IsEmpty, "case2 reject no packet at all");
                Check(_inv.Grid[1].Locked == false, "case2 reject no source lock");
                Check(_chr.Grid[(int)EquipmentSlot.Weapon].Locked, "case2 reject leaves case1 pending lock");
            }

            // ===== case3 状态回流：S.EquipItem 成功 → 数组交换 + 属性 + 双格解锁 =====
            {
                GameSession.EquipItem(new S.EquipItem
                {
                    Grid = MirGridType.Equipment,
                    UniqueID = _sword.UniqueID,
                    To = (int)EquipmentSlot.Weapon,
                    Success = true,
                });
                Check(_user.Inventory[6] == null, "case3 inventory slot freed");
                Check(_user.Equipment[(int)EquipmentSlot.Weapon] == _sword, "case3 equipment slot bound");
                Check(_user.Stats[Stat.MaxDC] >= 15, "case3 stats refreshed (MaxDC from item)");
                Check(_inv.Grid[0].Locked == false, "case3 source unlocked");
                Check(_chr.Grid[(int)EquipmentSlot.Weapon].Locked == false, "case3 target unlocked");
            }

            // ===== case4 卸下：双击装备格 → C.RemoveItem（目标=首个空背包格，从 BeltIdx 起扫） =====
            {
                _chr.Visible = true; // 装备格可命中
                DrainPackets();
                DoubleTap(Center(_chr.Grid[(int)EquipmentSlot.Weapon]));
                var rm = LastRemove();
                Check(rm != null, "case4 remove packet sent");
                Check(rm != null && rm.UniqueID == _sword.UniqueID, "case4 remove uniqueID");
                Check(rm != null && rm.Grid == MirGridType.Inventory, "case4 remove to inventory");
                Check(rm != null && rm.To >= _user.BeltIdx, "case4 remove target beyond belt");
                Check(rm != null && rm.To < _user.Inventory.Length, "case4 remove target in range");
                Check(_chr.Grid[(int)EquipmentSlot.Weapon].Locked, "case4 equipment cell locked");
            }

            // ===== case5 卸下回流：S.RemoveItem 成功 → 装备格清空、物品迁回背包 =====
            {
                GameSession.RemoveItem(new S.RemoveItem
                {
                    Grid = MirGridType.Inventory,
                    UniqueID = _sword.UniqueID,
                    To = 6,
                    Success = true,
                });
                Check(_user.Equipment[(int)EquipmentSlot.Weapon] == null, "case5 equipment slot cleared");
                Check(_user.Inventory[6] == _sword, "case5 item back in inventory");
                Check(_chr.Grid[(int)EquipmentSlot.Weapon].Locked == false, "case5 equipment cell unlocked");
            }

            // 还原静态（防污染后续探针）。
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            GameScene.SelectedCell = null;
            GameScene.HoverItem = null;
            MirControl.MouseControl = null;
            MirControl.ActiveControl = null;
            DrainPackets();
            CMain.Time = 0;

            if (_fail == 0)
            {
                Console.WriteLine("[equipverify] PASS cases=5");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[equipverify] FAIL cases=5 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
