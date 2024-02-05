using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;

public class NetTcpServer : NetServer
{
    private const int AcceptBufferMaxLength = 1024;
    
    private TcpListener? _tcpListener;
    private Thread? _listenerThread;
    private CancellationTokenSource? _cancellationTokenSource;
    private byte[]? _acceptBuffer;

    public delegate void OnProcessTcpAcceptDataDelegate(byte[] message, int bytesRead, NetworkStream clientStream);
    public event OnProcessTcpAcceptDataDelegate? OnProcessTcpAcceptData;

    public override void Initialize()
    {
        _acceptBuffer = new byte[AcceptBufferMaxLength];
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public void StartServer(string address, int port)
    {
        var ipAddress = IPAddress.Parse(address);
        _tcpListener = new TcpListener(ipAddress, port);

        _listenerThread = new Thread(new ThreadStart(OnThreadStart));
        _listenerThread.Start();

        Logger.Log(LogLevel.Info, "[TCP] Server started on " + ipAddress + ":" + port);
    }

    private void OnThreadStart()
    {
        _tcpListener?.Start();
        while (true)
        {
            // 等待客户端连接
            var client = _tcpListener?.AcceptTcpClient();
            Logger.Log(LogLevel.Info, $"[TCP] Client is connected! RemoteEndPoint:{client?.Client.RemoteEndPoint}");
            // 创建新线程处理客户端请求
            var clientThread = new Thread(new ParameterizedThreadStart(OnParameterizedThreadStart));
            clientThread.Start(client);
        }
    }

    private async void OnParameterizedThreadStart(object? n)
    {
        if (n is not TcpClient tcpClient) return;

        var clientStream = tcpClient.GetStream();
        while (true)
        {
            var bytesRead = 0;

            try
            {
                bytesRead = await clientStream.ReadAsync(_acceptBuffer!, 0, AcceptBufferMaxLength, _cancellationTokenSource!.Token);
            }
            catch(Exception ex)
            {
                Logger.Log(LogLevel.Exception, ex.Message);
                tcpClient.Close();
                break;
            }

            if (bytesRead == 0)
                break;

            OnProcessTcpAcceptData?.Invoke(_acceptBuffer!, bytesRead, clientStream);
        }

        tcpClient.Close();
    }
    
    private async void Send(Packet packet, Stream networkStream)
    {
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