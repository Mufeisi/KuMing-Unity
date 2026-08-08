using System;
using Client.MirScenes.Dialogs;
using UnityEngine;

namespace Crystal.Client.Rendering
{
    // 阶段8 第5项（软键盘/聊天）增量2：聊天触控控制器（纯逻辑层）。
    // 两个程序化按钮（底部左缘）：聊天按钮 → 开聊天输入框 + 弹软键盘（8-5-1 桥）；频道按钮 →
    // 循环频道（0 附近/1 全员 !/2 行会 @，旧客户端频道=文本前缀由键盘 !/@ 触发，移动端补可视化按钮）。
    // 与 Unity Input 解耦（OnTouch 喂入 JoystickPhase，MobileBootstrap 从 Input.touches 映射），
    // 可确定性单测（ChatVerify）。发送走软键盘 Enter（SoftKeyboardBridge Submitted → ChatTextBox_KeyPress
    // → C.Chat，文本含前缀服务器分频道），不另设发送按钮（YAGNI）。
    public sealed class MobileChat
    {
        public static readonly string[] ChannelPrefix = { "", "!", "@" };
        public const float ButtonW = 72f;
        public const float ButtonH = 54f;
        // 底部左缘锚定（px，ui 空间）：聊天/频道按钮并排，与右下攻击按钮同高（拇指可达区），
        // 左缘 20px。频道按钮在聊天按钮右侧。
        public static readonly Vector2 ChatMargin = new Vector2(20f, 160f);

        public int ScreenW, ScreenH;
        public int Channel; // 0=附近 1=全员 2=行会
        // 动作注入（调用方接 ChatDialog + SoftKeyboardBridge）。
        public Action OnOpenInput;   // 开聊天输入（SetChatText + Focus 弹软键盘）
        public Action<int> OnChannel; // 频道切换（调用方设 ChatTextBox 前缀）

        Rect _chatRect, _chanRect;
        bool _pressed, _chanPressed, _canceled;

        public MobileChat(int screenW, int screenH)
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

        void Recompute()
        {
            float y = ScreenH - ChatMargin.y;
            _chatRect = new Rect(ChatMargin.x, y, ButtonW, ButtonH);
            _chanRect = new Rect(ChatMargin.x + ButtonW + 8f, y, ButtonW, ButtonH);
        }

        public Rect ChatRect => _chatRect;
        public Rect ChannelRect => _chanRect;

        // 触摸喂入：Down 命中记录按下态（消费）；Up 松开触发（聊天按钮→开输入、频道按钮→切换）。
        // Ended 容错（模拟器低帧率 tap 帧合并，同 MobileBag/MobileHud）：Began 丢失时 Up 命中仍触发。
        // Cancel 后抑制（系统打断）。
        public bool OnTouch(int id, JoystickPhase phase, Vector2 pos)
        {
            if (phase == JoystickPhase.Down)
            {
                _canceled = false;
                if (_chatRect.Contains(pos)) { _pressed = true; return true; }
                if (_chanRect.Contains(pos)) { _chanPressed = true; return true; }
                return false;
            }
            if (!_pressed && !_chanPressed)
            {
                if (phase == JoystickPhase.Up && !_canceled)
                {
                    if (_chatRect.Contains(pos)) { OnOpenInput?.Invoke(); return true; }
                    if (_chanRect.Contains(pos)) { CycleChannel(); return true; }
                }
                return false;
            }
            if (phase == JoystickPhase.Up)
            {
                if (_pressed) { _pressed = false; OnOpenInput?.Invoke(); }
                if (_chanPressed) { _chanPressed = false; CycleChannel(); }
                return true;
            }
            if (phase == JoystickPhase.Cancel)
            {
                _pressed = false;
                _chanPressed = false;
                _canceled = true;
                return true;
            }
            return true; // 已按下期间的所有阶段由聊天控制器消费
        }

        void CycleChannel()
        {
            Channel = (Channel + 1) % ChannelPrefix.Length;
            OnChannel?.Invoke(Channel);
        }

        // ---- 聊天对话框接线助手（静态：MobileBootstrap 与探针共用同一逻辑，DRY）----

        // 开聊天输入（OnOpenInput 接线）：首次开注入当前频道前缀（按钮色相已表达频道选择），
        // SetChatText 聚焦显示 + 弹软键盘。服务器按 !/@ 前缀分频道，故前缀写进输入文本。
        public static void OpenInput(ChatDialog dlg, int channel)
        {
            if (dlg == null) return;
            if (!dlg.ChatTextBox.Visible)
                dlg.ChatTextBox.Text = ChannelPrefix[channel]; // 首次开：注入当前频道前缀
            dlg.SetChatText(""); // SetFocus + Visible=true + 光标尾部（空追加 no-op）
            SoftKeyboardBridge.Focus(dlg.ChatTextBox);
        }

        // 频道切换（OnChannel 接线）：输入框开着则重写文本前缀（去旧前缀+补新前缀）并重开软键盘
        // 使初始文本生效（SoftKeyboardBridge.Poll SyncText 以键盘文本覆盖框文本，须重开同步）。
        public static void ApplyChannel(ChatDialog dlg, int channel)
        {
            if (dlg == null) return;
            var box = dlg.ChatTextBox;
            if (!box.Visible) return; // 输入框未开：仅按钮色相切换（MobileChat.Channel 已更新）
            string t = box.Text;
            if (t.StartsWith("!")) t = t.Substring(1);
            else if (t.StartsWith("@")) t = t.Substring(1);
            box.Text = ChannelPrefix[channel] + t;
            SoftKeyboardBridge.Focus(box);
        }

        // 关聊天输入（Back 处理接线）：对齐 PC Escape 语义（隐藏+清空+清链接），返回 true=输入框本开着。
        public static bool CloseInput(ChatDialog dlg)
        {
            if (dlg == null) return false;
            var box = dlg.ChatTextBox;
            if (!box.Visible) return false;
            box.Visible = false;
            box.Text = string.Empty;
            dlg.LinkedItems.Clear();
            SoftKeyboardBridge.Unfocus();
            return true;
        }

        // 渲染（CrystalSpriteBatch 批次内调用）：聊天按钮青色 tint、频道按钮按当前频道色
        // （0 附近=灰绿 / 1 全员=橙 / 2 行会=紫）。纹理由调用方生成一次（白色方块，同 MobileBag）。
        public void Render(Texture2D tex)
        {
            CrystalSpriteBatch.Draw(tex, new Rect(0, 0, tex.width, tex.height), new Vector3(_chatRect.x, _chatRect.y, 0f), new Color(0.2f, 0.7f, 0.85f, 0.95f));
            Color chan = Channel switch
            {
                1 => new Color(0.95f, 0.62f, 0.2f, 0.95f),  // 全员橙
                2 => new Color(0.6f, 0.4f, 0.85f, 0.95f),  // 行会紫
                _ => new Color(0.4f, 0.75f, 0.4f, 0.95f),  // 附近绿
            };
            CrystalSpriteBatch.Draw(tex, new Rect(0, 0, tex.width, tex.height), new Vector3(_chanRect.x, _chanRect.y, 0f), chan);
        }
    }
}
