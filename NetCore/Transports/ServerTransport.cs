/// <summary>
/// 服务器基类
/// </summary>
public abstract class ServerTransport
{
    /// <summary>
    /// 服务器的地址信息
    /// </summary>
    /// <returns></returns>
    public virtual Uri Uri(){ return null; }
    
    /// <summary>
    /// 服务器是否处于存活状态
    /// </summary>
    /// <returns></returns>
    public virtual bool Active() { return false; }
    
    /// <summary>
    /// 开启服务器
    /// </summary>
    public virtual void Start() { }
    
    /// <summary>
    /// 服务器轮询
    /// </summary>
    public virtual void Update() { }
    
    /// <summary>
    /// 发送消息包
    /// </summary>
    /// <param name="packet">消息包</param>
    /// <param name="param">额外参数</param>
    public virtual void Send(Packet packet, object param) { }
    
    /// <summary>
    /// 断开某一个客户端的连接
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    public virtual void Disconnect(int connectionId) { }
    
    /// <summary>
    /// 获取客户端地址
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    /// <returns></returns>
    public virtual string GetClientAddress(int connectionId) { return string.Empty; }
    
    /// <summary>
    /// 关闭服务器
    /// </summary>
    public virtual void Shutdown() { }
    
    /// <summary>
    /// 收到客户端消息的处理回调
    /// </summary>
    /// <param name="buffer">字节数据数组</param>
    /// <param name="stream">数据流</param>
    /// <param name="onCommand">处理数据流的回调</param>
    /// <param name="onCatch">发生异常的回调</param>
    /// <param name="onFinally">发生异常的最终处理回调</param>
    public void OnMessageProcess(byte[] buffer, MemoryStream stream, Action<byte> onCommand, Action? onCatch = null, Action? onFinally = null)
    { 
        var packet = new Packet();
        unsafe
        {
            // 通过指针来获取头数据，以此获取出数据本身字节长度
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
            LogManager.Instance.Log(LogTag.Exception,$"{ex.Message}\n{ex.StackTrace}");
            onCatch?.Invoke();
        }
        finally
        {
            onFinally?.Invoke();
        }
    }
}