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

        public static void Disconnect()
        {
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

            // keepalive 按真实时间驱动（Environment.TickCount），与模拟时间 CMain.Time 解耦：
            // batchmode 主循环被后台任务抢占时 Process() 调用会稀疏、模拟时间刻度失真，
            // 按 CMain.Time 判定会错过服务器 10s 超时窗口。真实时间判定保证只要
            // Process() 实际调用间隔小于服务器窗口，keepalive 即严格按时发送。
            int nowMs = Environment.TickCount;
            if (nowMs - _lastKaReal >= Settings.TimeOut && _sendList != null && _sendList.IsEmpty)
            {
                _sendList.Enqueue(new C.KeepAlive());
                KeepAlivesSent++;
                _lastKaReal = nowMs;
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
