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

namespace Crystal.Rendering.Editor
{
    // 阶段8 第7项 行会流程触控纯逻辑验证（无服务器）：
    // GuildDialog 常驻（挂 scene 默认隐藏）+ 行会按钮 wire 静态数据；Show 发 C.RequestGuildInfo{Type=0}
    // （NoticeChanged 标记 + 5s 节流）；未加入行会 Show → MirMessageBox 提示；S.GuildStatus 回声同步
    // User.GuildName/GuildRankName + 填 Level/Experience/MemberCount/MaxMembers；S.GuildNoticeChange 整表
    // 填公告全文（25 行标签窗口）；Up/Down 按钮滚动 + 滚动条位置；S.GuildInvite 弹 MirMessageBox YesNo →
    // C.GuildInvite{AcceptInvite}；S.GuildMemberChange 行会频道提示 + 加入计数；S.GuildExpGain 经验累加；
    // RouteTouch 集成（行会按钮被 UiConsumer 消费）。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.GuildVerify.Run -quit
    // 断言：全过输出 [guildverify] PASS exit 0。
    public static class GuildVerify
    {
        static int _fail;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[guildverify] FAIL {what}"); }
        }

        // 探针夹具：清空全局 seam（Scene/User/Objects）+ 建空场景 + MainDialog（ChatDialog ctor 读其 Location）
        // + ChatDialog（行会频道消息）+ GuildDialog 常驻（隐藏）+ 静态行会状态复位（NoticeChanged/
        // LastNoticeRequest/MembersChanged，防跨用例污染）。
        static GameScene NewScene()
        {
            GameScene.Scene = null;
            GameScene.User = null;
            GameScene.NPCID = 0;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            MobileUiAdapter.DialogRoot = () => GameScene.Scene;
            GuildDialog.NoticeChanged = true;
            GuildDialog.MembersChanged = true;
            GuildDialog.LastNoticeRequest = 0;

            var user = new UserObject(1) { Name = "probe", Level = 30 };
            GameScene.User = user;
            MapObject.User = user;
            MapControl.User = user;

            var scene = new GameScene();
            GameScene.Scene = scene;

            var main = new MainDialog { Parent = scene };
            scene.MainDialog = main;

            var chat = new ChatDialog { Parent = scene };
            scene.ChatDialog = chat;

            var guild = new GuildDialog { Parent = scene, Visible = false };
            scene.GuildDialog = guild;
            return scene;
        }

        static void DrainPackets()
        {
            while (Network.SentPackets.TryDequeue(out _)) { }
        }

        static T Last<T>(Func<Packet, T> cast) where T : class
        {
            T result = null;
            foreach (var p in Network.SentPackets.ToArray())
                if (cast(p) != null) result = cast(p);
            return result;
        }

        static C.RequestGuildInfo LastGuildInfo() => Last(p => p as C.RequestGuildInfo);
        static C.GuildInvite LastGuildInvite() => Last(p => p as C.GuildInvite);

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

        static void FeedGuildStatus(string name, string rank, byte level, int members, int max)
        {
            var p = new S.GuildStatus
            {
                GuildName = name,
                GuildRankName = rank,
                Level = level,
                MemberCount = members,
                MaxMembers = max,
            };
            GameSession.GuildStatus(p);
        }

        static void FeedNotice(params string[] lines)
        {
            var p = new S.GuildNoticeChange();
            foreach (var l in lines) p.notice.Add(l);
            GameSession.GuildNoticeChange(p);
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;
            CMain.Time = 10000;

            // ===== case1 常驻创建：GuildDialog 挂 scene 默认隐藏 =====
            {
                var scene = NewScene();
                Check(scene.GuildDialog != null, "case1 dialog attached");
                Check(!scene.GuildDialog.Visible, "case1 hidden by default");
            }

            // ===== case2 Show 未加入行会 → MirMessageBox 提示 + 不 Visible =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                DrainPackets();
                g.Show();
                Check(!g.Visible, "case2 not shown without guild");
                var box = FindModal() as MirMessageBox;
                Check(box != null && box.Modal, "case2 not-in-guild prompt shown");
                if (box == null) { /* fail 已记 */ }
                else
                {
                    box.OKButton.InvokeMouseClick(EventArgs.Empty);
                    Check(FindModal() == null, "case2 prompt dismissed");
                }
            }

            // ===== case3 Show 已加入 → Visible + 发 C.RequestGuildInfo{Type=0}（节流首发）=====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                MapObject.User.GuildName = "ProbeGuild";
                DrainPackets();
                g.Show();
                Check(g.Visible, "case3 shown");
                var req = LastGuildInfo();
                Check(req != null && req.Type == 0, "case3 request sent");
            }

            // ===== case4 节流：Hide 再 Show（节流窗内）不发第二次 =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                MapObject.User.GuildName = "ProbeGuild";
                g.Show(); // LastNoticeRequest = Time+5000，NoticeChanged=false
                g.Hide();
                DrainPackets();
                g.Show();
                Check(LastGuildInfo() == null, "case4 throttle blocks resend");
            }

            // ===== case5 S.GuildStatus 回声：同步 User + 填状态页 =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                FeedGuildStatus("ProbeGuild", "Leader", 5, 12, 50);
                Check(MapObject.User.GuildName == "ProbeGuild" && MapObject.User.GuildRankName == "Leader", "case5 user guild synced");
                Check(g.Level == 5 && g.MemberCount == 12 && g.MaxMembers == 50, "case5 status filled");
            }

            // ===== case6 首次入会（User 空→有）→ Notice/Members 变更标记 =====
            {
                var scene = NewScene();
                FeedGuildStatus("FreshGuild", "Member", 1, 3, 50);
                Check(GuildDialog.NoticeChanged && GuildDialog.MembersChanged, "case6 first-join marks changed");
            }

            // ===== case7 S.GuildStatus 空行会名 → Hide =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                MapObject.User.GuildName = "ProbeGuild";
                g.Show();
                FeedGuildStatus("", "", 0, 0, 0);
                Check(!g.Visible, "case7 empty status hides");
            }

            // ===== case8 S.GuildNoticeChange 整表：全文 + 归零 + 首屏窗口 =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                var lines = new string[30];
                for (int i = 0; i < 30; i++) lines[i] = "Line" + (i + 1);
                FeedNotice(lines);
                Check(g.NoticeLines.Length == 30, "case8 full notice stored");
                Check(g.NoticeScrollIndex == 0, "case8 scroll reset");
                Check(g.NoticeLineLabels[0].Text == "Line1", "case8 window head");
                Check(g.NoticeLineLabels[24].Text == "Line25", "case8 window tail");
                Check(g.NoticeLineLabels[0].Text.Length > 0 && g.NoticeLineLabels[24].Text.Length > 0, "case8 window filled");
            }

            // ===== case9 公告滚动：Down/Up 窗口平移 + 末端守卫 + 滚动条位置 =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                var lines = new string[30];
                for (int i = 0; i < 30; i++) lines[i] = "Line" + (i + 1);
                FeedNotice(lines);
                g.NoticeDownButton.InvokeMouseClick(EventArgs.Empty);
                Check(g.NoticeScrollIndex == 1 && g.NoticeLineLabels[0].Text == "Line2", "case9 down scrolls");
                Check(g.NoticePositionBar.Location.Y > 16, "case9 scrollbar moved");
                g.NoticeUpButton.InvokeMouseClick(EventArgs.Empty);
                Check(g.NoticeScrollIndex == 0 && g.NoticeLineLabels[0].Text == "Line1", "case9 up scrolls back");
                for (int i = 0; i < 10; i++) g.NoticeDownButton.InvokeMouseClick(EventArgs.Empty);
                Check(g.NoticeScrollIndex == 5, "case9 clamp at end");
                Check(g.NoticeLineLabels[24].Text == "Line30", "case9 last window tail");
                g.NoticeDownButton.InvokeMouseClick(EventArgs.Empty);
                Check(g.NoticeScrollIndex == 5, "case9 down guard at end");
                g.NoticeUpButton.InvokeMouseClick(EventArgs.Empty);
                Check(g.NoticeScrollIndex == 4, "case9 up from end");
                g.NoticePositionBar.Location = new Crystal.Client.Core.MirMath.Point(337, 16);
                Check(g.NoticeScrollIndex == 4, "case9 positionbar untouched by direct set");
            }

            // ===== case10 S.GuildNoticeChange{update=-1} → NoticeChanged=true；Show 拉整表 =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                MapObject.User.GuildName = "ProbeGuild";
                FeedNotice("old");
                g.Show(); // NoticeChanged=false + 首发
                DrainPackets();
                FeedNotice(); // 空整表（或 update>=0）
                var mark = new S.GuildNoticeChange { update = -1 };
                GameSession.GuildNoticeChange(mark);
                Check(GuildDialog.NoticeChanged, "case10 notice changed marked");
                g.Hide();
                CMain.Time = 20000; // 越过 5s 节流窗（节流本身 case4 已测），验证 NoticeChanged→Show 重拉
                DrainPackets();
                g.Show();
                Check(LastGuildInfo() != null, "case10 show refetches after mark");
            }

            // ===== case11 S.GuildInvite → MirMessageBox YesNo → Yes → C.GuildInvite{true} =====
            {
                var scene = NewScene();
                GameSession.GuildInvite(new S.GuildInvite { Name = "WarGuild" });
                var box = FindModal() as MirMessageBox;
                Check(box != null && box.Modal && box.YesButton != null, "case11 invite prompt shown");
                if (box == null) { /* fail 已记 */ }
                else
                {
                    box.YesButton.InvokeMouseClick(EventArgs.Empty);
                    var inv = LastGuildInvite();
                    Check(inv != null && inv.AcceptInvite, "case11 accept sent");
                    Check(FindModal() == null, "case11 prompt dismissed");
                }
            }

            // ===== case12 GuildInvite No → C.GuildInvite{false} =====
            {
                var scene = NewScene();
                GameSession.GuildInvite(new S.GuildInvite { Name = "WarGuild" });
                var box = FindModal() as MirMessageBox;
                Check(box != null && box.NoButton != null, "case12 invite prompt shown");
                if (box == null) { /* fail 已记 */ }
                else
                {
                    box.NoButton.InvokeMouseClick(EventArgs.Empty);
                    var inv = LastGuildInvite();
                    Check(inv != null && !inv.AcceptInvite, "case12 reject sent");
                }
            }

            // ===== case13 S.GuildMemberChange{status=2} → 行会频道提示 + MemberCount++ =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                FeedGuildStatus("ProbeGuild", "Leader", 5, 10, 50);
                GameSession.GuildMemberChange(new S.GuildMemberChange { Name = "Newbie", Status = 2 });
                Check(g.MemberCount == 11, "case13 join increments count");
                var chat = scene.ChatDialog;
                Check(chat != null && chat.ChatLines.Count > 0 && chat.ChatLines[chat.ChatLines.Count - 1].Text.Contains("Newbie"), "case13 guild chat msg");
            }

            // ===== case14 S.GuildMemberChange{status=4} → 离开提示 + 计数不变 =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                FeedGuildStatus("ProbeGuild", "Leader", 5, 10, 50);
                GameSession.GuildMemberChange(new S.GuildMemberChange { Name = "GoneOne", Status = 4 });
                Check(g.MemberCount == 10, "case14 leave no count change");
                var chat = scene.ChatDialog;
                Check(chat != null && chat.ChatLines.Count > 0 && chat.ChatLines[chat.ChatLines.Count - 1].Text.Contains("GoneOne"), "case14 leave chat msg");
            }

            // ===== case15 S.GuildExpGain → Experience 累加 =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                g.Experience = 100;
                GameSession.GuildExpGain(new S.GuildExpGain { Amount = 50 });
                Check(g.Experience == 150, "case15 exp accumulates");
            }

            // ===== case16 CloseButton → Hide =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                MapObject.User.GuildName = "ProbeGuild";
                g.Show();
                g.CloseButton.InvokeMouseClick(EventArgs.Empty);
                Check(!g.Visible, "case16 close hides");
            }

            // ===== case17 RouteTouch 集成：行会按钮被 UiConsumer 消费 → 开/关面板 + Show 发拉取 =====
            {
                var scene = NewScene();
                var g = scene.GuildDialog;
                MapObject.User.GuildName = "ProbeGuild";
                var guildBtn = new MobileBag(1280, 720);
                guildBtn.SetMargin(new UnityEngine.Vector2(MobileBag.ButtonMargin.x, MobileBag.ButtonMargin.y + (MobileBag.ButtonH + 8f) * 6));
                guildBtn.OnToggle = open => { if (open) g.Show(); else g.Hide(); };
                bool joystickFired = false;
                var route = new MobileUiAdapter.TouchRoute
                {
                    UiConsumer = (id, ph, ui) => guildBtn.OnTouch(id, ph, ui),
                    PanelOpen = false,
                    DialogHit = p => MobileUiAdapter.UiHitTest(p),
                    Joystick = (id, ph, pos) => joystickFired = true,
                    Hud = (id, ph, ui) => { },
                };
                var r = guildBtn.ButtonRect;
                var ui = new UnityEngine.Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
                var raw = new UnityEngine.Vector2(ui.x, 720f - ui.y);
                DrainPackets();
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(g.Visible, "case17 route opens guild panel");
                Check(!joystickFired, "case17 guild tap consumes joystick");
                Check(LastGuildInfo() != null, "case17 request sent on open");
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Down, raw);
                MobileUiAdapter.RouteTouch(route, 0, JoystickPhase.Up, raw);
                Check(!g.Visible, "case17 route closes guild panel");
            }

            // 还原全局 seam（防污染后续探针）。
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.User = null;
            MirControl.ActiveControl = null;
            MirControl.MouseControl = null;
            MobileUiAdapter.DialogRoot = null;
            DrainPackets();

            if (_fail == 0)
            {
                Console.WriteLine("[guildverify] PASS cases=17");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[guildverify] FAIL cases=17 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
