using System;
using Client;
using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using UnityEngine;
using C = ClientPackets;

namespace Crystal.Client.Rendering
{
    // 阶段8 第1项（战斗触控 HUD）增量3：战斗 HUD（纯逻辑层 + 渲染适配）。
    // 右侧攻击按钮（按下命中/冷却/触发 C.Attack 面向玩家当前方向）+ 左上玩家血条（HP/MP）。
    // 与 Unity Input 解耦（OnTouch 喂入 JoystickPhase，MobileBootstrap 从 Input.touches 映射），
    // 可确定性单测（MobileHudVerify）。技能/药品/拾取按钮属阶段8 后续项，YAGNI 不预留。
    public sealed class MobileHud
    {
        // 时钟/动作注入（探针替换捕获），默认真实 Network.Enqueue。
        public static Func<long> Now = () => CMain.Time;
        public static Func<MirDirection> GetFacing = () => MapObject.User != null ? MapObject.User.Direction : MirDirection.Up;
        public static Action<MirDirection> SendAttack = d => Network.Enqueue(new C.Attack { Direction = d, Spell = Spell.None });
        // 8-11 G8 缺口补全：药品按钮（背包第一个 Potion → C.UseItem）/ 技能按钮（Magics[0].Spell → C.Magic）。
        // 探针注入替换捕获（不依赖真实背包/网络）；默认实现见 UseFirstPotion/CastFirstMagic。
        public static Action UsePotion = UseFirstPotion;
        public static Action<Spell> CastMagic = CastFirstMagic;

        public const int AttackCooldownMs = 800; // 攻击冷却（对齐 MobileCombat.AttackCooldownMs，UX 层）
        public const float AttackRadius = 60f;   // 攻击按钮半径（px）
        public const float PotionRadius = 40f;   // 药品按钮半径（px）
        public const float MagicRadius = 40f;    // 技能按钮半径（px）

        // 血条布局（左上角，px）
        public static readonly Vector2 HpBarPos = new Vector2(20f, 20f);
        public static readonly Vector2 HpBarSize = new Vector2(180f, 14f);
        public static readonly float MpBarGap = 6f;

        public int ScreenW, ScreenH;
        // 玩家血条数据（外部同步：GameSession HP/MP → Hp/Mp，S.UserInformation MaxHP/MaxMP → MaxHp/MaxMp）。
        public int Hp, Mp, MaxHp, MaxMp;

        Vector2 _attackCenter;
        Vector2 _hpPos; // 血条实际位置（基准 HpBarPos + 安全区偏移，8-5-1）
        Vector2 _potionCenter, _magicCenter;
        bool _attackPressed, _potionPressed, _magicPressed;

        public MobileHud(int screenW, int screenH)
        {
            ScreenW = screenW;
            ScreenH = screenH;
            RecomputeLayout();
        }

        public void SetScreen(int w, int h)
        {
            ScreenW = w;
            ScreenH = h;
            RecomputeLayout();
        }

        // 布局重算：右下攻击按钮圆心 + 左上血条 + 左下药品按钮 + 攻击上方技能按钮
        //（安全区偏移：刘海顶→血条下移、Home indicator 底→按钮上抬、左右 inset→内缩）。
        void RecomputeLayout()
        {
            _attackCenter = new Vector2(ScreenW - SafeArea.Right - 90f, ScreenH - SafeArea.Bottom - 160f);
            _hpPos = new Vector2(SafeArea.Left + HpBarPos.x, SafeArea.Top + HpBarPos.y);
            _potionCenter = new Vector2(SafeArea.Left + 70f, ScreenH - SafeArea.Bottom - 90f);
            _magicCenter = new Vector2(_attackCenter.x, _attackCenter.y - 100f);
        }

        public Vector2 AttackCenter => _attackCenter;
        public Vector2 PotionCenter => _potionCenter;
        public Vector2 MagicCenter => _magicCenter;
        public Vector2 HpPos => _hpPos; // 安全区生效的血条渲染位置（探针断言）
        public bool AttackPressed => _attackPressed;
        public bool PotionPressed => _potionPressed;
        public bool MagicPressed => _magicPressed;
        public bool AttackReady => Now() >= _attackCdUntil;
        long _attackCdUntil;
        bool _canceled; // Cancel 后抑制松手容错路径（系统打断后 Up 不触发攻击）

        // 触摸喂入：Down 命中按钮 → 按下态（视觉亮起）；Up 释放 → 触发（滑出仍触发，真机按钮语义）。
        // 与摇杆共存：MobileBootstrap 先喂摇杆（左侧）再喂 HUD（右侧），Down 由摇杆锁主导、按钮独立。
        public void OnTouch(int id, JoystickPhase phase, Vector2 pos)
        {
            if (phase == JoystickPhase.Down)
            {
                _canceled = false;
                if (InAttack(pos)) _attackPressed = true;
                else if (InPotion(pos)) _potionPressed = true;
                else if (InMagic(pos)) _magicPressed = true;
            }
            // Ended 容错（模拟器低帧率 tap 帧合并，同 MobileBag）：Began 丢失时 Up 命中按钮仍触发。
            // Cancel 后抑制（系统打断）。条件全在外层（if-else-if 链只执行首个匹配分支：
            // Up 时未按下的按钮容错命中，否则落入对应 _xxxPressed 按下态分支处理）。
            else if (phase == JoystickPhase.Up && !_canceled
                && ((InAttack(pos) && !_attackPressed) || (InPotion(pos) && !_potionPressed) || (InMagic(pos) && !_magicPressed)))
            {
                if (InAttack(pos) && !_attackPressed) { if (AttackReady) TriggerAttack(); return; }
                if (InPotion(pos) && !_potionPressed) { TriggerPotion(); return; }
                if (InMagic(pos) && !_magicPressed) { TriggerMagic(); return; }
            }
            else if (_attackPressed)
            {
                if (phase == JoystickPhase.Up)
                {
                    _attackPressed = false;
                    if (AttackReady) TriggerAttack();
                }
                else if (phase == JoystickPhase.Cancel)
                {
                    _attackPressed = false; // 系统打断不触发
                    _canceled = true;
                }
            }
            else if (_potionPressed)
            {
                if (phase == JoystickPhase.Up)
                {
                    _potionPressed = false;
                    TriggerPotion();
                }
                else if (phase == JoystickPhase.Cancel)
                {
                    _potionPressed = false;
                    _canceled = true;
                }
            }
            else if (_magicPressed)
            {
                if (phase == JoystickPhase.Up)
                {
                    _magicPressed = false;
                    TriggerMagic();
                }
                else if (phase == JoystickPhase.Cancel)
                {
                    _magicPressed = false;
                    _canceled = true;
                }
            }
        }

        bool InAttack(Vector2 pos) => (pos - _attackCenter).magnitude <= AttackRadius;
        bool InPotion(Vector2 pos) => (pos - _potionCenter).magnitude <= PotionRadius;
        bool InMagic(Vector2 pos) => (pos - _magicCenter).magnitude <= MagicRadius;

        // 公开命中谓词（ui 空间）：拾取等地图 tap 判定排除 HUD 按钮区（攻击/药品/技能按钮 tap 走 HUD 不触发拾取）。
        // 小地图区（右上角档位/大地图按钮）同属 HUD 交互：点小地图按钮走 MirButton.Click 链，不触发世界 tap。
        public bool Hit(Vector2 ui)
        {
            if (InAttack(ui) || InPotion(ui) || InMagic(ui)) return true;
            var mini = GameScene.Scene?.MiniMapDialog;
            if (mini == null) return false;
            var r = mini.DisplayRectangle;
            return ui.x >= r.X && ui.x <= r.Right && ui.y >= r.Y && ui.y <= r.Bottom;
        }

        // 外部打断（背包面板打开等）：丢弃按钮按下态（面板打开期间触摸不喂入，Up 永久丢失），
        // 并抑制后续松手容错（防面板关闭后残留 Up 误触发）。
        public void Cancel() { _attackPressed = false; _potionPressed = false; _magicPressed = false; _canceled = true; }

        void TriggerAttack()
        {
            _attackCdUntil = Now() + AttackCooldownMs;
            SendAttack(GetFacing());
        }

        void TriggerPotion() => UsePotion();

        void TriggerMagic()
        {
            var u = MapObject.User;
            if (u == null || u.Magics == null || u.Magics.Count == 0) return;
            CastMagic(u.Magics[0].Spell);
        }

        // 默认实现：背包第一个 Potion → C.UseItem（服务器端判定消耗）。
        static void UseFirstPotion()
        {
            var u = MapObject.User;
            if (u == null || u.Inventory == null) return;
            foreach (var item in u.Inventory)
                if (item != null && item.Info != null && item.Info.Type == ItemType.Potion)
                {
                    Network.Enqueue(new C.UseItem { UniqueID = item.UniqueID, Grid = MirGridType.Inventory });
                    return;
                }
            Debug.Log("[mobile-hud] no potion in inventory");
        }

        // 默认实现：Magics[0] 向当前方向施放（无目标锁定；目标选择属后续 UI）。
        static void CastFirstMagic(Spell spell)
        {
            var u = MapObject.User;
            if (u == null) return;
            Network.Enqueue(new C.Magic { Spell = spell, Direction = u.Direction });
        }

        // 血条填充比例 [0,1]（Max<=0 时按空条，避免除零）。
        public float HpRatio => MaxHp > 0 ? Mathf.Clamp01((float)Hp / MaxHp) : 0f;
        public float MpRatio => MaxMp > 0 ? Mathf.Clamp01((float)Mp / MaxMp) : 0f;

        // 渲染（CrystalSpriteBatch 批次内调用）：攻击按钮（圆盘 tint，按下亮/冷却灰）+ 血条（HP 红/MP 蓝，
        // 按比例 src 裁剪）+ 药品按钮（绿）/技能按钮（紫）。纹理由调用方生成一次（attackTex 圆盘直径=2*Radius，
        // hpTex/mpTex=满条纯色；药品/技能按钮复用 attackTex 圆盘，不同 tint）。
        public void Render(Texture2D attackTex, Texture2D hpTex, Texture2D mpTex)
        {
            Color atk = AttackPressed
                ? new Color(1f, 0.9f, 0.5f, 0.9f)
                : AttackReady
                    ? new Color(0.95f, 0.45f, 0.25f, 0.9f)
                    : new Color(0.35f, 0.35f, 0.35f, 0.9f);
            CrystalSpriteBatch.Draw(attackTex, Full(attackTex), new Vector3(_attackCenter.x - AttackRadius, _attackCenter.y - AttackRadius, 0f), atk);
            // 药品按钮（绿，按下亮）
            Color pot = PotionPressed ? new Color(0.6f, 1f, 0.5f, 0.95f) : new Color(0.15f, 0.6f, 0.2f, 0.9f);
            CrystalSpriteBatch.Draw(attackTex, Full(attackTex), new Vector3(_potionCenter.x - PotionRadius, _potionCenter.y - PotionRadius, 0f), pot);
            // 技能按钮（紫，按下亮）
            Color mag = MagicPressed ? new Color(0.8f, 0.6f, 1f, 0.95f) : new Color(0.45f, 0.2f, 0.7f, 0.9f);
            CrystalSpriteBatch.Draw(attackTex, Full(attackTex), new Vector3(_magicCenter.x - MagicRadius, _magicCenter.y - MagicRadius, 0f), mag);

            var hpr = new Rect(0f, 0f, hpTex.width * HpRatio, hpTex.height);
            CrystalSpriteBatch.Draw(hpTex, hpr, new Vector3(_hpPos.x, _hpPos.y, 0f), new Color(0.9f, 0.15f, 0.15f, 0.9f));
            var mpr = new Rect(0f, 0f, mpTex.width * MpRatio, mpTex.height);
            CrystalSpriteBatch.Draw(mpTex, mpr, new Vector3(_hpPos.x, _hpPos.y + HpBarSize.y + MpBarGap, 0f), new Color(0.15f, 0.35f, 0.9f, 0.9f));
        }

        static Rect Full(Texture2D t) => new Rect(0f, 0f, t.width, t.height);
    }
}
