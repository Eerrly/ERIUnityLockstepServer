using Google.Protobuf;

/// <summary>
/// 服务器消息池管理器
/// </summary>
public class MsgPoolManager : AManager<MsgPoolManager>
{
    /// <summary>
    /// 预缓存消息数量
    /// </summary>
    private const int PreCacheCount = 3;
    /// <summary>
    /// 消息缓存字典
    /// </summary>
    private Dictionary<int, Queue<IMessage>> _cacheMsgDic;

    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize()
    {
        _cacheMsgDic = new Dictionary<int, Queue<IMessage>>();
    }

    /// <summary>
    /// 释放
    /// </summary>
    public override void OnRelease()
    {
        _cacheMsgDic.Clear();
    }

    /// <summary>
    /// 获取对应消息类型的池子缓存数量
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public int GetMsgQueueCount<T>() where T : IMessage
    {
        var hash = typeof(T).GetHashCode();
        if (!_cacheMsgDic.ContainsKey(hash))
            return 0;
        return _cacheMsgDic[hash].Count;
    }

    /// <summary>
    /// 从池子中取出对应消息类型的消息对象
    /// </summary>
    /// <param name="usePreCacheMore">是否预缓存更多</param>
    /// <typeparam name="T">消息类型</typeparam>
    /// <returns>消息对象</returns>
    public T Require<T>(bool usePreCacheMore = false) where T : IMessage, new()
    {
        var hash = typeof(T).GetHashCode();
        if (!_cacheMsgDic.ContainsKey(hash))
            _cacheMsgDic[hash] = new Queue<IMessage>();
        if (_cacheMsgDic[hash].Count <= 0)
        {
            for (var i = 0; i < (usePreCacheMore ? PreCacheCount : 1); i++)
                _cacheMsgDic[hash].Enqueue(new T());
        }
        var msg = (T)_cacheMsgDic[hash].Dequeue();
        return msg;
    }

    /// <summary>
    /// 缓存消息对象
    /// </summary>
    /// <param name="msg">消息体</param>
    /// <typeparam name="T">消息类型</typeparam>
    public void Release<T>(T msg) where T : IMessage
    {
        var hash = typeof(T).GetHashCode();
        _cacheMsgDic[hash].Enqueue(msg);
    }
}