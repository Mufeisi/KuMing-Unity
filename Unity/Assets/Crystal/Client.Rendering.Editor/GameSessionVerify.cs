using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Client;
using Client.MirObjects;
using Client.MirScenes;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // GameSession 运行时网络会话验证：连真实服务器（Server.exe）走通 登录→（建号）→进图→对象 spawn→渲染。
    // 用法（batchmode 经 Hub 会话，gamesessionverify.ps1 编排起服务器）：
    //   CRYSTAL_NET_HOST/PORT/LOGIN_ID/LOGIN_PW/CHAR_NAME/NET_TIMEOUT/MAP_DIR/MAP_ATLAS_DIR
    static class GameSessionVerify
    {
        static bool _entered;
        static string _error;
        static bool _rendered;

        public static void Run()
        {
            string host = Env("CRYSTAL_NET_HOST", "127.0.0.1");
            int port = GetInt("CRYSTAL_NET_PORT", 7000);
            string id = Env("CRYSTAL_LOGIN_ID", "pcplayer");
            string pw = Env("CRYSTAL_LOGIN_PW", "pcplayer");
            string charName = Env("CRYSTAL_CHAR_NAME", "pcplayer");
            string mapDir = Env("CRYSTAL_MAP_DIR", "");
            string mapAtlasDir = Env("CRYSTAL_MAP_ATLAS_DIR", "");
            string atlasDir = Env("CRYSTAL_ATLAS_DIR", "");
            int timeoutMs = GetInt("CRYSTAL_NET_TIMEOUT", 60000);
            if (string.IsNullOrEmpty(mapDir) || string.IsNullOrEmpty(mapAtlasDir) || string.IsNullOrEmpty(atlasDir))
            {
                Console.WriteLine("gamesession-verify: CRYSTAL_MAP_DIR / CRYSTAL_MAP_ATLAS_DIR / CRYSTAL_ATLAS_DIR not set");
                EditorApplication.Exit(2);
                return;
            }

            GameSession.MapDir = Path.GetFullPath(mapDir);
            GameRenderer.MapAtlasDir = Path.GetFullPath(mapAtlasDir);
            GameRenderer.AtlasDir = Path.GetFullPath(atlasDir);
            GameRenderer.BatchFloor = true;

            GameSession.OnSelectReady += () =>
            {
                Console.WriteLine($"[gamesession] select-ready chars={GameSession.Characters.Count}");
                if (GameSession.Characters.Count > 0)
                    GameSession.SelectCharacter(0);
                else
                    GameSession.CreateCharacter(charName, MirGender.Male, MirClass.Warrior);
            };
            GameSession.OnEnterGame += () => { _entered = true; Console.WriteLine("[gamesession] enter-game"); };
            GameSession.OnError += m => { _error = m; Console.WriteLine($"[gamesession] error {m}"); };

            Console.WriteLine($"[gamesession] connect {host}:{port} id={id}");
            GameSession.Connect(host, port);
            GameSession.Login(id, pw);

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs && _error == null
                && !(_entered && GameSession.MapReader != null && GameSession.User != null))
            {
                GameSession.Process();
                Thread.Sleep(10);
            }
            sw.Stop();

            Console.WriteLine($"[gamesession] state={GameSession.State} entered={_entered} map={GameSession.MapFileName} user={GameSession.User?.Name} objects={MapControl.Objects.Count} elapsed={sw.ElapsedMilliseconds}ms");

            bool ok = _error == null && _entered && GameSession.MapReader != null && GameSession.User != null;
            if (!ok)
            {
                Console.WriteLine($"gamesession-verify: FAIL {_error ?? "timeout-no-enter"}");
                EditorApplication.Exit(2);
                return;
            }

            // 再等 2s 让怪物/NPC spawn 包到达
            Thread.Sleep(2000);
            GameSession.Process();

            // 渲染断言：User 位置中心绘制地图到 RT，非空。
            RenderCheck();
            if (!_rendered)
            {
                Console.WriteLine("gamesession-verify: FAIL render blank");
                EditorApplication.Exit(2);
                return;
            }

            Console.WriteLine($"[gamesession] objects={MapControl.Objects.Count} monsters={CountMonsters()}");
            Console.WriteLine("gamesession-verify: PASS");
            EditorApplication.Exit(0);
        }

        static void RenderCheck()
        {
            var user = GameSession.User;
            var cells = GameSession.MapReader.MapCells;
            int cx = user.Movement.X, cy = user.Movement.Y;
            int rtW = 1024, rtH = 640;
            int offX = rtW / 2 / MapControl.CellWidth;
            int offY = rtH / 2 / MapControl.CellHeight - 1;
            int rangeX = offX + 6, rangeY = offY + 6;

            var libByIndex = GameRenderer.BuildLibIndex(0, cells, GameSession.MapReader.Width, GameSession.MapReader.Height);
            var rt = RenderTexture.GetTemporary(rtW, rtH, 24, RenderTextureFormat.ARGB32);
            try
            {
                CrystalSpriteBatch.Begin(rt, rtW, rtH);
                CrystalSpriteBatch.Clear(new Color(0.1f, 0.1f, 0.1f, 1f));
                GameRenderer.DrawMapTiles(cells, GameSession.MapReader, cx, cy, offX, offY, rangeX, rangeY, libByIndex);
                CrystalSpriteBatch.End();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var read = new Texture2D(rtW, rtH, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, rtW, rtH), 0, 0);
                var px = read.GetPixels32();
                UnityEngine.Object.DestroyImmediate(read);
                RenderTexture.active = prev;

                int nonBg = 0;
                for (int i = 0; i < px.Length; i++)
                    if (!(px[i].r == 26 && px[i].g == 26 && px[i].b == 26)) nonBg++;
                _rendered = nonBg > 0;
                Console.WriteLine($"[gamesession] render center=({cx},{cy}) nonBg={nonBg}");
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static int CountMonsters()
        {
            int n = 0;
            foreach (var kv in MapControl.Objects)
                if (kv.Value is MonsterObject) n++;
            return n;
        }

        static string Env(string name, string def)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? def : v;
        }

        static int GetInt(string name, int def)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return int.TryParse(v, out int r) ? r : def;
        }
    }
}
