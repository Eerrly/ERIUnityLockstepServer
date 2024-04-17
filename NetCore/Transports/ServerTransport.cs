using kcp2k;

public abstract class ServerTransport
{
    public virtual Uri Uri(){ return null; }

    public virtual bool Active() { return false; }

    public virtual void Start() { }

    public virtual void Update() { }

    public virtual void Send(Packet packet, object param) { }

    public virtual void Disconnect(int connectionId) { }

    public virtual string GetClientAddress(int connectionId) { return string.Empty; }

    public virtual void Shutdown() { }

    public void OnMessageProcess(byte[] buffer, MemoryStream stream, Action<byte> onCommand, Action? onCatch = null, Action? onFinally = null)
    { 
        var packet = new Packet();
        unsafe
        {
            fixed (byte* src = buffer) packet._head = *((Head*)src);
        }
        try
        {
            stream.Reset();
            stream.Write(buffer, Head.HeadLength, packet._head._length);
            stream.Seek(0, SeekOrigin.Begin);
            onCommand?.Invoke(packet._head._cmd);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[NET] Exception ->\n{ex.Message}\n{ex.StackTrace}");
            onCatch?.Invoke();
        }
        finally
        {
            onFinally?.Invoke();
        }
    }
}