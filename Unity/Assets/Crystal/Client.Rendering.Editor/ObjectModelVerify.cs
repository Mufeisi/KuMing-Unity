using System;
using Client;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirScenes;
using Crystal.Client.Core.MirMath;
using Crystal.Client.Rendering;
using UnityEditor;
using S = ServerPackets;
using SDPoint = System.Drawing.Point;
using SDColor = System.Drawing.Color;

namespace Crystal.Rendering.Editor
{
    // 阶段P0 sanduan 提取 A1/A2 对象模型（SpellObject + ItemObject）确定性探针（无服务器）：
    // SpellObject：Load 按 Spell 枚举选帧（库/帧/间隔/帧数/Repeat/Blend）+ 地图特效副作用 +
    // Process 帧推进（Repeat 回绕 Ended / 单次不绕）；ItemObject：ObjectItem 装载（名称/帧/真尺寸）
    // + ObjectGold 金币分级帧 112-116 + Process 居中换算/DisplayRectangle；
    // GameSession 派发：ObjectSpell 落对象、ObjectItem/ObjectGold 在无 FloorItems 图集时优雅跳过。
    // batchmode：Unity.exe -executeMethod Crystal.Rendering.Editor.ObjectModelVerify.Run -quit
    // 断言：全过输出 [objectmodelverify] PASS exit 0。
    public static class ObjectModelVerify
    {
        static int _fail;
        static UserObject _user;

        static void Check(bool cond, string what)
        {
            if (!cond) { _fail++; Console.WriteLine($"[objectmodelverify] FAIL {what}"); }
        }

        // 帧推进：把时钟拨到下一动帧再 Process（Process 内部再推进 NextMotion = Time + FrameInterval）。
        static void Advance(SpellObject sp)
        {
            CMain.Time = sp.NextMotion;
            sp.Process();
        }

        public static void Run()
        {
            Settings.ScreenWidth = 1280; Settings.ScreenHeight = 720;
            CMain.Time = 0;
            GameScene.Scene = new GameScene { MapControl = new MapControl() };
            _user = new UserObject(100) { Class = MirClass.Warrior, Gender = MirGender.Male, Level = 1 };
            GameScene.User = _user;
            MapObject.User = _user;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapControl.Effects.Clear();
            MapControl.OffSetX = 0;
            MapControl.OffSetY = 0;
            MapControl.MapLocation = new Point(0, 0);

            SpellCaseChecks();
            SpellProcessChecks();
            DispatchChecks();
            ItemChecks();

            // 还原静态（防污染后续探针）。
            GameScene.Scene = null;
            GameScene.User = null;
            MapObject.User = null;
            MapControl.Objects.Clear();
            MapControl.ObjectsList.Clear();
            MapControl.Effects.Clear();
            MapControl.OffSetX = 0;
            MapControl.OffSetY = 0;
            MapControl.MapLocation = new Point(0, 0);
            CMain.Time = 0;

            if (_fail == 0)
            {
                Console.WriteLine("[objectmodelverify] PASS cases=24");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[objectmodelverify] FAIL cases=24 fail={_fail}");
                EditorApplication.Exit(1);
            }
        }

        static SpellObject LoadSpell(uint id, Spell spell, bool param = false, MirDirection dir = 0)
        {
            var sp = new SpellObject(id);
            sp.Load(new S.ObjectSpell { ObjectID = id, Location = new SDPoint(5, 5), Spell = spell, Direction = dir, Param = param });
            return sp;
        }

        static void SpellCaseChecks()
        {
            var fire = LoadSpell(1, Spell.FireWall);
            Check(fire.BodyLibrary == Libraries.Magic, "firewall lib Magic");
            Check(fire.DrawFrame == 1630 && fire.FrameInterval == 120 && fire.FrameCount == 6, "firewall frame 1630/120/6");
            Check(fire.Repeat && fire.Blend, "firewall repeat+blend");
            Check(MapControl.Objects.ContainsKey(1), "firewall registered");

            var blz = LoadSpell(2, Spell.Blizzard);
            Check(!blz.Repeat && blz.FrameCount == 30, "blizzard non-repeat count 30");
            Check(blz.AnimationOffset == new Point(0, -20), "blizzard offset -20");

            int fxBefore = MapControl.Effects.Count;
            LoadSpell(3, Spell.MeteorStrike);
            Check(MapControl.Effects.Count == fxBefore + 1, "meteor adds map effect");

            var rb = LoadSpell(4, Spell.Rubble, dir: 0);
            Check(rb.BodyLibrary == null, "rubble dir0 null lib");

            var dz = LoadSpell(5, Spell.DigOutZombie);
            // batchmode Monsters 数组全 null → 三元落 Monsters[ushort]=null
            Check(dz.BodyLibrary == null, "digoutzombie null lib in batchmode");

            var etBoom = LoadSpell(6, Spell.ExplosiveTrap, param: true);
            Check(etBoom.DrawFrame == 1570 && etBoom.FrameCount == 9 && !etBoom.Repeat, "explosivetrap boom frame");
            var etArmed = LoadSpell(7, Spell.ExplosiveTrap, param: false);
            Check(etArmed.DrawFrame == 1560 && etArmed.FrameCount == 10 && etArmed.Repeat, "explosivetrap armed frame");

            var horn = LoadSpell(8, Spell.HornedCommanderRockSpike);
            Check(horn.BodyLibrary == null, "hornedspike null lib in batchmode");
        }

        static void SpellProcessChecks()
        {
            var fire = MapControl.Objects[1] as SpellObject;
            for (int i = 0; i < 6; i++) Advance(fire);
            Check(fire.FrameIndex == 0 && fire.Ended, "firewall wraps after 6 frames");

            var blz = MapControl.Objects[2] as SpellObject;
            for (int i = 0; i < 30; i++) Advance(blz);
            Check(blz.FrameIndex == 30 && !blz.Ended, "blizzard single-run to end no wrap");

            // DrawLocation 世界→屏幕换算：obj(5,5)，User.Movement=(0,0)，OffSet=0 → (5*48, 5*32)=(240,160)
            var horn = MapControl.Objects[8] as SpellObject;
            fire.Process();
            Check(fire.DrawLocation == new Point(240, 160), "spell drawlocation world->screen");
        }

        static void DispatchChecks()
        {
            GameSession.ObjectSpell(new S.ObjectSpell { ObjectID = 10, Location = new SDPoint(3, 4), Spell = Spell.Trap, Direction = 0 });
            Check(MapControl.Objects.TryGetValue(10, out var so) && so is SpellObject spo && spo.DrawFrame == 2360, "dispatch objectspell -> trap object");

            // 无 FloorItems 图集（batchmode AtlasDir 空）→ 不建对象优雅跳过
            GameSession.ObjectItem(new S.ObjectItem { ObjectID = 11, Name = "ProbeSword", NameColour = SDColor.White, Location = new SDPoint(1, 1), Image = 5 });
            Check(!MapControl.Objects.ContainsKey(11), "dispatch objectitem skipped (no atlas)");
            GameSession.ObjectGold(new S.ObjectGold { ObjectID = 12, Gold = 500, Location = new SDPoint(1, 1) });
            Check(!MapControl.Objects.ContainsKey(12), "dispatch objectgold skipped (no atlas)");
        }

        static void ItemChecks()
        {
            // ObjectItem 直接装载：batchmode FloorItems seam 非 null（GetTrueSize → Size.Empty）
            var item = new ItemObject(20);
            item.Load(new S.ObjectItem { ObjectID = 20, Name = "Stick", NameColour = SDColor.White, Location = new SDPoint(2, 2), Image = 7 });
            Check(item.Name == "Stick", "item name");
            Check(item.DrawFrame == 7, "item image frame");
            Check(item.Size.Width == 0 && item.Size.Height == 0, "item seam size empty");
            Check(MapControl.Objects.ContainsKey(20), "item registered");

            item.Process();
            Check(item.DisplayRectangle.X == 120 && item.DisplayRectangle.Y == 80 && item.DisplayRectangle.Width == 0 && item.DisplayRectangle.Height == 0, "item process displayrect centered");

            int[] golds = { 50, 150, 400, 800, 2000 };
            int[] frames = { 112, 113, 114, 115, 116 };
            for (int i = 0; i < golds.Length; i++)
            {
                var g = new ItemObject((uint)(30 + i));
                g.Load(new S.ObjectGold { ObjectID = (uint)(30 + i), Gold = (uint)golds[i], Location = new SDPoint(0, 0) });
                Check(g.DrawFrame == frames[i], $"gold {golds[i]} -> frame {frames[i]}");
            }
        }
    }
}
