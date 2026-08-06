using System;
using System.Collections.Generic;
using Client;
using Crystal.Client.Core.MirMath;
using Client.MirGraphics;
using Client.MirGraphics.Particles;
using Client.MirScenes;

namespace ParticleVerify
{
    // R7 粒子系统确定性验证探针。
    // 目标：验证逐字移植的粒子状态机（ParticleEngine/Particle/FogParticle）在确定性时钟+seed 下，
    // 生成节律、帧推进（ProcessImage）、位移（Update/Position+=Velocity）、wrap-around（OnPositionChanged）、
    // 消亡（AliveTime）、偏移（ParticlesOffSet）、绘制帧选择 的语义与旧客户端逐字一致。
    //
    // 关键点（对照旧客户端）：
    //   - CMain.Time 由 Timer.ElapsedMilliseconds 驱动（单调毫秒）→ 探针直接赋值。
    //   - StartTime 固定基准 → CMain.Now = StartTime.AddMilliseconds(Time) 确定。
    //   - CMain.Random 在 ctor 一次 seed → 生成序列确定。
    //   - ParticleImageInfo.NextFrame 在 Duration 赋值前计算（保留原 bug 行为，逐字一致）。
    //
    // 验证命令：dotnet run --project tools/ParticleVerify，exit 0 即通过。

    static class Program
    {
        // 记录绘制调用序列（MLibrary seam 虚方法覆写）。
        sealed class SpyLibrary : MLibrary
        {
            public List<string> Calls = new List<string>();
            public SpyLibrary(string file) : base(file) { }
            public override void Draw(int index, Point point, Color colour, bool offSet, float opacity)
            { Calls.Add($"D:{index}@({point.X},{point.Y})"); }
            public override void DrawBlend(int index, Point point, Color colour, bool offSet, float rate)
            { Calls.Add($"DB:{index}@({point.X},{point.Y})"); }
        }

        static int fails = 0;

        static void Check(string name, bool cond, string detail)
        {
            if (cond) Console.WriteLine($"  [PASS] {name}: {detail}");
            else { Console.WriteLine($"  [FAIL] {name}: {detail}"); fails++; }
        }

        static int Main()
        {
            // 固定基准：进程内绝对时间无关，只依赖 CMain.Time 增量。
            CMain.StartTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            CMain.Time = 0;
            CMain.Random = new Random(12345);
            Settings.ScreenWidth = 1024;
            Settings.ScreenHeight = 768;

            // ProcessImage() 会写 GameScene.Scene.MapControl.TextureValid → 装配最小场景。
            GameScene.Scene = new GameScene { MapControl = new MapControl() };

            Console.WriteLine("== R7 Particle state machine deterministic verification ==");
            Console.WriteLine($"StartTime={CMain.StartTime:o} seed=12345 screen={Settings.ScreenWidth}x{Settings.ScreenHeight}");

            RunGenerateThrottle();
            RunUpdateMovement();
            RunWrapAround();
            RunLifetime();
            RunOffset();
            RunDrawFrame();
            RunDeterminism();

            Console.WriteLine(fails == 0 ? "R7 PASS (exit 0)" : $"R7 FAIL ({fails} checks)");
            return fails == 0 ? 0 : 1;
        }

        // 生成节律：GenerateParticles=true 时每 UpdateDelay(50ms) 生成 1 个；false 时不生成。
        static void RunGenerateThrottle()
        {
            Console.WriteLine("-- 生成节律 (GenerateParticles throttle) --");
            var lib = new SpyLibrary("Weather");
            lib.ImageSize = new Size(512, 512);
            var engine = new ParticleEngine(
                new List<ParticleImageInfo> { new ParticleImageInfo(lib, 0, 1, 50) },
                Vector2.Zero, ParticleType.Fog);

            Check("初始 0 粒子", engine.ParticleCount == 0, $"count={engine.ParticleCount}");

            // t=0 Process()：CMain.Now(0) > NextParticleTime(MinValue) → 生成第 1 个；NextParticleTime=50。
            engine.Process();
            Check("t=0 生成第 1 个", engine.ParticleCount == 1, $"count={engine.ParticleCount}");

            // t=20 Process()：Now(20) < NextParticleTime(50) → 不生成。
            CMain.Time = 20;
            engine.Process();
            Check("t=20 节流不生成", engine.ParticleCount == 1, $"count={engine.ParticleCount}");

            // t=50 Process()：Now(50) == NextParticleTime(50)，Now > NextParticleTime 为 false → 仍不生成？
            // 对照 Process()：`if (GenerateParticles && CMain.Now > NextParticleTime)` 严格大于。
            CMain.Time = 50;
            engine.Process();
            Check("t=50 严格大于才生成", engine.ParticleCount == 1, $"count={engine.ParticleCount}");

            // t=51 Process()：Now(51) > 50 → 生成第 2 个。
            CMain.Time = 51;
            engine.Process();
            Check("t=51 生成第 2 个", engine.ParticleCount == 2, $"count={engine.ParticleCount}");
        }

        // 位移：Update() 每 UpdateDelay 节流一次 Position+=Velocity。
        // 注意：粒子生成时已先经过一次 Process() 内 Update()（消费初始 NextUpdateTime=MinValue，位置 (0,0)+零速度不变），
        // 故生成后 NextUpdateTime 已前移到 t0+UpdateDelay；显式 Update() 序列验证 `<` 边界（Now==Next 时更新）。
        static void RunUpdateMovement()
        {
            Console.WriteLine("-- 位移 (Update Position+=Velocity) --");
            var lib = new SpyLibrary("Weather");
            lib.ImageSize = new Size(32, 32);
            var engine = new ParticleEngine(
                new List<ParticleImageInfo> { new ParticleImageInfo(lib, 0, 1, 50) },
                Vector2.Zero, ParticleType.Fog);

            // t=0 生成（内嵌 Update 消费 MinValue NextUpdateTime，NextUpdateTime→50）。
            CMain.Time = 0;
            engine.Process();
            var p = engine.Particles[0];
            p.Position = new Vector2(100, 200);
            p.Velocity = new Vector2(2, -3);
            p.UpdateDelay = TimeSpan.FromMilliseconds(50);

            // t=0：Now(0) < NextUpdateTime(50) → 节流跳过。
            p.Update();
            Check("t=0 节流不位移", p.Position == new Vector2(100, 200), $"pos={p.Position}");

            // t=50：Now(50) < 50 false → 更新（`<` 边界，Now==Next 也更新）。位移 (102,197)；NextUpdateTime=100。
            CMain.Time = 50;
            p.Update();
            Check("t=50 边界更新", p.Position == new Vector2(102, 197), $"pos={p.Position}");

            // t=60：Now(60) < NextUpdateTime(100) → 节流跳过。
            CMain.Time = 60;
            p.Update();
            Check("t=60 节流不位移", p.Position == new Vector2(102, 197), $"pos={p.Position}");

            // t=100：Now(100) < 100 false → 更新。位移 (104,194)。
            CMain.Time = 100;
            p.Update();
            Check("t=100 边界更新", p.Position == new Vector2(104, 194), $"pos={p.Position}");
        }

        // wrap-around：粒子移出屏幕边界 → OnPositionChanged 按 ImageInfo.Size 取模回卷。
        // 基类 OnPositionChanged 用 xreset/yreset = Size*ceil(screen/Size)+2。
        static void RunWrapAround()
        {
            Console.WriteLine("-- wrap-around (OnPositionChanged) --");
            var lib = new SpyLibrary("Weather");
            lib.ImageSize = new Size(512, 512);
            var engine = new ParticleEngine(
                new List<ParticleImageInfo> { new ParticleImageInfo(lib, 0, 1, 50) },
                Vector2.Zero, ParticleType.Fog);
            CMain.Time = 0;
            engine.Process();
            var p = engine.Particles[0];

            // xreset = 512*ceil(1024/512+2)=512*(2+2)=2048；yreset = 512*ceil(768/512+2)=512*(2+2)=2048。
            // Y 下溢：Position.Y=-1025 < -1024 → += yreset(0,2048)。
            p.Position = new Vector2(500, -1025);
            Check("Y 下溢 +yreset", p.Position == new Vector2(500, 1023), $"pos={p.Position}");

            // X 下溢：Position.X=-1025 < -1024 → += xreset(2048,0)。
            p.Position = new Vector2(-1025, 500);
            Check("X 下溢 +xreset", p.Position == new Vector2(1023, 500), $"pos={p.Position}");

            // Y 上溢：Position.Y=1024+512=1536 > 1024+512 → -= yreset。
            p.Position = new Vector2(500, 1537);
            Check("Y 上溢 -yreset", p.Position == new Vector2(500, -511), $"pos={p.Position}");

            // X 上溢：Position.X=1024+512=1536 > 1024+512 → -= xreset。
            p.Position = new Vector2(1537, 500);
            Check("X 上溢 -xreset", p.Position == new Vector2(-511, 500), $"pos={p.Position}");
        }

        // 消亡：AliveTime 到期 → OnParticleEnd + RemoveAt。
        // 注意：GenerateParticles=true 时引擎每 50ms 继续生成新粒子，故计数断言须考虑生成与消亡的叠加。
        static void RunLifetime()
        {
            Console.WriteLine("-- 消亡 (AliveTime) --");
            var lib = new SpyLibrary("Weather");
            lib.ImageSize = new Size(16, 16);
            var engine = new ParticleEngine(
                new List<ParticleImageInfo> { new ParticleImageInfo(lib, 0, 1, 50) },
                Vector2.Zero, ParticleType.Fog);
            CMain.Time = 0;
            engine.Process();   // t=0 生成第 1 个。
            engine.GenerateParticles = false; // 关掉生成，隔离消亡语义。

            var p = engine.Particles[0];
            p.AliveTime = CMain.Now.AddMilliseconds(100);

            // t=99：Now(99) > AliveTime(100) false → 存活。
            CMain.Time = 99;
            engine.Process();
            Check("t=99 未到期存活", engine.ParticleCount == 1, $"count={engine.ParticleCount}");

            // t=101：Now(101) > 100 → 移除。
            CMain.Time = 101;
            engine.Process();
            Check("t=101 到期移除", engine.ParticleCount == 0, $"count={engine.ParticleCount}");
        }

        // ParticlesOffSet：FogParticle 类型跳过（getType==FogParticle continue），其余 +offset。
        static void RunOffset()
        {
            Console.WriteLine("-- ParticlesOffSet --");
            var lib = new SpyLibrary("Weather");
            lib.ImageSize = new Size(16, 16);
            // Fog 引擎产 FogParticle。
            var fogEngine = new ParticleEngine(
                new List<ParticleImageInfo> { new ParticleImageInfo(lib, 0, 1, 50) },
                Vector2.Zero, ParticleType.Fog);
            CMain.Time = 0;
            fogEngine.Process();
            var fp = fogEngine.Particles[0];
            fp.Position = new Vector2(10, 20);
            fogEngine.ParticlesOffSet(new Point(5, 7));
            Check("FogParticle 偏移被跳过", fp.Position == new Vector2(10, 20), $"pos={fp.Position}");

            // 基础 Particle（如 Rain/WhiteEmber）不跳过。
            var baseEngine = new ParticleEngine(
                new List<ParticleImageInfo> { new ParticleImageInfo(lib, 0, 1, 50) },
                Vector2.Zero, ParticleType.Rain);
            CMain.Time = 0;
            baseEngine.Process();
            var bp = baseEngine.Particles[0];
            bp.Position = new Vector2(10, 20);
            baseEngine.ParticlesOffSet(new Point(5, 7));
            Check("基础 Particle 偏移生效", bp.Position == new Vector2(15, 27), $"pos={bp.Position}");
        }

        // 绘制帧选择：Draw 用 BaseIndex+CurrentFrame，Blend=true → DrawBlend。
        static void RunDrawFrame()
        {
            Console.WriteLine("-- 绘制帧选择 (Draw) --");
            var lib = new SpyLibrary("Weather");
            lib.ImageSize = new Size(32, 32);
            // Rain → Blend=true → DrawBlend；ImageInfo count=3。
            var engine = new ParticleEngine(
                new List<ParticleImageInfo> { new ParticleImageInfo(lib, 10, 3, 50) },
                Vector2.Zero, ParticleType.Rain);
            CMain.Time = 0;
            engine.Process();
            var p = engine.Particles[0];
            p.Position = new Vector2(100, 100);

            lib.Calls.Clear();
            p.Draw();
            Check("Blend=true 走 DrawBlend 帧 10", lib.Calls.Count == 1 && lib.Calls[0] == "DB:10@(100,100)", lib.Calls.Count == 1 ? lib.Calls[0] : "no call");

            // ProcessImage 帧推进。ParticleImageInfo ctor 的逐字 quirk：NextFrame 在 Duration 赋值前计算
            // （Duration 默认 0 → NextFrame=Start=0），故首次 ProcessImage 时 Time>0 即推进；
            // 推进后 else 分支用真实 Duration(150=50*3) 重算 NextFrame=Start+(150/3)*(frame+1)，恢复节律。
            p.ProcessImage();
            Check("t=0 帧 0 不推进", p.ImageInfo.CurrentFrame == 0, $"frame={p.ImageInfo.CurrentFrame}");

            // t=50：NextFrame=0，50<=0 false → frame=1，NextFrame=0+50*2=100。
            CMain.Time = 50;
            p.ProcessImage();
            Check("t=50 推进帧 1（NextFrame=100）", p.ImageInfo.CurrentFrame == 1, $"frame={p.ImageInfo.CurrentFrame}");

            // t=99：99<=100 → 节流。
            CMain.Time = 99;
            p.ProcessImage();
            Check("t=99 节流不推进", p.ImageInfo.CurrentFrame == 1, $"frame={p.ImageInfo.CurrentFrame}");

            // t=101：101<=100 false → frame=2，NextFrame=0+50*3=150。
            CMain.Time = 101;
            p.ProcessImage();
            Check("t=101 推进帧 2（NextFrame=150）", p.ImageInfo.CurrentFrame == 2, $"frame={p.ImageInfo.CurrentFrame}");

            // t=151：151<=150 false → ++frame=3>=Count(3) → 回绕 0，Start=151+Delay(0)=151，NextFrame=151+50*1=201。
            CMain.Time = 151;
            p.ProcessImage();
            Check("t=151 回绕帧 0（Start=151）", p.ImageInfo.CurrentFrame == 0 && p.ImageInfo.Start == 151, $"frame={p.ImageInfo.CurrentFrame} Start={p.ImageInfo.Start}");
        }

        // 确定性：同一 seed + 同一时间线 → 两次独立运行生成完全一致的粒子状态序列。
        static void RunDeterminism()
        {
            Console.WriteLine("-- 确定性 (同 seed 同时间线同结果) --");
            var seq1 = DeterministicRun(12345);
            var seq2 = DeterministicRun(12345);
            var seq3 = DeterministicRun(999);

            bool same = seq1.Count == seq2.Count;
            if (same)
                for (int i = 0; i < seq1.Count && same; i++)
                    same = seq1[i] == seq2[i];

            bool diffSeed = seq3.Count != seq1.Count || seq3[0] != seq1[0];
            Check("同 seed 两次运行序列一致", same, $"len={seq1.Count}");
            Check("不同 seed 序列不同", diffSeed, $"len3={seq3.Count} first3={seq3[0]}");
        }

        static List<string> DeterministicRun(int seed)
        {
            CMain.StartTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            CMain.Time = 0;
            CMain.Random = new Random(seed);
            Settings.ScreenWidth = 1024;
            Settings.ScreenHeight = 768;
            GameScene.Scene = new GameScene { MapControl = new MapControl() };

            var lib = new SpyLibrary("Weather");
            lib.ImageSize = new Size(64, 64);
            // WhiteEmber 的 Position/Velocity/Size/AliveTime 全部消费 CMain.Random → 序列对 seed 敏感。
            var engine = new ParticleEngine(
                new List<ParticleImageInfo> { new ParticleImageInfo(lib, 0, 1, 50) },
                Vector2.Zero, ParticleType.WhiteEmber);

            var snap = new List<string>();
            for (long t = 0; t <= 300; t += 50)
            {
                CMain.Time = t;
                engine.Process();
                foreach (var p in engine.Particles)
                    snap.Add($"{p.Position.X:0.0},{p.Position.Y:0.0}");
            }
            return snap;
        }
    }
}
