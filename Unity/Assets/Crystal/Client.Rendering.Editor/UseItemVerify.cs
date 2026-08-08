using System;
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
    // 阶段8 第2项 增量4 药品使用纯逻辑验证（无服务器）：
    // 双击背包药水格→C.UseItem（数量多瓶 + 锁格）；S.UseItem 成功回流→数量-1 / 最后一瓶清格 / 解锁；
    // 失败回流→解锁但不扣数；满血不溢出：客户端不本地补血（HP 恢复走独立 S.HealthChanged，
    // 服务器权威封顶），回流后 HP 恒不变；非消耗品（武器）→ 走 C.EquipItem 链、不发 C.UseItem。
    // 双击走 GameScene 双击分发（Mir 鼠标链 + 500ms 窗口），与真机/PC 双击同链路。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.UseItemVerify.Run -quit
    // 断言：全过输出 [useitemverify] PASS exit 0。
    public static class UseItemVerify
    {
        static int _fail;
        static UserObject _user;
        static InventoryDialog _inv;
        static UserItem _potion, _potionLast, _potionFail, _sword;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[useitemverify] FAIL {what}"); }
        }

        static ItemInfo MakePotionInfo(uint id)
        {
            var info = new ItemInfo
            {
                Index = 3101 + (int)id,
                Name = "ProbeUsePotion" + id,
                Type = ItemType.Potion,
                Shape = 1,
                Weight = 1,
                Image = 1,
                StackSize = 5,
                RequiredGender = RequiredGender.Male,
                RequiredClass = RequiredClass.Warrior,
                Stats = new Stats(),
            };
            return info;
        }

        static UserItem MakePotion(uint id, ushort count)
        {
            return new UserItem(MakePotionInfo(id)) { UniqueID = id, Count = count };
        }

        static UserItem MakeWeapon(uint id)
        {
            var info = new ItemInfo
            {
                Index = 3201 + (int)id,
                Name = "ProbeUseSword" + id,
                Type = ItemType.Weapon,
                Shape = 1,
                Weight = 5,
                Image = 1,
                StackSize = 1,
                RequiredGender = RequiredGender.Male,
                RequiredClass = RequiredClass.Warrior,
                RequiredType = RequiredType.Level,
                RequiredAmount = 0,
                Stats = new Stats(),
            };
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

        // 最近一次 C.UseItem（有则取，无则 null）。
        static C.UseItem LastUse()
        {
            C.UseItem result = null;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.UseItem use) result = use;
            return result;
        }

        static C.EquipItem LastEquip()
        {
            C.EquipItem result = null;
            foreach (var p in Network.SentPackets.ToArray())
                if (p is C.EquipItem eq) result = eq;
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

            _potion = MakePotion(31, 3);       // StackSize=5, Count=3
            _potionLast = MakePotion(32, 1);   // 最后一瓶（用后清格）
            _potionFail = MakePotion(33, 2);   // 失败回流不扣数
            _sword = MakeWeapon(34);
            _user.Inventory[6] = _potion;
            _user.Inventory[7] = _potionLast;
            _user.Inventory[8] = _potionFail;
            _user.Inventory[9] = _sword;

            // 背包窗口（AutoSize 陷阱同 equipverify：关 AutoSize 回落显式尺寸）。
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

            // 装备窗口（8-2-3 同款）：UseItem 的 dialog 守卫（scene.CharacterDialog）对药水分支也前置，
            // 真机恒存在；探针须显式建否则 289 行早退。隐藏创建即可（药水分支不读装备格）。
            var chr = new CharacterDialog(MirGridType.Equipment, _user) { Parent = GameScene.Scene, Visible = false };
            GameScene.Scene.CharacterDialog = chr;
            chr.AutoSize = false;
            chr.Size = new Size(264, 270);
            chr.CharacterPage.AutoSize = false;
            chr.CharacterPage.Size = new Size(240, 260);

            // 初始化 Stats（RefreshStats 内 RefreshBuffs 等依赖非空 BuffsDialog）。
            _user.RefreshStats();
            DrainPackets();

            // ===== case1 药品双击：多瓶 → C.UseItem + 锁格 =====
            {
                var cell = _inv.Grid[0]; // ItemSlot=6
                DoubleTap(Center(cell));
                var use = LastUse();
                Check(use != null, "case1 use packet sent");
                Check(use != null && use.UniqueID == _potion.UniqueID, "case1 use uniqueID");
                Check(use != null && use.Grid == MirGridType.Inventory, "case1 use from inventory");
                Check(cell.Locked, "case1 source cell locked");
                Check(GameScene.SelectedCell == null, "case1 double-click clears selection");
                Check(_potion.Count == 3, "case1 count unchanged before response");
            }

            // ===== case2 用后数量-1：S.UseItem 成功回流 → 扣数 + 解锁 =====
            {
                GameSession.UseItem(new S.UseItem { UniqueID = _potion.UniqueID, Success = true, Grid = MirGridType.Inventory });
                Check(_potion.Count == 2, "case2 count decremented");
                Check(_inv.Grid[0].Item == _potion, "case2 cell still holds potion");
                Check(_inv.Grid[0].Locked == false, "case2 cell unlocked");
            }

            // ===== case3 满血不溢出：客户端不本地补血，回流后 HP 不变 =====
            {
                _user.HP = 500;
                GameSession.UseItem(new S.UseItem { UniqueID = _potion.UniqueID, Success = true, Grid = MirGridType.Inventory });
                Check(_potion.Count == 1, "case3 count decremented again");
                Check(_user.HP == 500, "case3 no local heal (overflow impossible; HP via S.HealthChanged)");
                Check(_inv.Grid[0].Locked == false, "case3 cell unlocked");
            }

            // ===== case4 失败回流：S.UseItem Success=false → 解锁但不扣数 =====
            {
                var cell = _inv.Grid[2]; // ItemSlot=8, _potionFail Count=2
                GameSession.UseItem(new S.UseItem { UniqueID = _potionFail.UniqueID, Success = false, Grid = MirGridType.Inventory });
                Check(_potionFail.Count == 2, "case4 count unchanged on failure");
                Check(cell.Locked == false, "case4 cell unlocked");
            }

            // ===== case5 最后一瓶：S.UseItem 成功回流 → 清格 =====
            {
                var cell = _inv.Grid[1]; // ItemSlot=7, _potionLast Count=1
                GameSession.UseItem(new S.UseItem { UniqueID = _potionLast.UniqueID, Success = true, Grid = MirGridType.Inventory });
                Check(_inv.Grid[1].Item == null, "case5 last potion clears cell");
                Check(cell.Locked == false, "case5 cell unlocked");
            }

            // ===== case6 非药水拒绝：武器双击 → 走 C.EquipItem 链，不发 C.UseItem =====
            {
                DrainPackets();
                DoubleTap(Center(_inv.Grid[3])); // ItemSlot=9, _sword
                Check(LastUse() == null, "case6 no use packet for weapon");
                Check(LastEquip() != null, "case6 weapon routes to equip chain");
                Check(_sword.Info.Type == ItemType.Weapon, "case6 sanity: item is weapon");
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
                Console.WriteLine("[useitemverify] PASS cases=6");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[useitemverify] FAIL cases=6 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
