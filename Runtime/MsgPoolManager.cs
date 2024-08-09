using Google.Protobuf;

public class MsgPoolManager : AManager<MsgPoolManager>
{
    private const int PreCacheCount = 3;
    private Dictionary<int, Queue<IMessage>> _cacheMsgDic;

    public override void Initialize()
    {
        _cacheMsgDic = new Dictionary<int, Queue<IMessage>>();
    }

    public override void OnRelease()
    {
        _cacheMsgDic.Clear();
    }

    public int GetMsgQueueCount<T>() where T : IMessage
    {
        var hash = typeof(T).GetHashCode();
        if (!_cacheMsgDic.ContainsKey(hash))
            return 0;
        return _cacheMsgDic[hash].Count;
    }

    public T Require<T>(bool usePreCacheMore = false) where T : IMessage, new()
    {
        var hash = typeof(T).GetHashCode();
        if (!_cacheMsgDic.ContainsKey(hash))
            _cacheMsgDic[hash] = new Queue<IMessage>();
        if (_cacheMsgDic[hash].Count <= 0)
        {
            for (var i = 0; i < (usePreCacheMore ? PreCacheCount : 1); i++)
                _cacheMsgDic[hash].Enqueue(new T());
            Console.WriteLine($"NewMessageQueue Name:{typeof(T).Name} Count:{_cacheMsgDic[hash].Count}");
        }
        var msg = (T)_cacheMsgDic[hash].Dequeue();
        return msg;
    }

    public void Release<T>(T msg) where T : IMessage
    {
        var hash = typeof(T).GetHashCode();
        _cacheMsgDic[hash].Enqueue(msg);
    }
}