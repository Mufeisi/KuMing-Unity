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

        public const int AttackCooldownMs = 800; // 攻击冷却（对齐 MobileCombat.AttackCooldownMs，UX 层）
        public const float AttackRadius = 60f;   // 攻击按钮半径（px）

        // 血条布局（左上角，px）
        public static readonly Vector2 HpBarPos = new Vector2(20f, 20f);
        public static readonly Vector2 HpBarSize = new Vector2(180f, 14f);
        public static readonly float MpBarGap = 6f;

        public int ScreenW, ScreenH;
        // 玩家血条数据（外部同步：GameSession HP/MP → Hp/Mp，S.UserInformation MaxHP/MaxMP → MaxHp/MaxMp）。
        public int Hp, Mp, MaxHp, MaxMp;

        Vector2 _attackCenter;
        bool _attackPressed;

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

        // 攻击按钮圆心：右下角（右缘 90px、底缘 160px 锚定，拇指可达区）。
        void RecomputeLayout() => _attackCenter = new Vector2(ScreenW - 90f, ScreenH - 160f);

        public Vector2 AttackCenter => _attackCenter;
        public bool AttackPressed => _attackPressed;
        public bool AttackReady => Now() >= _attackCdUntil;
        long _attackCdUntil;
        bool _canceled; // Cancel 后抑制松手容错路径（系统打断后 Up 不触发攻击）

        // 触摸喂入：Down 命中按钮 → 按下态（视觉亮起）；Up 释放 → 触发攻击（滑出仍触发，真机按钮语义）。
        // 与摇杆共存：MobileBootstrap 先喂摇杆（左侧）再喂 HUD（右侧），Down 由摇杆锁主导、按钮独立。
        public void OnTouch(int id, JoystickPhase phase, Vector2 pos)
        {
            if (phase == JoystickPhase.Down)
            {
                _canceled = false;
                if (InAttack(pos)) _attackPressed = true;
            }
            // Ended 容错（模拟器低帧率 tap 帧合并，同 MobileBag）：Began 丢失时 Up 命中按钮仍触发攻击。
            // Cancel 后抑制（系统打断）。
            else if (phase == JoystickPhase.Up && InAttack(pos) && !_attackPressed && !_canceled)
            {
                if (AttackReady) TriggerAttack();
                return;
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
        }

        bool InAttack(Vector2 pos) => (pos - _attackCenter).magnitude <= AttackRadius;

        // 公开命中谓词（ui 空间）：拾取等地图 tap 判定排除 HUD 按钮区（攻击按钮 tap 走 HUD 不触发拾取）。
        // 小地图区（右上角档位/大地图按钮）同属 HUD 交互：点小地图按钮走 MirButton.Click 链，不触发世界 tap。
        public bool Hit(Vector2 ui)
        {
            if (InAttack(ui)) return true;
            var mini = GameScene.Scene?.MiniMapDialog;
            if (mini == null) return false;
            var r = mini.DisplayRectangle;
            return ui.x >= r.X && ui.x <= r.Right && ui.y >= r.Y && ui.y <= r.Bottom;
        }

        // 外部打断（背包面板打开等）：丢弃攻击按钮按下态（面板打开期间触摸不喂入，Up 永久丢失），
        // 并抑制后续松手容错（防面板关闭后残留 Up 误触发攻击）。
        public void Cancel() { _attackPressed = false; _canceled = true; }

        void TriggerAttack()
        {
            _attackCdUntil = Now() + AttackCooldownMs;
            SendAttack(GetFacing());
        }

        // 血条填充比例 [0,1]（Max<=0 时按空条，避免除零）。
        public float HpRatio => MaxHp > 0 ? Mathf.Clamp01((float)Hp / MaxHp) : 0f;
        public float MpRatio => MaxMp > 0 ? Mathf.Clamp01((float)Mp / MaxMp) : 0f;

        // 渲染（CrystalSpriteBatch 批次内调用）：攻击按钮（圆盘 tint，按下亮/冷却灰）+ 血条（HP 红/MP 蓝，
        // 按比例 src 裁剪）。纹理由调用方生成一次（attackTex 圆盘直径=2*AttackRadius，hpTex/mpTex=满条纯色）。
        public void Render(Texture2D attackTex, Texture2D hpTex, Texture2D mpTex)
        {
            Color atk = AttackPressed
                ? new Color(1f, 0.9f, 0.5f, 0.9f)
                : AttackReady
                    ? new Color(0.95f, 0.45f, 0.25f, 0.9f)
                    : new Color(0.35f, 0.35f, 0.35f, 0.9f);
            CrystalSpriteBatch.Draw(attackTex, Full(attackTex), new Vector3(_attackCenter.x - AttackRadius, _attackCenter.y - AttackRadius, 0f), atk);

            var hpr = new Rect(0f, 0f, hpTex.width * HpRatio, hpTex.height);
            CrystalSpriteBatch.Draw(hpTex, hpr, new Vector3(HpBarPos.x, HpBarPos.y, 0f), new Color(0.9f, 0.15f, 0.15f, 0.9f));
            var mpr = new Rect(0f, 0f, mpTex.width * MpRatio, mpTex.height);
            CrystalSpriteBatch.Draw(mpTex, mpr, new Vector3(HpBarPos.x, HpBarPos.y + HpBarSize.y + MpBarGap, 0f), new Color(0.15f, 0.35f, 0.9f, 0.9f));
        }

        static Rect Full(Texture2D t) => new Rect(0f, 0f, t.width, t.height);
    }
}
