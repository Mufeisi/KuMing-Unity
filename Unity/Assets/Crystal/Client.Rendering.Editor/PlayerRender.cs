using System;
using System.Collections.Generic;
using System.IO;
using Client.MirObjects;
using Crystal.Client.Assets;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // R9 探针：玩家角色渲染（C 系列 Warrior/Wizard/Tao，非变形非刺客）。
    // 复刻 PlayerObject.SetLibraries + DrawBody/DrawHead/DrawWeapon 的核心语义：
    //   - 图集映射（PlayerObject.cs:555-574）：BodyLibrary=CArmours[Armour]、
    //     HairLibrary=CHair[Hair]、WeaponLibrary1=CWeapons[Weapon]
    //     （MLibrary.cs:95-99 InitLibrary 命名规则 path+i.ToString("00")）
    //   - 帧区间：FrameSet.Player 硬编码表（Frames.cs:157-198，manifest Frames 为空不可用）
    //     Standing(0,4,0) → OffSet=Count+Skip=4
    //   - 帧选择（PlayerObject.cs:763）：DrawFrame = Frame.Start + Frame.OffSet*Direction + FrameIndex
    //   - 层叠（PlayerObject.cs:5041-5065）：DrawBody(DrawFrame+ArmourOffSet) →
    //     DrawHead(DrawFrame+HairOffSet) → DrawWeapon(DrawFrame+WeaponOffSet)，
    //     Offset 按 Gender（男 0 / 女 808/808/416，PlayerObject.cs:586-588）
    //   - 锚点（MonsterObject.cs:435，同 R4）：DrawLocation=((x-camX+offX)*48,(y-camY+offY)*32)，
    //     精灵左上=DrawLocation+(OffX,OffY)
    // 验证：①每图层首个不透明像素 RT==图集源（锚点钉死，后画层遮挡校验）；②层叠顺序（Body 最底）。
    // 用法（batchmode）：
    //   CRYSTAL_ATLAS_DIR=<all> CRYSTAL_ARMOUR=<n> [CRYSTAL_HAIR=<n>] [CRYSTAL_WEAPON=<n>]
    //   [CRYSTAL_GENDER=0|1] [CRYSTAL_ACTION=Standing|Walking|Attack1|...] [CRYSTAL_DIR=0]
    //   [CRYSTAL_FRAME=0] [CRYSTAL_RT_W/H] [CRYSTAL_OUT]
    static class PlayerRender
    {
        const int CellWidth = 48;
        const int CellHeight = 32;

        static string _atlasDir;
        static readonly Dictionary<string, AtlasLibrary> _libs = new Dictionary<string, AtlasLibrary>();

        static string ActionKey(string name) => name switch
        {
            "Standing" => "Standing",
            "Walking" => "Walking",
            "Running" => "Running",
            "Attack1" => "Attack1",
            "Attack2" => "Attack2",
            "Attack3" => "Attack3",
            "Attack4" => "Attack4",
            "Spell" => "Spell",
            "Struck" => "Struck",
            "Die" => "Die",
            "Dead" => "Dead",
            "Revive" => "Revive",
            "Stance" => "Stance",
            "Stance2" => "Stance2",
            "Harvest" => "Harvest",
            "Mine" => "Mine",
            "Lunge" => "Lunge",
            _ => null,
        };

        public static void Run()
        {
            string atlasDir = Environment.GetEnvironmentVariable("CRYSTAL_ATLAS_DIR");
            string armStr = Environment.GetEnvironmentVariable("CRYSTAL_ARMOUR");
            if (string.IsNullOrEmpty(atlasDir) || string.IsNullOrEmpty(armStr))
            {
                Console.WriteLine("player-render: CRYSTAL_ATLAS_DIR / CRYSTAL_ARMOUR not set");
                EditorApplication.Exit(2);
                return;
            }
            _atlasDir = Path.GetFullPath(atlasDir);
            int gender = GetInt("CRYSTAL_GENDER", 0);
            int arm = int.Parse(armStr);
            int hair = GetInt("CRYSTAL_HAIR", -1);
            int wep = GetInt("CRYSTAL_WEAPON", -1);
            string actionName = Environment.GetEnvironmentVariable("CRYSTAL_ACTION");
            if (string.IsNullOrEmpty(actionName)) actionName = "Standing";
            int dir = GetInt("CRYSTAL_DIR", 0);
            int frame = GetInt("CRYSTAL_FRAME", 0);
            int rtW = GetInt("CRYSTAL_RT_W", 1152);
            int rtH = GetInt("CRYSTAL_RT_H", 640);

            string actionKey = ActionKey(actionName);
            if (actionKey == null)
            {
                Console.WriteLine($"player-render: unsupported action {actionName}");
                EditorApplication.Exit(2);
                return;
            }
            var action = (MirAction)Enum.Parse(typeof(MirAction), actionKey);
            if (!FrameSet.Player.TryGetValue(action, out var frm))
            {
                Console.WriteLine($"player-render: action {actionName} not in FrameSet.Player");
                EditorApplication.Exit(2);
                return;
            }

            // C 系列图集映射（PlayerObject.cs:555-574）
            var body = EnsureLib($"CArmour/{arm:D2}");
            if (body == null) { Console.WriteLine("player-render: body lib missing"); EditorApplication.Exit(2); return; }
            AtlasLibrary hLib = hair >= 0 ? EnsureLib($"CHair/{hair:D2}") : null;
            AtlasLibrary wLib = wep >= 0 ? EnsureLib($"CWeapon/{wep:D2}") : null;

            // 帧选择（PlayerObject.cs:763）：DrawFrame = Start + OffSet*Dir + FrameIndex
            int offSet = frm.Count + frm.Skip;
            int drawFrame = frm.Start + offSet * dir + frame;
            // Offset 按 Gender（PlayerObject.cs:586-588：男 0 / 女 808/808/416）
            int armOff = gender == 0 ? 0 : 808;
            int hairOff = gender == 0 ? 0 : 808;
            int wepOff = gender == 0 ? 0 : 416;

            // 锚点（同 R4）：DrawLocation 格锚点，精灵左上=DrawLocation+(OffX,OffY)
            int cx = rtW / 2 / CellWidth, cy = rtH / 2 / CellHeight - 1;
            int drawX = cx * CellWidth, drawY = cy * CellHeight;

            // 层叠（PlayerObject.cs:5041-5065：先 Body 后 Head/Weapon）
            var layers = new List<(SpriteFrame f, AtlasLibrary lib, string layer)>();
            layers.Add((body.Frames[drawFrame + armOff], body, "body"));
            if (hLib != null)
            {
                var hf = hLib.Frames[drawFrame + hairOff];
                if (!hf.Empty) layers.Add((hf, hLib, "hair"));
            }
            if (wLib != null)
            {
                var wf = wLib.Frames[drawFrame + wepOff];
                if (!wf.Empty) layers.Add((wf, wLib, "weapon"));
            }
            Console.WriteLine($"player-render: gender={gender} armour={arm} hair={hair} weapon={wep} action={actionName} dir={dir} frame={frame} start={frm.Start} offSet={offSet} drawFrame={drawFrame} off=({armOff},{hairOff},{wepOff}) layers={layers.Count}");

            var rt = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            int fail = -1;
            try
            {
                CrystalSpriteBatch.Begin(rt, rtW, rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                foreach (var (f, lib, layer) in layers)
                {
                    var tex = lib.GetPage(f.Page);
                    int sx = drawX + f.OffX, sy = drawY + f.OffY;
                    CrystalSpriteBatch.Draw(tex, new Rect(f.X, f.Y, f.Width, f.Height), new Vector3(sx, sy, 0f), Color.white);
                    Console.WriteLine($"  layer {layer}: idx={drawFrame + (layer == "body" ? armOff : layer == "hair" ? hairOff : wepOff)} {f.Width}x{f.Height} page={f.Page} off=({f.OffX},{f.OffY}) at=({sx},{sy})");
                }
                CrystalSpriteBatch.End();

                // 回读 + 锚点验证：每图层首个不透明像素 RT==图集源（后画层可遮挡）
                var read = new Texture2D(rtW, rtH, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, rtW, rtH), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                var px = read.GetPixels32();

                fail = 0;
                for (int li = 0; li < layers.Count; li++)
                {
                    var (f, lib, layer) = layers[li];
                    var tex = lib.GetPage(f.Page);
                    int tw = tex.width, th = tex.height;
                    var src = tex.GetPixels32();
                    int sx0 = drawX + f.OffX, sy0 = drawY + f.OffY;
                    int d = 0;
                    for (int ly = 0; ly < f.Height && d == 0; ly++)
                        for (int lx = 0; lx < f.Width && d == 0; lx++)
                        {
                            var s = src[(th - 1 - (f.Y + ly)) * tw + (f.X + lx)];
                            if (s.a != 255) continue;
                            int sx = sx0 + lx, sy = sy0 + ly;
                            if (sx < 0 || sx >= rtW || sy < 0 || sy >= rtH) continue;
                            // 仅后画层（li 之后）可遮挡
                            bool occluded = false;
                            for (int lj = li + 1; lj < layers.Count; lj++)
                            {
                                var (f2, _, _) = layers[lj];
                                int ox = drawX + f2.OffX, oy = drawY + f2.OffY;
                                if (sx >= ox && sx < ox + f2.Width && sy >= oy && sy < oy + f2.Height) { occluded = true; break; }
                            }
                            if (occluded) continue;
                            var got = px[sy * rtW + sx];
                            if (got.r != s.r || got.g != s.g || got.b != s.b || got.a != s.a)
                            {
                                Console.WriteLine($"  presence diff {layer} local({lx},{ly}) screen({sx},{sy}) src({s.r:X2}{s.g:X2}{s.b:X2}{s.a:X2}) got({got.r:X2}{got.g:X2}{got.b:X2}{got.a:X2})");
                                d++;
                            }
                        }
                    Console.WriteLine($"  {layer} presence fail={d}");
                    fail += d;
                }

                // 正立 PNG
                var fl = new Color32[px.Length];
                for (int y = 0; y < rtH; y++)
                    Array.Copy(px, (rtH - 1 - y) * rtW, fl, y * rtW, rtW);
                read.SetPixels32(fl);
                read.Apply();
                string outPath = Environment.GetEnvironmentVariable("CRYSTAL_OUT");
                if (string.IsNullOrEmpty(outPath)) outPath = "Build/player-render.png";
                string fullOut = Path.GetFullPath(outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                File.WriteAllBytes(fullOut, read.EncodeToPNG());
                Console.WriteLine($"player-render: wrote {fullOut} fail={fail}");
                UnityEngine.Object.DestroyImmediate(read);
                EditorApplication.Exit(fail == 0 ? 0 : 1);
            }
            finally
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                foreach (var kv in _libs) kv.Value.UnloadAll();
                _libs.Clear();
                CrystalSpriteBatch.ReleaseMeshes();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        static AtlasLibrary EnsureLib(string rel)
        {
            if (_libs.TryGetValue(rel, out var lib)) return lib;
            string man = Path.Combine(_atlasDir, rel + ".json");
            if (!File.Exists(man))
            {
                Console.WriteLine($"  player-render: WARN manifest missing {man}");
                return null;
            }
            lib = AtlasLibrary.Load(man);
            _libs[rel] = lib;
            return lib;
        }

        static int GetInt(string name, int def)
        {
            string s = Environment.GetEnvironmentVariable(name);
            return int.TryParse(s, out int v) ? v : def;
        }
    }
}
