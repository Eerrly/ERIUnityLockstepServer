using System.Net;
using System.Net.Sockets;
using Google.Protobuf;

public class TcpServerTransport : ServerTransport
{
    private TcpListener _tcpListener;

    private readonly string _address;
    private readonly ushort _port;
    private readonly List<Task> _handleClientTasks;
    private readonly int _acceptBufferMaxLength;
    private readonly byte[] _acceptBuffer;
    
    public Action<byte[], int, NetworkStream> OnDataReceived;
    public Action<NetworkStream, Packet> OnDataSent;

    public TcpServerTransport(string address, ushort port, int acceptBufferMaxLength = 1024)
    {
        this._address = address;
        this._port = port;
        this._acceptBufferMaxLength = acceptBufferMaxLength;
        this._acceptBuffer = new byte[this._acceptBufferMaxLength];

        _handleClientTasks = new List<Task>();
    }

    public override Uri Uri()
    {
        var builder = new UriBuilder();
        builder.Scheme = nameof(KcpServerTransport);
        builder.Host = System.Net.Dns.GetHostName();
        builder.Port = _port;
        return builder.Uri;
    }

    public override bool Active() => _tcpListener.Server.IsBound;

    public override void Start()
    {
        var ipAddress = IPAddress.Parse(_address);
        _tcpListener = new TcpListener(ipAddress, _port);
        ListenForClientsAsync();
    }

    private async void ListenForClientsAsync()
    {
        try
        {
            _tcpListener.Start();
            while (true)
            {
                var client = await _tcpListener.AcceptTcpClientAsync();
                System.Console.WriteLine($"[TCP] AcceptTcpClientAsync -> RemoteEndPoint: {client.Client.RemoteEndPoint}");
                _handleClientTasks.Add(Task.Run(()=>HandleClientAsync(client)));
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[TCP] Exception ->\n{ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            if(_handleClientTasks.Count > 0)
            {
                foreach(var task in _handleClientTasks) task.Dispose();
                _handleClientTasks.Clear();
            }
            _tcpListener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            while (true)
            {
                var read = await stream.ReadAsync(_acceptBuffer!, 0, _acceptBufferMaxLength);
                if (read == 0) break;
                OnDataReceived?.Invoke(_acceptBuffer, read, stream);
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[TCP] Exception ->\n{ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            client.Close();
        }
    }

    public override void Send(Packet packet, object param)
    {
        SendAsync(packet, (NetworkStream)param);
    }

    private async void SendAsync(Packet packet, NetworkStream stream)
    {
        var buffer = BufferPool.GetBuffer(packet._head._length + Head.HeadLength);
        try
        {
            unsafe
            {
                fixed (byte* src = buffer) *((Head*)src) = packet._head;
            }
            Array.Copy(packet._data, 0, buffer, Head.HeadLength, packet._head._length);
            await stream.WriteAsync(buffer.AsMemory(0, buffer.Length));

            BufferPool.ReleaseBuff(buffer);
            System.Console.WriteLine($"[TCP] Send -> MsgID:{Enum.GetName(typeof(pb.LogicMsgID), packet._head._cmd)} dataSize:{packet._head._length}");
            OnDataSent?.Invoke(stream, packet);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[TCP] Exception ->\n{ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            BufferPool.ReleaseBuff(buffer);
        }
    }

    public void SendMessage<T>(pb.LogicMsgID logicMsgId, T message, NetworkStream stream) where T: IMessage
    {
        var head = new Head(){ _cmd = (byte)logicMsgId, _length = message.CalculateSize() };
        var packet = new Packet() { _data = message.ToByteArray(), _head = head };
        MsgPoolManager.Instance.Release(message);
        Send(packet, stream);
    }

}