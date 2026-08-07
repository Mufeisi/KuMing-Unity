using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using C = ClientPackets;

namespace Client.MirNetwork
{
    // Client.Core 的 Network seam：逐字移植旧客户端传输骨架（Client/MirNetwork/Network.cs）。
    // TcpClient NoDelay + BeginConnect/BeginReceive 异步收发 + Shared Packet.ReceivePacket 解码入队。
    // 去 WinForms：封包派发 MirScene.ActiveScene.ProcessPacket → 静态 OnPacket 委托（探针/生产注入）；
    // MirMessageBox/Program.Form → CMain.Log（渲染层注入 LogImpl=Debug.Log 后即还原 Unity 日志）。Connected=“已握手”（收到 S.Connected），非“TCP 已连”。
    public static class Network
    {
        private static TcpClient _client;
        public static int ConnectAttempt = 0;
        public static int MaxAttempts = 20;
        public static bool ErrorShown;
        public static bool Connected;
        public static long TimeOutTime, TimeConnected, RetryTime = CMain.Time + 5000;
        public static long KeepAlivesSent;

        private static int _lastKaReal;
        private static Timer _kaTimer;      // keepalive 独立心跳：主循环帧率极低（模拟器 0.2fps）时仍严格按时发送
        private static int _lastKaLogReal;  // keepalive 状态日志节流（真实时间，防 ThreadPool 线程打 Unity 日志）

        public static Action<Packet> OnPacket;

        private static ConcurrentQueue<Packet> _receiveList;
        private static ConcurrentQueue<Packet> _sendList;

        static byte[] _rawData = new byte[0];
        static readonly byte[] _rawBytes = new byte[8 * 1024];

        public static void Connect()
        {
            if (_client != null)
                Disconnect();

            if (ConnectAttempt >= MaxAttempts)
            {
                if (ErrorShown) return;
                ErrorShown = true;
                CMain.Log("network: max connection attempts reached");
                return;
            }

            ConnectAttempt++;

            try
            {
                _client = new TcpClient { NoDelay = true };
                _client?.BeginConnect(Settings.IPAddress, Settings.Port, Connection, null);
            }
            catch (ObjectDisposedException ex)
            {
                if (Settings.LogErrors) CMain.SaveError(ex.ToString());
                Disconnect();
            }
        }

        private static void Connection(IAsyncResult result)
        {
            try
            {
                _client?.EndConnect(result);

                if ((_client != null &&
                    !_client.Connected) ||
                    _client == null)
                {
                    Connect();
                    return;
                }

                _receiveList = new ConcurrentQueue<Packet>();
                _sendList = new ConcurrentQueue<Packet>();
                _rawData = new byte[0];

                TimeOutTime = CMain.Time + Settings.TimeOut;
                TimeConnected = CMain.Time;

                // keepalive 心跳与主循环解耦：Unity 模拟器 swiftshader 帧率可低至 0.2fps，Process() 调用稀疏，
                // 若 keepalive 由主循环驱动，发送间隔波动会超过服务器 10s 超时窗口导致连接被踢（moved=False 根因）。
                // 独立 Timer 严格按真实时间发送，且仅 TCP 已连时发送（不碰 Unity API，ThreadPool 线程安全）。
                _kaTimer?.Dispose();
                _kaTimer = new Timer(_ => SendKeepAlive(), null, Settings.TimeOut, Settings.TimeOut);

                BeginReceive();
            }
            catch (SocketException)
            {
                Thread.Sleep(100);
                Connect();
            }
            catch (Exception ex)
            {
                if (Settings.LogErrors) CMain.SaveError(ex.ToString());
                Disconnect();
            }
        }

        private static void BeginReceive()
        {
            if (_client == null || !_client.Connected) return;

            try
            {
                _client.Client.BeginReceive(_rawBytes, 0, _rawBytes.Length, SocketFlags.None, ReceiveData, _rawBytes);
            }
            catch
            {
                Disconnect();
            }
        }
        private static void ReceiveData(IAsyncResult result)
        {
            if (_client == null || !_client.Connected) return;

            int dataRead;

            try
            {
                dataRead = _client.Client.EndReceive(result);
            }
            catch
            {
                Disconnect();
                return;
            }

            if (dataRead == 0)
            {
                Disconnect();
            }

            byte[] rawBytes = result.AsyncState as byte[];

            byte[] temp = _rawData;
            _rawData = new byte[dataRead + temp.Length];
            Buffer.BlockCopy(temp, 0, _rawData, 0, temp.Length);
            Buffer.BlockCopy(rawBytes, 0, _rawData, temp.Length, dataRead);

            Packet p;
            List<byte> data = new List<byte>();

            while ((p = Packet.ReceivePacket(_rawData, out _rawData)) != null)
            {
                data.AddRange(p.GetPacketBytes());
                if (_receiveList != null)
                    _receiveList.Enqueue(p);
            }

            CMain.BytesReceived += data.Count;

            BeginReceive();
        }

        private static void BeginSend(List<byte> data)
        {
            if (_client == null || !_client.Connected || data.Count == 0) return;

            try
            {
                _client.Client.BeginSend(data.ToArray(), 0, data.Count, SocketFlags.None, SendData, null);
            }
            catch
            {
                Disconnect();
            }
        }
        private static void SendData(IAsyncResult result)
        {
            try
            {
                _client.Client.EndSend(result);
            }
            catch
            { }
        }

        // keepalive 心跳：ThreadPool 线程执行（不访问 Unity API）。仅在 TCP 已连时发送；
        // 握手前也发（服务器任意包均重置超时），与旧主循环行为一致。发送失败静默——
        // 连接异常由主循环 Process() 的断开检测/重连逻辑接管。
        private static void SendKeepAlive()
        {
            try
            {
                var c = _client;
                if (c == null || !c.Connected) return;
                byte[] data = new C.KeepAlive().GetPacketBytes().ToArray();
                c.Client.BeginSend(data, 0, data.Length, SocketFlags.None, SendData, null);
                KeepAlivesSent++;
                CMain.BytesSent += data.Length;
                _lastKaReal = Environment.TickCount;
            }
            catch
            { }
        }

        public static void Disconnect()
        {
            _kaTimer?.Dispose();
            _kaTimer = null;
            if (_client == null) return;

            _client?.Close();

            TimeConnected = 0;
            Connected = false;
            _sendList = null;
            _client = null;

            _receiveList = null;
        }

        public static void Process()
        {
            if (_client == null || !_client.Connected)
            {
                if (Connected)
                {
                    while (_receiveList != null && !_receiveList.IsEmpty)
                    {
                        if (!_receiveList.TryDequeue(out Packet p) || p == null) continue;
                        if (!(p is ServerPackets.Disconnect) && !(p is ServerPackets.ClientVersion)) continue;

                        OnPacket?.Invoke(p);
                        _receiveList = null;
                        return;
                    }

                    CMain.Log("network: lost connection with server");
                    Disconnect();
                    return;
                }
                else if (CMain.Time >= RetryTime)
                {
                    RetryTime = CMain.Time + 5000;
                    Connect();
                }
                return;
            }

            if (!Connected && TimeConnected > 0 && CMain.Time > TimeConnected + 5000)
            {
                Disconnect();
                Connect();
                return;
            }

            while (_receiveList != null && !_receiveList.IsEmpty)
            {
                if (!_receiveList.TryDequeue(out Packet p) || p == null) continue;
                OnPacket?.Invoke(p);
            }

            // keepalive 由独立 Timer 驱动（见 SendKeepAlive），主循环仅做状态日志（真实时间节流，
            // 帧率低时 Process() 调用稀疏也能在 60s 窗口内至少打印一次，供 androidverify 断连诊断）。
            int nowMs = Environment.TickCount;
            if (nowMs - _lastKaLogReal >= 60000)
            {
                _lastKaLogReal = nowMs;
                CMain.Log($"[network] keepalive heartbeat ka={KeepAlivesSent} lastMs={_lastKaReal} sendList={(_sendList == null ? 0 : _sendList.Count)}");
            }

            if (_sendList == null || _sendList.IsEmpty) return;

            TimeOutTime = CMain.Time + Settings.TimeOut;

            List<byte> data = new List<byte>();
            while (!_sendList.IsEmpty)
            {
                if (!_sendList.TryDequeue(out Packet p)) continue;
                data.AddRange(p.GetPacketBytes());
            }

            CMain.BytesSent += data.Count;

            BeginSend(data);
        }

        public static void Enqueue(Packet p)
        {
            if (_sendList != null && p != null)
                _sendList.Enqueue(p);
        }
    }
}
