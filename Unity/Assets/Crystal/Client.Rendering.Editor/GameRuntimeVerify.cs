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
    // C3 运行时主循环验证：连真实服务器走通 登录→进图→对象 spawn，用 GameRuntime.Tick 渲染到 RT，
    // 断言地图+对象非空（LastObjectDraws>0 且 nonBg>0）。
    // 用法（batchmode 经 Hub 会话，gameruntimeverify.ps1 编排起服务器）：
    //   CRYSTAL_NET_HOST/PORT/LOGIN_ID/LOGIN_PW/CHAR_NAME/NET_TIMEOUT/MAP_DIR/MAP_ATLAS_DIR/ATLAS_DIR [CRYSTAL_RT_W/H]
    static class GameRuntimeVerify
    {
        static bool _entered;
        static string _error;

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
                Console.WriteLine("gameruntime-verify: CRYSTAL_MAP_DIR / CRYSTAL_MAP_ATLAS_DIR / CRYSTAL_ATLAS_DIR not set");
                EditorApplication.Exit(2);
                return;
            }

            GameSession.MapDir = Path.GetFullPath(mapDir);
            GameRenderer.MapAtlasDir = Path.GetFullPath(mapAtlasDir);
            GameRenderer.AtlasDir = Path.GetFullPath(atlasDir);
            GameRenderer.BatchFloor = true;
            GameRuntime.ScreenW = GetInt("CRYSTAL_RT_W", 1024);
            GameRuntime.ScreenH = GetInt("CRYSTAL_RT_H", 640);

            GameSession.OnSelectReady += () =>
            {
                Console.WriteLine($"[gameruntime] select-ready chars={GameSession.Characters.Count}");
                if (GameSession.Characters.Count > 0)
                    GameSession.SelectCharacter(0);
                else
                    GameSession.CreateCharacter(charName, MirGender.Male, MirClass.Warrior);
            };
            GameSession.OnEnterGame += () => { _entered = true; Console.WriteLine("[gameruntime] enter-game"); };
            GameSession.OnError += m => { _error = m; Console.WriteLine($"[gameruntime] error {m}"); };

            Console.WriteLine($"[gameruntime] connect {host}:{port} id={id}");
            GameSession.Connect(host, port);
            GameSession.Login(id, pw);

            // 阶段1：登录→进图。
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs && _error == null
                && !(_entered && GameSession.MapReader != null && GameSession.User != null))
            {
                GameSession.Process();
                Thread.Sleep(10);
            }
            if (_error != null || !_entered || GameSession.MapReader == null || GameSession.User == null)
            {
                Console.WriteLine($"gameruntime-verify: FAIL enter {_error ?? "timeout-no-enter"} state={GameSession.State}");
                EditorApplication.Exit(2);
                return;
            }

            // 阶段2：GameRuntime.Tick 推进对象 Process 直到有对象实际绘制。
            var rt = RenderTexture.GetTemporary(GameRuntime.ScreenW, GameRuntime.ScreenH, 24, RenderTextureFormat.ARGB32);
            try
            {
                while (sw.ElapsedMilliseconds < timeoutMs && _error == null && GameRuntime.LastObjectDraws == 0)
                {
                    GameRuntime.Tick(rt);
                    Thread.Sleep(10);
                }
                int drawn = GameRuntime.LastObjectDraws;

                var px = ReadTopDown(rt, GameRuntime.ScreenW, GameRuntime.ScreenH);
                int nonBg = 0;
                for (int i = 0; i < px.Length; i++)
                    if (!(px[i].r == 26 && px[i].g == 26 && px[i].b == 26)) nonBg++;

                Console.WriteLine($"[gameruntime] state={GameSession.State} objects={MapControl.Objects.Count} drawn={drawn} nonBg={nonBg} elapsed={sw.ElapsedMilliseconds}ms");

                bool ok = _error == null && drawn > 0 && nonBg > 0;
                if (!ok) Console.WriteLine($"gameruntime-verify: FAIL drawn={drawn} nonBg={nonBg} {_error ?? ""}");
                Console.WriteLine($"gameruntime-verify: {(ok ? "PASS" : "FAIL")}");
                EditorApplication.Exit(ok ? 0 : 2);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static Color32[] ReadTopDown(RenderTexture rt, int w, int h)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            var px = read.GetPixels32();
            UnityEngine.Object.DestroyImmediate(read);
            RenderTexture.active = prev;
            return px;
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
