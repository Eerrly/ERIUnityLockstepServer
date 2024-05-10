using Google.Protobuf;

/// <summary>
/// 消息池管理器
/// </summary>
public class MsgPoolManager : AManager<MsgPoolManager>
{
    /// <summary>
    /// 消息缓存队列字典
    /// </summary>
    private Dictionary<int, Queue<IMessage>> cacheMsgDic;

    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize()
    {
        cacheMsgDic = new Dictionary<int, Queue<IMessage>>();
    }

    /// <summary>
    /// 释放
    /// </summary>
    public override void OnRelease()
    {
        cacheMsgDic.Clear();
    }

    /// <summary>
    /// 获取对应消息缓存队列的长度
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public int GetMsgQueueCount<T>() where T : IMessage
    {
        var hash = typeof(T).GetHashCode();
        if (!cacheMsgDic.ContainsKey(hash))
            return 0;
        return cacheMsgDic[hash].Count;
    }

    /// <summary>
    /// 取出一个消息对象
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <returns>消息对象</returns>
    public T Require<T>() where T : IMessage, new()
    {
        var msg = default(T);
        var hash = typeof(T).GetHashCode();
        if (!cacheMsgDic.ContainsKey(hash))
            cacheMsgDic[hash] = new Queue<IMessage>();
        if (cacheMsgDic[hash].Count > 0)
            msg = (T)cacheMsgDic[hash].Dequeue();
        msg = msg == null ? new T() : msg;
        return msg;
    }

    /// <summary>
    /// 回收一个消息对象
    /// </summary>
    /// <param name="msg">消息对象</param>
    /// <typeparam name="T">消息类型</typeparam>
    public void Release<T>(T msg) where T : IMessage
    {
        var hash = typeof(T).GetHashCode();
        cacheMsgDic[hash].Enqueue(msg);
    }
}