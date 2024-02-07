using Google.Protobuf;
using kcp2k;

/// <summary>
/// KCP服务器
/// </summary>
public class NetKcpServer : NetServer
{
    /// <summary>
    /// KCP服务器配置数据
    /// </summary>
    private KcpConfig? _kcpConfig;
    /// <summary>
    /// KCP服务器对象
    /// </summary>
    private KcpServer? _kcpServer;
    /// <summary>
    /// KCP轮询线程任务
    /// </summary>
    private Task? _kcpTickThread;
    /// <summary>
    /// KCP轮询线程任务取消操作句柄
    /// </summary>
    private CancellationTokenSource? _kcpTickCancellationTokenSource;

    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize()
    {
        _kcpConfig = new KcpConfig(
            NoDelay: true,
            DualMode: false,
            Interval: 1,
            Timeout: 2000,
            SendWindowSize: Kcp.WND_SND * 1000,
            ReceiveWindowSize: Kcp.WND_RCV * 1000,
            CongestionWindow: false,
            MaxRetransmits: Kcp.DEADLINK * 2
        );
        _kcpServer = new KcpServer(
            NetworkManager.Instance.OnKcpConnected, 
            NetworkManager.Instance.OnProcessNetKcpAcceptData, 
            NetworkManager.Instance.OnKcpDisconnected, 
            NetworkManager.Instance.OnKcpError, 
            _kcpConfig);
        _kcpTickCancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// 释放
    /// </summary>
    public override void OnRelease()
    {
        if (_kcpTickCancellationTokenSource != null) _kcpTickCancellationTokenSource.Cancel();
        if (_kcpTickThread != null) _kcpTickThread.Dispose();
        if (_kcpServer != null) _kcpServer.Stop();
    }

    /// <summary>
    /// 开始KCP服务器轮询
    /// </summary>
    public override void StartServer()
    {
        if(_kcpServer == null || _kcpConfig == null)
            return;
        
        _kcpServer.Start(NetConstant.KcpPort);
        var kcpTickCancellationToken = _kcpTickCancellationTokenSource!.Token;
        _kcpTickThread = Task.Run(async () =>
        {
            try
            {
                while (_kcpServer.IsActive() && !kcpTickCancellationToken.IsCancellationRequested)
                {
                    await KcpTick(kcpTickCancellationToken);
                    await Task.Delay((int)_kcpConfig.Interval, kcpTickCancellationToken);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Exception, ex.StackTrace);
                _kcpTickCancellationTokenSource.Cancel();
            }
        }, kcpTickCancellationToken);
    }

    /// <summary>
    /// KCP轮询
    /// </summary>
    /// <param name="cancellationToken">轮询取消操作句柄</param>
    /// <returns>异步任务</returns>
    private Task KcpTick(CancellationToken cancellationToken)
    {
        _kcpServer?.Tick();
        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }

    /// <summary>
    /// 关闭KCP服务器
    /// </summary>
    public void CloseKcpServer()
    {
        if(_kcpServer == null || !_kcpServer.IsActive()) 
            return;
        _kcpTickCancellationTokenSource?.Cancel();
        _kcpTickThread?.Dispose();
        _kcpServer.Stop();
    }
    
    /// <summary>
    /// 发送数据
    /// </summary>
    /// <param name="connectionId">客户端连接ID</param>
    /// <param name="packet">数据包</param>
    private void Send(int connectionId, Packet packet)
    {
        Logger.Log(LogLevel.Info, $"[KCP] Send connectionId:{connectionId} MsgID:{Enum.GetName(typeof(pb.BattleMsgID), packet._head._cmd)} dataSize:{packet._head._length}");
        if (_kcpServer == null || !_kcpServer!.IsActive()) 
            return;

        var buffer = BufferPool.GetBuffer(packet._head._length + Head.HeadLength);
        try
        {
            unsafe
            {
                fixed (byte* src = buffer) *((Head*)src) = packet._head;
            }

            Array.Copy(packet._data, 0, buffer, Head.HeadLength, packet._head._length);
            _kcpServer.Send(connectionId, new ArraySegment<byte>(buffer), KcpChannel.Unreliable);
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Exception, ex.Message);
            _kcpServer.Stop();
        }
        finally
        {
            BufferPool.ReleaseBuff(buffer);
        }
    }

    /// <summary>
    /// 发送KCP消息
    /// </summary>
    /// <param name="connectionId">客户端连接ID</param>
    /// <param name="battleMsgId">消息ID</param>
    /// <param name="msg">消息体</param>
    public void SendKcpMsg(int connectionId, pb.BattleMsgID battleMsgId, IMessage msg)
    {
        Send(connectionId, new Packet
        {
            _head = new Head
            {
                _cmd = (byte)battleMsgId,
                _length = msg.CalculateSize()
            },
            _data = msg.ToByteArray()
        });
    }

    /// <summary>
    /// 断开某个客户端的连接
    /// </summary>
    /// <param name="connectionId">客户端连接ID</param>
    public void DisconnectClient(int connectionId)
    {
        if(_kcpServer == null) 
            return;
        _kcpServer.Disconnect(connectionId);
    }
}