
using System.Net;
using Google.Protobuf;
using kcp2k;

/// <summary>
/// KCP消息信息包
/// </summary>
public struct PacketInfo
{
    public int ConnectionId;

    public Packet Packet;
}

/// <summary>
/// KCP服务器支持类
/// </summary>
public class KcpServerTransport : ServerTransport
{
    /// <summary>
    /// 端口号
    /// </summary>
    public ushort port {get; private set;}
    /// <summary>
    /// KCP配置
    /// </summary>
    public readonly KcpConfig _config;
    /// <summary>
    /// KCP服务器对象
    /// </summary>
    public KcpServer _server;
    /// <summary>
    /// 客户端连接数量
    /// </summary>
    public int ConnectionCount => _server.connections.Count;
    /// <summary>
    /// 客户端连接时回调
    /// </summary>
    public Action<int> OnConnected;
    /// <summary>
    /// 收到客户端数据时回调
    /// </summary>
    public Action<int, ArraySegment<byte>, KcpChannel> OnDataReceived;
    /// <summary>
    /// 客户端断开连接时回调
    /// </summary>
    public Action<int> OnDisconnected;
    /// <summary>
    /// 客户端发生错误时回调
    /// </summary>
    public Action<int, ErrorCode, string> OnError;
    /// <summary>
    /// 服务器发送数据后回调
    /// </summary>
    public Action<int, Packet> OnDataSent;
    /// <summary>
    /// 锁
    /// </summary>
    private object _lock = new object();
    /// <summary>
    /// 预发送的KCP信息包队列
    /// </summary>
    private Queue<PacketInfo> packetInfos;

    /// <summary>
    /// KCP服务器支持类构造函数
    /// </summary>
    /// <param name="config">KCP配置</param>
    /// <param name="port">端口号</param>
    public KcpServerTransport(KcpConfig config, ushort port)
    {
        packetInfos = new Queue<PacketInfo>();

        this.port = port;
        this._config = config;
        _server = new KcpServer(
            (connectionId) => OnConnected?.Invoke(connectionId),
            (connectionId, data, channel) => OnDataReceived?.Invoke(connectionId, data, channel),
            (connectionId) => OnDisconnected?.Invoke(connectionId),
            (connectionId, errorCode, error) => OnError?.Invoke(connectionId, errorCode, error),
            this._config
        );
    }

    /// <summary>
    /// 获取服务器URI
    /// </summary>
    /// <returns>URI</returns>
    public override Uri Uri()
    {
        var builder = new UriBuilder();
        builder.Scheme = nameof(KcpServerTransport);
        builder.Host = System.Net.Dns.GetHostName();
        builder.Port = port;
        return builder.Uri;
    }

    /// <summary>
    /// 服务器是否处于激活状态
    /// </summary>
    /// <returns>是否处于激活状态</returns>
    public override bool Active() => _server.IsActive();

    /// <summary>
    /// 服务器开启
    /// </summary>
    public override void Start() => _server.Start(port);

    /// <summary>
    /// 服务器轮询
    /// </summary>
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

    /// <summary>
    /// 处理需要发送给客户端的消息
    /// </summary>
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

    /// <summary>
    /// 服务器发送消息
    /// </summary>
    /// <param name="packet">消息包</param>
    /// <param name="param">附加参数（KCP客户端连接ID）</param>
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

    /// <summary>
    /// 服务器发送消息
    /// </summary>
    /// <param name="battleMsgId">消息ID</param>
    /// <param name="message">消息对象</param>
    /// <param name="connectionId">KCP客户端连接ID</param>
    /// <typeparam name="T">消息类型</typeparam>
    public void SendMessage<T>(pb.BattleMsgID battleMsgId, T message, int connectionId) where T : IMessage
    {
        var head = new Head(){ _cmd = (byte)battleMsgId, _length = message.CalculateSize() };
        var packet = new Packet(){ _data = message.ToByteArray(), _head = head };
        MsgPoolManager.Instance.Release(message);
        Send(packet, connectionId);
    }

    /// <summary>
    /// 通过连接ID断开某一个客户端(KCP)
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    public override void Disconnect(int connectionId) => _server.Disconnect(connectionId);

    /// <summary>
    /// 通过连接ID来获取某一个客户端的地址(KCP)
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    /// <returns>客户端地址</returns>
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

    /// <summary>
    /// 服务器断开
    /// </summary>
    public override void Shutdown() => _server.Stop();

}