using System;

namespace Client.MirControls
{
    // System.Windows.Forms 输入类型的纯 C# 等价物（控件事件模型的最小契约）。
    // 数值对齐 WinForms（Keys 键码 / MouseButtons 标志位），探针/渲染层可确定性驱动。
    // 真实输入接管（OS 事件 → 控件）落地时由渲染层把这些类型转为平台事件。

    public enum MouseButtons
    {
        None = 0x00000000,
        Left = 0x00100000,
        Right = 0x00200000,
        Middle = 0x00400000,
    }

    public class MouseEventArgs : EventArgs
    {
        public MouseButtons Button { get; }
        public int Clicks { get; }
        public int X { get; }
        public int Y { get; }
        public int Delta { get; }

        public MouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta)
        {
            Button = button;
            Clicks = clicks;
            X = x;
            Y = y;
            Delta = delta;
        }
    }

    // 键码对齐 System.Windows.Forms.Keys（仅覆盖控件模型用到的键）。
    public enum Keys
    {
        None = 0,
        Back = 0x08,
        Tab = 0x09,
        Enter = 0x0D,
        Space = 0x20,
        PageUp = 0x21,
        PageDown = 0x22,
        End = 0x23,
        Home = 0x24,
        Left = 0x25,
        Up = 0x26,
        Right = 0x27,
        Down = 0x28,
        PrintScreen = 0x2C,
        Insert = 0x2D,
        Delete = 0x2E,
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,
        Escape = 0x1B,
        // 修饰键（KeyboardLayoutDialog.CheckNewInput 排除检测，WinForms 键码）。
        ShiftKey = 0x10,
        ControlKey = 0x11,
        Menu = 0x12,
        // 反引号/波浪号（KeyboardLayoutDialog.CheckNewInput 排除检测，WinForms Oem8=0xDF）。
        Oem8 = 0xDF,
        // 字母区（KeyBindSettings 默认绑定，WinForms 键码 A=0x41 起）。
        A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45, F = 0x46, G = 0x47, H = 0x48,
        I = 0x49, J = 0x4A, K = 0x4B, L = 0x4C, M = 0x4D, N = 0x4E, O = 0x4F, P = 0x50,
        Q = 0x51, R = 0x52, S = 0x53, T = 0x54, U = 0x55, V = 0x56, W = 0x57, X = 0x58,
        Y = 0x59, Z = 0x5A,
        // 数字行（KeyBindSettings 腰带槽默认绑定，WinForms 键码 D1=0x31 起）。
        D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34, D5 = 0x35, D6 = 0x36, D7 = 0x37, D8 = 0x38,
        // 数字小键盘（KeyBindSettings 腰带槽 Alt 默认绑定，WinForms 键码 NumPad0=0x60 起）。
        NumPad1 = 0x61, NumPad2 = 0x62, NumPad3 = 0x63, NumPad4 = 0x64,
        NumPad5 = 0x65, NumPad6 = 0x66, NumPad7 = 0x67, NumPad8 = 0x68,
    }

    public class KeyEventArgs : EventArgs
    {
        public Keys KeyCode { get; }
        public bool Handled { get; set; }
        public bool Shift { get; }
        public bool Alt { get; }
        public bool Control { get; }

        public KeyEventArgs(Keys keyCode, bool shift = false, bool alt = false, bool control = false)
        {
            KeyCode = keyCode;
            Shift = shift;
            Alt = alt;
            Control = control;
        }
    }

    public class KeyPressEventArgs : EventArgs
    {
        public char KeyChar { get; }
        public bool Handled { get; set; }

        public KeyPressEventArgs(char keyChar)
        {
            KeyChar = keyChar;
        }
    }

    public delegate void MouseEventHandler(object sender, MouseEventArgs e);
    public delegate void KeyEventHandler(object sender, KeyEventArgs e);
    public delegate void KeyPressEventHandler(object sender, KeyPressEventArgs e);
}
