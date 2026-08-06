using System.Net.Sockets;
using Server;
using Server.MirEnvir;
using C = ClientPackets;

namespace ServerStress;

static class Program
{
    private static int _live; // 客户端侧仍持有未关闭 socket 的连接数
    static int AliveClients() => Volatile.Read(ref _live);
    static int Main(string[] args)
    {
        string dataDir = null;
        int conns = 500;
        int secs = 20;
        int port = 7000;
        bool raw = false; // 跳过 MaxIP/IPBlockSeconds 覆写（隔离覆写是否为 accept 链停摆元凶）
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data" && i + 1 < args.Length) dataDir = args[++i];
            else if (args[i] == "--conns" && i + 1 < args.Length) conns = int.Parse(args[++i]);
            else if (args[i] == "--secs" && i + 1 < args.Length) secs = int.Parse(args[++i]);
            else if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
            else if (args[i] == "--raw") raw = true;
        }

        Host(dataDir, port, raw);

        int connected = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(secs));
        var tasks = new Task[conns];
        for (int i = 0; i < conns; i++)
        {
            int id = i;
            tasks[i] = RunConn(port, cts.Token, () => Interlocked.Increment(ref connected));
        }

        // 采样服务端实际接受连接数峰值（Connections.Count），区分"客户端 TCP 建连成功"与"服务端真正接受"
        int peakServerConns = 0;
        var timeline = new List<string>();
        var serverLog = new List<string>();
        using var sampler = new System.Threading.Timer(_ =>
        {
            while (Server.MessageQueue.Instance.MessageLog.TryDequeue(out var m)) serverLog.Add(m);
            int n;
            lock (Envir.Main.Connections)
                n = Envir.Main.Connections.Count;
            if (n > peakServerConns) Interlocked.Exchange(ref peakServerConns, n);
            if (timeline.Count < 60)
            {
                int s;
                lock (Envir.Main.StatusConnections)
                    s = Envir.Main.StatusConnections.Count;
                timeline.Add($"{DateTime.Now:HH:mm:ss.fff} conns={n} status={s} tcpAlive={AliveClients()}");
            }
        }, null, 100, 100);

        // let connections establish and traffic flow before sampling
        Thread.Sleep(TimeSpan.FromSeconds(secs) - TimeSpan.FromSeconds(2));
        sampler.Change(Timeout.Infinite, Timeout.Infinite);
        while (Server.MessageQueue.Instance.MessageLog.TryDequeue(out var m)) serverLog.Add(m);
        File.WriteAllLines("stress-timeline.txt", timeline);
        File.WriteAllLines("server-log.txt", serverLog);

        try { Task.WaitAll(tasks); }
        catch (AggregateException) { }
        catch (OperationCanceledException) { }

        int connP95 = Report("conn_process", Envir.Main.ConnProcessLatency);
        Report("full_tick  ", Envir.Main.TickLatency);

        Console.WriteLine($"conns_connected={connected}/{conns}  server_accepted_peak={peakServerConns}/{conns}");
        Environment.Exit(connected == conns && peakServerConns >= conns && connP95 < 5 ? 0 : 1);
        return 1;
    }

    static int Report(string name, int[] data)
    {
        var samples = (int[])data.Clone();
        Array.Sort(samples);
        int Percentile(int pct) => samples[Math.Min(samples.Length - 1, samples.Length * pct / 100)];

        int p50 = Percentile(50), p95 = Percentile(95), p99 = Percentile(99), max = samples[samples.Length - 1];
        Console.WriteLine($"{name}: p50={p50}ms p95={p95}ms p99={p99}ms max={max}ms");
        return p95;
    }

    static void Host(string dataDir, int port, bool raw = false)
    {
        // 全程保持 cwd = dataDir：服务端相对路径（AccountPath 等）按调用时 cwd 解析，
        // boot 后还原 cwd 会导致存库写到别处（曾污染 repo 根目录）。进程靠 Environment.Exit 退出，无需还原。
        if (dataDir != null)
            Environment.CurrentDirectory = Path.GetFullPath(dataDir);

        Packet.IsServer = true;
        Settings.Load();
        Settings.Port = (ushort)port;
        if (!raw)
        {
            Settings.IPBlockSeconds = 0; // 压测同 IP 直连，禁用本地反滥用
            Settings.MaxIP = 10000;      // 压测同 IP 500 连接，绕过单 IP 连接上限（Setup.ini MaxIP=5）
            Settings.MaxUser = 10000;    // 压测 500 连接，绕过全局连接数上限（Setup.ini MaxUser=50）
        }

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
                return;
            }
            catch (SocketException)
            {
                Thread.Sleep(500);
            }
        }
        throw new TimeoutException("server did not open port within 180s");
    }

    static async Task RunConn(int port, CancellationToken ct, Action onConnected)
    {
        using var client = new TcpClient();
        client.NoDelay = true;
        await client.ConnectAsync("127.0.0.1", port, ct);
        onConnected();
        Interlocked.Increment(ref _live);
        try
        {
            await using var stream = client.GetStream();

            var bytes = new C.ClientVersion { VersionHash = Array.Empty<byte>() }.GetPacketBytes().ToArray();
            await stream.WriteAsync(bytes.AsMemory(), ct);

            var drain = DrainAsync(stream, ct);
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);
                var ka = new C.KeepAlive { Time = 0 }.GetPacketBytes().ToArray();
                await stream.WriteAsync(ka.AsMemory(), ct);
            }
            await drain;
        }
        finally
        {
            Interlocked.Decrement(ref _live);
        }
    }

    static async Task DrainAsync(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int n = await stream.ReadAsync(buf.AsMemory(), ct);
                if (n <= 0) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
