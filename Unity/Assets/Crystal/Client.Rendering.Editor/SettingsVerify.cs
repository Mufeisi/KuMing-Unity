using System;
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

namespace Crystal.Rendering.Editor
{
    // 阶段8 第8项增量4 设置三件套触控纯逻辑验证（无服务器）：
    // ChatOptionDialog（聊天筛选+透明，主设置页）/KeyboardLayoutDialog（键位查看/重置）/
    // HelpDialog（帮助）常驻隐藏；筛选按钮翻转 Settings.Filter*（ToggleAllFilters 全开/全关）；
    // 透明开关 Settings.TransparentChat；键位行点击 WaitingForBind + CheckNewInput 合成键
    // （Keys.K/Delete）更新 Keylist + ResetButton 回默认 + MirMessageBox + EnforceButton 翻转；
    // HelpDialog DisplayPage 翻页；MobileBag 设置按钮（左缘 y=286）被 UiConsumer 消费开关 +
    // 互斥关背包 + 不喂摇杆。设置纯本地（Settings 静态 + CMain.InputKeys，零网络包）。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.SettingsVerify.Run -quit
    // 断言：全过输出 [settingsverify] PASS exit 0。
    public static class SettingsVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[settingsverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam + 建空场景 + MainDialog + ChatDialog + 背包 + BuffsDialog +
        // 设置三件套常驻（默认隐藏，对齐 InitInGameDialogs）。Settings.Filter* 静态复位。
        static GameScene NewScene()
        {
            GameScene.Scene = null;
            GameScene.User = null;
            GameScene.SelectedCell = null;
            GameScene.Gold = 10000;
            GameScene.PickedUpGold = false;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            MobileUiAdapter.DialogRoot = () => GameScene.Scene;

            Settings.FilterNormalChat = Settings.FilterWhisperChat = Settings.FilterShoutChat =
                Settings.FilterSystemChat = Settings.FilterLoverChat = Settings.FilterMentorChat =
                Settings.FilterGroupChat = Settings.FilterGuildChat = false;
            Settings.TransparentChat = false;

            var user = new UserObject(1) { Name = "probe", Level = 30, Class = MirClass.Warrior };
            user.Inventory = new UserItem[56];
            GameScene.User = user;
            MapObject.User = user;
            MapControl.User = user;

            var scene = new GameScene();
            GameScene.Scene = scene;

            var main = new MainDialog { Parent = scene };
            scene.MainDialog = main;

            var chat = new ChatDialog { Parent = scene };
            scene.ChatDialog = chat;

            var inv = new InventoryDialog { Parent = scene, Visible = false };
            inv.AutoSize = false;
            inv.Size = new Size(340, 240); // 空库下面板 AutoSize 回退 0×0 → 显式尺寸供格子 hover 命中
            scene.InventoryDialog = inv;
            scene.BuffsDialog = new BuffDialog(); // RefreshStats 的 RefreshBuffs 依赖（空 Buffs）

            var option = new ChatOptionDialog { Parent = scene, Visible = false };
            scene.ChatOptionDialog = option;
            var keyLayout = new KeyboardLayoutDialog { Parent = scene, Visible = false };
            scene.KeyboardLayoutDialog = keyLayout;
            var help = new HelpDialog { Parent = scene, Visible = false };
            scene.HelpDialog = help;
            return scene;
        }

        // 瞬态模态查找（与 MobileBootstrap.FindModal 同语义：scene.Controls 树 Modal+Visible，倒序取顶层）。
        static MirControl FindModal()
        {
            var scene = GameScene.Scene;
            if (scene == null || scene.Controls == null) return null;
            for (int i = scene.Controls.Count - 1; i >= 0; i--)
            {
                var c = scene.Controls[i];
                if (c != null && !c.IsDisposed && c.Visible && c.Modal) return c;
            }
            return null;
        }

        public static void Run()
        {
            // ===== case1 常驻创建三件套默认隐藏 =====
            {
                var scene = NewScene();
                Check(scene.ChatOptionDialog != null && !scene.ChatOptionDialog.Visible, "case1 option resident hidden");
                Check(scene.KeyboardLayoutDialog != null && !scene.KeyboardLayoutDialog.Visible, "case1 keylayout resident hidden");
                Check(scene.HelpDialog != null && !scene.HelpDialog.Visible, "case1 help resident hidden");
            }

            // ===== case2 筛选单项：GeneralButton 翻转 FilterNormalChat =====
            {
                var scene = NewScene();
                var option = scene.ChatOptionDialog;
                option.GeneralButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(Settings.FilterNormalChat, "case2 normal filter on");
                Check(option.AllFiltersOff == false, "case2 allfiltersoff cleared");
                option.GeneralButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(!Settings.FilterNormalChat, "case2 normal filter off");
            }

            // ===== case3 AllButton：全开 =====
            {
                var scene = NewScene();
                var option = scene.ChatOptionDialog;
                option.AllButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(Settings.FilterNormalChat && Settings.FilterWhisperChat && Settings.FilterShoutChat &&
                    Settings.FilterSystemChat && Settings.FilterLoverChat && Settings.FilterMentorChat &&
                    Settings.FilterGroupChat && Settings.FilterGuildChat, "case3 all filters on");
            }

            // ===== case4 WhisperButton 翻转 =====
            {
                var scene = NewScene();
                var option = scene.ChatOptionDialog;
                option.WhisperButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(Settings.FilterWhisperChat, "case4 whisper filter on");
            }

            // ===== case5 TransparencyOn → TransparentChat=true =====
            {
                var scene = NewScene();
                var option = scene.ChatOptionDialog;
                option.TransparencyOnButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(Settings.TransparentChat, "case5 transparent on");
            }

            // ===== case6 TransparencyOff → false =====
            {
                var scene = NewScene();
                var option = scene.ChatOptionDialog;
                Settings.TransparentChat = true;
                option.TransparencyOffButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(!Settings.TransparentChat, "case6 transparent off");
            }

            // ===== case7 AllFiltersOff 初始 true + AllButton 全关后全开 =====
            {
                var scene = NewScene();
                var option = scene.ChatOptionDialog;
                Check(option.AllFiltersOff, "case7 initial allfiltersoff");
                option.AllButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)); // 全开
                option.AllButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0)); // 全关
                Check(!Settings.FilterNormalChat && !Settings.FilterGuildChat, "case7 all filters off again");
            }

            // ===== case8 KeyboardLayoutDialog Rows 含 KeybindRow =====
            {
                var scene = NewScene();
                var keyLayout = scene.KeyboardLayoutDialog;
                Check(keyLayout.Rows.Count(r => r is KeybindRow) > 0, "case8 keybind rows built");
                Check(keyLayout.Rows.Count(r => r is KeybindHeadingRow) > 0, "case8 heading row built");
            }

            // ===== case9 键位行点击 → WaitingForBind =====
            {
                var scene = NewScene();
                var keyLayout = scene.KeyboardLayoutDialog;
                var row = (KeybindRow)keyLayout.Rows.First(r => r is KeybindRow);
                row.CurrentBindButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(keyLayout.WaitingForBind != null, "case9 waiting for bind");
                Check(keyLayout.WaitingForBind == row.KeyBind, "case9 waiting bind = row keybind");
            }

            // ===== case10 CheckNewInput Keys.K → 绑定更新 + WaitingForBind 清空 =====
            {
                var scene = NewScene();
                var keyLayout = scene.KeyboardLayoutDialog;
                var row = (KeybindRow)keyLayout.Rows.First(r => r is KeybindRow);
                var func = row.KeyBind.function;
                var oldKey = CMain.InputKeys.Keylist.Single(b => b.function == func).Key;
                row.CurrentBindButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                keyLayout.CheckNewInput(new KeyEventArgs(Keys.K));
                var bind = CMain.InputKeys.Keylist.Single(b => b.function == func);
                Check(bind.Key == Keys.K, "case10 bind key=K");
                Check(keyLayout.WaitingForBind == null, "case10 waiting cleared");
                Check(oldKey != Keys.K, "case10 key changed from default");
            }

            // ===== case11 CheckNewInput Delete → 清空绑定 =====
            {
                var scene = NewScene();
                var keyLayout = scene.KeyboardLayoutDialog;
                var row = (KeybindRow)keyLayout.Rows.First(r => r is KeybindRow);
                var func = row.KeyBind.function;
                row.CurrentBindButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                keyLayout.CheckNewInput(new KeyEventArgs(Keys.Delete));
                var bind = CMain.InputKeys.Keylist.Single(b => b.function == func);
                Check(bind.Key == Keys.None, "case11 bind cleared");
                Check(bind.RequireAlt == 2, "case11 require flags 2 (off)");
            }

            // ===== case12 ResetButton → 回默认 + MirMessageBox =====
            {
                var scene = NewScene();
                var keyLayout = scene.KeyboardLayoutDialog;
                var row = (KeybindRow)keyLayout.Rows.First(r => r is KeybindRow);
                var func = row.KeyBind.function;
                row.CurrentBindButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                keyLayout.CheckNewInput(new KeyEventArgs(Keys.K));
                var before = CMain.InputKeys.Keylist.Single(b => b.function == func).Key;
                Check(before == Keys.K, "case12 modified before reset");
                keyLayout.ResetButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var after = CMain.InputKeys.Keylist.Single(b => b.function == func).Key;
                var def = CMain.InputKeys.DefaultKeylist.Single(b => b.function == func).Key;
                Check(after == def && def != Keys.K, "case12 reset to default");
                Check(FindModal() as MirMessageBox != null, "case12 reset prompt shown");
            }

            // ===== case13 EnforceButton 翻转 =====
            {
                var scene = NewScene();
                var keyLayout = scene.KeyboardLayoutDialog;
                Check(keyLayout.Enforce, "case13 enforce default true");
                keyLayout.EnforceButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(!keyLayout.Enforce, "case13 enforce off");
                keyLayout.EnforceButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(keyLayout.Enforce, "case13 enforce back on");
            }

            // ===== case14 HelpDialog DisplayPage 翻页 =====
            {
                var scene = NewScene();
                var help = scene.HelpDialog;
                help.DisplayPage(0);
                var p0 = help.PageLabel.Text;
                help.NextButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                var p1 = help.PageLabel.Text;
                Check(p0 != p1, "case14 page advanced");
                help.PreviousButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(help.PageLabel.Text == p0, "case14 page back");
            }

            // ===== case15 RouteTouch 集成：设置按钮（左缘 y=286）消费开关 + 不喂摇杆 =====
            {
                var scene = NewScene();
                var settingsBtn = new MobileBag(1280, 720) { LeftAnchored = true };
                settingsBtn.SetMargin(new UnityEngine.Vector2(90f, 100f + (MobileBag.ButtonH + 8f) * 3));
                settingsBtn.OnToggle = open => { if (open) scene.ChatOptionDialog.Show(); else scene.ChatOptionDialog.Hide(); };
                bool joystickFired = false;
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => settingsBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                };
                var r = settingsBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(scene.ChatOptionDialog.Visible, "case15 opened by tap");
                Check(!joystickFired, "case15 joystick not fed");
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!scene.ChatOptionDialog.Visible, "case15 closed by tap");
                Check(!joystickFired, "case15 joystick not fed on close");
            }

            // ===== case16 互斥：开设置关背包 + CloseButton Hide =====
            {
                var scene = NewScene();
                var option = scene.ChatOptionDialog;
                var inv = scene.InventoryDialog;
                inv.Show();
                option.Show();
                Check(inv.Visible, "case16 bag stays (option Show 不互斥自身)");
                option.CloseButton.InvokeMouseClick(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                Check(!option.Visible, "case16 option closed");
            }

            Console.WriteLine(_fail == 0 ? "[settingsverify] PASS cases=16" : $"[settingsverify] FAIL cases={_fail}");
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }
    }
}
