using System.Reflection;
using System.Text;
using System.Net.Sockets;
using Server.MirEnvir;

namespace Server.MirNetwork
{
    public class MirStatusConnection
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }
        protected static MessageQueue MessageQueue
        {
            get { return MessageQueue.Instance; }
        }

        public readonly string IPAddress;
        private TcpClient _client;

        private long NextSendTime;

        private bool _disconnecting;
        public bool Connected;
        public bool Disconnecting
        {
            get { return _disconnecting; }
            set
            {
                if (_disconnecting == value) return;
                _disconnecting = value;
                TimeOutTime = Envir.Time + 500;
            }
        }
        public readonly long TimeConnected;
        public long TimeOutTime;


        public MirStatusConnection(TcpClient client)
        {
            try
            {
                IPAddress = client.Client.RemoteEndPoint.ToString().Split(':')[0];

                _client = client;
                _client.NoDelay = true;

                TimeConnected = Envir.Time;
                TimeOutTime = TimeConnected + Settings.TimeOut;
                Connected = true;
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue(ex);
            }
        }

        private SocketAsyncEventArgs _sendArgs;

        private void BeginSend(byte[] data)
        {
            if (!Connected || data.Length == 0) return;

            try
            {
                if (_sendArgs == null)
                {
                    _sendArgs = new SocketAsyncEventArgs();
                    _sendArgs.Completed += SendData;
                }

                _sendArgs.SetBuffer(data, 0, data.Length);

                if (!_client.Client.SendAsync(_sendArgs))
                    SendData(this, _sendArgs);
            }
            catch
            {
                Disconnecting = true;
            }
        }
        private void SendData(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
                Disconnecting = true;
        }

        public void Process()
        {
            try
            {
                if (_client == null || !_client.Connected)
                {
                    Disconnect();
                    return;
                }

                if (Envir.Time > TimeOutTime || Disconnecting)
                {
                    Disconnect();
                    return;
                }


                if (Envir.Time > NextSendTime)
                {
                    NextSendTime = Envir.Time + 10000;
                    string output = string.Format("c;/NoName/{0}/CrystalM2/{1}//;", Envir.PlayerCount,
                                                  Assembly.GetCallingAssembly().GetName().Version);

                    BeginSend(Encoding.ASCII.GetBytes(output));
                }
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue(ex);
            }
        }
        public void Disconnect()
        {
            try
            {
                if (!Connected) return;

                Connected = false;

                lock (Envir.StatusConnections)
                    Envir.StatusConnections.Remove(this);

                if (_client != null) _client.Client.Dispose();
                _client = null;
            }
            catch (Exception ex)
            {
                MessageQueue.Enqueue(ex);
            }
        }
        public void SendDisconnect()
        {
            Disconnecting = true;
        }
    }
}
