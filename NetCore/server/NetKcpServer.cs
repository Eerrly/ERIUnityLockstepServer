using Google.Protobuf;
using kcp2k;

public class NetKcpServer : NetServer
{
    private KcpConfig? _kcpConfig;
    private KcpServer? _kcpServer;
    private Task? _kcpTickThread;
    private CancellationTokenSource? _kcpTickCancellationTokenSource;

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
    }

    public void StartServerTick()
    {
        _kcpServer!.Start(NetConstant.KcpPort);
        
        _kcpTickCancellationTokenSource = new CancellationTokenSource();
        var kcpTickCancellationToken = _kcpTickCancellationTokenSource.Token;
        _kcpTickThread = Task.Run(async () =>
        {
            while (_kcpServer!.IsActive() && !kcpTickCancellationToken.IsCancellationRequested)
            {
                await KcpTick(kcpTickCancellationToken);
                await Task.Delay((int)_kcpConfig!.Interval, kcpTickCancellationToken);
            }
        }, kcpTickCancellationToken);
    }

    private Task KcpTick(CancellationToken cancellationToken)
    {
        _kcpServer?.Tick();
        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }

    public void CloseKcpServer()
    {
        if(_kcpServer == null || !_kcpServer.IsActive()) 
            return;
        _kcpTickCancellationTokenSource?.Cancel();
        _kcpTickThread?.Dispose();
        _kcpServer?.Stop();
    }
    
    private void Send(int connectionId, Packet packet)
    {
        Logger.Log(LogLevel.Info, $"[KCP] Send connectionId:{connectionId} dataSize:{packet._head._length}");
        if (!_kcpServer!.IsActive()) return;

        var buffer = BufferPool.GetBuffer(packet._head._length + Head.HeadLength);
        try
        {
            unsafe
            {
                fixed (byte* src = buffer) *((Head*)src) = packet._head;
            }

            Array.Copy(packet._data, 0, buffer, Head.HeadLength, packet._head._length);
            _kcpServer?.Send(connectionId, new ArraySegment<byte>(buffer), KcpChannel.Unreliable);
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Exception, ex.Message);
            _kcpServer?.Stop();
        }
        finally
        {
            BufferPool.ReleaseBuff(buffer);
        }
    }

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

    public void DisconnectClient(int connectionId)
    {
        _kcpServer?.Disconnect(connectionId);
    }
}