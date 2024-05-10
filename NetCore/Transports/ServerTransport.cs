using kcp2k;

/// <summary>
/// 服务器支持类基类
/// </summary>
public abstract class ServerTransport
{
    /// <summary>
    /// 获取URI
    /// </summary>
    /// <returns>URI</returns>
    public virtual Uri Uri(){ return null; }

    /// <summary>
    /// 是否处于激活状态
    /// </summary>
    /// <returns>是否处于激活状态</returns>
    public virtual bool Active() { return false; }

    /// <summary>
    /// 服务器开启
    /// </summary>
    public virtual void Start() { }

    /// <summary>
    /// 服务器轮询
    /// </summary>
    public virtual void Update() { }

    /// <summary>
    /// 发送消息
    /// </summary>
    /// <param name="packet">消息包</param>
    /// <param name="param">附加参数</param>
    public virtual void Send(Packet packet, object param) { }

    /// <summary>
    /// 通过连接ID断开某一个客户端(KCP)
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    public virtual void Disconnect(int connectionId) { }

    /// <summary>
    /// 通过连接ID来获取某一个客户端的地址(KCP)
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    /// <returns></returns>
    public virtual string GetClientAddress(int connectionId) { return string.Empty; }

    /// <summary>
    /// 服务器关闭
    /// </summary>
    public virtual void Shutdown() { }

    /// <summary>
    /// 收到客户端消息后首次处理，将字节数组变为消息包
    /// </summary>
    /// <param name="buffer">收到的字节数组</param>
    /// <param name="stream">数据流</param>
    /// <param name="onCommand">对消息包的二次处理回调</param>
    /// <param name="onCatch">发生异常时的Catch回调</param>
    /// <param name="onFinally">发生异常时的Finally回调</param>
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