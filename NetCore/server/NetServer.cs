public abstract class NetServer
{
    public virtual void Initialize()
    {
    }

    public virtual void OnRelease()
    {
    }

    public virtual void StartServer()
    {
    }

    public void OnData(byte[] buffer, MemoryStream stream, Action<byte> onCommand, Action onCatch)
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
            Logger.Log(LogLevel.Exception, ex.Message);
            onCatch?.Invoke();
        }
    }
}