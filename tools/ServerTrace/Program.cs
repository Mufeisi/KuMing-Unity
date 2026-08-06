using System.Net.Sockets;
using Server;
using Server.MirEnvir;
using C = ClientPackets;

namespace ServerTrace;

static class Program
{
    private static byte[] _recvBuf = Array.Empty<byte>();

    static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "record") return Record(args);
        if (args.Length >= 1 && args[0] == "diff") return Diff(args);
        if (args.Length >= 1 && args[0] == "stats") return Stats(args);

        Console.WriteLine("Usage:");
        Console.WriteLine("  ServerTrace record [--host --data <dir>] [--port <p>] --out <file>");
        Console.WriteLine("      --host: host the server in-process from <dir> (cwd = data dir, boots Envir, waits for listener)");
        Console.WriteLine("      --port: port to record against (default: from Setup.ini when hosting, else 7000)");
        Console.WriteLine("      --out : trace file");
        Console.WriteLine("  ServerTrace diff <traceA> <traceB>   byte-compare two traces, exit 0 if identical");
        return 2;
    }

    static int Stats(string[] args)
    {
        // Dump loaded per-class base stats (Settings.Load parses Configs/*.ini) to verify
        // the config format matches LoadBaseStats's expected per-stat [Stat] sections.
        Packet.IsServer = true;
        Console.WriteLine($"cwd={Environment.CurrentDirectory}");
        Console.WriteLine($"configPath={Settings.ConfigPath}");
        var ini = Path.Combine(Settings.ConfigPath, "BaseStatsWarrior.ini");
        Console.WriteLine($"ini={ini} exists={File.Exists(ini)}");
        if (File.Exists(ini))
        {
            var r = new InIReader(ini);
            Console.WriteLine($"Accuracy.Formula={r.ReadString("Accuracy", "Formula", "<null>", false)}");
            Console.WriteLine($"Accuracy.Base={r.ReadInt32("Accuracy", "Base", -999, false)}");
            Console.WriteLine($"Warrior.StartAccuracy={r.ReadInt32("Warrior", "StartAccuracy", -999, false)}");
        }
        Settings.Load();
        foreach (var cls in Settings.ClassBaseStats)
        {
            Console.WriteLine($"[{cls.Job}] stats={cls.Stats.Count}");
            foreach (var s in cls.Stats)
                Console.WriteLine($"  {s.Type}: formula={s.FormulaType} base={s.Base} gain={s.Gain} rate={s.GainRate} max={s.Max}");
        }
        return 0;
    }

    static int Record(string[] args)
    {
        int port = 7000;
        string outFile = "trace.txt";
        string dataDir = null;
        bool host = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
            else if (args[i] == "--out" && i + 1 < args.Length) outFile = args[++i];
            else if (args[i] == "--data" && i + 1 < args.Length) dataDir = args[++i];
            else if (args[i] == "--host") host = true;
        }

        // 在 HostServer 切 cwd 之前把输出路径固定为绝对路径，避免写入随数据目录漂移
        outFile = Path.GetFullPath(outFile);

        if (host)
            port = HostServer(dataDir, port);

        using var client = new TcpClient();
        client.NoDelay = true;
        client.Connect("127.0.0.1", port);
        using var stream = client.GetStream();

        var trace = new List<string>();

        void Send(Packet p)
        {
            var bytes = p.GetPacketBytes().ToArray();
            stream.Write(bytes, 0, bytes.Length);
        }

        void Drain(int waitMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
            var buf = new byte[65536];
            while (DateTime.UtcNow < deadline)
            {
                while (stream.DataAvailable)
                {
                    int n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0) return;
                    AppendFrames(buf, n, trace);
                }
                Thread.Sleep(10);
            }
        }

        Send(new C.ClientVersion { VersionHash = Array.Empty<byte>() });
        Drain(300);

        for (int i = 0; i < 20; i++)
            Send(new C.KeepAlive { Time = 0 });
        Drain(500);

        Send(new C.Login { AccountID = "ZZZTRACE", Password = "ZZZTRACE" });
        Drain(500);

        client.Close();
        File.WriteAllLines(outFile, trace);
        Console.WriteLine($"recorded {trace.Count} packets -> {outFile}");
        // Envir boots foreground threads; force-exit so the harness doesn't hang after Main returns
        Environment.Exit(trace.Count > 0 ? 0 : 1);
        return 0;
    }

    static int HostServer(string dataDir, int port)
    {
        // 全程保持 cwd = dataDir：服务端相对路径（AccountPath 等）按调用时 cwd 解析，
        // boot 后还原 cwd 会导致存库写到别处。进程靠 Environment.Exit 退出，无需还原。
        if (dataDir != null)
            Environment.CurrentDirectory = Path.GetFullPath(dataDir);

        Packet.IsServer = true;
        Settings.Load();
        Settings.Port = (ushort)port;
        Envir.Main.Start();

        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var probe = new TcpClient();
                probe.Connect("127.0.0.1", port);
                probe.Close();
                Console.WriteLine($"server ready on port {port}");
                // probe connection just blocked 127.0.0.1 for IPBlockSeconds; wait it out
                // so the trace client isn't rejected at accept
                Thread.Sleep(6000);
                return port;
            }
            catch (SocketException)
            {
                Thread.Sleep(500);
            }
        }
        throw new TimeoutException($"server did not open port {port} within 180s (boot failed: check CanStartEnvir / maps / DB)");
    }

    static int Diff(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("diff <traceA> <traceB>");
            return 2;
        }
        var a = File.ReadAllLines(args[1]);
        var b = File.ReadAllLines(args[2]);

        if (a.SequenceEqual(b))
        {
            Console.WriteLine($"identical: {a.Length} packets match");
            return 0;
        }

        int shown = 0;
        int n = Math.Max(a.Length, b.Length);
        for (int i = 0; i < n && shown < 10; i++)
        {
            bool same = i < a.Length && i < b.Length && a[i] == b[i];
            if (same) continue;
            Console.WriteLine($"line {i}:");
            Console.WriteLine($"  A: {(i < a.Length ? a[i] : "<none>")}");
            Console.WriteLine($"  B: {(i < b.Length ? b[i] : "<none>")}");
            shown++;
        }
        Console.WriteLine($"DIFFERS: {a.Length} vs {b.Length} packets, first {shown} shown");
        return 1;
    }

    static void AppendFrames(byte[] data, int count, List<string> trace)
    {
        if (count <= 0) return;
        var buf = new byte[_recvBuf.Length + count];
        Buffer.BlockCopy(_recvBuf, 0, buf, 0, _recvBuf.Length);
        Buffer.BlockCopy(data, 0, buf, _recvBuf.Length, count);
        _recvBuf = buf;

        while (_recvBuf.Length >= 4)
        {
            int len = BitConverter.ToUInt16(_recvBuf, 0);
            if (len > _recvBuf.Length || len < 2)
            {
                _recvBuf = Array.Empty<byte>();
                break;
            }
            short id = BitConverter.ToInt16(_recvBuf, 2);
            trace.Add($"{id}\t{Convert.ToHexString(_recvBuf, 0, len)}");
            var rest = new byte[_recvBuf.Length - len];
            Buffer.BlockCopy(_recvBuf, len, rest, 0, rest.Length);
            _recvBuf = rest;
        }
    }
}
