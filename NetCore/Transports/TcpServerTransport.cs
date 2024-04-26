using System.Net;
using System.Net.Sockets;
using Google.Protobuf;

public class TcpServerTransport : ServerTransport
{
    public string address {get; private set;}
    public ushort port {get; private set;}
    public Action<byte[], int, NetworkStream> onDataReceived;
    public Action<NetworkStream, Packet> onDataSent;

    private List<Task> handleClientTasks;
    private TcpListener tcpListener;
    private int acceptBufferMaxLength;
    private byte[] acceptBuffer;

    public TcpServerTransport(string address, ushort port, int acceptBufferMaxLength = 1024)
    {
        this.address = address;
        this.port = port;
        this.acceptBufferMaxLength = acceptBufferMaxLength;
        this.acceptBuffer = new byte[this.acceptBufferMaxLength];

        handleClientTasks = new List<Task>();
    }

    public override Uri Uri()
    {
        var builder = new UriBuilder();
        builder.Scheme = nameof(KcpServerTransport);
        builder.Host = System.Net.Dns.GetHostName();
        builder.Port = port;
        return builder.Uri;
    }

    public override bool Active() => tcpListener.Server.IsBound;

    public override void Start()
    {
        var ipAddress = IPAddress.Parse(address);
        tcpListener = new TcpListener(ipAddress, port);
        ListenForClientsAsync();
    }

    private async void ListenForClientsAsync()
    {
        try
        {
            tcpListener.Start();
            while (true)
            {
                var client = await tcpListener.AcceptTcpClientAsync();
                System.Console.WriteLine($"[TCP] AcceptTcpClientAsync -> RemoteEndPoint: {client.Client.RemoteEndPoint}");
                handleClientTasks.Add(Task.Run(()=>HandleClientAsync(client)));
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[TCP] Exception ->\n{ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            if(handleClientTasks.Count > 0)
            {
                foreach(var task in handleClientTasks) task.Dispose();
                handleClientTasks.Clear();
            }
            tcpListener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            while (true)
            {
                var read = await stream.ReadAsync(acceptBuffer!, 0, acceptBufferMaxLength);
                if (read == 0) break;
                onDataReceived?.Invoke(acceptBuffer, read, stream);
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
            System.Console.WriteLine($"[KCP] Send -> MsgID:{Enum.GetName(typeof(pb.LogicMsgID), packet._head._cmd)} dataSize:{packet._head._length}");
            onDataSent?.Invoke(stream, packet);
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