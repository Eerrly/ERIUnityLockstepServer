using System.Net;
using Google.Protobuf;
using kcp2k;

public struct PacketInfo
{
    public int ConnectionId;

    public Packet Packet;
}

public class KcpServerTransport : ServerTransport
{
    private readonly KcpServer _server;
    private readonly ushort _port;
    private readonly KcpConfig _config;
    private readonly Queue<PacketInfo> _packetInfos;
    
    public int ConnectionCount => _server.connections.Count;
    public Action<int> OnConnected;
    public Action<int, ArraySegment<byte>, KcpChannel> OnDataReceived;
    public Action<int> OnDisconnected;
    public Action<int, ErrorCode, string> OnError;
    public Action<int, Packet> OnDataSent;
    
    public KcpServerTransport(KcpConfig config, ushort port)
    {
        _packetInfos = new Queue<PacketInfo>();

        this._port = port;
        this._config = config;
        _server = new KcpServer(
            (connectionId) => OnConnected?.Invoke(connectionId),
            (connectionId, data, channel) => OnDataReceived?.Invoke(connectionId, data, channel),
            (connectionId) => OnDisconnected?.Invoke(connectionId),
            (connectionId, errorCode, error) => OnError?.Invoke(connectionId, errorCode, error),
            this._config
        );
    }

    public override Uri Uri()
    {
        var builder = new UriBuilder();
        builder.Scheme = nameof(KcpServerTransport);
        builder.Host = System.Net.Dns.GetHostName();
        builder.Port = _port;
        return builder.Uri;
    }

    public override bool Active() => _server.IsActive();

    public override void Start() => _server.Start(_port);

    public override void Update()
    {
        Task.Run(async () => {
            while(true){
                UpdatePacketInfosSent();
                _server.Tick();
                await Task.Delay(TimeSpan.FromMilliseconds(_config.Interval));
            }
        });
    }

    private void UpdatePacketInfosSent()
    {
        if(_packetInfos.Count <= 0) return;
        
        var packetInfo = _packetInfos.Dequeue();
        var buffer = BufferPool.GetBuffer(packetInfo.Packet._head._length + Head.HeadLength);
        try
        {
            unsafe
            {
                fixed (byte* src = buffer) *((Head*)src) = packetInfo.Packet._head;
            }
            Array.Copy(packetInfo.Packet._data, 0, buffer, Head.HeadLength, packetInfo.Packet._head._length);
            _server.Send(packetInfo.ConnectionId, new ArraySegment<byte>(buffer), KcpChannel.Unreliable);

            BufferPool.ReleaseBuff(buffer);
            System.Console.WriteLine($"[KCP] Send -> connectionId:{packetInfo.ConnectionId} MsgID:{Enum.GetName(typeof(pb.BattleMsgID), packetInfo.Packet._head._cmd)} dataSize:{packetInfo.Packet._head._length}");
            OnDataSent?.Invoke(packetInfo.ConnectionId, packetInfo.Packet);
        }
        catch(Exception ex)
        {
            System.Console.WriteLine($"[KCP] Exception ->\n{ex.Message}\n{ex.StackTrace}");
            Shutdown();
        }
        finally
        {
            BufferPool.ReleaseBuff(buffer);
        }
    }

    public override void Send(Packet packet, object param)
    {
        try
        {
            _packetInfos.Enqueue(new PacketInfo(){ ConnectionId = (int)param, Packet = packet });
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[KCP] Exception ->\n{ex.Message}\n{ex.StackTrace}");
        }
    }

    public void SendMessage<T>(pb.BattleMsgID battleMsgId, T message, int connectionId) where T : IMessage
    {
        if (!Active()) return;
        var head = new Head(){ _cmd = (byte)battleMsgId, _length = message.CalculateSize() };
        var packet = new Packet(){ _data = message.ToByteArray(), _head = head };
        MsgPoolManager.Instance.Release(message);
        Send(packet, connectionId);
    }

    public override void Disconnect(int connectionId) => _server.Disconnect(connectionId);

    public override string GetClientAddress(int connectionId)
    {
        IPEndPoint endPoint = _server.GetClientEndPoint(connectionId);
        if(endPoint != null)
        {
            if(endPoint.Address.IsIPv4MappedToIPv6){
                return endPoint.Address.MapToIPv4().ToString();
            }
            return endPoint.Address.ToString();
        }
        return "";
    }

    public override void Shutdown() => _server.Stop();

}