using System.Net;
using System.Net.Sockets;
using Google.Protobuf;

/// <summary>
/// 接受到消息的回调委托
/// </summary>
public delegate void OnProcessTcpAcceptDataDelegate(byte[] message, int bytesRead, NetworkStream clientStream);

/// <summary>
/// TCP服务器
/// </summary>
public class NetTcpServer : NetServer
{
    /// <summary>
    /// 最大接受消息数组长度
    /// </summary>
    private const int AcceptBufferMaxLength = 1024;
    /// <summary>
    /// TCP监听对象
    /// </summary>
    private TcpListener? _tcpListener;
    /// <summary>
    /// TCP取消操作句柄
    /// </summary>
    private CancellationTokenSource? _cancellationTokenSource;
    /// <summary>
    /// 客户端的请求Task集合
    /// </summary>
    private List<Task> _handleClientTasks;
    /// <summary>
    /// 接受数据数组
    /// </summary>
    private byte[]? _acceptBuffer;
    /// <summary>
    /// 接受到消息的回调事件
    /// </summary>
    public event OnProcessTcpAcceptDataDelegate? OnProcessTcpAcceptData;

    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize()
    {
        _acceptBuffer = new byte[AcceptBufferMaxLength];
        _handleClientTasks = new List<Task>();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// 释放
    /// </summary>
    public override void OnRelease()
    {
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
        }
        if (_handleClientTasks.Count > 0)
        {
            _handleClientTasks.Clear();
        }
        if (_acceptBuffer != null)
        {
            Array.Clear(_acceptBuffer, 0, _acceptBuffer.Length);
        }
        if (_tcpListener != null)
        {
            _tcpListener.Stop();
        }
    }

    /// <summary>
    /// 开启服务器
    /// </summary>
    public override void StartServer()
    {
        var ipAddress = IPAddress.Parse(NetConstant.TcpAddress);
        _tcpListener = new TcpListener(ipAddress, NetConstant.TcpPort);

        ListenForClientsAsync();

        Logger.Log(LogLevel.Info, "[TCP] Server started on " + ipAddress + ":" + NetConstant.TcpPort);
    }

    /// <summary>
    /// 开启监听客户端线程
    /// </summary>
    private async void ListenForClientsAsync()
    {
        if(_tcpListener == null || _cancellationTokenSource == null) return;

        var tcpListenerCancellationToken = _cancellationTokenSource.Token;
        try
        {
            _tcpListener.Start();
            while (true)
            {
                var client = await _tcpListener.AcceptTcpClientAsync(tcpListenerCancellationToken);
                Logger.Log(LogLevel.Info, $"[TCP] Client is connected! RemoteEndPoint:{client?.Client.RemoteEndPoint}");

                if (client != null) _handleClientTasks.Add(Task.Run(() => HandleClientAsync(client), tcpListenerCancellationToken));
            }
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Exception, ex.Message + ex.StackTrace);
        }
        finally
        {
            if (_handleClientTasks.Count > 0)
            {
                foreach (var t in _handleClientTasks)
                    t.Dispose();
                _handleClientTasks.Clear();
            }
        }
    }

    /// <summary>
    /// 客户端连接新线程处理
    /// </summary>
    /// <param name="client">TCP客户端</param>
    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            var clientStream = client.GetStream();
            while (true)
            {
                var bytesRead = await clientStream.ReadAsync(_acceptBuffer!, 0, AcceptBufferMaxLength, _cancellationTokenSource!.Token);

                if (bytesRead == 0)
                    break;

                OnProcessTcpAcceptData?.Invoke(_acceptBuffer!, bytesRead, clientStream);
            }
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Exception, ex.Message + ex.StackTrace);
        }
        finally
        {
            client.Close();
        }
    }
    
    /// <summary>
    /// TCP发送数据
    /// </summary>
    /// <param name="packet">数据包</param>
    /// <param name="networkStream">客户端流</param>
    private async void Send(Packet packet, Stream networkStream)
    {
        Logger.Log(LogLevel.Info, $"[TCP] Send MsgID:{Enum.GetName(typeof(pb.LogicMsgID), packet._head._cmd)} dataSize:{packet._head._length}");
        var buffer = BufferPool.GetBuffer(packet._head._length + Head.HeadLength);
        try
        {
            unsafe
            {
                fixed (byte* src = buffer) *((Head*)src) = packet._head;
            }

            Array.Copy(packet._data, 0, buffer, Head.HeadLength, packet._head._length);
            await networkStream.WriteAsync(buffer.AsMemory(0, buffer.Length), _cancellationTokenSource!.Token);
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Exception, ex.Message);
        }
        finally
        {
            BufferPool.ReleaseBuff(buffer);
        }
    }
    
    /// <summary>
    /// TCP发送消息
    /// </summary>
    /// <param name="logicMsgId">消息ID</param>
    /// <param name="message">消息</param>
    /// <param name="stream">客户端流</param>
    public void SendTcpMsg(pb.LogicMsgID logicMsgId, IMessage message, NetworkStream stream)
    {
        Send(new Packet
        {
            _head = new Head
            {
                _cmd = (byte)logicMsgId,
                _length = message.CalculateSize()
            },
            _data = message.ToByteArray()
        }, stream);
    }
}