
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
    public ushort port {get; private set;}
    public readonly KcpConfig _config;

    public KcpServer _server;
    public int ConnectionCount => _server.connections.Count;

    public Action<int> onConnected;
    public Action<int, ArraySegment<byte>, KcpChannel> onDataReceived;
    public Action<int> onDisconnected;
    public Action<int, ErrorCode, string> onError;
    public Action<int, Packet> onDataSent;

    private object _lock = new object();
    private Queue<PacketInfo> packetInfos;

    public KcpServerTransport(KcpConfig config, ushort port)
    {
        packetInfos = new Queue<PacketInfo>();

        this.port = port;
        this._config = config;
        _server = new KcpServer(
            (connectionId) => onConnected?.Invoke(connectionId),
            (connectionId, data, channel) => onDataReceived?.Invoke(connectionId, data, channel),
            (connectionId) => onDisconnected?.Invoke(connectionId),
            (connectionId, errorCode, error) => onError?.Invoke(connectionId, errorCode, error),
            this._config
        );
    }

    public override Uri Uri()
    {
        UriBuilder builder = new UriBuilder();
        builder.Scheme = nameof(KcpServerTransport);
        builder.Host = System.Net.Dns.GetHostName();
        builder.Port = port;
        return builder.Uri;
    }

    public override bool Active() => _server.IsActive();

    public override void Start() => _server.Start(port);

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
        if(packetInfos.Count <= 0) return;
        
        var packetInfo = packetInfos.Dequeue();
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
            onDataSent?.Invoke(packetInfo.ConnectionId, packetInfo.Packet);
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
            packetInfos.Enqueue(new PacketInfo(){ ConnectionId = (int)param, Packet = packet });
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[KCP] Exception ->\n{ex.Message}\n{ex.StackTrace}");
        }
    }

    public void SendMessage(pb.BattleMsgID battleMsgID, IMessage message, int connectionId)
    {
        var head = new Head(){ _cmd = (byte)battleMsgID, _length = message.CalculateSize() };
        var packet = new Packet(){ _data = message.ToByteArray(), _head = head };
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