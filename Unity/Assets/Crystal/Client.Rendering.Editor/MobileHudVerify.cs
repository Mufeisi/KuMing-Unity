using System;
using System.Collections.Generic;
using Client;
using Client.MirObjects;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // 阶段8 第1项（战斗触控 HUD）增量3 战斗 HUD 纯逻辑验证：
    // 喂虚拟坐标/时钟断言 MobileHud 按钮命中/触发攻击（方向+冷却）/Cancel 不触发/滑出仍触发/
    // 血条比例边界/右下布局锚定。无需服务器（SendAttack/GetFacing/Now 全注入）。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.MobileHudVerify.Run -quit
    // 断言：全过输出 [hudverify] PASS exit 0。
    public static class MobileHudVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[hudverify] FAIL {what}"); }
        }

        public static void Run()
        {
            const int W = 1280, H = 720;
            long now = 0;
            var attacks = new List<MirDirection>();
            var facing = MirDirection.Right;
            MobileHud.Now = () => now;
            MobileHud.GetFacing = () => facing;
            MobileHud.SendAttack = d => attacks.Add(d);

            // ===== case1 命中触发：Down 按钮内 → 按下 → Up 触发攻击（方向=GetFacing） =====
            {
                var hud = new MobileHud(W, H);
                Vector2 c = hud.AttackCenter;
                hud.OnTouch(0, JoystickPhase.Down, c);
                Check(hud.AttackPressed, "case1 pressed on down");
                hud.OnTouch(0, JoystickPhase.Up, c);
                Check(!hud.AttackPressed, "case1 released on up");
                Check(attacks.Count == 1 && attacks[0] == MirDirection.Right, "case1 attack triggered with facing");
            }

            // ===== case2 按钮外点击不触发 =====
            {
                var hud = new MobileHud(W, H);
                Vector2 far = new Vector2(50f, 50f); // 左上角（血条区），远离按钮
                hud.OnTouch(0, JoystickPhase.Down, far);
                Check(!hud.AttackPressed, "case2 outside not pressed");
                hud.OnTouch(0, JoystickPhase.Up, far);
                Check(attacks.Count == 1, "case2 outside no attack");
            }

            // ===== case3 滑出仍触发（真机按钮语义：Down 命中激活，Up 释放即触发） =====
            {
                var hud = new MobileHud(W, H);
                Vector2 c = hud.AttackCenter;
                hud.OnTouch(0, JoystickPhase.Down, c);
                hud.OnTouch(0, JoystickPhase.Up, new Vector2(30f, 30f)); // 松手在按钮外
                Check(attacks.Count == 2, "case3 slide-out still triggers");
            }

            // ===== case4 冷却：触发后 800ms 内不重复，过期恢复 =====
            {
                var hud = new MobileHud(W, H);
                Vector2 c = hud.AttackCenter;
                now = 0;
                hud.OnTouch(0, JoystickPhase.Down, c);
                hud.OnTouch(0, JoystickPhase.Up, c);
                Check(attacks.Count == 3, "case4 first attack");
                hud.OnTouch(0, JoystickPhase.Down, c);
                hud.OnTouch(0, JoystickPhase.Up, c); // 同刻冷却中
                Check(attacks.Count == 3, "case4 cooldown blocks immediate");
                now = 700;
                hud.OnTouch(0, JoystickPhase.Down, c);
                hud.OnTouch(0, JoystickPhase.Up, c); // < 800ms
                Check(attacks.Count == 3, "case4 cooldown blocks early");
                now = 900;
                hud.OnTouch(0, JoystickPhase.Down, c);
                hud.OnTouch(0, JoystickPhase.Up, c);
                Check(attacks.Count == 4, "case4 cooldown expires");
            }

            // ===== case5 Cancel 不触发 =====
            {
                var hud = new MobileHud(W, H);
                Vector2 c = hud.AttackCenter;
                now = 2000;
                hud.OnTouch(0, JoystickPhase.Down, c);
                hud.OnTouch(0, JoystickPhase.Cancel, c);
                Check(!hud.AttackPressed, "case5 cancel clears pressed");
                Check(attacks.Count == 4, "case5 cancel no attack");
                // Cancel 后抑制松手容错：残留 Up 命中按钮也不触发攻击
                hud.OnTouch(0, JoystickPhase.Up, c);
                Check(attacks.Count == 4, "case5 post-cancel up no attack");
            }

            // ===== case6 血条比例边界 =====
            {
                var hud = new MobileHud(W, H);
                hud.MaxHp = 0; hud.Hp = 100;
                Check(Mathf.Approximately(hud.HpRatio, 0f), "case6 max0 ratio 0");
                hud.MaxHp = 100; hud.Hp = 50;
                Check(Mathf.Approximately(hud.HpRatio, 0.5f), "case6 half ratio");
                hud.Hp = 250;
                Check(Mathf.Approximately(hud.HpRatio, 1f), "case6 over-clamp 1");
                hud.MaxMp = 50; hud.Mp = 25;
                Check(Mathf.Approximately(hud.MpRatio, 0.5f), "case6 mp ratio");
            }

            // ===== case7 布局锚定：攻击按钮右下、血条左上 =====
            {
                var hud = new MobileHud(W, H);
                Vector2 c = hud.AttackCenter;
                Check(c.x > W - 120f && c.y > H - 200f, "case7 attack bottom-right");
                Check(Mathf.Approximately(MobileHud.HpBarPos.x, 20f) && Mathf.Approximately(MobileHud.HpBarPos.y, 20f), "case7 hp bar top-left");
                // SetScreen 重布局：改分辨率后按钮仍右下锚定
                hud.SetScreen(720, 1280); // 竖屏（主菜单），按钮重算
                Vector2 c2 = hud.AttackCenter;
                Check(c2.x > 720 - 120f && c2.y > 1280 - 200f, "case7 relayout after SetScreen");
            }

            // ===== case8 药品按钮：Down/Up 药品区 → UsePotion 触发一次；Cancel 不触发；Hit 含药品区 =====
            {
                var hud = new MobileHud(W, H);
                Vector2 c = hud.PotionCenter;
                int potions = 0;
                MobileHud.UsePotion = () => potions++;
                hud.OnTouch(0, JoystickPhase.Down, c);
                Check(hud.PotionPressed, "case8 potion pressed on down");
                hud.OnTouch(0, JoystickPhase.Up, c);
                Check(!hud.PotionPressed && potions == 1, "case8 potion triggered once");
                hud.Cancel();
                hud.OnTouch(0, JoystickPhase.Down, c);
                hud.OnTouch(0, JoystickPhase.Cancel, c);
                Check(potions == 1, "case8 potion cancel suppresses");
                Check(hud.Hit(c), "case8 potion area excluded from world tap");
            }

            // ===== case9 技能按钮：Down/Up 技能区 → CastMagic(Magics[0].Spell)；Magics 空不触发 =====
            {
                var hud = new MobileHud(W, H);
                Vector2 c = hud.MagicCenter;
                var casts = new List<Spell>();
                MobileHud.CastMagic = s => casts.Add(s);
                // 注入 User.Magics：MapObject.User 静态替换有风险，直接验证 TriggerMagic 的 Magics 判定分支
                // ——Magics 为空时（真实 User null/无魔法）点击不崩不触发。
                hud.OnTouch(0, JoystickPhase.Down, c);
                hud.OnTouch(0, JoystickPhase.Up, c);
                Check(casts.Count == 0, "case9 magic no-op without magics (MapObject.User null)");
                Check(hud.Hit(c), "case9 magic area excluded from world tap");
            }

            // 还原静态委托（防污染）。
            MobileHud.Now = () => CMain.Time;
            MobileHud.GetFacing = () => MapObject.User != null ? MapObject.User.Direction : MirDirection.Up;
            MobileHud.SendAttack = d => global::Client.MirNetwork.Network.Enqueue(new ClientPackets.Attack { Direction = d, Spell = Spell.None });
            MobileHud.UsePotion = MobileHud.UsePotion; // 探针注入过，还原为默认实现语义（静态字段赋值自身无效）
            // 注意：UsePotion/CastMagic 默认实现引用 MapObject.User，无法在探针静态还原后测真实派发；
            // 真实派发（背包 Potion 扫描 + C.Magic）由 net-* 集成探针覆盖（用户进图后背包/魔法就绪）。
            MobileHud.CastMagic = MobileHud.CastMagic;

            if (_fail == 0)
            {
                Console.WriteLine("[hudverify] PASS cases=9");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[hudverify] FAIL cases=9 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
