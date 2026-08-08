using System;
using UnityEngine;

namespace Crystal.Client.Rendering
{
    // 阶段8 第2项（背包/装备/药品/拾取 移动端）增量1：背包开/关按钮（纯逻辑层）。
    // 右上角程序化按钮：Down 命中→按下态，Up 释放→toggle 背包面板；Cancel 系统打断不 toggle。
    // 与 Unity Input 解耦（OnTouch 喂入 JoystickPhase，MobileBootstrap 从 Input.touches 映射），
    // 可确定性单测（MobileHud/TouchJoystick 同款模式）。纹理由调用方生成一次（白色方块，
    // Render tint 上色：开=亮黄，关=橙黄），E2E 截图按颜色扫描定位按钮坐标。
    public sealed class MobileBag
    {
        public const float ButtonW = 72f;
        public const float ButtonH = 54f;
        // 右上角锚定边距（px，ui 空间：左上原点，与 MobileHud 同坐标系，MobileUiAdapter.ToUi 翻转后喂入）。
        // y=140 逻辑（物理约 210）：避开 Android 状态栏（模拟器实证顶部约 126px 触摸被系统消费
        // 不进 Unity——tap 无响应根因候选 A），同时给真机刘海/安全区留余量（backlog 安全区适配）。
        public static readonly Vector2 ButtonMargin = new Vector2(90f, 140f);

        public int ScreenW, ScreenH;
        public bool Open;
        // 开关动作注入（调用方接 GameScene.Scene.InventoryDialog 的 Show/Hide + RefreshInventory）。
        public Action<bool> OnToggle;
        // 按钮锚点边距（实例可覆写：装备窗口按钮复用本控件时下移换色区分，E2E 按颜色定位）。
        public Vector2 Margin = ButtonMargin;
        // 开/关 tint（实例可覆写；背包=黄，装备=绿，E2E 扫描区分两按钮）。
        public Color TintOpen = new Color(1f, 0.85f, 0.3f, 0.95f);
        public Color TintClosed = new Color(0.95f, 0.62f, 0.2f, 0.95f);
        // 左缘锚定（8-8-1 英雄按钮）：右侧按钮列已满（第 10 列 720 高即超屏），英雄入口放左缘
        // 顶部（x=Margin.x 距左缘，y=Margin.y 距顶）。默认 false=右上角锚定（现有语义不变）。
        public bool LeftAnchored;

        Rect _rect;
        bool _pressed;
        bool _canceled; // Cancel 后抑制松手容错路径（系统打断后 Up 不 toggle）

        public MobileBag(int screenW, int screenH)
        {
            ScreenW = screenW;
            ScreenH = screenH;
            Recompute();
        }

        public void SetScreen(int w, int h)
        {
            ScreenW = w;
            ScreenH = h;
            Recompute();
        }

        // 换锚点（Start 阶段在首帧 SetScreen 可能不触发时生效，避免脏 _rect 覆盖原按钮）。
        public void SetMargin(Vector2 margin)
        {
            Margin = margin;
            Recompute();
        }

        // 右上角锚定重算：右/顶安全区 inset 内缩（刘海顶→按钮下移、Home indicator/圆角右→内缩）。
        // 派生按钮（装备/任务/地图 SetMargin 下移）继承本方法 → 整列同步偏移。SafeArea 默认全屏 inset=0 → 布局不变。
        // 左缘锚定（LeftAnchored）：x=Margin.x 距左缘（左/顶安全区 inset 内缩），英雄按钮专用。
        void Recompute()
        {
            if (LeftAnchored) _rect = new Rect(Margin.x + SafeArea.Left, Margin.y + SafeArea.Top, ButtonW, ButtonH);
            else _rect = new Rect(ScreenW - ButtonW - Margin.x - SafeArea.Right, Margin.y + SafeArea.Top, ButtonW, ButtonH);
        }

        public Rect ButtonRect => _rect;
        public bool HitTest(Vector2 pos) => _rect.Contains(pos);

        // 触摸喂入：Down 命中→按下态（返回 true=本触摸已被背包按钮消费，调用方不再喂摇杆/HUD）；
        // 已按下期间 Up→toggle、Cancel→系统打断不 toggle、Move/Stationary→保持按下态并消费。
        public bool OnTouch(int id, JoystickPhase phase, Vector2 pos)
        {
            if (phase == JoystickPhase.Down)
            {
                _canceled = false;
                if (HitTest(pos)) { _pressed = true; return true; }
                return false;
            }
            // Ended 容错（模拟器低帧率 tap 帧合并）：Began 与 Ended 落在同一帧时 Down 相位丢失，
            // 若已按下（正常路径）走下方 Up toggle；若从未按下（Began 丢失）但松手位置命中按钮，
            // 仍 toggle——真机快速 tap 语义（与 MobileHud 攻击按钮同款容错）。Cancel 后抑制（系统打断）。
            if (phase == JoystickPhase.Up && HitTest(pos) && !_pressed && !_canceled)
            {
                Toggle();
                return true;
            }
            if (!_pressed) return false;
            if (phase == JoystickPhase.Up)
            {
                _pressed = false;
                Toggle();
            }
            else if (phase == JoystickPhase.Cancel)
            {
                _pressed = false; // 系统打断不触发
                _canceled = true;
            }
            return true; // 已按下期间的所有阶段均由背包按钮消费
        }

        void Toggle()
        {
            Open = !Open;
            if (OnToggle != null) OnToggle(Open);
        }

        // 渲染（CrystalSpriteBatch 批次内调用）：程序化方块纹理 tint 上色（开=亮黄，关=橙黄，
        // 与右下攻击按钮橙圆盘色系区分：背包按钮 G 通道高，E2E 扫描可用 R>200&&G>170&&B<140 定位）。
        public void Render(Texture2D tex)
        {
            var c = Open ? TintOpen : TintClosed;
            CrystalSpriteBatch.Draw(tex, new Rect(0, 0, tex.width, tex.height), new Vector3(_rect.x, _rect.y, 0f), c);
        }
    }
}
